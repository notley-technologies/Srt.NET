// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Srt.Net.Interop;

internal sealed class SrtSocketHandle : SafeHandle
{
    private int closeRequested;

    // Listeners keep a level-triggered poll, subscribed before srt_listen, to test for queued connections, so srt_accept
    // (which logs an error on an empty queue) runs only when a connection has arrived. Other waits use the shared engine.
    private int acceptPoll = NativeConstants.SRT_ERROR;

    // Connection metrics, set once when the socket becomes connected while metrics are enabled, and balanced when the
    // handle is released (including by its finalizer).
    private string? connectionRole;
    private long connectedTimestamp;
    private bool connectionCounted;

    internal SrtSocketHandle(int socket) : base(new IntPtr(NativeConstants.SRT_INVALID_SOCK), true)
    {
        SetHandle(new IntPtr(socket));
    }

    public override bool IsInvalid => handle == new IntPtr(NativeConstants.SRT_INVALID_SOCK);

    internal NativeListenCallback? ListenCallback
    {
        get; set;
    }

    internal bool IsCloseRequested => Volatile.Read(ref closeRequested) != 0;

    /// <summary>Rejects new leases. Returns true for the first request. Uses a full fence for publication checks.</summary>
    internal bool RequestClose()
    {
        // SafeHandle.IsClosed stays false while a borrowed reference is active.
        return Interlocked.Exchange(ref closeRequested, 1) == 0;
    }

    internal void CloseSocket()
    {
        RequestClose();
        Dispose();
    }

    /// <summary>Records that the socket is connected, once. Skipped when the handle is already released.</summary>
    internal void MarkConnected(string role)
    {
        bool added = false;

        try
        {
            // While the reference is held, ReleaseHandle cannot run, so it always sees a recorded connection.
            DangerousAddRef(ref added);

            if (Interlocked.CompareExchange(ref connectionRole, role, null) == null)
            {
                connectedTimestamp = Stopwatch.GetTimestamp();
                connectionCounted = SrtMetrics.ConnectionOpened(role);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            if (added)
            {
                DangerousRelease();
            }
        }
    }

    internal SrtSocketLease Borrow()
    {
        return new(this);
    }

    internal int AcquireReference()
    {
        bool added = false;

        try
        {
            if (IsCloseRequested)
            {
                throw new ObjectDisposedException(nameof(SrtSocket));
            }

            DangerousAddRef(ref added);

            if (IsCloseRequested)
            {
                throw new ObjectDisposedException(nameof(SrtSocket));
            }

            return DangerousGetHandle().ToInt32();
        }
        catch
        {
            if (added)
            {
                DangerousRelease();
            }

            throw;
        }
    }

    /// <summary>Returns the listener's level-triggered accept poll, creating it on first use. Call while holding a lease.</summary>
    internal int GetAcceptPoll(int socket)
    {
        int poll = Volatile.Read(ref acceptPoll);

        if (poll != NativeConstants.SRT_ERROR)
        {
            return poll;
        }

        poll = NativeMethods.srt_epoll_create();
        SrtException.ThrowIfError(poll, "create an SRT event poll");
        int events = NativeConstants.SRT_EPOLL_IN | NativeConstants.SRT_EPOLL_ERR;

        if (NativeMethods.srt_epoll_add_usock(poll, socket, ref events) == NativeConstants.SRT_ERROR)
        {
            SrtException error = SrtException.LastError("watch the SRT listener");
            NativeMethods.srt_epoll_release(poll);

            throw error;
        }

        int existing = Interlocked.CompareExchange(ref acceptPoll, poll, NativeConstants.SRT_ERROR);

        if (existing != NativeConstants.SRT_ERROR)
        {
            NativeMethods.srt_epoll_release(poll);

            return existing;
        }

        return poll;
    }

    /// <summary>Returns whether a listener reports a queued connection, without calling srt_accept.</summary>
    /// <remarks>
    /// libsrt logs an error when srt_accept finds no connection. The poll is subscribed before srt_listen, so its
    /// level-triggered readiness is set for every queued connection. libsrt does not clear it when its collector discards
    /// a queued connection that broke before it was accepted, so one accept attempt can still find the queue empty, log
    /// that error once, and go back to waiting; libsrt 1.5 offers no way to read the queue length instead.
    /// </remarks>
    internal bool IsAcceptReady(int socket)
    {
        int poll = GetAcceptPoll(socket);

        return NativeMethods.srt_epoll_uwait(poll, out _, 1, 0) > 0;
    }

    protected override bool ReleaseHandle()
    {
        // The runtime lease belongs to the handle, including finalizer-driven cleanup.
        int socket = handle.ToInt32();
        ReleasePoll(ref acceptPoll);

        // A socket collected without Dispose still has an engine entry; disposal removed it already otherwise.
        SrtAsyncEngine.ForgetReleased(socket);
        bool closed = SrtAsyncEngine.Close(socket);
        GC.KeepAlive(ListenCallback);

        if (connectionRole != null)
        {
            SrtMetrics.ConnectionClosed(connectionRole, connectedTimestamp, connectionCounted);
        }

        SrtRuntime.Release();

        return closed;
    }

    private static void ReleasePoll(ref int poll)
    {
        int released = Interlocked.Exchange(ref poll, NativeConstants.SRT_ERROR);

        if (released != NativeConstants.SRT_ERROR)
        {
            NativeMethods.srt_epoll_release(released);
        }
    }
}
