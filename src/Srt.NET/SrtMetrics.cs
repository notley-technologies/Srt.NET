// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
#if NET
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
#endif

namespace Srt.Net;

/// <summary>Connection metrics, published through the <c>Srt.Net</c> meter on .NET 8 and later.</summary>
/// <remarks>
/// Instrument names, units and the <c>error.type</c> attribute follow the OpenTelemetry semantic conventions used
/// by System.Net. Recording costs nothing until a listener (for example, OpenTelemetry's <c>AddMeter("Srt.Net")</c>)
/// enables an instrument. On .NET Standard 2.0 every method is a no-op.
/// </remarks>
internal static class SrtMetrics
{
    internal const string MeterName = "Srt.Net";
    internal const string CallerRole = "caller";
    internal const string ListenerRole = "listener";
    internal const string RendezvousRole = "rendezvous";

#if NET
    private const string RoleTag = "srt.connection.role";

    private static readonly Meter Meter = new(
        MeterName,
        typeof(SrtMetrics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    private static readonly Histogram<double> ConnectDuration = Meter.CreateHistogram<double>(
        "srt.connect.duration",
        "s",
        "Duration of outgoing SRT connection attempts, including the handshake.");

    private static readonly UpDownCounter<long> OpenConnections = Meter.CreateUpDownCounter<long>(
        "srt.open_connections",
        "{connection}",
        "Number of connected SRT sockets that have not been closed.");

    private static readonly Histogram<double> ConnectionDuration = Meter.CreateHistogram<double>(
        "srt.connection.duration",
        "s",
        "Time from connection establishment until the socket is closed.");

    /// <summary>True when a listener has enabled any instrument.</summary>
    internal static bool IsEnabled => ConnectDuration.Enabled || OpenConnections.Enabled || ConnectionDuration.Enabled;

    /// <summary>Records a finished connection attempt that was started at <paramref name="started"/>.</summary>
    internal static void ConnectCompleted(string role, long started, Exception? error)
    {
        if (!ConnectDuration.Enabled)
        {
            return;
        }

        TagList tags = new() { { RoleTag, role } };

        if (error != null)
        {
            tags.Add("error.type", GetErrorType(error));

            if (error is SrtException { RejectionReason: SrtRejectReason reason })
            {
                tags.Add("srt.reject_reason", GetRejectReasonTag(reason));
            }
        }

        ConnectDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
    }

    /// <summary>Counts a newly connected socket. Returns whether it was counted, so closing can balance it.</summary>
    internal static bool ConnectionOpened(string role)
    {
        if (!OpenConnections.Enabled)
        {
            return false;
        }

        OpenConnections.Add(1, new KeyValuePair<string, object?>(RoleTag, role));

        return true;
    }

    /// <summary>Records the close of a socket that was connected at <paramref name="connected"/>.</summary>
    internal static void ConnectionClosed(string role, long connected, bool counted)
    {
        KeyValuePair<string, object?> tag = new(RoleTag, role);

        if (counted)
        {
            OpenConnections.Add(-1, tag);
        }

        if (ConnectionDuration.Enabled)
        {
            ConnectionDuration.Record(Stopwatch.GetElapsedTime(connected).TotalSeconds, tag);
        }
    }

    /// <summary>Returns a low-cardinality tag value: the remote listener chooses codes of 1000 and above freely.</summary>
    private static string GetRejectReasonTag(SrtRejectReason reason)
    {
        if (Enum.IsDefined(reason))
        {
            return reason.ToString();
        }

        return reason >= SrtRejectReason.UserDefined ? nameof(SrtRejectReason.UserDefined) : "_OTHER";
    }

    private static string GetErrorType(Exception error)
    {
        return error switch
        {
            SrtException srt => srt.SrtErrorCode.ToString(),
            OperationCanceledException => "canceled",
            TimeoutException => "timeout",
            _ => error.GetType().FullName ?? "other"
        };
    }
#else
    internal static bool IsEnabled => false;

    internal static void ConnectCompleted(string role, long started, Exception? error)
    {
    }

    internal static bool ConnectionOpened(string role)
    {
        return false;
    }

    internal static void ConnectionClosed(string role, long connected, bool counted)
    {
    }
#endif
}
