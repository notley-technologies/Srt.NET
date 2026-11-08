// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net.Interop;

internal static class EpollSubscription
{
    /// <summary>Removes a socket from a poll, skipping sockets that libsrt has already closed.</summary>
    /// <remarks>
    /// When a peer disconnects, libsrt's collector moves the broken socket to its closed list, often within
    /// milliseconds, because closing the other end wakes the collector. srt_epoll_remove_usock then logs an error for
    /// the socket even though it still removes the subscription. libsrt removes every subscription itself when it frees
    /// a closed socket, so there is nothing to do for those. A socket that the collector closes between the state check
    /// and the removal still makes libsrt log one remove_usock error, and two "IPE ... NOT SUBSCRIBED" errors when it is
    /// freed; none of them has an effect. The engine therefore skips the removal for sockets that are being disposed.
    /// </remarks>
    internal static int Remove(int poll, int socket)
    {
        SrtSocketState state = NativeMethods.srt_getsockstate(socket);

        if (state is SrtSocketState.Closed or SrtSocketState.Nonexistent)
        {
            return 0;
        }

        return NativeMethods.srt_epoll_remove_usock(poll, socket);
    }
}
