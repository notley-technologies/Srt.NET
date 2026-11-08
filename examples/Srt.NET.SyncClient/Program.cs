// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Net;
using System.Text;
using Srt.Net;

namespace Srt.NET.SyncClient;

internal static class Program
{
    private static void Main()
    {
        using SrtSocket client = new();
        client.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
        client.SetOption(SrtOptions.MessageApi, true);

        // Synchronous operations take a timeout; each call blocks this thread for at most that long.
        TimeSpan timeout = TimeSpan.FromSeconds(10);
        client.Connect(new(IPAddress.Loopback, 9000), timeout);

        byte[] message = Encoding.UTF8.GetBytes("Hello from Srt.NET!");
        client.Send(message, timeout);
        Console.WriteLine($"Sent: {Encoding.UTF8.GetString(message)}");

        // Wait for the echo so the client stays connected until the server has received the data.
        byte[] buffer = new byte[1500];
        int received = client.Receive(buffer, timeout);
        Console.WriteLine($"Echo: {Encoding.UTF8.GetString(buffer, 0, received)}");
    }
}
