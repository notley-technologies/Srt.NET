// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using Srt.Net.Interop;

namespace Srt.Net;

/// <summary>The byte count and native metadata for a sent or received message.</summary>
public readonly struct SrtMessageResult
{
    internal SrtMessageResult(int bytes, in NativeMessageControl control)
    {
        BytesTransferred = bytes;
        SourceTimeMicroseconds = control.SourceTime;
        PacketSequenceNumber = control.PacketSequence;
        MessageNumber = control.MessageNumber;
    }

    /// <summary>Bytes accepted for sending or copied into the receive buffer.</summary>
    public int BytesTransferred
    {
        get;
    }

    /// <summary>The source timestamp in the native clock's microsecond units.</summary>
    public long SourceTimeMicroseconds
    {
        get;
    }

    /// <summary>The first packet's sequence number, or -1 when not supplied.</summary>
    public int PacketSequenceNumber
    {
        get;
    }

    /// <summary>The message number, or -1 when not supplied.</summary>
    public int MessageNumber
    {
        get;
    }
}
