// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net;

/// <summary>The transmission profile applied by libsrt.</summary>
public enum SrtTransmissionType
{
    /// <summary>Timed messages for live media, with a default payload limit of 1316 bytes.</summary>
    Live = 0,

    /// <summary>Reliable bulk transfer. Uses a byte stream unless MessageApi is enabled afterward.</summary>
    File = 1
}
