// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Srt.Net;

namespace Srt.Net.Examples.AsyncServer;

internal static class Program
{
    private static async Task Main()
    {
        using CancellationTokenSource shutdown = new();

        void cancelHandler(object? _, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        }

        Console.CancelKeyPress += cancelHandler;

        try
        {
            await RunServerAsync(shutdown.Token);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        Console.WriteLine("Server stopped.");
    }

    private static async Task RunServerAsync(CancellationToken cancellationToken)
    {
        using SrtSocket listener = new();
        listener.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
        listener.SetOption(SrtOptions.MessageApi, true);
        listener.Bind(new(IPAddress.Loopback, 9000));
        listener.Listen();

        Console.WriteLine("Listening on 127.0.0.1:9000. Press Ctrl+C to stop.");
        List<Task> clients = [];

        try
        {
            while (true)
            {
                // Each client is served concurrently; the accept loop never waits for a client to finish.
                SrtSocket connection = await listener.AcceptAsync(cancellationToken);
                clients.Add(EchoAsync(connection, cancellationToken));
                clients.RemoveAll(client => client.IsCompleted);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ctrl+C: stop accepting and let the client loops observe the same token.
        }

        await Task.WhenAll(clients);
    }

    private static async Task EchoAsync(SrtSocket connection, CancellationToken cancellationToken)
    {
        using (connection)
        {
            IPEndPoint client = connection.RemoteEndPoint;
            Console.WriteLine($"Client {client} connected.");
            byte[] buffer = new byte[1500];

            try
            {
                while (true)
                {
                    int received = await connection.ReceiveAsync(buffer, cancellationToken);
                    Console.WriteLine($"Received from {client}: {Encoding.UTF8.GetString(buffer, 0, received)}");
                    await connection.SendAsync(buffer.AsMemory(0, received), cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"Closing connection to {client}.");
            }

            // After the peer closes, libsrt reports a lost connection, or an invalid socket once it has released it.
            catch (SrtException error) when (error.SrtErrorCode is SrtErrorCode.ConnectionLost or SrtErrorCode.NotConnected or SrtErrorCode.InvalidSocket)
            {
                Console.WriteLine($"Client {client} disconnected.");
            }
            catch (SrtException error)
            {
                Console.WriteLine($"Connection to {client} ended: {error.Message}");
            }
        }
    }
}
