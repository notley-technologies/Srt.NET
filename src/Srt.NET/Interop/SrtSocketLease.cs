// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;

namespace Srt.Net.Interop;

internal ref struct SrtSocketLease
{
    private SrtSocketHandle? owner;

    internal SrtSocketLease(SrtSocketHandle handle)
    {
        Socket = handle.AcquireReference();
        owner = handle;
    }

    internal int Socket
    {
        get;
    }

    public void Dispose()
    {
        SrtSocketHandle? handle = owner;
        owner = null;

        handle?.DangerousRelease();
    }
}
