// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Srt.Net;
using Srt.Net.Async;
using Srt.Net.Interop;
using Xunit;

namespace Srt.Net.Tests;

public sealed class AsyncTests : IClassFixture<RuntimeFixture>
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectAcceptSendAndReceiveAsynchronously(bool ipv6)
    {
        using SrtSocket listener = new();
        using SrtSocket caller = new();
        listener.Bind(new(ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, 0));
        listener.Listen();
        using CancellationTokenSource timeout = new(Deadline);

        Task<SrtSocket> accepting = listener.AcceptAsync(timeout.Token).AsTask();
        await caller.ConnectAsync(listener.LocalEndPoint, timeout.Token);
        using SrtSocket peer = await accepting;

        Assert.Equal(SrtSocketState.Connected, caller.State);
        Assert.Equal(caller.LocalEndPoint, peer.RemoteEndPoint);

        byte[] received = new byte[1500];
        Assert.Equal(3, await caller.SendAsync(new byte[] { 1, 2, 3 }, timeout.Token));
        Assert.Equal(3, await peer.ReceiveAsync(received, timeout.Token));
        Assert.Equal(new byte[] { 1, 2, 3 }, received.Take(3));

        Assert.Equal(2, await peer.SendAsync(new byte[] { 9, 8 }, timeout.Token));
        Assert.Equal(2, await caller.ReceiveAsync(received, timeout.Token));
        Assert.Equal(new byte[] { 9, 8 }, received.Take(2));
    }

    [Fact]
    public async Task PendingReceiveCompletesWhenDataArrives()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using CancellationTokenSource timeout = new(Deadline);
        byte[] received = new byte[1500];

        ValueTask<int> pending = pair.Peer.ReceiveAsync(received, timeout.Token);
        Assert.False(pending.IsCompleted);
        await Task.Delay(50, timeout.Token);
        Assert.False(pending.IsCompleted);

        await pair.Caller.SendAsync(new byte[] { 42 }, timeout.Token);
        Assert.Equal(1, await pending);
        Assert.Equal(42, received[0]);
    }

    [Fact]
    public async Task MessagesExposeMetadataAsynchronously()
    {
        // Source timestamps apply in live mode.
        using SrtSocket listener = new();
        using SrtSocket caller = new();
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        caller.Connect(listener.LocalEndPoint, Deadline);
        using SrtSocket peer = listener.Accept(Deadline);
        using CancellationTokenSource timeout = new(Deadline);
        long timestamp = SrtClock.NowMicroseconds;
        byte[] received = new byte[1500];

        ValueTask<SrtMessageResult> receiving = peer.ReceiveMessageAsync(received, timeout.Token);
        SrtMessageOptions options = new()
        {
            SourceTimeMicroseconds = timestamp
        };
        SrtMessageResult sent = await caller.SendMessageAsync(new byte[] { 5, 6, 7 }, options, timeout.Token);
        SrtMessageResult result = await receiving;

        Assert.Equal(3, result.BytesTransferred);
        Assert.Equal(timestamp, sent.SourceTimeMicroseconds);
        Assert.Equal(sent.MessageNumber, result.MessageNumber);
        Assert.Equal(new byte[] { 5, 6, 7 }, received.Take(3));
    }

    [Fact]
    public async Task QueuedReceivesCompleteInOrder()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using CancellationTokenSource timeout = new(Deadline);
        byte[][] buffers = [new byte[1500], new byte[1500], new byte[1500]];
        Task<int>[] receives = [.. buffers.Select(buffer => pair.Peer.ReceiveAsync(buffer, timeout.Token).AsTask())];

        for (byte value = 1; value <= 3; value++)
        {
            await pair.Caller.SendAsync(new[] { value }, timeout.Token);
        }

        int[] counts = await Task.WhenAll(receives);
        Assert.Equal([1, 1, 1], counts);
        Assert.Equal(new byte[] { 1, 2, 3 }, buffers.Select(buffer => buffer[0]));
    }

    [Fact]
    public async Task CancellationEndsAPendingReceiveAndLeavesTheSocketUsable()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using CancellationTokenSource timeout = new(Deadline);
        using CancellationTokenSource cancel = new();
        byte[] received = new byte[1500];

        ValueTask<int> pending = pair.Peer.ReceiveAsync(received, cancel.Token);
        cancel.Cancel();
        OperationCanceledException canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.Equal(cancel.Token, canceled.CancellationToken);

        await pair.Caller.SendAsync(new byte[] { 7 }, timeout.Token);
        Assert.Equal(1, await pair.Peer.ReceiveAsync(received, timeout.Token));
        Assert.Equal(7, received[0]);
    }

    [Fact]
    public async Task AlreadyCanceledTokensDoNotStartOperations()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pair.Peer.ReceiveAsync(new byte[1500], cancel.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pair.Caller.SendAsync(new byte[] { 1 }, cancel.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pair.Peer.ReceiveMessageAsync(new byte[1500], cancel.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pair.Caller.SendMessageAsync(new byte[] { 1 }, cancellationToken: cancel.Token));

        using SrtSocket untouched = new();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await untouched.ConnectAsync(new(IPAddress.Loopback, 9), cancel.Token));
        Assert.Equal(SrtSocketState.Initialized, untouched.State);

        using SrtSocket listener = new();
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await listener.AcceptAsync(cancel.Token));
        Assert.Equal(SrtSocketState.Listening, listener.State);

        // Nothing was started, so the connection still carries data in both directions.
        pair.Caller.Send([3], Deadline);
        byte[] received = new byte[1500];
        Assert.Equal(1, pair.Peer.Receive(received, Deadline));
        Assert.Equal(3, received[0]);
    }

    [Fact]
    public async Task AnEngineFaultFailsPendingAndLaterOperationsWithTheCause()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using CancellationTokenSource timeout = new(Deadline);
        ValueTask<int> pending = pair.Peer.ReceiveAsync(new byte[1500], timeout.Token);
        Assert.False(pending.IsCompleted);

        // The engine aborts a socket with the defect when its event handling throws; simulate that defect.
        InvalidOperationException defect = new("Simulated engine defect");
        pair.Peer.AsyncContext!.Abort(defect);

        InvalidOperationException ended = await Assert.ThrowsAsync<InvalidOperationException>(async () => await pending);
        Assert.Same(defect, ended.InnerException);
        InvalidOperationException later = await Assert.ThrowsAsync<InvalidOperationException>(async () => await pair.Peer.ReceiveAsync(new byte[1500], timeout.Token));
        Assert.Same(defect, later.InnerException);
        InvalidOperationException blocking = Assert.Throws<InvalidOperationException>(() => pair.Peer.Receive(new byte[1500], Deadline));
        Assert.Same(defect, blocking.InnerException);

        // The other socket of the connection is unaffected.
        Assert.Equal(SrtSocketState.Connected, pair.Caller.State);
    }

    [Fact]
    public async Task ATimedOutReceiveQueuedBehindAnotherLeavesTheEarlierOneWaiting()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using CancellationTokenSource timeout = new(Deadline);
        byte[] first = new byte[1500];
        ValueTask<int> queued = pair.Peer.ReceiveAsync(first, timeout.Token);
        Assert.False(queued.IsCompleted);

        // The synchronous receive waits behind the asynchronous one and gives up first.
        Assert.Throws<TimeoutException>(() => pair.Peer.Receive(new byte[1500], TimeSpan.FromMilliseconds(50)));
        Assert.False(queued.IsCompleted);

        pair.Caller.Send([5], Deadline);
        Assert.Equal(1, await queued);
        Assert.Equal(5, first[0]);

        // The queue is idle again, so the next receive also completes.
        pair.Caller.Send([6], Deadline);
        Assert.Equal(1, pair.Peer.Receive(first, Deadline));
        Assert.Equal(6, first[0]);
    }

    [Fact]
    public async Task CancelingTheFirstQueuedReceiveHandsTheQueueToTheNext()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using CancellationTokenSource timeout = new(Deadline);
        using CancellationTokenSource cancel = new();
        Task<int> blocking = Task.Run(() => pair.Peer.Receive(new byte[1500], Deadline, cancel.Token));
        Assert.True(SpinWait.SpinUntil(() => blocking.IsCompleted || pair.Peer.AsyncContext?.ReceiveQueue.IsWaiting == true, Deadline));
        Assert.False(blocking.IsCompleted);

        byte[] second = new byte[1500];
        ValueTask<int> queued = pair.Peer.ReceiveAsync(second, timeout.Token);
        Assert.False(queued.IsCompleted);
        cancel.Cancel();
        Assert.Same(blocking, await Task.WhenAny(blocking, Task.Delay(Deadline)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocking);

        pair.Caller.Send([8], Deadline);
        Assert.Equal(1, await queued);
        Assert.Equal(8, second[0]);
    }

    [Fact]
    public async Task DisposeFailsPendingOperations()
    {
        using ConnectionPair pair = TestSockets.Connect();
        SrtSocket listener = new();
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();

        ValueTask<int> receiving = pair.Peer.ReceiveAsync(new byte[1500]);
        ValueTask<SrtSocket> accepting = listener.AcceptAsync();
        pair.Peer.Dispose();
        listener.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await receiving);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await accepting);
        Assert.Throws<ObjectDisposedException>(() => pair.Peer.ReceiveAsync(new byte[1500]));
    }

    [Fact]
    public async Task PeerCloseFaultsAPendingReceive()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using CancellationTokenSource timeout = new(Deadline);

        ValueTask<int> pending = pair.Peer.ReceiveAsync(new byte[1500], timeout.Token);
        pair.Caller.Dispose();

        SrtException error = await Assert.ThrowsAsync<SrtException>(async () => await pending);
        Assert.True(error.SrtErrorCode is SrtErrorCode.ConnectionLost or SrtErrorCode.NotConnected or SrtErrorCode.InvalidSocket, error.Message);
    }

    [Fact]
    public async Task ConnectAsyncReportsTypedRejectionAndDisposesTheCaller()
    {
        using SrtSocket listener = new();
        SrtSocket caller = new();
        listener.SetConnectionHandler(request =>
        {
            request.SetRejectionReason(SrtRejectReason.Forbidden);

            return false;
        });
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        using CancellationTokenSource timeout = new(Deadline);

        SrtException error = await Assert.ThrowsAsync<SrtException>(async () => await caller.ConnectAsync(listener.LocalEndPoint, timeout.Token));
        Assert.Equal(SrtErrorCode.ConnectionRejected, error.SrtErrorCode);
        Assert.Equal(SrtRejectReason.Forbidden, error.RejectionReason);
        Assert.Equal(SrtSocketState.Closed, caller.State);
    }

    [Fact]
    public async Task ConnectAsyncToAnAbsentPeerTimesOutAsNoServer()
    {
        using SrtSocket probe = new();
        probe.Bind(new(IPAddress.Loopback, 0));
        IPEndPoint unused = probe.LocalEndPoint;
        probe.Dispose();

        SrtSocket caller = new();
        caller.SetOption(SrtOptions.ConnectTimeout, 300);
        using CancellationTokenSource timeout = new(Deadline);

        SrtException error = await Assert.ThrowsAsync<SrtException>(async () => await caller.ConnectAsync(unused, timeout.Token));
        Assert.Equal(SrtErrorCode.NoServer, error.SrtErrorCode);
        Assert.Equal(SrtRejectReason.Timeout, error.RejectionReason);
        Assert.Equal(SrtSocketState.Closed, caller.State);
    }

    [Fact]
    public async Task CancelingConnectAsyncDisposesTheSocket()
    {
        using SrtSocket probe = new();
        probe.Bind(new(IPAddress.Loopback, 0));
        IPEndPoint unused = probe.LocalEndPoint;
        probe.Dispose();

        SrtSocket caller = new();
        using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await caller.ConnectAsync(unused, cancel.Token));
        Assert.Equal(SrtSocketState.Closed, caller.State);
    }

    [Fact]
    public async Task AcceptAsyncWaitsQuietlyAndAcceptsEachQueuedConnection()
    {
        ConcurrentQueue<string> logs = new();
        SrtLogging.SetHandler(entry => logs.Enqueue(entry.Message));

        try
        {
            using SrtSocket listener = new();
            listener.Bind(new(IPAddress.Loopback, 0));
            listener.Listen();
            using CancellationTokenSource timeout = new(Deadline);

            Assert.False(listener.TryAccept(out _));
            ValueTask<SrtSocket> pending = listener.AcceptAsync(timeout.Token);
            await Task.Delay(100, timeout.Token);
            Assert.False(pending.IsCompleted);

            using SrtSocket first = new();
            using SrtSocket second = new();
            await first.ConnectAsync(listener.LocalEndPoint, timeout.Token);
            await second.ConnectAsync(listener.LocalEndPoint, timeout.Token);

            using SrtSocket acceptedFirst = await pending;
            using SrtSocket acceptedSecond = await listener.AcceptAsync(timeout.Token);
            Assert.Equal(SrtSocketState.Connected, acceptedFirst.State);
            Assert.Equal(SrtSocketState.Connected, acceptedSecond.State);
            Assert.DoesNotContain(logs, message => message.Contains("no pending connection available"));
        }
        finally
        {
            SrtLogging.SetHandler(null);
        }
    }

    [Fact]
    public async Task ConnectionsQueuedBeforeTheFirstAsyncAcceptAreNotMissed()
    {
        using SrtSocket listener = new();
        using SrtSocket caller = new();
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        caller.Connect(listener.LocalEndPoint, Deadline);
        using CancellationTokenSource timeout = new(Deadline);

        using SrtSocket accepted = await listener.AcceptAsync(timeout.Token);
        Assert.Equal(caller.LocalEndPoint, accepted.RemoteEndPoint);
    }

    [Fact]
    public async Task SendAsyncWaitsForBufferSpaceUntilThePeerReceives()
    {
        using ConnectionPair pair = TestSockets.Connect(bufferBytes: 32 * 1456);
        using CancellationTokenSource timeout = new(Deadline);
        byte[] payload = new byte[1400];
        int sent = 0;

        // Fill both buffers: once the caller's flow window is zero and a send still fails, only a read can free space.
        while (true)
        {
            int window = pair.Caller.GetStatistics(instantaneous: true).PacketsFlowWindow;

            if (pair.Caller.TrySend(payload, out _))
            {
                sent++;
                Assert.True(sent < 1000, "The peer kept accepting data without reading it.");

                continue;
            }

            if (window == 0)
            {
                break;
            }

            await Task.Delay(10, timeout.Token);
        }

        ValueTask<int> blocked = pair.Caller.SendAsync(payload, timeout.Token);
        await Task.Delay(300, timeout.Token);
        Assert.False(blocked.IsCompleted, "The send completed before the peer read anything.");

        byte[] received = new byte[1500];

        for (int index = 0; index <= sent; index++)
        {
            Assert.Equal(payload.Length, await pair.Peer.ReceiveAsync(received, timeout.Token));
        }

        Assert.Equal(payload.Length, await blocked);
    }

    [Fact]
    public async Task AContinuationCanWaitForTheOtherDirection()
    {
        using ConnectionPair pair = TestSockets.Connect(bufferBytes: 32 * 1456);
        using CancellationTokenSource timeout = new(Deadline);
        using ManualResetEventSlim blocking = new();
        byte[] payload = new byte[1400];

        // Fill both buffers (see SendAsyncWaitsForBufferSpaceUntilThePeerReceives), so the send waits for the peer. Each
        // buffer holds about 32 packets, so far fewer sends than the bound are needed; a stall ends at the test deadline.
        int sent = 0;

        while (true)
        {
            int window = pair.Caller.GetStatistics(instantaneous: true).PacketsFlowWindow;

            if (pair.Caller.TrySendMessage(payload, out _))
            {
                sent++;
                Assert.True(sent < 1000, "The peer kept accepting data, so the buffers never filled.");

                continue;
            }

            if (window == 0)
            {
                break;
            }

            await Task.Delay(10, timeout.Token);
        }

        // A pending send, and a pending receive whose continuation then blocks until that send completes. The receive
        // completes through the engine, so without care its continuation would hold back the send's readiness.
        Task<SrtMessageResult> send = pair.Caller.SendMessageAsync(payload, cancellationToken: timeout.Token).AsTask();
        ValueTask<SrtMessageResult> receive = pair.Caller.ReceiveMessageAsync(new byte[1500], timeout.Token);
        Assert.False(receive.IsCompleted);
        Task<bool> continuation = WaitInContinuationAsync(receive, send, blocking);

        await pair.Peer.SendMessageAsync(new byte[] { 1 }, cancellationToken: timeout.Token);
        Assert.True(blocking.Wait(Deadline));
        byte[] drain = new byte[1500];

        while (!send.IsCompleted && !timeout.IsCancellationRequested)
        {
            if (!pair.Peer.TryReceiveMessage(drain, out _))
            {
                await Task.Delay(5, timeout.Token);
            }
        }

        Assert.True(await continuation);

        static async Task<bool> WaitInContinuationAsync(ValueTask<SrtMessageResult> receive, Task send, ManualResetEventSlim blocking)
        {
            await receive.ConfigureAwait(false);
            blocking.Set();

            return send.Wait(Deadline);
        }
    }

    [Fact]
    public async Task AContinuationCanWaitForTheNextOperationInTheSameDirection()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using CancellationTokenSource timeout = new(Deadline);
        using ManualResetEventSlim blocking = new();

        ValueTask<SrtMessageResult> first = pair.Peer.ReceiveMessageAsync(new byte[1500], timeout.Token);
        Assert.False(first.IsCompleted);
        Task<bool> continuation = WaitForTheNextReceiveAsync(first, pair.Peer, blocking, timeout.Token);

        // The second receive needs a new readiness edge while the first one's continuation still runs.
        await pair.Caller.SendMessageAsync(new byte[] { 1 }, cancellationToken: timeout.Token);
        Assert.True(blocking.Wait(Deadline));
        await pair.Caller.SendMessageAsync(new byte[] { 2 }, cancellationToken: timeout.Token);

        Assert.True(await continuation);

        static async Task<bool> WaitForTheNextReceiveAsync(ValueTask<SrtMessageResult> first, SrtSocket peer, ManualResetEventSlim blocking, CancellationToken cancellationToken)
        {
            await first.ConfigureAwait(false);
            Task<SrtMessageResult> second = peer.ReceiveMessageAsync(new byte[1500], cancellationToken).AsTask();
            blocking.Set();

            return second.Wait((int)Deadline.TotalMilliseconds, CancellationToken.None);
        }
    }

    [Fact]
    public async Task ContinuationsRunOffTheEngineThreadWithTheCallersExecutionContext()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using CancellationTokenSource timeout = new(Deadline);
        AsyncLocal<string> flowed = new()
        {
            Value = "caller"
        };

        // Record where the continuation runs without letting the test body itself run there.
        TaskCompletionSource<(bool OnEngineThread, string? Flowed)> observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ValueTask<int> pending = pair.Peer.ReceiveAsync(new byte[1500], timeout.Token);
        _ = pending.AsTask().ContinueWith(
            _ => observed.SetResult((SrtAsyncEngine.IsEngineThread, flowed.Value)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        await Task.Delay(20, timeout.Token);
        await pair.Caller.SendAsync(new byte[] { 1 }, timeout.Token);

        (bool onEngineThread, string? value) = await observed.Task;
        Assert.False(onEngineThread);
        Assert.Equal("caller", value);
    }

    [Fact]
    public async Task ManySocketsExchangeMessagesConcurrently()
    {
        const int Pairs = 24;
        const int Messages = 200;
        ConnectionPair[] pairs = [.. Enumerable.Range(0, Pairs).Select(_ => TestSockets.Connect())];
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));

        try
        {
            await Task.WhenAll(pairs.Select(pair => Task.WhenAll(
                Task.Run(async () =>
                {
                    byte[] message = new byte[4];

                    for (int index = 0; index < Messages; index++)
                    {
                        BitConverter.GetBytes(index).CopyTo(message, 0);
                        await pair.Caller.SendAsync(message, timeout.Token);
                    }
                }),
                Task.Run(async () =>
                {
                    byte[] received = new byte[1500];

                    for (int index = 0; index < Messages; index++)
                    {
                        Assert.Equal(4, await pair.Peer.ReceiveAsync(received, timeout.Token));
                        Assert.Equal(index, BitConverter.ToInt32(received, 0));
                    }
                }))));
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
    public async Task SynchronousAndAsynchronousDirectionsCanRunTogether()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using CancellationTokenSource timeout = new(Deadline);
        byte[] received = new byte[1500];

        ValueTask<int> asyncReceive = pair.Peer.ReceiveAsync(received, timeout.Token);
        pair.Peer.Send([3], Deadline);
        Assert.Equal(1, pair.Caller.Receive(new byte[1500], Deadline));
        pair.Caller.Send([4], Deadline);

        Assert.Equal(1, await asyncReceive);
        Assert.Equal(4, received[0]);
    }

