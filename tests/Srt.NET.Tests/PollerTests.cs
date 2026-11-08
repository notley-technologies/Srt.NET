// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Srt.Net;
using Xunit;

namespace Srt.NET.Tests;

public sealed class PollerTests : IClassFixture<RuntimeFixture>
{
    [Fact]
    public void ListenerWaitsQuietlyAndRetainsQueuedConnections()
    {
        using SrtSocket listener = new();
        using SrtSocket first = new();
        using SrtSocket second = new();
        using SrtPoller poller = new();
        ConcurrentQueue<string> logs = new();
        SrtLogging.SetHandler(entry => logs.Enqueue(entry.Message));

        try
        {
            listener.Bind(new(IPAddress.Loopback, 0));
            poller.Add(listener, SrtPollEvents.Accept);
            listener.Listen();
            Assert.Empty(poller.Wait(TimeSpan.FromMilliseconds(180)));
            first.Connect(listener.LocalEndPoint, TestSockets.Deadline);
            second.Connect(listener.LocalEndPoint, TestSockets.Deadline);

            Assert.Single(poller.Wait(TimeSpan.Zero));
            Assert.True(listener.TryAccept(out SrtSocket? firstPeer));
            using SrtSocket acceptedFirst = firstPeer!;
            Assert.Single(poller.Wait(TimeSpan.Zero));
            using SrtSocket acceptedSecond = listener.Accept(TimeSpan.Zero);
            Assert.Empty(poller.Wait(TimeSpan.FromMilliseconds(80)));
            Assert.DoesNotContain(logs, message => message.Contains("no pending connection available"));
        }
        finally
        {
            SrtLogging.SetHandler(null);
        }
    }

    [Fact]
    public void ReportsMultipleSocketsAndUpdatesInterests()
    {
        using ConnectionPair first = TestSockets.Connect();
        using ConnectionPair second = TestSockets.Connect();
        using SrtPoller poller = new();
        poller.Add(first.Peer, SrtPollEvents.Read);
        poller.Add(second.Peer, SrtPollEvents.Read);
        first.Caller.Send([1], TestSockets.Deadline);
        second.Caller.Send([2], TestSockets.Deadline);
        SrtSocket[] expected = [first.Peer, second.Peer];
        bool[] found = new bool[2];
        using CancellationTokenSource cancel = new(TestSockets.Deadline);

        while (!found.All(value => value))
        {
            foreach (SrtPollEvent ready in poller.Wait(cancellationToken: cancel.Token))
            {
                int index = Array.IndexOf(expected, ready.Socket);
                Assert.InRange(index, 0, 1);
                found[index] = true;
                Assert.True(ready.Socket.TryReceive(new byte[1500], out int received));
                Assert.Equal(1, received);
            }
        }

        poller.Update(first.Peer, SrtPollEvents.Write);
        Assert.Contains(poller.Wait(TestSockets.Deadline), entry => entry.Socket == first.Peer && entry.Events.HasFlag(SrtPollEvents.Write));
        poller.Remove(first.Peer);
        poller.Remove(second.Peer);
        Assert.Empty(poller.Wait());
        Assert.Equal(SrtSocketState.Connected, first.Peer.State);
    }

    [Fact]
    public async Task CancellationAndDisposalInterruptWaiting()
    {
        using SrtSocket listener = new();
        using SrtPoller poller = new();
        listener.Bind(new(IPAddress.Loopback, 0));
        poller.Add(listener, SrtPollEvents.Accept);
        listener.Listen();
        using CancellationTokenSource cancel = new(100);
        Assert.ThrowsAny<OperationCanceledException>(() => poller.Wait(cancellationToken: cancel.Token));

        Task<SrtPollEvent[]> waiting = Task.Run(() => poller.Wait(TestSockets.Deadline));
        await Task.Delay(100);
        poller.Dispose();
        Assert.Same(waiting, await Task.WhenAny(waiting, Task.Delay(TestSockets.Deadline)));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
        Assert.Equal(SrtSocketState.Listening, listener.State);
    }

