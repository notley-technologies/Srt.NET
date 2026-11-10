# Changelog

All notable changes to Srt.NET are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Package versions come from `vX.Y.Z` Git tags through MinVer. The release workflow publishes the section whose heading matches the tag (for example `## [1.2.3] - 2026-12-01`) as the GitHub release notes, and fails if that section is missing.

## [Unreleased]

### Added

- SRT socket API over libsrt's C API: callers, listeners, and rendezvous; IPv4 and IPv6; live messages and file-mode byte streams; typed options; timeouts and cancellation; readiness polling (`SrtPoller`); connection admission; message metadata; transport statistics; and native logging. The package targets .NET Standard 2.0, including .NET Framework 4.8 in x64 processes, and .NET 8 or later, where it is trimming- and Native AOT-compatible. On .NET Standard 2.0 it depends on Microsoft.Bcl.AsyncInterfaces 8.0.0 and System.Memory 4.5.5 or later.
- Srt.NET is licensed under the Mozilla Public License 2.0. The package declares the `MPL-2.0` license expression and copies the license text with the dependency notices into `licenses/Srt.NET/` in build and publish output.
- Echo client and server examples for both the asynchronous and the synchronous API; see [the examples](https://github.com/notley-technologies/Srt.NET/blob/main/examples/README.md).
- .NET Framework projects get `srt.dll` copied into their output by the package's MSBuild target, which fails x86, Arm64, and Arm builds, and AnyCPU executables that prefer 32-bit or native Arm64, with the settings that fix them. Default SDK-style executables run as x64.
- Asynchronous socket API: `ConnectAsync`, `AcceptAsync`, `SendAsync`, `ReceiveAsync`, `SendMessageAsync`, and `ReceiveMessageAsync` return `ValueTask` and accept cancellation tokens. A shared readiness engine waits on one libsrt epoll for the whole process instead of blocking a thread per pending operation. Synchronous operations use the same engine and queue, so both kinds can be mixed on one socket and complete in the order they started.
- `Connect` and `ConnectAsync` fail only once libsrt has rejected or given up on the attempt. libsrt briefly reports a socket as broken, with no rejection reason, while it completes a successful handshake; that state does not fail the connection.
- `SrtStream`, a `System.IO.Stream` adapter over a connected socket with synchronous and asynchronous reads and writes (`Span` and `Memory` overloads on .NET 8 and later). Reads return 0 after an orderly close and throw when the connection breaks.
- Typed results: `SrtErrorCode` on `SrtException`, `SrtRejectReason` for rejected connections, and `SrtKeyMaterialState` for the encryption handshake state.
- `SrtMessageOptions`, a struct for per-message time-to-live, in-order delivery, and source time, with a constructor for C# versions without init accessors (such as the C# 7.3 default of .NET Framework projects).
- `Accept` and `AcceptAsync` return a queued connection whose peer has already left, with its unread data. Reading `SrtOptions.StreamId` decodes bytes that are not valid UTF-8 as U+FFFD, as the connection handler does.
- Disposing a socket with linger enabled (the file-profile default) keeps sending its queued data in the background for up to the linger time, and the peer then sees an orderly close. Process exit and the unload of an application domain or collectible `AssemblyLoadContext` discard data that is still queued.
- Clean shutdown: libsrt is cleaned up when the process exits, and when a .NET Framework application domain or a collectible `AssemblyLoadContext` that loaded Srt.NET unloads, even while sockets or asynchronous operations are still active. A log handler that this copy of Srt.NET installed receives the warnings that cleanup logs and is then removed, so libsrt never calls into code that is exiting or unloading. Srt.NET does not keep a collectible context alive.
- Connection metrics on .NET 8 and later through the `Srt.Net` meter: `srt.connect.duration`, `srt.open_connections`, and `srt.connection.duration`, tagged with the connection role and, for failed attempts, `error.type` and `srt.reject_reason`. `srt.reject_reason` is the `SrtRejectReason` name for a defined reason, `UserDefined` for application codes of 2000 or more, and `_OTHER` for any other code. See [collect metrics](https://github.com/notley-technologies/Srt.NET/blob/main/docs/advanced-usage.md#collect-metrics).
- Native libraries for `win-x64`, `linux-x64`, `linux-arm64`, `linux-musl-x64`, `linux-musl-arm64`, `osx-x64`, and `osx-arm64`. The Linux libraries are built in pinned manylinux_2_28 (glibc) and Alpine (musl) containers. The glibc builds need glibc 2.28 or later (for example RHEL 8, Debian 10, or Ubuntu 20.04) and link the C++ runtime statically, so they do not depend on the system libstdc++ or libgcc_s. CI enforces these limits and runs the test suite on RHEL 8.
- Each runtime folder holds a build manifest, `runtimes/<rid>/native/srt.build-info.json`, with the dependency versions, the libsrt source revision, the verified OpenSSL archive checksum, the compiler, the Linux toolchain packages, and the library's SHA-256.
- The Windows library frees OpenSSL's per-thread state whenever one of libsrt's worker threads exits, so encrypted outbound connections do not leak memory as their threads come and go.
- The assembly is strong-named (public key token `386853069135f01e`), so strong-named .NET Framework applications can reference it.
- The NuGet package embeds an SPDX 2.2 software bill of materials (`_manifest/spdx_2.2/manifest.spdx.json`) and one SPDX document per native runtime (`sbom/`) that records libsrt, including the licenses of its embedded files, the statically linked OpenSSL, and the statically linked compiler runtime.
- Releases carry build provenance and SBOM attestations; [verifying releases](https://github.com/notley-technologies/Srt.NET/blob/main/SECURITY.md#verifying-releases) lists the attested files and shows how to check them.

### Security

- Bundles libsrt 1.5.7 (commit `899348d8318eb9a3c5a5b6ec43c4a1114288773a`) with OpenSSL 3.5.9 linked statically. OpenSSL security fixes require a rebuilt Srt.NET release; see [bundled native code](https://github.com/notley-technologies/Srt.NET/blob/main/SECURITY.md#bundled-native-code).
- OpenSSL is built without DSO, engine, or provider-module loading and without automatic `openssl.cnf` loading. On Linux and macOS its configuration directory is the fixed `/etc/ssl` rather than a build-machine path.
- Linux libraries export only the `srt_*` C API and link with RELRO, immediate binding, and a non-executable stack. libsrt and the statically linked OpenSSL are compiled with `-fstack-protector-strong` and `_FORTIFY_SOURCE=2`.
- The native library is loaded from the package's runtime assets or the directory of the application or `Srt.NET.dll`. On Windows the remaining search is limited to the safe DLL directories, so `srt.dll` is never loaded from the current directory or `PATH`; on Linux and macOS the system library search remains the fallback. To load your own libsrt build on .NET, use `NativeLibrary.SetDllImportResolver`; see [use a different libsrt build](https://github.com/notley-technologies/Srt.NET/blob/main/docs/advanced-usage.md#use-a-different-libsrt-build).
- Tag builds compile OpenSSL and libsrt from source in the release run; cached native binaries are never released.

[Unreleased]: https://github.com/notley-technologies/Srt.NET/commits/main
