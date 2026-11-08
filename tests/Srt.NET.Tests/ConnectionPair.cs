// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using Srt.Net;

namespace Srt.NET.Tests;

internal sealed class ConnectionPair : IDisposable
{
    internal ConnectionPair(SrtSocket caller, SrtSocket peer)
    {
        Caller = caller;
        Peer = peer;
    }

    internal SrtSocket Caller
    {
        get;
    }

    internal SrtSocket Peer
    {
        get;
    }

    public void Dispose()
    {
        Peer.Dispose();
        Caller.Dispose();
    }
}
