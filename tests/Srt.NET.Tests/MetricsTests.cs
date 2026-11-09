// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

#if NET
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Srt.Net;
using Xunit;

namespace Srt.Net.Tests;

/// <summary>Verifies the instruments of the Srt.Net meter (tests run one at a time, so measurements are isolated).</summary>
public sealed class MetricsTests : IClassFixture<RuntimeFixture>
{
    private const string Role = "srt.connection.role";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ConnectionsAreTimedCountedAndBalancedOnClose()
    {
        using MetricsRecorder metrics = new();
        using SrtSocket listener = Listen();
        SrtSocket caller = new();
        using CancellationTokenSource timeout = new(Deadline);

        Task<SrtSocket> accepting = listener.AcceptAsync(timeout.Token).AsTask();
        await caller.ConnectAsync(listener.LocalEndPoint, timeout.Token);
        SrtSocket peer = await accepting;

        RecordedMeasurement connect = Assert.Single(metrics.Get("srt.connect.duration"));
        Assert.Equal("caller", connect.Tags[Role]);
        Assert.False(connect.Tags.ContainsKey("error.type"));
        Assert.True(connect.Value > 0);
        Assert.Equal(1, metrics.Sum("srt.open_connections", "caller"));
        Assert.Equal(1, metrics.Sum("srt.open_connections", "listener"));

        peer.Dispose();
        caller.Dispose();

        Assert.Equal(0, metrics.Sum("srt.open_connections", "caller"));
        Assert.Equal(0, metrics.Sum("srt.open_connections", "listener"));
        Assert.Equal(["caller", "listener"], metrics.Get("srt.connection.duration").Select(item => (string)item.Tags[Role]!).Order());
    }

    [Fact]
    public void RejectedConnectionsRecordTheErrorTypeAndReason()
    {
        using MetricsRecorder metrics = new();
        using SrtSocket listener = new();
        listener.SetConnectionHandler(request =>
        {
            request.SetRejectionReason(SrtRejectReason.Forbidden);

            return false;
        });
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        SrtSocket caller = new();

        Assert.Throws<SrtException>(() => caller.Connect(listener.LocalEndPoint, Deadline));

        RecordedMeasurement connect = Assert.Single(metrics.Get("srt.connect.duration"));
        Assert.Equal("caller", connect.Tags[Role]);
        Assert.Equal(nameof(SrtErrorCode.ConnectionRejected), connect.Tags["error.type"]);
        Assert.Equal(nameof(SrtRejectReason.Forbidden), connect.Tags["srt.reject_reason"]);
        Assert.Empty(metrics.Get("srt.open_connections"));
    }

    [Theory]
    [InlineData(2042, "UserDefined")]
    [InlineData(1410, "_OTHER")]
    [InlineData(1403, "Forbidden")]
    public void RejectReasonsChosenByThePeerUseABoundedSetOfTagValues(int code, string expected)
    {
        using MetricsRecorder metrics = new();
        using SrtSocket listener = new();
        listener.SetConnectionHandler(request =>
        {
            request.SetRejectionReason((SrtRejectReason)code);

            return false;
        });
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        SrtSocket caller = new();

        Assert.Throws<SrtException>(() => caller.Connect(listener.LocalEndPoint, Deadline));
        Assert.Equal(expected, Assert.Single(metrics.Get("srt.connect.duration")).Tags["srt.reject_reason"]);
    }

    [Fact]
    public void PolledConnectionsAreRecordedOnceHoweverOftenTheyAreCompleted()
    {
        using MetricsRecorder metrics = new();
        using SrtSocket listener = Listen();
        using SrtSocket caller = new();
        using SrtPoller poller = new();

        poller.Add(caller, SrtPollEvents.Connect);
        caller.BeginConnect(listener.LocalEndPoint);
        Assert.Single(poller.Wait(Deadline));
        caller.CompleteConnect();
        caller.CompleteConnect();
        using SrtSocket peer = listener.Accept(Deadline);

        Assert.Single(metrics.Get("srt.connect.duration"));
        Assert.Equal(1, metrics.Sum("srt.open_connections", "caller"));
    }

