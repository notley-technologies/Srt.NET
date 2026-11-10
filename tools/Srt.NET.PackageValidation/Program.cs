// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net.PackageValidation;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            switch (args)
            {
                case ["verify-package", string package, .. string[] runtimes]:
                    PackageVerifier.Verify(package, runtimes.Length == 0 ? ToolContext.NativeFiles.Keys : runtimes);
                    break;

                case ["test-package", string package, string rid]:
                    PackageConsumer.Run(package, rid, nativeAot: false);
                    break;

                case ["test-package", string package, string rid, "--aot"]:
                    PackageConsumer.Run(package, rid, nativeAot: true);
                    break;

                default:
                    Console.Error.WriteLine("Usage: Srt.NET.PackageValidation verify-package <package.nupkg> [rid ...]");
                    Console.Error.WriteLine("       Srt.NET.PackageValidation test-package <package.nupkg> <rid> [--aot]");
                    Console.Error.WriteLine($"Runtimes: {string.Join(", ", ToolContext.NativeFiles.Keys)}");

                    return 1;
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);

            return 1;
        }
    }
}
