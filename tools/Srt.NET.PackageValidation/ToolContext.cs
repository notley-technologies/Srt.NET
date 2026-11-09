// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Srt.Net.PackageValidation;

internal static class ToolContext
{
    /// <summary>The provenance manifest next to each native library (runtimes/&lt;rid&gt;/native/).</summary>
    internal const string BuildInfoFile = "srt.build-info.json";

    /// <summary>The manifest's former name, which collided with application files of the same name on publish (NETSDK1152).</summary>
    internal const string LegacyBuildInfoFile = "build-info.json";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    /// <summary>The native library file for each supported runtime identifier.</summary>
    internal static readonly Dictionary<string, string> NativeFiles = new(StringComparer.Ordinal)
    {
        ["win-x64"] = "srt.dll",
        ["linux-x64"] = "libsrt.so",
        ["linux-arm64"] = "libsrt.so",
        ["linux-musl-x64"] = "libsrt.so",
        ["linux-musl-arm64"] = "libsrt.so",
        ["osx-x64"] = "libsrt.dylib",
        ["osx-arm64"] = "libsrt.dylib"
    };

    internal static readonly string[] NoticeFiles =
    [
        "Srt.NET-LICENSE.txt",
        "libsrt-MPL-2.0.txt",
        "openssl-LICENSE.txt",
        "libsrt-COPYRIGHT.txt",
        "openssl-COPYRIGHT.txt"
    ];

    internal static string Root { get; } = FindRoot();

    internal static Dictionary<string, NativeDependency> ReadDependencies()
    {
        string path = Path.Combine(Root, "native", "versions.json");

        return JsonSerializer.Deserialize<Dictionary<string, NativeDependency>>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The native dependency manifest is empty.");
    }

    internal static string Execute(string executable, IEnumerable<string> arguments, bool captureOutput = false, string? workingDirectory = null)
    {
        using Process process = Start(executable, arguments, workingDirectory);

        Task<string>? captured = captureOutput ? process.StandardOutput.ReadToEndAsync() : null;
        Task outputTask = captured != null ? captured : ForwardAsync(process.StandardOutput, Console.Out);
        Task errorTask = ForwardAsync(process.StandardError, Console.Error);
        process.WaitForExit();
        Task.WhenAll(outputTask, errorTask).GetAwaiter().GetResult();
        string output = captured?.GetAwaiter().GetResult() ?? string.Empty;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{executable} exited with code {process.ExitCode}.");
        }

        return output.Trim();
    }

    /// <summary>Runs a command that must fail and returns its standard output and error.</summary>
    internal static string ExecuteExpectingFailure(string executable, IEnumerable<string> arguments)
    {
        using Process process = Start(executable, arguments, null);
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string text = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();

        if (process.ExitCode == 0)
        {
            throw new InvalidOperationException($"{executable} succeeded, but it was expected to fail:{Environment.NewLine}{text}");
        }

        return text;
    }

    private static Process Start(string executable, IEnumerable<string> arguments, string? workingDirectory)
    {
        ProcessStartInfo startInfo = new(executable)
        {
            WorkingDirectory = workingDirectory ?? Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Native AOT links through vcvarsall.bat, whose VsDevCmd.bat changes to the Visual Studio Installer folder and
        // starts vswhere.exe by its bare name. With NoDefaultCurrentDirectoryInExePath set, cmd.exe does not search the
        // current directory, and the error text ends up in the linker path, so put that folder on PATH instead.
        if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("NoDefaultCurrentDirectoryInExePath") != null)
        {
            string installer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer");

            if (Directory.Exists(installer))
            {
                startInfo.Environment["PATH"] = installer + Path.PathSeparator + startInfo.Environment["PATH"];
            }
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException($"Unable to start {executable}.");
    }

    private static async Task ForwardAsync(TextReader input, TextWriter output)
    {
        char[] buffer = new char[4096];
        int count;

        while ((count = await input.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) != 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, count)).ConfigureAwait(false);
        }
    }

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(Directory.GetCurrentDirectory());

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "native", "versions.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Run the package tests from a Srt.NET checkout.");
    }
}