    [Fact]
    public void ConnectionsOpenedBeforeListeningAreNotBalancedLater()
    {
        // .NET 8 keeps an instrument enabled after its last listener is disposed, so once an earlier test has listened,
        // a connection can no longer be made with the instruments disabled.
        Assert.SkipWhen(SrtMetrics.IsEnabled, "The runtime still reports the instruments as enabled after an earlier listener.");

        using ConnectionPair pair = TestSockets.Connect();
        using MetricsRecorder metrics = new();

        pair.Peer.Dispose();
        pair.Caller.Dispose();

        // A listener that starts in the middle of a connection must not see a decrement without its increment.
        Assert.Empty(metrics.Get("srt.open_connections"));
        Assert.Empty(metrics.Get("srt.connection.duration"));
    }

    [Fact]
    public async Task TimedOutAndInterruptedConnectsRecordTheirErrorType()
    {
        using MetricsRecorder metrics = new();

        // A peer that never answers keeps the handshake pending until the timeout or until the caller is disposed.
        using UdpClient silent = new(new IPEndPoint(IPAddress.Loopback, 0));
        SrtSocket timedOut = new();
        Assert.Throws<TimeoutException>(() => timedOut.Connect((IPEndPoint)silent.Client.LocalEndPoint!, TimeSpan.Zero));

        SrtSocket interrupted = new();
        Task connecting = Task.Run(() => interrupted.Connect((IPEndPoint)silent.Client.LocalEndPoint!, Deadline));
        Assert.True(SpinWait.SpinUntil(() => connecting.IsCompleted || interrupted.AsyncContext?.SendQueue.IsWaiting == true, Deadline));
        interrupted.Dispose();
        Assert.Same(connecting, await Task.WhenAny(connecting, Task.Delay(Deadline)));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => connecting);

        Assert.Equal(
            ["System.ObjectDisposedException", "timeout"],
            metrics.Get("srt.connect.duration").Select(item => (string)item.Tags["error.type"]!).Order());
        Assert.Empty(metrics.Get("srt.open_connections"));
    }

    [Fact]
    public async Task InstrumentsCanBeEnabledIndividually()
    {
        using SrtSocket listener = Listen();
        using CancellationTokenSource timeout = new(Deadline);

        using (MetricsRecorder openOnly = new("srt.open_connections"))
        {
            (SrtSocket caller, SrtSocket peer) = await ConnectAsync(listener, timeout.Token);
            Assert.Empty(openOnly.Get("srt.connect.duration"));
            Assert.Equal(1, openOnly.Sum("srt.open_connections", "caller"));
            peer.Dispose();
            caller.Dispose();
            Assert.Equal(0, openOnly.Sum("srt.open_connections", "caller"));
            Assert.Empty(openOnly.Get("srt.connection.duration"));
        }

        using (MetricsRecorder connectOnly = new("srt.connect.duration"))
        {
            (SrtSocket caller, SrtSocket peer) = await ConnectAsync(listener, timeout.Token);
            Assert.Equal("caller", Assert.Single(connectOnly.Get("srt.connect.duration")).Tags[Role]);
            peer.Dispose();
            caller.Dispose();
            Assert.Empty(connectOnly.Get("srt.open_connections"));
            Assert.Empty(connectOnly.Get("srt.connection.duration"));
        }
    }

    private static async Task<(SrtSocket Caller, SrtSocket Peer)> ConnectAsync(SrtSocket listener, CancellationToken cancellationToken)
    {
        Task<SrtSocket> accepting = listener.AcceptAsync(cancellationToken).AsTask();
        SrtSocket caller = new();
        await caller.ConnectAsync(listener.LocalEndPoint, cancellationToken);

        return (caller, await accepting);
    }

    private static SrtSocket Listen()
    {
        SrtSocket listener = new();
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();

        return listener;
    }

    private sealed record RecordedMeasurement(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);

    private sealed class MetricsRecorder : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly ConcurrentQueue<RecordedMeasurement> measurements = new();

        internal MetricsRecorder(string? only = null)
        {
            listener.InstrumentPublished = (instrument, owner) =>
            {
                if (instrument.Meter.Name == "Srt.Net" && (only == null || instrument.Name == only))
                {
                    owner.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
            listener.Start();
        }

        internal List<RecordedMeasurement> Get(string instrument)
        {
            return [.. measurements.Where(item => item.Instrument == instrument)];
        }

        internal double Sum(string instrument, string role)
        {
            return Get(instrument).Where(item => (string?)item.Tags[Role] == role).Sum(item => item.Value);
        }

        public void Dispose()
        {
            listener.Dispose();
        }

        private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            Dictionary<string, object?> copy = [];

            foreach (KeyValuePair<string, object?> tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            measurements.Enqueue(new(instrument.Name, value, copy));
        }
    }
}
#endif
