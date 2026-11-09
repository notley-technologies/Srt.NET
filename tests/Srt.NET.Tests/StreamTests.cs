// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Srt.Net;
using Xunit;

namespace Srt.Net.Tests;

public sealed class StreamTests : IClassFixture<RuntimeFixture>
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task CopiesALargePayloadAndEndsAtPeerClose()
    {
        (SrtSocket sender, SrtSocket receiver) = ConnectFileStream();
        byte[] payload = [.. Enumerable.Range(0, 2 * 1024 * 1024).Select(value => (byte)(value * 31))];
        using CancellationTokenSource timeout = new(Deadline);

        using SrtStream input = new(receiver, ownsSocket: true);
        using MemoryStream copy = new();
        Task receiving = input.CopyToAsync(copy, 81920, timeout.Token);

        using (SrtStream output = new(sender, ownsSocket: true))
        {
            await output.WriteAsync(payload, 0, payload.Length, timeout.Token);

            // Closing does not wait for delivery; wait until the peer has received everything first.
            while (copy.Length < payload.Length && !timeout.IsCancellationRequested)
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        await receiving;
        Assert.Equal(payload, copy.ToArray());
        Assert.Equal(0, await input.ReadAsync(new byte[16], 0, 16, timeout.Token));
    }

    [Fact]
    public void SynchronousReadsAndWritesHonorTimeouts()
    {
        (SrtSocket sender, SrtSocket receiver) = ConnectFileStream();
        using SrtStream output = new(sender, ownsSocket: true);
        using SrtStream input = new(receiver, ownsSocket: true)
        {
            ReadTimeout = 100
        };

        output.Write([1, 2, 3], 0, 3);
        byte[] buffer = new byte[16];
        int count = 0;

        while (count < 3)
        {
            count += input.Read(buffer, count, buffer.Length - count);
        }

        Assert.Equal(new byte[] { 1, 2, 3 }, buffer.Take(3));
        IOException timedOut = Assert.Throws<IOException>(() => input.Read(buffer, 0, buffer.Length));
        Assert.IsType<TimeoutException>(timedOut.InnerException);
        Assert.Throws<ArgumentOutOfRangeException>(() => input.WriteTimeout = 0);
    }

    [Fact]
    public void ReportsStreamCapabilitiesAndOwnership()
    {
        (SrtSocket sender, SrtSocket receiver) = ConnectFileStream();
        using SrtSocket keptReceiver = receiver;
        SrtStream owning = new(sender, ownsSocket: true);
        SrtStream borrowing = new(keptReceiver);

        Assert.True(owning.CanRead && owning.CanWrite && owning.CanTimeout);
        Assert.False(owning.CanSeek);
        Assert.Throws<NotSupportedException>(() => owning.Length);
        Assert.Throws<NotSupportedException>(() => owning.Seek(0, SeekOrigin.Begin));

        borrowing.Dispose();
        Assert.False(borrowing.CanRead);
        Assert.Equal(SrtSocketState.Connected, keptReceiver.State);

        owning.Dispose();
        Assert.Equal(SrtSocketState.Closed, sender.State);
        Assert.Throws<ObjectDisposedException>(() => owning.Write([1], 0, 1));
    }

    [Fact]
    public async Task DisposingTheWriterRightAfterWritingStillDeliversEverything()
    {
        (SrtSocket sender, SrtSocket receiver) = ConnectFileStream();
        byte[] payload = [.. Enumerable.Range(0, 2 * 1024 * 1024).Select(value => (byte)(value * 7))];
        using CancellationTokenSource timeout = new(Deadline);

        using SrtStream input = new(receiver, ownsSocket: true);
        using MemoryStream copy = new();
        Task receiving = input.CopyToAsync(copy, 81920, timeout.Token);

        // Writes complete once libsrt has queued the data; linger (on by default in file mode) lets it drain after Dispose.
        using (SrtStream output = new(sender, ownsSocket: true))
        {
            await output.WriteAsync(payload, 0, payload.Length, timeout.Token);
        }

        await receiving;
        Assert.Equal(payload, copy.ToArray());
    }

    [Fact]
    public async Task ABrokenConnectionFailsReadsInsteadOfEndingTheStream()
    {
        // libsrt releases a broken socket on its next cleanup pass once its received data has been read, and after that a
        // break and an orderly close both report InvalidSocket. A read that runs after the release therefore cannot tell
        // them apart (docs/advanced-usage.md). That race depends on scheduling, so try a few times to observe the break.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (await BreakIsObservedAndStaysAnErrorAsync())
            {
                return;
            }
        }

        Assert.Skip("libsrt released the broken socket before the read in every attempt, so the break could not be observed.");
    }

    private static async Task<bool> BreakIsObservedAndStaysAnErrorAsync()
    {
        using SrtSocket listener = new();
        listener.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
        listener.SetOption(SrtOptions.PeerIdleTimeout, 1000);
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        using UdpRelay relay = new(listener.LocalEndPoint);
        using SrtSocket sender = new();
        sender.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
        sender.SetOption(SrtOptions.PeerIdleTimeout, 1000);
        sender.Connect(relay.EndPoint, TimeSpan.FromSeconds(5));
        using SrtStream input = new(listener.Accept(TimeSpan.FromSeconds(5)), ownsSocket: true);
        using CancellationTokenSource timeout = new(Deadline);

        sender.Send([1, 2, 3, 4, 5, 6], TimeSpan.FromSeconds(5));
        byte[] buffer = new byte[16];
        Assert.Equal(3, await input.ReadAsync(buffer, 0, 3, timeout.Token));

        // No orderly close arrives: the connection breaks at the peer idle timeout. libsrt keeps a broken socket while
        // received data is unread, so data received before the break is still delivered.
        relay.DropEverything();
        Assert.True(SpinWait.SpinUntil(() => input.Socket.State == SrtSocketState.Broken, Deadline));
        Assert.Equal(3, await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token));
        Assert.Equal(new byte[] { 4, 5, 6 }, buffer.Take(3));

        try
        {
            int read = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token);

            // Ending the stream is only acceptable when libsrt had already released the socket.
            Assert.Equal(0, read);
            Assert.True(IsReleased(input.Socket), "A broken connection read as the end of the stream while libsrt still reported it.");

            return false;
        }
        catch (IOException)
        {
        }

        // The stream has reported the break, so reads must keep failing once libsrt releases the socket.
        Assert.True(SpinWait.SpinUntil(() => IsReleased(input.Socket), Deadline));
        await Assert.ThrowsAnyAsync<IOException>(() => input.ReadAsync(buffer, 0, buffer.Length, timeout.Token));
        Assert.ThrowsAny<IOException>(() => input.Read(buffer, 0, buffer.Length));

        return true;
    }

    [Fact]
    public async Task AnOrderlyCloseStillEndsTheStreamAfterLibsrtReleasesTheSocket()
    {
        (SrtSocket sender, SrtSocket receiver) = ConnectFileStream();
        using SrtStream input = new(receiver, ownsSocket: true);
        using CancellationTokenSource timeout = new(Deadline);

        sender.Dispose();

        // libsrt reports InvalidSocket once it has released the closed socket; without an earlier break that is the end.
        Assert.True(SpinWait.SpinUntil(() => IsReleased(input.Socket), Deadline));
        byte[] buffer = new byte[16];
        Assert.Equal(0, await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token));
        Assert.Equal(0, input.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public async Task ExposesTimeoutsAndFlushesAsNoOps()
    {
        (SrtSocket sender, SrtSocket receiver) = ConnectFileStream();
        using SrtStream output = new(sender, ownsSocket: true);
        using SrtStream input = new(receiver, ownsSocket: true);

        Assert.Equal(Timeout.Infinite, input.ReadTimeout);
        Assert.Equal(Timeout.Infinite, output.WriteTimeout);
        input.ReadTimeout = 250;
        output.WriteTimeout = 750;
        Assert.Equal(250, input.ReadTimeout);
        Assert.Equal(750, output.WriteTimeout);
        input.ReadTimeout = Timeout.Infinite;
        Assert.Equal(Timeout.Infinite, input.ReadTimeout);
        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadTimeout = -2);

        output.Flush();
        await output.FlushAsync(CancellationToken.None);
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        Task flushing = output.FlushAsync(canceled.Token);
        Assert.True(flushing.IsCanceled);

        Assert.Throws<NotSupportedException>(() => output.Position);
        Assert.Throws<NotSupportedException>(() => output.Position = 0);
        Assert.Throws<NotSupportedException>(() => output.SetLength(0));
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task ValidatesBufferArgumentsAndAllowsEmptyReads()
    {
        (SrtSocket sender, SrtSocket receiver) = ConnectFileStream();
        using SrtStream output = new(sender, ownsSocket: true);
        using SrtStream input = new(receiver, ownsSocket: true);
        byte[] buffer = new byte[16];

        Assert.Throws<ArgumentNullException>("buffer", () => input.Read(null!, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>("offset", () => input.Read(buffer, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>("offset", () => input.Read(buffer, 17, 0));
        Assert.Throws<ArgumentOutOfRangeException>("count", () => input.Read(buffer, 0, 17));
        Assert.Throws<ArgumentOutOfRangeException>("count", () => output.Write(buffer, 8, -1));
        Assert.Throws<ArgumentNullException>("buffer", () => { _ = output.WriteAsync(null!, 0, 0); });

        // Empty reads return at once instead of waiting for data, and empty writes send nothing.
        Assert.Equal(0, input.Read(buffer, 0, 0));
        Assert.Equal(0, await input.ReadAsync(buffer, 16, 0));
        output.Write(buffer, 0, 0);
        await output.WriteAsync(buffer, 0, 0);

        output.Write([4, 5], 0, 2);
        Assert.Equal(new byte[] { 4, 5 }, await ReadExactlyAsync(input, 2));
    }

#if NET
    [Fact]
    public async Task SpanAndMemoryOverloadsTransferData()
    {
        (SrtSocket sender, SrtSocket receiver) = ConnectFileStream();
        using SrtStream output = new(sender, ownsSocket: true);
        using SrtStream input = new(receiver, ownsSocket: true);
        using CancellationTokenSource timeout = new(Deadline);

        output.Write([1, 2, 3]);
        await output.WriteAsync(new ReadOnlyMemory<byte>([4, 5]), timeout.Token);
        byte[] received = new byte[5];
        int count = 0;

        while (count < 3)
        {
            count += input.Read(received.AsSpan(count, 3 - count));
        }

        while (count < received.Length)
        {
            count += await input.ReadAsync(received.AsMemory(count), timeout.Token);
        }

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, received);
        Assert.Equal(0, input.Read([]));
        Assert.Equal(0, await input.ReadAsync(Memory<byte>.Empty, timeout.Token));
    }
#endif

    [Fact]
    public void AWriteThatCannotBeQueuedInTimeFailsWithAnIOException()
    {
        // Small buffers on both sides, and nobody reads, so the writer runs out of buffer space.
        (SrtSocket sender, SrtSocket receiver) = ConnectFileStream(bufferBytes: 64 * 1456);
        using SrtStream output = new(sender, ownsSocket: true)
        {
            WriteTimeout = 100
        };
        using SrtStream input = new(receiver, ownsSocket: true);

        IOException timedOut = Assert.Throws<IOException>(() => output.Write(new byte[4 * 1024 * 1024], 0, 4 * 1024 * 1024));
        Assert.IsType<TimeoutException>(timedOut.InnerException);
        Assert.Equal(SrtSocketState.Connected, sender.State);
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int length)
    {
        using CancellationTokenSource timeout = new(Deadline);
        byte[] buffer = new byte[length];
        int count = 0;

        while (count < length)
        {
            int read = await stream.ReadAsync(buffer, count, length - count, timeout.Token);
            Assert.NotEqual(0, read);
            count += read;
        }

        return buffer;
    }

    private static bool IsReleased(SrtSocket socket)
    {
        return socket.State is SrtSocketState.Closed or SrtSocketState.Nonexistent;
    }

    private static (SrtSocket Sender, SrtSocket Receiver) ConnectFileStream(int? bufferBytes = null)
    {
        using SrtSocket listener = new();
        SrtSocket caller = new();

        try
        {
            listener.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
            caller.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);

            if (bufferBytes.HasValue)
            {
                listener.SetOption(SrtOptions.ReceiveBufferSize, bufferBytes.Value);
                caller.SetOption(SrtOptions.SendBufferSize, bufferBytes.Value);
            }

            listener.Bind(new(IPAddress.Loopback, 0));
            listener.Listen();
            caller.Connect(listener.LocalEndPoint, TimeSpan.FromSeconds(5));

            return (caller, listener.Accept(TimeSpan.FromSeconds(5)));
        }
        catch
        {
            caller.Dispose();

            throw;
        }
    }
}
