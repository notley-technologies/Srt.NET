// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.PackageTests;

/// <summary>A symbol version that an ELF image requires from one of its dependencies, such as GLIBC_2.17 from libc.so.6.</summary>
internal sealed record ElfVersionNeed(string File, string Version);
