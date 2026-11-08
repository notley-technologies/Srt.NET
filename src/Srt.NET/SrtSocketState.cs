// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net;

/// <summary>The current native socket state.</summary>
public enum SrtSocketState
{
    /// <summary>Created, with no local address.</summary>
    Initialized = 1,

    /// <summary>Bound to a local address.</summary>
    Opened = 2,

    /// <summary>Accepting incoming connections.</summary>
    Listening = 3,

    /// <summary>A connection is being established.</summary>
    Connecting = 4,

    /// <summary>Ready to transfer data.</summary>
    Connected = 5,

    /// <summary>The connection failed.</summary>
    Broken = 6,

    /// <summary>The socket is closing.</summary>
    Closing = 7,

    /// <summary>The socket is closed.</summary>
    Closed = 8,

    /// <summary>The socket no longer exists in libsrt.</summary>
    Nonexistent = 9
}

