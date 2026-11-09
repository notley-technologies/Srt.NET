// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading;
using Srt.Net.Async;
using Srt.Net.Interop;

namespace Srt.Net;

/// <summary>A disposable SRT socket for live messages or reliable file transfer.</summary>
/// <remarks>
/// Synchronous operations accept a timeout and a cancellation token; asynchronous operations accept a cancellation
/// token. Both kinds wait through one shared readiness engine instead of polling. Each direction has one queue shared
/// by synchronous and asynchronous operations, so any number of operations may be pending and they complete in the
/// order they started. Configure the socket before use. Disposing the socket immediately fails every pending
/// operation with <see cref="ObjectDisposedException"/>.
/// </remarks>
public sealed unsafe partial class SrtSocket : IDisposable
{
    private readonly SrtSocketHandle handle;

    internal SrtSocketHandle Handle => handle;

    /// <summary>The socket's operation queues once an operation has had to wait, for tests.</summary>
    internal SrtAsyncContext? AsyncContext => Volatile.Read(ref asyncContext);

    private readonly object asyncContextGate = new();
    private Exception? lastAdmissionException;
    private SrtAsyncContext? asyncContext;

    // A BeginConnect attempt awaiting CompleteConnect, recorded only while metrics are enabled.
    private string? pendingConnectRole;
    private long pendingConnectStarted;

    /// <summary>The last exception thrown by an admission handler. Such exceptions reject the connection.</summary>
    public Exception? LastAdmissionException => Volatile.Read(ref lastAdmissionException);

    /// <summary>Creates a socket with the native live transmission defaults.</summary>
    public SrtSocket()
    {
        SrtRuntime.Acquire();
        int socket;

        try
        {
            socket = NativeMethods.srt_create_socket();
            SrtException.ThrowIfError(socket, "create an SRT socket");
        }
        catch
        {
            SrtRuntime.Release();
            throw;
        }

        handle = new(socket);
        Initialize();
    }

    private SrtSocket(int socket)
    {
        try
        {
            SrtRuntime.Acquire();
        }
        catch
        {
            NativeMethods.srt_close(socket);
            throw;
        }

        // An accepted socket inherits the listener's nonblocking modes. Setting them again would also fail when the
        // peer has already gone, although libsrt keeps such a socket readable until its received data is consumed.
        handle = new(socket);
    }

    private void Initialize()
    {
        try
        {
            using SrtSocketLease lease = handle.Borrow();
            byte nonblocking = 0;
            SrtException.ThrowIfError(NativeMethods.srt_setsockflag(lease.Socket, NativeConstants.SRTO_SNDSYN, &nonblocking, 1), "configure sending");
            SrtException.ThrowIfError(NativeMethods.srt_setsockflag(lease.Socket, NativeConstants.SRTO_RCVSYN, &nonblocking, 1), "configure receiving");
        }
        catch
        {
            handle.CloseSocket();
            throw;
        }
    }

    /// <summary>The version of the loaded native libsrt library.</summary>
    public static Version NativeVersion => SrtRuntime.NativeVersion;

    /// <summary>The current socket state. Disposed sockets report Closed.</summary>
    public SrtSocketState State
    {
        get
        {
            if (handle.IsCloseRequested)
            {
                return SrtSocketState.Closed;
            }

            using SrtSocketLease lease = handle.Borrow();

            return NativeMethods.srt_getsockstate(lease.Socket);
        }
    }

    /// <summary>The local endpoint. Bind or connect the socket before reading it.</summary>
    public IPEndPoint LocalEndPoint => GetEndPoint(false);

    /// <summary>The remote endpoint of a connected socket.</summary>
    public IPEndPoint RemoteEndPoint => GetEndPoint(true);

    /// <summary>libsrt's start time for this socket, in microseconds on its monotonic clock (see <see cref="SrtClock"/>).</summary>
    /// <remarks>
    /// libsrt records it when the socket is opened: by Bind, or by Connect on an unbound socket (before the handshake), and
    /// it does not change when that connection is established; use <see cref="State"/> for that. For an accepted socket it
    /// is the time the listener created the socket for the incoming connection, before Accept returns it. It is zero only
    /// before the socket is opened. Source times earlier than this value are rejected.
    /// </remarks>
    public long ConnectionTimeMicroseconds
    {
        get
        {
            using SrtSocketLease lease = handle.Borrow();
            long time = NativeMethods.srt_connection_time(lease.Socket);

            if (time == NativeConstants.SRT_ERROR)
            {
                throw SrtException.LastError("read the SRT connection time");
            }

            return time;
        }
    }