#if NET8_0_OR_GREATER
    [Fact]
    public async Task ReadyOperationsCompleteSynchronouslyWithoutAllocating()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using SrtPoller poller = new();
        poller.Add(pair.Peer, SrtPollEvents.Read);
        byte[] payload = [1];
        byte[] received = new byte[1500];

        // Warm up the async context and JIT outside the measured region.
        using CancellationTokenSource timeout = new(Deadline);
        await pair.Caller.SendAsync(payload, timeout.Token);
        await pair.Peer.ReceiveAsync(received, timeout.Token);
        MeasureReadyOperations(pair, poller, payload, received, 20);

        Assert.InRange(MeasureReadyOperations(pair, poller, payload, received, 200), 0, 512);
    }

    private static long MeasureReadyOperations(ConnectionPair pair, SrtPoller poller, byte[] payload, byte[] received, int iterations)
    {
        SrtPollEvent[] events = new SrtPollEvent[1];
        long allocated = 0;

        for (int index = 0; index < iterations; index++)
        {
            ValueTask<int> send = pair.Caller.SendAsync(payload);

            if (!send.IsCompletedSuccessfully || poller.Wait(events.AsSpan(), Deadline) != 1)
            {
                throw new InvalidOperationException("Expected a ready connection.");
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            ValueTask<int> receive = pair.Peer.ReceiveAsync(received);
            bool synchronous = receive.IsCompletedSuccessfully && receive.Result == 1;
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;

            if (!synchronous)
            {
                throw new InvalidOperationException("A ready receive did not complete synchronously.");
            }
        }

        return allocated;
    }
#endif

    [Fact]
    public async Task ReadmeRoundTripDeliversTheReply()
    {
        // Mirrors the README listener and caller: the side that receives the last message closes first.
        using CancellationTokenSource timeout = new(Deadline);
        using SrtSocket listener = new();
        listener.SetOption(SrtOptions.ReceiveLatency, 120);
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();

        Task<string> server = Task.Run(async () =>
        {
            using SrtSocket connection = await listener.AcceptAsync(timeout.Token);
            byte[] buffer = new byte[1500];
            int count = await connection.ReceiveAsync(buffer, timeout.Token);
            await connection.SendAsync(Encoding.UTF8.GetBytes("received"), timeout.Token);

            try
            {
                await connection.ReceiveAsync(buffer, timeout.Token);
            }
            catch (SrtException error) when (error.SrtErrorCode is SrtErrorCode.ConnectionLost or SrtErrorCode.NotConnected or SrtErrorCode.InvalidSocket)
            {
            }

            return Encoding.UTF8.GetString(buffer, 0, count);
        });

        using (SrtSocket connection = new())
        {
            connection.SetOption(SrtOptions.StreamId, "camera-1");
            await connection.ConnectAsync(listener.LocalEndPoint, timeout.Token);
            await connection.SendAsync(Encoding.UTF8.GetBytes("Hello over SRT"), timeout.Token);
            byte[] reply = new byte[1500];
            int count = await connection.ReceiveAsync(reply, timeout.Token);
            Assert.Equal("received", Encoding.UTF8.GetString(reply, 0, count));
        }

        Assert.Equal("Hello over SRT", await server);
    }
}
