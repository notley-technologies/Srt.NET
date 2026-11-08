// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Srt.Net.Interop;

namespace Srt.Net;

/// <summary>Waits for readiness on sockets explicitly registered by the application.</summary>
/// <remarks>
/// Register listeners before calling Listen; libsrt does not replay previously queued listener events.
/// One Wait may run at a time. Add, Update, Remove, and Dispose can run concurrently with Wait.
/// Error events are always monitored. Readiness is a snapshot, not a reservation of data or connections.
/// </remarks>
public sealed unsafe class SrtPoller : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<int, SrtPollRegistration> registrations = new();
    private readonly Dictionary<SrtSocket, SrtPollRegistration> sockets = new();
    private readonly List<SrtPollRegistration> closed = new();
    private NativePollEvent[] nativeEvents = [];
    private SrtPollEvent[] readyEvents = [];
    private int poll = NativeConstants.SRT_ERROR;
    private int waiting;
    private bool nativeWaiting;
    private bool disposed;

    /// <summary>Creates an empty poller and acquires a native runtime lease.</summary>
    public SrtPoller()
    {
        SrtRuntime.Acquire();

        try
        {
            poll = NativeMethods.srt_epoll_create();
            SrtException.ThrowIfError(poll, "create an SRT poller");

            // Concurrent removal may temporarily empty a poll that is already waiting.
            SrtException.ThrowIfError(NativeMethods.srt_epoll_set(poll, NativeConstants.SRT_EPOLL_ENABLE_EMPTY), "configure the SRT poller");
        }
        catch
        {
            if (poll != NativeConstants.SRT_ERROR)
            {
                NativeMethods.srt_epoll_release(poll);
                poll = NativeConstants.SRT_ERROR;
            }

            SrtRuntime.Release();
            throw;
        }
    }

    /// <summary>Releases abandoned native registrations.</summary>
    ~SrtPoller()
    {
        Dispose();
    }

    /// <summary>Whether a Wait is in progress, for tests.</summary>
    internal bool IsWaiting => Volatile.Read(ref waiting) != 0;

    /// <summary>Registers a socket. Register listeners before Listen and callers before Connect.</summary>
    public void Add(SrtSocket socket, SrtPollEvents events)
    {
        if (socket == null)
        {
            throw new ArgumentNullException(nameof(socket));
        }

        int mask = ValidateEvents(events);

        lock (gate)
        {
            ThrowIfDisposed();

            if (socket.State == SrtSocketState.Listening)
            {
                throw new InvalidOperationException("Register the listener before calling Listen so queued connections cannot be missed.");
            }

            if (sockets.ContainsKey(socket))
            {
                throw new InvalidOperationException("The socket is already registered with this poller.");
            }

            EnsureCapacity(registrations.Count + 1);

            // The lease keeps the socket open only while it is subscribed; the registration holds no reference after that.
            using SrtSocketLease lease = socket.Handle.Borrow();
            SrtPollRegistration registration = new(socket, lease.Socket);
            SrtException.ThrowIfError(NativeMethods.srt_epoll_add_usock(poll, registration.SocketId, ref mask), "register an SRT socket");

            try
            {
                registrations.Add(registration.SocketId, registration);
                sockets.Add(socket, registration);
            }
            catch
            {
                EpollSubscription.Remove(poll, registration.SocketId);
                registrations.Remove(registration.SocketId);
                sockets.Remove(socket);

                throw;
            }
        }
    }

    /// <summary>Changes the readiness interests for a registered socket.</summary>
    public void Update(SrtSocket socket, SrtPollEvents events)
    {
        int mask = ValidateEvents(events);

        lock (gate)
        {
            ThrowIfDisposed();
            SrtPollRegistration registration = Find(socket);

            // Keep the socket open during the native call; a disposed socket reports its Closed event instead.
            using SrtSocketLease lease = socket.Handle.Borrow();
            SrtException.ThrowIfError(NativeMethods.srt_epoll_update_usock(poll, registration.SocketId, ref mask), "update SRT readiness interests");
        }
    }

    /// <summary>Unregisters a socket without closing it.</summary>
    public void Remove(SrtSocket socket)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            Remove(Find(socket));
        }
    }

    /// <summary>Returns ready sockets, or an empty array when the timeout expires or nothing is registered.</summary>
    /// <remarks>Disposed sockets produce one Closed event and are removed automatically.</remarks>
    public SrtPollEvent[] Wait(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        TimeSpan duration = ValidateTimeout(timeout);
        EnterWait();
        SrtPollEvent[]? buffer = null;
        int written = -1;

        try
        {
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                buffer = readyEvents;

                if (registrations.Count == 0)
                {
                    return [];
                }
            }

            int count = WaitCore(buffer, duration, cancellationToken);
            written = count;

            return count == 0 ? [] : buffer.AsSpan(0, count).ToArray();
        }
        finally
        {
            if (buffer != null)
            {
                Array.Clear(buffer, 0, written < 0 ? buffer.Length : written);
            }

            Volatile.Write(ref waiting, 0);
        }
    }

    /// <summary>Writes ready sockets into a nonempty destination and returns the number written.</summary>
    /// <remarks>
    /// Only the returned prefix is modified. A full destination may leave more events for subsequent waits.
    /// Closed events are removed only when delivered. Readiness remains active until the application handles it.
    /// </remarks>
    public int Wait(Span<SrtPollEvent> destination, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (destination.IsEmpty)
        {
            throw new ArgumentException("Provide space for at least one readiness event.", nameof(destination));
        }

        TimeSpan duration = ValidateTimeout(timeout);
        EnterWait();

        try
        {
            return WaitCore(destination, duration, cancellationToken);
        }
        finally
        {
            Volatile.Write(ref waiting, 0);
        }
    }

    private int WaitCore(Span<SrtPollEvent> destination, TimeSpan duration, CancellationToken cancellationToken)
    {
        long started = duration == Timeout.InfiniteTimeSpan ? 0 : Stopwatch.GetTimestamp();
        bool attempted = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativePollEvent[] buffer;
            int capacity;
            int pollId;
            long milliseconds;

            lock (gate)
            {
                ThrowIfDisposed();
                int count = CollectClosed(destination);

                if (count != 0 || registrations.Count == 0)
                {
                    return count;
                }

                double remaining = duration == Timeout.InfiniteTimeSpan ? 50
                    : duration.TotalMilliseconds - ((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);

                if (attempted && remaining <= 0)
                {
                    return 0;
                }

                buffer = nativeEvents;
                capacity = Math.Min(destination.Length, buffer.Length);
                pollId = poll;
                milliseconds = (long)Math.Max(0, Math.Min(50, Math.Ceiling(remaining)));
                nativeWaiting = true;
            }

            try
            {
                int count;

                fixed (NativePollEvent* pointer = buffer)
                {
                    count = NativeMethods.srt_epoll_uwait_buffer(pollId, pointer, capacity, milliseconds);
                }

                attempted = true;
                SrtException.ThrowIfError(count, "wait for SRT readiness");
                cancellationToken.ThrowIfCancellationRequested();

                lock (gate)
                {
                    ThrowIfDisposed();
                    int written = 0;

                    for (int index = 0; index < count; index++)
                    {
                        NativePollEvent nativeEvent = buffer[index];

                        if (registrations.TryGetValue(nativeEvent.Socket, out SrtPollRegistration? registration))
                        {
                            bool isClosed = registration.Socket.Handle.IsCloseRequested;
                            destination[written++] = new(registration.Socket, isClosed ? SrtPollEvents.Closed : (SrtPollEvents)nativeEvent.Events);

                            if (isClosed)
                            {
                                Remove(registration);
                            }
                        }
                    }

                    if (written != 0)
                    {
                        return written;
                    }
                }
            }
            finally
            {
                lock (gate)
                {
                    nativeWaiting = false;

                    if (disposed)
                    {
                        Monitor.PulseAll(gate);
                    }

                    if (disposed)
                    {
                        Release();
                    }
                }
            }
        }
    }

    /// <summary>Interrupts a pending Wait and waits for its current native call to finish before releasing resources.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;

            while (nativeWaiting)
            {
                Monitor.Wait(gate);
            }

            Release();
        }

        GC.SuppressFinalize(this);
    }

    private void Release()
    {
        if (poll == NativeConstants.SRT_ERROR)
        {
            return;
        }

        NativeMethods.srt_epoll_release(poll);
        poll = NativeConstants.SRT_ERROR;
        registrations.Clear();
        sockets.Clear();
        readyEvents = [];
        nativeEvents = [];
        SrtRuntime.Release();
    }

    private int CollectClosed(Span<SrtPollEvent> destination)
    {
        int written = 0;

        try
        {
            foreach (SrtPollRegistration registration in registrations.Values)
            {
                if (registration.Socket.Handle.IsCloseRequested)
                {
                    destination[written++] = new(registration.Socket, SrtPollEvents.Closed);
                    closed.Add(registration);

                    if (written == destination.Length)
                    {
                        break;
                    }
                }
            }

            foreach (SrtPollRegistration registration in closed)
            {
                Remove(registration);
            }

            return written;
        }
        finally
        {
            closed.Clear();
        }
    }

    private void EnsureCapacity(int required)
    {
        if (nativeEvents.Length >= required)
        {
            return;
        }

        int capacity = Math.Max(required, nativeEvents.Length <= int.MaxValue / 2 ? nativeEvents.Length * 2 : required);
        capacity = Math.Max(4, capacity);
        NativePollEvent[] nativeBuffer = new NativePollEvent[capacity];
        SrtPollEvent[] readyBuffer = new SrtPollEvent[capacity];
        closed.Capacity = capacity;
        nativeEvents = nativeBuffer;
        readyEvents = readyBuffer;
    }

    private void EnterWait()
    {
        if (Interlocked.Exchange(ref waiting, 1) != 0)
        {
            throw new InvalidOperationException("Only one Wait may run on a poller at a time.");
        }
    }

    private static TimeSpan ValidateTimeout(TimeSpan? timeout)
    {
        TimeSpan duration = timeout ?? Timeout.InfiniteTimeSpan;

        if (duration < TimeSpan.Zero && duration != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        return duration;
    }

    private static int ValidateEvents(SrtPollEvents events)
    {
        if (events == SrtPollEvents.None || (events & ~(SrtPollEvents.Read | SrtPollEvents.Write | SrtPollEvents.Error)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(events));
        }

        return (int)(events | SrtPollEvents.Error);
    }

    private SrtPollRegistration Find(SrtSocket socket)
    {
        if (socket == null)
        {
            throw new ArgumentNullException(nameof(socket));
        }

        if (sockets.TryGetValue(socket, out SrtPollRegistration? registration))
        {
            return registration;
        }

        throw new ArgumentException("The socket is not registered with this poller.", nameof(socket));
    }

    private void Remove(SrtPollRegistration registration)
    {
        // A disposed socket may already be closed natively; EpollSubscription skips sockets libsrt has closed.
        SrtException.ThrowIfError(EpollSubscription.Remove(poll, registration.SocketId), "remove a socket from polling");
        registrations.Remove(registration.SocketId);
        sockets.Remove(registration.Socket);
    }

    private void ThrowIfDisposed()
    {
        if (disposed || poll == NativeConstants.SRT_ERROR)
        {
            throw new ObjectDisposedException(nameof(SrtPoller));
        }
    }
}
