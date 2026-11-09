// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Srt.Net.PackageValidation;

internal static class PackageConsumer
{
    internal static void Run(string packagePath, string rid, bool nativeAot)
    {
        PackageVerifier.Verify(packagePath, [rid]);

        string absolutePackage = Path.GetFullPath(packagePath);
        using ZipArchive archive = ZipFile.OpenRead(absolutePackage);
        ZipArchiveEntry nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using Stream manifestStream = nuspec.Open();
        XDocument manifest = XDocument.Load(manifestStream);
        string version = manifest.Descendants().Single(element => element.Name.LocalName == "version").Value;
        PackageVersion.Validate(version);

        string packageId = manifest.Descendants().Single(element => element.Name.LocalName == "id").Value;

        if (!string.Equals(packageId, "Srt.NET", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The package under test must have the Srt.NET package ID.");
        }

        byte[] expectedHash = HashFile(absolutePackage);
        string digest = Convert.ToHexStringLower(expectedHash)[..16];

        // Fresh outputs and a private cache prevent earlier runs from supplying missing package assets.
        string output = Path.Combine(ToolContext.Root, "artifacts", "package-tests", rid, digest, Guid.NewGuid().ToString("N"));
        string feed = Path.Combine(output, "feed");
        string cache = Path.Combine(output, "packages");
        Directory.CreateDirectory(feed);
        File.Copy(absolutePackage, Path.Combine(feed, $"Srt.NET.{version}.nupkg"));

        string templates = Path.Combine(ToolContext.Root, "tests", "PackageConsumers");
        string direct = Path.Combine(output, "direct");
        string transitive = Path.Combine(output, "transitive");
        CopyTemplates(templates, direct);
        CopyTemplates(templates, transitive);

        string configPath = Path.Combine(output, "NuGet.Config");
        WriteNuGetConfig(configPath, feed);

        string[] commonProperties = [$"-p:SrtPackageVersion={version}", $"-p:RestorePackagesPath={cache}"];
        string wrapper = Path.Combine(transitive, "Wrapper", "Srt.NET.PackageWrapper.csproj");
        ToolContext.Execute("dotnet", ["restore", wrapper, "--configfile", configPath, .. commonProperties]);
        VerifyRestoredPackage(cache, expectedHash);
        ToolContext.Execute("dotnet", ["pack", wrapper, "-c", "Release", "-o", feed, "--no-restore", .. commonProperties]);

        RunConsumer(direct, false, rid, configPath, cache, expectedHash, commonProperties);
        RunConsumer(transitive, true, rid, configPath, cache, expectedHash, commonProperties);

        if (OperatingSystem.IsWindows())
        {
            RunFrameworkArchitectureChecks(direct, configPath, cache, expectedHash, commonProperties);
            RunCSharp73Consumer(direct, configPath, cache, expectedHash, commonProperties);
        }

        if (nativeAot)
        {
            RunNativeAotConsumer(direct, rid, configPath, cache, expectedHash, commonProperties);
        }
    }

    private static void WriteNuGetConfig(string path, string feed)
    {
        XDocument configuration = new(
            new XElement("configuration",
                new XElement("packageSources",
                    new XElement("clear"),
                    new XElement("add", new XAttribute("key", "local"), new XAttribute("value", feed)),
                    new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json"))),
                new XElement("packageSourceMapping",
                    new XElement("clear"),
                    new XElement("packageSource", new XAttribute("key", "local"),
                        new XElement("package", new XAttribute("pattern", "Srt.NET")),
                        new XElement("package", new XAttribute("pattern", "Srt.NET.PackageWrapper"))),
                    new XElement("packageSource", new XAttribute("key", "nuget.org"),
                        new XElement("package", new XAttribute("pattern", "*"))))));

        configuration.Save(path);
    }

    private static void RunConsumer(string consumer, bool transitive, string rid, string configPath,
        string cache, byte[] expectedHash, string[] commonProperties)
    {
        string project = Path.Combine(consumer, "Srt.NET.PackageConsumer.csproj");
        string architecture = rid.EndsWith("arm64", StringComparison.Ordinal) ? "ARM64" : "x64";
        string[] properties =
        [
            .. commonProperties,
            $"-p:PlatformTarget={architecture}",
            $"-p:RuntimeIdentifier={rid}",
            $"-p:UseTransitivePackage={transitive.ToString().ToLowerInvariant()}",
            "-p:SelfContained=false"
        ];

        ToolContext.Execute("dotnet", ["restore", project, "--configfile", configPath, .. properties]);
        VerifyRestoredPackage(cache, expectedHash);

        string[] frameworks = OperatingSystem.IsWindows() ? ["net10.0", "net48"] : ["net10.0"];
        string[] commands = ["build", "publish"];

        foreach (string framework in frameworks)
        {
            foreach (string command in commands)
            {
                string directory = Path.Combine(consumer, "outputs", framework, command);
                Console.WriteLine($"Testing {(transitive ? "transitive" : "direct")} package reference: {framework}, {command} output.");
                ToolContext.Execute("dotnet",
                [
                    command, project, "-c", "Release", "-f", framework, "--no-restore", "-o", directory,
                    .. properties
                ]);

                RunOutput(directory, framework, rid);
            }
        }
    }

    private static void RunFrameworkArchitectureChecks(string consumer, string configPath, string cache, byte[] expectedHash,
        string[] commonProperties)
    {
        // The package's MSBuild target rejects .NET Framework builds that cannot run as x64. It must check the platform
        // the SDK finally compiles for: an SDK-style executable without a platform starts as x86 and becomes AnyCPU,
        // which builds and runs as x64. An explicit x86 build must fail with the package's message.
        string project = Path.Combine(consumer, "Srt.NET.PackageConsumer.csproj");
        string[] properties = [.. commonProperties, "-p:UseTransitivePackage=false"];
        ToolContext.Execute("dotnet", ["restore", project, "--configfile", configPath, .. properties]);
        VerifyRestoredPackage(cache, expectedHash);

        string directory = Path.Combine(consumer, "outputs", "net48", "default-platform");
        Console.WriteLine("Testing direct package reference: net48 without a platform target, build output.");
        ToolContext.Execute("dotnet", ["build", project, "-c", "Release", "-f", "net48", "--no-restore", "-o", directory, .. properties]);
        RunOutput(directory, "net48", "win-x64");

        Console.WriteLine("Testing direct package reference: net48 x86 build is rejected.");
        string output = ToolContext.ExecuteExpectingFailure("dotnet",
            ["build", project, "-c", "Release", "-f", "net48", "--no-restore", "-p:PlatformTarget=x86", .. properties]);

        if (!output.Contains("Srt.NET includes only an x64 Windows native library", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The x86 .NET Framework build failed without the Srt.NET architecture error:{Environment.NewLine}{output}");
        }
    }

    private static void RunCSharp73Consumer(string consumer, string configPath, string cache, byte[] expectedHash,
        string[] commonProperties)
    {
        // .NET Framework projects compile as C# 7.3 by default, which cannot set init-only properties or read nullable
        // annotations. This consumer keeps that language version, unlike the other templates, so the package's public
        // surface (such as the SrtMessageOptions constructor) stays usable from it.
        string project = Path.Combine(consumer, "CSharp73", "Srt.NET.CSharp73Consumer.csproj");
        string directory = Path.Combine(consumer, "outputs", "net48", "csharp73");
        string[] properties = [.. commonProperties, "-p:PlatformTarget=x64"];

        Console.WriteLine("Testing direct package reference: net48 with C# 7.3, build output.");
        ToolContext.Execute("dotnet", ["restore", project, "--configfile", configPath, .. properties]);
        VerifyRestoredPackage(cache, expectedHash);
        ToolContext.Execute("dotnet", ["build", project, "-c", "Release", "--no-restore", "-o", directory, .. properties]);

        if (!File.Exists(Path.Combine(directory, ToolContext.NativeFiles["win-x64"])))
        {
            throw new FileNotFoundException($"Native library missing from {directory}.");
        }

        ToolContext.Execute(Path.Combine(directory, "Srt.NET.CSharp73Consumer.exe"), [], workingDirectory: directory);
    }

    private static void RunNativeAotConsumer(string consumer, string rid, string configPath, string cache,
        byte[] expectedHash, string[] commonProperties)
    {
        // Native AOT is always self-contained, so this run omits SelfContained=false and PlatformTarget. It needs its own
        // restore, which adds the RID-specific ILCompiler package, and the host's native toolchain (MSVC, clang, or Xcode).
        string project = Path.Combine(consumer, "Srt.NET.PackageConsumer.csproj");
        string directory = Path.Combine(consumer, "outputs", "net10.0", "aot");
        string[] properties = [.. commonProperties, $"-p:RuntimeIdentifier={rid}", "-p:SrtAotTest=true"];

        Console.WriteLine("Testing direct package reference: net10.0, Native AOT publish output.");
        ToolContext.Execute("dotnet", ["restore", project, "--configfile", configPath, .. properties]);
        VerifyRestoredPackage(cache, expectedHash);
        ToolContext.Execute("dotnet", ["publish", project, "-c", "Release", "-f", "net10.0", "--no-restore", "-o", directory, .. properties]);

        if (!File.Exists(Path.Combine(directory, ToolContext.NativeFiles[rid])))
        {
            throw new FileNotFoundException($"Native library missing from {directory}.");
        }

        if (File.Exists(Path.Combine(directory, "Srt.NET.dll")))
        {
            throw new InvalidDataException($"The Native AOT publish output contains Srt.NET.dll, so it was not compiled ahead of time: {directory}");
        }

        string executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "Srt.NET.PackageConsumer.exe" : "Srt.NET.PackageConsumer");
        ToolContext.Execute(executable, [], workingDirectory: directory);
    }

    private static void RunOutput(string directory, string framework, string rid)
    {
        if (!File.Exists(Path.Combine(directory, ToolContext.NativeFiles[rid])))
        {
            throw new FileNotFoundException($"Native library missing from {directory}.");
        }

        if (framework == "net48")
        {
            ToolContext.Execute(Path.Combine(directory, "Srt.NET.PackageConsumer.exe"), [], workingDirectory: directory);
        }
        else
        {
            ToolContext.Execute("dotnet", [Path.Combine(directory, "Srt.NET.PackageConsumer.dll")], workingDirectory: directory);
        }
    }

    private static void VerifyRestoredPackage(string cache, byte[] expectedHash)
    {
        string[] restored = Directory.GetFiles(Path.Combine(cache, "srt.net"), "*.nupkg", SearchOption.AllDirectories);

        if (restored.Length != 1 || !HashFile(restored[0]).AsSpan().SequenceEqual(expectedHash))
        {
            throw new InvalidDataException("The restored Srt.NET package differs from the package under test.");
        }
    }

    private static byte[] HashFile(string path)
    {
        using FileStream input = File.OpenRead(path);

        return SHA256.HashData(input);
    }

    private static void CopyTemplates(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.GetFiles(source))
        {
            if (Path.GetExtension(file) is ".cs" or ".csproj" or ".props")
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }
        }

        foreach (string directory in Directory.GetDirectories(source))
        {
            string name = Path.GetFileName(directory);

            if (name is not ("bin" or "obj"))
            {
                CopyTemplates(directory, Path.Combine(destination, name));
            }
        }
    }
}
