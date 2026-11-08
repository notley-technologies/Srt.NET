// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;

namespace Srt.Net;

/// <summary>Readiness reported by an application-owned poller.</summary>
[Flags]
public enum SrtPollEvents
{
    /// <summary>No events.</summary>
    None = 0,

    /// <summary>Data is available, or a listener has a pending connection.</summary>
    Read = 1,

    /// <summary>A listener has a pending connection; an alias of Read.</summary>
    Accept = Read,

    /// <summary>Sending is possible, or a connection attempt has succeeded.</summary>
    Write = 4,

    /// <summary>A connection attempt has succeeded; an alias of Write.</summary>
    Connect = Write,

    /// <summary>The socket has failed. Perform the operation to obtain its error.</summary>
    Error = 8,

    /// <summary>The application disposed the socket. Reported once, then unregistered.</summary>
    Closed = 256
}
