// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Srt.Net;
using Srt.Net.Async;
using Srt.Net.Interop;
using Xunit;

namespace Srt.Net.Tests;

/// <summary>Regression tests for the shared readiness engine and its libsrt edge cases.</summary>
public sealed class EngineTests : IClassFixture<RuntimeFixture>
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task MultiPacketMessageSendsCompleteWhenBufferSpaceFrees(bool asynchronous, bool messageCall)
    {
        // libsrt does not clear writable readiness when a message larger than the free buffer would block,
        // so no new edge arrives as space frees. The engine must retry on its own.
        using ConnectionPair pair = TestSockets.Connect(bufferBytes: 64 * 1456);
        using CancellationTokenSource timeout = new(Deadline);
        byte[] message = new byte[23 * 1400];
        int queued = 0;

        // Fill the send buffer and the peer's receive buffer, so no space frees until the peer reads. A send that fails
        // only proves the send buffer is full. The caller's flow window drops to zero once it has processed the
        // acknowledgement that the peer's buffer is full, and libsrt frees acknowledged send space before it publishes the
        // window, so a send that fails after a zero window means nothing more can be sent until the peer reads.
        while (true)
        {
            int window = pair.Caller.GetStatistics(instantaneous: true).PacketsFlowWindow;

            if (pair.Caller.TrySendMessage(message, out _))
            {
                queued++;
                Assert.True(queued < 64, "The peer kept accepting data without reading it.");

                continue;
            }

            if (window == 0)
            {
                break;
            }

            await Task.Delay(10, timeout.Token);
        }

        // On a MessageApi socket, Send and SendAsync also send one message, so they block the same way.
        Task<int> blocked = (asynchronous, messageCall) switch
        {
            (true, true) => SendMessageAsync(),
            (true, false) => pair.Caller.SendAsync(message, timeout.Token).AsTask(),
            (false, true) => Task.Run(() => pair.Caller.SendMessage(message, timeout: Deadline).BytesTransferred, timeout.Token),
            (false, false) => Task.Run(() => pair.Caller.Send(message, Deadline), timeout.Token)
        };

        await Task.Delay(50, timeout.Token);
        Assert.False(blocked.IsCompleted);

        byte[] received = new byte[message.Length];

        for (int index = 0; index <= queued; index++)
        {
            Assert.Equal(message.Length, (await pair.Peer.ReceiveMessageAsync(received, timeout.Token)).BytesTransferred);
        }

        Assert.Equal(message.Length, await blocked);

        // The watchdog is off during tests, so only the retry timer can have completed the send.
        Assert.True(pair.Caller.AsyncContext!.SendQueue.TimedRetries > 0);

        async Task<int> SendMessageAsync()
        {
            return (await pair.Caller.SendMessageAsync(message, cancellationToken: timeout.Token)).BytesTransferred;
        }
    }

    [Fact]
    public async Task ASendStuckBehindAPeerThatStopsReadingBacksOffItsRetries()
    {
        using ConnectionPair pair = TestSockets.Connect(bufferBytes: 64 * 1456);
        using CancellationTokenSource timeout = new(Deadline);
        byte[] message = new byte[23 * 1400];
        int queued = 0;

        // Fill both buffers as in MultiPacketMessageSendsCompleteWhenBufferSpaceFrees, so the next send blocks while
        // libsrt still reports the socket writable, and stays blocked while the peer does not read.
        while (true)
        {
            int window = pair.Caller.GetStatistics(instantaneous: true).PacketsFlowWindow;

            if (pair.Caller.TrySendMessage(message, out _))
            {
                queued++;
                Assert.True(queued < 64, "The peer kept accepting data without reading it.");

                continue;
            }

            if (window == 0)
            {
                break;
            }

            await Task.Delay(10, timeout.Token);
        }

        Task<int> blocked = pair.Caller.SendAsync(message, timeout.Token).AsTask();
        await Task.Delay(1500, timeout.Token);
        Assert.False(blocked.IsCompleted);

        // Retries every 10 ms would make about 150 attempts by now; backing off to 250 ms makes about 10.
        int retries = pair.Caller.AsyncContext!.SendQueue.TimedRetries;
        Assert.InRange(retries, 3, 30);

        byte[] received = new byte[message.Length];

        for (int index = 0; index <= queued; index++)
        {
            Assert.Equal(message.Length, (await pair.Peer.ReceiveMessageAsync(received, timeout.Token)).BytesTransferred);
        }

        Assert.Equal(message.Length, await blocked);
    }

    [Fact]
    public async Task WatchdogRetriesAQueueThatSeesNoEdge()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using CancellationTokenSource timeout = new(Deadline);
        int previous = SrtAsyncEngine.WatchdogMilliseconds;
        SrtAsyncEngine.WatchdogMilliseconds = 50;

        try
        {
            ValueTask<int> pending = pair.Peer.ReceiveAsync(new byte[1500], timeout.Token);
            SrtOperationQueue queue = pair.Peer.AsyncContext!.ReceiveQueue;

            Assert.True(SpinWait.SpinUntil(() => queue.WatchdogRetries >= 2, Deadline));
            Assert.False(pending.IsCompleted);

            await pair.Caller.SendAsync(new byte[] { 3 }, timeout.Token);
            Assert.Equal(1, await pending);
        }
        finally
        {
            SrtAsyncEngine.WatchdogMilliseconds = previous;
        }
    }

    [Fact]
    public async Task CancelingTheHeadOfAQueueLetsTheNextOperationComplete()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using CancellationTokenSource timeout = new(Deadline);
        using CancellationTokenSource cancelFirst = new();
        byte[] first = new byte[1500];
        byte[] second = new byte[1500];

        ValueTask<int> canceled = pair.Peer.ReceiveAsync(first, cancelFirst.Token);
        ValueTask<int> remaining = pair.Peer.ReceiveAsync(second, timeout.Token);
        cancelFirst.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceled);

        await pair.Caller.SendAsync(new byte[] { 11 }, timeout.Token);
        Assert.Equal(1, await remaining);
        Assert.Equal(11, second[0]);
    }

    [Fact]
    public async Task SynchronousAndAsynchronousReceivesShareOneFifoQueue()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using CancellationTokenSource timeout = new(Deadline);
        byte[] asyncBuffer = new byte[1500];
        byte[] syncBuffer = new byte[1500];

        ValueTask<int> asyncReceive = pair.Peer.ReceiveAsync(asyncBuffer, timeout.Token);
        Task<int> syncReceive = Task.Run(() => pair.Peer.Receive(syncBuffer, Deadline), timeout.Token);

        // Let the synchronous receive queue behind the asynchronous one.
        await Task.Delay(100, timeout.Token);
        await pair.Caller.SendAsync(new byte[] { 1 }, timeout.Token);
        await pair.Caller.SendAsync(new byte[] { 2 }, timeout.Token);

        Assert.Equal(1, await asyncReceive);
        Assert.Equal(1, await syncReceive);
        Assert.Equal(1, asyncBuffer[0]);
        Assert.Equal(2, syncBuffer[0]);
    }

    [Fact]
    public async Task DisposeInterruptsABlockedSynchronousReceiveImmediately()
    {
        using ConnectionPair pair = TestSockets.Connect();

        // The timeout only bounds a regression; disposal must end the wait long before it.
        Task<int> blocked = Task.Run(() => pair.Peer.Receive(new byte[1500], Deadline));
        WaitUntilReceivePending(pair.Peer, blocked);

        System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
        pair.Peer.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => blocked);
        Assert.True(elapsed.ElapsedMilliseconds < 1000, $"Took {elapsed.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public async Task CancellationInterruptsABlockedSynchronousReceiveAndLeavesTheSocketUsable()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using CancellationTokenSource cancel = new();
        byte[] buffer = new byte[1500];

        Task<int> blocked = Task.Run(() => pair.Peer.Receive(buffer, cancellationToken: cancel.Token));
        WaitUntilReceivePending(pair.Peer, blocked);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
        Assert.Throws<TimeoutException>(() => pair.Peer.Receive(buffer, TimeSpan.FromMilliseconds(50)));

        pair.Caller.Send([5], Deadline);
        Assert.Equal(1, pair.Peer.Receive(buffer, Deadline));
        Assert.Equal(5, buffer[0]);
    }

    [Fact]
    public async Task ManySocketsWithRandomCancellationAndDisposalAllComplete()
    {
        const int Pairs = 32;
        ConnectionPair[] pairs = [.. Enumerable.Range(0, Pairs).Select(_ => TestSockets.Connect())];
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));

        try
        {
            await Task.WhenAll(pairs.Select((pair, index) => Task.Run(async () =>
            {
                byte[] buffer = new byte[1500];

                for (int round = 0; round < 40; round++)
                {
                    using CancellationTokenSource cancel = new();
                    ValueTask<int> receive = pair.Peer.ReceiveAsync(buffer, cancel.Token);

                    if ((round + index) % 3 == 0)
                    {
                        cancel.CancelAfter((round + index) % 5);
                    }
                    else
                    {
                        await pair.Caller.SendAsync(new[] { (byte)round }, timeout.Token);
                    }

                    try
                    {
                        Assert.Equal(1, await receive);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected for the canceled rounds.
                    }
                }

                if (index % 4 == 0)
                {
                    ValueTask<int> pending = pair.Peer.ReceiveAsync(buffer, timeout.Token);
                    pair.Peer.Dispose();
                    await Assert.ThrowsAnyAsync<Exception>(async () => await pending);
                }
            }, timeout.Token)));
        }
        finally
        {
            foreach (ConnectionPair pair in pairs)
            {
                pair.Dispose();
            }
        }
    }

    [Fact]
    public async Task UndisposedSocketsThatUsedTheEngineAreFinalized()
    {
        // The peer stays open, so only the abandoned caller's finalizer can close the caller's native socket.
        (int socketId, SrtSocket peer) = await CreateAbandonedConnectionAsync();

        using (peer)
        {
            Assert.Equal(SrtSocketState.Connected, NativeMethods.srt_getsockstate(socketId));

            for (int attempt = 0; attempt < 50 && NativeMethods.srt_getsockstate(socketId) == SrtSocketState.Connected; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(20);
            }

            Assert.True(NativeMethods.srt_getsockstate(socketId) is SrtSocketState.Closing or SrtSocketState.Closed or SrtSocketState.Nonexistent);
        }
    }

    [Fact]
    public async Task DisposedSocketsLeaveTheEngine()
    {
        int baseline = SrtAsyncEngine.RegisteredSockets;

        using CancellationTokenSource timeout = new(Deadline);

        using (ConnectionPair pair = TestSockets.Connect())
        {
            await pair.Caller.SendAsync(new byte[] { 1 }, timeout.Token);
            await pair.Peer.ReceiveAsync(new byte[1500], timeout.Token);
            Assert.True(SrtAsyncEngine.RegisteredSockets >= baseline + 2);
        }

        // In the file profile, linger keeps a socket registered while its queued data drains; a sender disposed before
        // the peer's acknowledgement arrives holds one registration for that short drain. Every registration must end.
        Assert.True(SpinWait.SpinUntil(() => SrtAsyncEngine.RegisteredSockets == baseline, Deadline), $"{SrtAsyncEngine.RegisteredSockets - baseline} disposed sockets are still registered with the engine.");
    }

    [Fact]
    public async Task AnAcceptLoopKeepsServingAfterAQueuedClientLeaves()
    {
        using SrtSocket listener = new();
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        using CancellationTokenSource timeout = new(Deadline);
        using SrtSocket caller = new();
        await caller.ConnectAsync(listener.LocalEndPoint, timeout.Token);

        // libsrt accepts the lowest socket identifier first and hands out decreasing identifiers, so the connection that
        // breaks is accepted before the healthy one. It is either discarded by libsrt or returned in a broken state with
        // any unread data; it must never fail the accept loop. (libsrt's transient SRT_ESCLOSED, which the wrapper skips,
        // is a narrow internal race that a black-box test cannot provoke reliably.)
        using (SrtSocket early = new())
        {
            early.Connect(listener.LocalEndPoint, Deadline);
        }

        await Task.Delay(100, timeout.Token);

        while (true)
        {
            using SrtSocket accepted = await listener.AcceptAsync(timeout.Token);

            if (accepted.State == SrtSocketState.Connected && accepted.RemoteEndPoint.Equals(caller.LocalEndPoint))
            {
                break;
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptReturnsAConnectionWhosePeerLeftWithUnreadData(bool asynchronous)
    {
        using SrtSocket listener = new();
        listener.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
        listener.SetOption(SrtOptions.MessageApi, true);
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        using CancellationTokenSource timeout = new(Deadline);

        using (SrtSocket early = new())
        {
            early.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
            early.SetOption(SrtOptions.MessageApi, true);
            early.Connect(listener.LocalEndPoint, Deadline);
            early.SendMessage([7, 8, 9], timeout: Deadline);
            await Task.Delay(200, timeout.Token);
        }

        // libsrt keeps the broken connection queued while it holds unread data; accepting it must not fail.
        await Task.Delay(300, timeout.Token);
        using SrtSocket accepted = asynchronous ? await listener.AcceptAsync(timeout.Token) : listener.Accept(Deadline);
        byte[] buffer = new byte[1500];

        Assert.Equal(3, accepted.ReceiveMessage(buffer, Deadline).BytesTransferred);
        Assert.Equal(new byte[] { 7, 8, 9 }, buffer.Take(3));
    }

    [Fact]
    public async Task LingerEndsTheDrainOfDataThePeerNeverReads()
    {
        using ConnectionPair pair = TestSockets.Connect(bufferBytes: 64 * 1456);
        pair.Caller.SetOption(SrtOptions.Linger, new SrtLinger(true, 1));
        byte[] message = new byte[1400];

        while (pair.Caller.TrySendMessage(message, out _))
        {
        }

        // The peer never reads, so the send buffer cannot drain; the close must still happen after the linger time.
        pair.Caller.Dispose();
        System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();

        while (pair.Peer.State == SrtSocketState.Connected && elapsed.Elapsed < Deadline)
        {
            await Task.Delay(50);
        }

        Assert.NotEqual(SrtSocketState.Connected, pair.Peer.State);
    }

    /// <summary>Waits until a receive has queued on the socket and is waiting for data, so the test interrupts it rather than its start.</summary>
    private static void WaitUntilReceivePending(SrtSocket socket, Task receive)
    {
        bool pending = SpinWait.SpinUntil(() => receive.IsCompleted || socket.AsyncContext?.ReceiveQueue.IsWaiting == true, Deadline);

        Assert.False(receive.IsCompleted, $"The receive completed before it was interrupted: {receive.Status}.");
        Assert.True(pending, "The receive did not start waiting for data in time.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(int SocketId, SrtSocket Peer)> CreateAbandonedConnectionAsync()
    {
        using CancellationTokenSource timeout = new(Deadline);
        using SrtSocket listener = new();
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        SrtSocket caller = new();
        await caller.ConnectAsync(listener.LocalEndPoint, timeout.Token);
        SrtSocket peer = await listener.AcceptAsync(timeout.Token);
        await caller.SendAsync(new byte[] { 1 }, timeout.Token);
        await peer.ReceiveAsync(new byte[1500], timeout.Token);

        // Drop the caller without disposing it.
        return (caller.Handle.DangerousGetHandle().ToInt32(), peer);
    }
}
