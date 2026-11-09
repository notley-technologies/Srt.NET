// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Srt.Net.Async;
using Srt.Net.Interop;

namespace Srt.Net;

/// <content>Asynchronous operations.</content>
public sealed partial class SrtSocket
{
    /// <summary>Connects to a peer asynchronously. A failed or canceled attempt disposes this socket.</summary>
    /// <remarks>
    /// libsrt's handshake timeout (<see cref="SrtOptions.ConnectTimeout"/>) bounds the attempt. A token that is
    /// already canceled leaves the socket untouched.
    /// </remarks>
    public ValueTask ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken = default)
    {
        Span<byte> address = stackalloc byte[128];
        int addressLength = SocketAddressCodec.Encode(endpoint, address);

        ThrowIfDisposed();

        if (cancellationToken.IsCancellationRequested)
        {
            return new(Task.FromCanceled(cancellationToken));
        }

        SrtAsyncContext context = GetAsyncContext();
        string? metricsRole = GetConnectMetricsRole();
        long started = metricsRole != null ? Stopwatch.GetTimestamp() : 0;
        ValueTask pending;

        try
        {
            using (SrtSocketLease lease = handle.Borrow())
            {
                unsafe
                {
                    fixed (byte* pointer = address)
                    {
                        SrtException.ThrowIfError(NativeMethods.srt_connect(lease.Socket, pointer, addressLength), "connect the SRT socket");
                    }
                }
            }

            pending = context.ConnectAsync(cancellationToken);
        }
        catch (Exception error)
        {
            Dispose();
            OnConnectFailed(metricsRole, started, error);

            return new(Task.FromException(error));
        }

        if (!pending.IsCompletedSuccessfully)
        {
            return AwaitConnectAsync(pending, metricsRole, started);
        }

        OnConnectSucceeded(metricsRole, started);

        return default;
    }

    /// <summary>Accepts one connection asynchronously. The returned socket must be disposed separately.</summary>
    public ValueTask<SrtSocket> AcceptAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (cancellationToken.IsCancellationRequested)
        {
            return new(Task.FromCanceled<SrtSocket>(cancellationToken));
        }

        return GetAsyncContext().AcceptAsync(cancellationToken);
    }

    /// <summary>Sends a buffer asynchronously. Live and message modes preserve its boundary; stream mode may send fewer bytes.</summary>
    /// <remarks>The memory must remain valid and unchanged until the operation completes.</remarks>
    public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateNotEmpty(buffer.Length);

        if (cancellationToken.IsCancellationRequested)
        {
            return new(Task.FromCanceled<int>(cancellationToken));
        }

        return GetAsyncContext().SendAsync(buffer, cancellationToken);
    }

    /// <summary>Receives one message, or available stream bytes, asynchronously.</summary>
    /// <remarks>Use at least 1456 bytes for default live-mode receive buffers. The memory must remain valid until completion.</remarks>
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateNotEmpty(buffer.Length);

        if (cancellationToken.IsCancellationRequested)
        {
            return new(Task.FromCanceled<int>(cancellationToken));
        }

        return GetAsyncContext().ReceiveAsync(buffer, cancellationToken);
    }

    /// <summary>Sends a message asynchronously with optional TTL, ordering, and source time controls.</summary>
    /// <remarks>The memory must remain valid and unchanged until the operation completes.</remarks>
    public ValueTask<SrtMessageResult> SendMessageAsync(ReadOnlyMemory<byte> buffer, SrtMessageOptions options = default,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateNotEmpty(buffer.Length);
        NativeMessageControl control = NativeMessageControl.Default;
        ApplyOptions(ref control, in options);

        if (cancellationToken.IsCancellationRequested)
        {
            return new(Task.FromCanceled<SrtMessageResult>(cancellationToken));
        }

        return GetAsyncContext().SendMessageAsync(buffer, in control, cancellationToken);
    }

    /// <summary>Receives one message asynchronously, including its source time, sequence number, and message number.</summary>
    /// <remarks>The memory must remain valid until the operation completes.</remarks>
    public ValueTask<SrtMessageResult> ReceiveMessageAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateNotEmpty(buffer.Length);

        if (cancellationToken.IsCancellationRequested)
        {
            return new(Task.FromCanceled<SrtMessageResult>(cancellationToken));
        }

        return GetAsyncContext().ReceiveMessageAsync(buffer, cancellationToken);
    }

    private void ThrowIfDisposed()
    {
        if (handle.IsCloseRequested)
        {
            throw new ObjectDisposedException(nameof(SrtSocket));
        }
    }

    /// <summary>Returns the socket's operation queues, subscribing it with the engine on first use.</summary>
    internal SrtAsyncContext GetAsyncContext()
    {
        return Volatile.Read(ref asyncContext) ?? CreateAsyncContext();
    }

    private SrtAsyncContext CreateAsyncContext()
    {
        lock (asyncContextGate)
        {
            SrtAsyncContext? existing = Volatile.Read(ref asyncContext);

            if (existing != null)
            {
                return existing;
            }

            using SrtSocketLease lease = handle.Borrow();
            SrtAsyncContext created = new(handle, lease.Socket);

            // Subscribe before any operation can be attempted, so no readiness edge is missed.
            created.Register();

            // Full fences on both sides: either Dispose sees the context, or this sees the close request.
            Interlocked.Exchange(ref asyncContext, created);

            if (handle.IsCloseRequested)
            {
                created.Abort();
            }

            return created;
        }
    }

    private async ValueTask AwaitConnectAsync(ValueTask pending, string? metricsRole, long started)
    {
        try
        {
            await pending.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Dispose();
            OnConnectFailed(metricsRole, started, error);

            throw;
        }

        OnConnectSucceeded(metricsRole, started);
    }
}