    /// <summary>Binds the socket to a local IPv4 or IPv6 endpoint. Port zero selects an available port.</summary>
    public void Bind(IPEndPoint endpoint)
    {
        Span<byte> address = stackalloc byte[128];
        int addressLength = SocketAddressCodec.Encode(endpoint, address);
        using SrtSocketLease lease = handle.Borrow();

        fixed (byte* pointer = address)
        {
            SrtException.ThrowIfError(NativeMethods.srt_bind(lease.Socket, pointer, addressLength), "bind the SRT socket");
        }
    }

    /// <summary>Installs or clears a connection admission handler before listening. Return true to accept or false to reject.</summary>
    /// <remarks>Runs on a native receive thread. Keep it short; do not block, perform I/O, dispose sockets, or reenter the listener. Exceptions reject the peer and are available in LastAdmissionException.</remarks>
    public void SetConnectionHandler(Func<SrtConnectionRequest, bool>? handler)
    {
        using SrtSocketLease lease = handle.Borrow();
        SrtSocketState state = NativeMethods.srt_getsockstate(lease.Socket);

        if (state != SrtSocketState.Initialized && state != SrtSocketState.Opened)
        {
            throw new InvalidOperationException("Set the connection handler before calling Listen.");
        }

        NativeListenCallback? callback = handler == null ? null : (_, socket, version, peer, streamId) =>
        {
            SrtConnectionRequest? request = null;

            try
            {
                request = new(socket, version, SocketAddressCodec.Decode(peer), Utf8Interop.Read(streamId, 512));

                return handler(request) ? 0 : -1;
            }
            catch (Exception error)
            {
                Volatile.Write(ref lastAdmissionException, error);

                return -1;
            }
            finally
            {
                request?.Expire();
            }
        };
        SrtException.ThrowIfError(NativeMethods.srt_listen_callback(lease.Socket, callback, IntPtr.Zero), "configure SRT connection admission");
        handle.ListenCallback = callback;
    }

    /// <summary>Begins listening for connections on a bound socket.</summary>
    public void Listen(int backlog = 16)
    {
        if (backlog < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(backlog));
        }

        using SrtSocketLease lease = handle.Borrow();

