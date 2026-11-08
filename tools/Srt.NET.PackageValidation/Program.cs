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

                case ["verify-native", string rid, string library, .. string[] assemblies] when assemblies.Length <= 1:
                    VerifyNative(rid, library, assemblies.Length == 0 ? null : assemblies[0]);
                    break;

                default:
                    Console.Error.WriteLine("Usage: Srt.NET.PackageValidation verify-package <package.nupkg> [rid ...]");
                    Console.Error.WriteLine("       Srt.NET.PackageValidation test-package <package.nupkg> <rid> [--aot]");
                    Console.Error.WriteLine("       Srt.NET.PackageValidation verify-native <rid> <native library> [Srt.NET.dll]");
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

    private static void VerifyNative(string rid, string library, string? assembly)
    {
        SortedSet<string>? entryPoints = assembly == null ? null : PInvokeEntryPoints.ReadFile(assembly);
        string summary = NativeLibraryVerifier.Verify(rid, File.ReadAllBytes(library), entryPoints);
        string checkedEntryPoints = entryPoints == null ? "P/Invoke entry points not checked" : $"exports all {entryPoints.Count} P/Invoke entry points";
        Console.WriteLine($"Verified {library} for {rid}: {summary}; {checkedEntryPoints}");
    }
}
