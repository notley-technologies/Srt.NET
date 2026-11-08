// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net;

/// <summary>A socket and its readiness flags at the time of a poll.</summary>
public readonly struct SrtPollEvent
{
    internal SrtPollEvent(SrtSocket socket, SrtPollEvents events)
    {
        Socket = socket;
        Events = events;
    }

    /// <summary>The registered socket.</summary>
    public SrtSocket Socket
    {
        get;
    }

    /// <summary>The readiness or closure flags.</summary>
    public SrtPollEvents Events
    {
        get;
    }
}
