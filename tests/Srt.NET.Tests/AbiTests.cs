// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Srt.Net.Interop;
using Xunit;

namespace Srt.Net.Tests;

/// <summary>
/// Checks the managed declarations against <c>native/abi/managed_abi.c</c>, which the native build compiles against
/// the bundled libsrt headers. Together they tie every marshaled layout and native constant to libsrt.
/// </summary>
public sealed class AbiTests
{
    private static readonly Assembly Library = typeof(SrtSocket).Assembly;

    private static readonly Type[] Structures = [typeof(NativeStatistics), typeof(NativeMessageControl), typeof(NativePollEvent)];

    // Every public enumeration mirrors libsrt values, so a new one must be added to the contract.
    private static readonly Type[] Enumerations = [.. Library.GetTypes().Where(type => type.IsEnum && type.IsPublic && type.Namespace == "Srt.Net")];

    // Managed values with no libsrt constant (Off, None, Closed, Default), aliases of listed members (Accept, Connect),
    // and srt.h constants declared as static const variables, which C cannot use in a constant expression.
    private static readonly HashSet<string> ManagedOnly =
    [
        "SrtLogLevel.Off", "SrtLogFormat.Default", "SrtPollEvents.None", "SrtPollEvents.Accept", "SrtPollEvents.Connect",
        "SrtPollEvents.Closed", "NativeConstants.SRT_ERROR", "NativeConstants.SRT_INVALID_SOCK",
    ];

    // The managed type that marshals each native field type in the contract (64-bit targets).
    private static readonly Dictionary<string, Type> FieldTypes = new(StringComparer.Ordinal)
    {
        ["int"] = typeof(int),
        ["int32_t"] = typeof(int),
        ["SRTSOCKET"] = typeof(int),
        ["int64_t"] = typeof(long),
        ["uint64_t"] = typeof(ulong),
        ["double"] = typeof(double),
        ["size_t"] = typeof(UIntPtr),
    };

    private static readonly Lazy<Contract> Entries = new(Contract.Load);

    [Fact]
    public void StructureSizesMatchTheNativeContract()
    {
        foreach ((string managed, string native, long size) in Entries.Value.Sizes)
        {
            Assert.True(size == Marshal.SizeOf(Resolve(managed)), $"{managed} must be {size} bytes, like {native}.");
        }
    }

    [Fact]
    public void FieldOffsetsMatchTheNativeContract()
    {
        foreach ((string managed, string native, _, long offset) in Entries.Value.Fields)
        {
            (Type type, string field) = Split(managed);
            Assert.True(new IntPtr(offset) == Marshal.OffsetOf(type, field), $"{managed} must be at offset {offset}, like {native}.");
        }
    }

    [Fact]
    public void FieldTypesMatchTheNativeContract()
    {
        foreach ((string managed, string native, string nativeType, _) in Entries.Value.Fields)
        {
            (Type type, string name) = Split(managed);
            Type expected = nativeType.EndsWith("*", StringComparison.Ordinal) ? typeof(IntPtr) : FieldTypes[nativeType];
            Type actual = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.FieldType;
            Assert.True(expected == actual, $"{managed} is {actual.Name}, but {native} is {nativeType}, which marshals as {expected.Name}.");
        }
    }

    [Fact]
    public void ConstantsMatchTheNativeContract()
    {
        foreach ((string managed, string native, long value) in Entries.Value.Values)
        {
            Assert.True(unchecked((uint)value) == unchecked((uint)ManagedValue(managed)), $"{managed} must be {value}, like {native}.");
        }
    }

    [Fact]
    public void TheNativeContractListsEveryManagedDeclaration()
    {
        HashSet<string> listed = [.. Entries.Value.Sizes.Select(entry => entry.Managed)];
        listed.UnionWith(Entries.Value.Fields.Select(entry => entry.Managed));
        listed.UnionWith(Entries.Value.Values.Select(entry => entry.Managed));

        IEnumerable<string> declared = Structures.Select(type => type.Name)
            .Concat(Structures.SelectMany(type => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(field => $"{type.Name}.{field.Name}")))
            .Concat(typeof(SrtOptions).GetProperties(BindingFlags.Static | BindingFlags.Public).Select(property => $"{nameof(SrtOptions)}.{property.Name}"))
            .Concat(typeof(NativeConstants).GetFields(BindingFlags.Static | BindingFlags.NonPublic).Where(field => field.IsLiteral).Select(field => $"{nameof(NativeConstants)}.{field.Name}"))
            .Concat(Enumerations.SelectMany(type => Enum.GetNames(type).Select(name => $"{type.Name}.{name}")));

        string[] missing = [.. declared.Where(name => !listed.Contains(name) && !ManagedOnly.Contains(name)).OrderBy(name => name, StringComparer.Ordinal)];
        Assert.True(missing.Length == 0, $"native/abi/managed_abi.c does not check: {string.Join(", ", missing)}");
    }

