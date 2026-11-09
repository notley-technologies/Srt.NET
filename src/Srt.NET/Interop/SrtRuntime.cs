// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
#if NET
using System.Runtime.Loader;
#endif
using System.Threading;
using Srt.Net.Async;

namespace Srt.Net.Interop;

internal static class SrtRuntime
{
    private static readonly object Gate = new();
    private static int references;
    private static bool exitHandlerInstalled;
    private static int exiting;
    private static Version? nativeVersion;

    internal static Version NativeVersion
    {
        get
        {
            Version? version = Volatile.Read(ref nativeVersion);

            if (version != null)
            {
                return version;
            }

            uint packed = NativeMethods.srt_getversion();
            version = new((int)(packed >> 16), (int)((packed >> 8) & 255), (int)(packed & 255));

            return Interlocked.CompareExchange(ref nativeVersion, version, null) ?? version;
        }
    }

    /// <summary>Whether libsrt has been shut down because the process is exiting.</summary>
    internal static bool IsExiting => Volatile.Read(ref exiting) != 0;

    internal static void Acquire()
    {
        lock (Gate)
        {
            if (IsExiting)
            {
                throw new ObjectDisposedException("libsrt", "libsrt has been shut down because the process is exiting.");
            }

            if (references == 0)
            {
                Version version = NativeVersion;

                if (version.Major != 1 || version.Minor < 5)
                {
                    throw new NotSupportedException("Srt.NET requires libsrt 1.5 or a later 1.x release.");
                }

                SrtException.ThrowIfError(NativeMethods.srt_startup(), "start libsrt");
                InstallExitHandlers();
            }

            references++;
        }
    }

    /// <summary>Installs the exit and unload handler, which also removes the native log handler (see SrtLogging).</summary>
    internal static void EnsureExitHandlers()
    {
        lock (Gate)
        {
            InstallExitHandlers();
        }
    }

    private static void InstallExitHandlers()
    {
        if (exitHandlerInstalled)
        {
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
#if NET
        // A collectible load context can unload this assembly before the process exits.
        AssemblyLoadContext.GetLoadContext(typeof(SrtRuntime).Assembly)!.Unloading += OnUnloading;
#else
        // A non-default .NET Framework application domain (ASP.NET, test runners, plug-in hosts) unloads without
        // ProcessExit, and its threads are aborted. libsrt counts startups process-wide, so this domain cleans up its
        // own share as it unloads.
        AppDomain.CurrentDomain.DomainUnload += OnProcessExit;
#endif
        exitHandlerInstalled = true;
    }

    internal static void Release()
    {
        lock (Gate)
        {
            if (IsExiting)
            {
                return;
            }

            if (--references == 0)
            {
                NativeMethods.srt_cleanup();
            }
        }
    }

#if NET
    private static void OnUnloading(AssemblyLoadContext context)
    {
        // A process-wide event handler would keep a collectible load context alive.
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        OnProcessExit(null, EventArgs.Empty);
    }
#endif

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        // Stop the engine thread first, outside the lock (its exit path may take it), so no thread is inside
        // libsrt while it is cleaned up or while static destructors run after this handler returns.
        SrtAsyncEngine.ShutdownForExit();

        lock (Gate)
        {
            // If libsrt is still started when the OS unloads it, its destructor runs cleanup after the process has
            // killed libsrt's threads, which aborts the process (0xC0000409 on Windows). Cleaning up here instead
            // breaks and removes every remaining socket and joins libsrt's threads while they are still running.
            if (Interlocked.Exchange(ref exiting, 1) == 0 && references != 0)
            {
                NativeMethods.srt_cleanup();
            }
        }

        // Last, so the warnings that cleanup logs still reach the managed handler. Another component in the process can
        // keep libsrt started past this point, and its threads must not call into code that is shutting down or
        // unloading.
        SrtLogging.DetachForExit();
    }
}
