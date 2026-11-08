// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Text;

namespace Srt.Net.Interop;

internal static unsafe class Utf8Interop
{
    internal static string Read(IntPtr pointer, int maximumBytes = 65536)
    {
        if (pointer == IntPtr.Zero)
        {
            return string.Empty;
        }

        int length = 0;
        byte* bytes = (byte*)pointer;

        while (length < maximumBytes && bytes[length] != 0)
        {
            length++;
        }

        return Encoding.UTF8.GetString(bytes, length);
    }
}
