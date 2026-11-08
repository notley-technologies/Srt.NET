// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;

namespace Srt.Net;

/// <summary>Optional omissions from the native formatted log text.</summary>
[Flags]
public enum SrtLogFormat
{
    /// <summary>Keep all native formatting.</summary>
    Default = 0,

    /// <summary>Omit the timestamp.</summary>
    OmitTime = 1,

    /// <summary>Omit the thread name.</summary>
    OmitThreadName = 2,

    /// <summary>Omit severity labels.</summary>
    OmitSeverity = 4,

    /// <summary>Omit the trailing newline.</summary>
    OmitNewline = 8
}
