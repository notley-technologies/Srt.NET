// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net;

/// <summary>Connection rejection reasons (<c>SRT_REJECT_REASON</c> and <c>SRT_REJX_*</c>).</summary>
/// <remarks>
/// Values below 1000 are set by libsrt. Values from 1000 are set by listeners, including the predefined
/// server codes below. Applications may define their own codes from <see cref="UserDefined"/> upward,
/// for example <c>SrtRejectReason.UserDefined + 1</c>.
/// </remarks>
public enum SrtRejectReason
{
    /// <summary>No reason is known (<c>SRT_REJ_UNKNOWN</c>).</summary>
    Unknown = 0,

    /// <summary>A system function failed (<c>SRT_REJ_SYSTEM</c>).</summary>
    System = 1,

    /// <summary>The peer rejected the connection (<c>SRT_REJ_PEER</c>).</summary>
    Peer = 2,

    /// <summary>A resource allocation failed (<c>SRT_REJ_RESOURCE</c>).</summary>
    Resource = 3,

    /// <summary>The handshake contained incorrect data (<c>SRT_REJ_ROGUE</c>).</summary>
    Rogue = 4,

    /// <summary>The listener backlog is full (<c>SRT_REJ_BACKLOG</c>).</summary>
    Backlog = 5,

    /// <summary>An internal program error occurred (<c>SRT_REJ_IPE</c>).</summary>
    InternalError = 6,

    /// <summary>The socket is closing (<c>SRT_REJ_CLOSE</c>).</summary>
    Closing = 7,

    /// <summary>The peer is older than the minimum version (<c>SRT_REJ_VERSION</c>).</summary>
    Version = 8,

    /// <summary>Rendezvous cookies collided (<c>SRT_REJ_RDVCOOKIE</c>).</summary>
    RendezvousCookie = 9,

    /// <summary>The passphrase does not match (<c>SRT_REJ_BADSECRET</c>).</summary>
    BadSecret = 10,

    /// <summary>Encryption is required on one side only (<c>SRT_REJ_UNSECURE</c>).</summary>
    Unsecure = 11,

    /// <summary>Message and stream modes do not match (<c>SRT_REJ_MESSAGEAPI</c>).</summary>
    MessageApi = 12,

    /// <summary>Congestion controllers do not match (<c>SRT_REJ_CONGESTION</c>).</summary>
    Congestion = 13,

    /// <summary>Packet filters do not match (<c>SRT_REJ_FILTER</c>).</summary>
    Filter = 14,

    /// <summary>Group settings do not match (<c>SRT_REJ_GROUP</c>).</summary>
    Group = 15,

    /// <summary>The connection timed out (<c>SRT_REJ_TIMEOUT</c>).</summary>
    Timeout = 16,

    /// <summary>Cryptographic modes do not match (<c>SRT_REJ_CRYPTO</c>).</summary>
    Crypto = 17,

    /// <summary>A generic listener rejection (<c>SRT_REJX_FALLBACK</c>).</summary>
    Fallback = 1000,

    /// <summary>A stream ID key is not supported (<c>SRT_REJX_KEY_NOTSUP</c>).</summary>
    KeyNotSupported = 1001,

    /// <summary>The requested file path is invalid (<c>SRT_REJX_FILEPATH</c>).</summary>
    FilePath = 1002,

    /// <summary>The requested host is not recognized (<c>SRT_REJX_HOSTNOTFOUND</c>).</summary>
    HostNotFound = 1003,

    /// <summary>The request is malformed (<c>SRT_REJX_BAD_REQUEST</c>).</summary>
    BadRequest = 1400,

    /// <summary>Authentication failed (<c>SRT_REJX_UNAUTHORIZED</c>).</summary>
    Unauthorized = 1401,

    /// <summary>The server is overloaded (<c>SRT_REJX_OVERLOAD</c>).</summary>
    Overload = 1402,

    /// <summary>Access is denied (<c>SRT_REJX_FORBIDDEN</c>).</summary>
    Forbidden = 1403,

    /// <summary>The resource was not found (<c>SRT_REJX_NOTFOUND</c>).</summary>
    NotFound = 1404,

    /// <summary>The requested mode is not supported (<c>SRT_REJX_BAD_MODE</c>).</summary>
    BadMode = 1405,

    /// <summary>The requested parameters cannot be satisfied (<c>SRT_REJX_UNACCEPTABLE</c>).</summary>
    Unacceptable = 1406,

    /// <summary>The resource is locked for modification (<c>SRT_REJX_CONFLICT</c>).</summary>
    Conflict = 1409,

    /// <summary>The media type is not supported (<c>SRT_REJX_NOTSUP_MEDIA</c>).</summary>
    NotSupportedMedia = 1415,

    /// <summary>The resource is locked (<c>SRT_REJX_LOCKED</c>).</summary>
    Locked = 1423,

    /// <summary>A dependent session has disconnected (<c>SRT_REJX_FAILED_DEPEND</c>).</summary>
    FailedDependency = 1424,

    /// <summary>An unexpected server error occurred (<c>SRT_REJX_ISE</c>).</summary>
    InternalServerError = 1500,

    /// <summary>The request is not implemented (<c>SRT_REJX_UNIMPLEMENTED</c>).</summary>
    NotImplemented = 1501,

    /// <summary>An upstream endpoint rejected the connection (<c>SRT_REJX_GW</c>).</summary>
    Gateway = 1502,

    /// <summary>The service is temporarily unavailable (<c>SRT_REJX_DOWN</c>).</summary>
    ServiceUnavailable = 1503,

    /// <summary>The SRT version is not supported (<c>SRT_REJX_VERSION</c>).</summary>
    VersionNotSupported = 1505,

    /// <summary>The server lacks storage space (<c>SRT_REJX_NOROOM</c>).</summary>
    NoRoom = 1507,

    /// <summary>The first application-defined code (<c>SRT_REJC_USERDEFINED</c>).</summary>
    UserDefined = 2000
}
