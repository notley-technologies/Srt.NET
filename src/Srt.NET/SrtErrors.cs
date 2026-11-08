// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using Srt.Net.Interop;

namespace Srt.Net;

/// <summary>Descriptions for native error and connection rejection codes.</summary>
public static class SrtErrors
{
    /// <summary>Formats a native error code. libsrt's text does not include the operating system error.</summary>
    public static string Describe(SrtErrorCode errorCode)
    {
        // srt_strerror returns static text and ignores its second argument.
        return Utf8Interop.Read(NativeMethods.srt_strerror((int)errorCode, 0));
    }

    /// <summary>Formats a native, predefined, or application-defined connection rejection reason.</summary>
    public static string DescribeRejection(SrtRejectReason reason)
    {
        return Utf8Interop.Read(NativeMethods.srt_rejectreason_str((int)reason));
    }
}
