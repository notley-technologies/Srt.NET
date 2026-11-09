// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Srt.Net.PackageValidation;

/// <summary>Reads the native entry points that an assembly's P/Invoke declarations bind to (the ImplMap table).</summary>
internal static class PInvokeEntryPoints
{
    /// <summary>The DllImport library name that Srt.NET uses for libsrt.</summary>
    internal const string LibsrtModule = "srt";

    internal static SortedSet<string> Read(byte[] assembly, string module)
    {
        try
        {
            using PEReader reader = new(ImmutableArray.Create(assembly));
            MetadataReader metadata = reader.GetMetadataReader();
            SortedSet<string> entryPoints = new(StringComparer.Ordinal);

            foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions)
            {
                MethodDefinition method = metadata.GetMethodDefinition(handle);

                if ((method.Attributes & MethodAttributes.PinvokeImpl) == 0)
                {
                    continue;
                }

                MethodImport import = method.GetImport();
                ModuleReference reference = metadata.GetModuleReference(import.Module);

                if (metadata.StringComparer.Equals(reference.Name, module))
                {
                    // Compilers always record the import name; it equals the method name unless EntryPoint is set.
                    entryPoints.Add(metadata.GetString(import.Name.IsNil ? method.Name : import.Name));
                }
            }

            return entryPoints;
        }
        catch (BadImageFormatException exception)
        {
            throw new InvalidDataException("The managed assembly is malformed.", exception);
        }
    }

    internal static SortedSet<string> ReadFile(string path)
    {
        SortedSet<string> entryPoints = Read(File.ReadAllBytes(path), LibsrtModule);

        if (entryPoints.Count == 0)
        {
            throw new InvalidDataException($"{path} declares no P/Invoke entry points in '{LibsrtModule}'.");
        }

        return entryPoints;
    }
}
