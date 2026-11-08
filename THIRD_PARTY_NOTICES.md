# Native dependencies

Srt.NET is licensed under the Mozilla Public License 2.0 (`LICENSE.txt` in the repository, `licenses/Srt.NET-LICENSE.txt` in the package). Its managed implementation calls the public libsrt C ABI through P/Invoke.

Release packages include the following separately licensed native software:

| Component | Version | License used | Source |
| --- | --- | --- | --- |
| Haivision libsrt | 1.5.7 | Mozilla Public License 2.0; some files carry the University of Illinois UDT license (BSD-3-Clause), Aladdin's MD5 license (Zlib), or the Unlicense | [Exact source revision](https://github.com/Haivision/srt/tree/899348d8318eb9a3c5a5b6ec43c4a1114288773a) |
| OpenSSL | 3.5.9 | Apache License 2.0 | [Official release](https://github.com/openssl/openssl/releases/tag/openssl-3.5.9) |
| GCC runtime libraries (libstdc++, libgcc), Linux runtimes only | From the GCC 14 build toolchain | GPL-3.0-or-later WITH GCC Runtime Library Exception 3.1 | [GCC releases](https://gcc.gnu.org/releases.html) |
| Microsoft Visual C++ runtime libraries (C runtime, Universal CRT, C++ standard library), `win-x64` only | From the Visual Studio 2022 build tools | Visual Studio license terms for redistributable code | Linked statically into `srt.dll` with `/MT` |

Copyright notices and license texts are retained in the package's `licenses/` directory, including the University of Illinois UDT BSD notice and Aladdin's MD5 notice carried by libsrt. The native build uses these sources without patches and links OpenSSL statically into libsrt. Build configuration is in `native/CMakeLists.txt`; source pins are in `native/versions.json`. Each native binary is accompanied by `srt.build-info.json` with its dependency versions, the SRT source revision, the verified OpenSSL source archive checksum, the compiler, and the native binary SHA-256 digest, and by an SPDX document (`sbom/native-<rid>.spdx.json` in the package).

The Linux libraries link the GCC C++ runtime (libstdc++ and libgcc) statically, so they do not depend on the system's copies. That code is covered by the GCC Runtime Library Exception, which allows a library compiled with GCC through an eligible compilation process, as these builds are, to be distributed under terms of the distributor's choice. The exact compiler version is recorded in each `srt.build-info.json`.

The Windows library links the Microsoft Visual C++ runtime statically, so it needs no separate runtime installation. Those libraries are redistributed under the Visual Studio license terms, which have no SPDX identifier, so the native SBOM records their license as NOASSERTION. The compiler version is also recorded in `srt.build-info.json`.

The source of the MPL-covered libsrt component is available at the revision linked above under MPL 2.0. Recipients may obtain, modify, and redistribute that component under that license. Srt.NET's MPL-2.0 license does not replace these dependency licenses or restrict the rights they grant.

See [Mozilla's MPL FAQ](https://www.mozilla.org/en-US/MPL/2.0/FAQ/) for the distinction between separate files in a larger work and modifications to MPL-covered files.
