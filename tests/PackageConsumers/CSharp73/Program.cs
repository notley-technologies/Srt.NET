// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Net;
using System.Threading.Tasks;
using Srt.Net;

namespace Srt.Net.PackageConsumers.CSharp73
{
    /// <summary>Uses the package with only C# 7.3 syntax, as a .NET Framework project does by default.</summary>
    internal static class Program
    {
        private static void Main()
        {
            TimeSpan timeout = TimeSpan.FromSeconds(5);

            using (SrtSocket listener = new SrtSocket())
            using (SrtSocket caller = new SrtSocket())
            {
                // File message mode, where the in-order flag of SrtMessageOptions applies.
                listener.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
                listener.SetOption(SrtOptions.MessageApi, true);
                caller.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
                caller.SetOption(SrtOptions.MessageApi, true);

                listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                listener.Listen();
                caller.Connect(listener.LocalEndPoint, timeout);

                using (SrtSocket peer = listener.Accept(timeout))
                {
                    // C# 7.3 cannot set init-only properties, so it configures message options through the constructor.
                    SrtMessageOptions options = new SrtMessageOptions(TimeSpan.FromSeconds(2), inOrder: true);

                    if (options.TimeToLive != TimeSpan.FromSeconds(2) || !options.InOrder || options.SourceTimeMicroseconds != 0)
                    {
                        throw new InvalidOperationException("SrtMessageOptions did not keep its constructor arguments.");
                    }

                    caller.SendMessage(new byte[] { 7, 3 }, options, timeout);

                    byte[] buffer = new byte[1500];
                    SrtMessageResult message = peer.ReceiveMessage(buffer, timeout);

                    if (message.BytesTransferred != 2 || buffer[0] != 7 || buffer[1] != 3)
                    {
                        throw new InvalidOperationException("The C# 7.3 consumer received incorrect data.");
                    }

                    Task<int> receiving = peer.ReceiveAsync(buffer).AsTask();
                    caller.SendAsync(new byte[] { 73 }).AsTask().GetAwaiter().GetResult();

                    if (!receiving.Wait(timeout) || receiving.Result != 1 || buffer[0] != 73)
                    {
                        throw new InvalidOperationException("The C# 7.3 consumer could not complete asynchronous operations.");
                    }
                }
            }

            Console.WriteLine("C# 7.3 package consumer passed with libsrt " + SrtSocket.NativeVersion);
        }
    }
}
