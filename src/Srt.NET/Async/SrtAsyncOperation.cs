// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Threading;

namespace Srt.Net.Async;

/// <summary>A queued operation for one direction of a socket, either asynchronous or a blocked synchronous call.</summary>
/// <remarks>
/// <para>This mirrors the operation state machine of .NET's Unix socket engine. Exactly one party ends each
/// operation: the queue owner that runs it (Waiting to Running, then Complete), or a canceller that wins
/// Waiting to Canceled/Aborted. A cancellation that arrives while the native call runs is recorded and
/// honored only if the call would block; a completed native call always wins.</para>
/// <para>A canceled or aborted operation can stay linked in its queue until the next readiness event,
/// so it is never reused.</para>
/// </remarks>
internal abstract class SrtAsyncOperation
{
    private const int Waiting = 0;
    private const int Running = 1;
    private const int RunningWithPendingCancellation = 2;
    private const int RunningWithPendingAbort = 3;
    private const int Complete = 4;
    private const int Canceled = 5;
    private const int Aborted = 6;

    private int state;

    protected SrtAsyncOperation(SrtAsyncContext context, bool sending)
    {
        Context = context;
        Sending = sending;
    }

    /// <summary>The next operation in the owning queue, guarded by the queue lock.</summary>
    internal SrtAsyncOperation? Next
    {
        get; set;
    }

    /// <summary>Signaled when a synchronous caller should run its own operation; null for asynchronous operations.</summary>
    internal ManualResetEventSlim? SyncEvent
    {
        get; private protected set;
    }

    internal bool Sending
    {
        get;
    }

    internal CancellationTokenRegistration Registration
    {
        get; set;
    }

    /// <summary>Whether this operation ended because it was canceled by its token rather than aborted.</summary>
    internal bool WasCanceled => Volatile.Read(ref state) == Canceled;

    protected SrtAsyncContext Context
    {
        get;
    }

    protected CancellationToken CancellationToken
    {
        get; private set;
    }

    internal SrtOperationQueue Queue => Sending ? Context.SendQueue : Context.ReceiveQueue;

    /// <summary>Runs the native call once, if the operation is still waiting.</summary>
    internal SrtOperationResult TryExecute()
    {
        int previous = Interlocked.CompareExchange(ref state, Running, Waiting);

        if (previous is Canceled or Aborted)
        {
            return SrtOperationResult.Aborted;
        }

        bool completed;

        try
        {
            completed = TryCompleteOperation();
        }
        catch (Exception error)
        {
            // Attempts record failures rather than throw; this only guards against defects.
            RecordFailure(error);
            completed = true;
        }

        if (completed)
        {
            // While Running, the only other writer moves to a pending-cancellation state, which completion overrides.
            Volatile.Write(ref state, Complete);

            return SrtOperationResult.Completed;
        }

        while (true)
        {
            int current = Volatile.Read(ref state);
            int next = current switch
            {
                Running => Waiting,
                RunningWithPendingCancellation => Canceled,
                _ => Aborted
            };

            if (Interlocked.CompareExchange(ref state, next, current) == current)
            {
                if (next == Waiting)
                {
                    return SrtOperationResult.Pending;
                }

                NotifyEnded();

                return SrtOperationResult.Aborted;
            }
        }
    }

    /// <summary>Cancels or aborts the operation. Never takes a queue lock, so it may run under one.</summary>
    /// <returns>True if this call ended the operation and scheduled its notification.</returns>
    internal bool TryCancel(bool abort)
    {
        while (true)
        {
            int current = Volatile.Read(ref state);

            if (current is not (Waiting or Running))
            {
                return false;
            }

            int next = current == Waiting
                ? (abort ? Aborted : Canceled)
                : (abort ? RunningWithPendingAbort : RunningWithPendingCancellation);

            if (Interlocked.CompareExchange(ref state, next, current) == current)
            {
                if (current == Running)
                {
                    // The runner sees the request after its native call, unless the call completes.
                    return false;
                }

                NotifyEnded();

                return true;
            }
        }
    }

    /// <summary>Starts processing this operation after ownership passed to it: wakes a synchronous caller, or schedules work.</summary>
    internal void Dispatch()
    {
        if (SyncEvent != null)
        {
            SyncEvent.Set();

            return;
        }

#if NET
        ThreadPool.UnsafeQueueUserWorkItem(static operation => operation.Context.Process(operation), this, preferLocal: true);
#else
        ThreadPool.UnsafeQueueUserWorkItem(static operation => ((SrtAsyncOperation)operation!).Context.Process((SrtAsyncOperation)operation!), this);
#endif
    }

    /// <summary>Ends an operation that was never enqueued because its queue had stopped.</summary>
    internal void MarkAborted()
    {
        Volatile.Write(ref state, Aborted);
    }

    /// <summary>Hooks up token cancellation. Called with the operation fully linked, possibly under the queue lock.</summary>
    internal void RegisterCancellation(CancellationToken token)
    {
        if (!token.CanBeCanceled)
        {
            return;
        }

        CancellationToken = token;

        // The callback may run synchronously here. TryCancel never takes the queue lock.
#if NET
        Registration = token.UnsafeRegister(static state => ((SrtAsyncOperation)state!).TryCancel(abort: false), this);
#else
        if (ExecutionContext.IsFlowSuppressed())
        {
            Registration = token.Register(static state => ((SrtAsyncOperation)state!).TryCancel(abort: false), this);
        }
        else
        {
            using (ExecutionContext.SuppressFlow())
            {
                Registration = token.Register(static state => ((SrtAsyncOperation)state!).TryCancel(abort: false), this);
            }
        }
#endif
    }

    /// <summary>Creates the exception for an operation that ended without a native result.</summary>
    internal Exception CreateEndedException()
    {
        if (WasCanceled)
        {
            return new OperationCanceledException(CancellationToken);
        }

        Exception? fault = Context.Fault;

        return fault == null
            ? new ObjectDisposedException(nameof(SrtSocket))
            : new InvalidOperationException("The SRT async engine failed while processing this socket.", fault);
    }

    /// <summary>Delivers the outcome to an asynchronous awaiter. Runs on a thread pool thread or the initiating thread.</summary>
    internal abstract void OnCompleted(SrtOperationResult result);

    /// <summary>Resets shared state before the operation is reused.</summary>
    protected void ResetState()
    {
        Volatile.Write(ref state, Waiting);
        Next = null;
        CancellationToken = default;
        Registration = default;
    }

    /// <summary>Attempts the native operation once. Returns false only when it would block. Must not throw.</summary>
    protected abstract bool TryCompleteOperation();

    /// <summary>Records an unexpected failure as the operation's outcome.</summary>
    protected abstract void RecordFailure(Exception error);

    private void NotifyEnded()
    {
        if (SyncEvent != null)
        {
            SyncEvent.Set();

            return;
        }

        // Cancellation can run on the user's thread inside CancellationTokenSource.Cancel, or under a queue lock.
        // Deliver the outcome from the thread pool so no user continuation runs there.
#if NET
        ThreadPool.UnsafeQueueUserWorkItem(static operation => operation.OnCompleted(SrtOperationResult.Aborted), this, preferLocal: false);
#else
        ThreadPool.UnsafeQueueUserWorkItem(static operation => ((SrtAsyncOperation)operation!).OnCompleted(SrtOperationResult.Aborted), this);
#endif
    }
}
