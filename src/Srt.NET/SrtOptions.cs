// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net;

/// <summary>Typed options. Set connection and transport options before binding or connecting.</summary>
public static class SrtOptions
{
    static SrtOptions()
    {
        MaximumSegmentSize = new(0, "SRTO_MSS");
        InitialSequenceNumber = new(3, "SRTO_ISN", canWrite: false);
        FlowControlWindow = new(4, "SRTO_FC");
        Linger = new(7, "SRTO_LINGER");
        UdpSendBufferSize = new(8, "SRTO_UDP_SNDBUF");
        UdpReceiveBufferSize = new(9, "SRTO_UDP_RCVBUF");
        AvailableEvents = new(18, "SRTO_EVENT", canWrite: false);
        PendingSendPackets = new(19, "SRTO_SNDDATA", canWrite: false);
        PendingReceivePackets = new(20, "SRTO_RCVDATA", canWrite: false);
        TimestampBasedDelivery = new(22, "SRTO_TSBPDMODE");
        IpTimeToLive = new(29, "SRTO_IPTTL");
        IpTypeOfService = new(30, "SRTO_IPTOS");
        SendDropDelay = new(32, "SRTO_SNDDROPDELAY");
        DriftTracer = new(37, "SRTO_DRIFTTRACER");
        MinimumInputBandwidth = new(38, "SRTO_MININPUTBW");
        MaximumReorderTolerance = new(42, "SRTO_LOSSMAXTTL");
        MinimumPeerVersion = new(45, "SRTO_MINVERSION");
        CongestionController = new(47, "SRTO_CONGESTION", maximumBytes: 128);
        KeyRefreshRate = new(51, "SRTO_KMREFRESHRATE");
        KeyPreannounce = new(52, "SRTO_KMPREANNOUNCE");
        BindToDevice = new(56, "SRTO_BINDTODEVICE", maximumBytes: 512);
        PacketFilter = new(60, "SRTO_PACKETFILTER", maximumBytes: 512);
        RetransmissionAlgorithm = new(61, "SRTO_RETRANSMITALGO");
        TransmissionType = new(50, "SRTO_TRANSTYPE", canRead: false);
        MessageApi = new(48, "SRTO_MESSAGEAPI");
        StreamId = new(46, "SRTO_STREAMID", maximumBytes: 512);
        Passphrase = new(26, "SRTO_PASSPHRASE", canRead: false, maximumBytes: 80, isSecret: true);
        EncryptionKeySize = new(27, "SRTO_PBKEYLEN");
        EnforcedEncryption = new(53, "SRTO_ENFORCEDENCRYPTION");
        ReceiveLatency = new(43, "SRTO_RCVLATENCY");
        PeerLatency = new(44, "SRTO_PEERLATENCY");
        PayloadSize = new(49, "SRTO_PAYLOADSIZE");
        SendBufferSize = new(5, "SRTO_SNDBUF");
        ReceiveBufferSize = new(6, "SRTO_RCVBUF");
        MaximumBandwidth = new(16, "SRTO_MAXBW");
        InputBandwidth = new(24, "SRTO_INPUTBW");
        OverheadBandwidth = new(25, "SRTO_OHEADBW");
        ConnectTimeout = new(36, "SRTO_CONNTIMEO");
        PeerIdleTimeout = new(55, "SRTO_PEERIDLETIMEO");
        Rendezvous = new(12, "SRTO_RENDEZVOUS");
        ReuseAddress = new(15, "SRTO_REUSEADDR");
        IPv6Only = new(54, "SRTO_IPV6ONLY");
        TooLatePacketDrop = new(31, "SRTO_TLPKTDROP");
        NegativeAcknowledgmentReports = new(33, "SRTO_NAKREPORT");
        SendEncryptionState = new(40, "SRTO_SNDKMSTATE", canWrite: false);
        ReceiveEncryptionState = new(41, "SRTO_RCVKMSTATE", canWrite: false);
        PeerVersion = new(35, "SRTO_PEERVERSION", canWrite: false);
    }

