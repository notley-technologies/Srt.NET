// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

#if !NETCOREAPP3_0_OR_GREATER
namespace System.Diagnostics.CodeAnalysis;

/// <summary>Nullable analysis attribute for netstandard2.0, where the framework does not define it.</summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
internal sealed class NotNullWhenAttribute : Attribute
{
    public NotNullWhenAttribute(bool returnValue)
    {
        ReturnValue = returnValue;
    }

    public bool ReturnValue
    {
        get;
    }
}
#endif
