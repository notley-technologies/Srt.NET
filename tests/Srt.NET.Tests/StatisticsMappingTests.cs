// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Linq;
using System.Reflection;
using Srt.Net;
using Srt.Net.Interop;
using Xunit;

namespace Srt.Net.Tests;

/// <summary>Checks that every public statistic reads its own native field, not a neighbour's.</summary>
public sealed class StatisticsMappingTests
{
    private const BindingFlags NativeFields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [Fact]
    public void EveryStatisticReturnsTheNativeFieldOfTheSameName()
    {
        FieldInfo[] fields = typeof(NativeStatistics).GetFields(NativeFields);
        object boxed = default(NativeStatistics);

        // A distinct value per field, so a property that reads the wrong field cannot match by accident.
        for (int index = 0; index < fields.Length; index++)
        {
            fields[index].SetValue(boxed, Convert.ChangeType(index + 1, fields[index].FieldType, System.Globalization.CultureInfo.InvariantCulture));
        }

        NativeStatistics native = (NativeStatistics)boxed;
        SrtStatistics statistics = new(in native);
        PropertyInfo[] properties = typeof(SrtStatistics).GetProperties(BindingFlags.Instance | BindingFlags.Public);

        Assert.Equal(82, fields.Length);
        Assert.Equal(fields.Select(field => field.Name).OrderBy(name => name, StringComparer.Ordinal), properties.Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));

        foreach (PropertyInfo property in properties)
        {
            FieldInfo field = fields.Single(candidate => candidate.Name == property.Name);

            Assert.Equal(field.FieldType, property.PropertyType);
            Assert.True(Equals(field.GetValue(boxed), property.GetValue(statistics)), $"{property.Name} does not return the native {field.Name} field.");
        }
    }

    [Fact]
    public void TheMessageOptionsConstructorStoresEveryValue()
    {
        SrtMessageOptions options = new(TimeSpan.FromMilliseconds(250), inOrder: true, sourceTimeMicroseconds: 42);

        Assert.Equal(TimeSpan.FromMilliseconds(250), options.TimeToLive);
        Assert.True(options.InOrder);
        Assert.Equal(42, options.SourceTimeMicroseconds);

        SrtMessageOptions defaults = new(null);

        Assert.Null(defaults.TimeToLive);
        Assert.False(defaults.InOrder);
        Assert.Equal(0, defaults.SourceTimeMicroseconds);
    }
}
