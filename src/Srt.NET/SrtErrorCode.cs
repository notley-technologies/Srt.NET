// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net;

/// <summary>Error codes reported by libsrt (<c>SRT_ERRNO</c>).</summary>
/// <remarks>Values match the native codes, so unrecognized future codes are still preserved.</remarks>
public enum SrtErrorCode
{
    /// <summary>Unknown error (<c>SRT_EUNKNOWN</c>).</summary>
    Unknown = -1,

    /// <summary>No error (<c>SRT_SUCCESS</c>).</summary>
    Success = 0,

    /// <summary>Connection setup failed (<c>SRT_ECONNSETUP</c>).</summary>
    ConnectionSetupFailed = 1000,

    /// <summary>The peer did not respond before the connection timeout (<c>SRT_ENOSERVER</c>).</summary>
    NoServer = 1001,

    /// <summary>The peer rejected the connection; see <see cref="SrtException.RejectionReason"/> (<c>SRT_ECONNREJ</c>).</summary>
    ConnectionRejected = 1002,

    /// <summary>A system socket could not be created or configured (<c>SRT_ESOCKFAIL</c>).</summary>
    SocketFailed = 1003,

    /// <summary>The security handshake failed (<c>SRT_ESECFAIL</c>).</summary>
    SecurityFailed = 1004,

    /// <summary>The socket was closed during setup (<c>SRT_ESCLOSED</c>).</summary>
    SocketClosed = 1005,

    /// <summary>A connection-level failure occurred (<c>SRT_ECONNFAIL</c>).</summary>
    ConnectionFailed = 2000,

    /// <summary>An established connection was lost (<c>SRT_ECONNLOST</c>).</summary>
    ConnectionLost = 2001,

    /// <summary>The socket is not connected (<c>SRT_ENOCONN</c>).</summary>
    NotConnected = 2002,

    /// <summary>A system resource failure occurred (<c>SRT_ERESOURCE</c>).</summary>
    ResourceFailed = 3000,

    /// <summary>A native thread could not be created (<c>SRT_ETHREAD</c>).</summary>
    ThreadFailed = 3001,

    /// <summary>Native memory could not be allocated (<c>SRT_ENOBUF</c>).</summary>
    NoBuffer = 3002,

    /// <summary>A system object could not be created (<c>SRT_ESYSOBJ</c>).</summary>
    SystemObjectFailed = 3003,

    /// <summary>A native file operation failed (<c>SRT_EFILE</c>).</summary>
    FileFailed = 4000,

    /// <summary>Invalid read offset (<c>SRT_EINVRDOFF</c>).</summary>
    InvalidReadOffset = 4001,

    /// <summary>Read permission denied (<c>SRT_ERDPERM</c>).</summary>
    ReadPermissionDenied = 4002,

    /// <summary>Invalid write offset (<c>SRT_EINVWROFF</c>).</summary>
    InvalidWriteOffset = 4003,

    /// <summary>Write permission denied (<c>SRT_EWRPERM</c>).</summary>
    WritePermissionDenied = 4004,

    /// <summary>The operation is not supported in the current state (<c>SRT_EINVOP</c>).</summary>
    InvalidOperation = 5000,

    /// <summary>The socket is already bound (<c>SRT_EBOUNDSOCK</c>).</summary>
    AlreadyBound = 5001,

    /// <summary>The socket is already connected (<c>SRT_ECONNSOCK</c>).</summary>
    AlreadyConnected = 5002,

    /// <summary>An argument or option value is invalid (<c>SRT_EINVPARAM</c>).</summary>
    InvalidParameter = 5003,

    /// <summary>The socket identifier is invalid or closed (<c>SRT_EINVSOCK</c>).</summary>
    InvalidSocket = 5004,

    /// <summary>The socket is not bound (<c>SRT_EUNBOUNDSOCK</c>).</summary>
    NotBound = 5005,

    /// <summary>The socket is not listening (<c>SRT_ENOLISTEN</c>).</summary>
    NotListening = 5006,

    /// <summary>Rendezvous sockets cannot listen or accept (<c>SRT_ERDVNOSERV</c>).</summary>
    RendezvousNoServer = 5007,

    /// <summary>Rendezvous sockets must be bound before connecting (<c>SRT_ERDVUNBOUND</c>).</summary>
    RendezvousUnbound = 5008,

    /// <summary>The operation requires message mode (<c>SRT_EINVALMSGAPI</c>).</summary>
    InvalidMessageApi = 5009,

    /// <summary>The operation requires stream mode (<c>SRT_EINVALBUFFERAPI</c>).</summary>
    InvalidBufferApi = 5010,

    /// <summary>Another socket is already listening on this port (<c>SRT_EDUPLISTEN</c>).</summary>
    DuplicateListener = 5011,

    /// <summary>The message exceeds the configured payload or buffer size (<c>SRT_ELARGEMSG</c>).</summary>
    MessageTooLarge = 5012,

    /// <summary>The poll identifier is invalid (<c>SRT_EINVPOLLID</c>).</summary>
    InvalidPollId = 5013,

    /// <summary>The poll has no registered sockets (<c>SRT_EPOLLEMPTY</c>).</summary>
    PollEmpty = 5014,

    /// <summary>The local address is in use with incompatible settings (<c>SRT_EBINDCONFLICT</c>).</summary>
    BindConflict = 5015,

    /// <summary>A nonblocking operation could not complete (<c>SRT_EASYNCFAIL</c>).</summary>
    WouldBlock = 6000,

    /// <summary>The send buffer is full (<c>SRT_EASYNCSND</c>).</summary>
    SendWouldBlock = 6001,

    /// <summary>No data is available to receive (<c>SRT_EASYNCRCV</c>).</summary>
    ReceiveWouldBlock = 6002,

    /// <summary>The operation timed out (<c>SRT_ETIMEOUT</c>).</summary>
    TimedOut = 6003,

    /// <summary>The operation was refused because of congestion (<c>SRT_ECONGEST</c>).</summary>
    Congestion = 6004,

    /// <summary>The peer reported an error (<c>SRT_EPEERERR</c>).</summary>
    PeerError = 7000
}