    /// <summary>Maximum UDP packet size in bytes, including protocol headers. Configure before binding.</summary>
    public static SrtOption<int> MaximumSegmentSize
    {
        get;
    }

    /// <summary>Initial packet sequence number, available after connection.</summary>
    public static SrtOption<int> InitialSequenceNumber
    {
        get;
    }

    /// <summary>Flow-control window in packets.</summary>
    public static SrtOption<int> FlowControlWindow
    {
        get;
    }

    /// <summary>How long a disposed socket keeps sending its queued data; see <see cref="SrtLinger"/>.</summary>
    public static SrtOption<SrtLinger> Linger
    {
        get;
    }

    /// <summary>Underlying UDP send buffer in bytes. Configure before binding.</summary>
    public static SrtOption<int> UdpSendBufferSize
    {
        get;
    }

    /// <summary>Underlying UDP receive buffer in bytes. Configure before binding.</summary>
    public static SrtOption<int> UdpReceiveBufferSize
    {
        get;
    }

    /// <summary>Current data-readiness flags. Listener readiness requires SrtPoller.</summary>
    public static SrtOption<int> AvailableEvents
    {
        get;
    }

    /// <summary>Packets queued in the send buffer.</summary>
    public static SrtOption<int> PendingSendPackets
    {
        get;
    }

    /// <summary>Packets available in the receive buffer.</summary>
    public static SrtOption<int> PendingReceivePackets
    {
        get;
    }

    /// <summary>Enable timestamp-based packet delivery.</summary>
    public static SrtOption<bool> TimestampBasedDelivery
    {
        get;
    }

    /// <summary>IP time-to-live, configured before binding.</summary>
    public static SrtOption<int> IpTimeToLive
    {
        get;
    }

    /// <summary>IP traffic class/type-of-service byte, configured before binding.</summary>
    public static SrtOption<int> IpTypeOfService
    {
        get;
    }

    /// <summary>Additional sender late-drop delay in milliseconds; -1 disables sender dropping.</summary>
    public static SrtOption<int> SendDropDelay
    {
        get;
    }

    /// <summary>Track peer clock drift for timestamp-based delivery.</summary>
    public static SrtOption<bool> DriftTracer
    {
        get;
    }

    /// <summary>Minimum estimated input bandwidth in bytes per second.</summary>
    public static SrtOption<long> MinimumInputBandwidth
    {
        get;
    }

    /// <summary>Maximum packet reordering tolerance before reporting loss.</summary>
    public static SrtOption<int> MaximumReorderTolerance
    {
        get;
    }

    /// <summary>Minimum peer version encoded as major &lt;&lt; 16 | minor &lt;&lt; 8 | patch.</summary>
    public static SrtOption<int> MinimumPeerVersion
    {
        get;
    }

    /// <summary>Native congestion controller name, normally live or file.</summary>
    public static SrtOption<string> CongestionController
    {
        get;
    }

    /// <summary>Packets sent before rotating encryption keys; zero uses libsrt's default.</summary>
    public static SrtOption<int> KeyRefreshRate
    {
        get;
    }

    /// <summary>Packets used to announce a new encryption key before rotation.</summary>
    public static SrtOption<int> KeyPreannounce
    {
        get;
    }

    /// <summary>Network interface name on platforms supporting SO_BINDTODEVICE. Configure before binding.</summary>
    public static SrtOption<string> BindToDevice
    {
        get;
    }

    /// <summary>Native packet filter configuration, including FEC when enabled in the native build.</summary>
    public static SrtOption<string> PacketFilter
    {
        get;
    }

    /// <summary>Retransmission algorithm selection: 0 for legacy, 1 for the current algorithm.</summary>
    public static SrtOption<int> RetransmissionAlgorithm
    {
        get;
    }

    /// <summary>Transport profile. Set this before other options because it resets profile defaults.</summary>
    public static SrtOption<SrtTransmissionType> TransmissionType
    {
        get;
    }

