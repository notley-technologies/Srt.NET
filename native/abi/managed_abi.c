/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Noah Davis
 */

/*
 * The libsrt structure layouts and constant values that Srt.NET's managed declarations rely on, for 64-bit targets.
 *
 * The native build compiles this file against the bundled libsrt headers, so a libsrt update that moves, retypes,
 * removes or renumbers anything listed here fails the build instead of corrupting memory or calling the wrong option
 * at run time. AbiTests (tests/Srt.NET.Tests) checks every entry against the managed declaration named in its first
 * argument, and that every declaration is listed, so this file and the managed code cannot drift apart. When a test
 * reports a declaration that is not listed, add an entry here with the native name and value from the libsrt headers.
 *
 * Not covered: function signatures (the P/Invoke declarations in NativeMethods), the value type of each socket option
 * (exercised by the option tests), the operating system's struct linger (encoded by SocketOptionCodec and exercised by
 * the option tests), and SRT_ERROR and SRT_INVALID_SOCK, which srt.h declares as static const variables.
 *
 * SRTNET_SIZE(managed type, native type, size in bytes)
 * SRTNET_FIELD(managed field, native type, native field, native field type, offset in bytes)
 * SRTNET_VALUE(managed constant, native constant, value)
 */

#include <stddef.h>

#include <srt/srt.h>
#include <srt/access_control.h>

