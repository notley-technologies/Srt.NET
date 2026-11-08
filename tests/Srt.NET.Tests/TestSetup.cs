// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System.Runtime.CompilerServices;
using Srt.Net.Interop;

namespace Srt.NET.Tests;

internal static class TestSetup
{
    /// <summary>Turns off the engine's watchdog for the whole test run.</summary>
    /// <remarks>
    /// The watchdog retries a queue that has waited 250 ms without a readiness edge. Left on, it would turn a lost edge into
    /// a slow but passing test. EngineTests turns it back on briefly to cover the backstop itself.
    /// </remarks>
    [ModuleInitializer]
    internal static void DisableEngineWatchdog()
    {
        SrtAsyncEngine.WatchdogMilliseconds = int.MaxValue;
    }
}
