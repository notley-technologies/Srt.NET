// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using Srt.Net.Interop;

namespace Srt.Net;

/// <summary>An immutable snapshot of all 82 counters in the libsrt 1.5 statistics ABI.</summary>
/// <remarks>Totals span the connection lifetime. Other traffic counters span the interval since the last reset, except <see cref="PacketsReorderDistance"/>, which is a gauge. Buffer gauges may be instantaneous or averaged.</remarks>
public sealed class SrtStatistics
{
    private readonly NativeStatistics values;

    internal SrtStatistics(in NativeStatistics values)
    {
        this.values = values;
    }

    /// <summary>Native <c>msTimeStamp</c>: milliseconds, lifetime.</summary>
    public long TimestampMilliseconds => values.TimestampMilliseconds;

    /// <summary>Native <c>pktSentTotal</c>: packets, lifetime.</summary>
    public long PacketsSentTotal => values.PacketsSentTotal;

    /// <summary>Native <c>pktRecvTotal</c>: packets, lifetime.</summary>
    public long PacketsReceivedTotal => values.PacketsReceivedTotal;

    /// <summary>Native <c>pktSndLossTotal</c>: packets, lifetime.</summary>
    public int PacketsSendLossTotal => values.PacketsSendLossTotal;

    /// <summary>Native <c>pktRcvLossTotal</c>: packets, lifetime.</summary>
    public int PacketsReceivedLossTotal => values.PacketsReceivedLossTotal;

    /// <summary>Native <c>pktRetransTotal</c>: packets, lifetime.</summary>
    public int PacketsRetransmittedTotal => values.PacketsRetransmittedTotal;

    /// <summary>Native <c>pktSentACKTotal</c>: packets, lifetime.</summary>
    public int PacketsSentAckTotal => values.PacketsSentAckTotal;

    /// <summary>Native <c>pktRecvACKTotal</c>: packets, lifetime.</summary>
    public int PacketsReceivedAckTotal => values.PacketsReceivedAckTotal;

    /// <summary>Native <c>pktSentNAKTotal</c>: packets, lifetime.</summary>
    public int PacketsSentNakTotal => values.PacketsSentNakTotal;

    /// <summary>Native <c>pktRecvNAKTotal</c>: packets, lifetime.</summary>
    public int PacketsReceivedNakTotal => values.PacketsReceivedNakTotal;

    /// <summary>Native <c>usSndDurationTotal</c>: microseconds, lifetime.</summary>
    public long MicrosecondsSendDurationTotal => values.MicrosecondsSendDurationTotal;

    /// <summary>Native <c>pktSndDropTotal</c>: packets, lifetime.</summary>
    public int PacketsSendDropTotal => values.PacketsSendDropTotal;

    /// <summary>Native <c>pktRcvDropTotal</c>: packets, lifetime.</summary>
    public int PacketsReceivedDropTotal => values.PacketsReceivedDropTotal;

    /// <summary>Native <c>pktRcvUndecryptTotal</c>: packets, lifetime.</summary>
    public int PacketsReceivedUndecryptedTotal => values.PacketsReceivedUndecryptedTotal;

    /// <summary>Native <c>byteSentTotal</c>: bytes, lifetime.</summary>
    public ulong BytesSentTotal => values.BytesSentTotal;

    /// <summary>Native <c>byteRecvTotal</c>: bytes, lifetime.</summary>
    public ulong BytesReceivedTotal => values.BytesReceivedTotal;

    /// <summary>Native <c>byteRcvLossTotal</c>: bytes, lifetime.</summary>
    public ulong BytesReceivedLossTotal => values.BytesReceivedLossTotal;

    /// <summary>Native <c>byteRetransTotal</c>: bytes, lifetime.</summary>
    public ulong BytesRetransmittedTotal => values.BytesRetransmittedTotal;

    /// <summary>Native <c>byteSndDropTotal</c>: bytes, lifetime.</summary>
    public ulong BytesSendDropTotal => values.BytesSendDropTotal;

    /// <summary>Native <c>byteRcvDropTotal</c>: bytes, lifetime.</summary>
    public ulong BytesReceivedDropTotal => values.BytesReceivedDropTotal;

    /// <summary>Native <c>byteRcvUndecryptTotal</c>: bytes, lifetime.</summary>
    public ulong BytesReceivedUndecryptedTotal => values.BytesReceivedUndecryptedTotal;

    /// <summary>Native <c>pktSent</c>: packets, since reset.</summary>
    public long PacketsSent => values.PacketsSent;

    /// <summary>Native <c>pktRecv</c>: packets, since reset.</summary>
    public long PacketsReceived => values.PacketsReceived;

    /// <summary>Native <c>pktSndLoss</c>: packets, since reset.</summary>
    public int PacketsSendLoss => values.PacketsSendLoss;

    /// <summary>Native <c>pktRcvLoss</c>: packets, since reset.</summary>
    public int PacketsReceivedLoss => values.PacketsReceivedLoss;

    /// <summary>Native <c>pktRetrans</c>: packets, since reset.</summary>
    public int PacketsRetransmitted => values.PacketsRetransmitted;

