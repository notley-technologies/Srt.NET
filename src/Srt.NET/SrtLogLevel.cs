// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net;

/// <summary>Native logging thresholds. Selecting a level includes more severe messages.</summary>
public enum SrtLogLevel
{
    /// <summary>Disable native logging.</summary>
    Off = 0,

    /// <summary>Fatal failures.</summary>
    Fatal = 2,

    /// <summary>Errors.</summary>
    Error = 3,

    /// <summary>Warnings.</summary>
    Warning = 4,

    /// <summary>Significant informational events.</summary>
    Notice = 5,

    /// <summary>Informational messages.</summary>
    Information = 6,

    /// <summary>Verbose diagnostic messages.</summary>
    Debug = 7
}
