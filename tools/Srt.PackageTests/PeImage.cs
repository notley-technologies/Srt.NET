// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Srt.PackageTests;

/// <summary>The machine type and named exports of a native Windows PE image.</summary>
internal sealed class PeImage
{
    // IMAGE_DLLCHARACTERISTICS_GUARD_CF, and the IMAGE_DEBUG_TYPE_EX_DLLCHARACTERISTICS entry with its CET_COMPAT bit;
    // System.Reflection.Metadata does not name them.
    private const ushort GuardCf = 0x4000;
    private const int ExtendedDllCharacteristicsEntry = 20;
    private const uint CetCompat = 0x1;

    private PeImage(Machine machine, bool isPE32Plus, bool isDll, DllCharacteristics dllCharacteristics)
    {
        Machine = machine;
        IsPE32Plus = isPE32Plus;
        IsDll = isDll;
        DllCharacteristics = dllCharacteristics;
    }

    internal Machine Machine
    {
        get;
    }

    internal bool IsPE32Plus
    {
        get;
    }

    internal bool IsDll
    {
        get;
    }

    internal DllCharacteristics DllCharacteristics
    {
        get;
    }

    /// <summary>Whether the image was linked with Control Flow Guard (/guard:cf).</summary>
    internal bool HasControlFlowGuard => ((ushort)DllCharacteristics & GuardCf) != 0;

    /// <summary>Whether the image is marked compatible with CET shadow stacks (/CETCOMPAT).</summary>
    internal bool IsCetCompatible
    {
        get;
        private set;
    }

    /// <summary>The names in the export directory; ordinal-only exports are not listed.</summary>
    internal SortedSet<string> Exports { get; } = new(StringComparer.Ordinal);

    internal static PeImage Read(byte[] image)
    {
        try
        {
            using PEReader reader = new(ImmutableArray.Create(image));
            PEHeaders headers = reader.PEHeaders;

            if (headers.PEHeader == null)
            {
                throw new InvalidDataException("The PE image has no optional header.");
            }

            PeImage result = new(
                headers.CoffHeader.Machine,
                headers.PEHeader.Magic == PEMagic.PE32Plus,
                (headers.CoffHeader.Characteristics & Characteristics.Dll) != 0,
                headers.PEHeader.DllCharacteristics);

            foreach (DebugDirectoryEntry entry in reader.ReadDebugDirectory())
            {
                if ((int)entry.Type == ExtendedDllCharacteristicsEntry && entry.DataSize >= sizeof(uint))
                {
                    result.IsCetCompatible = (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(entry.DataPointer)) & CetCompat) != 0;
                }
            }

            DirectoryEntry directory = headers.PEHeader.ExportTableDirectory;

            if (directory.Size != 0)
            {
                result.ReadExports(image, headers, directory.RelativeVirtualAddress);
            }

            return result;
        }
        catch (BadImageFormatException exception)
        {
            throw new InvalidDataException("The PE image is malformed.", exception);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException("The PE export table is truncated or malformed.", exception);
        }
    }

    private static int ToFileOffset(PEHeaders headers, int relativeVirtualAddress)
    {
        foreach (SectionHeader section in headers.SectionHeaders)
        {
            int size = Math.Max(section.VirtualSize, section.SizeOfRawData);

            if (relativeVirtualAddress >= section.VirtualAddress && relativeVirtualAddress < section.VirtualAddress + size)
            {
                return checked(relativeVirtualAddress - section.VirtualAddress + section.PointerToRawData);
            }
        }

        throw new InvalidDataException($"PE address 0x{relativeVirtualAddress:X} is outside every section.");
    }

    private void ReadExports(ReadOnlySpan<byte> image, PEHeaders headers, int directoryAddress)
    {
        // IMAGE_EXPORT_DIRECTORY: NumberOfNames at 0x18 and AddressOfNames (an array of name RVAs) at 0x20.
        ReadOnlySpan<byte> directory = image[ToFileOffset(headers, directoryAddress)..];
        int count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(directory[0x18..]));

        if (count == 0)
        {
            return;
        }

        int names = ToFileOffset(headers, checked((int)BinaryPrimitives.ReadUInt32LittleEndian(directory[0x20..])));

        for (int index = 0; index < count; index++)
        {
            int address = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(image[(names + (index * 4))..]));
            ReadOnlySpan<byte> name = image[ToFileOffset(headers, address)..];
            int length = name.IndexOf((byte)0);

            if (length < 0)
            {
                throw new InvalidDataException("A PE export name is not terminated.");
            }

            Exports.Add(Encoding.ASCII.GetString(name[..length]));
        }
    }
}
