// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Srt.Net;

/// <summary>A <see cref="Stream"/> over a connected SRT socket, for reliable file transfer in stream mode.</summary>
/// <remarks>
/// Configure the socket with <see cref="SrtTransmissionType.File"/> and leave <see cref="SrtOptions.MessageApi"/>
/// disabled. Reads return zero at the end of the stream, after the peer closed the connection in an orderly way. A
/// connection that broke instead (the peer process died, or the network failed until the peer idle timeout) throws
/// <see cref="SrtException"/>, an <see cref="IOException"/>. libsrt reports both cases as an invalid socket once it has
/// released the socket, which it can do before the end of the stream is read; that is treated as the end of the stream
/// unless a broken connection was already reported. SRT signals an orderly close with one control packet, and a sender
/// whose process exits while data is still queued discards that data but still closes in an orderly way, so
/// applications that must detect truncation should frame their data, for example with a length prefix or digest, and
/// acknowledge it.
/// <para>Writes complete once libsrt has queued the data, not when the peer has received it. In live mode each write must
/// fit one message (1316 bytes by default) and each read needs a buffer of at least 1456 bytes, or the call fails; in
/// message mode each write is one message and a read truncates a message that does not fit. Use the socket's message
/// methods in both modes instead. Like <see cref="System.Net.Sockets.NetworkStream"/>, one reader and one writer may run
/// concurrently.</para>
/// </remarks>
public sealed class SrtStream : Stream
{
    private readonly bool ownsSocket;
    private int readTimeout = Timeout.Infinite;
    private int writeTimeout = Timeout.Infinite;
    private int disposed;
    private int connectionBroken;

    /// <summary>Creates a stream over a connected socket.</summary>
    /// <param name="socket">The connected socket.</param>
    /// <param name="ownsSocket">Whether disposing the stream also disposes the socket.</param>
    public SrtStream(SrtSocket socket, bool ownsSocket = false)
    {
        Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        this.ownsSocket = ownsSocket;
    }

    /// <summary>The underlying socket.</summary>
    public SrtSocket Socket
    {
        get;
    }

    /// <inheritdoc />
    public override bool CanRead => Volatile.Read(ref disposed) == 0;

    /// <inheritdoc />
    public override bool CanWrite => Volatile.Read(ref disposed) == 0;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanTimeout => true;

    /// <summary>Timeout in milliseconds for synchronous reads, or <see cref="Timeout.Infinite"/>.</summary>
    public override int ReadTimeout
    {
        get => readTimeout;
        set => readTimeout = ValidateTimeout(value);
    }

    /// <summary>Timeout in milliseconds for synchronous writes, or <see cref="Timeout.Infinite"/>.</summary>
    public override int WriteTimeout
    {
        get => writeTimeout;
        set => writeTimeout = ValidateTimeout(value);
    }

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateArguments(buffer, offset, count);

        return ReadCore(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateArguments(buffer, offset, count);
        WriteCore(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateArguments(buffer, offset, count);

        return ReadCoreAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateArguments(buffer, offset, count);

        return WriteCoreAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

#if NET
    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        return ReadCore(buffer);
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        WriteCore(buffer);
    }

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return ReadCoreAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return WriteCoreAsync(buffer, cancellationToken);
    }
#endif

    /// <summary>Does nothing: libsrt sends queued data without an explicit flush.</summary>
    public override void Flush()
    {
    }

    /// <summary>Completes immediately: libsrt sends queued data without an explicit flush.</summary>
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0 && disposing && ownsSocket)
        {
            Socket.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Decides whether a receive failure is the end of the stream, remembering a broken connection.</summary>
    private bool IsEndOfStream(SrtException error)
    {
        switch (error.SrtErrorCode)
        {
            case SrtErrorCode.ConnectionLost:
            case SrtErrorCode.NotConnected:
                Volatile.Write(ref connectionBroken, 1);

                return false;

            case SrtErrorCode.InvalidSocket:
                // libsrt releases a closed connection's socket once its received data is drained, after an orderly close
                // as well as after a break, so only a break that was already reported keeps this an error.
                return Volatile.Read(ref connectionBroken) == 0;

            default:
                return false;
        }
    }

    private static TimeSpan? ToTimeout(int milliseconds)
    {
        return milliseconds == Timeout.Infinite ? null : TimeSpan.FromMilliseconds(milliseconds);
    }

    private static int ValidateTimeout(int value)
    {
        if (value <= 0 && value != Timeout.Infinite)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Use a positive timeout or Timeout.Infinite.");
        }

        return value;
    }

    private static void ValidateArguments(byte[] buffer, int offset, int count)
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (offset < 0 || offset > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (count < 0 || count > buffer.Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
    }

    private int ReadCore(Span<byte> buffer)
    {
        ThrowIfDisposed();

        if (buffer.IsEmpty)
        {
            return 0;
        }

        try
        {
            return Socket.Receive(buffer, ToTimeout(readTimeout));
        }
        catch (SrtException error) when (IsEndOfStream(error))
        {
            return 0;
        }
        catch (TimeoutException error)
        {
            throw new IOException("The SRT read timed out.", error);
        }
    }

    private void WriteCore(ReadOnlySpan<byte> buffer)
    {
        ThrowIfDisposed();

        try
        {
            while (!buffer.IsEmpty)
            {
                buffer = buffer.Slice(Socket.Send(buffer, ToTimeout(writeTimeout)));
            }
        }
        catch (TimeoutException error)
        {
            throw new IOException("The SRT write timed out.", error);
        }
    }

    private async ValueTask<int> ReadCoreAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (buffer.IsEmpty)
        {
            return 0;
        }

        try
        {
            return await Socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (SrtException error) when (IsEndOfStream(error))
        {
            return 0;
        }
    }

    private async ValueTask WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        while (!buffer.IsEmpty)
        {
            buffer = buffer.Slice(await Socket.SendAsync(buffer, cancellationToken).ConfigureAwait(false));
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(SrtStream));
        }
    }
}
