// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System.IO;
using Srt.Net.Interop;

namespace Srt.Net;

/// <summary>A failure reported by libsrt.</summary>
public sealed class SrtException : IOException
{
    internal SrtException(string message, SrtErrorCode error, int systemError = 0, SrtRejectReason? rejectionReason = null)
        : base(message)
    {
        SrtErrorCode = error;
        SystemErrorCode = systemError;
        RejectionReason = rejectionReason;
    }

    /// <summary>The libsrt error code.</summary>
    public SrtErrorCode SrtErrorCode
    {
        get;
    }

    /// <summary>The underlying operating system error, when provided by libsrt.</summary>
    public int SystemErrorCode
    {
        get;
    }

    /// <summary>The handshake rejection reason when a connection attempt failed, otherwise null.</summary>
    public SrtRejectReason? RejectionReason
    {
        get;
    }

    internal static SrtException LastError(string operation)
    {
        // libsrt errors are thread-local. Read them before any cleanup or other native call.
        int error = NativeMethods.srt_getlasterror(out int systemError);

        return LastError(operation, error, systemError);
    }

    internal static SrtException LastError(string operation, int error, int systemError)
    {
        string detail = Utf8Interop.Read(NativeMethods.srt_getlasterror_str());

        if (detail.Length == 0)
        {
            detail = "Unknown SRT error";
        }

        return new($"Unable to {operation}: {detail} (SRT {error}).", (SrtErrorCode)error, systemError);
    }

    internal static SrtException ConnectionFailed(string operation, int reason)
    {
        SrtRejectReason rejection = (SrtRejectReason)reason;
        string detail = Utf8Interop.Read(NativeMethods.srt_rejectreason_str(reason));

        // Match the codes that libsrt's blocking connect reports for the same outcome.
        SrtErrorCode error = rejection == SrtRejectReason.Timeout ? SrtErrorCode.NoServer : SrtErrorCode.ConnectionRejected;

        return new($"Unable to {operation}: {detail} (rejection {reason}).", error, rejectionReason: rejection);
    }

    internal static void ThrowIfError(int result, string operation)
    {
        if (result == NativeConstants.SRT_ERROR)
        {
            throw LastError(operation);
        }
    }
}
