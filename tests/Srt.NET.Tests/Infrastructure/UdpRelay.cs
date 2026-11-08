// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Srt.Net.Tests;

/// <summary>Forwards UDP datagrams between one client and a target, and can drop them to simulate a network failure.</summary>
internal sealed class UdpRelay : IDisposable
{
    private readonly UdpClient front = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly UdpClient back = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly IPEndPoint target;
    private IPEndPoint? client;
    private int dropping;

    internal UdpRelay(IPEndPoint target)
    {
        this.target = target;
        IgnoreConnectionResets(front);
        IgnoreConnectionResets(back);
        _ = Task.Run(ForwardFromClientAsync);
        _ = Task.Run(ForwardToClientAsync);
    }

    /// <summary>The endpoint the client connects to.</summary>
    internal IPEndPoint EndPoint => (IPEndPoint)front.Client.LocalEndPoint!;

    /// <summary>Drops every datagram in both directions from now on.</summary>
    internal void DropEverything()
    {
        Volatile.Write(ref dropping, 1);
    }

    public void Dispose()
    {
        front.Dispose();
        back.Dispose();
    }

    private static void IgnoreConnectionResets(UdpClient udp)
    {
        // On Windows, an ICMP port-unreachable reply otherwise fails the next receive (SIO_UDP_CONNRESET).
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            udp.Client.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null);
        }
    }

    private async Task ForwardFromClientAsync()
    {
        try
        {
            while (true)
            {
                UdpReceiveResult datagram = await front.ReceiveAsync().ConfigureAwait(false);
                Volatile.Write(ref client, datagram.RemoteEndPoint);

                if (Volatile.Read(ref dropping) == 0)
                {
                    await back.SendAsync(datagram.Buffer, datagram.Buffer.Length, target).ConfigureAwait(false);
                }
            }
        }
        catch (Exception error) when (error is ObjectDisposedException or SocketException)
        {
            // The relay was disposed.
        }
    }

    private async Task ForwardToClientAsync()
    {
        try
        {
            while (true)
            {
                UdpReceiveResult datagram = await back.ReceiveAsync().ConfigureAwait(false);
                IPEndPoint? destination = Volatile.Read(ref client);

                if (destination != null && Volatile.Read(ref dropping) == 0)
                {
                    await front.SendAsync(datagram.Buffer, datagram.Buffer.Length, destination).ConfigureAwait(false);
                }
            }
        }
        catch (Exception error) when (error is ObjectDisposedException or SocketException)
        {
            // The relay was disposed.
        }
    }
}
