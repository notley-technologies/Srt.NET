# Transport statistics

Call `socket.GetStatistics(clear: true, instantaneous: true)` to take a snapshot and start a new measurement interval. Choose one owner for interval resets when several parts of an application observe the same socket. With `clear: false`, interval counters continue accumulating until another caller clears them. Lifetime counters are unaffected by resets.

`instantaneous: true` requests current buffer occupancy; false uses the native averaged buffer measurements. Other gauges, such as round-trip time, retain their native semantics. Rate fields use megabits per second, while bandwidth socket options use bytes per second. Byte counters describe native transport accounting and are not an application payload acknowledgment.

All 82 fields in the bundled libsrt 1.5.7 statistics structure are exposed. The native identifiers below link each managed property to the [upstream metric definitions](https://github.com/Haivision/srt/blob/v1.5.7/docs/API/statistics.md). Values describe this socket's view of the connection, not a merged report from both peers. Loss, retransmission, drops, and filter recovery are distinct measurements; do not add them together as a single loss count.

The upstream documentation mentions `pktRcvRetransTotal`, but that field is absent from the pinned C structure. Srt.NET exposes the available interval measurement, `PacketsReceivedRetransmitted`, and does not invent a lifetime field.

`PacketsReorderDistance` is not an interval counter, although the upstream documentation calls it interval-based: clearing statistics does not reset it. It holds the largest out-of-order sequence distance the receiver has seen on the connection. When `SrtOptions.MaximumReorderTolerance` (`SRTO_LOSSMAXTTL`) is above zero, libsrt also lowers it by one each time it lowers the reorder tolerance, so it can fall below earlier peaks and below zero. With the default of zero it never decreases.

Statistics use a versioned native structure with no caller-supplied size. Srt.NET currently permits this call for the reviewed 1.5.0–1.5.7 ABI and rejects later versions until their layout is reviewed. See the [coverage audit](api-coverage.md#abi-and-validation).

| Managed property | Native field | Unit | Measurement |
| --- | --- | --- | --- |
| `TimestampMilliseconds` | `msTimeStamp` | milliseconds | lifetime |
| `PacketsSentTotal` | `pktSentTotal` | packets | lifetime |
| `PacketsReceivedTotal` | `pktRecvTotal` | packets | lifetime |
| `PacketsSendLossTotal` | `pktSndLossTotal` | packets | lifetime |
| `PacketsReceivedLossTotal` | `pktRcvLossTotal` | packets | lifetime |
| `PacketsRetransmittedTotal` | `pktRetransTotal` | packets | lifetime |
| `PacketsSentAckTotal` | `pktSentACKTotal` | packets | lifetime |
| `PacketsReceivedAckTotal` | `pktRecvACKTotal` | packets | lifetime |
| `PacketsSentNakTotal` | `pktSentNAKTotal` | packets | lifetime |
| `PacketsReceivedNakTotal` | `pktRecvNAKTotal` | packets | lifetime |
| `MicrosecondsSendDurationTotal` | `usSndDurationTotal` | microseconds | lifetime |
| `PacketsSendDropTotal` | `pktSndDropTotal` | packets | lifetime |
| `PacketsReceivedDropTotal` | `pktRcvDropTotal` | packets | lifetime |
| `PacketsReceivedUndecryptedTotal` | `pktRcvUndecryptTotal` | packets | lifetime |
| `BytesSentTotal` | `byteSentTotal` | bytes | lifetime |
| `BytesReceivedTotal` | `byteRecvTotal` | bytes | lifetime |
| `BytesReceivedLossTotal` | `byteRcvLossTotal` | bytes | lifetime |
| `BytesRetransmittedTotal` | `byteRetransTotal` | bytes | lifetime |
| `BytesSendDropTotal` | `byteSndDropTotal` | bytes | lifetime |
| `BytesReceivedDropTotal` | `byteRcvDropTotal` | bytes | lifetime |
| `BytesReceivedUndecryptedTotal` | `byteRcvUndecryptTotal` | bytes | lifetime |
| `PacketsSent` | `pktSent` | packets | since reset |
| `PacketsReceived` | `pktRecv` | packets | since reset |
| `PacketsSendLoss` | `pktSndLoss` | packets | since reset |
| `PacketsReceivedLoss` | `pktRcvLoss` | packets | since reset |
| `PacketsRetransmitted` | `pktRetrans` | packets | since reset |
| `PacketsReceivedRetransmitted` | `pktRcvRetrans` | packets | since reset |
| `PacketsSentAck` | `pktSentACK` | packets | since reset |
| `PacketsReceivedAck` | `pktRecvACK` | packets | since reset |
| `PacketsSentNak` | `pktSentNAK` | packets | since reset |
| `PacketsReceivedNak` | `pktRecvNAK` | packets | since reset |
| `SendRateMegabitsPerSecond` | `mbpsSendRate` | megabits/second | since reset |
| `ReceiveRateMegabitsPerSecond` | `mbpsRecvRate` | megabits/second | since reset |
| `MicrosecondsSendDuration` | `usSndDuration` | microseconds | since reset |
| `PacketsReorderDistance` | `pktReorderDistance` | packets | gauge, not reset by clear (see above) |
| `AverageLateArrivalMilliseconds` | `pktRcvAvgBelatedTime` | milliseconds | gauge |
| `PacketsReceivedBelated` | `pktRcvBelated` | packets | since reset |
| `PacketsSendDrop` | `pktSndDrop` | packets | since reset |
| `PacketsReceivedDrop` | `pktRcvDrop` | packets | since reset |
| `PacketsReceivedUndecrypted` | `pktRcvUndecrypt` | packets | since reset |
| `BytesSent` | `byteSent` | bytes | since reset |
| `BytesReceived` | `byteRecv` | bytes | since reset |
| `BytesReceivedLoss` | `byteRcvLoss` | bytes | since reset |
| `BytesRetransmitted` | `byteRetrans` | bytes | since reset |
| `BytesSendDrop` | `byteSndDrop` | bytes | since reset |
| `BytesReceivedDrop` | `byteRcvDrop` | bytes | since reset |
| `BytesReceivedUndecrypted` | `byteRcvUndecrypt` | bytes | since reset |
| `SendPeriodMicroseconds` | `usPktSndPeriod` | microseconds | gauge |
| `PacketsFlowWindow` | `pktFlowWindow` | packets | gauge |
| `PacketsCongestionWindow` | `pktCongestionWindow` | packets | gauge |
| `PacketsFlightSize` | `pktFlightSize` | packets | gauge |
| `RoundTripTimeMilliseconds` | `msRTT` | milliseconds | gauge |
| `BandwidthMegabitsPerSecond` | `mbpsBandwidth` | megabits/second | gauge |
| `BytesAvailableSendBuffer` | `byteAvailSndBuf` | bytes | gauge |
| `BytesAvailableReceivedBuffer` | `byteAvailRcvBuf` | bytes | gauge |
| `MaximumBandwidthMegabitsPerSecond` | `mbpsMaxBW` | megabits/second | gauge |
| `MaximumSegmentSizeBytes` | `byteMSS` | bytes | gauge |
| `PacketsSendBuffer` | `pktSndBuf` | packets | gauge |
| `BytesSendBuffer` | `byteSndBuf` | bytes | gauge |
| `MillisecondsSendBuffer` | `msSndBuf` | milliseconds | gauge |
| `MillisecondsSendDeliveryDelay` | `msSndTsbPdDelay` | milliseconds | gauge |
| `PacketsReceivedBuffer` | `pktRcvBuf` | packets | gauge |
| `BytesReceivedBuffer` | `byteRcvBuf` | bytes | gauge |
| `MillisecondsReceivedBuffer` | `msRcvBuf` | milliseconds | gauge |
| `MillisecondsReceivedDeliveryDelay` | `msRcvTsbPdDelay` | milliseconds | gauge |
| `PacketsSendFilterExtraTotal` | `pktSndFilterExtraTotal` | packets | lifetime |
| `PacketsReceivedFilterExtraTotal` | `pktRcvFilterExtraTotal` | packets | lifetime |
| `PacketsReceivedFilterSupplyTotal` | `pktRcvFilterSupplyTotal` | packets | lifetime |
| `PacketsReceivedFilterLossTotal` | `pktRcvFilterLossTotal` | packets | lifetime |
| `PacketsSendFilterExtra` | `pktSndFilterExtra` | packets | since reset |
| `PacketsReceivedFilterExtra` | `pktRcvFilterExtra` | packets | since reset |
| `PacketsReceivedFilterSupply` | `pktRcvFilterSupply` | packets | since reset |
| `PacketsReceivedFilterLoss` | `pktRcvFilterLoss` | packets | since reset |
| `PacketsReorderTolerance` | `pktReorderTolerance` | packets | gauge |
| `PacketsSentUniqueTotal` | `pktSentUniqueTotal` | packets | lifetime |
| `PacketsReceivedUniqueTotal` | `pktRecvUniqueTotal` | packets | lifetime |
| `BytesSentUniqueTotal` | `byteSentUniqueTotal` | bytes | lifetime |
| `BytesReceivedUniqueTotal` | `byteRecvUniqueTotal` | bytes | lifetime |
| `PacketsSentUnique` | `pktSentUnique` | packets | since reset |
| `PacketsReceivedUnique` | `pktRecvUnique` | packets | since reset |
| `BytesSentUnique` | `byteSentUnique` | bytes | since reset |
| `BytesReceivedUnique` | `byteRecvUnique` | bytes | since reset |