    [Fact]
    public void DisposedSocketProducesOneClosedEvent()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using SrtPoller poller = new();
        poller.Add(pair.Peer, SrtPollEvents.Read);
        pair.Peer.Dispose();
        SrtPollEvent ready = Assert.Single(poller.Wait());
        Assert.Equal(SrtPollEvents.Closed, ready.Events);
        Assert.Empty(poller.Wait());
    }

    [Fact]
    public void ConnectCanBeDrivenByTheApplication()
    {
        using SrtSocket listener = new();
        using SrtSocket caller = new();
        using SrtPoller poller = new();
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        poller.Add(caller, SrtPollEvents.Connect);
        caller.BeginConnect(listener.LocalEndPoint);
        SrtPollEvent ready = Assert.Single(poller.Wait(TestSockets.Deadline));
        Assert.True(ready.Events.HasFlag(SrtPollEvents.Connect));
        caller.CompleteConnect();
        using SrtSocket peer = listener.Accept(TestSockets.Deadline);
        Assert.Equal(SrtSocketState.Connected, caller.State);
    }

    [Fact]
    public void RejectsLateListenerRegistrationAndInvalidInterests()
    {
        using SrtSocket listener = new();
        using SrtPoller poller = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => poller.Add(listener, SrtPollEvents.Closed));
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        Assert.Throws<InvalidOperationException>(() => poller.Add(listener, SrtPollEvents.Accept));
    }

    [Fact]
    public void FailedConnectionReportsErrorReadiness()
    {
        using SrtSocket listener = new();
        using SrtSocket caller = new();
        using SrtPoller poller = new();
        listener.SetConnectionHandler(_ => false);
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        poller.Add(caller, SrtPollEvents.Connect);
        caller.BeginConnect(listener.LocalEndPoint);
        SrtPollEvent ready = Assert.Single(poller.Wait(TestSockets.Deadline));
        Assert.True(ready.Events.HasFlag(SrtPollEvents.Error));
        Assert.Throws<SrtException>(() => caller.CompleteConnect());
        Assert.Equal(SrtSocketState.Closed, caller.State);
    }

    [Fact]
    public async Task RegistrationsCanChangeDuringAWait()
    {
        using SrtSocket first = new();
        using SrtSocket second = new();
        using SrtSocket caller = new();
        using SrtPoller poller = new();
        first.Bind(new(IPAddress.Loopback, 0));
        second.Bind(new(IPAddress.Loopback, 0));
        poller.Add(first, SrtPollEvents.Accept);
        first.Listen();
        Task<SrtPollEvent[]> waiting = Task.Run(() => poller.Wait(TestSockets.Deadline));
        Assert.True(SpinWait.SpinUntil(() => poller.IsWaiting, TestSockets.Deadline));
        Assert.Throws<InvalidOperationException>(() => poller.Wait(TimeSpan.Zero));
        poller.Add(second, SrtPollEvents.Accept);
        second.Listen();
        caller.Connect(second.LocalEndPoint, TestSockets.Deadline);
        Assert.Same(waiting, await Task.WhenAny(waiting, Task.Delay(TestSockets.Deadline)));
        Assert.Equal(second, Assert.Single(await waiting).Socket);
        using SrtSocket peer = second.Accept(TimeSpan.Zero);
    }

    [Fact]
    public void DisposingARegisteredSocketClosesItWithoutWaitingForThePoller()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using SrtPoller poller = new();
        poller.Add(pair.Caller, SrtPollEvents.Read);

        // The poller must not keep the native socket open until its next Wait.
        pair.Caller.Dispose();
        Assert.True(SpinWait.SpinUntil(() => pair.Peer.State != SrtSocketState.Connected, TestSockets.Deadline));

        SrtPollEvent closed = Assert.Single(poller.Wait(TimeSpan.Zero));
        Assert.Same(pair.Caller, closed.Socket);
        Assert.Equal(SrtPollEvents.Closed, closed.Events);
        Assert.Empty(poller.Wait(TimeSpan.Zero));
    }
}
