// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Srt.Net.Interop;
using Xunit;

namespace Srt.Net.Tests;

/// <summary>Covers the marshaling helpers directly, including inputs that libsrt itself never produces.</summary>
public sealed class InteropCodecTests
{
    [Fact]
    public void EndpointsRoundTripThroughTheNativeAddressLayout()
    {
        IPEndPoint[] endpoints = [new(IPAddress.Loopback, 9000), new(IPAddress.IPv6Loopback, 65535), new(IPAddress.Parse("192.0.2.7"), 1)];

        foreach (IPEndPoint endpoint in endpoints)
        {
            byte[] address = new byte[128];
            int length = SocketAddressCodec.Encode(endpoint, address);
            Assert.Equal(endpoint, SocketAddressCodec.Decode(address.AsSpan(0, length)));
        }
    }

    [Fact]
    public void RejectsMissingEndpointsAndInvalidAddressLengths()
    {
        Assert.Throws<ArgumentNullException>("endpoint", () => SocketAddressCodec.Encode(null!, new byte[128]));
        Assert.Throws<ArgumentException>("destination", () => SocketAddressCodec.Encode(new(IPAddress.IPv6Loopback, 1), new byte[8]));
        Assert.Throws<InvalidOperationException>(() => SocketAddressCodec.Decode(new byte[1]));
        Assert.Throws<InvalidOperationException>(() => SocketAddressCodec.Decode(new byte[129]));
    }

    [Fact]
    public void ReadsNullTerminatedUtf8WithinTheLimit()
    {
        Assert.Equal(string.Empty, Utf8Interop.Read(IntPtr.Zero));

        byte[] text = [.. Encoding.UTF8.GetBytes("café"), 0, (byte)'x'];
        GCHandle pinned = GCHandle.Alloc(text, GCHandleType.Pinned);

        try
        {
            IntPtr pointer = pinned.AddrOfPinnedObject();
            Assert.Equal("café", Utf8Interop.Read(pointer));
            Assert.Equal("ca", Utf8Interop.Read(pointer, 2));
        }
        finally
        {
            pinned.Free();
        }
    }
}
