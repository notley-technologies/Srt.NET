// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net;

/// <summary>The amount of data still queued for transmission.</summary>
public readonly struct SrtSendBuffer
{
    internal SrtSendBuffer(ulong blocks, ulong bytes)
    {
        Blocks = blocks;
        Bytes = bytes;
    }

    /// <summary>The number of queued blocks.</summary>
    public ulong Blocks
    {
        get;
    }

    /// <summary>The number of queued bytes.</summary>
    public ulong Bytes
    {
        get;
    }
}
