// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Runtime.InteropServices;

namespace Srt.Net.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMessageControl
{
    internal static readonly NativeMessageControl Default = CreateDefault();

    internal int Flags;
    internal int TimeToLive;
    internal int InOrder;

    // Reserved by libsrt 1.5 and never written; kept for the SRT_MSGCTRL layout.
    internal int Boundary;
    internal long SourceTime;
    internal int PacketSequence;
    internal int MessageNumber;
    internal IntPtr GroupData;
    internal UIntPtr GroupDataSize;

    private static NativeMessageControl CreateDefault()
    {
        NativeMethods.srt_msgctrl_init(out NativeMessageControl control);

        return control;
    }
}
