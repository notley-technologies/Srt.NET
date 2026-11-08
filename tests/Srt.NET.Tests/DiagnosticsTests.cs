// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Net;
using System.Threading;
using Srt.Net;
using Xunit;

namespace Srt.Net.Tests;

public sealed class DiagnosticsTests : IClassFixture<RuntimeFixture>
{
    [Fact]
    public void StatisticsIncludeTrafficAndCanResetIntervalCounters()
    {
        using ConnectionPair pair = TestSockets.Connect();
        pair.Caller.Send([1, 2, 3], TestSockets.Deadline);
        pair.Peer.Receive(new byte[1500], TestSockets.Deadline);
        SrtStatistics first = pair.Peer.GetStatistics(clear: true, instantaneous: true);
        Assert.True(first.PacketsReceivedUniqueTotal >= 1);
        Assert.True(first.BytesReceivedUniqueTotal >= 3);
        Assert.True(first.RoundTripTimeMilliseconds >= 0);
        SrtStatistics second = pair.Peer.GetStatistics(instantaneous: true);
        Assert.Equal(first.PacketsReceivedUniqueTotal, second.PacketsReceivedUniqueTotal);
        Assert.Equal(0, second.PacketsReceivedUnique);
        Assert.True(pair.Caller.GetSendBuffer().Bytes < ulong.MaxValue);
    }

    [Fact]
    public void NonblockingReceiveLeavesTheConnectionUsable()
    {
        using ConnectionPair pair = TestSockets.Connect();
        byte[] bytes = new byte[1500];
        Assert.False(pair.Peer.TryReceive(bytes, out int empty));
        Assert.Equal(0, empty);
        Assert.True(pair.Caller.TrySend([7, 8], out int sent));
        Assert.Equal(2, sent);
        Assert.Equal(2, pair.Peer.Receive(bytes, TestSockets.Deadline));
        Assert.Equal(7, bytes[0]);
    }

    [Fact]
    public void MessageMetadataAndSegmentsRoundTrip()
    {
        using ConnectionPair pair = TestSockets.Connect();
        SrtMessageOptions options = new()
        {
            InOrder = true,
            TimeToLive = TimeSpan.FromSeconds(2)
        };
        SrtMessageResult sent = pair.Caller.SendMessage([99, 1, 2, 3, 99], 1, 3, options, TestSockets.Deadline);
        byte[] received = new byte[1500];
        SrtMessageResult result = pair.Peer.ReceiveMessage(received, 1, 1499, TestSockets.Deadline);
        Assert.Equal(3, result.BytesTransferred);
        Assert.Equal(sent.MessageNumber, result.MessageNumber);
        Assert.True(result.PacketSequenceNumber >= 0);
        Assert.Equal(0, received[0]);
        Assert.Equal(1, received[1]);
        Assert.True(SrtClock.NowMicroseconds > 0);
        Assert.True(pair.Caller.ConnectionTimeMicroseconds > 0);
        Assert.True(pair.Caller.ConnectionTimeMicroseconds <= SrtClock.NowMicroseconds);
        Assert.NotEmpty(SrtErrors.Describe(SrtErrorCode.ConnectionLost));
        Assert.NotEmpty(SrtErrors.DescribeRejection(SrtRejectReason.BadSecret));
        Assert.Throws<ArgumentOutOfRangeException>(() => pair.Caller.SendMessage([1], new() { TimeToLive = TimeSpan.FromMilliseconds(-1) }));
    }

    [Fact]
    public void LingerAndExpandedOptionsUseTheirNativeTypes()
    {
        using SrtSocket socket = new();
        socket.SetOption(SrtOptions.Linger, new(true, 2));
        SrtLinger linger = socket.GetOption(SrtOptions.Linger);
        Assert.True(linger.Enabled);
        Assert.Equal(2, linger.Seconds);
        socket.SetOption(SrtOptions.MinimumInputBandwidth, 3_000_000_000L);
        Assert.Equal(3_000_000_000L, socket.GetOption(SrtOptions.MinimumInputBandwidth));
        socket.SetOption(SrtOptions.KeyRefreshRate, 100000);
        Assert.Equal(100000, socket.GetOption(SrtOptions.KeyRefreshRate));
        socket.SetOption(SrtOptions.DriftTracer, false);
        Assert.False(socket.GetOption(SrtOptions.DriftTracer));
        socket.SetOption(SrtOptions.PacketFilter, "fec,cols:10,rows:5");
        Assert.Contains("cols:10", socket.GetOption(SrtOptions.PacketFilter));
    }

