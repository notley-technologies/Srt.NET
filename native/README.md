# Native build

This folder builds libsrt with a statically linked OpenSSL for every Srt.NET runtime and stages the result in `artifacts/`. [CONTRIBUTING.md](../CONTRIBUTING.md) has the build commands; this page explains what the build guarantees and how to maintain it.

| Path | Purpose |
| --- | --- |
| `versions.json` | Pinned libsrt commit and OpenSSL release (URL, SHA-256, signing key) |
| `openssl-release-key.asc` | OpenSSL release-signing key used to verify the downloaded archive |
| `CMakeLists.txt` | Builds OpenSSL, then libsrt, then runs `cmake/StageNative.cmake` |
| `build-linux.sh`, `build-macos.sh`, `build-windows.cmd` | Complete build recipes per operating system, used by CI and contributors |
| `cmake/StageNative.cmake` | Checks the sources, verifies and copies the library, writes the manifest, notices and SBOM |
| `cmake/VerifyElf.cmake`, `cmake/VerifyMachO.cmake` | Portability gates for Linux and macOS libraries |
| `cmake/NativeSbom.cmake`, `cmake/CollectNotices.cmake` | SPDX 2.2 document and dependency notices for the staged library |
| `cmake/UpdateOpenSSL.cmake`, `cmake/VerifyOpenSSL.cmake` | Verified OpenSSL updates |
| `linux/libsrt.map` | Linux version script: exports only `srt_*` |
| `macos/libsrt.exp` | macOS exported symbols list: exports only `_srt_*` |
| `windows/` | `DllMain` for `srt.dll` that frees OpenSSL's per-thread state (added to libsrt's build without patching it), and `PrepareWindows.cmake`, which downloads the pinned Perl and NASM |
| `docker/` | Pinned Linux build images used by `build-linux.sh` |

The Linux test containers (RHEL 8 and Alpine) are not part of the native build; they live in [tests/linux](../tests/linux).

CI builds with the same scripts (`build-windows.cmd`, `build-macos.sh`, `build-linux.sh`). The CI native cache key combines a hash of this folder with the extra CMake arguments the workflow passes (`NATIVE_CMAKE_ARGS`), so it changes whenever the recipe does. Windows and macOS keys also include the runner image version, because those builds use the runner's compiler.

## Runtimes

| Runtime | Built on | Runs on |
| --- | --- | --- |
| `win-x64` | Visual Studio 2022, static CRT | Windows 10 and later |
| `linux-x64`, `linux-arm64` | manylinux_2_28 (AlmaLinux 8, GCC 14) | glibc 2.28 and later: RHEL 8, Debian 10, Ubuntu 20.04 and later |
| `linux-musl-x64`, `linux-musl-arm64` | Alpine 3.22 (musl 1.2.5, GCC 14) | musl 1.2.5 and later (Alpine 3.22, the oldest supported Alpine branch, and later) |
| `osx-x64`, `osx-arm64` | Xcode command line tools | macOS 13 and later |

CMake detects musl from the compiler's target triple, and the requested runtime must match the host. Linux runtimes are built with `build-linux.sh`, which runs the CMake build in the pinned image for that runtime.

## What every build guarantees

- **Pinned, verified sources.** OpenSSL is downloaded over TLS and checked against `versions.json`; libsrt is fetched at its pinned commit. Staging fails if either source differs or has local changes.
- **Nothing loaded at run time.** OpenSSL is configured with `no-shared no-module no-dso no-engine no-autoload-config`: it cannot load engines, provider modules or `openssl.cnf`. On Linux and macOS its configuration directory is the fixed, root-owned `/etc/ssl` rather than the build tree. Its prefix, from which it derives the (unused) module and engine directories compiled into libcrypto, is the fixed `/usr` on Linux and macOS and OpenSSL's default `C:\Program Files\OpenSSL` on Windows; OpenSSL is installed below it with `DESTDIR`, so these directories do not name the build tree.
- **Per-thread cleanup on Windows.** A static OpenSSL frees each thread's state (such as its random number generators) only when `OPENSSL_thread_stop()` runs on that thread. `windows/dllmain.c` calls it for every thread that exits, as libcrypto's own `DllMain` does in a shared build, so libsrt's short-lived worker threads do not leak it. `windows/AddDllMain.cmake` adds the file to libsrt's `srt_shared` target. Linux and macOS free the state through thread-local destructors.
- **Hardened Windows DLL.** OpenSSL and libsrt are compiled with Control Flow Guard (`/guard:cf`), and `srt.dll` is linked with `/guard:cf` and `/CETCOMPAT` (CET shadow stack compatible). MSVC has no documented option that removes source paths from `__FILE__`, so libsrt's log locations and OpenSSL's error records still name the build tree's source files.
- **Manifest.** `runtimes/<rid>/native/srt.build-info.json` records the runtime, dependency versions, binary SHA-256, OpenSSL configuration, compiler and, on Linux, the C library requirement and the distribution packages that supplied the compilers, binutils, static C++ runtime and C library files. It has an Srt.NET-specific name because it is copied into application output.
- **SBOM.** `artifacts/sbom/native-<rid>.spdx.json` (SPDX 2.2) describes the binary (SHA-1 and SHA-256), the libsrt commit it was built from, the statically linked OpenSSL release (Apache-2.0, with its CPE and archive checksum) and the statically linked compiler runtime: the GCC runtime on Linux (GPL-3.0-or-later WITH GCC-exception-3.1) and the MSVC runtime on Windows (license NOASSERTION, because the Visual Studio license terms have no SPDX identifier). libsrt is declared MPL-2.0; its concluded license, `MPL-2.0 AND BSD-3-Clause AND Zlib AND Unlicense`, also covers the University of Illinois UDT notice, the Aladdin MD5 notice and the Unlicense of `atomic.h` and `atomic_msvc.h`. The win-x64 SBOM also lists `windows/dllmain.c` (MPL-2.0). `NativeSbom.cmake` explains which small toolchain objects are not listed, such as glibc's `libc_nonshared.a` members. The namespace is derived from the binary hash; `SOURCE_DATE_EPOCH` sets the creation time.

