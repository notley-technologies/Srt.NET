// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Srt.Net;
using Xunit;

namespace Srt.Net.Tests;

public sealed class PollerBufferTests : IClassFixture<RuntimeFixture>
{
    [Fact]
    public void SmallDestinationRetainsUndeliveredClosedEvents()
    {
        using SrtPoller poller = new();
        using SrtSocket first = new();
        using SrtSocket second = new();
        using SrtSocket third = new();
        SrtSocket[] sockets = [first, second, third];

        foreach (SrtSocket socket in sockets)
        {
            poller.Add(socket, SrtPollEvents.Read);
            socket.Dispose();
        }

        SrtPollEvent[] buffer = new SrtPollEvent[3];
        HashSet<SrtSocket> found = new();

        for (int index = 0; index < sockets.Length; index++)
        {
            Assert.Equal(1, poller.Wait(buffer.AsSpan(1, 1), TimeSpan.Zero));
            Assert.Equal(SrtPollEvents.Closed, buffer[1].Events);
            Assert.True(found.Add(buffer[1].Socket));
            Assert.Equal(default, buffer[0]);
            Assert.Equal(default, buffer[2]);
        }

        Assert.Equal(0, poller.Wait(buffer.AsSpan(), TimeSpan.Zero));
        Assert.Equal(3, found.Count);
    }

    [Fact]
    public void SmallDestinationRetainsOtherReadySockets()
    {
        using ConnectionPair first = TestSockets.Connect();
        using ConnectionPair second = TestSockets.Connect();
        using SrtPoller poller = new();
        poller.Add(first.Peer, SrtPollEvents.Read);
        poller.Add(second.Peer, SrtPollEvents.Read);
        first.Caller.Send([1], TestSockets.Deadline);
        second.Caller.Send([2], TestSockets.Deadline);
        SrtPollEvent[] events = new SrtPollEvent[1];
        byte[] bytes = new byte[1500];
        HashSet<SrtSocket> found = new();

        for (int index = 0; index < 2; index++)
        {
            Assert.Equal(1, poller.Wait(events.AsSpan(), TestSockets.Deadline));
            Assert.True(found.Add(events[0].Socket));
            Assert.True(events[0].Socket.TryReceive(bytes.AsSpan(), out int count));
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task RegistrationChangesCanRunDuringNativeWait()
    {
        using SrtSocket listener = new();
        using ConnectionPair pair = TestSockets.Connect();
        using SrtPoller poller = new();
        listener.Bind(new(IPAddress.Loopback, 0));
        poller.Add(listener, SrtPollEvents.Accept);
        listener.Listen();
        SrtPollEvent[] events = new SrtPollEvent[1];
        Task<int> wait = Task.Run(() => poller.Wait(events.AsSpan(), TestSockets.Deadline));
        await Task.Delay(80);
        poller.Add(pair.Peer, SrtPollEvents.Read);
        poller.Update(pair.Peer, SrtPollEvents.Write);
        Assert.Same(wait, await Task.WhenAny(wait, Task.Delay(TestSockets.Deadline)));
        Assert.Equal(1, await wait);
        Assert.Same(pair.Peer, events[0].Socket);

        poller.Remove(pair.Peer);
        wait = Task.Run(() => poller.Wait(events.AsSpan(), TestSockets.Deadline));
        await Task.Delay(80);
        poller.Remove(listener);
        Assert.Same(wait, await Task.WhenAny(wait, Task.Delay(TestSockets.Deadline)));
        Assert.Equal(0, await wait);
        Assert.Equal(SrtSocketState.Listening, listener.State);
    }

    [Fact]
    public async Task SpanWaitPreservesCancellationAndConcurrentDisposal()
    {
        for (int iteration = 0; iteration < 10; iteration++)
        {
            using SrtSocket listener = new();
            using SrtPoller poller = new();
            using CancellationTokenSource cancel = new();
            listener.Bind(new(IPAddress.Loopback, 0));
            poller.Add(listener, SrtPollEvents.Accept);
            listener.Listen();
            SrtPollEvent[] events = new SrtPollEvent[1];
            Task wait = Task.Run(() => poller.Wait(events.AsSpan(), cancellationToken: cancel.Token));
            await Task.Delay(20);

            if (iteration % 2 == 0)
            {
                cancel.Cancel();
                Assert.Same(wait, await Task.WhenAny(wait, Task.Delay(TestSockets.Deadline)));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
                Assert.Equal(0, poller.Wait(events.AsSpan(), TimeSpan.Zero));
            }
            else
            {
                poller.Dispose();
                Assert.Same(wait, await Task.WhenAny(wait, Task.Delay(TestSockets.Deadline)));
                await Assert.ThrowsAsync<ObjectDisposedException>(() => wait);
            }

            Assert.Equal(SrtSocketState.Listening, listener.State);
        }
    }

    [Fact]
    public void ValidatesDestinationAndPreCanceledWait()
    {
        using SrtPoller poller = new();
        using CancellationTokenSource cancel = new();
        SrtPollEvent[] events = new SrtPollEvent[1];
        Assert.Throws<ArgumentException>(() => poller.Wait((Span<SrtPollEvent>)[]));
        Assert.Throws<ArgumentOutOfRangeException>(() => poller.Wait(events.AsSpan(), TimeSpan.FromMilliseconds(-2)));
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => poller.Wait(events.AsSpan(), cancellationToken: cancel.Token));
        Assert.Equal(0, poller.Wait(events.AsSpan(), TimeSpan.Zero));
    }
}