    [Fact]
    public void ConfiguredTransportOptionsCanBeReadBack()
    {
        using SrtSocket socket = new();
        socket.SetOption(SrtOptions.TimestampBasedDelivery, false);
        socket.SetOption(SrtOptions.SendDropDelay, 200);
        socket.SetOption(SrtOptions.CongestionController, "live");
        socket.SetOption(SrtOptions.MessageApi, true);
        socket.SetOption(SrtOptions.EnforcedEncryption, true);
        socket.SetOption(SrtOptions.PayloadSize, 1000);
        socket.SetOption(SrtOptions.ConnectTimeout, 1500);

        Assert.False(socket.GetOption(SrtOptions.TimestampBasedDelivery));
        Assert.Equal(200, socket.GetOption(SrtOptions.SendDropDelay));
        Assert.Equal("live", socket.GetOption(SrtOptions.CongestionController));
        Assert.True(socket.GetOption(SrtOptions.MessageApi));
        Assert.True(socket.GetOption(SrtOptions.EnforcedEncryption));
        Assert.Equal(1000, socket.GetOption(SrtOptions.PayloadSize));
        Assert.Equal(1500, socket.GetOption(SrtOptions.ConnectTimeout));
    }

    [Fact]
    public void LiveMessagesExposeSourceTimestamps()
    {
        using SrtSocket listener = new();
        using SrtSocket caller = new();
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        caller.Connect(listener.LocalEndPoint, TestSockets.Deadline);
        using SrtSocket peer = listener.Accept(TestSockets.Deadline);
        long timestamp = SrtClock.NowMicroseconds;
        SrtMessageOptions options = new()
        {
            SourceTimeMicroseconds = timestamp
        };
        SrtMessageResult sent = caller.SendMessage([1, 2, 3], options, TestSockets.Deadline);
        SrtMessageResult received = peer.ReceiveMessage(new byte[1500], TestSockets.Deadline);
        Assert.Equal(timestamp, sent.SourceTimeMicroseconds);
        Assert.True(received.SourceTimeMicroseconds > 0);
        Assert.Equal(sent.MessageNumber, received.MessageNumber);
        Assert.Equal(3, received.BytesTransferred);
    }

    [Fact]
    public void LoggingRunsOutsideNativeLocksAndContainsHandlerExceptions()
    {
        using ManualResetEventSlim delivered = new();
        InvalidOperationException failure = new("Test handler failure");
        SrtLogging.SetHandler(entry =>
        {
            SrtLogging.SetLevel(SrtLogLevel.Warning);
            delivered.Set();
            throw failure;
        });

        try
        {
            using SrtSocket invalid = new();
            Assert.Throws<SrtException>(() => invalid.Accept(TimeSpan.Zero));
            Assert.True(delivered.Wait(TestSockets.Deadline));
            Assert.True(SpinWait.SpinUntil(() => ReferenceEquals(failure, SrtLogging.LastHandlerException), TestSockets.Deadline));
            Assert.True(SrtLogging.DroppedMessages > 0);
        }
        finally
        {
            SrtLogging.SetHandler(null);
        }
    }

    [Fact]
    public void SendBufferErrorsReflectTheSocketStateNotAStaleError()
    {
        using SrtSocket unconnected = new();
        Assert.Equal(SrtErrorCode.NotConnected, Assert.Throws<SrtException>(() => unconnected.GetSendBuffer()).SrtErrorCode);

        using ConnectionPair pair = TestSockets.Connect();

        // Leave a would-block error on this thread, then let libsrt close the caller after its peer goes away.
        Assert.False(pair.Caller.TryReceive(new byte[1500], out _));
        pair.Peer.Dispose();
        SpinWait.SpinUntil(() => pair.Caller.State == SrtSocketState.Closed, TimeSpan.FromSeconds(10));

        Assert.Equal(SrtSocketState.Closed, pair.Caller.State);
        Assert.Equal(SrtErrorCode.InvalidSocket, Assert.Throws<SrtException>(() => pair.Caller.GetSendBuffer()).SrtErrorCode);
    }
}