    /// <summary>Native <c>pktRcvRetrans</c>: packets, since reset.</summary>
    public int PacketsReceivedRetransmitted => values.PacketsReceivedRetransmitted;

    /// <summary>Native <c>pktSentACK</c>: packets, since reset.</summary>
    public int PacketsSentAck => values.PacketsSentAck;

    /// <summary>Native <c>pktRecvACK</c>: packets, since reset.</summary>
    public int PacketsReceivedAck => values.PacketsReceivedAck;

    /// <summary>Native <c>pktSentNAK</c>: packets, since reset.</summary>
    public int PacketsSentNak => values.PacketsSentNak;

    /// <summary>Native <c>pktRecvNAK</c>: packets, since reset.</summary>
    public int PacketsReceivedNak => values.PacketsReceivedNak;

    /// <summary>Native <c>mbpsSendRate</c>: megabits/second, since reset.</summary>
    public double SendRateMegabitsPerSecond => values.SendRateMegabitsPerSecond;

    /// <summary>Native <c>mbpsRecvRate</c>: megabits/second, since reset.</summary>
    public double ReceiveRateMegabitsPerSecond => values.ReceiveRateMegabitsPerSecond;

    /// <summary>Native <c>usSndDuration</c>: microseconds, since reset.</summary>
    public long MicrosecondsSendDuration => values.MicrosecondsSendDuration;

    /// <summary>
    /// Native <c>pktReorderDistance</c>: packets, gauge. The largest out-of-order distance seen on this connection;
    /// clearing statistics does not reset it.
    /// </summary>
    /// <remarks>
    /// When <see cref="SrtOptions.MaximumReorderTolerance"/> is above zero, libsrt also lowers this value by one each
    /// time it lowers the reorder tolerance, so it can fall below earlier peaks and below zero.
    /// </remarks>
    public int PacketsReorderDistance => values.PacketsReorderDistance;

    /// <summary>Native <c>pktRcvAvgBelatedTime</c>: milliseconds, gauge.</summary>
    public double AverageLateArrivalMilliseconds => values.AverageLateArrivalMilliseconds;

    /// <summary>Native <c>pktRcvBelated</c>: packets, since reset.</summary>
    public long PacketsReceivedBelated => values.PacketsReceivedBelated;

    /// <summary>Native <c>pktSndDrop</c>: packets, since reset.</summary>
    public int PacketsSendDrop => values.PacketsSendDrop;

    /// <summary>Native <c>pktRcvDrop</c>: packets, since reset.</summary>
    public int PacketsReceivedDrop => values.PacketsReceivedDrop;

    /// <summary>Native <c>pktRcvUndecrypt</c>: packets, since reset.</summary>
    public int PacketsReceivedUndecrypted => values.PacketsReceivedUndecrypted;

    /// <summary>Native <c>byteSent</c>: bytes, since reset.</summary>
    public ulong BytesSent => values.BytesSent;

    /// <summary>Native <c>byteRecv</c>: bytes, since reset.</summary>
    public ulong BytesReceived => values.BytesReceived;

    /// <summary>Native <c>byteRcvLoss</c>: bytes, since reset.</summary>
    public ulong BytesReceivedLoss => values.BytesReceivedLoss;

    /// <summary>Native <c>byteRetrans</c>: bytes, since reset.</summary>
    public ulong BytesRetransmitted => values.BytesRetransmitted;

    /// <summary>Native <c>byteSndDrop</c>: bytes, since reset.</summary>
    public ulong BytesSendDrop => values.BytesSendDrop;

    /// <summary>Native <c>byteRcvDrop</c>: bytes, since reset.</summary>
    public ulong BytesReceivedDrop => values.BytesReceivedDrop;

    /// <summary>Native <c>byteRcvUndecrypt</c>: bytes, since reset.</summary>
    public ulong BytesReceivedUndecrypted => values.BytesReceivedUndecrypted;

    /// <summary>Native <c>usPktSndPeriod</c>: microseconds, gauge.</summary>
    public double SendPeriodMicroseconds => values.SendPeriodMicroseconds;

    /// <summary>Native <c>pktFlowWindow</c>: packets, gauge.</summary>
    public int PacketsFlowWindow => values.PacketsFlowWindow;

    /// <summary>Native <c>pktCongestionWindow</c>: packets, gauge.</summary>
    public int PacketsCongestionWindow => values.PacketsCongestionWindow;

    /// <summary>Native <c>pktFlightSize</c>: packets, gauge.</summary>
    public int PacketsFlightSize => values.PacketsFlightSize;

    /// <summary>Native <c>msRTT</c>: milliseconds, gauge.</summary>
    public double RoundTripTimeMilliseconds => values.RoundTripTimeMilliseconds;

    /// <summary>Native <c>mbpsBandwidth</c>: megabits/second, gauge.</summary>
    public double BandwidthMegabitsPerSecond => values.BandwidthMegabitsPerSecond;

    /// <summary>Native <c>byteAvailSndBuf</c>: bytes, gauge.</summary>
    public int BytesAvailableSendBuffer => values.BytesAvailableSendBuffer;

