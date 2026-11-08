// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net.Interop;

/// <summary>A socket registered with a poller, by object and native identifier.</summary>
/// <remarks>
/// The registration holds no reference on the socket handle, so disposing a registered socket closes it immediately. libsrt
/// drops a closed socket's subscriptions itself, and the poller ignores events for identifiers it no longer has.
/// </remarks>
internal sealed class SrtPollRegistration
{
    internal SrtPollRegistration(SrtSocket socket, int socketId)
    {
        Socket = socket;
        SocketId = socketId;
    }

    internal SrtSocket Socket
    {
        get;
    }

    internal int SocketId
    {
        get;
    }
}