        // Subscribe before listening: libsrt does not report connections queued before a subscription.
        handle.GetAcceptPoll(lease.Socket);
        SrtException.ThrowIfError(NativeMethods.srt_listen(lease.Socket, backlog), "listen for SRT connections");
    }

    /// <summary>Waits for and accepts one connection. The returned socket must be disposed separately.</summary>
    public SrtSocket Accept(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return AcceptCore(true, timeout, cancellationToken)!;
    }

    private SrtSocket? AcceptCore(bool waitForReadiness, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        TimeSpan duration = ValidateTimeout(timeout);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        SrtAsyncContext? context = Volatile.Read(ref asyncContext);
        int observed = 0;
        SrtSocket? accepted;
        Exception? error;

        if ((context == null || context.ReceiveQueue.IsReady(out observed)) && SrtAsyncIo.TryAccept(handle, out accepted, out error))
        {
            return accepted ?? throw error!;
        }

        if (!waitForReadiness)
        {
            return null;
        }

        if (duration == TimeSpan.Zero)
        {
            throw new TimeoutException("The SRT operation timed out.");
        }

        if (context == null)
        {
            // The socket subscribed after the first attempt; try again before waiting for an edge.
            context = GetAsyncContext();

            if (context.ReceiveQueue.IsReady(out observed) && SrtAsyncIo.TryAccept(handle, out accepted, out error))
            {
                return accepted ?? throw error!;
            }
        }

        SyncOperation operation = context.RentSync(sending: false);
        operation.PrepareAccept();
        RunSync(context, operation, observed, duration, cancellationToken);
        accepted = operation.Accepted;
        error = operation.Error;
        operation.Clear();
        context.ReturnSync(operation);

        return accepted ?? throw error!;
    }

    /// <summary>Connects to a peer. A failed or canceled connection attempt disposes this socket.</summary>
    /// <remarks>A null timeout uses libsrt's handshake timeout. A token that is already canceled leaves the socket untouched.</remarks>
    public void Connect(IPEndPoint endpoint, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        Span<byte> address = stackalloc byte[128];
        int addressLength = SocketAddressCodec.Encode(endpoint, address);
        TimeSpan duration = ValidateTimeout(timeout);
        cancellationToken.ThrowIfCancellationRequested();

        // Subscribe before connecting, so the connection's readiness edge cannot be missed.
        SrtAsyncContext context = GetAsyncContext();
        string? metricsRole = GetConnectMetricsRole();
        long started = metricsRole != null ? Stopwatch.GetTimestamp() : 0;

        try
        {
            using (SrtSocketLease lease = handle.Borrow())
            {
                fixed (byte* pointer = address)
                {
                    SrtException.ThrowIfError(NativeMethods.srt_connect(lease.Socket, pointer, addressLength), "connect the SRT socket");
                }
            }

            Exception? error;

            if (!(context.SendQueue.IsReady(out int observed) && SrtAsyncIo.TryCompleteConnect(handle, out error)))
            {
                if (duration == TimeSpan.Zero)
                {
                    throw new TimeoutException("The SRT connection timed out.");
                }

                SyncOperation operation = context.RentSync(sending: true);
                operation.PrepareConnect();
                RunSync(context, operation, observed, duration, cancellationToken);
                error = operation.Error;
                operation.Clear();
                context.ReturnSync(operation);
            }

            ThrowIfFailed(error);
        }
        catch (Exception error)
        {
            Dispose();
            OnConnectFailed(metricsRole, started, error);

            throw;
        }

        OnConnectSucceeded(metricsRole, started);
    }

    /// <summary>Sends a buffer. Live and message modes preserve its boundary; stream mode may send fewer bytes.</summary>
    public int Send(byte[] buffer, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return Send(buffer, 0, buffer?.Length ?? 0, timeout, cancellationToken);
    }

    /// <summary>Sends part of a buffer. Returns the number of bytes accepted by libsrt, not acknowledged by the peer.</summary>
    public int Send(byte[] buffer, int offset, int count, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return Send(SliceBuffer(buffer, offset, count), timeout, cancellationToken);
    }

    /// <summary>Sends a span without copying it into a managed buffer. The memory is used only until this call returns.</summary>
    public int Send(ReadOnlySpan<byte> buffer, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return Transfer(buffer, default, true, true, timeout, cancellationToken);
    }

    /// <summary>Receives one message, or available stream bytes. Use at least 1456 bytes for default live-mode receive buffers.</summary>
    public int Receive(byte[] buffer, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return Receive(buffer, 0, buffer?.Length ?? 0, timeout, cancellationToken);
    }

    /// <summary>Receives data into part of a buffer. Returns the number of received bytes.</summary>
    public int Receive(byte[] buffer, int offset, int count, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return Receive(SliceBuffer(buffer, offset, count), timeout, cancellationToken);
    }

    /// <summary>Receives directly into a span. Use at least 1456 bytes for default live-mode receive buffers.</summary>
    public int Receive(Span<byte> buffer, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return Transfer(default, buffer, false, true, timeout, cancellationToken);
    }

    /// <summary>Attempts one accept without waiting. Returns false when no connection is queued.</summary>
    public bool TryAccept([NotNullWhen(true)] out SrtSocket? connection)
    {
        connection = AcceptCore(false, null, default);

        return connection != null;
    }

    /// <summary>Attempts one send without waiting. Other native errors still throw.</summary>
    public bool TrySend(byte[] buffer, out int bytesSent)
    {
        return TrySend(buffer, 0, buffer?.Length ?? 0, out bytesSent);
    }

    /// <summary>Attempts one partial-buffer send without waiting. Other native errors still throw.</summary>
    public bool TrySend(byte[] buffer, int offset, int count, out int bytesSent)
    {
        return TrySend(SliceBuffer(buffer, offset, count), out bytesSent);
    }

    /// <summary>Attempts one span send without waiting. Returns false when the send buffer is full.</summary>
    public bool TrySend(ReadOnlySpan<byte> buffer, out int bytesSent)
    {
        int result = Transfer(buffer, default, true, false, null, default);
        bytesSent = Math.Max(0, result);

        return result != NativeConstants.SRT_ERROR;
    }

    /// <summary>Attempts one receive without waiting. Returns false when no data is ready.</summary>
    public bool TryReceive(byte[] buffer, out int bytesReceived)
    {
        return TryReceive(buffer, 0, buffer?.Length ?? 0, out bytesReceived);
    }

    /// <summary>Attempts one partial-buffer receive without waiting. Returns false when no data is ready.</summary>
    public bool TryReceive(byte[] buffer, int offset, int count, out int bytesReceived)
    {
        return TryReceive(SliceBuffer(buffer, offset, count), out bytesReceived);
    }

    /// <summary>Attempts one span receive without waiting. Returns false when no data is ready.</summary>
    public bool TryReceive(Span<byte> buffer, out int bytesReceived)
    {
        int result = Transfer(default, buffer, false, false, null, default);
        bytesReceived = Math.Max(0, result);

        return result != NativeConstants.SRT_ERROR;
    }

    /// <summary>Starts a connection without waiting. Register with a poller before calling this method.</summary>
    /// <remarks>Wait for Connect or Error readiness, then call CompleteConnect. Dispose to abandon the attempt.</remarks>
    public void BeginConnect(IPEndPoint endpoint)
    {
        Span<byte> address = stackalloc byte[128];
        int addressLength = SocketAddressCodec.Encode(endpoint, address);
        using SrtSocketLease lease = handle.Borrow();
        string? metricsRole = GetConnectMetricsRole();
        long started = metricsRole != null ? Stopwatch.GetTimestamp() : 0;

        try
        {
            fixed (byte* pointer = address)
            {
                SrtException.ThrowIfError(NativeMethods.srt_connect(lease.Socket, pointer, addressLength), "start an SRT connection");
            }
        }
        catch (Exception error)
        {
            Dispose();
            OnConnectFailed(metricsRole, started, error);

            throw;
        }

        pendingConnectStarted = started;
        Volatile.Write(ref pendingConnectRole, metricsRole);
    }

    /// <summary>Checks a connection after Connect or Error readiness. A failed attempt closes the socket.</summary>
    /// <remarks>Calling before the connection is ready also closes the unfinished attempt and throws.</remarks>
    public void CompleteConnect()
    {
        using SrtSocketLease lease = handle.Borrow();

        // Taking the pending role records each attempt once, however often this method is called.
        if (NativeMethods.srt_getsockstate(lease.Socket) == SrtSocketState.Connected)
        {
            OnConnectSucceeded(Interlocked.Exchange(ref pendingConnectRole, null), pendingConnectStarted);

            return;
        }

        SrtException error = SrtException.ConnectionFailed("complete the SRT connection", NativeMethods.srt_getrejectreason(lease.Socket));
        string? metricsRole = Interlocked.Exchange(ref pendingConnectRole, null);
        Dispose();
        OnConnectFailed(metricsRole, pendingConnectStarted, error);

        throw error;
    }

    /// <summary>Reads all native transport statistics. Clear resets interval counters, not lifetime totals.</summary>
    /// <remarks>The structure ABI is verified for libsrt 1.5.0 through 1.5.7. Newer native versions require an ABI review.</remarks>
    public SrtStatistics GetStatistics(bool clear = false, bool instantaneous = false)
    {
        Version version = NativeVersion;

        if (version.Major != 1 || version.Minor != 5 || version.Build > 7)
        {
            throw new NotSupportedException("Statistics require the verified libsrt 1.5.0–1.5.7 structure layout.");
        }

        using SrtSocketLease lease = handle.Borrow();
        SrtException.ThrowIfError(NativeMethods.srt_bistats(lease.Socket, out NativeStatistics statistics, clear ? 1 : 0, instantaneous ? 1 : 0), "read SRT statistics");

        return new(in statistics);
    }

    /// <summary>Reads the blocks and bytes queued for sending. An empty buffer is not an application acknowledgment.</summary>
    public SrtSendBuffer GetSendBuffer()
    {
        using SrtSocketLease lease = handle.Borrow();

        if (NativeMethods.srt_getsndbuffer(lease.Socket, out UIntPtr blocks, out UIntPtr bytes) == NativeConstants.SRT_ERROR)
        {
            // libsrt does not set its last error for this call, so the reason comes from the socket state.
            throw NativeMethods.srt_getsockstate(lease.Socket) is SrtSocketState.Closing or SrtSocketState.Closed or SrtSocketState.Nonexistent
                ? new SrtException("Unable to read the SRT send buffer: libsrt has closed the socket (SRT 5004).", SrtErrorCode.InvalidSocket)
                : new SrtException("Unable to read the SRT send buffer: the socket is not connected (SRT 2002).", SrtErrorCode.NotConnected);
        }

        return new(blocks.ToUInt64(), bytes.ToUInt64());
    }

    /// <summary>Sends a message with optional TTL, ordering, and source time controls.</summary>
    public SrtMessageResult SendMessage(byte[] buffer, SrtMessageOptions options = default, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return SendMessage(buffer, 0, buffer?.Length ?? 0, options, timeout, cancellationToken);
    }

    /// <summary>Sends part of a buffer as a message and returns its native metadata.</summary>
    public SrtMessageResult SendMessage(byte[] buffer, int offset, int count, SrtMessageOptions options = default, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return SendMessage(SliceBuffer(buffer, offset, count), options, timeout, cancellationToken);
    }

    /// <summary>Sends a span as a message and returns its native metadata.</summary>
    public SrtMessageResult SendMessage(ReadOnlySpan<byte> buffer, SrtMessageOptions options = default, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        TransferMessage(buffer, default, true, true, in options, timeout, cancellationToken, out SrtMessageResult result);

        return result;
    }

    /// <summary>Receives one message, including its source time, sequence number, and message number.</summary>
    public SrtMessageResult ReceiveMessage(byte[] buffer, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return ReceiveMessage(buffer, 0, buffer?.Length ?? 0, timeout, cancellationToken);
    }

    /// <summary>Receives a message into part of a buffer and returns its native metadata.</summary>
    public SrtMessageResult ReceiveMessage(byte[] buffer, int offset, int count, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return ReceiveMessage(SliceBuffer(buffer, offset, count), timeout, cancellationToken);
    }

    /// <summary>Receives a message directly into a span and returns its native metadata.</summary>
    public SrtMessageResult ReceiveMessage(Span<byte> buffer, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        TransferMessage(default, buffer, false, true, default, timeout, cancellationToken, out SrtMessageResult result);

        return result;
    }

    /// <summary>Attempts one message send without waiting. Returns false when the send buffer is full.</summary>
    public bool TrySendMessage(byte[] buffer, out SrtMessageResult result, SrtMessageOptions options = default)
    {
        return TrySendMessage(buffer, 0, buffer?.Length ?? 0, out result, options);
    }

    /// <summary>Attempts one partial-buffer message send without waiting.</summary>
    public bool TrySendMessage(byte[] buffer, int offset, int count, out SrtMessageResult result, SrtMessageOptions options = default)
    {
        return TrySendMessage(SliceBuffer(buffer, offset, count), out result, options);
    }

    /// <summary>Attempts one span message send without waiting. The result is default when no data was sent.</summary>
    public bool TrySendMessage(ReadOnlySpan<byte> buffer, out SrtMessageResult result, SrtMessageOptions options = default)
    {
        return TransferMessage(buffer, default, true, false, in options, null, default, out result);
    }

    /// <summary>Attempts one message receive without waiting. Returns false when no data is ready.</summary>
    public bool TryReceiveMessage(byte[] buffer, out SrtMessageResult result)
    {
        return TryReceiveMessage(buffer, 0, buffer?.Length ?? 0, out result);
    }

    /// <summary>Attempts one partial-buffer message receive without waiting.</summary>
    public bool TryReceiveMessage(byte[] buffer, int offset, int count, out SrtMessageResult result)
    {
        return TryReceiveMessage(SliceBuffer(buffer, offset, count), out result);
    }

    /// <summary>Attempts one span message receive without waiting. The result is default when no data is ready.</summary>
    public bool TryReceiveMessage(Span<byte> buffer, out SrtMessageResult result)
    {
        return TransferMessage(default, buffer, false, false, default, null, default, out result);
    }

    private bool TransferMessage(ReadOnlySpan<byte> source, Span<byte> destination, bool sending, bool waitForReadiness,
        in SrtMessageOptions options, TimeSpan? timeout, CancellationToken cancellationToken, out SrtMessageResult message)
    {
        int count = sending ? source.Length : destination.Length;
        ValidateNotEmpty(count);
        NativeMessageControl control = NativeMessageControl.Default;

        if (sending)
        {
            ApplyOptions(ref control, in options);
        }

        if (!TransferCore(source, destination, sending, true, ref control, waitForReadiness, timeout, cancellationToken, out int bytes))
        {
            message = default;

            return false;
        }

        message = new(bytes, in control);

        return true;
    }

    private static void ApplyOptions(ref NativeMessageControl control, in SrtMessageOptions options)
    {
        if (options.TimeToLive is TimeSpan timeToLive)
        {
            if (timeToLive < TimeSpan.Zero || timeToLive.TotalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "TimeToLive must be between zero and int.MaxValue milliseconds.");
            }

            control.TimeToLive = (int)Math.Ceiling(timeToLive.TotalMilliseconds);
        }

        if (options.SourceTimeMicroseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SourceTimeMicroseconds cannot be negative.");
        }

        control.InOrder = options.InOrder ? 1 : 0;
        control.SourceTime = options.SourceTimeMicroseconds;
    }

    private static SrtErrorCode WouldBlockError(bool sending)
    {
        return sending ? SrtErrorCode.SendWouldBlock : SrtErrorCode.ReceiveWouldBlock;
    }

    private int Transfer(ReadOnlySpan<byte> source, Span<byte> destination, bool sending, bool waitForReadiness,
        TimeSpan? timeout, CancellationToken cancellation)
    {
        int count = sending ? source.Length : destination.Length;
        ValidateNotEmpty(count);
        NativeMessageControl control = default;

        return TransferCore(source, destination, sending, false, ref control, waitForReadiness, timeout, cancellation, out int bytes)
            ? bytes
            : NativeConstants.SRT_ERROR;
    }

    /// <summary>Sends or receives once, waiting through the socket's operation queue when the native call would block.</summary>
    /// <returns>False only when <paramref name="waitForReadiness"/> is false and the operation would block.</returns>
    private bool TransferCore(ReadOnlySpan<byte> source, Span<byte> destination, bool sending, bool message, ref NativeMessageControl control,
        bool waitForReadiness, TimeSpan? timeout, CancellationToken cancellationToken, out int bytes)
    {
        TimeSpan duration = ValidateTimeout(timeout);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        NativeMessageControl template = control;
        SrtAsyncContext? context = Volatile.Read(ref asyncContext);
        int observed = 0;
        bool retrySoon = false;
        Exception? error;
        bytes = 0;

        // Queued operations in this direction run first, so only an idle queue allows an inline attempt.
        if (context == null || context.QueueFor(sending).IsReady(out observed))
        {
            if (SrtAsyncIo.TryTransfer(handle, source, destination, sending, message, ref control, out bytes, out error, out retrySoon))
            {
                ThrowIfFailed(error);

                return true;
            }

            control = template;
        }

        if (!waitForReadiness)
        {
            return false;
        }

        if (duration == TimeSpan.Zero)
        {
            throw new TimeoutException("The SRT operation timed out.");
        }

        if (context == null)
        {
            // The socket subscribed after the first attempt; try again before waiting for an edge.
            context = GetAsyncContext();

            if (context.QueueFor(sending).IsReady(out observed)
                && SrtAsyncIo.TryTransfer(handle, source, destination, sending, message, ref control, out bytes, out error, out retrySoon))
            {
                ThrowIfFailed(error);

                return true;
            }

            control = template;
        }

        if (retrySoon)
        {
            context.RequestSendRetry();
        }

        SyncOperation operation = context.RentSync(sending);

        // The operation runs only on this thread, inside this block, so the pinned pointers stay valid.
        fixed (byte* sourcePointer = source)
        fixed (byte* destinationPointer = destination)
        {
            operation.PrepareTransfer(sending ? sourcePointer : destinationPointer, sending ? source.Length : destination.Length, message, in template);
            RunSync(context, operation, observed, duration, cancellationToken);
        }

        bytes = operation.Bytes;
        control = operation.Control;
        error = operation.Error;
        operation.Clear();
        context.ReturnSync(operation);
        ThrowIfFailed(error);

        return true;
    }

    private static TimeSpan ValidateTimeout(TimeSpan? timeout)
    {
        TimeSpan duration = timeout ?? Timeout.InfiniteTimeSpan;

        if (duration < TimeSpan.Zero && duration != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        return duration;
    }

    private static void ThrowIfFailed(Exception? error)
    {
        if (error != null)
        {
            throw error;
        }
    }

    /// <summary>Waits for a queued synchronous operation. Returns when it completed; throws when it timed out, was canceled, or was aborted.</summary>
    private static void RunSync(SrtAsyncContext context, SyncOperation operation, int observed, TimeSpan timeout, CancellationToken cancellationToken)
    {
        SrtOperationResult result;

        try
        {
            result = context.ExecuteSync(operation, observed, timeout, cancellationToken);
        }
        catch
        {
            // A timed-out or canceled operation is unlinked from its queue, but a hand-off made just before can still
            // signal its event, so it is never reused (as in System.Net.Sockets); the next call allocates a new one.
            operation.Clear();

            throw;
        }

        if (result == SrtOperationResult.Aborted)
        {
            Exception error = operation.CreateEndedException();
            operation.Clear();

            throw error;
        }
    }

    private static Span<byte> SliceBuffer(byte[] buffer, int offset, int count)
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (offset < 0 || offset > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (count < 1 || count > buffer.Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        return buffer.AsSpan(offset, count);
    }

    /// <summary>Rejects an empty buffer. The buffer parameter of every public overload that reaches this is named buffer.</summary>
    private static void ValidateNotEmpty(int length)
    {
        if (length < 1)
        {
            throw new ArgumentOutOfRangeException("buffer", "The buffer must contain at least one byte.");
        }
    }

    /// <summary>Sets a typed socket option. libsrt validates ranges and when an option may be changed.</summary>
    public void SetOption<T>(SrtOption<T> option, T value)
    {
        using SrtSocketLease lease = handle.Borrow();
        SocketOptionCodec.Set(lease.Socket, option, value);
    }

    /// <summary>Reads a typed socket option.</summary>
    public T GetOption<T>(SrtOption<T> option)
    {
        using SrtSocketLease lease = handle.Borrow();

        return SocketOptionCodec.Get<T>(lease.Socket, option);
    }

    private IPEndPoint GetEndPoint(bool remote)
    {
        using SrtSocketLease lease = handle.Borrow();
        Span<byte> address = stackalloc byte[128];
        int length = address.Length;

        fixed (byte* pointer = address)
        {
            int result = remote
                ? NativeMethods.srt_getpeername(lease.Socket, pointer, ref length)
                : NativeMethods.srt_getsockname(lease.Socket, pointer, ref length);
            SrtException.ThrowIfError(result, "read the SRT endpoint");
        }

        if (length < 2 || length > address.Length)
        {
            throw new InvalidOperationException("libsrt returned an invalid endpoint length.");
        }

        return SocketAddressCodec.Decode(address.Slice(0, length));
    }

    /// <summary>Closes the socket after in-progress native calls have returned. Safe to call more than once.</summary>
    /// <remarks>
    /// Pending operations fail with <see cref="ObjectDisposedException"/>. With <see cref="SrtOptions.Linger"/> enabled
    /// (the file-profile default), queued data keeps draining in the background for up to the linger time. Unlike a TCP
    /// close, process exit discards data that is still queued while the peer still sees an orderly close, and delivery
    /// is never confirmed, so use an application acknowledgment when it matters.
    /// </remarks>
    public void Dispose()
    {
        // Reject new native calls first, then end queued asynchronous operations, then close.
        handle.RequestClose();
        Volatile.Read(ref asyncContext)?.Abort();
        handle.Dispose();
    }

    internal static SrtSocket FromAccepted(int socket)
    {
        SrtSocket accepted = new(socket);

        if (SrtMetrics.IsEnabled)
        {
            try
            {
                accepted.handle.MarkConnected(SrtMetrics.ListenerRole);
            }
            catch
            {
                accepted.Dispose();

                throw;
            }
        }

        return accepted;
    }

    /// <summary>Returns the role recorded for a connection attempt, or null when metrics are disabled.</summary>
    private string? GetConnectMetricsRole()
    {
        if (!SrtMetrics.IsEnabled)
        {
            return null;
        }

        try
        {
            return GetOption(SrtOptions.Rendezvous) ? SrtMetrics.RendezvousRole : SrtMetrics.CallerRole;
        }
        catch (SrtException)
        {
            return SrtMetrics.CallerRole;
        }
    }

    private void OnConnectSucceeded(string? metricsRole, long started)
    {
        if (metricsRole != null)
        {
            SrtMetrics.ConnectCompleted(metricsRole, started, null);
            handle.MarkConnected(metricsRole);
        }
    }

    private static void OnConnectFailed(string? metricsRole, long started, Exception error)
    {
        if (metricsRole != null)
        {
            SrtMetrics.ConnectCompleted(metricsRole, started, error);
        }
    }
}
