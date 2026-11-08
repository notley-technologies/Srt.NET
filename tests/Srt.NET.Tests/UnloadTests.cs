// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

#if NET
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Srt.Net;
using Xunit;

namespace Srt.NET.Tests;

/// <summary>Plug-in hosts load Srt.NET into collectible load contexts and must be able to unload it again.</summary>
public sealed class UnloadTests : IClassFixture<RuntimeFixture>
{
    [Fact]
    public void ACollectibleLoadContextThatUsedSrtCanBeCollected()
    {
        WeakReference context = UseAndUnload();

        for (int attempt = 0; attempt < 20 && context.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(context.IsAlive, "The unloaded load context is still referenced.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference UseAndUnload()
    {
        AssemblyLoadContext context = new("Srt.NET unload test", isCollectible: true);
        Assembly assembly = context.LoadFromAssemblyPath(typeof(SrtSocket).Assembly.Location);
        Type socketType = assembly.GetType(typeof(SrtSocket).FullName!, throwOnError: true)!;

        // A second copy of the assembly, with its own libsrt startup and process-exit handler.
        ((IDisposable)Activator.CreateInstance(socketType)!).Dispose();
        context.Unload();

        return new(context);
    }
}
#endif
