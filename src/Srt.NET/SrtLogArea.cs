// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net;

/// <summary>Native functional areas used to select diagnostic messages.</summary>
public enum SrtLogArea
{
    /// <summary>General messages.</summary>
    General = 0,

    /// <summary>Socket Management messages.</summary>
    SocketManagement = 1,

    /// <summary>Connection messages.</summary>
    Connection = 2,

    /// <summary>Timers messages.</summary>
    Timers = 3,

    /// <summary>Timestamp Delivery messages.</summary>
    TimestampDelivery = 4,

    /// <summary>Resources messages.</summary>
    Resources = 5,

    /// <summary>Congestion messages.</summary>
    Congestion = 7,

    /// <summary>Packet Filter messages.</summary>
    PacketFilter = 8,

    /// <summary>Control Api messages.</summary>
    ControlApi = 11,

    /// <summary>Control Queue messages.</summary>
    ControlQueue = 13,

    /// <summary>Poll Updates messages.</summary>
    PollUpdates = 16,

    /// <summary>Receive Api messages.</summary>
    ReceiveApi = 21,

    /// <summary>Receive Buffer messages.</summary>
    ReceiveBuffer = 22,

    /// <summary>Receive Queue messages.</summary>
    ReceiveQueue = 23,

    /// <summary>Receive Channel messages.</summary>
    ReceiveChannel = 24,

    /// <summary>Receive Group messages.</summary>
    ReceiveGroup = 25,

    /// <summary>Send Api messages.</summary>
    SendApi = 31,

    /// <summary>Send Buffer messages.</summary>
    SendBuffer = 32,

    /// <summary>Send Queue messages.</summary>
    SendQueue = 33,

    /// <summary>Send Channel messages.</summary>
    SendChannel = 34,

    /// <summary>Send Group messages.</summary>
    SendGroup = 35,

    /// <summary>Internal messages.</summary>
    Internal = 41,

    /// <summary>Queue Management messages.</summary>
    QueueManagement = 43,

    /// <summary>Channel Management messages.</summary>
    ChannelManagement = 44,

    /// <summary>Group Management messages.</summary>
    GroupManagement = 45,

    /// <summary>Poll Api messages.</summary>
    PollApi = 46,

    /// <summary>Encryption messages.</summary>
    Encryption = 6,

    /// <summary>Application messages.</summary>
    Application = 10
}