    /// <summary>Preserve messages in file mode; false selects a byte stream. Live mode requires true.</summary>
    public static SrtOption<bool> MessageApi
    {
        get;
    }

    /// <summary>The stream identifier sent during the handshake, limited to 512 UTF-8 bytes.</summary>
    /// <remarks>On an accepted socket this is the caller's untrusted value; bytes that are not valid UTF-8 read as U+FFFD.</remarks>
    public static SrtOption<string> StreamId
    {
        get;
    }

    /// <summary>The encryption passphrase (10–80 UTF-8 bytes), or empty to disable encryption.</summary>
    public static SrtOption<string> Passphrase
    {
        get;
    }

    /// <summary>AES key size in bytes: 0 (negotiate), 16, 24, or 32.</summary>
    public static SrtOption<int> EncryptionKeySize
    {
        get;
    }

    /// <summary>Reject peers with incompatible encryption settings.</summary>
    public static SrtOption<bool> EnforcedEncryption
    {
        get;
    }

    /// <summary>Receiver latency in milliseconds.</summary>
    public static SrtOption<int> ReceiveLatency
    {
        get;
    }

    /// <summary>Minimum latency requested from the peer, in milliseconds.</summary>
    public static SrtOption<int> PeerLatency
    {
        get;
    }

    /// <summary>Maximum payload size per live message, in bytes.</summary>
    public static SrtOption<int> PayloadSize
    {
        get;
    }

    /// <summary>Send buffer capacity in bytes.</summary>
    public static SrtOption<int> SendBufferSize
    {
        get;
    }

    /// <summary>Receive buffer capacity in bytes.</summary>
    public static SrtOption<int> ReceiveBufferSize
    {
        get;
    }

    /// <summary>Maximum bandwidth in bytes per second; -1 is unlimited and 0 is relative to input bandwidth.</summary>
    public static SrtOption<long> MaximumBandwidth
    {
        get;
    }

    /// <summary>Expected input bandwidth in bytes per second.</summary>
    public static SrtOption<long> InputBandwidth
    {
        get;
    }

    /// <summary>Bandwidth allowance for retransmissions, as a percentage of input bandwidth.</summary>
    public static SrtOption<int> OverheadBandwidth
    {
        get;
    }

    /// <summary>Native handshake timeout in milliseconds, also subject to the Connect method's timeout.</summary>
    public static SrtOption<int> ConnectTimeout
    {
        get;
    }

    /// <summary>Maximum peer silence in milliseconds.</summary>
    public static SrtOption<int> PeerIdleTimeout
    {
        get;
    }

    /// <summary>Use rendezvous mode. Bind both peers before connecting them simultaneously.</summary>
    public static SrtOption<bool> Rendezvous
    {
        get;
    }

    /// <summary>Allow compatible sockets to share the underlying UDP port.</summary>
    public static SrtOption<bool> ReuseAddress
    {
        get;
    }

    /// <summary>Set 1 for IPv6 only, 0 for dual stack, or -1 for the platform default.</summary>
    public static SrtOption<int> IPv6Only
    {
        get;
    }

    /// <summary>Drop packets that can no longer meet their delivery deadline.</summary>
    public static SrtOption<bool> TooLatePacketDrop
    {
        get;
    }

    /// <summary>Enable periodic loss reports.</summary>
    public static SrtOption<bool> NegativeAcknowledgmentReports
    {
        get;
    }

    /// <summary>Current sender encryption state.</summary>
    public static SrtOption<SrtKeyMaterialState> SendEncryptionState
    {
        get;
    }

    /// <summary>Current receiver encryption state.</summary>
    public static SrtOption<SrtKeyMaterialState> ReceiveEncryptionState
    {
        get;
    }

    /// <summary>The peer version encoded as major &lt;&lt; 16 | minor &lt;&lt; 8 | patch.</summary>
    public static SrtOption<int> PeerVersion
    {
        get;
    }
}
