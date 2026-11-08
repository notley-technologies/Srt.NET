// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Net;
using Srt.Net;

namespace Srt.Net.Benchmarks;

/// <summary>A loopback caller and accepted peer configured for file-mode messages.</summary>
internal sealed class ConnectedPair : IDisposable
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    internal ConnectedPair()
    {
        SrtLogging.SetLevel(SrtLogLevel.Off);
        using SrtSocket listener = new();
        Caller = new();
        listener.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
        listener.SetOption(SrtOptions.MessageApi, true);
        Caller.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
        Caller.SetOption(SrtOptions.MessageApi, true);
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        Caller.Connect(listener.LocalEndPoint, Deadline);
        Peer = listener.Accept(Deadline);
    }

    internal SrtSocket Caller
    {
        get;
    }

    internal SrtSocket Peer
    {
        get;
    }

    public void Dispose()
    {
        Peer.Dispose();
        Caller.Dispose();
    }
}
