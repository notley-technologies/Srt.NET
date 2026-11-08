// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Net;
using Srt.Net.Interop;

namespace Srt.Net;

/// <summary>A connection under admission review. Configuration is valid only on the callback thread while the callback runs.</summary>
public sealed class SrtConnectionRequest
{
    private readonly int socket;
    private readonly int threadId = Environment.CurrentManagedThreadId;
    private bool active = true;

    internal SrtConnectionRequest(int socket, int handshakeVersion, IPEndPoint remoteEndPoint, string streamId)
    {
        this.socket = socket;
        HandshakeVersion = handshakeVersion;
        RemoteEndPoint = remoteEndPoint;
        StreamId = streamId;
    }

    /// <summary>The peer's handshake version.</summary>
    public int HandshakeVersion
    {
        get;
    }

    /// <summary>The peer's address and port.</summary>
    public IPEndPoint RemoteEndPoint
    {
        get;
    }

    /// <summary>The stream identifier supplied by the peer. It is untrusted; bytes that are not valid UTF-8 read as U+FFFD.</summary>
    public string StreamId
    {
        get;
    }

    /// <summary>Sets a per-peer option, such as a passphrase, before the handshake completes.</summary>
    public void SetOption<T>(SrtOption<T> option, T value)
    {
        EnsureActive();
        SocketOptionCodec.Set(socket, option, value);
    }

    /// <summary>Reads a native option during admission.</summary>
    public T GetOption<T>(SrtOption<T> option)
    {
        EnsureActive();

        return SocketOptionCodec.Get<T>(socket, option);
    }

    /// <summary>Sets the reason returned to a rejected caller. Return false from the handler to reject.</summary>
    /// <remarks>Use a predefined server reason such as <see cref="SrtRejectReason.Forbidden"/>, or an application code from <see cref="SrtRejectReason.UserDefined"/> upward.</remarks>
    public void SetRejectionReason(SrtRejectReason reason)
    {
        EnsureActive();

        if (reason < SrtRejectReason.Fallback)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), "Reasons below 1000 are reserved for libsrt.");
        }

        SrtException.ThrowIfError(NativeMethods.srt_setrejectreason(socket, (int)reason), "set an SRT rejection reason");
    }

    internal void Expire()
    {
        active = false;
    }

    private void EnsureActive()
    {
        if (threadId != Environment.CurrentManagedThreadId || !active)
        {
            throw new InvalidOperationException("Connection options may only be accessed on the admission callback thread while it is running.");
        }
    }
}
