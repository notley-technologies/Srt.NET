// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Srt.Net.PackageValidation;

internal static partial class PackageVerifier
{
    /// <summary>The nuget.org readme (src/Srt.NET/PACKAGE.md), packed at the package root.</summary>
    private const string PackageReadme = "PACKAGE.md";

    /// <summary>Srt.NET's own license, as the nuspec license expression.</summary>
    private const string PackageLicense = "MPL-2.0";

    /// <summary>The SPDX manifest that Microsoft.Sbom.Targets embeds when the package is packed with GenerateSBOM=true.</summary>
    private const string SbomManifest = "_manifest/spdx_2.2/manifest.spdx.json";

    /// <summary>Image hosts that nuget.org shows in package readmes (a conservative subset of its allowed domains).</summary>
    private static readonly HashSet<string> AllowedImageHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "img.shields.io",
        "raw.githubusercontent.com"
    };

    private static readonly string[] ManagedAssemblies =
    [
        "lib/netstandard2.0/Srt.NET.dll",
        "lib/net8.0/Srt.NET.dll"
    ];

    internal static void Verify(string packagePath, IEnumerable<string> runtimes)
    {
        string[] selectedRuntimes = [.. runtimes];

        foreach (string rid in selectedRuntimes)
        {
            if (!ToolContext.NativeFiles.ContainsKey(rid))
            {
                throw new ArgumentException($"Unsupported runtime: {rid}. Supported: {string.Join(", ", ToolContext.NativeFiles.Keys)}");
            }
        }

        Dictionary<string, NativeDependency> dependencies = ToolContext.ReadDependencies();
        using ZipArchive package = ZipFile.OpenRead(packagePath);

        string[] requiredFiles =
        [
            .. ManagedAssemblies,
            PackageReadme,
            "THIRD_PARTY_NOTICES.md",
            "buildTransitive/Srt.NET.targets",
            .. ToolContext.NoticeFiles.Select(name => $"licenses/{name}")
        ];

        foreach (string requiredFile in requiredFiles)
        {
            _ = GetEntry(package, requiredFile);
        }

        VerifyReadme(package);
        VerifyRuntimeLayout(package, selectedRuntimes);
        VerifySbomManifest(package);

        foreach (string rid in selectedRuntimes)
        {
            string prefix = $"runtimes/{rid}/native/";
            byte[] binary = ReadEntry(GetEntry(package, prefix + ToolContext.NativeFiles[rid]));
            using Stream manifest = GetEntry(package, prefix + ToolContext.BuildInfoFile).Open();
            NativeBuildInfo info = JsonSerializer.Deserialize<NativeBuildInfo>(manifest, ToolContext.JsonOptions)
                ?? throw new InvalidDataException($"Missing native build manifest for {rid}.");

            JsonNode? expectedDependencies = JsonSerializer.SerializeToNode(dependencies, ToolContext.JsonOptions);
            JsonNode? actualDependencies = JsonSerializer.SerializeToNode(info.Dependencies, ToolContext.JsonOptions);

            if (info.Rid != rid || info.Encryption != "openssl-evp (static)" || !JsonNode.DeepEquals(expectedDependencies, actualDependencies))
            {
                throw new InvalidDataException($"Incorrect native build provenance for {rid}.");
            }

            if (Convert.ToHexStringLower(SHA256.HashData(binary)) != info.Sha256)
            {
                throw new InvalidDataException($"Native binary hash does not match its manifest: {rid}.");
            }

            string? libc = rid.StartsWith("linux-musl-", StringComparison.Ordinal) ? "musl" : rid.StartsWith("linux-", StringComparison.Ordinal) ? "glibc" : null;

            if (libc != null && info.Libc != null && !info.Libc.StartsWith(libc, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The {rid} manifest records C library '{info.Libc}', not {libc}.");
            }

            string summary = NativeLibraryVerifier.Verify(rid, binary);
            Console.WriteLine($"  {rid}: {summary}; built with {info.Compiler ?? "an unrecorded compiler"}");
        }

        Console.WriteLine($"Verified {Path.GetFileName(packagePath)}: {string.Join(", ", selectedRuntimes)}");
    }

    private static void VerifyRuntimeLayout(ZipArchive package, string[] selectedRuntimes)
    {
        // Everything in runtimes/<rid>/native/ is copied into consumers' build and publish output.
        List<string> failures = [];

        foreach (ZipArchiveEntry entry in package.Entries)
        {
            string[] parts = entry.FullName.Split('/');

            if (parts[0] != "runtimes" || entry.Name.Length == 0)
            {
                continue;
            }

            if (parts.Length != 4 || parts[2] != "native" || !ToolContext.NativeFiles.TryGetValue(parts[1], out string? library))
            {
                failures.Add($"unexpected runtime asset {entry.FullName}");
            }
            else if (selectedRuntimes.Contains(parts[1], StringComparer.Ordinal) && parts[3] != library && parts[3] != ToolContext.BuildInfoFile)
            {
                failures.Add($"unexpected native file {entry.FullName}");
            }
        }

        if (failures.Count != 0)
        {
            string separator = Environment.NewLine + "  - ";

            throw new InvalidDataException($"The package layout is invalid:{separator}{string.Join(separator, failures)}");
        }
    }

    private static void VerifyReadme(ZipArchive package)
    {
        XDocument nuspec = ReadNuspec(package);
        string? readme = nuspec.Descendants().SingleOrDefault(element => element.Name.LocalName == "readme")?.Value;

        if (readme != PackageReadme)
        {
            throw new InvalidDataException($"The nuspec readme is '{readme}', not {PackageReadme}.");
        }

        XElement? license = nuspec.Descendants().SingleOrDefault(element => element.Name.LocalName == "license");

        if (license?.Attribute("type")?.Value != "expression" || license.Value != PackageLicense)
        {
            throw new InvalidDataException($"The nuspec license is '{license?.Value}', not the {PackageLicense} expression.");
        }

        // nuget.org removes relative links from package readmes, so every link target must be absolute or a fragment.
        using StreamReader reader = new(GetEntry(package, PackageReadme).Open());
        string text = reader.ReadToEnd();
        string[] relativeTargets =
        [
            .. MarkdownLinkTarget().Matches(text)
                .Concat(MarkdownReferenceTarget().Matches(text))
                .Concat(HtmlLinkTarget().Matches(text))
                .Select(match => match.Groups["target"].Value)
                .Where(target => !target.StartsWith("https://", StringComparison.Ordinal) && !target.StartsWith('#'))
        ];

        if (relativeTargets.Length != 0)
        {
            throw new InvalidDataException($"{PackageReadme} has links that nuget.org cannot render: {string.Join(", ", relativeTargets)}");
        }

        // nuget.org shows images only from its allowed domains. These hosts are a conservative subset of that list.
        string[] blockedImages =
        [
            .. MarkdownImageTarget().Matches(text)
                .Concat(HtmlImageTarget().Matches(text))
                .Select(match => match.Groups["target"].Value)
                .Where(target => !Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || !AllowedImageHosts.Contains(uri.Host))
        ];

        if (blockedImages.Length != 0)
        {
            throw new InvalidDataException($"{PackageReadme} has images that nuget.org does not show: {string.Join(", ", blockedImages)}");
        }
    }

    private static void VerifySbomManifest(ZipArchive package)
    {
        // Packages packed without GenerateSBOM=true have no manifest; the CI package job requires one separately.
        ZipArchiveEntry? entry = package.GetEntry(SbomManifest);

        if (entry == null)
        {
            return;
        }

        using Stream stream = entry.Open();
        JsonNode manifest = JsonNode.Parse(stream) ?? throw new InvalidDataException($"{SbomManifest} is empty.");
        JsonArray files = manifest["files"]?.AsArray() ?? throw new InvalidDataException($"{SbomManifest} lists no files.");
        List<string> missing = [];

        foreach (JsonNode? file in files)
        {
            string name = file?["fileName"]?.GetValue<string>() ?? string.Empty;

            if (!name.StartsWith("./", StringComparison.Ordinal) || name.Contains("/../", StringComparison.Ordinal) || package.GetEntry(name[2..]) == null)
            {
                missing.Add(name);
            }
        }

        if (missing.Count != 0)
        {
            throw new InvalidDataException($"{SbomManifest} lists files that are not in the package: {string.Join(", ", missing)}");
        }

        Console.WriteLine($"  SBOM manifest: {files.Count} files, all present in the package");
    }

    private static XDocument ReadNuspec(ZipArchive package)
    {
        ZipArchiveEntry entry = package.Entries.Single(candidate => candidate.FullName.EndsWith(".nuspec", StringComparison.Ordinal) && !candidate.FullName.Contains('/', StringComparison.Ordinal));
        using Stream stream = entry.Open();

        return XDocument.Load(stream);
    }

    private static ZipArchiveEntry GetEntry(ZipArchive package, string path)
    {
        return package.GetEntry(path) ?? throw new InvalidDataException($"Missing package file: {path}");
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        // Deflate streams cannot seek, and the PE reader needs random access.
        byte[] contents = new byte[checked((int)entry.Length)];
        using Stream input = entry.Open();
        input.ReadExactly(contents);

        return contents;
    }

    /// <summary>The target of an inline Markdown link or image, with an optional title: <c>[text](target "title")</c>.</summary>
    [GeneratedRegex(@"\]\(\s*<?(?<target>[^)\s>]+)>?(?:\s+(?:""[^""]*""|'[^']*'|\([^)]*\)))?\s*\)")]
    private static partial Regex MarkdownLinkTarget();

    /// <summary>The target of a Markdown reference definition: <c>[label]: target</c>.</summary>
    [GeneratedRegex(@"^ {0,3}\[[^\]]+\]:\s*<?(?<target>[^\s>]+)", RegexOptions.Multiline)]
    private static partial Regex MarkdownReferenceTarget();

    /// <summary>The target of an HTML link or image attribute: <c>href="target"</c> or <c>src="target"</c>.</summary>
    [GeneratedRegex(@"\b(?:href|src)\s*=\s*[""'](?<target>[^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlLinkTarget();

    /// <summary>The source of an inline Markdown image: <c>![alt](target)</c>.</summary>
    [GeneratedRegex(@"!\[[^\]]*\]\(\s*<?(?<target>[^)\s>]+)")]
    private static partial Regex MarkdownImageTarget();

    /// <summary>The source of an HTML image: <c>&lt;img src="target"&gt;</c>.</summary>
    [GeneratedRegex(@"<img\b[^>]*\bsrc\s*=\s*[""'](?<target>[^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlImageTarget();
}
