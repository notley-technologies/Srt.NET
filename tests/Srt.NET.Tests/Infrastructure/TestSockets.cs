// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using Srt.Net;

namespace Srt.Net.Tests;

internal static class TestSockets
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    internal static ConnectionPair Connect(int? bufferBytes = null)
    {
        using SrtSocket listener = new();
        SrtSocket caller = new();

        try
        {
            listener.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
            listener.SetOption(SrtOptions.MessageApi, true);
            caller.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
            caller.SetOption(SrtOptions.MessageApi, true);

            if (bufferBytes.HasValue)
            {
                listener.SetOption(SrtOptions.ReceiveBufferSize, bufferBytes.Value);
                caller.SetOption(SrtOptions.SendBufferSize, bufferBytes.Value);
            }

            listener.Bind(new(IPAddress.Loopback, 0));
            listener.Listen();
            caller.Connect(listener.LocalEndPoint, Deadline);

            return new(caller, listener.Accept(Deadline));
        }
        catch
        {
            caller.Dispose();
            throw;
        }
    }

    /// <summary>The libsrt release recorded in the build manifest staged with the native library under test.</summary>
    /// <remarks>The native build writes <c>srt.build-info.json</c> from <c>native/versions.json</c>, and the test project copies it next to the library.</remarks>
    internal static Version BundledNativeVersion()
    {
        string manifest = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "srt.build-info.json"));
        Match match = Regex.Match(manifest, "\"srt\"\\s*:\\s*\\{[^}]*\"version\"\\s*:\\s*\"(\\d+\\.\\d+\\.\\d+)\"", RegexOptions.CultureInvariant);

        return match.Success ? Version.Parse(match.Groups[1].Value) : throw new InvalidDataException("srt.build-info.json does not record the libsrt version.");
    }
}
