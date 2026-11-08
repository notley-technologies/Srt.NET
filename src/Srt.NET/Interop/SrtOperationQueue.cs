// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Diagnostics;
using System.Threading;

namespace Srt.Net.Interop;

/// <summary>Orders the operations for one direction of a socket and reacts to edge-triggered readiness.</summary>
/// <remarks>
/// <para>This follows .NET's Unix socket engine. libsrt reports an edge only when readiness changes from clear to set,
/// and a would-block result clears it again. The sequence number counts edges seen while no operation was waiting,
/// so an operation that would block retries if an edge arrived after its attempt began, instead of waiting for an
/// edge that was already consumed.</para>
/// <para>States: Ready (data may be available; queue empty), Waiting (the head operation would block; queue not
/// empty), Processing (one thread owns running the head; queue not empty), Stopped (disposed; queue empty). While
/// Processing, ownership passes from one operation to the next without returning to Ready.</para>
/// <para>The lock guards only queue state. It is never held around native calls, user code, or anything that takes
/// another queue's lock.</para>
/// </remarks>
internal sealed class SrtOperationQueue
{
    private const int Ready = 0;
    private const int Waiting = 1;
    private const int Processing = 2;
    private const int Stopped = 3;

    // Consecutive timed retries of a blocked operation back off up to the watchdog's default interval, so a send stuck
    // behind a peer that stops reading costs a few attempts per second.
    private const int InitialRetryDelayMilliseconds = 10;
    private const int MaximumRetryDelayMilliseconds = 250;

    private readonly object gate = new();
    private readonly SrtAsyncContext context;
    private int state;
    private int sequence;
    private int timedRetries;
    private int watchdogRetries;
    private bool nextMayBeInline;
    private long lastActivity;
    private long retryDue;
    private int retryDelayMilliseconds;
    private SrtAsyncOperation? head;
    private SrtAsyncOperation? tail;

    internal SrtOperationQueue(SrtAsyncContext context)
    {
        this.context = context;
    }

    /// <summary>Whether the head is a synchronous operation, so the engine thread can wake it directly.</summary>
    /// <remarks>Read without the lock; a stale value only routes the event through the thread pool.</remarks>
    internal bool NextMayBeInline => Volatile.Read(ref nextMayBeInline);

    internal bool IsWaiting => Volatile.Read(ref state) == Waiting;

    /// <summary>When the queue last waited or saw an event, as a Stopwatch timestamp.</summary>
    internal long LastActivity => Volatile.Read(ref lastActivity);

    /// <summary>When a timed retry is due, as a Stopwatch timestamp, or zero.</summary>
    internal long RetryDue => Volatile.Read(ref retryDue);

    /// <summary>Returns whether a caller may attempt the native operation inline.</summary>
    /// <param name="observedSequence">The edge count to pass to <see cref="StartOperation"/>; read before the attempt.</param>
    internal bool IsReady(out int observedSequence)
    {
        // State and sequence are read without the lock. If the caller sees an old non-Ready state with a
        // sequence that a drain has already advanced, the decrement forces StartOperation to retry.
        int current = Volatile.Read(ref state);
        observedSequence = Volatile.Read(ref sequence);
        bool ready = current == Ready;

        if (!ready)
        {
            observedSequence--;
        }

        return ready;
    }

    /// <summary>Enqueues an operation whose inline attempt would block, retrying inline if an edge arrived meanwhile.</summary>
    internal SrtOperationResult StartOperation(SrtAsyncOperation operation, int observedSequence, CancellationToken cancellationToken)
    {
        while (true)
        {
            bool stopped = false;

            lock (gate)
            {
                if (state == Ready && observedSequence != sequence)
                {
                    observedSequence = sequence;
                }
                else if (state == Stopped)
                {
                    stopped = true;
                }
                else
                {
                    if (state == Ready)
                    {
                        state = Waiting;
                        lastActivity = Stopwatch.GetTimestamp();
                        context.OnQueueBusyChanged(busy: true);
                    }

                    if (tail == null)
                    {
                        head = operation;
                        nextMayBeInline = operation.SyncEvent != null;
                    }
                    else
                    {
                        tail.Next = operation;
                    }

                    tail = operation;

                    // Registration may cancel synchronously; that only changes the operation's state.
                    operation.RegisterCancellation(cancellationToken);

                    return SrtOperationResult.Pending;
                }
            }

            if (stopped)
            {
                operation.MarkAborted();

                return SrtOperationResult.Aborted;
            }

            // The operation is not linked yet, so run it directly.
            SrtOperationResult result = operation.TryExecute();

            if (result != SrtOperationResult.Pending)
            {
                return result;
            }
        }
    }

    /// <summary>How often the retry timer has retried this queue, for tests.</summary>
    internal int TimedRetries => Volatile.Read(ref timedRetries);

    /// <summary>How often the watchdog has retried this queue, for tests.</summary>
    internal int WatchdogRetries => Volatile.Read(ref watchdogRetries);

    internal void CountTimedRetry()
    {
        Interlocked.Increment(ref timedRetries);
    }

    internal void CountWatchdogRetry()
    {
        Interlocked.Increment(ref watchdogRetries);
    }

    /// <summary>Handles a readiness event for this direction.</summary>
    /// <param name="inlineOnly">True on the engine thread: wake a synchronous head, but never take ownership of an asynchronous one.</param>
    /// <returns>An asynchronous head operation the caller must process (or, when <paramref name="inlineOnly"/>, schedule); otherwise null.</returns>
    internal SrtAsyncOperation? ProcessInlineOrGet(bool inlineOnly)
    {
        SrtAsyncOperation operation;

        lock (gate)
        {
            lastActivity = Stopwatch.GetTimestamp();

            if (state is Ready or Processing)
            {
                sequence++;

                return null;
            }

            if (state != Waiting)
            {
                return null;
            }

            operation = head!;

            if (inlineOnly && operation.SyncEvent == null)
            {
                return operation;
            }

            state = Processing;
            retryDue = 0;
        }

        if (operation.SyncEvent != null)
        {
            // Hand ownership to the blocked caller, which runs its own operation.
            operation.SyncEvent.Set();

            return null;
        }

        return operation;
    }

