// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System.Text.RegularExpressions;

namespace Srt.PackageTests;

internal static partial class PackageVersion
{
    internal static void Validate(string version)
    {
        if (!MyRegex().IsMatch(version))
        {
            throw new ArgumentException("Use a three-part NuGet version with an optional prerelease suffix.");
        }
    }

    [GeneratedRegex(@"\A[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?\z")]
    private static partial Regex MyRegex();
}
