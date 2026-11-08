# Native API coverage audit

This audit inventories all 72 exported C functions and all 61 socket-option identifiers in libsrt 1.5.7, pinned to `899348d8318eb9a3c5a5b6ec43c4a1114288773a`. Conditional APIs are included even when the native build disables them. The statistics inventory covers all 82 fields in that release. This is capability coverage, not a promise of identical method names or unrestricted native access.

Sources: [pinned C header](https://github.com/Haivision/srt/blob/899348d8318eb9a3c5a5b6ec43c4a1114288773a/srtcore/srt.h), [function contracts](https://github.com/Haivision/srt/blob/v1.5.7/docs/API/API-functions.md), [socket options](https://github.com/Haivision/srt/blob/v1.5.7/docs/API/API-socket-options.md).

## Scope and decisions

Implemented priorities are application-owned readiness polling, explicit nonblocking operations and connection completion, complete transport statistics, message metadata, connection admission, native logging, and the stable socket controls needed for tuning and diagnosis. The [server example](../examples/Srt.NET.Server/Program.cs) uses an AcceptAsync loop with one task per client and no log suppression; the [advanced usage guide](advanced-usage.md#wait-before-accepting) shows a poller that watches a listener together with other sockets.

The application controls which sockets to monitor, when to wait, its deadline/cancellation policy, and when to accept or transfer data. Register listeners before Listen and callers before BeginConnect. Do not use AvailableEvents as a listener-pending check.

New functionality beyond a raw function binding includes resource-safe polling registrations, managed cancellation, immutable snapshots, platform-correct linger marshalling, a Boolean admission decision, exception containment, and bounded log dispatch outside native logging locks. Admission callbacks remain synchronous because native handshake decisions require a return value.

Asynchronous .NET I/O is provided by a readiness engine: one thread waits on an edge-triggered libsrt epoll for every socket, and completions run on the thread pool, so it scales without a thread per operation. Deliberately deferred: bonding and socket groups, adopting existing UDP sockets, mixing OS descriptors into polling, native path-based file transfer, and experimental encryption/retransmission switches. Existing .NET file APIs can feed the byte-stream methods, directly or through SrtStream. These exclusions keep the first API focused on ordinary clients, servers, and media relays.

Every bundled runtime is built with libsrt's default feature switches, so ENABLE_BONDING, ENABLE_AEAD_API_PREVIEW, and ENABLE_MAXREXMITBW are disabled. Feature-dependent options still delegate platform validation to libsrt.

## Coverage terms

- **Covered:** directly exposed through the managed API, possibly with several native functions represented by one method.
- **Managed:** ownership, error state, or blocking behavior is handled by the wrapper.
- **Equivalent:** callers can express the capability using other managed methods; any limits are stated.
- **Deferred:** useful in a narrower workload, but intentionally outside this pass.
- **Excluded:** obsolete, internal, or unsafe to expose as an unrestricted native control.

## Function inventory

| Native function | Status | Managed surface | Decision / limit |
| --- | --- | --- | --- |
| `srt_accept` | Covered | Accept; AcceptAsync; TryAccept | Called only when the listener reports a queued connection, so waiting for clients does not call it. libsrt leaves that readiness set when it discards a queued connection that broke before it was accepted; the next attempt then logs one Error record ("no pending connection available at the moment") and keeps waiting, or TryAccept returns false. A queued connection that libsrt has already closed is skipped; in that rare race libsrt logs one Error record ("pending connection has unexpectedly closed"). |
| `srt_accept_bond` | Equivalent | SrtPoller + Accept | A poller can monitor multiple listeners; no separate accept-list wrapper is needed. |
| `srt_addlogfa` | Covered | SrtLogging | Application-selected process-wide logging, functional areas, and bounded managed callback dispatch. No implicit filtering. |
| `srt_bind` | Covered | Bind | IPv4 and IPv6 endpoints. |
| `srt_bind_acquire` | Deferred | — | Adopting OS UDP sockets requires a separate ownership contract across Windows and Unix. |
| `srt_bistats` | Covered | GetStatistics | All 82 fields; optional interval reset and instantaneous gauges. |
| `srt_bstats` | Covered | GetStatistics | All 82 fields; optional interval reset and instantaneous gauges. |
| `srt_cleanup` | Managed | Automatic runtime leases | Sockets, pollers, and the readiness engine hold the runtime until released. At process exit Srt.NET cleans up libsrt itself, because libsrt's static destructor crashes once the OS has stopped its threads. |
| `srt_clearlasterror` | Excluded | — | The managed API consumes errors as exceptions; callers do not manage native thread-local error state. |
| `srt_clock_type` | Excluded | — | Internal clock implementation selection is not needed to use the native timestamp clock. |
| `srt_close` | Managed | Dispose | Lifetime ownership and interruption of outstanding operations. |
| `srt_config_add` | Deferred | — | Bonding/group lifecycle and recovery require their own design and native feature validation; the bundled builds have bonding disabled. |
| `srt_connect` | Covered | Connect; ConnectAsync; BeginConnect; CompleteConnect | Blocking or asynchronous managed calls, or application-driven connection readiness. |
| `srt_connect_bind` | Equivalent | Bind + Connect; Rendezvous option | The same socket setup can be expressed without duplicate convenience methods. |
| `srt_connect_callback` | Equivalent | SrtPoller Connect/Error + CompleteConnect | Completion is observed by the application; a native-thread callback is not exposed. Native callback error/token data is not reproduced. |
| `srt_connect_debug` | Excluded | — | Forced initial sequence numbers are an upstream developer/debugging facility. |
| `srt_connect_group` | Deferred | — | Bonding/group lifecycle and recovery require their own design and native feature validation; the bundled builds have bonding disabled. |
| `srt_connection_time` | Covered | SrtClock.NowMicroseconds; ConnectionTimeMicroseconds | Native clock values suitable for source timestamp forwarding and diagnostics. |
| `srt_create_config` | Deferred | — | Bonding/group lifecycle and recovery require their own design and native feature validation; the bundled builds have bonding disabled. |
| `srt_create_group` | Deferred | — | Bonding/group lifecycle and recovery require their own design and native feature validation; the bundled builds have bonding disabled. |
| `srt_create_socket` | Covered | SrtSocket constructor | Modern socket creation replaces the deprecated constructor. |
| `srt_delete_config` | Deferred | — | Bonding/group lifecycle and recovery require their own design and native feature validation; the bundled builds have bonding disabled. |
| `srt_dellogfa` | Covered | SrtLogging | Application-selected process-wide logging, functional areas, and bounded managed callback dispatch. No implicit filtering. |
| `srt_epoll_add_ssock` | Deferred | — | Mixed OS/SRT socket polling needs a cross-platform descriptor and ownership design. |
| `srt_epoll_add_usock` | Covered | SrtPoller.Add; Update; Remove | Read/accept, write/connect, and error interests. |
| `srt_epoll_clear_usocks` | Equivalent | Remove registrations or dispose the poller | No separate bulk-clear method. |
| `srt_epoll_create` | Covered | SrtPoller lifetime; readiness engine | Opt-in poller with native resource ownership. The engine owns one edge-triggered poll for all sockets; each listener also keeps a level-triggered poll to test for queued connections. |
| `srt_epoll_release` | Covered | SrtPoller lifetime | Opt-in poller with native resource ownership. |
| `srt_epoll_remove_ssock` | Deferred | — | Mixed OS/SRT socket polling needs a cross-platform descriptor and ownership design. |
| `srt_epoll_remove_usock` | Covered | SrtPoller.Add; Update; Remove | Read/accept, write/connect, and error interests. |
| `srt_epoll_set` | Excluded | — | Native poller control flags stay internal. Empty registrations return immediately; callers can supply result storage. Concurrent removal of the last registration is supported. Edge-triggered mode is used internally by the readiness engine but not exposed. |
| `srt_epoll_update_ssock` | Deferred | — | Mixed OS/SRT socket polling needs a cross-platform descriptor and ownership design. |
| `srt_epoll_update_usock` | Covered | SrtPoller.Add; Update; Remove | Read/accept, write/connect, and error interests. |
| `srt_epoll_uwait` | Covered | SrtPoller.Wait | Multiple SRT sockets; array or Span results, cancellation, deadlines, and closure events. |
| `srt_epoll_wait` | Covered | SrtPoller.Wait | Multiple SRT sockets; array or Span results, cancellation, deadlines, and closure events. |
| `srt_getlasterror` | Managed | SrtException | Capture native thread-local errors immediately, including system error and rejection reason where available. |
| `srt_getlasterror_str` | Managed | SrtException | Capture native thread-local errors immediately, including system error and rejection reason where available. |
| `srt_getpeername` | Covered | RemoteEndPoint; LocalEndPoint | Managed endpoint values. |
| `srt_getrejectreason` | Covered | SrtException.RejectionReason | Connection failures expose rejection details. |
| `srt_getsndbuffer` | Covered | GetSendBuffer | Queued blocks and bytes, without treating queue drain as an application acknowledgment. |
| `srt_getsockflag` | Covered | GetOption; SetOption | Typed options replace raw pointers and obsolete level arguments. See the option inventory. |
| `srt_getsockname` | Covered | RemoteEndPoint; LocalEndPoint | Managed endpoint values. |
| `srt_getsockopt` | Covered | GetOption; SetOption | Typed options replace raw pointers and obsolete level arguments. See the option inventory. |
| `srt_getsockstate` | Covered | State | Socket state is separate from readiness. |
| `srt_getversion` | Covered | SrtSocket.NativeVersion | Loaded native version. |
| `srt_group_data` | Deferred | — | Bonding/group lifecycle and recovery require their own design and native feature validation; the bundled builds have bonding disabled. |
| `srt_groupof` | Deferred | — | Bonding/group lifecycle and recovery require their own design and native feature validation; the bundled builds have bonding disabled. |
| `srt_listen` | Covered | Listen | Subscribes the listener's accept readiness before listening, so connections that arrive early are not missed. |
| `srt_listen_callback` | Covered | SetConnectionHandler; SrtConnectionRequest | Boolean admission decision, peer address, UTF-8 stream ID, per-peer options, custom rejection reasons. |
| `srt_msgctrl_init` | Covered | SendMessage; ReceiveMessage; TrySendMessage; TryReceiveMessage; SrtMessageOptions | TTL, ordering, source time, message number, packet sequence; boundary and flags are reserved by libsrt and not exposed; group metadata is deferred. |
| `srt_prepare_endpoint` | Deferred | — | Bonding/group lifecycle and recovery require their own design and native feature validation; the bundled builds have bonding disabled. |
| `srt_recv` | Covered | Send; Receive; SendAsync; ReceiveAsync; TrySend; TryReceive; SrtStream | Segments, partial stream transfers, cancellation, and explicit nonblocking attempts. |
| `srt_recvfile` | Deferred | Compose .NET FileStream with Send/Receive | Avoid native path encoding, file ownership, and uncancellable file operations. No native file-transfer convenience wrapper. |
| `srt_recvmsg` | Covered | Send; Receive; SendAsync; ReceiveAsync; TrySend; TryReceive; SrtStream | Segments, partial stream transfers, cancellation, and explicit nonblocking attempts. |
| `srt_recvmsg2` | Covered | SendMessage; ReceiveMessage; SendMessageAsync; ReceiveMessageAsync; TrySendMessage; TryReceiveMessage; SrtMessageOptions | TTL, ordering, source time, message number, packet sequence; boundary and flags are reserved by libsrt and not exposed; group metadata is deferred. |
| `srt_rejectreason_str` | Covered | SrtErrors | Descriptions for stored native error and rejection codes. |
| `srt_rendezvous` | Equivalent | Bind + Connect; Rendezvous option | The same socket setup can be expressed without duplicate convenience methods. |
| `srt_resetlogfa` | Covered | SrtLogging | Application-selected process-wide logging, functional areas, and bounded managed callback dispatch. No implicit filtering. |
| `srt_send` | Covered | Send; Receive; SendAsync; ReceiveAsync; TrySend; TryReceive; SrtStream | Segments, partial stream transfers, cancellation, and explicit nonblocking attempts. |
| `srt_sendfile` | Deferred | Compose .NET FileStream with Send/Receive | Avoid native path encoding, file ownership, and uncancellable file operations. No native file-transfer convenience wrapper. |
| `srt_sendmsg` | Covered | SendMessage; ReceiveMessage; SendMessageAsync; ReceiveMessageAsync; TrySendMessage; TryReceiveMessage; SrtMessageOptions | TTL, ordering, source time, message number, packet sequence; boundary and flags are reserved by libsrt and not exposed; group metadata is deferred. |
| `srt_sendmsg2` | Covered | SendMessage; ReceiveMessage; SendMessageAsync; ReceiveMessageAsync; TrySendMessage; TryReceiveMessage; SrtMessageOptions | TTL, ordering, source time, message number, packet sequence; boundary and flags are reserved by libsrt and not exposed; group metadata is deferred. |
| `srt_setlogflags` | Covered | SrtLogging | Application-selected process-wide logging, functional areas, and bounded managed callback dispatch. No implicit filtering. |
| `srt_setloghandler` | Covered | SrtLogging | Application-selected process-wide logging, functional areas, and bounded managed callback dispatch. No implicit filtering. |
| `srt_setloglevel` | Covered | SrtLogging | Application-selected process-wide logging, functional areas, and bounded managed callback dispatch. No implicit filtering. |
| `srt_setrejectreason` | Covered | SetConnectionHandler; SrtConnectionRequest | Boolean admission decision, peer address, UTF-8 stream ID, per-peer options, custom rejection reasons. |
| `srt_setsockflag` | Covered | GetOption; SetOption | Typed options replace raw pointers and obsolete level arguments. See the option inventory. |
| `srt_setsockopt` | Covered | GetOption; SetOption | Typed options replace raw pointers and obsolete level arguments. See the option inventory. |
| `srt_socket` | Covered | SrtSocket constructor | Modern socket creation replaces the deprecated constructor. |
| `srt_startup` | Managed | Automatic runtime leases | Sockets and pollers hold the runtime until native resources are released. |
| `srt_strerror` | Covered | SrtErrors | Descriptions for stored native error and rejection codes. |
| `srt_time_now` | Covered | SrtClock.NowMicroseconds; ConnectionTimeMicroseconds | Native clock values suitable for source timestamp forwarding and diagnostics. |

## Socket option inventory

Access is listed for each typed option. Readable native controls remain readable through GetOption, including timestamp delivery, congestion selection, message mode, encryption enforcement, payload size, and connection/drop timeouts. TransmissionType and Passphrase remain write-only because libsrt has no getter for them.

The enum sentinel SRTO_E_SIZE is not an option. Reserved numeric gaps are not options.

| Native option | Status | Managed surface | Decision / limit |
| --- | --- | --- | --- |
| `SRTO_MSS` | Covered | SrtOptions.MaximumSegmentSize | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_SNDSYN` | Managed | Blocking and asynchronous operations; Try operations; SrtPoller | Native sockets remain nonblocking; blocking calls wait through the readiness engine so cancellation and disposal interrupt them immediately. Arbitrary native blocking mode is not exposed. |
| `SRTO_RCVSYN` | Managed | Blocking and asynchronous operations; Try operations; SrtPoller | Native sockets remain nonblocking; blocking calls wait through the readiness engine so cancellation and disposal interrupt them immediately. Arbitrary native blocking mode is not exposed. |
| `SRTO_ISN` | Covered | SrtOptions.InitialSequenceNumber | read; native code validates platform support, range, and configuration timing. |
| `SRTO_FC` | Covered | SrtOptions.FlowControlWindow | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_SNDBUF` | Covered | SrtOptions.SendBufferSize | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_RCVBUF` | Covered | SrtOptions.ReceiveBufferSize | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_LINGER` | Covered | SrtOptions.Linger | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_UDP_SNDBUF` | Covered | SrtOptions.UdpSendBufferSize | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_UDP_RCVBUF` | Covered | SrtOptions.UdpReceiveBufferSize | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_RENDEZVOUS` | Covered | SrtOptions.Rendezvous | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_SNDTIMEO` | Managed | Per-operation timeout | Managed deadlines replace native blocking-operation timeouts. |
| `SRTO_RCVTIMEO` | Managed | Per-operation timeout | Managed deadlines replace native blocking-operation timeouts. |
| `SRTO_REUSEADDR` | Covered | SrtOptions.ReuseAddress | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_MAXBW` | Covered | SrtOptions.MaximumBandwidth | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_STATE` | Covered | SrtSocket.State | A state value does not indicate an accept queue is ready. |
| `SRTO_EVENT` | Covered | SrtOptions.AvailableEvents | read; native code validates platform support, range, and configuration timing. |
| `SRTO_SNDDATA` | Covered | SrtOptions.PendingSendPackets | read; native code validates platform support, range, and configuration timing. |
| `SRTO_RCVDATA` | Covered | SrtOptions.PendingReceivePackets | read; native code validates platform support, range, and configuration timing. |
| `SRTO_SENDER` | Excluded | — | Legacy handshake-v4 sender selection; current handshakes negotiate bidirectional operation. |
| `SRTO_TSBPDMODE` | Covered | SrtOptions.TimestampBasedDelivery | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_LATENCY` | Equivalent | ReceiveLatency + PeerLatency | Prefer the two directional options over the older combined alias. |
| `SRTO_INPUTBW` | Covered | SrtOptions.InputBandwidth | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_OHEADBW` | Covered | SrtOptions.OverheadBandwidth | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_PASSPHRASE` | Covered | SrtOptions.Passphrase | write; native code validates platform support, range, and configuration timing. |
| `SRTO_PBKEYLEN` | Covered | SrtOptions.EncryptionKeySize | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_KMSTATE` | Equivalent | SendEncryptionState + ReceiveEncryptionState | Directional encryption state is clearer than the legacy combined option. |
| `SRTO_IPTTL` | Covered | SrtOptions.IpTimeToLive | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_IPTOS` | Covered | SrtOptions.IpTypeOfService | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_TLPKTDROP` | Covered | SrtOptions.TooLatePacketDrop | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_SNDDROPDELAY` | Covered | SrtOptions.SendDropDelay | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_NAKREPORT` | Covered | SrtOptions.NegativeAcknowledgmentReports | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_VERSION` | Covered | SrtSocket.NativeVersion | Managed Version value. |
| `SRTO_PEERVERSION` | Covered | SrtOptions.PeerVersion | read; native code validates platform support, range, and configuration timing. |
| `SRTO_CONNTIMEO` | Covered | SrtOptions.ConnectTimeout | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_DRIFTTRACER` | Covered | SrtOptions.DriftTracer | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_MININPUTBW` | Covered | SrtOptions.MinimumInputBandwidth | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_SNDKMSTATE` | Covered | SrtOptions.SendEncryptionState | read; native code validates platform support, range, and configuration timing. |
| `SRTO_RCVKMSTATE` | Covered | SrtOptions.ReceiveEncryptionState | read; native code validates platform support, range, and configuration timing. |
| `SRTO_LOSSMAXTTL` | Covered | SrtOptions.MaximumReorderTolerance | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_RCVLATENCY` | Covered | SrtOptions.ReceiveLatency | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_PEERLATENCY` | Covered | SrtOptions.PeerLatency | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_MINVERSION` | Covered | SrtOptions.MinimumPeerVersion | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_STREAMID` | Covered | SrtOptions.StreamId | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_CONGESTION` | Covered | SrtOptions.CongestionController | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_MESSAGEAPI` | Covered | SrtOptions.MessageApi | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_PAYLOADSIZE` | Covered | SrtOptions.PayloadSize | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_TRANSTYPE` | Covered | SrtOptions.TransmissionType | write; native code validates platform support, range, and configuration timing. |
| `SRTO_KMREFRESHRATE` | Covered | SrtOptions.KeyRefreshRate | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_KMPREANNOUNCE` | Covered | SrtOptions.KeyPreannounce | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_ENFORCEDENCRYPTION` | Covered | SrtOptions.EnforcedEncryption | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_IPV6ONLY` | Covered | SrtOptions.IPv6Only | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_PEERIDLETIMEO` | Covered | SrtOptions.PeerIdleTimeout | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_BINDTODEVICE` | Covered | SrtOptions.BindToDevice | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_GROUPCONNECT` | Deferred | — | Bonding is not enabled in the bundled native builds and needs a dedicated group API. |
| `SRTO_GROUPMINSTABLETIMEO` | Deferred | — | Bonding is not enabled in the bundled native builds and needs a dedicated group API. |
| `SRTO_GROUPTYPE` | Deferred | — | Bonding is not enabled in the bundled native builds and needs a dedicated group API. |
| `SRTO_PACKETFILTER` | Covered | SrtOptions.PacketFilter | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_RETRANSMITALGO` | Covered | SrtOptions.RetransmissionAlgorithm | read/write; native code validates platform support, range, and configuration timing. |
| `SRTO_CRYPTOMODE` | Deferred | — | Compile-time optional features are disabled in the bundled native builds; do not advertise unsupported knobs. |
| `SRTO_MAXREXMITBW` | Deferred | — | Compile-time optional features are disabled in the bundled native builds; do not advertise unsupported knobs. |

## ABI and validation

SrtStatistics exposes all 82 fields; see [statistics](statistics.md). The supported 64-bit layout is 496 bytes for statistics, 48 for message control, and 8 for a poll event. Native linger is 4 bytes on Windows and 8 on Unix. A native sizeof/offsetof probe and managed layout tests cover these contracts. Statistics are guarded to the reviewed 1.5.0–1.5.7 layout; review the ABI before enabling a later version. Message group metadata remains internal and zero-initialized.

Integration tests cover queued and delayed listener readiness, multiple sockets, interest updates, concurrent registration, cancellation/disposal, connection rejection, UTF-8 admission and per-peer encryption, metadata, statistics/reset behavior, log callback reentrancy, and native option types. Tests run on .NET 8 and .NET 10 on every platform, and on .NET Framework 4.8 on Windows. macOS still requires its CI runners.

This audit does not certify every network impairment scenario, FEC recovery behavior, every socket option combination, or optional native features. Error/rejection codes remain integers so application-defined and future native values can be preserved. Public readiness and logging flags have managed enums; native pointers and mutable ABI structs are kept private.
