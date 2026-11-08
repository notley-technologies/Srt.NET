// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net;

/// <summary>An immutable native log record.</summary>
public sealed class SrtLogEntry
{
    internal SrtLogEntry(SrtLogLevel level, string file, int line, string area, string message)
    {
        Level = level;
        File = file;
        Line = line;
        Area = area;
        Message = message;
    }

    /// <summary>The message severity.</summary>
    public SrtLogLevel Level
    {
        get;
    }

    /// <summary>The native source file, when available.</summary>
    public string File
    {
        get;
    }

    /// <summary>The native source line, when available.</summary>
    public int Line
    {
        get;
    }

    /// <summary>The native functional area label.</summary>
    public string Area
    {
        get;
    }

    /// <summary>The native formatted UTF-8 message, decoded as managed text.</summary>
    public string Message
    {
        get;
    }
}
