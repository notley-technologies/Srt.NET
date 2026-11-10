// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

namespace Srt.Net.PackageValidation;

/// <summary>The machine type and security characteristics of a native Windows PE image.</summary>
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

            return result;
        }
        catch (BadImageFormatException exception)
        {
            throw new InvalidDataException("The PE image is malformed.", exception);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The PE debug directory is truncated.", exception);
        }
    }
}
