// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Collections.Concurrent;
using System.Threading;
using Srt.Net.Interop;

namespace Srt.Net;

/// <summary>Process-wide native logging configuration. Nothing changes until the application configures it.</summary>
/// <remarks>
/// Handlers run on a managed worker, never under libsrt's logging lock. Delivery is bounded to 1024 queued records.
/// The native handler is shared by the whole process, so the most recent <see cref="SetHandler"/> call wins. Clear the
/// handler before unloading application-owned logging services. When the process exits, or an application domain or
/// collectible load context that loaded Srt.NET unloads, the handler is removed after libsrt has been cleaned up, so
/// the warnings that cleanup logs still reach it; libsrt's default output applies afterwards.
/// </remarks>
public static class SrtLogging
{
    private static readonly object Gate = new();
    private static readonly NativeLogCallback Callback = Receive;
    private static readonly ConcurrentQueue<SrtLogEntry> Queue = new();
    private static Action<SrtLogEntry>? handler;
    private static bool exitHandlerInstalled;
    private static Exception? lastHandlerException;
    private static int queued;
    private static int worker;
    private static long dropped;

    /// <summary>Records dropped because the queue was full or a callback failed.</summary>
    public static long DroppedMessages => Interlocked.Read(ref dropped);

    /// <summary>The most recent managed handler exception. Exceptions do not escape to native threads.</summary>
    public static Exception? LastHandlerException => Volatile.Read(ref lastHandlerException);

    /// <summary>Changes the native severity threshold for the whole process.</summary>
    public static void SetLevel(SrtLogLevel level)
    {
        if (!Enum.IsDefined(typeof(SrtLogLevel), level))
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        NativeMethods.srt_setloglevel((int)level);
    }

    /// <summary>Changes native message formatting for the whole process.</summary>
    public static void SetFormat(SrtLogFormat format)
    {
        if (((int)format & ~15) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        NativeMethods.srt_setlogflags((int)format);
    }

    /// <summary>Replaces the process-wide managed handler. Null restores libsrt's default output.</summary>
    /// <remarks>In-flight callbacks may finish after replacement; queued records use the handler current at delivery.</remarks>
    public static void SetHandler(Action<SrtLogEntry>? value)
    {
        lock (Gate)
        {
            if (value != null && SrtRuntime.IsExiting)
            {
                // The exit and unload handler has already removed the native handler and would not remove this one.
                throw new ObjectDisposedException("libsrt", "libsrt has been shut down because the process is exiting.");
            }

            if (value != null && !exitHandlerInstalled)
            {
                // libsrt would otherwise keep calling into code that no longer exists, which ends the process. The
                // runtime's exit and unload handler cleans libsrt up first and then calls DetachForExit.
                SrtRuntime.EnsureExitHandlers();
                exitHandlerInstalled = true;
            }

            Volatile.Write(ref handler, value);
            NativeMethods.srt_setloghandler(IntPtr.Zero, value == null ? null : Callback);
        }
    }

    /// <summary>Removes the native handler as the process exits or this copy of Srt.NET unloads. Called by SrtRuntime.</summary>
    internal static void DetachForExit()
    {
        lock (Gate)
        {
            // This copy's last call installed a handler. libsrt cannot report the current handler, so a handler that
            // another domain or context installed since then is cleared too and must be set again.
            Action<SrtLogEntry>? current = Volatile.Read(ref handler);

            if (current == null)
            {
                return;
            }

            // libsrt calls the handler under its logging lock, so no record arrives after this returns.
            NativeMethods.srt_setloghandler(IntPtr.Zero, null);

            // Deliver what is still queued, such as the warnings that cleanup just logged, while this code still exists.
            // Take over from the worker so records stay in order; one that does not finish in time drops the rest.
            if (SpinWait.SpinUntil(() => Interlocked.CompareExchange(ref worker, 1, 0) == 0, TimeSpan.FromSeconds(1)))
            {
                while (Queue.TryDequeue(out SrtLogEntry? entry))
                {
                    Interlocked.Decrement(ref queued);

                    try
                    {
                        current(entry);
                    }
                    catch (Exception error)
                    {
                        Interlocked.Increment(ref dropped);
                        Volatile.Write(ref lastHandlerException, error);
                    }
                }

                Volatile.Write(ref worker, 0);
            }

            Volatile.Write(ref handler, null);

            while (Queue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref queued);
                Interlocked.Increment(ref dropped);
            }
        }
    }

    /// <summary>Enables one native functional area without resetting others.</summary>
    public static void EnableArea(SrtLogArea area)
    {
        ValidateArea(area);
        NativeMethods.srt_addlogfa((int)area);
    }

    /// <summary>Disables one native functional area.</summary>
    public static void DisableArea(SrtLogArea area)
    {
        ValidateArea(area);
        NativeMethods.srt_dellogfa((int)area);
    }

    /// <summary>Replaces the enabled functional areas. An empty array disables all areas.</summary>
    public static void SetAreas(params SrtLogArea[] areas)
    {
        if (areas == null)
        {
            throw new ArgumentNullException(nameof(areas));
        }

        int[] values = new int[areas.Length];

        for (int index = 0; index < areas.Length; index++)
        {
            ValidateArea(areas[index]);
            values[index] = (int)areas[index];
        }

        NativeMethods.srt_resetlogfa(values, new((uint)values.Length));
    }

    private static void ValidateArea(SrtLogArea area)
    {
        if (!Enum.IsDefined(typeof(SrtLogArea), area))
        {
            throw new ArgumentOutOfRangeException(nameof(area));
        }
    }

    private static void Receive(IntPtr context, int level, IntPtr file, int line, IntPtr area, IntPtr message)
    {
        bool reserved = false;

        try
        {
            if (Volatile.Read(ref handler) == null)
            {
                return;
            }

            if (Interlocked.Increment(ref queued) > 1024)
            {
                Interlocked.Decrement(ref queued);
                Interlocked.Increment(ref dropped);

                return;
            }

            reserved = true;
            SrtLogEntry entry = new((SrtLogLevel)level, Utf8Interop.Read(file), line, Utf8Interop.Read(area), Utf8Interop.Read(message));
            Queue.Enqueue(entry);
            reserved = false;

            if (Interlocked.CompareExchange(ref worker, 1, 0) == 0)
            {
                ThreadPool.QueueUserWorkItem(Drain);
            }
        }
        catch (Exception)
        {
            if (reserved)
            {
                Interlocked.Decrement(ref queued);
            }

            // Managed failures cannot unwind through libsrt's callback boundary.
            Interlocked.Increment(ref dropped);
        }
    }

    private static void Drain(object? state)
    {
        do
        {
            while (Queue.TryDequeue(out SrtLogEntry? entry))
            {
                Interlocked.Decrement(ref queued);

                try
                {
                    Volatile.Read(ref handler)?.Invoke(entry);
                }
                catch (Exception error)
                {
                    // Count before publishing so an observer of LastHandlerException also sees the drop.
                    Interlocked.Increment(ref dropped);
                    Volatile.Write(ref lastHandlerException, error);
                }
            }

            Volatile.Write(ref worker, 0);
        }
        while (!Queue.IsEmpty && Interlocked.CompareExchange(ref worker, 1, 0) == 0);
    }
}
