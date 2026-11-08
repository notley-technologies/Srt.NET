// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System.Reflection.PortableExecutable;

namespace Srt.PackageTests;

/// <summary>Checks that a native libsrt binary matches its runtime identifier and can load on every supported system.</summary>
internal static class NativeLibraryVerifier
{
    /// <summary>The newest glibc symbol version a glibc build may require (RHEL 8 and manylinux_2_28 ship glibc 2.28).</summary>
    internal static readonly Version MaximumGlibc = new(2, 28);

    /// <summary>The newest minimum macOS version a macOS build may require (LC_BUILD_VERSION minos); Srt.NET supports macOS 13 and later.</summary>
    internal static readonly Version MaximumMacOS = new(13, 0, 0);

    /// <summary>The only libraries a macOS build may load: the system C++ runtime and libSystem.</summary>
    private static readonly string[] AllowedMacOSLibraries = ["/usr/lib/libc++.1.dylib", "/usr/lib/libSystem.B.dylib"];

    private static readonly string[] CompilerRuntimeVersionPrefixes = ["GLIBCXX_", "CXXABI_", "GCC_"];

    /// <summary>Verifies one binary and returns a one-line description; throws listing every rule it breaks.</summary>
    /// <param name="rid">A supported runtime identifier.</param>
    /// <param name="binary">The native library contents.</param>
    /// <param name="entryPoints">P/Invoke entry points that must be exported, or null to skip that check.</param>
    internal static string Verify(string rid, byte[] binary, IReadOnlyCollection<string>? entryPoints)
    {
        if (!TestContext.NativeFiles.ContainsKey(rid))
        {
            throw new ArgumentException($"Unsupported runtime: {rid}");
        }

        List<string> failures = [];
        string summary;

        if (rid.StartsWith("linux-", StringComparison.Ordinal))
        {
            summary = VerifyElf(rid, binary, entryPoints, failures);
        }
        else if (rid.StartsWith("win-", StringComparison.Ordinal))
        {
            summary = VerifyPe(binary, entryPoints, failures);
        }
        else
        {
            summary = VerifyMachO(rid, binary, entryPoints, failures);
        }

        if (failures.Count != 0)
        {
            string separator = Environment.NewLine + "  - ";

            throw new InvalidDataException($"The {rid} native library failed verification:{separator}{string.Join(separator, failures)}");
        }

        return summary;
    }

