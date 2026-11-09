// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System.Buffers.Binary;
using System.Text;

namespace Srt.Net.PackageValidation;

/// <summary>
/// The dynamic-linking metadata of a 64-bit little-endian Mach-O image (x86_64 or arm64), read from its load commands:
/// the libraries it loads, its run-path search entries, its minimum OS version and its exported symbols. Thin images
/// and universal (fat) files are supported; a universal file is read through the slice for one CPU type.
/// </summary>
internal sealed class MachOImage
{
    internal const uint CpuTypeX64 = 0x01000007;
    internal const uint CpuTypeArm64 = 0x0100000C;
    internal const uint FileTypeDylib = 6;
    internal const uint PlatformMacOS = 1;

    private const uint Magic64 = 0xFEEDFACF;
    private const uint FatMagic = 0xCAFEBABE;
    private const uint FatMagic64 = 0xCAFEBABF;
    private const int HeaderSize = 32;
    private const int FatArchSize = 20;
    private const int FatArch64Size = 32;
    private const int SymbolSize = 16;
    private const int MaximumTrieDepth = 128;

    private const uint CommandSymbolTable = 0x2;
    private const uint CommandLoadDylib = 0xC;
    private const uint CommandIdDylib = 0xD;
    private const uint CommandLazyLoadDylib = 0x20;
    private const uint CommandDyldInfo = 0x22;
    private const uint CommandVersionMinMacOS = 0x24;
    private const uint CommandBuildVersion = 0x32;
    private const uint CommandLoadWeakDylib = 0x80000018;
    private const uint CommandRunPath = 0x8000001C;
    private const uint CommandReexportDylib = 0x8000001F;
    private const uint CommandDyldInfoOnly = 0x80000022;
    private const uint CommandLoadUpwardDylib = 0x80000023;
    private const uint CommandDyldExportsTrie = 0x80000033;

    private MachOImage(uint cpuType, uint fileType)
    {
        CpuType = cpuType;
        FileType = fileType;
    }

    /// <summary>The cputype field: 0x01000007 for x86_64, 0x0100000C for arm64.</summary>
    internal uint CpuType
    {
        get;
    }

    /// <summary>The filetype field: 6 (MH_DYLIB) for a dynamic library.</summary>
    internal uint FileType
    {
        get;
    }

    /// <summary>The LC_ID_DYLIB install name, when present.</summary>
    internal string? InstallName
    {
        get; private set;
    }

    /// <summary>The libraries named by LC_LOAD_DYLIB, LC_LOAD_WEAK_DYLIB, LC_REEXPORT_DYLIB, LC_LAZY_LOAD_DYLIB and LC_LOAD_UPWARD_DYLIB, in file order.</summary>
    internal List<string> Dependencies { get; } = [];

    /// <summary>The LC_RPATH entries, in file order.</summary>
    internal List<string> RunPaths { get; } = [];

    /// <summary>The platforms named by LC_BUILD_VERSION (or 1, macOS, for LC_VERSION_MIN_MACOSX), with their minimum OS versions.</summary>
    internal List<KeyValuePair<uint, Version>> MinimumVersions { get; } = [];

    /// <summary>Exported symbols as C names: the Mach-O leading underscore is removed.</summary>
    internal SortedSet<string> Exports { get; } = new(StringComparer.Ordinal);

    /// <summary>Where the exports were read: the dyld export trie, or the symbol table when the image has no trie.</summary>
    internal string ExportSource { get; private set; } = "none";

    /// <summary>Whether the file starts with a 64-bit little-endian Mach-O header or a universal (fat) header.</summary>
    internal static bool IsMachO(ReadOnlySpan<byte> file)
    {
        return file.Length >= 8
            && (BinaryPrimitives.ReadUInt32LittleEndian(file) == Magic64
                || BinaryPrimitives.ReadUInt32BigEndian(file) is FatMagic or FatMagic64);
    }

    /// <summary>The CPU types in the file: one for a thin image, one per slice for a universal file.</summary>
    internal static List<uint> ReadCpuTypes(byte[] file)
    {
        try
        {
            return [.. ReadSlices(file).Select(slice => slice.CpuType)];
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException("The Mach-O file is truncated or malformed.", exception);
        }
    }

