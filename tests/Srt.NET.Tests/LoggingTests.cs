// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Srt.Net;
using Srt.Net.Interop;
using Xunit;

namespace Srt.Net.Tests;

/// <summary>Covers the process-wide logging configuration. Each test restores libsrt's defaults afterwards.</summary>
public sealed class LoggingTests : IClassFixture<RuntimeFixture>, IDisposable
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static readonly SrtLogArea[] AllAreas = [.. Enum.GetValues(typeof(SrtLogArea)).Cast<SrtLogArea>()];

    public void Dispose()
    {
        SrtLogging.SetHandler(null);

        // libsrt starts with every functional area enabled, full formatting, and warnings and above.
        SrtLogging.SetAreas(AllAreas);
        SrtLogging.SetFormat(SrtLogFormat.Default);
        SrtLogging.SetLevel(SrtLogLevel.Warning);
    }

    [Theory]
    [InlineData(SrtLogFormat.Default)]
    [InlineData(SrtLogFormat.OmitTime)]
    [InlineData(SrtLogFormat.OmitThreadName)]
    [InlineData(SrtLogFormat.OmitSeverity)]
    [InlineData(SrtLogFormat.OmitNewline)]
    [InlineData(SrtLogFormat.OmitTime | SrtLogFormat.OmitThreadName | SrtLogFormat.OmitSeverity | SrtLogFormat.OmitNewline)]
    public void SetFormatAcceptsEachFlagAndCombinations(SrtLogFormat format)
    {
        SrtLogging.SetFormat(format);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void SetFormatRejectsUnknownFlags(int format)
    {
        Assert.Throws<ArgumentOutOfRangeException>("format", () => SrtLogging.SetFormat((SrtLogFormat)format));
    }

    [Fact]
    public void EnableAndDisableAcceptEveryDefinedArea()
    {
        foreach (SrtLogArea area in AllAreas)
        {
            SrtLogging.DisableArea(area);
            SrtLogging.EnableArea(area);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(1000)]
    public void EnableAndDisableRejectUndefinedAreas(int area)
    {
        Assert.Throws<ArgumentOutOfRangeException>("area", () => SrtLogging.EnableArea((SrtLogArea)area));
        Assert.Throws<ArgumentOutOfRangeException>("area", () => SrtLogging.DisableArea((SrtLogArea)area));
    }

    [Fact]
    public void SetAreasAcceptsEmptySomeAndAllAreas()
    {
        SrtLogging.SetAreas();
        SrtLogging.SetAreas(SrtLogArea.General, SrtLogArea.Connection);
        SrtLogging.SetAreas(AllAreas);
    }

    [Fact]
    public void SetAreasRejectsNullAndUndefinedAreas()
    {
        Assert.Throws<ArgumentNullException>("areas", () => SrtLogging.SetAreas(null!));
        Assert.Throws<ArgumentOutOfRangeException>("area", () => SrtLogging.SetAreas(SrtLogArea.General, (SrtLogArea)9));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(-1)]
    public void SetLevelRejectsUndefinedLevels(int level)
    {
        Assert.Throws<ArgumentOutOfRangeException>("level", () => SrtLogging.SetLevel((SrtLogLevel)level));
    }

    [Fact]
    public void TheHandlerReceivesNativeRecordsOffTheCallingThread()
    {
        ConcurrentQueue<(SrtLogEntry Entry, int Thread)> entries = new();
        SrtLogging.SetHandler(entry => entries.Enqueue((entry, Environment.CurrentManagedThreadId)));
        int socket = LogAcceptError();

        Assert.True(SpinWait.SpinUntil(() => entries.Any(item => Mentions(item.Entry, socket)), Deadline), Describe(entries.Select(item => item.Entry)));
        (SrtLogEntry entry, int thread) = entries.First(item => Mentions(item.Entry, socket));
        Assert.Equal(SrtLogLevel.Error, entry.Level);
        Assert.NotEqual(Environment.CurrentManagedThreadId, thread);
        Assert.False(string.IsNullOrEmpty(entry.Area));
        Assert.True(entry.Line >= 0);
        Assert.NotNull(entry.File);
    }

    [Fact]
    public void DisabledAreasProduceNoRecords()
    {
        ConcurrentQueue<SrtLogEntry> entries = new();
        SrtLogging.SetHandler(entries.Enqueue);
        SrtLogging.SetAreas();
        int silenced = LogAcceptError();
        SrtLogging.SetAreas(AllAreas);
        int reported = LogAcceptError();

        // Records are delivered in order, so a record for the first error would arrive before the second one's.
        Assert.True(SpinWait.SpinUntil(() => entries.Any(entry => Mentions(entry, reported)), Deadline), Describe(entries));
        Assert.DoesNotContain(entries, entry => Mentions(entry, silenced));
    }

    [Fact]
    public void AFullQueueDropsNewRecordsUntilTheHandlerCatchesUp()
    {
        using ManualResetEventSlim entered = new();
        using ManualResetEventSlim release = new();
        int delivered = 0;
        SrtLogging.SetHandler(_ =>
        {
            entered.Set();
            release.Wait(Deadline);
            Interlocked.Increment(ref delivered);
        });

        try
        {
            LogAcceptError();
            Assert.True(entered.Wait(Deadline));
            long dropped = SrtLogging.DroppedMessages;

            // libsrt calls the handler synchronously, so each failed accept has queued its record when it returns.
            for (int index = 0; index < 1100; index++)
            {
                LogAcceptError();
            }

            Assert.True(SrtLogging.DroppedMessages > dropped);
        }
        finally
        {
            release.Set();
        }

        // The queued records are still delivered: the blocked one and a full queue of 1024.
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref delivered) >= 1025, Deadline), $"Delivered {Volatile.Read(ref delivered)} records.");
    }

    [Fact]
    public void DetachingForExitRemovesTheHandlerAndDropsWhatItCannotDeliver()
    {
        using ManualResetEventSlim entered = new();
        using ManualResetEventSlim release = new();
        int calls = 0;
        SrtLogging.SetHandler(_ =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            release.Wait(Deadline);
        });

        try
        {
            LogAcceptError();
            Assert.True(entered.Wait(Deadline));
            LogAcceptError();
            long dropped = SrtLogging.DroppedMessages;

            // The handler is still busy, so detaching gives up waiting for it and drops the queued record.
            SrtLogging.DetachForExit();
            Assert.True(SrtLogging.DroppedMessages > dropped);
        }
        finally
        {
            release.Set();
        }

        // Detaching again is harmless, and a new handler can be installed afterwards.
        SrtLogging.DetachForExit();
        ConcurrentQueue<SrtLogEntry> entries = new();
        SrtLogging.SetHandler(entries.Enqueue);
        int socket = LogAcceptError();
        Assert.True(SpinWait.SpinUntil(() => entries.Any(entry => Mentions(entry, socket)), Deadline), Describe(entries));
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    /// <summary>Makes libsrt log one error that names a new socket, and returns that socket's id.</summary>
    private static int LogAcceptError()
    {
        using SrtSocket socket = new();
        Assert.Throws<SrtException>(() => socket.Accept(TimeSpan.Zero));

        return socket.Handle.DangerousGetHandle().ToInt32();
    }

    private static bool Mentions(SrtLogEntry entry, int socket)
    {
        return Regex.IsMatch(entry.Message, "@" + socket.ToString(CultureInfo.InvariantCulture) + @"(?!\d)");
    }

    private static string Describe(IEnumerable<SrtLogEntry> entries)
    {
        return string.Join(" | ", entries.Select(entry => $"{entry.Level} [{entry.Area}] {entry.File}:{entry.Line} {entry.Message}"));
    }
}
