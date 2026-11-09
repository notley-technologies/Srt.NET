// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net.PackageValidation;

/// <summary>The provenance manifest (srt.build-info.json) that native/cmake/StageNative.cmake writes beside each library.</summary>
internal sealed record NativeBuildInfo(
    string Rid,
    Dictionary<string, NativeDependency> Dependencies,
    string Encryption,
    string Sha256,
    string? Compiler = null,
    string? Libc = null,
    string? OpensslConfiguration = null);