## Linux portability gate

`cmake/VerifyElf.cmake` inspects the library with `readelf` before staging, and the build fails unless the library:

- is a 64-bit shared object for the runtime's architecture;
- needs only `libc.so.6`, `libm.so.6`, `libpthread.so.0`, `libdl.so.2`, `librt.so.1` and the dynamic loader (glibc), or only `libc.musl-<arch>.so.1` (musl), including that runtime's C library;
- needs no glibc symbol version newer than 2.28, and no `GLIBCXX`, `CXXABI` or `GCC` versions;
- imports no unversioned global symbols from glibc;
- exports only `srt_*` symbols;
- has full RELRO (`-z relro -z now`), a non-executable stack and no text relocations.

OpenSSL and libsrt are compiled with `-fstack-protector-strong` and `_FORTIFY_SOURCE=2`; the manylinux compiler enables neither by default. libsrt also uses hidden visibility and a file-prefix map that removes build paths from its log locations. It is linked with `-static-libstdc++ -static-libgcc`, `--exclude-libs,ALL`, `--gc-sections`, `--no-undefined` and `libsrt.map`. The static C++ and GCC runtimes come precompiled with the toolchain.

## macOS portability gate

`VerifyMachO.cmake` inspects the library with `otool`, `nm` and `lipo` before staging, and the build fails unless the library:

- is a thin 64-bit dynamic library for the runtime's architecture (`x86_64` or `arm64`);
- loads only `/usr/lib/libc++.1.dylib` and `/usr/lib/libSystem.B.dylib`;
- has no `LC_RPATH` entries;
- requires macOS 13.0 or earlier (`LC_BUILD_VERSION` minos);
- exports only `_srt_*` symbols.

libsrt is compiled with hidden visibility and a file-prefix map, and linked with `-dead_strip` and `libsrt.exp`. `Srt.NET.PackageValidation verify-package` applies the same dependency, run-path, minimum version and export rules to the packed library with its own Mach-O reader.

## Reproducibility

`build-linux.sh` always builds in `/build/<rid>` inside the container, so a rebuild does not depend on the host. The glibc runtimes rebuild byte-identically from the same image digests and sources as long as the image contents are unchanged: the manylinux images contain their GCC toolset, but they may update packages such as `glibc-devel` when the image is built, so `srt.build-info.json` lists the toolchain package versions that were used. The Alpine image installs its toolchain (GCC, binutils, musl-dev, fortify-headers) from the Alpine 3.22 repository when it is built, and that repository keeps only the newest revision of each package. musl rebuilds are therefore byte-identical only while those package revisions are unchanged; `srt.build-info.json` lists the versions that were used. On Windows, `srt.dll` is linked with `/Brepro`, which writes a content hash instead of a timestamp into the PE header, so two clean builds of the same sources in the same build tree and Visual Studio version produce an identical file; the macOS build has not yet been checked for reproducibility. When `build-linux.sh` runs on a Windows host, its build tree lives in the Docker volume `srtnet-native-build`, because Docker Desktop's drive mounts are too slow for OpenSSL's many small files; on Linux it is `artifacts/native-build/<rid>`. Set `SRTNET_BUILD_VOLUME` to override either.

## Updating the build images

Every `FROM` line in `docker/` pins a tag and its digest. Dependabot proposes digest refreshes and manylinux date tags; moving to a new .NET major version or a new Alpine branch stays a manual change, together with `global.json`, the test target frameworks and the musl baseline. To update by hand, look up the digest with `docker buildx imagetools inspect <image>:<tag>` (use the multi-architecture index digest where the image has one), update tag and digest together, rebuild all four Linux runtimes, and run the managed tests in the musl and RHEL 8 test images. Raise the glibc baseline in `cmake/VerifyElf.cmake` only together with the manylinux image and the supported-platform documentation.