    /// <summary>Native <c>byteAvailRcvBuf</c>: bytes, gauge.</summary>
    public int BytesAvailableReceivedBuffer => values.BytesAvailableReceivedBuffer;

    /// <summary>Native <c>mbpsMaxBW</c>: megabits/second, gauge.</summary>
    public double MaximumBandwidthMegabitsPerSecond => values.MaximumBandwidthMegabitsPerSecond;

    /// <summary>Native <c>byteMSS</c>: bytes, gauge.</summary>
    public int MaximumSegmentSizeBytes => values.MaximumSegmentSizeBytes;

    /// <summary>Native <c>pktSndBuf</c>: packets, gauge.</summary>
    public int PacketsSendBuffer => values.PacketsSendBuffer;

    /// <summary>Native <c>byteSndBuf</c>: bytes, gauge.</summary>
    public int BytesSendBuffer => values.BytesSendBuffer;

    /// <summary>Native <c>msSndBuf</c>: milliseconds, gauge.</summary>
    public int MillisecondsSendBuffer => values.MillisecondsSendBuffer;

    /// <summary>Native <c>msSndTsbPdDelay</c>: milliseconds, gauge.</summary>
    public int MillisecondsSendDeliveryDelay => values.MillisecondsSendDeliveryDelay;

    /// <summary>Native <c>pktRcvBuf</c>: packets, gauge.</summary>
    public int PacketsReceivedBuffer => values.PacketsReceivedBuffer;

    /// <summary>Native <c>byteRcvBuf</c>: bytes, gauge.</summary>
    public int BytesReceivedBuffer => values.BytesReceivedBuffer;

    /// <summary>Native <c>msRcvBuf</c>: milliseconds, gauge.</summary>
    public int MillisecondsReceivedBuffer => values.MillisecondsReceivedBuffer;

    /// <summary>Native <c>msRcvTsbPdDelay</c>: milliseconds, gauge.</summary>
    public int MillisecondsReceivedDeliveryDelay => values.MillisecondsReceivedDeliveryDelay;

    /// <summary>Native <c>pktSndFilterExtraTotal</c>: packets, lifetime.</summary>
    public int PacketsSendFilterExtraTotal => values.PacketsSendFilterExtraTotal;

    /// <summary>Native <c>pktRcvFilterExtraTotal</c>: packets, lifetime.</summary>
    public int PacketsReceivedFilterExtraTotal => values.PacketsReceivedFilterExtraTotal;

    /// <summary>Native <c>pktRcvFilterSupplyTotal</c>: packets, lifetime.</summary>
    public int PacketsReceivedFilterSupplyTotal => values.PacketsReceivedFilterSupplyTotal;

    /// <summary>Native <c>pktRcvFilterLossTotal</c>: packets, lifetime.</summary>
    public int PacketsReceivedFilterLossTotal => values.PacketsReceivedFilterLossTotal;

    /// <summary>Native <c>pktSndFilterExtra</c>: packets, since reset.</summary>
    public int PacketsSendFilterExtra => values.PacketsSendFilterExtra;

    /// <summary>Native <c>pktRcvFilterExtra</c>: packets, since reset.</summary>
    public int PacketsReceivedFilterExtra => values.PacketsReceivedFilterExtra;

    /// <summary>Native <c>pktRcvFilterSupply</c>: packets, since reset.</summary>
    public int PacketsReceivedFilterSupply => values.PacketsReceivedFilterSupply;

    /// <summary>Native <c>pktRcvFilterLoss</c>: packets, since reset.</summary>
    public int PacketsReceivedFilterLoss => values.PacketsReceivedFilterLoss;

    /// <summary>Native <c>pktReorderTolerance</c>: packets, gauge.</summary>
    public int PacketsReorderTolerance => values.PacketsReorderTolerance;

    /// <summary>Native <c>pktSentUniqueTotal</c>: packets, lifetime.</summary>
    public long PacketsSentUniqueTotal => values.PacketsSentUniqueTotal;

    /// <summary>Native <c>pktRecvUniqueTotal</c>: packets, lifetime.</summary>
    public long PacketsReceivedUniqueTotal => values.PacketsReceivedUniqueTotal;

    /// <summary>Native <c>byteSentUniqueTotal</c>: bytes, lifetime.</summary>
    public ulong BytesSentUniqueTotal => values.BytesSentUniqueTotal;

    /// <summary>Native <c>byteRecvUniqueTotal</c>: bytes, lifetime.</summary>
    public ulong BytesReceivedUniqueTotal => values.BytesReceivedUniqueTotal;

    /// <summary>Native <c>pktSentUnique</c>: packets, since reset.</summary>
    public long PacketsSentUnique => values.PacketsSentUnique;

    /// <summary>Native <c>pktRecvUnique</c>: packets, since reset.</summary>
    public long PacketsReceivedUnique => values.PacketsReceivedUnique;

    /// <summary>Native <c>byteSentUnique</c>: bytes, since reset.</summary>
    public ulong BytesSentUnique => values.BytesSentUnique;

    /// <summary>Native <c>byteRecvUnique</c>: bytes, since reset.</summary>
    public ulong BytesReceivedUnique => values.BytesReceivedUnique;
}