    /// <summary>Runs the head operation. Called by the owner while the queue is Processing.</summary>
    internal SrtOperationResult TryCompleteQueued(SrtAsyncOperation operation)
    {
        int observedSequence;

        lock (gate)
        {
            if (state == Stopped)
            {
                return SrtOperationResult.Aborted;
            }

            if (state != Processing || operation != head)
            {
                // Only the owner of the head operation may run it. A synchronous caller can be woken by a late hand-off
                // signal meant for an earlier call; it then simply waits again.
                return SrtOperationResult.Pending;
            }

            observedSequence = sequence;
        }

        SrtOperationResult result;

        while (true)
        {
            result = operation.TryExecute();

            if (result != SrtOperationResult.Pending)
            {
                break;
            }

            lock (gate)
            {
                if (state == Stopped)
                {
                    return SrtOperationResult.Aborted;
                }

                if (observedSequence != sequence)
                {
                    // An event arrived during the native call; it may be the edge this operation needs.
                    observedSequence = sequence;

                    continue;
                }

                state = Waiting;
                lastActivity = Stopwatch.GetTimestamp();

                return SrtOperationResult.Pending;
            }
        }

        SrtAsyncOperation? next = null;
        bool retried;

        lock (gate)
        {
            retried = retryDelayMilliseconds != 0;

            // A stopped queue was detached and aborted by StopAndAbort, so the operation is no longer linked.
            if (state != Stopped)
            {
                head = operation.Next;

                if (head == null)
                {
                    tail = null;
                    nextMayBeInline = false;
                    state = Ready;
                    retryDelayMilliseconds = 0;
                    sequence++;
                    context.OnQueueBusyChanged(busy: false);
                }
                else
                {
                    // Ownership passes to the next operation; the queue stays Processing.
                    retryDelayMilliseconds = 0;
                    next = head;
                    nextMayBeInline = next.SyncEvent != null;
                }
            }
        }

        // A send that needed timed retries is usually followed by another that blocks; keep the engine ready for it.
        if (retried)
        {
            context.RefreshRetryWait();
        }

        // Start the next operation before the completed one's continuation can run.
        next?.Dispatch();

        return result;
    }

    /// <summary>Removes a synchronous operation after its caller timed out or was canceled.</summary>
    internal void CancelSyncAndContinue(SrtAsyncOperation operation)
    {
        SrtAsyncOperation? next = null;

        lock (gate)
        {
            if (state == Stopped)
            {
                return;
            }

            if (head == operation)
            {
                head = operation.Next;

                if (head == null)
                {
                    tail = null;
                    nextMayBeInline = false;
                }
                else
                {
                    nextMayBeInline = head.SyncEvent != null;

                    // The successor starts its own retries, and a backed-off retry due for the canceled operation runs now.
                    retryDelayMilliseconds = 0;

                    if (retryDue != 0)
                    {
                        Volatile.Write(ref retryDue, Stopwatch.GetTimestamp());
                    }
                }

                if (state == Processing)
                {
                    // Ownership was handed to this caller; pass it on.
                    if (head == null)
                    {
                        state = Ready;
                        retryDelayMilliseconds = 0;
                        sequence++;
                        context.OnQueueBusyChanged(busy: false);
                    }
                    else
                    {
                        retryDelayMilliseconds = 0;
                        next = head;
                    }
                }
                else if (head == null)
                {
                    state = Ready;
                    retryDelayMilliseconds = 0;
                    sequence++;
                    context.OnQueueBusyChanged(busy: false);
                }
            }
            else
            {
                SrtAsyncOperation? previous = head;

                while (previous != null && previous.Next != operation)
                {
                    previous = previous.Next;
                }

                if (previous != null)
                {
                    previous.Next = operation.Next;

                    if (tail == operation)
                    {
                        tail = previous;
                    }
                }
            }
        }

        next?.Dispatch();
    }

    /// <summary>Aborts pending operations and rejects new ones. Safe to call more than once.</summary>
    internal void StopAndAbort()
    {
        lock (gate)
        {
            if (state == Stopped)
            {
                return;
            }

            if (state != Ready)
            {
                context.OnQueueBusyChanged(busy: false);
            }

            state = Stopped;
            nextMayBeInline = false;

            for (SrtAsyncOperation? operation = head; operation != null; operation = operation.Next)
            {
                // Disposing waits for a running cancellation callback, which never takes this lock.
                operation.Registration.Dispose();
                operation.TryCancel(abort: true);
            }

            head = null;
            tail = null;
        }
    }

    /// <summary>Requests a retry soon even without an edge. Used when libsrt will not signal one.</summary>
    /// <returns>True for the first retry since an operation completed, false for a retry that backs off.</returns>
    internal bool RequestRetry()
    {
        lock (gate)
        {
            // A retry requested while the queue is Ready comes from a new operation's inline attempt, so it starts over even
            // if an earlier operation completed inline and left a delay behind.
            bool first = state == Ready || retryDelayMilliseconds == 0;
            retryDelayMilliseconds = first ? InitialRetryDelayMilliseconds : Math.Min(retryDelayMilliseconds * 2, MaximumRetryDelayMilliseconds);
            Volatile.Write(ref retryDue, Stopwatch.GetTimestamp() + (Stopwatch.Frequency * retryDelayMilliseconds / 1000));

            return first;
        }
    }
}
