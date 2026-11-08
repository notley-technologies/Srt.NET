// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;

namespace Srt.Net;

/// <summary>Optional delivery controls for a single message. The default value uses libsrt's defaults.</summary>
public readonly struct SrtMessageOptions
{
    /// <summary>Creates message options. Use this constructor from C# versions without init accessors, such as .NET Framework's default C# 7.3.</summary>
    /// <param name="timeToLive">Maximum time allowed to deliver the message, or null for no limit.</param>
    /// <param name="inOrder">Require ordered delivery in file message mode.</param>
    /// <param name="sourceTimeMicroseconds">Source time from <see cref="SrtClock"/>, in microseconds; zero lets libsrt assign the time.</param>
    public SrtMessageOptions(TimeSpan? timeToLive, bool inOrder = false, long sourceTimeMicroseconds = 0)
    {
        TimeToLive = timeToLive;
        InOrder = inOrder;
        SourceTimeMicroseconds = sourceTimeMicroseconds;
    }

    /// <summary>Maximum time allowed to deliver the message, or null for no limit.</summary>
    public TimeSpan? TimeToLive
    {
        get; init;
    }

    /// <summary>Require ordered delivery in file message mode. Live and stream modes ignore this setting.</summary>
    public bool InOrder
    {
        get; init;
    }

    /// <summary>Source time from <see cref="SrtClock"/>, in microseconds; zero lets libsrt assign the time.</summary>
    public long SourceTimeMicroseconds
    {
        get; init;
    }
}
