// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Srt.Net.Interop;

/// <summary>The process-wide readiness loop for socket operations.</summary>
/// <remarks>
/// <para>libsrt wakes epoll waiters through one process-wide condition variable signaled with notify_one, so a single
/// waiting thread receives wake-ups promptly while several waiters can each be delayed by up to 10 ms. One engine
/// thread therefore waits for every socket. It only harvests events: it wakes blocked synchronous callers directly
/// and schedules asynchronous processing on the thread pool, so no native socket call or user code runs on it.</para>
/// <para>Sockets are subscribed edge-triggered. The engine holds sockets weakly while they have no pending operation,
/// so undisposed sockets can be finalized, and strongly while operations are pending. The thread stops after a period
/// with no registered sockets, and at process exit before libsrt is cleaned up.</para>
/// </remarks>
internal sealed unsafe class SrtAsyncEngine
{
    private const int EventCapacity = 256;
    private const int SubscribedEvents = NativeConstants.SRT_EPOLL_IN | NativeConstants.SRT_EPOLL_OUT
        | NativeConstants.SRT_EPOLL_ERR | NativeConstants.SRT_EPOLL_ET;

    // libsrt itself rescans every 10 ms, so a finite wait costs nothing extra.
    internal const int IdleWaitMilliseconds = 100;
    private const int RetryWaitMilliseconds = 10;
    private const int TickMilliseconds = 10;
    private const int IdleShutdownMilliseconds = 10_000;

    // Nothing can wake a libsrt wait early, so a retry requested during a 100 ms wait is seen only when it ends. A sender
    // that keeps blocking keeps the short wait for this long after the first retry of its latest blocked send, or after
    // a send that needed retries completes, so only the first retry after a quiet period can be late. Retries that back
    // off do not extend it.
    private const int RetryHoldMilliseconds = 1_000;

    private static readonly object CreationGate = new();

    [ThreadStatic]
    private static bool onEngineThread;
    private static int watchdogMilliseconds = 250;
    private static SrtAsyncEngine? instance;
    private static bool shutDown;

    private readonly ConcurrentDictionary<int, WeakReference<SrtAsyncContext>> contexts = new();
    private readonly ConcurrentDictionary<SrtAsyncContext, bool> busy = new();
    private readonly object drainGate = new();
    private readonly List<KeyValuePair<int, long>> drains = [];
    private readonly Thread thread;
    private readonly int poll;
    private int registered;
    private int retriesRequested;
    private long lastRetryRequest;
    private int drainCount;
    private int stopRequested;
    private bool releaseRuntimeLease = true;

    private SrtAsyncEngine()
    {
        SrtRuntime.Acquire();

        try
        {
            poll = NativeMethods.srt_epoll_create();
            SrtException.ThrowIfError(poll, "create the SRT engine poll");

            if (NativeMethods.srt_epoll_set(poll, NativeConstants.SRT_EPOLL_ENABLE_EMPTY) == NativeConstants.SRT_ERROR)
            {
                SrtException error = SrtException.LastError("configure the SRT engine poll");
                NativeMethods.srt_epoll_release(poll);

                throw error;
            }
        }
        catch
        {
            SrtRuntime.Release();

            throw;
        }

        thread = new(Run)
        {
            IsBackground = true,
            Name = "SRT engine"
        };

        // Do not leak the creating caller's AsyncLocal values into the engine thread.
        using (ExecutionContext.SuppressFlow())
        {
            thread.Start();
        }
    }

    /// <summary>How long a queue may wait without an edge before the engine retries it anyway.</summary>
    /// <remarks>
    /// A backstop for readiness changes that libsrt does not report as edges. Tests raise it so that a lost edge fails
    /// instead of being hidden by this retry.
    /// </remarks>
    internal static int WatchdogMilliseconds
    {
        get => Volatile.Read(ref watchdogMilliseconds);
        set => Volatile.Write(ref watchdogMilliseconds, value);
    }

