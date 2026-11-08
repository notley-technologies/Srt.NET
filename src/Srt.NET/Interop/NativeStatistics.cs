// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System.Runtime.InteropServices;

namespace Srt.Net.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeStatistics
{
    internal long TimestampMilliseconds;

    internal long PacketsSentTotal;

    internal long PacketsReceivedTotal;

    internal int PacketsSendLossTotal;

    internal int PacketsReceivedLossTotal;

    internal int PacketsRetransmittedTotal;

    internal int PacketsSentAckTotal;

    internal int PacketsReceivedAckTotal;

    internal int PacketsSentNakTotal;

    internal int PacketsReceivedNakTotal;

    internal long MicrosecondsSendDurationTotal;

    internal int PacketsSendDropTotal;

    internal int PacketsReceivedDropTotal;

    internal int PacketsReceivedUndecryptedTotal;

    internal ulong BytesSentTotal;

    internal ulong BytesReceivedTotal;

    internal ulong BytesReceivedLossTotal;

    internal ulong BytesRetransmittedTotal;

    internal ulong BytesSendDropTotal;

    internal ulong BytesReceivedDropTotal;

    internal ulong BytesReceivedUndecryptedTotal;

    internal long PacketsSent;

    internal long PacketsReceived;

    internal int PacketsSendLoss;

    internal int PacketsReceivedLoss;

    internal int PacketsRetransmitted;

    internal int PacketsReceivedRetransmitted;

    internal int PacketsSentAck;

    internal int PacketsReceivedAck;

    internal int PacketsSentNak;

    internal int PacketsReceivedNak;

    internal double SendRateMegabitsPerSecond;

    internal double ReceiveRateMegabitsPerSecond;

    internal long MicrosecondsSendDuration;

    internal int PacketsReorderDistance;

    internal double AverageLateArrivalMilliseconds;

    internal long PacketsReceivedBelated;

    internal int PacketsSendDrop;

    internal int PacketsReceivedDrop;

    internal int PacketsReceivedUndecrypted;

    internal ulong BytesSent;

    internal ulong BytesReceived;

    internal ulong BytesReceivedLoss;

    internal ulong BytesRetransmitted;

    internal ulong BytesSendDrop;

    internal ulong BytesReceivedDrop;

    internal ulong BytesReceivedUndecrypted;

    internal double SendPeriodMicroseconds;

    internal int PacketsFlowWindow;

    internal int PacketsCongestionWindow;

    internal int PacketsFlightSize;

    internal double RoundTripTimeMilliseconds;

    internal double BandwidthMegabitsPerSecond;

    internal int BytesAvailableSendBuffer;

    internal int BytesAvailableReceivedBuffer;

    internal double MaximumBandwidthMegabitsPerSecond;

    internal int MaximumSegmentSizeBytes;

    internal int PacketsSendBuffer;

    internal int BytesSendBuffer;

    internal int MillisecondsSendBuffer;

    internal int MillisecondsSendDeliveryDelay;

    internal int PacketsReceivedBuffer;

    internal int BytesReceivedBuffer;

    internal int MillisecondsReceivedBuffer;

    internal int MillisecondsReceivedDeliveryDelay;

    internal int PacketsSendFilterExtraTotal;

    internal int PacketsReceivedFilterExtraTotal;

    internal int PacketsReceivedFilterSupplyTotal;

    internal int PacketsReceivedFilterLossTotal;

    internal int PacketsSendFilterExtra;

    internal int PacketsReceivedFilterExtra;

    internal int PacketsReceivedFilterSupply;

    internal int PacketsReceivedFilterLoss;

    internal int PacketsReorderTolerance;

    internal long PacketsSentUniqueTotal;

    internal long PacketsReceivedUniqueTotal;

    internal ulong BytesSentUniqueTotal;

    internal ulong BytesReceivedUniqueTotal;

    internal long PacketsSentUnique;

    internal long PacketsReceivedUnique;

    internal ulong BytesSentUnique;

    internal ulong BytesReceivedUnique;
}
