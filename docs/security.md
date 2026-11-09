# Security model and assurance case

This page describes what you can and cannot expect from Srt.NET in terms of security, and argues why its security requirements are met. Report vulnerabilities as described in [SECURITY.md](https://github.com/notley-technologies/Srt.NET/blob/main/SECURITY.md).

## What Srt.NET provides

- **SRT encryption when you ask for it.** Setting `SrtOptions.Passphrase` (10 to 80 bytes) on both peers encrypts the payload with AES using libsrt's SRT encryption, backed by OpenSSL 3.5. Keys are derived from the passphrase with PBKDF2-HMAC-SHA1 (2048 iterations, 16-byte random salt) and wrapped with AES key wrap (RFC 3394). `SrtOptions.EncryptionKeySize` selects 128-, 192- or 256-bit keys; the default is 128 bits.
- **Memory safety at the native boundary.** Managed code passes libsrt only pinned buffers with checked lengths, reads native strings with an upper bound, and never exposes native pointers or handles in the public API.
- **No use of closed sockets.** Socket identifiers are wrapped in a `SafeHandle` and leased for every native call, so a disposed socket cannot reach a socket that libsrt later created with the same identifier.
- **Validated inputs.** Public arguments are checked against allowlists: enum values must be defined, timeouts and sizes must be in range, and option strings must be valid UTF-8 within libsrt's limits (for example, at most 512 bytes for a stream ID).
- **Write-only secrets.** The passphrase can be set but not read back, and Srt.NET never logs it.
- **Safe native loading.** `srt.dll` is never loaded from the current directory or `PATH` on Windows.
- **Bounded resource use.** The managed log queue holds at most 1024 records and drops the rest; send retries back off; the async engine uses one thread for all sockets.
- **A verifiable supply chain.** libsrt and OpenSSL are built from pinned, verified sources in CI; releases are built from signed tags, carry build provenance and SBOM attestations, and are published with short-lived trusted-publishing credentials. See [verifying releases](https://github.com/notley-technologies/Srt.NET/blob/main/SECURITY.md#verifying-releases).

## What Srt.NET does not provide

- **Encryption by default.** Connections are unencrypted unless the application sets a passphrase, because SRT encryption needs a secret shared by both peers.
- **Payload integrity.** In libsrt 1.5.7, SRT encryption uses AES-CTR, which keeps data confidential but does not detect modification: an attacker on the path can flip bits in encrypted packets without being detected. SRT's authenticated mode (AES-GCM) is a preview compile option in libsrt 1.5.7 and is not enabled in the bundled builds. Where tampering matters, add your own integrity check, for example an HMAC or digest over each message or file.
- **Peer authentication beyond the passphrase.** SRT has no certificates or user identities. Anyone who knows the passphrase can connect and decrypt; use a connection handler (`SetConnectionHandler`) to admit peers by address or stream ID.
- **Confidential handshake metadata.** The stream ID and other handshake fields are sent unencrypted.
- **Strong denial-of-service protection.** libsrt's handshake uses a cookie (computed with MD5) to make spoofed connection requests harder; it is not a cryptographic integrity mechanism. Rate-limit or firewall untrusted networks.
- **Delivery confirmation.** A completed send means libsrt queued the data. Process exit discards queued data while the peer still sees an orderly close; use an application acknowledgement when delivery matters.

## Security requirements

1. Managed code must never read or write native memory out of bounds, and must not let a closed or reused socket identifier be used.
2. Invalid arguments must be rejected before they reach libsrt.
3. Secrets passed to Srt.NET must not be readable or logged.
4. Exceptions must never unwind through native frames, and Srt.NET's own native callbacks must not block libsrt's threads. (The connection handler runs application code on libsrt's thread; its documentation says to keep it short.)
5. The native library must load only from expected locations.
6. Released packages must be built from reviewed source in CI, with verifiable provenance, and must contain only the declared native code.
7. Encryption must use libsrt's supported algorithms with keys of at least 128 bits, and the documentation must state its limits.

## Threat model and trust boundaries

| Boundary | Trusted side | Threats considered |
| --- | --- | --- |
| Network and libsrt | Neither: the network and remote peers are untrusted | Eavesdropping, packet injection and modification, spoofed handshakes, malformed packets, resource exhaustion. libsrt (C++) parses all network input. |
| libsrt and managed code | libsrt is trusted to honour its C API | Values libsrt returns (addresses, lengths, strings, statistics, callback arguments) are decoded defensively with fixed sizes and bounds. |
| Application and Srt.NET | The application is trusted; its arguments are not assumed valid | Invalid arguments, misuse of disposed objects, concurrent use. |
| Build, release and consumers | Reviewed source on `main` and CI | Compromised dependencies or build steps, tampered packages, publishing credentials. |

Code running in the same process as Srt.NET is trusted: it can read the application's memory, including the passphrase, by other means.

## Secure design

- **Economy of mechanism:** a thin wrapper over the C API; OpenSSL is built with `no-shared no-module no-dso no-engine no-autoload-config`, and the Linux libraries export only `srt_*`.
- **Fail-safe defaults:** libsrt's defaults are kept; native loading excludes the current directory and `PATH`; .NET Framework builds fail rather than run an unsupported architecture.
- **Complete mediation:** every native call goes through a socket lease; every public argument is validated.
- **Open design:** confidentiality depends only on the passphrase.
- **Least privilege:** CI tokens are read-only by default with per-job permissions; publishing uses OIDC trusted publishing.
- **Separation of privilege:** a release needs a signed tag that only maintainers can create and an approval in the protected `nuget` environment.
- **Least common mechanism:** no shared temporary files; process-wide libsrt state is reference-counted and released deterministically.
- **Psychological acceptability:** a .NET-idiomatic API (`using`, `async`/`await`, cancellation) and documentation that names unsafe patterns.

## Countered weaknesses

| Weakness | Countermeasure |
| --- | --- |
| Out-of-bounds read or write (CWE-125, CWE-787) | Spans and pinned buffers with checked lengths; fixed native structure sizes pinned by layout tests; bounded native string reads. |
| Use after free, handle reuse (CWE-416) | `SafeHandle` with a lease on every native call; disposal waits for in-progress calls. |
| Improper input validation (CWE-20) | Allowlist validation of enums, ranges and option values; strict UTF-8 encoding of option strings. |
| Uncontrolled search path (CWE-427) | `DefaultDllImportSearchPaths(AssemblyDirectory \| SafeDirectories)`. |
| Exposure of secrets (CWE-200, CWE-532) | Write-only passphrase; never logged. |
| Race conditions (CWE-362) | Queue state machine with a single owner per operation; adversarially reviewed and covered by engine tests. |
| Resource exhaustion (CWE-400) | Bounded log queue; retry back-off; one engine thread. |
| Download of code without integrity check (CWE-494) | OpenSSL verified by SHA-256 and release signature; libsrt pinned to a full commit; actions and images pinned by digest. |

CodeQL (security-extended queries), the .NET analyzers, FsCheck property tests over the native-boundary code, and the full test suite on all seven runtimes check these continuously.

## Security review

In November 2026 the maintainer reviewed the whole repository (code, interop against the libsrt 1.5.7 source, native build, packaging, CI and documentation) with AI-assisted, multi-reviewer analysis, and had every finding independently checked before fixing it. The reviews covered the security requirements and trust boundaries above. Their fixes include the native loading restriction, socket lifetime and engine race fixes, hardened native builds (stack protection, fortification, Control Flow Guard, CET, export control), release provenance, and this documentation.
