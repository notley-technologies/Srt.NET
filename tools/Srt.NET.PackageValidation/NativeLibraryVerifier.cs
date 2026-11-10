// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System.Reflection.PortableExecutable;

namespace Srt.Net.PackageValidation;

/// <summary>Checks a packed native library that no build-time gate inspects: the Windows srt.dll.</summary>
internal static class NativeLibraryVerifier
{
    /// <summary>Verifies one binary and returns a one-line description; throws listing every rule it breaks.</summary>
    /// <param name="rid">A supported runtime identifier.</param>
    /// <param name="binary">The native library contents.</param>
    internal static string Verify(string rid, byte[] binary)
    {
        // native/cmake/VerifyElf.cmake and VerifyMachO.cmake gate Linux and macOS libraries when they are staged, and the
        // manifest hash ties the packed file to that staged file.
        if (!rid.StartsWith("win-", StringComparison.Ordinal))
        {
            return "portability checked by the native build";
        }

        PeImage image = PeImage.Read(binary);
        List<string> failures = [];

        if (image.Machine != Machine.Amd64 || !image.IsPE32Plus)
        {
            failures.Add($"PE machine is {image.Machine} ({(image.IsPE32Plus ? "PE32+" : "PE32")}); win-x64 requires Amd64 (PE32+)");
        }

        if (!image.IsDll)
        {
            failures.Add("the PE image is not a DLL");
        }

        // native/CMakeLists.txt links srt.dll with these; a stale build tree can silently drop them.
        DllCharacteristics required = DllCharacteristics.DynamicBase | DllCharacteristics.HighEntropyVirtualAddressSpace | DllCharacteristics.NxCompatible;

        if ((image.DllCharacteristics & required) != required)
        {
            failures.Add($"the DLL lacks ASLR, high-entropy ASLR, or DEP (DllCharacteristics {image.DllCharacteristics})");
        }

        if (!image.HasControlFlowGuard)
        {
            failures.Add("the DLL was not linked with Control Flow Guard (/guard:cf)");
        }

        if (!image.IsCetCompatible)
        {
            failures.Add("the DLL is not marked CET shadow stack compatible (/CETCOMPAT)");
        }

        if (failures.Count != 0)
        {
            string separator = Environment.NewLine + "  - ";

            throw new InvalidDataException($"The {rid} native library failed verification:{separator}{string.Join(separator, failures)}");
        }

        return $"PE machine {image.Machine}; ASLR, DEP, CFG and CET";
    }
}
