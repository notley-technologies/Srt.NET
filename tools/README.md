# Build and package-test tools

The native build uses CMake and the upstream C/C++ toolchains, and the Linux runtimes build in pinned containers (see [native/README.md](../native/README.md)). It does not require the .NET SDK. `native/cmake/StageNative.cmake` verifies source inputs, checks Linux libraries against the portability gate, stages the native library and dependency notices, and writes the build manifest (`srt.build-info.json`) and an SPDX document. It also adjusts the macOS library identity and signature. MinVer derives package versions from Git tags. Standard .NET SDK and MSBuild commands compile the managed library and create its NuGet and symbol packages.

`Srt.NET.PackageValidation` is a C# command-line tool that validates packages and native libraries. It never builds or stages native libraries or creates a release package. Its commands are:

```text
dotnet run --project tools/Srt.NET.PackageValidation -c Release -- verify-package <package.nupkg> [rid ...]
dotnet run --project tools/Srt.NET.PackageValidation -c Release -- test-package <package.nupkg> <rid> [--aot]
dotnet run --project tools/Srt.NET.PackageValidation -c Release -- verify-native <rid> <native library> [Srt.NET.dll]
```

The supported runtime identifiers are `win-x64`, `linux-x64`, `linux-arm64`, `linux-musl-x64`, `linux-musl-arm64`, `osx-x64`, and `osx-arm64`.

## Package verification

`verify-package` checks the managed and native assets, dependency versions, hashes, and required notices. With no runtime arguments it requires all seven runtimes, as a release package must; supply runtimes after the package path to check a local partial package. For each runtime it checks:

- The `runtimes/<rid>/native/` folder holds only the library and `srt.build-info.json`. A legacy `build-info.json` is rejected, because copied into an application's output it collides with application files (NETSDK1152).
- The manifest matches the library's SHA-256, the pinned dependency versions, and the runtime's C library.
- The binary format and architecture match the runtime: ELF for Linux, PE32+ for Windows, Mach-O for macOS.
- Linux libraries pass the portability rules: only libc, libm, libpthread, libdl, librt, and the loader (glibc) or the musl libc are needed; no glibc symbol version is newer than 2.28 (none for musl); there are no libstdc++, CXXABI, or libgcc version needs; and only `srt_*` symbols are exported.
- Every library (Linux and Windows) exports every P/Invoke entry point that the packaged `Srt.NET.dll` assemblies declare. Mach-O exports are not parsed yet; macOS libraries get the header and architecture checks only.

For the package as a whole it checks that the nuspec license expression is `MPL-2.0`, that the readme is `PACKAGE.md`, that its links are absolute `https://` or `#fragment` links (inline, reference-style, and HTML), and that its images come from hosts nuget.org shows. When the package embeds an SBOM (`_manifest/spdx_2.2/manifest.spdx.json`), every file it lists must be in the package.

`verify-native` runs the same binary checks on a loose library, for example right after a native build, and also checks P/Invoke exports when given an `Srt.NET.dll`.

## Package installation tests

`test-package` verifies the package for its runtime, then installs it into fresh consumer applications on the matching host. Each run uses a fresh directory and a private package cache. Only the supplied Srt.NET package and a locally built wrapper fixture are placed in the test feed; NuGet source mapping binds both package IDs to that feed, and other dependencies come from nuget.org. The runner also compares the restored Srt.NET archive's SHA-256 with the supplied archive.

The consumer matrix exercises:

- An application referencing Srt.NET directly, and one referencing a wrapper package that depends on Srt.NET.
- Build output and publish output for each reference pattern.
- .NET 10 on every runtime, plus .NET Framework 4.8 on Windows. On Windows it also builds a .NET Framework consumer with no platform target, which must run as x64, checks that an x86 build fails with Srt.NET's architecture error, and builds and runs a .NET Framework consumer on C# 7.3, that framework's default language version, with warnings treated as errors.
- With `--aot`, a Native AOT publish of the direct consumer for the host runtime, with trimming warnings treated as errors. It needs a Microsoft-built SDK and a native toolchain (MSVC, clang with zlib headers on Linux, or the Xcode command line tools); Linux distribution source-built SDKs, such as RHEL's, cannot publish Native AOT.

Each output runs from its own directory and must include the native library and dependency notices. The shared [smoke test](../tests/PackageConsumers/PackageSmokeTest.cs) exercises encryption, readiness, messages, statistics, the native logging callback, and asynchronous I/O, and deliberately leaves a socket open to check that the process still exits cleanly. The wrapper is a test fixture and must never be published. The consumer templates stay outside the main solution because they need a packed Srt.NET artifact before restore.

## Benchmarks

Performance measurements live in `benchmarks/Srt.NET.Benchmarks` and use BenchmarkDotNet with its memory diagnoser. Run them after staging the native library:

```bash
dotnet run -c Release -f net10.0 --project benchmarks/Srt.NET.Benchmarks -- --filter "*"
dotnet run -c Release -f net10.0 --project benchmarks/Srt.NET.Benchmarks -- --filter "*" --runtimes net8.0 net10.0
```

`OperationBenchmarks` groups nonblocking calls by category; only the receive category compares managed calls with a raw P/Invoke baseline, and the others report absolute costs, `TransferBenchmarks` measures loopback send-then-receive exchanges, and `WaitStrategyBenchmarks` shows why the readiness engine keeps one long-lived native poll instead of creating one per wait. Use BenchmarkDotNet's standard options (`--job short`, `--exporters`, `--artifacts`) rather than custom timing code. Loopback timings are local diagnostics, not network throughput or tail-latency benchmarks.
