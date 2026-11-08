// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net.Interop;

/// <summary>Native constants from srt.h that are used internally but not exposed as public options.</summary>
internal static class NativeConstants
{
    internal const int SRTO_SNDSYN = 1;
    internal const int SRTO_RCVSYN = 2;
    internal const int SRTO_EVENT = 18;

    internal const int SRT_EPOLL_IN = 1;
    internal const int SRT_EPOLL_OUT = 4;
    internal const int SRT_EPOLL_ERR = 8;
    internal const int SRT_EPOLL_ET = unchecked((int)0x80000000);

    internal const int SRT_EPOLL_ENABLE_EMPTY = 1;

    internal const int SRT_ERROR = -1;
    internal const int SRT_INVALID_SOCK = -1;
}
