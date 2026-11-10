// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Srt.Net.Interop;
using Xunit;

namespace Srt.Net.Tests;

/// <summary>
/// The staged libsrt must export every function that Srt.NET imports from it (today all are in
/// <see cref="NativeMethods"/>). The native build gates check only srt_startup, so a missing export would otherwise surface
/// as an EntryPointNotFoundException at its first call.
/// </summary>
public sealed class NativeExportTests
{
    [Fact]
    public void TheNativeLibraryExportsEveryDeclaredEntryPoint()
    {
        string libraryName = typeof(NativeMethods).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(method => method.GetCustomAttribute<DllImportAttribute>()?.Value)
            .First(name => name != null)!;
        MethodInfo[] imports =
        [
            .. typeof(NativeMethods).Assembly.GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(method => method.GetCustomAttribute<DllImportAttribute>()?.Value == libraryName)
        ];
        Assert.NotEmpty(imports);

#if NET
        // The same probing as the P/Invoke calls: the library name and Srt.NET's DefaultDllImportSearchPaths. The handle
        // is not freed, because the other tests keep using the library.
        IntPtr library = NativeLibrary.Load(libraryName, typeof(NativeMethods).Assembly, null);
        string[] missing =
        [
            .. imports.Select(method => method.GetCustomAttribute<DllImportAttribute>()!.EntryPoint ?? method.Name)
                .Where(entryPoint => !NativeLibrary.TryGetExport(library, entryPoint, out _))
                .OrderBy(entryPoint => entryPoint, StringComparer.Ordinal)
        ];
        Assert.True(missing.Length == 0, $"The native library does not export: {string.Join(", ", missing)}");
#else
        // .NET Framework has no NativeLibrary. Prelink binds a declaration and throws EntryPointNotFoundException; PrelinkAll
        // would skip these declarations, because it binds only public methods.
        foreach (MethodInfo method in imports)
        {
            Marshal.Prelink(method);
        }
#endif
    }
}
