// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;

namespace Srt.Net;

/// <summary>How long a closed socket keeps sending its queued data. Draining does not confirm application-level delivery.</summary>
/// <remarks>
/// When enabled, disposing a connected socket that still has queued data closes it in the background once libsrt has
/// sent and the peer has acknowledged that data, the connection breaks, or the timeout passes, whichever comes first.
/// Process exit and the unload of an application domain or collectible load context discard data that is still queued,
/// and the peer still sees an orderly close. The file profile enables linger for 180 seconds; the live profile disables
/// it.
/// </remarks>
public readonly struct SrtLinger
{
    /// <summary>Creates portable linger settings with a timeout of 0 through 65535 seconds.</summary>
    public SrtLinger(bool enabled, int seconds)
    {
        if (seconds < 0 || seconds > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        Enabled = enabled;
        Seconds = seconds;
    }

    /// <summary>Whether a closed socket keeps sending its queued data.</summary>
    public bool Enabled
    {
        get;
    }

    /// <summary>The longest time, in whole seconds, that a closed socket keeps sending its queued data.</summary>
    public int Seconds
    {
        get;
    }
}
