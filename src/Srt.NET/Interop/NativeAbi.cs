// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;

namespace Srt.Net.Interop;

/// <summary>The libsrt releases for which Srt.NET reads the statistics structure.</summary>
/// <remarks>
/// <c>srt_bstats</c> and <c>srt_bistats</c> fill <see cref="NativeStatistics"/> without a size argument, so a release
/// that added fields would write past it. Releases 1.5.0 through 1.5.7 were checked against their headers, and each
/// release bundled since is checked at build time: the native build compiles <c>native/abi/managed_abi.c</c> against
/// the bundled headers and fails if the layout differs, and <c>native/cmake/UpdateLibsrt.cmake</c> raises
/// <see cref="VerifiedVersion"/> to the bundled release. Other releases in the range, such as ones that were never
/// bundled or a backport published after a newer release was bundled, are accepted on the assumption that the layout
/// libsrt has kept since 1.4.2 is unchanged. Newer releases are rejected.
/// </remarks>
internal static class NativeAbi
{
    internal static readonly Version MinimumVersion = new(1, 5, 0);

    internal static readonly Version VerifiedVersion = new(1, 5, 7);

    internal static bool HasVerifiedStatisticsLayout(Version version)
    {
        return version >= MinimumVersion && version <= VerifiedVersion;
    }
}