    [Fact]
    public void TheVerifiedStatisticsLayoutCoversTheBundledRelease()
    {
        // native/cmake/UpdateLibsrt.cmake raises the verified version together with native/versions.json, which the
        // native build records in srt.build-info.json; the native ABI check makes raising it safe.
        Version bundled = TestSockets.BundledNativeVersion();
        Assert.Equal(bundled, NativeAbi.VerifiedVersion);
        Assert.True(NativeAbi.HasVerifiedStatisticsLayout(bundled));
        Assert.True(NativeAbi.HasVerifiedStatisticsLayout(new Version(1, 5, 0)));
        Assert.False(NativeAbi.HasVerifiedStatisticsLayout(new Version(1, 4, 4)));
        Assert.False(NativeAbi.HasVerifiedStatisticsLayout(new Version(bundled.Major, bundled.Minor, bundled.Build + 1)));
        Assert.False(NativeAbi.HasVerifiedStatisticsLayout(new Version(bundled.Major, bundled.Minor + 1, 0)));
    }

    private static Type Resolve(string name)
    {
        return Library.GetTypes().SingleOrDefault(type => type.Name == name && type.Namespace is "Srt.Net" or "Srt.Net.Interop")
            ?? throw new InvalidOperationException($"No Srt.NET type is named {name}.");
    }

    private static (Type Type, string Member) Split(string managed)
    {
        int dot = managed.IndexOf('.');
        return (Resolve(managed.Substring(0, dot)), managed.Substring(dot + 1));
    }

    private static long ManagedValue(string managed)
    {
        (Type type, string member) = Split(managed);

        if (type.IsEnum)
        {
            return Convert.ToInt64(Enum.Parse(type, member), CultureInfo.InvariantCulture);
        }

        if (type == typeof(SrtOptions))
        {
            object option = type.GetProperty(member, BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
            return (int)option.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(option)!;
        }

        FieldInfo field = type.GetField(member, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{managed} is not a constant.");

        return Convert.ToInt64(field.GetRawConstantValue(), CultureInfo.InvariantCulture);
    }

    private sealed class Contract
    {
        private static readonly Regex Entry = new(@"^SRTNET_(SIZE|FIELD|VALUE)\(([^)]*)\);", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        internal List<(string Managed, string Native, long Size)> Sizes { get; } = [];

        internal List<(string Managed, string Native, string NativeType, long Offset)> Fields { get; } = [];

        internal List<(string Managed, string Native, long Value)> Values { get; } = [];

        internal static Contract Load()
        {
            // Copied next to the tests from native/abi/managed_abi.c (see Srt.NET.Tests.csproj).
            string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "managed_abi.c"));
            Contract contract = new();

            foreach (Match match in Entry.Matches(text))
            {
                string[] arguments = [.. match.Groups[2].Value.Split(',').Select(argument => argument.Trim())];

                switch (match.Groups[1].Value)
                {
                    case "SIZE":
                        contract.Sizes.Add((arguments[0], arguments[1], Number(arguments[2])));
                        break;
                    case "FIELD":
                        contract.Fields.Add((arguments[0], $"{arguments[1]}.{arguments[2]}", arguments[3], Number(arguments[4])));
                        break;
                    default:
                        contract.Values.Add((arguments[0], arguments[1], Number(arguments[2])));
                        break;
                }
            }

            Assert.True(contract.Sizes.Count > 0 && contract.Fields.Count > 0 && contract.Values.Count > 0, "managed_abi.c has no entries.");
            return contract;
        }

        private static long Number(string text)
        {
            return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? long.Parse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }
    }
}
