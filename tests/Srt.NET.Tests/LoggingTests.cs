// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Linq;
using Srt.Net;
using Xunit;

namespace Srt.Net.Tests;

/// <summary>Covers the process-wide logging configuration. Each test restores libsrt's defaults afterwards.</summary>
public sealed class LoggingTests : IClassFixture<RuntimeFixture>, IDisposable
{
    private static readonly SrtLogArea[] AllAreas = [.. Enum.GetValues(typeof(SrtLogArea)).Cast<SrtLogArea>()];

    public void Dispose()
    {
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
}
