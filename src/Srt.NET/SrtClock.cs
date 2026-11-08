// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using Srt.Net.Interop;

namespace Srt.Net;

/// <summary>The native clock used for message source timestamps.</summary>
public static class SrtClock
{
    /// <summary>Current native time in microseconds. Use this clock, rather than DateTime ticks, for message timestamps.</summary>
    public static long NowMicroseconds => NativeMethods.srt_time_now();
}
