# Architecture

Srt.NET is a managed wrapper over the libsrt C API. The library has three layers: the public API, the asynchronous engine, and the interop layer that talks to libsrt. Around it are the native build that produces libsrt for each runtime and the packaging and release pipeline. This page describes how they fit together; [security.md](security.md) describes the security properties and trust boundaries, and the [repository layout](https://github.com/notley-technologies/Srt.NET/blob/main/CONTRIBUTING.md#repository-layout) maps them to folders.

## Public API (`Srt.Net`, `src/Srt.NET/`)

| Type | Role |
| --- | --- |
| `SrtSocket` | An SRT socket: bind, listen, accept, connect (caller and rendezvous), send and receive (bytes or messages), options, statistics. Every operation has a synchronous form and, where it can wait, a `ValueTask` form. |
| `SrtStream` | A `System.IO.Stream` over a connected file-mode socket. |
| `SrtPoller` | Optional application-owned readiness polling over libsrt's epoll. |
| `SrtOptions`, `SrtOption<T>` | Typed socket options; each knows its native identifier, value type, size limits and whether it is readable. |
| `SrtStatistics` | A copy of libsrt's transport statistics (all 82 fields of `SRT_TRACEBSTATS`). |
| `SrtConnectionRequest` | The admission decision for an incoming connection (peer address, stream ID, per-peer options, rejection reason). |
| `SrtLogging` | Process-wide native logging configuration and a bounded managed log handler. |
| `SrtException`, `SrtErrors`, `SrtErrorCode`, `SrtRejectReason` | Typed errors and native error descriptions. |
| `SrtMetrics` (internal, .NET 8 and later) | The `Srt.Net` meter: connection counts and durations. |

## Interop layer (`Srt.Net.Interop`, internal, `src/Srt.NET/Interop/`)

- **`NativeMethods`** declares the libsrt entry points used (`DllImport` with blittable arguments; the log and listen callbacks are delegates kept alive for as long as libsrt can call them). The assembly restricts native library loading to the package's runtime assets, the application and `Srt.NET.dll` directories, and the safe system directories.
- **Codecs** translate data that crosses the boundary: `SocketAddressCodec` (IPv4/IPv6 `sockaddr` layouts, including the BSD length-prefixed form on macOS), `SocketOptionCodec` (option values, including strict UTF-8 strings with native length limits and the platform-specific `linger` layout) and `Utf8Interop` (bounded reads of native strings). Native structures (`NativeStatistics`, `NativeMessageControl`, `NativePollEvent`) have their layouts pinned by tests against libsrt 1.5.7.
- **`SrtRuntime`** reference-counts `srt_startup`/`srt_cleanup` and cleans libsrt up when the process exits or the loading application domain or collectible `AssemblyLoadContext` unloads.
- **`SrtSocketHandle`** is a `SafeHandle` for the libsrt socket identifier. Every native call borrows it through an `SrtSocketLease`, so a socket cannot be closed (and its identifier reused by libsrt) while a call is in progress, and calls after disposal fail instead of reaching another socket.

## Asynchronous engine (`Srt.Net.Async`, internal, `src/Srt.NET/Async/`)

One background thread, `SrtAsyncEngine`, waits on a single edge-triggered libsrt epoll for every socket with pending work, so idle connections cost no threads.

- Each socket gets an `SrtAsyncContext` with two `SrtOperationQueue`s (send and receive). Operations complete in the order they started; synchronous calls queue as `SyncOperation`s, so both kinds can be mixed on one socket.
- Readiness edges are dispatched to the thread pool; the engine thread never runs user continuations or blocking native calls.
- libsrt does not raise a new writable edge when buffer space frees for a message that did not fit, so such sends retry on a timer that backs off from 10 ms to 250 ms. A 250 ms watchdog retries any queue that saw no edge, as a backstop.
- Disposing a socket with linger enabled hands it to the engine, which closes it once its queued data has drained or the linger time ends, without blocking the caller.
- The engine thread stops after 10 seconds without registered sockets and restarts on demand.

Operations are pooled `IValueTaskSource` implementations (`ManualResetValueTaskSourceCore`), so steady-state sends and receives do not allocate.

## Native build (`native/`)

A CMake superbuild downloads the pinned OpenSSL release (verified by SHA-256 and the OpenSSL release signature), fetches libsrt at its pinned commit, builds OpenSSL as a minimal static library and links it into libsrt. Linux builds run in digest-pinned manylinux_2_28 (glibc) and Alpine (musl) containers through `build-linux.sh`; Windows and macOS builds use `build-windows.cmd` and `build-macos.sh`. Staging (`cmake/StageNative.cmake`) checks the binary (ELF portability and exports on Linux, Mach-O dependencies and exports on macOS), and writes `srt.build-info.json`, the dependency notices and an SPDX document. See [native/README.md](https://github.com/notley-technologies/Srt.NET/blob/main/native/README.md).

## Packaging

`src/Srt.NET/Srt.NET.csproj` packs the netstandard2.0 and net8.0 assemblies, the native libraries under `runtimes/<rid>/native/`, the license and notices, the native SPDX documents, and an SPDX SBOM for the package. `buildTransitive/Srt.NET.targets` (from `src/Srt.NET/buildTransitive/`) copies `srt.dll` for .NET Framework projects and rejects builds that would not run as x64. `tools/Srt.NET.PackageValidation` verifies a packed package (`verify-package`) and installs it into fresh applications built from the templates in `tests/PackageConsumers`, including Native AOT (`test-package`).

## Continuous integration and release

`.github/workflows/build.yml` lints, builds and tests on every runtime, runs the compatibility tests on RHEL 8 (the containers in `tests/linux`), packs and verifies the package, and on a `vX.Y.Z` tag rebuilds everything from source, attests the outputs, creates the GitHub release and publishes to nuget.org with trusted publishing. CodeQL, OpenSSF Scorecard and dependency review run in their own workflows. [CONTRIBUTING.md](https://github.com/notley-technologies/Srt.NET/blob/main/CONTRIBUTING.md) describes the commands and the release steps.
