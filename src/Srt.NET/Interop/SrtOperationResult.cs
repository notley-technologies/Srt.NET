// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net.Interop;

/// <summary>The outcome of one attempt to run a queued operation.</summary>
internal enum SrtOperationResult
{
    /// <summary>The native call would block; the operation stays queued.</summary>
    Pending,

    /// <summary>The operation recorded a result or a native error.</summary>
    Completed,

    /// <summary>The operation was canceled or aborted and will not run again.</summary>
    Aborted
}
