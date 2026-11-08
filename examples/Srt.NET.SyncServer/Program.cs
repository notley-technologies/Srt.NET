// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using Srt.Net;

namespace Srt.NET.SyncServer;

internal static class Program
{
    private static void Main()
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
            RunServer(shutdown.Token);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        Console.WriteLine("Server stopped.");
    }

    private static void RunServer(CancellationToken cancellationToken)
    {
        using SrtSocket listener = new();
        listener.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
        listener.SetOption(SrtOptions.MessageApi, true);
        listener.Bind(new(IPAddress.Loopback, 9000));
        listener.Listen();

        Console.WriteLine("Listening on 127.0.0.1:9000. Press Ctrl+C to stop.");
        List<Thread> clients = [];

        try
        {
            while (true)
            {
                // Accept blocks this thread until a client connects; each client then gets its own thread, so the
                // loop is back in Accept while earlier clients are still being served.
                SrtSocket connection = listener.Accept(cancellationToken: cancellationToken);
                Thread client = new(() => Echo(connection, cancellationToken))
                {
                    Name = "SRT echo client"
                };
                client.Start();
                clients.Add(client);
                clients.RemoveAll(thread => !thread.IsAlive);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ctrl+C: stop accepting and let the client threads observe the same token.
        }

        foreach (Thread client in clients)
        {
            client.Join();
        }
    }

    private static void Echo(SrtSocket connection, CancellationToken cancellationToken)
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
                    // Receive and Send block this thread; the token unblocks them when the server stops.
                    int received = connection.Receive(buffer, cancellationToken: cancellationToken);
                    Console.WriteLine($"Received from {client}: {Encoding.UTF8.GetString(buffer, 0, received)}");
                    connection.Send(buffer, 0, received, cancellationToken: cancellationToken);
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
