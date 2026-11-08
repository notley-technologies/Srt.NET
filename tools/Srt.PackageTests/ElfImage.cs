// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System.Buffers.Binary;
using System.Text;

namespace Srt.PackageTests;

/// <summary>The dynamic-linking metadata of a little-endian ELF64 shared library, read from its section headers.</summary>
internal sealed class ElfImage
{
    internal const ushort MachineX64 = 62;
    internal const ushort MachineAArch64 = 183;

    private const uint SectionDynamic = 6;
    private const uint SectionDynamicSymbols = 11;
    private const uint SectionVersionNeeds = 0x6ffffffe;
    private const int SectionHeaderSize = 64;
    private const int DynamicEntrySize = 16;
    private const int SymbolSize = 24;
    private const long TagNull = 0;
    private const long TagNeeded = 1;
    private const long TagSharedObjectName = 14;

    private ElfImage(ushort machine)
    {
        Machine = machine;
    }

    /// <summary>The e_machine value: 62 for x86-64, 183 for AArch64.</summary>
    internal ushort Machine
    {
        get;
    }

    /// <summary>The DT_SONAME entry, when present.</summary>
    internal string? SharedObjectName
    {
        get; private set;
    }

    /// <summary>The DT_NEEDED entries, in file order.</summary>
    internal List<string> Needed { get; } = [];

    /// <summary>The GNU symbol versions required from dependencies (SHT_GNU_verneed).</summary>
    internal List<ElfVersionNeed> VersionNeeds { get; } = [];

    /// <summary>Defined GLOBAL or WEAK dynamic symbols with default or protected visibility.</summary>
    internal SortedSet<string> Exports { get; } = new(StringComparer.Ordinal);

    internal static bool IsElf64LittleEndian(ReadOnlySpan<byte> image)
    {
        return image.Length >= 64 && image[..6].SequenceEqual("\u007fELF\u0002\u0001"u8);
    }

    internal static ElfImage Read(byte[] image)
    {
        try
        {
            return ReadCore(image);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException("The ELF image is truncated or malformed.", exception);
        }
    }

    private static ElfImage ReadCore(ReadOnlySpan<byte> elf)
    {
        if (!IsElf64LittleEndian(elf))
        {
            throw new InvalidDataException("Not a little-endian ELF64 file.");
        }

        ElfImage image = new(ReadUInt16(elf, 18));
        int sectionTable = checked((int)ReadUInt64(elf, 0x28));
        int sectionSize = ReadUInt16(elf, 0x3A);
        int sectionCount = ReadUInt16(elf, 0x3C);

        if (sectionCount == 0 || sectionSize < SectionHeaderSize)
        {
            throw new InvalidDataException("The ELF image has no usable section headers.");
        }

        for (int index = 0; index < sectionCount; index++)
        {
            ReadOnlySpan<byte> section = GetSectionHeader(elf, sectionTable, sectionSize, index);
            uint type = ReadUInt32(section, 4);

            if (type is not (SectionDynamic or SectionDynamicSymbols or SectionVersionNeeds))
            {
                continue;
            }

            // Each of these sections names its string table (.dynstr) through sh_link.
            ReadOnlySpan<byte> data = GetSectionData(elf, section);
            ReadOnlySpan<byte> strings = GetSectionData(elf, GetSectionHeader(elf, sectionTable, sectionSize, checked((int)ReadUInt32(section, 0x28))));

            if (type == SectionDynamic)
            {
                image.ReadDynamic(data, strings);
            }
            else if (type == SectionDynamicSymbols)
            {
                int entrySize = checked((int)ReadUInt64(section, 0x38));
                image.ReadSymbols(data, strings, entrySize == 0 ? SymbolSize : entrySize);
            }
            else
            {
                image.ReadVersionNeeds(data, strings, checked((int)ReadUInt32(section, 0x2C)));
            }
        }

        return image;
    }

    private static ReadOnlySpan<byte> GetSectionHeader(ReadOnlySpan<byte> elf, int table, int size, int index)
    {
        return elf.Slice(checked(table + (index * size)), SectionHeaderSize);
    }

    private static ReadOnlySpan<byte> GetSectionData(ReadOnlySpan<byte> elf, ReadOnlySpan<byte> section)
    {
        return elf.Slice(checked((int)ReadUInt64(section, 0x18)), checked((int)ReadUInt64(section, 0x20)));
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]);
    }

    private static string ReadString(ReadOnlySpan<byte> table, ulong offset)
    {
        ReadOnlySpan<byte> text = table[checked((int)offset)..];
        int length = text.IndexOf((byte)0);

        if (length < 0)
        {
            throw new InvalidDataException("An ELF string is not terminated.");
        }

        return Encoding.UTF8.GetString(text[..length]);
    }

    private void ReadDynamic(ReadOnlySpan<byte> data, ReadOnlySpan<byte> strings)
    {
        for (int offset = 0; offset + DynamicEntrySize <= data.Length; offset += DynamicEntrySize)
        {
            long tag = (long)ReadUInt64(data, offset);
            ulong value = ReadUInt64(data, offset + 8);

            if (tag == TagNull)
            {
                break;
            }

            if (tag == TagNeeded)
            {
                Needed.Add(ReadString(strings, value));
            }
            else if (tag == TagSharedObjectName)
            {
                SharedObjectName = ReadString(strings, value);
            }
        }
    }

    private void ReadSymbols(ReadOnlySpan<byte> data, ReadOnlySpan<byte> strings, int entrySize)
    {
        if (entrySize < SymbolSize)
        {
            throw new InvalidDataException("The ELF dynamic symbol table has an invalid entry size.");
        }

        // Entry 0 is the reserved undefined symbol.
        for (int offset = entrySize; offset + SymbolSize <= data.Length; offset += entrySize)
        {
            byte information = data[offset + 4];
            byte other = data[offset + 5];
            ushort sectionIndex = ReadUInt16(data, offset + 6);
            int binding = information >> 4;
            int symbolType = information & 0xF;
            int visibility = other & 3;

            // Defined (st_shndx != SHN_UNDEF), STB_GLOBAL or STB_WEAK, STV_DEFAULT or STV_PROTECTED, not STT_SECTION or STT_FILE.
            bool exported = sectionIndex != 0
                && binding is 1 or 2
                && visibility is 0 or 3
                && symbolType is not (3 or 4);

            if (exported)
            {
                Exports.Add(ReadString(strings, ReadUInt32(data, offset)));
            }
        }
    }

    private void ReadVersionNeeds(ReadOnlySpan<byte> data, ReadOnlySpan<byte> strings, int count)
    {
        // Elf64_Verneed { u16 version, u16 cnt, u32 file, u32 aux, u32 next }
        // Elf64_Vernaux { u32 hash, u16 flags, u16 other, u32 name, u32 next }
        int need = 0;

        for (int index = 0; index < count; index++)
        {
            int auxiliaryCount = ReadUInt16(data, need + 2);
            string file = ReadString(strings, ReadUInt32(data, need + 4));
            int auxiliary = checked(need + (int)ReadUInt32(data, need + 8));

            for (int version = 0; version < auxiliaryCount; version++)
            {
                VersionNeeds.Add(new(file, ReadString(strings, ReadUInt32(data, auxiliary + 8))));
                auxiliary = checked(auxiliary + (int)ReadUInt32(data, auxiliary + 12));
            }

            uint next = ReadUInt32(data, need + 12);

            if (next == 0)
            {
                break;
            }

            need = checked(need + (int)next);
        }
    }
}
