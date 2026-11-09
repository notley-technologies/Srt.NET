// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Srt.Net;

namespace Srt.Net.PackageConsumers;

internal static class PackageSmokeTest
{
    private static SrtSocket? Leaked
    {
        get; set;
    }

    internal static void Run()
    {
        string[] notices =
        [
            "THIRD_PARTY_NOTICES.md",
            "Srt.NET-LICENSE.txt",
            "libsrt-MPL-2.0.txt",
            "openssl-LICENSE.txt",
            "libsrt-COPYRIGHT.txt",
            "openssl-COPYRIGHT.txt"
        ];

        foreach (string notice in notices)
        {
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "licenses", "Srt.NET", notice)))
            {
                throw new InvalidOperationException($"Missing dependency notice: {notice}");
            }
        }

#if NETFRAMEWORK
        // ASP.NET and test runners use Srt.NET from application domains that unload without ProcessExit. The domain must
        // release libsrt (or the process aborts at exit) and clear its log handler (or the next record calls into it).
        AppDomain domain = AppDomain.CreateDomain("Srt.NET consumer domain", null, AppDomain.CurrentDomain.SetupInformation);
        domain.DoCallBack(UseSrtInThisDomain);
        AppDomain.Unload(domain);

        using (new SrtSocket())
        {
            // Creating a socket logs at the debug level the unloaded domain left set.
        }

        SrtLogging.SetLevel(SrtLogLevel.Warning);
#endif

        using (SrtSocket listener = new())
        using (SrtSocket caller = new())
        using (SrtPoller poller = new())
        {
            listener.SetConnectionHandler(request =>
            {
                request.SetOption(SrtOptions.Passphrase, "package test password");

                return request.StreamId == "package-consumer";
            });

            caller.SetOption(SrtOptions.Passphrase, "package test password");
            caller.SetOption(SrtOptions.StreamId, "package-consumer");

            // Native log records arrive through a second native-to-managed callback, which Native AOT must also support.
            int logRecords = 0;
            SrtLogging.SetLevel(SrtLogLevel.Debug);
            SrtLogging.SetHandler(_ => Interlocked.Increment(ref logRecords));

            try
            {
                listener.Bind(new(IPAddress.Loopback, 0));
                poller.Add(listener, SrtPollEvents.Accept);
                listener.Listen();
                caller.Connect(listener.LocalEndPoint, TimeSpan.FromSeconds(5));

                // The handler runs on a managed worker after the native callback returns.
                if (!SpinWait.SpinUntil(() => Volatile.Read(ref logRecords) > 0, TimeSpan.FromSeconds(5)))
                {
                    throw new InvalidOperationException("Package consumer did not receive native log records.");
                }
            }
            finally
            {
                // Restore libsrt's defaults: warnings and above, written to standard error.
                SrtLogging.SetLevel(SrtLogLevel.Warning);
                SrtLogging.SetHandler(null);
            }

            SrtPollEvent[] events = new SrtPollEvent[1];

            if (poller.Wait(events.AsSpan(), TimeSpan.FromSeconds(5)) != 1)
            {
                throw new InvalidOperationException("Package consumer did not observe a pending connection.");
            }

            using SrtSocket peer = listener.Accept(TimeSpan.Zero);
            caller.SendMessage([17, 42], timeout: TimeSpan.FromSeconds(5));

            byte[] buffer = new byte[1500];
            SrtMessageResult message = peer.ReceiveMessage(buffer.AsSpan(), TimeSpan.FromSeconds(5));

            if (message.BytesTransferred != 2 || buffer[0] != 17 || buffer[1] != 42)
            {
                throw new InvalidOperationException("Package consumer received incorrect data.");
            }

            if (peer.GetOption(SrtOptions.ReceiveEncryptionState) != SrtKeyMaterialState.Secured)
            {
                throw new InvalidOperationException("Package consumer did not negotiate encryption.");
            }

            if (peer.GetStatistics().PacketsReceivedUniqueTotal < 1 || message.SourceTimeMicroseconds <= 0)
            {
                throw new InvalidOperationException("Package consumer could not read native statistics or message metadata.");
            }

            // Exercise the asynchronous engine, which keeps libsrt started for the rest of the process.
            Task<int> receiving = peer.ReceiveAsync(buffer.AsMemory()).AsTask();
            caller.SendAsync(new byte[] { 99 }.AsMemory()).AsTask().GetAwaiter().GetResult();

            if (!receiving.Wait(TimeSpan.FromSeconds(5)) || receiving.Result != 1 || buffer[0] != 99)
            {
                throw new InvalidOperationException("Package consumer could not complete asynchronous operations.");
            }
        }

        // Deliberately leave a socket open: the process must still exit cleanly (exit code 0).
        Leaked = new();
        Console.WriteLine($"Package consumer passed with libsrt {SrtSocket.NativeVersion}");
    }

#if NETFRAMEWORK
    private static void UseSrtInThisDomain()
    {
        SrtLogging.SetLevel(SrtLogLevel.Debug);
        SrtLogging.SetHandler(_ => { });

        using SrtSocket listener = new();
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        using SrtSocket caller = new();
        caller.ConnectAsync(listener.LocalEndPoint).AsTask().GetAwaiter().GetResult();
        using SrtSocket peer = listener.AcceptAsync().AsTask().GetAwaiter().GetResult();
        Task<int> receiving = peer.ReceiveAsync(new byte[1500].AsMemory()).AsTask();
        caller.SendAsync(new byte[] { 5 }.AsMemory()).AsTask().GetAwaiter().GetResult();

        if (!receiving.Wait(TimeSpan.FromSeconds(5)) || receiving.Result != 1)
        {
            throw new InvalidOperationException("Package consumer could not use Srt.NET in a second application domain.");
        }
    }
#endif
}