    /// <summary>Whether the calling thread is the engine thread, for tests.</summary>
    internal static bool IsEngineThread => onEngineThread;

    /// <summary>The number of sockets registered with the running engine, for diagnostics and tests.</summary>
    internal static int RegisteredSockets => Volatile.Read(ref instance) is SrtAsyncEngine engine ? Volatile.Read(ref engine.registered) : 0;

    /// <summary>Registers a socket, starting the engine if needed. Call while holding a lease on the socket.</summary>
    internal static SrtAsyncEngine Register(SrtAsyncContext context)
    {
        SrtAsyncEngine engine;

        lock (CreationGate)
        {
            if (shutDown)
            {
                throw new ObjectDisposedException("libsrt", "libsrt has been shut down because the process is exiting.");
            }

            // A registered socket prevents the engine from stopping, so it stays valid after the lock is released. The
            // increment is atomic because the decrements run outside the lock.
            engine = instance ??= new();
            Interlocked.Increment(ref engine.registered);
        }

        // Publish the mapping first, so the initial readiness that the subscription reports finds it.
        engine.contexts[context.SocketId] = new(context);
        int events = SubscribedEvents;

        if (NativeMethods.srt_epoll_add_usock(engine.poll, context.SocketId, ref events) == NativeConstants.SRT_ERROR)
        {
            SrtException error = SrtException.LastError("register the socket with the SRT engine");
            engine.Forget(context.SocketId);

            throw error;
        }

        return engine;
    }

    /// <summary>Removes a socket whose handle was released without being disposed (for example, by its finalizer).</summary>
    internal static void ForgetReleased(int socketId)
    {
        Volatile.Read(ref instance)?.Forget(socketId);
    }

    /// <summary>Closes a released socket, first letting it send its queued data when linger is enabled.</summary>
    /// <remarks>
    /// With a nonblocking socket, libsrt's own linger stops transmitting after a few packets yet keeps the socket, and any
    /// srt_cleanup call, waiting for the whole linger time (180 seconds by default in file mode). Sockets are therefore
    /// always closed with linger off: immediately, or once the engine sees that the send buffer has drained, the
    /// connection has broken, or the linger time has passed. That matches a TCP close. Process exit abandons the drain.
    /// </remarks>
    internal static bool Close(int socket)
    {
        long deadline = GetDrainDeadline(socket);

        if (deadline != 0 && TryScheduleDrain(socket, deadline))
        {
            return true;
        }

        return CloseNow(socket);
    }

    /// <summary>Stops the engine before libsrt is cleaned up at process exit, so its thread is not inside libsrt.</summary>
    internal static void ShutdownForExit()
    {
        SrtAsyncEngine? engine;

        lock (CreationGate)
        {
            shutDown = true;
            engine = instance;
            instance = null;
        }

        if (engine == null)
        {
            return;
        }

        // The runtime is about to clean up libsrt itself.
        engine.releaseRuntimeLease = false;
        Volatile.Write(ref engine.stopRequested, 1);

        if (Thread.CurrentThread != engine.thread)
        {
            engine.thread.Join(TimeSpan.FromSeconds(1));
        }

        // Exit must stay bounded, so data that is still queued is discarded.
        engine.CloseDrains(force: true, 0);
    }

    /// <param name="context">The socket's context.</param>
    /// <param name="closing">
    /// True when the socket is about to be closed. srt_close drops the subscription itself, and removing it here could
    /// race libsrt's collector, which logs errors; events that still arrive for the socket are ignored.
    /// </param>
    internal void Unregister(SrtAsyncContext context, bool closing)
    {
        Unroot(context);

        if (!closing)
        {
            EpollSubscription.Remove(poll, context.SocketId);
        }

        Forget(context.SocketId);
    }

    internal void Root(SrtAsyncContext context)
    {
        busy[context] = true;
    }

    internal void Unroot(SrtAsyncContext context)
    {
        busy.TryRemove(context, out _);
    }

