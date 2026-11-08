// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net;

/// <summary>A socket option with a defined managed value type.</summary>
/// <typeparam name="T">The option's value type.</typeparam>
public sealed class SrtOption<T>
{
    internal int Id
    {
        get;
    }

    internal bool CanRead
    {
        get;
    }

    internal bool CanWrite
    {
        get;
    }

    internal int MaximumBytes
    {
        get;
    }

    /// <summary>Gets whether values must be cleared from temporary buffers after use.</summary>
    internal bool IsSecret
    {
        get;
    }

    internal SrtOption(int id, string name, bool canRead = true, bool canWrite = true, int maximumBytes = 0, bool isSecret = false)
    {
        Id = id;
        Name = name;
        CanRead = canRead;
        CanWrite = canWrite;
        MaximumBytes = maximumBytes;
        IsSecret = isSecret;
    }

    /// <summary>The option name used by the libsrt C API.</summary>
    public string Name
    {
        get;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Name;
    }
}
