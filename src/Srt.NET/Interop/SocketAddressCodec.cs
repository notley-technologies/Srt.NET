// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Srt.Net.Interop;

internal static unsafe class SocketAddressCodec
{
    private const int Ipv4Length = 16;
    private const int Ipv6Length = 28;

    // BSD-derived systems (macOS) start sockaddr with a one-byte length followed by a one-byte family.
    // Linux and Windows use a two-byte family. The kernel fills sa_len, but .NET does not, so compare
    // only the family bytes rather than the whole prefix.
    private static readonly bool HasLengthPrefix = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        || RuntimeInformation.IsOSPlatform(OSPlatform.Create("FREEBSD"));

    private static readonly int Ipv6Family = ReadFamily(new SocketAddress(AddressFamily.InterNetworkV6));
    private static readonly IPEndPoint Ipv4Template = new(IPAddress.Any, 0);
    private static readonly IPEndPoint Ipv6Template = new(IPAddress.IPv6Any, 0);

    internal static IPEndPoint Decode(IntPtr pointer)
    {
        byte* address = (byte*)pointer;
        int family = HasLengthPrefix ? address[1] : address[0] | (address[1] << 8);

        return Decode(new ReadOnlySpan<byte>(address, family == Ipv6Family ? Ipv6Length : Ipv4Length));
    }

    internal static int Encode(IPEndPoint endpoint, Span<byte> destination)
    {
        if (endpoint == null)
        {
            throw new ArgumentNullException(nameof(endpoint));
        }

        SocketAddress address = endpoint.Serialize();

        if (address.Size > destination.Length)
        {
            throw new ArgumentException("The endpoint buffer is too small.", nameof(destination));
        }

        for (int index = 0; index < address.Size; index++)
        {
            destination[index] = address[index];
        }

        return address.Size;
    }

    internal static IPEndPoint Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2 || bytes.Length > 128)
        {
            throw new InvalidOperationException("libsrt returned an invalid endpoint length.");
        }

        // SocketAddress supplies the operating system's family values and sockaddr layout.
        SocketAddress address = new(AddressFamily.InterNetwork, bytes.Length);

        for (int index = 0; index < bytes.Length; index++)
        {
            address[index] = bytes[index];
        }

        IPEndPoint template = address.Family == AddressFamily.InterNetworkV6 ? Ipv6Template : Ipv4Template;

        return (IPEndPoint)template.Create(address);
    }

    private static int ReadFamily(SocketAddress address)
    {
        return HasLengthPrefix ? address[1] : address[0] | (address[1] << 8);
    }
}