    /// <summary>Reads the thin image, or the slice of a universal file, for one CPU type.</summary>
    internal static MachOImage Read(byte[] file, uint cpuType)
    {
        try
        {
            foreach ((uint sliceCpuType, int offset, int length) in ReadSlices(file))
            {
                if (sliceCpuType == cpuType)
                {
                    return ReadCore(file.AsSpan(offset, length));
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException("The Mach-O image is truncated or malformed.", exception);
        }

        throw new InvalidDataException($"The Mach-O file has no image for CPU type 0x{cpuType:X8}.");
    }

    private static List<(uint CpuType, int Offset, int Length)> ReadSlices(byte[] file)
    {
        ReadOnlySpan<byte> data = file;

        if (!IsMachO(data))
        {
            throw new InvalidDataException("Not a 64-bit Mach-O image or universal file.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(data) == Magic64)
        {
            return [(ReadUInt32(data, 4), 0, data.Length)];
        }

        // fat_header and fat_arch(_64) are big-endian.
        bool fat64 = BinaryPrimitives.ReadUInt32BigEndian(data) == FatMagic64;
        uint count = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        int entrySize = fat64 ? FatArch64Size : FatArchSize;

        if (count == 0 || count > (uint)((data.Length - 8) / entrySize))
        {
            throw new InvalidDataException("The universal file has an invalid number of slices.");
        }

        List<(uint, int, int)> slices = [];

        for (int index = 0; index < (int)count; index++)
        {
            ReadOnlySpan<byte> entry = data.Slice(8 + (index * entrySize), entrySize);
            uint cpuType = BinaryPrimitives.ReadUInt32BigEndian(entry);
            ulong offset = fat64 ? BinaryPrimitives.ReadUInt64BigEndian(entry[8..]) : BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            ulong length = fat64 ? BinaryPrimitives.ReadUInt64BigEndian(entry[16..]) : BinaryPrimitives.ReadUInt32BigEndian(entry[12..]);

            if (offset > (ulong)data.Length || length > (ulong)data.Length - offset)
            {
                throw new InvalidDataException("A universal file slice lies outside the file.");
            }

            slices.Add((cpuType, (int)offset, (int)length));
        }

        return slices;
    }

    private static MachOImage ReadCore(ReadOnlySpan<byte> macho)
    {
        if (macho.Length < HeaderSize || ReadUInt32(macho, 0) != Magic64)
        {
            throw new InvalidDataException("The slice is not a 64-bit little-endian Mach-O image.");
        }

        MachOImage image = new(ReadUInt32(macho, 4), ReadUInt32(macho, 12));
        uint commandCount = ReadUInt32(macho, 16);
        ReadOnlySpan<byte> commands = macho.Slice(HeaderSize, checked((int)ReadUInt32(macho, 20)));
        ReadOnlySpan<byte> symbolTable = default;
        ReadOnlySpan<byte> exportTrie = default;
        int offset = 0;

        for (uint index = 0; index < commandCount; index++)
        {
            uint command = ReadUInt32(commands, offset);
            int size = checked((int)ReadUInt32(commands, offset + 4));

            if (size < 8 || size > commands.Length - offset)
            {
                throw new InvalidDataException($"Load command {index} has an invalid size.");
            }

            ReadOnlySpan<byte> body = commands.Slice(offset, size);

            switch (command)
            {
                case CommandLoadDylib or CommandLoadWeakDylib or CommandReexportDylib or CommandLazyLoadDylib or CommandLoadUpwardDylib:
                    image.Dependencies.Add(ReadCommandString(body));
                    break;
                case CommandIdDylib:
                    image.InstallName = ReadCommandString(body);
                    break;
                case CommandRunPath:
                    image.RunPaths.Add(ReadCommandString(body));
                    break;
                case CommandBuildVersion:
                    image.MinimumVersions.Add(new(ReadUInt32(body, 8), DecodeVersion(ReadUInt32(body, 12))));
                    break;
                case CommandVersionMinMacOS:
                    image.MinimumVersions.Add(new(PlatformMacOS, DecodeVersion(ReadUInt32(body, 8))));
                    break;
                case CommandSymbolTable:
                    symbolTable = body;
                    break;
                case CommandDyldInfo or CommandDyldInfoOnly:
                    // dyld_info_command: export_off and export_size follow four offset and size pairs.
                    exportTrie = Slice(macho, ReadUInt32(body, 40), ReadUInt32(body, 44));
                    break;
                case CommandDyldExportsTrie:
                    // linkedit_data_command: dataoff, datasize.
                    exportTrie = Slice(macho, ReadUInt32(body, 8), ReadUInt32(body, 12));
                    break;
                default:
                    break;
            }

            offset += size;
        }

        if (!exportTrie.IsEmpty)
        {
            image.ExportSource = "export trie";
            image.ReadExportTrie(exportTrie, 0, [], 0, []);
        }
        else if (!symbolTable.IsEmpty)
        {
            image.ExportSource = "symbol table";
            image.ReadSymbolTable(macho, symbolTable);
        }

        return image;
    }

    private static Version DecodeVersion(uint encoded)
    {
        // Encoded as xxxx.yy.zz: the major version in the high 16 bits, then the minor and patch versions in one byte each.
        return new((int)(encoded >> 16), (int)((encoded >> 8) & 0xFF), (int)(encoded & 0xFF));
    }

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, uint offset, uint length)
    {
        if (offset > (uint)data.Length || length > (uint)data.Length - offset)
        {
            throw new InvalidDataException("A Mach-O table lies outside the image.");
        }

        return data.Slice((int)offset, (int)length);
    }

    private static string ReadCommandString(ReadOnlySpan<byte> command)
    {
        // An lc_str is an offset from the start of the load command; the string is NUL-terminated within it.
        return ReadString(command, ReadUInt32(command, 8));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    }

    private static string ReadString(ReadOnlySpan<byte> table, ulong offset)
    {
        ReadOnlySpan<byte> text = table[checked((int)offset)..];
        int length = text.IndexOf((byte)0);

        if (length < 0)
        {
            throw new InvalidDataException("A Mach-O string is not terminated.");
        }

        return Encoding.UTF8.GetString(text[..length]);
    }

    private static ulong ReadUleb128(ReadOnlySpan<byte> data, ref int offset)
    {
        ulong value = 0;
        int shift = 0;
        byte current;

        do
        {
            if (shift > 63)
            {
                throw new InvalidDataException("A ULEB128 value in the export trie is too long.");
            }

            current = data[offset++];
            value |= (ulong)(current & 0x7F) << shift;
            shift += 7;
        }
        while ((current & 0x80) != 0);

        return value;
    }

    private void ReadExportTrie(ReadOnlySpan<byte> trie, int node, List<byte> prefix, int depth, HashSet<int> visiting)
    {
        // Each node: ULEB128 terminal size, terminal information (flags and address or re-export data) when the size is
        // not zero, a child count byte, then for each child a NUL-terminated edge label and a ULEB128 child offset.
        // A valid trie is a tree, so a node reached twice (a cycle, or children shared by several parents) is malformed;
        // rejecting it also bounds the walk by the number of nodes.
        if (depth > MaximumTrieDepth || !visiting.Add(node))
        {
            throw new InvalidDataException("The Mach-O export trie reaches a node twice or is too deep.");
        }

        int offset = node;
        ulong terminalSize = ReadUleb128(trie, ref offset);

        if (terminalSize != 0)
        {
            string name = Encoding.UTF8.GetString([.. prefix]);
            Exports.Add(name.StartsWith('_') ? name[1..] : name);
        }

        offset = checked(offset + (int)terminalSize);
        int childCount = trie[offset++];

        for (int child = 0; child < childCount; child++)
        {
            int labelLength = trie[offset..].IndexOf((byte)0);

            if (labelLength < 0)
            {
                throw new InvalidDataException("An export trie edge label is not terminated.");
            }

            int prefixLength = prefix.Count;
            prefix.AddRange(trie.Slice(offset, labelLength));
            offset += labelLength + 1;
            int childNode = checked((int)ReadUleb128(trie, ref offset));

            if (childNode >= trie.Length)
            {
                throw new InvalidDataException("An export trie edge points outside the trie.");
            }

            ReadExportTrie(trie, childNode, prefix, depth + 1, visiting);
            prefix.RemoveRange(prefixLength, prefix.Count - prefixLength);
        }
    }

    private void ReadSymbolTable(ReadOnlySpan<byte> macho, ReadOnlySpan<byte> command)
    {
        // symtab_command: symoff, nsyms, stroff, strsize. nlist_64: n_strx (u32), n_type, n_sect, n_desc (u16), n_value (u64).
        uint count = ReadUInt32(command, 12);
        ReadOnlySpan<byte> symbols = Slice(macho, ReadUInt32(command, 8), checked(count * SymbolSize));
        ReadOnlySpan<byte> strings = Slice(macho, ReadUInt32(command, 16), ReadUInt32(command, 20));

        for (int offset = 0; offset + SymbolSize <= symbols.Length; offset += SymbolSize)
        {
            byte type = symbols[offset + 4];

            // Not a debugging (N_STAB) entry, external (N_EXT) but not private external (N_PEXT), and defined:
            // N_TYPE is N_ABS, N_SECT or N_INDR rather than N_UNDF or N_PBUD.
            bool exported = (type & 0xE0) == 0
                && (type & 0x01) != 0
                && (type & 0x10) == 0
                && (type & 0x0E) is 0x2 or 0xE or 0xA;

            if (exported)
            {
                string name = ReadString(strings, ReadUInt32(symbols, offset));
                Exports.Add(name.StartsWith('_') ? name[1..] : name);
            }
        }
    }
}
