// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Srt.Net;
using Xunit;

namespace Srt.Net.Tests;

public sealed class SocketTests : IClassFixture<RuntimeFixture>
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public void LoadsPinnedNativeVersion()
    {
        Assert.Equal(new Version(1, 5, 7), SrtSocket.NativeVersion);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TransfersLiveMessages(bool encrypted, bool ipv6)
    {
        using ConnectionPair pair = Connect(encrypted, ipv6);
        byte[] source = Encoding.UTF8.GetBytes("xxSRT message: café / 東京yy");
        byte[] received = [.. Enumerable.Repeat((byte)0xcc, 1600)];
        Assert.Equal(source.Length - 4, pair.Caller.Send(source, 2, source.Length - 4, Deadline));
        int length = pair.Peer.Receive(received, 3, 1500, Deadline);
        Assert.Equal(source.Skip(2).Take(source.Length - 4), received.Skip(3).Take(length));
        Assert.Equal(0xcc, received[2]);
        Assert.Equal(0xcc, received[3 + length]);
        Assert.Equal("#!::r=東京,café", pair.Peer.GetOption(SrtOptions.StreamId));
        Assert.Equal(pair.Caller.LocalEndPoint, pair.Peer.RemoteEndPoint);
        Assert.Equal(pair.Peer.LocalEndPoint, pair.Caller.RemoteEndPoint);

        if (encrypted)
        {
            Assert.Equal(SrtKeyMaterialState.Secured, pair.Caller.GetOption(SrtOptions.SendEncryptionState));
            Assert.Equal(SrtKeyMaterialState.Secured, pair.Peer.GetOption(SrtOptions.ReceiveEncryptionState));
        }

        byte[] reply = Encoding.UTF8.GetBytes("reply");
        pair.Peer.Send(reply, Deadline);
        Assert.Equal(reply.Length, pair.Caller.Receive(received, Deadline));
        Assert.Equal(reply, received.Take(reply.Length));
    }

    [Fact]
    public void PreservesMessageBoundaries()
    {
        using ConnectionPair pair = Connect();
        byte[] buffer = new byte[1500];
        pair.Caller.Send([1, 2], Deadline);
        pair.Caller.Send([3, 4, 5], Deadline);
        Assert.Equal(2, pair.Peer.Receive(buffer, Deadline));
        Assert.Equal(new byte[] { 1, 2 }, buffer.Take(2));
        Assert.Equal(3, pair.Peer.Receive(buffer, Deadline));
        Assert.Equal(new byte[] { 3, 4, 5 }, buffer.Take(3));
    }

    [Fact]
    public void TransfersAFileModeByteStream()
    {
        using ConnectionPair pair = Connect(fileStream: true);
        byte[] sent = [.. Enumerable.Range(0, 30000).Select(n => (byte)n)];
        byte[] received = new byte[sent.Length];
        int offset = 0;

        while (offset < sent.Length)
        {
            offset += pair.Caller.Send(sent, offset, sent.Length - offset, Deadline);
        }

        offset = 0;

        while (offset < received.Length)
        {
            offset += pair.Peer.Receive(received, offset, received.Length - offset, Deadline);
        }

        Assert.Equal(sent, received);
    }

    [Fact]
    public void MarshalsBooleanIntegerAnd64BitOptions()
    {
        using SrtSocket socket = new();
        socket.SetOption(SrtOptions.ReuseAddress, false);
        Assert.False(socket.GetOption(SrtOptions.ReuseAddress));
        socket.SetOption(SrtOptions.ReuseAddress, true);
        Assert.True(socket.GetOption(SrtOptions.ReuseAddress));
        socket.SetOption(SrtOptions.ReceiveLatency, 250);
        Assert.Equal(250, socket.GetOption(SrtOptions.ReceiveLatency));
        socket.SetOption(SrtOptions.MaximumBandwidth, 5_000_000_000L);
        Assert.Equal(5_000_000_000L, socket.GetOption(SrtOptions.MaximumBandwidth));
    }

    [Fact]
    public void ValidatesUtf8ByteLimitsAndOptionDirection()
    {
        using SrtSocket socket = new();
        string maximum = new('é', 256);
        socket.SetOption(SrtOptions.StreamId, maximum);
        Assert.Equal(maximum, socket.GetOption(SrtOptions.StreamId));
        Assert.Throws<ArgumentException>(() => socket.SetOption(SrtOptions.StreamId, maximum + "x"));
        Assert.Throws<ArgumentException>(() => socket.SetOption(SrtOptions.StreamId, "a\0b"));
        Assert.Throws<ArgumentException>(() => socket.GetOption(SrtOptions.Passphrase));
        Assert.Throws<ArgumentException>(() => socket.SetOption(SrtOptions.PeerVersion, 1));
        socket.SetOption(SrtOptions.StreamId, "");
        Assert.Equal("", socket.GetOption(SrtOptions.StreamId));
    }

    [Fact]
    public void PreservesNativeErrorsWithoutIncludingOptionValues()
    {
        using SrtSocket socket = new();
        SrtException error = Assert.Throws<SrtException>(() => socket.SetOption(SrtOptions.Passphrase, "secret"));
        Assert.Equal(SrtErrorCode.InvalidParameter, error.SrtErrorCode);
        Assert.DoesNotContain("secret", error.Message);
        Assert.Equal(SrtSocketState.Initialized, socket.State);
    }

    [Fact]
    public void AcceptsTheFullNativePassphraseLength()
    {
        using SrtSocket socket = new();
        socket.SetOption(SrtOptions.Passphrase, new string('é', 40));
        Assert.Throws<ArgumentException>(() => socket.SetOption(SrtOptions.Passphrase, new string('é', 41)));
        socket.SetOption(SrtOptions.Passphrase, "");
    }

    [Fact]
    public async Task ConnectsRendezvousPeers()
    {
        using SrtSocket first = new();
        using SrtSocket second = new();
        SrtSocket[] sockets = [first, second];

        foreach (SrtSocket socket in sockets)
        {
            socket.SetOption(SrtOptions.Rendezvous, true);
            socket.Bind(new(IPAddress.Loopback, 0));
        }

        IPEndPoint firstAddress = first.LocalEndPoint;
        IPEndPoint secondAddress = second.LocalEndPoint;
        await Task.WhenAll(
            Task.Run(() => first.Connect(secondAddress, Deadline)),
            Task.Run(() => second.Connect(firstAddress, Deadline)));
        first.Send([123], Deadline);
        byte[] received = new byte[1500];
        Assert.Equal(1, second.Receive(received, Deadline));
        Assert.Equal(123, received[0]);
    }

    [Fact]
    public void PreCanceledConnectDoesNotStartAHandshake()
    {
        using SrtSocket socket = new();
        using CancellationTokenSource cancel = new();
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => socket.Connect(
            new(IPAddress.Loopback, 9000), cancellationToken: cancel.Token));
        Assert.Equal(SrtSocketState.Initialized, socket.State);
    }

    [Fact]
    public void ReceiveTimeoutLeavesTheSocketUsable()
    {
        using ConnectionPair pair = Connect();
        byte[] buffer = new byte[1500];
        Assert.Throws<TimeoutException>(() => pair.Peer.Receive(buffer, TimeSpan.FromMilliseconds(80)));
        pair.Caller.Send([42], Deadline);
        Assert.Equal(1, pair.Peer.Receive(buffer, Deadline));
        Assert.Equal(42, buffer[0]);
    }

    [Fact]
    public async Task CancellationInterruptsReceiveWithoutClosingTheSocket()
    {
        using ConnectionPair pair = Connect();
        using CancellationTokenSource cancel = new();
        Task receive = Task.Run(() => pair.Peer.Receive(new byte[1500], cancellationToken: cancel.Token));
        cancel.CancelAfter(80);
        await AssertCompletes(receive);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receive);
        Assert.Equal(SrtSocketState.Connected, pair.Peer.State);
    }

    [Fact]
    public async Task DisposalInterruptsPendingReceive()
    {
        using ConnectionPair pair = Connect();
        using ManualResetEventSlim started = new();
        Task receive = Task.Run(() =>
        {
            started.Set();
            pair.Peer.Receive(new byte[1500]);
        });
        Assert.True(started.Wait(Deadline));
        await Task.Delay(80);
        pair.Peer.Dispose();
        await AssertCompletes(receive);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => receive);
        pair.Peer.Dispose();
        Assert.Equal(SrtSocketState.Closed, pair.Peer.State);
    }

    [Fact]
    public void AcceptSupportsTimeoutAndCancellation()
    {
        using SrtSocket listener = new();
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        Assert.Throws<TimeoutException>(() => listener.Accept(TimeSpan.FromMilliseconds(50)));
        using CancellationTokenSource cancel = new(80);
        Assert.ThrowsAny<OperationCanceledException>(() => listener.Accept(cancellationToken: cancel.Token));
        Assert.Equal(SrtSocketState.Listening, listener.State);
    }

    [Fact]
    public void ValidatesBufferRangesAndDisposedAccess()
    {
        using SrtSocket socket = new();
        Assert.Throws<ArgumentNullException>(() => socket.Send(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => socket.Send(new byte[2], -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => socket.Receive(new byte[2], 1, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => socket.Send([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => socket.Receive(new byte[1500], TimeSpan.FromMilliseconds(-2)));
        socket.Dispose();
        Assert.Throws<ObjectDisposedException>(() => socket.Bind(new(IPAddress.Loopback, 0)));
        Assert.Throws<ObjectDisposedException>(() => socket.GetOption(SrtOptions.ReuseAddress));
    }

    [Fact]
    public void RejectedEncryptedHandshakeReportsReasonAndClosesCaller()
    {
        using SrtSocket listener = new();
        using SrtSocket caller = new();
        listener.SetOption(SrtOptions.Passphrase, "listener password");
        caller.SetOption(SrtOptions.Passphrase, "different password");
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        SrtException error = Assert.Throws<SrtException>(() => caller.Connect(listener.LocalEndPoint, Deadline));
        Assert.Equal(SrtErrorCode.ConnectionRejected, error.SrtErrorCode);
        Assert.Equal(SrtRejectReason.BadSecret, error.RejectionReason);
        Assert.Equal(SrtSocketState.Closed, caller.State);
    }

    [Fact]
    public void ListenAndAdmissionSetupValidateTheirPreconditions()
    {
        using SrtSocket listener = new();
        Assert.Throws<ArgumentNullException>("endpoint", () => listener.Bind(null!));
        Assert.Throws<ArgumentOutOfRangeException>("backlog", () => listener.Listen(0));

        // libsrt refuses to listen on a socket that is not bound.
        Assert.Throws<SrtException>(() => listener.Listen());

        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        Assert.Equal(SrtSocketState.Listening, listener.State);
        Assert.Throws<InvalidOperationException>(() => listener.SetConnectionHandler(_ => true));
    }

    [Fact]
    public void ZeroTimeoutsFailFastWhenNothingIsReady()
    {
        using SrtSocket listener = new();
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        Assert.Throws<TimeoutException>(() => listener.Accept(TimeSpan.Zero));
        Assert.Equal(SrtSocketState.Listening, listener.State);

        using ConnectionPair pair = Connect();
        byte[] buffer = new byte[1500];
        Assert.Throws<TimeoutException>(() => pair.Peer.Receive(buffer, TimeSpan.Zero));
        Assert.Throws<TimeoutException>(() => pair.Peer.ReceiveMessage(buffer, TimeSpan.Zero));
        pair.Caller.Send([9], Deadline);
        Assert.Equal(1, pair.Peer.Receive(buffer, Deadline));
        Assert.Equal(9, buffer[0]);
    }

    [Fact]
    public void AZeroTimeoutConnectFailsAndDisposesTheCaller()
    {
        // A peer that never answers, so the handshake cannot complete.
        using UdpClient silent = new(new IPEndPoint(IPAddress.Loopback, 0));
        using SrtSocket caller = new();
        Assert.Throws<TimeoutException>(() => caller.Connect((IPEndPoint)silent.Client.LocalEndPoint!, TimeSpan.Zero));
        Assert.Equal(SrtSocketState.Closed, caller.State);
        Assert.Throws<ObjectDisposedException>(() => caller.Send([1]));
    }

    [Fact]
    public async Task AcceptWaitsForAConnectionThatArrivesLater()
    {
        using SrtSocket listener = new();
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        Task<SrtSocket> accepting = Task.Run(() => listener.Accept(Deadline));

        // Connect only once Accept is blocked, so it must be woken by the connection's readiness edge.
        Assert.True(SpinWait.SpinUntil(() => accepting.IsCompleted || listener.AsyncContext?.ReceiveQueue.IsWaiting == true, Deadline));
        Assert.False(accepting.IsCompleted);

        using SrtSocket caller = new();
        caller.Connect(listener.LocalEndPoint, Deadline);
        await AssertCompletes(accepting);
        using SrtSocket peer = await accepting;
        Assert.Equal(caller.LocalEndPoint, peer.RemoteEndPoint);
        Assert.Equal(SrtSocketState.Connected, peer.State);
    }

    [Fact]
    public void ConnectingAListeningSocketFailsAndDisposesIt()
    {
        using SrtSocket target = new();
        target.Bind(new(IPAddress.Loopback, 0));
        target.Listen();

        SrtSocket listener = new();
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        Assert.Throws<SrtException>(() => listener.BeginConnect(target.LocalEndPoint));
        Assert.Equal(SrtSocketState.Closed, listener.State);
    }

    [Fact]
    public async Task ConnectAsyncOnAListeningSocketFaultsAndDisposesIt()
    {
        using SrtSocket target = new();
        target.Bind(new(IPAddress.Loopback, 0));
        target.Listen();

        SrtSocket listener = new();
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        ValueTask connecting = listener.ConnectAsync(target.LocalEndPoint);
        Assert.True(connecting.IsCompleted);
        await Assert.ThrowsAsync<SrtException>(async () => await connecting);
        Assert.Equal(SrtSocketState.Closed, listener.State);
    }

    [Fact]
    public void RejectsNegativeMessageSourceTimes()
    {
        using ConnectionPair pair = Connect();
        SrtMessageOptions options = new()
        {
            SourceTimeMicroseconds = -1
        };
        Assert.Throws<ArgumentOutOfRangeException>("options", () => pair.Caller.SendMessage([1], options, Deadline));
        Assert.Throws<ArgumentOutOfRangeException>("options", () => pair.Caller.SendMessageAsync(new byte[] { 1 }, options));
    }

    [Fact]
    public void FailuresOnABrokenConnectionNameTheOperation()
    {
        using ConnectionPair pair = Connect();
        pair.Caller.Dispose();
        Assert.True(SpinWait.SpinUntil(() => pair.Peer.State != SrtSocketState.Connected, Deadline));
        byte[] buffer = new byte[1500];

        Assert.Contains("send SRT data", Assert.Throws<SrtException>(() => pair.Peer.Send([1], Deadline)).Message, StringComparison.Ordinal);
        Assert.Contains("send an SRT message", Assert.Throws<SrtException>(() => pair.Peer.SendMessage([1], timeout: Deadline)).Message, StringComparison.Ordinal);
        Assert.Contains("receive an SRT message", Assert.Throws<SrtException>(() => pair.Peer.ReceiveMessage(buffer, Deadline)).Message, StringComparison.Ordinal);
        Assert.Contains("receive SRT data", Assert.Throws<SrtException>(() => pair.Peer.Receive(buffer, Deadline)).Message, StringComparison.Ordinal);
    }

    private static async Task AssertCompletes(Task task)
    {
        Assert.Same(task, await Task.WhenAny(task, Task.Delay(Deadline)));
    }

    private static ConnectionPair Connect(bool encrypted = false, bool ipv6 = false, bool fileStream = false)
    {
        using SrtSocket listener = new();
        SrtSocket caller = new();

        try
        {
            SrtSocket[] sockets = [listener, caller];

            foreach (SrtSocket socket in sockets)
            {
                if (fileStream)
                {
                    socket.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
                    socket.SetOption(SrtOptions.MessageApi, false);
                }

                if (encrypted)
                {
                    socket.SetOption(SrtOptions.Passphrase, "test passphrase 123");
                }

                socket.SetOption(SrtOptions.ReceiveLatency, 20);
                socket.SetOption(SrtOptions.PeerLatency, 20);
            }

            caller.SetOption(SrtOptions.StreamId, "#!::r=東京,café");
            listener.Bind(new(ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, 0));
            listener.Listen();
            caller.Connect(listener.LocalEndPoint, Deadline);

            return new ConnectionPair(caller, listener.Accept(Deadline));
        }
        catch
        {
            caller.Dispose();
            throw;
        }
    }

}