    internal void RequestRetries(bool first)
    {
        if (first)
        {
            Volatile.Write(ref lastRetryRequest, Stopwatch.GetTimestamp());
        }

        Volatile.Write(ref retriesRequested, 1);
    }

    private void Forget(int socketId)
    {
        // Whoever removes the entry releases its registration, so each registration is counted exactly once.
        if (contexts.TryRemove(socketId, out _))
        {
            Interlocked.Decrement(ref registered);
        }
    }

    private void Run()
    {
        onEngineThread = true;
        NativePollEvent* events = stackalloc NativePollEvent[EventCapacity];
        long tickTicks = Stopwatch.Frequency * TickMilliseconds / 1000;
        long idleTicks = Stopwatch.Frequency * IdleShutdownMilliseconds / 1000;
        long nextTick = 0;
        long idleSince = 0;

        try
        {
            RunLoop(events, tickTicks, idleTicks, ref nextTick, ref idleSince);
        }
        finally
        {
            // Also runs when the thread is aborted (application domain unload) or faults, so libsrt is not left started.
            NativeMethods.srt_epoll_release(poll);

            if (releaseRuntimeLease)
            {
                SrtRuntime.Release();
            }
        }
    }

    private void RunLoop(NativePollEvent* events, long tickTicks, long idleTicks, ref long nextTick, ref long idleSince)
    {
        long retryHoldTicks = Stopwatch.Frequency * RetryHoldMilliseconds / 1000;

        while (Volatile.Read(ref stopRequested) == 0)
        {
            bool retrying = Volatile.Read(ref retriesRequested) != 0
                || Stopwatch.GetTimestamp() - Volatile.Read(ref lastRetryRequest) < retryHoldTicks;
            int timeout = retrying ? RetryWaitMilliseconds : IdleWaitMilliseconds;
            int count = NativeMethods.srt_epoll_uwait_buffer(poll, events, EventCapacity, timeout);

            if (count > 0)
            {
                Dispatch(events, count);
            }
            else if (count == NativeConstants.SRT_ERROR)
            {
                if (SrtRuntime.IsExiting)
                {
                    break;
                }

                // A wait on a valid poll should not fail; avoid spinning if it does.
                Thread.Sleep(TickMilliseconds);
            }

            long now = Stopwatch.GetTimestamp();

            if (now < nextTick)
            {
                continue;
            }

            nextTick = now + tickTicks;
            CheckTimers(now, Stopwatch.Frequency * WatchdogMilliseconds / 1000);

            if (Volatile.Read(ref registered) != 0)
            {
                idleSince = 0;
            }
            else if (idleSince == 0)
            {
                idleSince = now;
            }
            else if (now - idleSince >= idleTicks && TryStopWhenIdle())
            {
                break;
            }
        }
    }

    private void Dispatch(NativePollEvent* events, int count)
    {
        for (int index = 0; index < count; index++)
        {
            int socket = events[index].Socket;

            if (!contexts.TryGetValue(socket, out WeakReference<SrtAsyncContext>? reference))
            {
                // A late event for a socket that has been disposed or released.
                continue;
            }

            if (!reference.TryGetTarget(out SrtAsyncContext? context))
            {
                // The socket was collected without being disposed; its handle's finalizer closes it.
                if (((ICollection<KeyValuePair<int, WeakReference<SrtAsyncContext>>>)contexts).Remove(new(socket, reference)))
                {
                    Interlocked.Decrement(ref registered);
                }

                continue;
            }

            try
            {
                context.OnEvents(events[index].Events);
            }
            catch (Exception error)
            {
                // Event handling only touches queue state; reaching here is a defect. Fail this socket only.
                context.Abort(error);
            }
        }
    }

    private static bool CloseNow(int socket)
    {
        try
        {
            SocketOptionCodec.Set(socket, SrtOptions.Linger, new SrtLinger(false, 0));
        }
        catch (SrtException)
        {
            // libsrt refuses options on a broken socket, which it never lingers anyway.
        }

        return NativeMethods.srt_close(socket) != NativeConstants.SRT_ERROR;
    }

