// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net.PackageConsumers;

internal static class Program
{
    private static void Main()
    {
#if TRANSITIVE_PACKAGE
        PackageWrapper.Run();
#else
        PackageSmokeTest.Run();
#endif
    }
}