    private static string VerifyElf(string rid, byte[] binary, IReadOnlyCollection<string>? entryPoints, List<string> failures)
    {
        bool arm64 = rid.EndsWith("-arm64", StringComparison.Ordinal);
        bool musl = rid.StartsWith("linux-musl-", StringComparison.Ordinal);
        ushort machine = arm64 ? ElfImage.MachineAArch64 : ElfImage.MachineX64;

        if (!ElfImage.IsElf64LittleEndian(binary))
        {
            failures.Add("not a little-endian ELF64 file");

            return "not ELF";
        }

        ElfImage image = ElfImage.Read(binary);

        if (image.Machine != machine)
        {
            failures.Add($"ELF e_machine is {image.Machine}; {rid} requires {machine}");
        }

        // Only the C library may be dynamic: libstdc++, libgcc_s, and OpenSSL are linked statically.
        string libc = musl ? (arm64 ? "libc.musl-aarch64.so.1" : "libc.musl-x86_64.so.1") : "libc.so.6";
        string[] allowed = musl
            ? [libc]
            : ["libc.so.6", "libm.so.6", "libpthread.so.0", "libdl.so.2", "librt.so.1", arm64 ? "ld-linux-aarch64.so.1" : "ld-linux-x86-64.so.2"];
        string[] unexpected = [.. image.Needed.Where(name => !allowed.Contains(name, StringComparer.Ordinal))];

        if (unexpected.Length != 0)
        {
            failures.Add($"needs libraries outside the {(musl ? "musl" : "glibc")} allowlist: {string.Join(", ", unexpected)}");
        }

        if (!image.Needed.Contains(libc, StringComparer.Ordinal))
        {
            failures.Add($"does not need {libc}, so it was not built for {(musl ? "musl" : "glibc")}");
        }

        string[] versions = [.. image.VersionNeeds.Select(need => need.Version).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        string[] compilerRuntime = [.. versions.Where(version => CompilerRuntimeVersionPrefixes.Any(prefix => version.StartsWith(prefix, StringComparison.Ordinal)))];

        if (compilerRuntime.Length != 0)
        {
            failures.Add($"requires C++ or GCC runtime symbol versions: {string.Join(", ", compilerRuntime)}");
        }

        Version? newestGlibc = null;

        foreach (string version in versions.Where(version => version.StartsWith("GLIBC_", StringComparison.Ordinal)))
        {
            if (musl)
            {
                failures.Add($"requires glibc symbol version {version}, but musl has no symbol versions");
            }
            else if (!Version.TryParse(version["GLIBC_".Length..], out Version? parsed))
            {
                // For example GLIBC_PRIVATE, or GLIBC_ABI_DT_RELR from linking with -z pack-relative-relocs (glibc 2.36).
                failures.Add($"requires {version}, which is not a numbered glibc release");
            }
            else if (newestGlibc == null || parsed > newestGlibc)
            {
                newestGlibc = parsed;
            }
        }

        if (newestGlibc > MaximumGlibc)
        {
            failures.Add($"requires GLIBC_{newestGlibc}; the newest allowed is GLIBC_{MaximumGlibc}");
        }

        string[] foreign = [.. image.Exports.Where(name => !name.StartsWith("srt_", StringComparison.Ordinal))];

        if (foreign.Length != 0)
        {
            failures.Add($"exports {foreign.Length} symbols outside the srt_ API, for example {string.Join(", ", foreign.Take(5))}");
        }

        VerifyEntryPoints(image.Exports, entryPoints, failures);

        return $"ELF machine {image.Machine}; soname {image.SharedObjectName ?? "none"}; needs {string.Join(", ", image.Needed)}; "
            + $"newest glibc version {newestGlibc?.ToString() ?? "none"}; {image.Exports.Count} exports";
    }

    private static string VerifyPe(byte[] binary, IReadOnlyCollection<string>? entryPoints, List<string> failures)
    {
        PeImage image = PeImage.Read(binary);

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

        VerifyEntryPoints(image.Exports, entryPoints, failures);

        return $"PE machine {image.Machine}; CFG and CET; {image.Exports.Count} exports";
    }

    private static string VerifyMachO(string rid, byte[] binary, IReadOnlyCollection<string>? entryPoints, List<string> failures)
    {
        uint cpuType = rid == "osx-arm64" ? MachOImage.CpuTypeArm64 : MachOImage.CpuTypeX64;

        if (!MachOImage.IsMachO(binary))
        {
            failures.Add("not a 64-bit Mach-O image or universal file");

            return "not Mach-O";
        }

        List<uint> cpuTypes = MachOImage.ReadCpuTypes(binary);

        // Same rule as native/VerifyMachO.cmake: each runtime ships a thin, single-architecture library.
        if (cpuTypes.Count != 1)
        {
            failures.Add($"a single-architecture dylib is required, but the file has {cpuTypes.Count} architectures");
        }

        if (!cpuTypes.Contains(cpuType))
        {
            failures.Add($"contains no image for CPU type 0x{cpuType:X8} (has {string.Join(", ", cpuTypes.Select(type => $"0x{type:X8}"))})");

            return "Mach-O without the required architecture";
        }

        MachOImage image = MachOImage.Read(binary, cpuType);

        if (image.FileType != MachOImage.FileTypeDylib)
        {
            failures.Add($"Mach-O file type is {image.FileType}; a dynamic library (MH_DYLIB, {MachOImage.FileTypeDylib}) is required");
        }

        // OpenSSL is linked statically: only the system C++ runtime and libSystem may be loaded, from fixed paths.
        string[] unexpected = [.. image.Dependencies.Where(name => !AllowedMacOSLibraries.Contains(name, StringComparer.Ordinal))];

        if (unexpected.Length != 0)
        {
            failures.Add($"loads libraries outside the macOS system library allowlist: {string.Join(", ", unexpected)}");
        }

        if (!image.Dependencies.Contains("/usr/lib/libSystem.B.dylib", StringComparer.Ordinal))
        {
            failures.Add("does not load /usr/lib/libSystem.B.dylib");
        }

        if (image.RunPaths.Count != 0)
        {
            failures.Add($"has LC_RPATH entries: {string.Join(", ", image.RunPaths)}");
        }

        // native/StageNative.cmake sets this install name after the CMake gate has run, so only the packed file shows it.
        if (image.InstallName != "@rpath/libsrt.dylib")
        {
            failures.Add($"install name is '{image.InstallName}', not @rpath/libsrt.dylib");
        }

        Version? minimum = null;

        if (image.MinimumVersions.Count != 1)
        {
            failures.Add($"has {image.MinimumVersions.Count} minimum OS version load commands; exactly one, for macOS, is required");
        }
        else if (image.MinimumVersions[0].Key != MachOImage.PlatformMacOS)
        {
            failures.Add($"is built for platform {image.MinimumVersions[0].Key}, not macOS ({MachOImage.PlatformMacOS})");
        }
        else
        {
            minimum = image.MinimumVersions[0].Value;

            if (minimum > MaximumMacOS)
            {
                failures.Add($"requires macOS {minimum}; the newest allowed minimum is {MaximumMacOS}");
            }
        }

        string[] foreign = [.. image.Exports.Where(name => !name.StartsWith("srt_", StringComparison.Ordinal))];

        if (foreign.Length != 0)
        {
            failures.Add($"exports {foreign.Length} symbols outside the srt_ API, for example {string.Join(", ", foreign.Take(5))}");
        }

        if (image.Exports.Count == 0)
        {
            failures.Add($"exports no symbols (read from: {image.ExportSource})");
        }

        VerifyEntryPoints(image.Exports, entryPoints, failures);

        return $"Mach-O CPU type 0x{image.CpuType:X8}{(cpuTypes.Count > 1 ? $" (universal, {cpuTypes.Count} slices)" : string.Empty)}; "
            + $"loads {string.Join(", ", image.Dependencies)}; minimum macOS {minimum?.ToString() ?? "unknown"}; "
            + $"{image.Exports.Count} exports ({image.ExportSource})";
    }

    private static void VerifyEntryPoints(SortedSet<string> exports, IReadOnlyCollection<string>? entryPoints, List<string> failures)
    {
        if (entryPoints == null)
        {
            return;
        }

        string[] missing = [.. entryPoints.Where(entryPoint => !exports.Contains(entryPoint))];

        if (missing.Length != 0)
        {
            failures.Add($"does not export {missing.Length} of {entryPoints.Count} P/Invoke entry points: {string.Join(", ", missing)}");
        }
    }
}
