// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Runtime.InteropServices;
using Srt.Net.Interop;
using Xunit;

namespace Srt.NET.Tests;

public sealed class AbiTests
{
    [Fact]
    public void ManagedLayoutsMatchThePinnedNativeHeaders()
    {
        // Sizes and offsets were checked with sizeof/offsetof against the pinned native headers.
        Assert.Equal(496, Marshal.SizeOf<NativeStatistics>());
        Assert.Equal(48, Marshal.SizeOf<NativeMessageControl>());
        Assert.Equal(8, Marshal.SizeOf<NativePollEvent>());
        Assert.Equal(new IntPtr(80), Marshal.OffsetOf<NativeStatistics>(nameof(NativeStatistics.BytesSentTotal)));
        Assert.Equal(new IntPtr(328), Marshal.OffsetOf<NativeStatistics>(nameof(NativeStatistics.RoundTripTimeMilliseconds)));
        Assert.Equal(new IntPtr(432), Marshal.OffsetOf<NativeStatistics>(nameof(NativeStatistics.PacketsSentUniqueTotal)));
        Assert.Equal(new IntPtr(488), Marshal.OffsetOf<NativeStatistics>(nameof(NativeStatistics.BytesReceivedUnique)));
        Assert.Equal(new IntPtr(16), Marshal.OffsetOf<NativeMessageControl>(nameof(NativeMessageControl.SourceTime)));
        Assert.Equal(new IntPtr(32), Marshal.OffsetOf<NativeMessageControl>(nameof(NativeMessageControl.GroupData)));
    }
}
