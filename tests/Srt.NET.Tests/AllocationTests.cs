// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

#if NET8_0_OR_GREATER
using System;
using Srt.Net;
using Xunit;

namespace Srt.Net.Tests;

public sealed class AllocationTests : IClassFixture<RuntimeFixture>
{
    [Fact]
    public void WouldBlockAndReusedPollingAvoidPerCallAllocations()
    {
        using ConnectionPair pair = TestSockets.Connect();
        using SrtPoller poller = new();
        poller.Add(pair.Peer, SrtPollEvents.Read);
        byte[] buffer = new byte[1500];
        SrtPollEvent[] events = new SrtPollEvent[1];

        for (int index = 0; index < 1000; index++)
        {
            Exercise(pair.Peer, poller, buffer, events);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int index = 0; index < 1000; index++)
        {
            Exercise(pair.Peer, poller, buffer, events);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Allow incidental runtime bookkeeping, but not even one small object per call.
        Assert.InRange(allocated, 0, 1024);
    }

    private static void Exercise(SrtSocket socket, SrtPoller poller, byte[] buffer, SrtPollEvent[] events)
    {
        if (socket.TryReceive(buffer.AsSpan(), out _)
            || socket.TryReceiveMessage(buffer.AsSpan(), out _)
            || poller.Wait(events.AsSpan(), TimeSpan.Zero) != 0
            || socket.State != SrtSocketState.Connected)
        {
            throw new InvalidOperationException("Expected an idle connected socket.");
        }

        socket.GetOption(SrtOptions.ReceiveLatency);
        socket.GetSendBuffer();
    }
}
#endif
