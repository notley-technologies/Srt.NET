// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Srt.Net;

namespace Srt.Net.Examples.AsyncClient;

internal static class Program
{
    private static async Task Main()
    {
        using SrtSocket client = new();
        client.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
        client.SetOption(SrtOptions.MessageApi, true);

        // Asynchronous operations take a CancellationToken; a timed token source bounds the whole exchange.
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(new(IPAddress.Loopback, 9000), timeout.Token);

        byte[] message = Encoding.UTF8.GetBytes("Hello from Srt.NET!");
        await client.SendAsync(message, timeout.Token);
        Console.WriteLine($"Sent: {Encoding.UTF8.GetString(message)}");

        // Wait for the echo so the client stays connected until the server has received the data.
        byte[] buffer = new byte[1500];
        int received = await client.ReceiveAsync(buffer, timeout.Token);
        Console.WriteLine($"Echo: {Encoding.UTF8.GetString(buffer, 0, received)}");
    }
}