#define SRTNET_SIZE(managed, type, size) _Static_assert(sizeof(type) == (size), "sizeof(" #type ") must be " #size)
/* The field must have exactly the native type, so widening a field into padding fails even when no offset moves. */
#define SRTNET_FIELD(managed, type, field, ctype, offset) \
    _Static_assert(offsetof(type, field) == (offset), #type "." #field " must be at offset " #offset); \
    _Static_assert(_Generic(((type *)0)->field, ctype: 1, default: 0), #type "." #field " must be " #ctype)
/* Compared as 32-bit unsigned values, so negative codes and the high epoll flag need no casts at the call site. */
#define SRTNET_VALUE(managed, name, value) \
    _Static_assert((unsigned int)(name) == (unsigned int)(value), #name " must be " #value)

/* SRT_TRACEBSTATS (srt_bstats, srt_bistats): libsrt fills it without a size argument. */
SRTNET_SIZE(NativeStatistics, SRT_TRACEBSTATS, 496);
SRTNET_FIELD(NativeStatistics.TimestampMilliseconds, SRT_TRACEBSTATS, msTimeStamp, int64_t, 0);
SRTNET_FIELD(NativeStatistics.PacketsSentTotal, SRT_TRACEBSTATS, pktSentTotal, int64_t, 8);
SRTNET_FIELD(NativeStatistics.PacketsReceivedTotal, SRT_TRACEBSTATS, pktRecvTotal, int64_t, 16);
SRTNET_FIELD(NativeStatistics.PacketsSendLossTotal, SRT_TRACEBSTATS, pktSndLossTotal, int, 24);
SRTNET_FIELD(NativeStatistics.PacketsReceivedLossTotal, SRT_TRACEBSTATS, pktRcvLossTotal, int, 28);
SRTNET_FIELD(NativeStatistics.PacketsRetransmittedTotal, SRT_TRACEBSTATS, pktRetransTotal, int, 32);
SRTNET_FIELD(NativeStatistics.PacketsSentAckTotal, SRT_TRACEBSTATS, pktSentACKTotal, int, 36);
SRTNET_FIELD(NativeStatistics.PacketsReceivedAckTotal, SRT_TRACEBSTATS, pktRecvACKTotal, int, 40);
SRTNET_FIELD(NativeStatistics.PacketsSentNakTotal, SRT_TRACEBSTATS, pktSentNAKTotal, int, 44);
SRTNET_FIELD(NativeStatistics.PacketsReceivedNakTotal, SRT_TRACEBSTATS, pktRecvNAKTotal, int, 48);
SRTNET_FIELD(NativeStatistics.MicrosecondsSendDurationTotal, SRT_TRACEBSTATS, usSndDurationTotal, int64_t, 56);
SRTNET_FIELD(NativeStatistics.PacketsSendDropTotal, SRT_TRACEBSTATS, pktSndDropTotal, int, 64);
SRTNET_FIELD(NativeStatistics.PacketsReceivedDropTotal, SRT_TRACEBSTATS, pktRcvDropTotal, int, 68);
SRTNET_FIELD(NativeStatistics.PacketsReceivedUndecryptedTotal, SRT_TRACEBSTATS, pktRcvUndecryptTotal, int, 72);
SRTNET_FIELD(NativeStatistics.BytesSentTotal, SRT_TRACEBSTATS, byteSentTotal, uint64_t, 80);
SRTNET_FIELD(NativeStatistics.BytesReceivedTotal, SRT_TRACEBSTATS, byteRecvTotal, uint64_t, 88);
SRTNET_FIELD(NativeStatistics.BytesReceivedLossTotal, SRT_TRACEBSTATS, byteRcvLossTotal, uint64_t, 96);
SRTNET_FIELD(NativeStatistics.BytesRetransmittedTotal, SRT_TRACEBSTATS, byteRetransTotal, uint64_t, 104);
SRTNET_FIELD(NativeStatistics.BytesSendDropTotal, SRT_TRACEBSTATS, byteSndDropTotal, uint64_t, 112);
SRTNET_FIELD(NativeStatistics.BytesReceivedDropTotal, SRT_TRACEBSTATS, byteRcvDropTotal, uint64_t, 120);
SRTNET_FIELD(NativeStatistics.BytesReceivedUndecryptedTotal, SRT_TRACEBSTATS, byteRcvUndecryptTotal, uint64_t, 128);
SRTNET_FIELD(NativeStatistics.PacketsSent, SRT_TRACEBSTATS, pktSent, int64_t, 136);
SRTNET_FIELD(NativeStatistics.PacketsReceived, SRT_TRACEBSTATS, pktRecv, int64_t, 144);
SRTNET_FIELD(NativeStatistics.PacketsSendLoss, SRT_TRACEBSTATS, pktSndLoss, int, 152);
SRTNET_FIELD(NativeStatistics.PacketsReceivedLoss, SRT_TRACEBSTATS, pktRcvLoss, int, 156);
SRTNET_FIELD(NativeStatistics.PacketsRetransmitted, SRT_TRACEBSTATS, pktRetrans, int, 160);
SRTNET_FIELD(NativeStatistics.PacketsReceivedRetransmitted, SRT_TRACEBSTATS, pktRcvRetrans, int, 164);
SRTNET_FIELD(NativeStatistics.PacketsSentAck, SRT_TRACEBSTATS, pktSentACK, int, 168);
SRTNET_FIELD(NativeStatistics.PacketsReceivedAck, SRT_TRACEBSTATS, pktRecvACK, int, 172);
SRTNET_FIELD(NativeStatistics.PacketsSentNak, SRT_TRACEBSTATS, pktSentNAK, int, 176);
SRTNET_FIELD(NativeStatistics.PacketsReceivedNak, SRT_TRACEBSTATS, pktRecvNAK, int, 180);
SRTNET_FIELD(NativeStatistics.SendRateMegabitsPerSecond, SRT_TRACEBSTATS, mbpsSendRate, double, 184);
SRTNET_FIELD(NativeStatistics.ReceiveRateMegabitsPerSecond, SRT_TRACEBSTATS, mbpsRecvRate, double, 192);
SRTNET_FIELD(NativeStatistics.MicrosecondsSendDuration, SRT_TRACEBSTATS, usSndDuration, int64_t, 200);
SRTNET_FIELD(NativeStatistics.PacketsReorderDistance, SRT_TRACEBSTATS, pktReorderDistance, int, 208);
SRTNET_FIELD(NativeStatistics.AverageLateArrivalMilliseconds, SRT_TRACEBSTATS, pktRcvAvgBelatedTime, double, 216);
SRTNET_FIELD(NativeStatistics.PacketsReceivedBelated, SRT_TRACEBSTATS, pktRcvBelated, int64_t, 224);
SRTNET_FIELD(NativeStatistics.PacketsSendDrop, SRT_TRACEBSTATS, pktSndDrop, int, 232);
SRTNET_FIELD(NativeStatistics.PacketsReceivedDrop, SRT_TRACEBSTATS, pktRcvDrop, int, 236);
SRTNET_FIELD(NativeStatistics.PacketsReceivedUndecrypted, SRT_TRACEBSTATS, pktRcvUndecrypt, int, 240);
SRTNET_FIELD(NativeStatistics.BytesSent, SRT_TRACEBSTATS, byteSent, uint64_t, 248);
SRTNET_FIELD(NativeStatistics.BytesReceived, SRT_TRACEBSTATS, byteRecv, uint64_t, 256);
SRTNET_FIELD(NativeStatistics.BytesReceivedLoss, SRT_TRACEBSTATS, byteRcvLoss, uint64_t, 264);
SRTNET_FIELD(NativeStatistics.BytesRetransmitted, SRT_TRACEBSTATS, byteRetrans, uint64_t, 272);
SRTNET_FIELD(NativeStatistics.BytesSendDrop, SRT_TRACEBSTATS, byteSndDrop, uint64_t, 280);
SRTNET_FIELD(NativeStatistics.BytesReceivedDrop, SRT_TRACEBSTATS, byteRcvDrop, uint64_t, 288);
SRTNET_FIELD(NativeStatistics.BytesReceivedUndecrypted, SRT_TRACEBSTATS, byteRcvUndecrypt, uint64_t, 296);
SRTNET_FIELD(NativeStatistics.SendPeriodMicroseconds, SRT_TRACEBSTATS, usPktSndPeriod, double, 304);
SRTNET_FIELD(NativeStatistics.PacketsFlowWindow, SRT_TRACEBSTATS, pktFlowWindow, int, 312);
SRTNET_FIELD(NativeStatistics.PacketsCongestionWindow, SRT_TRACEBSTATS, pktCongestionWindow, int, 316);
SRTNET_FIELD(NativeStatistics.PacketsFlightSize, SRT_TRACEBSTATS, pktFlightSize, int, 320);
SRTNET_FIELD(NativeStatistics.RoundTripTimeMilliseconds, SRT_TRACEBSTATS, msRTT, double, 328);
SRTNET_FIELD(NativeStatistics.BandwidthMegabitsPerSecond, SRT_TRACEBSTATS, mbpsBandwidth, double, 336);
SRTNET_FIELD(NativeStatistics.BytesAvailableSendBuffer, SRT_TRACEBSTATS, byteAvailSndBuf, int, 344);
SRTNET_FIELD(NativeStatistics.BytesAvailableReceivedBuffer, SRT_TRACEBSTATS, byteAvailRcvBuf, int, 348);
SRTNET_FIELD(NativeStatistics.MaximumBandwidthMegabitsPerSecond, SRT_TRACEBSTATS, mbpsMaxBW, double, 352);
SRTNET_FIELD(NativeStatistics.MaximumSegmentSizeBytes, SRT_TRACEBSTATS, byteMSS, int, 360);
SRTNET_FIELD(NativeStatistics.PacketsSendBuffer, SRT_TRACEBSTATS, pktSndBuf, int, 364);
SRTNET_FIELD(NativeStatistics.BytesSendBuffer, SRT_TRACEBSTATS, byteSndBuf, int, 368);
SRTNET_FIELD(NativeStatistics.MillisecondsSendBuffer, SRT_TRACEBSTATS, msSndBuf, int, 372);
SRTNET_FIELD(NativeStatistics.MillisecondsSendDeliveryDelay, SRT_TRACEBSTATS, msSndTsbPdDelay, int, 376);
SRTNET_FIELD(NativeStatistics.PacketsReceivedBuffer, SRT_TRACEBSTATS, pktRcvBuf, int, 380);
SRTNET_FIELD(NativeStatistics.BytesReceivedBuffer, SRT_TRACEBSTATS, byteRcvBuf, int, 384);
SRTNET_FIELD(NativeStatistics.MillisecondsReceivedBuffer, SRT_TRACEBSTATS, msRcvBuf, int, 388);
SRTNET_FIELD(NativeStatistics.MillisecondsReceivedDeliveryDelay, SRT_TRACEBSTATS, msRcvTsbPdDelay, int, 392);
SRTNET_FIELD(NativeStatistics.PacketsSendFilterExtraTotal, SRT_TRACEBSTATS, pktSndFilterExtraTotal, int, 396);
SRTNET_FIELD(NativeStatistics.PacketsReceivedFilterExtraTotal, SRT_TRACEBSTATS, pktRcvFilterExtraTotal, int, 400);
SRTNET_FIELD(NativeStatistics.PacketsReceivedFilterSupplyTotal, SRT_TRACEBSTATS, pktRcvFilterSupplyTotal, int, 404);
SRTNET_FIELD(NativeStatistics.PacketsReceivedFilterLossTotal, SRT_TRACEBSTATS, pktRcvFilterLossTotal, int, 408);
SRTNET_FIELD(NativeStatistics.PacketsSendFilterExtra, SRT_TRACEBSTATS, pktSndFilterExtra, int, 412);
SRTNET_FIELD(NativeStatistics.PacketsReceivedFilterExtra, SRT_TRACEBSTATS, pktRcvFilterExtra, int, 416);
SRTNET_FIELD(NativeStatistics.PacketsReceivedFilterSupply, SRT_TRACEBSTATS, pktRcvFilterSupply, int, 420);
SRTNET_FIELD(NativeStatistics.PacketsReceivedFilterLoss, SRT_TRACEBSTATS, pktRcvFilterLoss, int, 424);
SRTNET_FIELD(NativeStatistics.PacketsReorderTolerance, SRT_TRACEBSTATS, pktReorderTolerance, int, 428);
SRTNET_FIELD(NativeStatistics.PacketsSentUniqueTotal, SRT_TRACEBSTATS, pktSentUniqueTotal, int64_t, 432);
SRTNET_FIELD(NativeStatistics.PacketsReceivedUniqueTotal, SRT_TRACEBSTATS, pktRecvUniqueTotal, int64_t, 440);
SRTNET_FIELD(NativeStatistics.BytesSentUniqueTotal, SRT_TRACEBSTATS, byteSentUniqueTotal, uint64_t, 448);
SRTNET_FIELD(NativeStatistics.BytesReceivedUniqueTotal, SRT_TRACEBSTATS, byteRecvUniqueTotal, uint64_t, 456);
SRTNET_FIELD(NativeStatistics.PacketsSentUnique, SRT_TRACEBSTATS, pktSentUnique, int64_t, 464);
SRTNET_FIELD(NativeStatistics.PacketsReceivedUnique, SRT_TRACEBSTATS, pktRecvUnique, int64_t, 472);
SRTNET_FIELD(NativeStatistics.BytesSentUnique, SRT_TRACEBSTATS, byteSentUnique, uint64_t, 480);
SRTNET_FIELD(NativeStatistics.BytesReceivedUnique, SRT_TRACEBSTATS, byteRecvUnique, uint64_t, 488);

/* SRT_MSGCTRL (srt_sendmsg2, srt_recvmsg2, srt_msgctrl_init). */
SRTNET_SIZE(NativeMessageControl, SRT_MSGCTRL, 48);
SRTNET_FIELD(NativeMessageControl.Flags, SRT_MSGCTRL, flags, int, 0);
SRTNET_FIELD(NativeMessageControl.TimeToLive, SRT_MSGCTRL, msgttl, int, 4);
SRTNET_FIELD(NativeMessageControl.InOrder, SRT_MSGCTRL, inorder, int, 8);
SRTNET_FIELD(NativeMessageControl.Boundary, SRT_MSGCTRL, boundary, int, 12);
SRTNET_FIELD(NativeMessageControl.SourceTime, SRT_MSGCTRL, srctime, int64_t, 16);
SRTNET_FIELD(NativeMessageControl.PacketSequence, SRT_MSGCTRL, pktseq, int32_t, 24);
SRTNET_FIELD(NativeMessageControl.MessageNumber, SRT_MSGCTRL, msgno, int32_t, 28);
SRTNET_FIELD(NativeMessageControl.GroupData, SRT_MSGCTRL, grpdata, SRT_SOCKGROUPDATA *, 32);
SRTNET_FIELD(NativeMessageControl.GroupDataSize, SRT_MSGCTRL, grpdata_size, size_t, 40);

/* SRT_EPOLL_EVENT (srt_epoll_uwait). */
SRTNET_SIZE(NativePollEvent, SRT_EPOLL_EVENT, 8);
SRTNET_FIELD(NativePollEvent.Socket, SRT_EPOLL_EVENT, fd, SRTSOCKET, 0);
SRTNET_FIELD(NativePollEvent.Events, SRT_EPOLL_EVENT, events, int, 4);

/* Socket option identifiers (SRT_SOCKOPT). */
SRTNET_VALUE(SrtOptions.MaximumSegmentSize, SRTO_MSS, 0);
SRTNET_VALUE(SrtOptions.InitialSequenceNumber, SRTO_ISN, 3);
SRTNET_VALUE(SrtOptions.FlowControlWindow, SRTO_FC, 4);
SRTNET_VALUE(SrtOptions.Linger, SRTO_LINGER, 7);
SRTNET_VALUE(SrtOptions.UdpSendBufferSize, SRTO_UDP_SNDBUF, 8);
SRTNET_VALUE(SrtOptions.UdpReceiveBufferSize, SRTO_UDP_RCVBUF, 9);
SRTNET_VALUE(SrtOptions.AvailableEvents, SRTO_EVENT, 18);
SRTNET_VALUE(SrtOptions.PendingSendPackets, SRTO_SNDDATA, 19);
SRTNET_VALUE(SrtOptions.PendingReceivePackets, SRTO_RCVDATA, 20);
SRTNET_VALUE(SrtOptions.TimestampBasedDelivery, SRTO_TSBPDMODE, 22);
SRTNET_VALUE(SrtOptions.IpTimeToLive, SRTO_IPTTL, 29);
SRTNET_VALUE(SrtOptions.IpTypeOfService, SRTO_IPTOS, 30);
SRTNET_VALUE(SrtOptions.SendDropDelay, SRTO_SNDDROPDELAY, 32);
SRTNET_VALUE(SrtOptions.DriftTracer, SRTO_DRIFTTRACER, 37);
SRTNET_VALUE(SrtOptions.MinimumInputBandwidth, SRTO_MININPUTBW, 38);
SRTNET_VALUE(SrtOptions.MaximumReorderTolerance, SRTO_LOSSMAXTTL, 42);
SRTNET_VALUE(SrtOptions.MinimumPeerVersion, SRTO_MINVERSION, 45);
SRTNET_VALUE(SrtOptions.CongestionController, SRTO_CONGESTION, 47);
SRTNET_VALUE(SrtOptions.KeyRefreshRate, SRTO_KMREFRESHRATE, 51);
SRTNET_VALUE(SrtOptions.KeyPreannounce, SRTO_KMPREANNOUNCE, 52);
SRTNET_VALUE(SrtOptions.BindToDevice, SRTO_BINDTODEVICE, 56);
SRTNET_VALUE(SrtOptions.PacketFilter, SRTO_PACKETFILTER, 60);
SRTNET_VALUE(SrtOptions.RetransmissionAlgorithm, SRTO_RETRANSMITALGO, 61);
SRTNET_VALUE(SrtOptions.TransmissionType, SRTO_TRANSTYPE, 50);
SRTNET_VALUE(SrtOptions.MessageApi, SRTO_MESSAGEAPI, 48);
SRTNET_VALUE(SrtOptions.StreamId, SRTO_STREAMID, 46);
SRTNET_VALUE(SrtOptions.Passphrase, SRTO_PASSPHRASE, 26);
SRTNET_VALUE(SrtOptions.EncryptionKeySize, SRTO_PBKEYLEN, 27);
SRTNET_VALUE(SrtOptions.EnforcedEncryption, SRTO_ENFORCEDENCRYPTION, 53);
SRTNET_VALUE(SrtOptions.ReceiveLatency, SRTO_RCVLATENCY, 43);
SRTNET_VALUE(SrtOptions.PeerLatency, SRTO_PEERLATENCY, 44);
SRTNET_VALUE(SrtOptions.PayloadSize, SRTO_PAYLOADSIZE, 49);
SRTNET_VALUE(SrtOptions.SendBufferSize, SRTO_SNDBUF, 5);
SRTNET_VALUE(SrtOptions.ReceiveBufferSize, SRTO_RCVBUF, 6);
SRTNET_VALUE(SrtOptions.MaximumBandwidth, SRTO_MAXBW, 16);
SRTNET_VALUE(SrtOptions.InputBandwidth, SRTO_INPUTBW, 24);
SRTNET_VALUE(SrtOptions.OverheadBandwidth, SRTO_OHEADBW, 25);
SRTNET_VALUE(SrtOptions.ConnectTimeout, SRTO_CONNTIMEO, 36);
SRTNET_VALUE(SrtOptions.PeerIdleTimeout, SRTO_PEERIDLETIMEO, 55);
SRTNET_VALUE(SrtOptions.Rendezvous, SRTO_RENDEZVOUS, 12);
SRTNET_VALUE(SrtOptions.ReuseAddress, SRTO_REUSEADDR, 15);
SRTNET_VALUE(SrtOptions.IPv6Only, SRTO_IPV6ONLY, 54);
SRTNET_VALUE(SrtOptions.TooLatePacketDrop, SRTO_TLPKTDROP, 31);
SRTNET_VALUE(SrtOptions.NegativeAcknowledgmentReports, SRTO_NAKREPORT, 33);
SRTNET_VALUE(SrtOptions.SendEncryptionState, SRTO_SNDKMSTATE, 40);
SRTNET_VALUE(SrtOptions.ReceiveEncryptionState, SRTO_RCVKMSTATE, 41);
SRTNET_VALUE(SrtOptions.PeerVersion, SRTO_PEERVERSION, 35);

/* Internal constants (NativeConstants). */
SRTNET_VALUE(NativeConstants.SRTO_SNDSYN, SRTO_SNDSYN, 1);
SRTNET_VALUE(NativeConstants.SRTO_RCVSYN, SRTO_RCVSYN, 2);
SRTNET_VALUE(NativeConstants.SRTO_EVENT, SRTO_EVENT, 18);
SRTNET_VALUE(NativeConstants.SRT_EPOLL_IN, SRT_EPOLL_IN, 1);
SRTNET_VALUE(NativeConstants.SRT_EPOLL_OUT, SRT_EPOLL_OUT, 4);
SRTNET_VALUE(NativeConstants.SRT_EPOLL_ERR, SRT_EPOLL_ERR, 8);
SRTNET_VALUE(NativeConstants.SRT_EPOLL_ET, SRT_EPOLL_ET, 0x80000000);
SRTNET_VALUE(NativeConstants.SRT_EPOLL_ENABLE_EMPTY, SRT_EPOLL_ENABLE_EMPTY, 1);

/* Error codes (SRT_ERRNO). */
SRTNET_VALUE(SrtErrorCode.Unknown, SRT_EUNKNOWN, -1);
SRTNET_VALUE(SrtErrorCode.Success, SRT_SUCCESS, 0);
SRTNET_VALUE(SrtErrorCode.ConnectionSetupFailed, SRT_ECONNSETUP, 1000);
SRTNET_VALUE(SrtErrorCode.NoServer, SRT_ENOSERVER, 1001);
SRTNET_VALUE(SrtErrorCode.ConnectionRejected, SRT_ECONNREJ, 1002);
SRTNET_VALUE(SrtErrorCode.SocketFailed, SRT_ESOCKFAIL, 1003);
SRTNET_VALUE(SrtErrorCode.SecurityFailed, SRT_ESECFAIL, 1004);
SRTNET_VALUE(SrtErrorCode.SocketClosed, SRT_ESCLOSED, 1005);
SRTNET_VALUE(SrtErrorCode.ConnectionFailed, SRT_ECONNFAIL, 2000);
SRTNET_VALUE(SrtErrorCode.ConnectionLost, SRT_ECONNLOST, 2001);
SRTNET_VALUE(SrtErrorCode.NotConnected, SRT_ENOCONN, 2002);
SRTNET_VALUE(SrtErrorCode.ResourceFailed, SRT_ERESOURCE, 3000);
SRTNET_VALUE(SrtErrorCode.ThreadFailed, SRT_ETHREAD, 3001);
SRTNET_VALUE(SrtErrorCode.NoBuffer, SRT_ENOBUF, 3002);
SRTNET_VALUE(SrtErrorCode.SystemObjectFailed, SRT_ESYSOBJ, 3003);
SRTNET_VALUE(SrtErrorCode.FileFailed, SRT_EFILE, 4000);
SRTNET_VALUE(SrtErrorCode.InvalidReadOffset, SRT_EINVRDOFF, 4001);
SRTNET_VALUE(SrtErrorCode.ReadPermissionDenied, SRT_ERDPERM, 4002);
SRTNET_VALUE(SrtErrorCode.InvalidWriteOffset, SRT_EINVWROFF, 4003);
SRTNET_VALUE(SrtErrorCode.WritePermissionDenied, SRT_EWRPERM, 4004);
SRTNET_VALUE(SrtErrorCode.InvalidOperation, SRT_EINVOP, 5000);
SRTNET_VALUE(SrtErrorCode.AlreadyBound, SRT_EBOUNDSOCK, 5001);
SRTNET_VALUE(SrtErrorCode.AlreadyConnected, SRT_ECONNSOCK, 5002);
SRTNET_VALUE(SrtErrorCode.InvalidParameter, SRT_EINVPARAM, 5003);
SRTNET_VALUE(SrtErrorCode.InvalidSocket, SRT_EINVSOCK, 5004);
SRTNET_VALUE(SrtErrorCode.NotBound, SRT_EUNBOUNDSOCK, 5005);
SRTNET_VALUE(SrtErrorCode.NotListening, SRT_ENOLISTEN, 5006);
SRTNET_VALUE(SrtErrorCode.RendezvousNoServer, SRT_ERDVNOSERV, 5007);
SRTNET_VALUE(SrtErrorCode.RendezvousUnbound, SRT_ERDVUNBOUND, 5008);
SRTNET_VALUE(SrtErrorCode.InvalidMessageApi, SRT_EINVALMSGAPI, 5009);
SRTNET_VALUE(SrtErrorCode.InvalidBufferApi, SRT_EINVALBUFFERAPI, 5010);
SRTNET_VALUE(SrtErrorCode.DuplicateListener, SRT_EDUPLISTEN, 5011);
SRTNET_VALUE(SrtErrorCode.MessageTooLarge, SRT_ELARGEMSG, 5012);
SRTNET_VALUE(SrtErrorCode.InvalidPollId, SRT_EINVPOLLID, 5013);
SRTNET_VALUE(SrtErrorCode.PollEmpty, SRT_EPOLLEMPTY, 5014);
SRTNET_VALUE(SrtErrorCode.BindConflict, SRT_EBINDCONFLICT, 5015);
SRTNET_VALUE(SrtErrorCode.WouldBlock, SRT_EASYNCFAIL, 6000);
SRTNET_VALUE(SrtErrorCode.SendWouldBlock, SRT_EASYNCSND, 6001);
SRTNET_VALUE(SrtErrorCode.ReceiveWouldBlock, SRT_EASYNCRCV, 6002);
SRTNET_VALUE(SrtErrorCode.TimedOut, SRT_ETIMEOUT, 6003);
SRTNET_VALUE(SrtErrorCode.Congestion, SRT_ECONGEST, 6004);
SRTNET_VALUE(SrtErrorCode.PeerError, SRT_EPEERERR, 7000);

/* Rejection reasons (SRT_REJECT_REASON, access_control.h). */
SRTNET_VALUE(SrtRejectReason.Unknown, SRT_REJ_UNKNOWN, 0);
SRTNET_VALUE(SrtRejectReason.System, SRT_REJ_SYSTEM, 1);
SRTNET_VALUE(SrtRejectReason.Peer, SRT_REJ_PEER, 2);
SRTNET_VALUE(SrtRejectReason.Resource, SRT_REJ_RESOURCE, 3);
SRTNET_VALUE(SrtRejectReason.Rogue, SRT_REJ_ROGUE, 4);
SRTNET_VALUE(SrtRejectReason.Backlog, SRT_REJ_BACKLOG, 5);
SRTNET_VALUE(SrtRejectReason.InternalError, SRT_REJ_IPE, 6);
SRTNET_VALUE(SrtRejectReason.Closing, SRT_REJ_CLOSE, 7);
SRTNET_VALUE(SrtRejectReason.Version, SRT_REJ_VERSION, 8);
SRTNET_VALUE(SrtRejectReason.RendezvousCookie, SRT_REJ_RDVCOOKIE, 9);
SRTNET_VALUE(SrtRejectReason.BadSecret, SRT_REJ_BADSECRET, 10);
SRTNET_VALUE(SrtRejectReason.Unsecure, SRT_REJ_UNSECURE, 11);
SRTNET_VALUE(SrtRejectReason.MessageApi, SRT_REJ_MESSAGEAPI, 12);
SRTNET_VALUE(SrtRejectReason.Congestion, SRT_REJ_CONGESTION, 13);
SRTNET_VALUE(SrtRejectReason.Filter, SRT_REJ_FILTER, 14);
SRTNET_VALUE(SrtRejectReason.Group, SRT_REJ_GROUP, 15);
SRTNET_VALUE(SrtRejectReason.Timeout, SRT_REJ_TIMEOUT, 16);
/* Only in libsrt builds with the AEAD preview API, which the bundled build does not enable. */
#ifdef ENABLE_AEAD_API_PREVIEW
SRTNET_VALUE(SrtRejectReason.Crypto, SRT_REJ_CRYPTO, 17);
#endif
SRTNET_VALUE(SrtRejectReason.Fallback, SRT_REJX_FALLBACK, 1000);
SRTNET_VALUE(SrtRejectReason.KeyNotSupported, SRT_REJX_KEY_NOTSUP, 1001);
SRTNET_VALUE(SrtRejectReason.FilePath, SRT_REJX_FILEPATH, 1002);
SRTNET_VALUE(SrtRejectReason.HostNotFound, SRT_REJX_HOSTNOTFOUND, 1003);
SRTNET_VALUE(SrtRejectReason.BadRequest, SRT_REJX_BAD_REQUEST, 1400);
SRTNET_VALUE(SrtRejectReason.Unauthorized, SRT_REJX_UNAUTHORIZED, 1401);
SRTNET_VALUE(SrtRejectReason.Overload, SRT_REJX_OVERLOAD, 1402);
SRTNET_VALUE(SrtRejectReason.Forbidden, SRT_REJX_FORBIDDEN, 1403);
SRTNET_VALUE(SrtRejectReason.NotFound, SRT_REJX_NOTFOUND, 1404);
SRTNET_VALUE(SrtRejectReason.BadMode, SRT_REJX_BAD_MODE, 1405);
SRTNET_VALUE(SrtRejectReason.Unacceptable, SRT_REJX_UNACCEPTABLE, 1406);
SRTNET_VALUE(SrtRejectReason.Conflict, SRT_REJX_CONFLICT, 1409);
SRTNET_VALUE(SrtRejectReason.NotSupportedMedia, SRT_REJX_NOTSUP_MEDIA, 1415);
SRTNET_VALUE(SrtRejectReason.Locked, SRT_REJX_LOCKED, 1423);
SRTNET_VALUE(SrtRejectReason.FailedDependency, SRT_REJX_FAILED_DEPEND, 1424);
SRTNET_VALUE(SrtRejectReason.InternalServerError, SRT_REJX_ISE, 1500);
SRTNET_VALUE(SrtRejectReason.NotImplemented, SRT_REJX_UNIMPLEMENTED, 1501);
SRTNET_VALUE(SrtRejectReason.Gateway, SRT_REJX_GW, 1502);
SRTNET_VALUE(SrtRejectReason.ServiceUnavailable, SRT_REJX_DOWN, 1503);
SRTNET_VALUE(SrtRejectReason.VersionNotSupported, SRT_REJX_VERSION, 1505);
SRTNET_VALUE(SrtRejectReason.NoRoom, SRT_REJX_NOROOM, 1507);
SRTNET_VALUE(SrtRejectReason.UserDefined, SRT_REJC_USERDEFINED, 2000);

/* Key material states (SRT_KM_STATE). */
SRTNET_VALUE(SrtKeyMaterialState.Unsecured, SRT_KM_S_UNSECURED, 0);
SRTNET_VALUE(SrtKeyMaterialState.Securing, SRT_KM_S_SECURING, 1);
SRTNET_VALUE(SrtKeyMaterialState.Secured, SRT_KM_S_SECURED, 2);
SRTNET_VALUE(SrtKeyMaterialState.NoSecret, SRT_KM_S_NOSECRET, 3);
SRTNET_VALUE(SrtKeyMaterialState.BadSecret, SRT_KM_S_BADSECRET, 4);
SRTNET_VALUE(SrtKeyMaterialState.BadCryptoMode, SRT_KM_S_BADCRYPTOMODE, 5);

/* Socket states (SRT_SOCKSTATUS). */
SRTNET_VALUE(SrtSocketState.Initialized, SRTS_INIT, 1);
SRTNET_VALUE(SrtSocketState.Opened, SRTS_OPENED, 2);
SRTNET_VALUE(SrtSocketState.Listening, SRTS_LISTENING, 3);
SRTNET_VALUE(SrtSocketState.Connecting, SRTS_CONNECTING, 4);
SRTNET_VALUE(SrtSocketState.Connected, SRTS_CONNECTED, 5);
SRTNET_VALUE(SrtSocketState.Broken, SRTS_BROKEN, 6);
SRTNET_VALUE(SrtSocketState.Closing, SRTS_CLOSING, 7);
SRTNET_VALUE(SrtSocketState.Closed, SRTS_CLOSED, 8);
SRTNET_VALUE(SrtSocketState.Nonexistent, SRTS_NONEXIST, 9);

/* Transmission types (SRT_TRANSTYPE). */
SRTNET_VALUE(SrtTransmissionType.Live, SRTT_LIVE, 0);
SRTNET_VALUE(SrtTransmissionType.File, SRTT_FILE, 1);

/* Log functional areas (SRT_LOGFA_*). */
SRTNET_VALUE(SrtLogArea.General, SRT_LOGFA_GENERAL, 0);
SRTNET_VALUE(SrtLogArea.SocketManagement, SRT_LOGFA_SOCKMGMT, 1);
SRTNET_VALUE(SrtLogArea.Connection, SRT_LOGFA_CONN, 2);
SRTNET_VALUE(SrtLogArea.Timers, SRT_LOGFA_XTIMER, 3);
SRTNET_VALUE(SrtLogArea.TimestampDelivery, SRT_LOGFA_TSBPD, 4);
SRTNET_VALUE(SrtLogArea.Resources, SRT_LOGFA_RSRC, 5);
SRTNET_VALUE(SrtLogArea.Congestion, SRT_LOGFA_CONGEST, 7);
SRTNET_VALUE(SrtLogArea.PacketFilter, SRT_LOGFA_PFILTER, 8);
SRTNET_VALUE(SrtLogArea.ControlApi, SRT_LOGFA_API_CTRL, 11);
SRTNET_VALUE(SrtLogArea.ControlQueue, SRT_LOGFA_QUE_CTRL, 13);
SRTNET_VALUE(SrtLogArea.PollUpdates, SRT_LOGFA_EPOLL_UPD, 16);
SRTNET_VALUE(SrtLogArea.ReceiveApi, SRT_LOGFA_API_RECV, 21);
SRTNET_VALUE(SrtLogArea.ReceiveBuffer, SRT_LOGFA_BUF_RECV, 22);
SRTNET_VALUE(SrtLogArea.ReceiveQueue, SRT_LOGFA_QUE_RECV, 23);
SRTNET_VALUE(SrtLogArea.ReceiveChannel, SRT_LOGFA_CHN_RECV, 24);
SRTNET_VALUE(SrtLogArea.ReceiveGroup, SRT_LOGFA_GRP_RECV, 25);
SRTNET_VALUE(SrtLogArea.SendApi, SRT_LOGFA_API_SEND, 31);
SRTNET_VALUE(SrtLogArea.SendBuffer, SRT_LOGFA_BUF_SEND, 32);
SRTNET_VALUE(SrtLogArea.SendQueue, SRT_LOGFA_QUE_SEND, 33);
SRTNET_VALUE(SrtLogArea.SendChannel, SRT_LOGFA_CHN_SEND, 34);
SRTNET_VALUE(SrtLogArea.SendGroup, SRT_LOGFA_GRP_SEND, 35);
SRTNET_VALUE(SrtLogArea.Internal, SRT_LOGFA_INTERNAL, 41);
SRTNET_VALUE(SrtLogArea.QueueManagement, SRT_LOGFA_QUE_MGMT, 43);
SRTNET_VALUE(SrtLogArea.ChannelManagement, SRT_LOGFA_CHN_MGMT, 44);
SRTNET_VALUE(SrtLogArea.GroupManagement, SRT_LOGFA_GRP_MGMT, 45);
SRTNET_VALUE(SrtLogArea.PollApi, SRT_LOGFA_EPOLL_API, 46);
SRTNET_VALUE(SrtLogArea.Encryption, SRT_LOGFA_HAICRYPT, 6);
SRTNET_VALUE(SrtLogArea.Application, SRT_LOGFA_APPLOG, 10);

/* Log levels (syslog LOG_* values used by srt_setloglevel). */
SRTNET_VALUE(SrtLogLevel.Fatal, LOG_CRIT, 2);
SRTNET_VALUE(SrtLogLevel.Error, LOG_ERR, 3);
SRTNET_VALUE(SrtLogLevel.Warning, LOG_WARNING, 4);
SRTNET_VALUE(SrtLogLevel.Notice, LOG_NOTICE, 5);
SRTNET_VALUE(SrtLogLevel.Information, LOG_INFO, 6);
SRTNET_VALUE(SrtLogLevel.Debug, LOG_DEBUG, 7);

/* Log formatting flags (SRT_LOGF_*, srt_setlogflags). */
SRTNET_VALUE(SrtLogFormat.OmitTime, SRT_LOGF_DISABLE_TIME, 1);
SRTNET_VALUE(SrtLogFormat.OmitThreadName, SRT_LOGF_DISABLE_THREADNAME, 2);
SRTNET_VALUE(SrtLogFormat.OmitSeverity, SRT_LOGF_DISABLE_SEVERITY, 4);
SRTNET_VALUE(SrtLogFormat.OmitNewline, SRT_LOGF_DISABLE_EOL, 8);

/* Readiness events (SRT_EPOLL_OPT). */
SRTNET_VALUE(SrtPollEvents.Read, SRT_EPOLL_IN, 1);
SRTNET_VALUE(SrtPollEvents.Write, SRT_EPOLL_OUT, 4);
SRTNET_VALUE(SrtPollEvents.Error, SRT_EPOLL_ERR, 8);
