// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Srt.Net;
using Xunit;

namespace Srt.Net.Tests;

public sealed class SpanTests : IClassFixture<RuntimeFixture>
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void StackBuffersPreserveSlicesAndMessageMetadata(bool encrypted, bool fileMode)
    {
        using SrtSocket listener = new();
        using SrtSocket caller = new();
        SrtSocket[] sockets = [listener, caller];

        foreach (SrtSocket socket in sockets)
        {
            if (fileMode)
            {
                socket.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
                socket.SetOption(SrtOptions.MessageApi, true);
            }

            if (encrypted)
            {
                socket.SetOption(SrtOptions.Passphrase, "span test passphrase");
            }

            socket.SetOption(SrtOptions.ReceiveLatency, 20);
            socket.SetOption(SrtOptions.PeerLatency, 20);
        }

        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        caller.Connect(listener.LocalEndPoint, TestSockets.Deadline);
        using SrtSocket peer = listener.Accept(TestSockets.Deadline);
        Span<byte> source = stackalloc byte[8];
        source.Fill(42);
        Span<byte> received = stackalloc byte[1500];
        received.Fill(0xcc);

        Assert.Equal(3, caller.Send(source.Slice(2, 3), TestSockets.Deadline));
        Assert.Equal(3, peer.Receive(received.Slice(3, 1456), TestSockets.Deadline));
        Assert.True(received.Slice(3, 3).SequenceEqual(source.Slice(2, 3)));
        Assert.Equal(0xcc, received[2]);
        Assert.Equal(0xcc, received[6]);

        SrtMessageResult sent = caller.SendMessage(source.Slice(1, 4), timeout: TestSockets.Deadline);
        SrtMessageResult result = peer.ReceiveMessage(received, TestSockets.Deadline);
        Assert.Equal(4, result.BytesTransferred);
        Assert.Equal(sent.MessageNumber, result.MessageNumber);
        Assert.True(received.Slice(0, 4).SequenceEqual(source.Slice(1, 4)));

        Assert.False(peer.TryReceive(received, out int bytes));
        Assert.Equal(0, bytes);
        Assert.False(peer.TryReceiveMessage(received, out SrtMessageResult empty));
        Assert.Equal(default, empty);
        Assert.True(caller.TrySendMessage(source, out SrtMessageResult queued));
        Assert.Equal(source.Length, queued.BytesTransferred);
        using SrtPoller poller = new();
        poller.Add(peer, SrtPollEvents.Read);
        poller.Wait(TestSockets.Deadline);
        Assert.True(peer.TryReceiveMessage(received, out result));
        Assert.Equal(queued.MessageNumber, result.MessageNumber);
        Assert.Equal(source.Length, result.BytesTransferred);
    }

    [Fact]
    public unsafe void TransfersThroughUnmanagedMemory()
    {
        using ConnectionPair pair = TestSockets.Connect();
        IntPtr memory = Marshal.AllocHGlobal(1500);

        try
        {
            Span<byte> buffer = new((void*)memory, 1500);
            buffer.Fill(71);
            Assert.True(pair.Caller.TrySend(buffer.Slice(0, 32), out int sent));
            Assert.Equal(32, sent);
            buffer.Clear();
            Assert.Equal(32, pair.Peer.Receive(buffer, TestSockets.Deadline));

            for (int index = 0; index < 32; index++)
            {
                Assert.Equal(71, buffer[index]);
            }

            Assert.Equal(0, buffer[32]);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    [Fact]
    public void SpanValidationCancellationAndDisposalMatchArrayOperations()
    {
        using ConnectionPair pair = TestSockets.Connect();
        byte[] bytes = new byte[1500];
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => pair.Caller.Send(bytes.AsSpan(), cancellationToken: cancel.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => pair.Peer.ReceiveMessage(bytes.AsSpan(), cancellationToken: cancel.Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => pair.Caller.Send((ReadOnlySpan<byte>)[]));
        Assert.Throws<ArgumentOutOfRangeException>(() => pair.Peer.TryReceive((Span<byte>)[], out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => pair.Peer.Receive(bytes.AsSpan(), TimeSpan.FromMilliseconds(-2)));
        Assert.Throws<ArgumentNullException>(() => pair.Caller.TrySendMessage(null!, out _));

        pair.Peer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => pair.Peer.TryReceive(bytes.AsSpan(), out _));
        Assert.Throws<ObjectDisposedException>(() => pair.Peer.TryReceiveMessage(bytes.AsSpan(), out _));
    }

    [Fact]
    public async Task WaitingReceiveSurvivesCompactingCollections()
    {
        using ConnectionPair pair = TestSockets.Connect();
        byte[] bytes = new byte[1500];
        using ManualResetEventSlim started = new();
        Task<int> receive = Task.Run(() =>
        {
            started.Set();

            return pair.Peer.Receive(bytes.AsSpan(), TestSockets.Deadline);
        });
        Assert.True(started.Wait(TestSockets.Deadline));
        await Task.Delay(80);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        pair.Caller.Send([4, 5, 6], TestSockets.Deadline);
        Assert.Same(receive, await Task.WhenAny(receive, Task.Delay(TestSockets.Deadline)));
        Assert.Equal(3, await receive);
        Assert.Equal(4, bytes[0]);
        Assert.Equal(6, bytes[2]);
    }

    [Fact]
    public void BackpressureReturnsFalseAndLeavesTheConnectionUsable()
    {
        using ConnectionPair pair = TestSockets.Connect(64 * 1500);
        byte[] bytes = new byte[1316];
        bool sent = true;

        for (int attempt = 0; attempt < 20000 && sent; attempt++)
        {
            sent = pair.Caller.TrySend(bytes.AsSpan(), out int count);
            Assert.Equal(sent ? bytes.Length : 0, count);
        }

        Assert.False(sent);
        sent = true;

        for (int attempt = 0; attempt < 20000 && sent; attempt++)
        {
            sent = pair.Caller.TrySendMessage(bytes.AsSpan(), out SrtMessageResult result);
            Assert.Equal(sent ? bytes.Length : 0, result.BytesTransferred);
        }

        Assert.False(sent);
        Assert.Equal(bytes.Length, pair.Peer.Receive(bytes.AsSpan(), TestSockets.Deadline));
        Assert.Equal(SrtSocketState.Connected, pair.Caller.State);
    }
}
