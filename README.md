# Srt.NET

[![CI](https://github.com/notley-technologies/Srt.NET/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/notley-technologies/Srt.NET/actions/workflows/build.yml) [![CodeQL](https://github.com/notley-technologies/Srt.NET/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/notley-technologies/Srt.NET/actions/workflows/codeql.yml) [![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/notley-technologies/Srt.NET/badge)](https://scorecard.dev/viewer/?uri=github.com/notley-technologies/Srt.NET) [![OpenSSF Best Practices](https://www.bestpractices.dev/projects/16461/badge)](https://www.bestpractices.dev/projects/16461) [![License: MPL 2.0](https://img.shields.io/badge/License-MPL_2.0-brightgreen.svg)](https://opensource.org/licenses/MPL-2.0)

Srt.NET is a .NET socket API for [Secure Reliable Transport](https://github.com/Haivision/srt) (SRT). It calls Haivision's libsrt C API directly through P/Invoke, with synchronous and `ValueTask`-based asynchronous operations, and ships libsrt with statically linked OpenSSL for seven runtimes. It targets .NET Standard 2.0 (including .NET Framework 4.8) and .NET 8 or later, where it is trimming- and Native AOT-compatible.

It supports callers, listeners, and rendezvous connections; IPv4 and IPv6; encrypted live messages and file-mode byte streams; typed options; timeouts and cancellation; readiness polling; connection admission; message metadata; transport statistics; native logging; and connection metrics. See the [advanced usage guide](docs/advanced-usage.md) and the [native API coverage audit](docs/api-coverage.md), which also lists deliberately deferred features such as connection bonding.

## Send and receive

For runnable applications, start with the [client and server examples](examples/README.md), written with both the asynchronous and the synchronous API. They build against the source project and a locally built native library, as their README explains. Applications reference the `Srt.NET` package (`dotnet add package Srt.NET`), which supplies the managed assembly and the native library for each supported runtime.

Listener:

```csharp
using System;
using System.Net;
using System.Text;
using Srt.Net;

using SrtSocket listener = new();
listener.SetOption(SrtOptions.ReceiveLatency, 120);
listener.Bind(new(IPAddress.Any, 9000));
listener.Listen();

using SrtSocket connection = await listener.AcceptAsync();
byte[] buffer = new byte[1500];
int count = await connection.ReceiveAsync(buffer);
Console.WriteLine(Encoding.UTF8.GetString(buffer, 0, count));
await connection.SendAsync(Encoding.UTF8.GetBytes("received"));

// Live mode discards queued data on close, so stay connected until the caller has read the reply and closed.
try
{
    await connection.ReceiveAsync(buffer);
}
catch (SrtException error) when (error.SrtErrorCode is SrtErrorCode.ConnectionLost or SrtErrorCode.InvalidSocket)
{
}
```

Caller:

```csharp
using System;
using System.Net;
using System.Text;
using System.Threading;
using Srt.Net;

using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
using SrtSocket connection = new();
connection.SetOption(SrtOptions.StreamId, "camera-1");
await connection.ConnectAsync(new(IPAddress.Loopback, 9000), timeout.Token);
await connection.SendAsync(Encoding.UTF8.GetBytes("Hello over SRT"), timeout.Token);
byte[] reply = new byte[1500];
int count = await connection.ReceiveAsync(reply, timeout.Token);
Console.WriteLine(Encoding.UTF8.GetString(reply, 0, count));
```

Every operation also has a synchronous form (`Connect`, `Accept`, `Send`, `Receive`, and so on) that takes a timeout and a `CancellationToken`. For encryption, set `SrtOptions.Passphrase` to the same secret on the caller and listener before connecting or listening, and supply it from your application's configuration. No separate cryptographic library is needed.

## Behavior

- **Lifetime.** `using` owns socket lifetime. libsrt starts and stops automatically and stays started while any socket or poller is alive. Accepted sockets can outlive their listener.
- **Waiting.** Operations that cannot complete immediately wait on one shared readiness engine: a single background thread waits for every socket, so idle connections cost no threads. Synchronous and asynchronous operations share one queue per direction and complete in the order they started, so they can be mixed on one socket.
- **Timeouts and cancellation.** A null timeout waits indefinitely, and zero makes one attempt without waiting; `Connect` is also bounded by libsrt's handshake timeout. Use a timed `CancellationTokenSource` with the asynchronous methods. Cancellation leaves the socket usable, except that a failed or canceled connection attempt disposes the socket. Disposing a socket immediately fails its pending operations with `ObjectDisposedException`. With linger enabled (the file-profile default), queued data keeps draining in the background for up to the linger time; the live profile discards it. Unlike a TCP close, process exit discards data that is still queued while the peer still sees an orderly close. Delivery is never confirmed, so use an application acknowledgment when it matters.
- **Streams.** `SrtStream` adapts a connected file-mode socket to `System.IO.Stream`, like `NetworkStream`. Reads return 0 after an orderly close and throw when the connection breaks.
- **Polling.** `SrtPoller` is optional and application-owned. `TryAccept`, `TrySend`, and `TryReceive` make one attempt; `BeginConnect` and `CompleteConnect` complete connections through a poller.
- **Modes.** Live mode sends one message per call, 1316 bytes by default; receive into at least 1456 bytes with the default MTU. File mode is a byte stream, so handle partial send and receive results. Set `TransmissionType` before other options because it resets profile defaults.
- **Errors.** The wrapper checks option types and the UTF-8 byte limits of values it sets; libsrt validates ranges and timing. Stream IDs are peer-supplied, so invalid UTF-8 in them reads as U+FFFD. `SrtException` exposes the typed `SrtErrorCode` and the system error number. Failed connection attempts report `ConnectionRejected` or `NoServer` with a typed `RejectionReason`.
- **Diagnostics.** `GetStatistics` exposes all 82 native statistics fields ([units and reset behavior](docs/statistics.md)). `SendMessage` and `ReceiveMessage` expose source timestamps and message metadata. `SrtLogging` routes native logs, and on .NET 8 and later the `Srt.Net` meter publishes [connection metrics](docs/advanced-usage.md#collect-metrics).
- **Handles.** SRT sockets are 32-bit identifiers, not operating-system sockets. Native handles and mutable native structures are kept out of the public API.

## Platforms

Every release package contains encryption-enabled libsrt for all of these runtimes:

| Runtime identifier | Runs on |
| --- | --- |
| `win-x64` | 64-bit Windows 10 and later; no separate cryptography or MSVC runtime installation |
| `linux-x64`, `linux-arm64` | glibc 2.28 and later (for example RHEL 8, Debian 10, Ubuntu 20.04, and later); no libstdc++ dependency |
| `linux-musl-x64`, `linux-musl-arm64` | musl 1.2.5 and later (Alpine 3.22 and later) |
| `osx-x64` | Intel macOS 13 and later |
| `osx-arm64` | Apple Silicon macOS 13 and later |

CI builds each runtime on its own architecture, tests it on .NET 8 and .NET 10, runs the Linux tests again on RHEL 8 (the glibc 2.28 baseline), and installs the package into fresh applications, including Native AOT. Windows x86 and Windows Arm64 are not included; Windows Arm64 can run the `win-x64` build in x64 processes under emulation.

Modern .NET resolves the native library through NuGet's runtime assets. .NET Framework projects must use `PackageReference` and run as x64 (including ASP.NET on IIS): set `PlatformTarget` to `x64`, or use AnyCPU without `Prefer32Bit` or `PreferNativeArm64`, which is what a default SDK-style executable does. The package's MSBuild target copies `srt.dll` into the output and fails x86, Arm64, and Arm builds, and AnyCPU executables that prefer 32-bit or native Arm64. Srt.NET loads the native library from the package's runtime assets or the directory of the application or `Srt.NET.dll`. On Windows the remaining search is limited to the safe DLL directories, so `srt.dll` is never loaded from the current directory or `PATH`; on Linux and macOS the system library search remains the fallback. To load your own libsrt build, see [use a different libsrt build](docs/advanced-usage.md#use-a-different-libsrt-build). Srt.NET's license and the dependency notices are copied into `licenses/Srt.NET/` in build and publish output.

Single-file publishing works with the default settings: the native library stays next to the executable and must be deployed with it. Set `IncludeNativeLibrariesForSelfExtract=true` only to embed it in the bundle; the host then extracts it at startup to a directory that must be writable and allow running code (`DOTNET_BUNDLE_EXTRACT_BASE_DIR`, or a per-user default).

## Verifying releases

Releases are built from source by the [CI workflow](.github/workflows/build.yml) on a version tag. The package embeds an SPDX software bill of materials, with one more SPDX document per native runtime that records libsrt and OpenSSL. Each GitHub release carries build provenance attestations for the `.nupkg` and `.snupkg` and for the package contents (the managed assemblies, the native libraries, the nuspec, and the `buildTransitive` MSBuild targets), and an SBOM attestation for the `.nupkg` release asset; `gh attestation verify` checks them. nuget.org adds its own signature to the package, which changes the `.nupkg` digest, so verify a package from nuget.org through the matching release asset or the files inside it. See [verifying releases](SECURITY.md#verifying-releases) for the exact commands. The assembly is strong-named (public key token `386853069135f01e`) for identity, not for tamper protection.

## Contributing

Build, test, and packaging instructions are in [CONTRIBUTING.md](CONTRIBUTING.md); [GOVERNANCE.md](GOVERNANCE.md) describes how the project is run, [ROADMAP.md](ROADMAP.md) its plans, and [docs/architecture.md](docs/architecture.md) its design. [docs/security.md](docs/security.md) describes what you can and cannot expect from Srt.NET's security, including the limits of SRT encryption. and the native build is described in [native/README.md](native/README.md). Report vulnerabilities privately as described in [SECURITY.md](SECURITY.md). Changes are recorded in [CHANGELOG.md](CHANGELOG.md).

## Source and license

Upstream references for the bundled libsrt release: the [C API](https://github.com/Haivision/srt/blob/v1.5.7/srtcore/srt.h) and the [API documentation](https://github.com/Haivision/srt/tree/v1.5.7/docs/API). The [API coverage audit](docs/api-coverage.md) records what Srt.NET wraps.

Srt.NET is an independent project. It is not affiliated with or endorsed by Haivision or the SRT Alliance; SRT and libsrt are their projects, used here under libsrt's MPL-2.0 license.

Srt.NET is licensed under the [Mozilla Public License 2.0](LICENSE.txt) (MPL-2.0). The MPL is a file-level copyleft: you can use the package in applications under any license, including proprietary ones, but if you distribute changed Srt.NET source files, those files stay under the MPL and their source must be made available. Each source file carries the MPL notice. The source of every release is the tagged commit in this repository, which the package's repository metadata and Source Link identify.

Bundled libsrt, OpenSSL, the GCC runtime libraries in the Linux builds, and the Microsoft Visual C++ runtime in the Windows build keep their own licenses and notices; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Build and publish output receives Srt.NET's license and those notices in `licenses/Srt.NET/`.