    /// <summary>Returns when to give up draining the socket, or zero when it can be closed now.</summary>
    private static long GetDrainDeadline(int socket)
    {
        SrtLinger linger;

        try
        {
            linger = SocketOptionCodec.Get(socket, SrtOptions.Linger);
        }
        catch (SrtException)
        {
            return 0;
        }

        if (!linger.Enabled || linger.Seconds <= 0 || IsDrained(socket))
        {
            return 0;
        }

        return Stopwatch.GetTimestamp() + (linger.Seconds * Stopwatch.Frequency);
    }

    private static bool IsDrained(int socket)
    {
        return NativeMethods.srt_getsockstate(socket) != SrtSocketState.Connected
            || NativeMethods.srt_getsndbuffer(socket, out _, out UIntPtr bytes) == NativeConstants.SRT_ERROR
            || bytes == UIntPtr.Zero;
    }

    private static bool TryScheduleDrain(int socket, long deadline)
    {
        SrtAsyncEngine engine;

        lock (CreationGate)
        {
            if (shutDown)
            {
                return false;
            }

            try
            {
                engine = instance ??= new();
            }
            catch (Exception)
            {
                // libsrt is shutting down, or the engine could not start; close without draining.
                return false;
            }

            // Counted as a registration, so the engine keeps running (and libsrt started) until the drain ends.
            Interlocked.Increment(ref engine.registered);
        }

        lock (engine.drainGate)
        {
            engine.drains.Add(new(socket, deadline));
            engine.drainCount = engine.drains.Count;
        }

        return true;
    }

    private static void CloseDrained(object? state)
    {
        KeyValuePair<SrtAsyncEngine, int> drain = (KeyValuePair<SrtAsyncEngine, int>)state!;

        try
        {
            CloseNow(drain.Value);
        }
        finally
        {
            Interlocked.Decrement(ref drain.Key.registered);
        }
    }

    /// <summary>Closes drained or expired sockets on the thread pool, or every remaining socket now when forced.</summary>
    private void CloseDrains(bool force, long now)
    {
        List<int>? finished = null;

        lock (drainGate)
        {
            for (int index = drains.Count - 1; index >= 0; index--)
            {
                KeyValuePair<int, long> drain = drains[index];

                if (force || now >= drain.Value || IsDrained(drain.Key))
                {
                    drains.RemoveAt(index);
                    (finished ??= []).Add(drain.Key);
                }
            }

            drainCount = drains.Count;
        }

        if (finished == null)
        {
            return;
        }

        foreach (int socket in finished)
        {
            if (force)
            {
                CloseNow(socket);
                Interlocked.Decrement(ref registered);
            }
            else
            {
                // srt_close takes libsrt locks that transport threads also hold, so keep it off the engine thread.
                ThreadPool.UnsafeQueueUserWorkItem(CloseDrained, new KeyValuePair<SrtAsyncEngine, int>(this, socket));
            }
        }
    }

    private void CheckTimers(long now, long watchdogTicks)
    {
        if (Volatile.Read(ref drainCount) != 0)
        {
            CloseDrains(force: false, now);
        }

        // Clear before scanning, and only set afterwards, so a request made during the scan is not lost.
        Interlocked.Exchange(ref retriesRequested, 0);

        if (busy.IsEmpty)
        {
            return;
        }

        bool retryScheduled = false;

        foreach (KeyValuePair<SrtAsyncContext, bool> entry in busy)
        {
            try
            {
                retryScheduled |= entry.Key.CheckTimers(now, watchdogTicks);
            }
            catch (Exception error)
            {
                entry.Key.Abort(error);
            }
        }

        if (retryScheduled)
        {
            Volatile.Write(ref retriesRequested, 1);
        }
    }

    private bool TryStopWhenIdle()
    {
        lock (CreationGate)
        {
            if (Volatile.Read(ref registered) != 0 || instance != this)
            {
                return false;
            }

            instance = null;

            return true;
        }
    }
}
