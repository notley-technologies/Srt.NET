// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Srt.Net.Interop;

/// <summary>Operation state for one socket: a receive-side and a send-side queue, shared by synchronous and asynchronous calls.</summary>
/// <remarks>
/// Receives and accepts use the receive queue (IN readiness); sends and connects use the send queue (OUT readiness).
/// Error readiness wakes both, and the next native attempt reports the error. Every entry point tries the native
/// call inline first when its queue is idle, so ready operations complete without queuing or allocating.
/// </remarks>
internal sealed class SrtAsyncContext
#if NET
    : IThreadPoolWorkItem
#endif
{
    private readonly object rootGate = new();
    private TransferOperation? cachedReceive;
    private TransferOperation? cachedSend;
    private SyncOperation? cachedSyncReceive;
    private SyncOperation? cachedSyncSend;
    private Exception? fault;
    private int busyQueues;
    private int pendingEvents;
    private int scheduled;
    private int aborted;
    private SrtAsyncEngine? engine;

    internal SrtAsyncContext(SrtSocketHandle handle, int socketId)
    {
        Handle = handle;
        SocketId = socketId;
        ReceiveQueue = new(this);
        SendQueue = new(this);
    }

    internal SrtSocketHandle Handle
    {
        get;
    }

    internal int SocketId
    {
        get;
    }

    internal SrtOperationQueue ReceiveQueue
    {
        get;
    }

    internal SrtOperationQueue SendQueue
    {
        get;
    }

    internal Exception? Fault => Volatile.Read(ref fault);

    internal SrtOperationQueue QueueFor(bool sending)
    {
        return sending ? SendQueue : ReceiveQueue;
    }

    /// <summary>Subscribes the socket with the engine. Call once, while holding a lease on the socket.</summary>
    internal void Register()
    {
        engine = SrtAsyncEngine.Register(this);
    }

    /// <summary>Ends all pending operations and stops event delivery. Safe to call more than once.</summary>
    /// <param name="error">The defect that ended the socket's operations, or null when the socket is being closed.</param>
    internal void Abort(Exception? error = null)
    {
        if (error != null)
        {
            Interlocked.CompareExchange(ref fault, error, null);
        }

        if (Interlocked.Exchange(ref aborted, 1) != 0)
        {
            return;
        }

        // Without an error the socket is being disposed, and closing it removes its subscription.
        engine?.Unregister(this, closing: error == null);
        ReceiveQueue.StopAndAbort();
        SendQueue.StopAndAbort();
    }

    /// <summary>Handles readiness on the engine thread: wakes synchronous callers directly and schedules the rest.</summary>
    internal void OnEvents(int events)
    {
        if ((events & NativeConstants.SRT_EPOLL_ERR) != 0)
        {
            events |= NativeConstants.SRT_EPOLL_IN | NativeConstants.SRT_EPOLL_OUT;
        }

        int remaining = 0;

        if ((events & NativeConstants.SRT_EPOLL_IN) != 0 && ReceiveQueue.ProcessInlineOrGet(inlineOnly: true) != null)
        {
            remaining |= NativeConstants.SRT_EPOLL_IN;
        }

        if ((events & NativeConstants.SRT_EPOLL_OUT) != 0 && SendQueue.ProcessInlineOrGet(inlineOnly: true) != null)
        {
            remaining |= NativeConstants.SRT_EPOLL_OUT;
        }

        if (remaining == 0)
        {
            return;
        }

        InterlockedOr(ref pendingEvents, remaining);

        if (Interlocked.Exchange(ref scheduled, 1) == 0)
        {
#if NET
            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
#else
            ThreadPool.UnsafeQueueUserWorkItem(static context => ((SrtAsyncContext)context!).Execute(), this);
#endif
        }
    }

    /// <summary>Raises synthetic events for timed retries and the watchdog. Runs on the engine thread.</summary>
    /// <returns>True if a timed retry is still scheduled.</returns>
    internal bool CheckTimers(long now, long watchdogTicks)
    {
        bool retryScheduled = false;
        int events = CheckQueueTimers(ReceiveQueue, NativeConstants.SRT_EPOLL_IN, now, watchdogTicks, ref retryScheduled)
            | CheckQueueTimers(SendQueue, NativeConstants.SRT_EPOLL_OUT, now, watchdogTicks, ref retryScheduled);

        if (events != 0)
        {
            OnEvents(events);
        }

        return retryScheduled;
    }

    private static int CheckQueueTimers(SrtOperationQueue queue, int readiness, long now, long watchdogTicks, ref bool retryScheduled)
    {
        long due = queue.RetryDue;

        if (due != 0 && now < due)
        {
            // Keep the engine's short wait until the retry is due, even if the operation is not queued yet. A retry that
            // has backed off further than the engine's idle wait does not need it yet.
            retryScheduled |= due - now <= Stopwatch.Frequency * SrtAsyncEngine.IdleWaitMilliseconds / 1000;

            return 0;
        }

        if (!queue.IsWaiting)
        {
            return 0;
        }

        if (due != 0)
        {
            // The worker re-arms the retry if the send still blocks; keep the short wait so that re-arm is seen promptly.
            queue.CountTimedRetry();
            retryScheduled = true;

            return readiness;
        }

        // A backstop for any readiness change that libsrt does not report as an edge.
        if (now - queue.LastActivity < watchdogTicks)
        {
            return 0;
        }

        queue.CountWatchdogRetry();

        return readiness;
    }

    /// <summary>Called under a queue lock when the queue leaves or returns to Ready.</summary>
    internal void OnQueueBusyChanged(bool busy)
    {
        lock (rootGate)
        {
            busyQueues += busy ? 1 : -1;

            // Pending operations keep the socket reachable through the engine, as in System.Net.Sockets.
            // Idle sockets are referenced weakly, so an undisposed socket can still be finalized.
            if (busy && busyQueues == 1)
            {
                engine?.Root(this);
            }
            else if (!busy && busyQueues == 0)
            {
                engine?.Unroot(this);
            }
        }
    }

    /// <summary>Schedules a retry soon for a send that would block without libsrt signaling a later edge.</summary>
    internal void RequestSendRetry()
    {
        bool first = SendQueue.RequestRetry();
        engine?.RequestRetries(first);
    }

    /// <summary>Keeps the engine's short wait for a while, so the next blocked send's first retry runs on time.</summary>
    internal void RefreshRetryWait()
    {
        engine?.RequestRetries(first: true);
    }

#if NET
    void IThreadPoolWorkItem.Execute()
    {
        Execute();
    }
#endif

    /// <summary>Runs an asynchronous head operation on this thread and delivers its result.</summary>
    internal void Process(SrtAsyncOperation operation)
    {
        SrtOperationResult result = operation.Queue.TryCompleteQueued(operation);

        if (result == SrtOperationResult.Pending)
        {
            return;
        }

        // Waits for a running cancellation callback, so it cannot affect a later reuse of the operation.
        operation.Registration.Dispose();

        if (result == SrtOperationResult.Completed)
        {
            operation.OnCompleted(result);
        }
    }

    internal SrtOperationResult ExecuteSync(SyncOperation operation, int observedSequence, TimeSpan timeout, CancellationToken cancellationToken)
    {
        SrtOperationQueue queue = operation.Queue;
        SrtOperationResult result = queue.StartOperation(operation, observedSequence, default);

        if (result != SrtOperationResult.Pending)
        {
            return result;
        }

        long started = timeout == Timeout.InfiniteTimeSpan ? 0 : Stopwatch.GetTimestamp();

        while (true)
        {
            int remaining = RemainingMilliseconds(timeout, started);
            bool signaled;

            try
            {
                signaled = remaining != 0 && operation.SyncEvent!.Wait(remaining, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                queue.CancelSyncAndContinue(operation);

                throw;
            }

            if (!signaled)
            {
                queue.CancelSyncAndContinue(operation);

                throw new TimeoutException("The SRT operation timed out.");
            }

            // Reset before running, so a wake-up that arrives during the attempt is not lost.
            operation.SyncEvent!.Reset();
            result = queue.TryCompleteQueued(operation);

            if (result != SrtOperationResult.Pending)
            {
                return result;
            }
        }
    }

    internal ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        NativeMessageControl control = default;

        if (ReceiveQueue.IsReady(out int observed)
            && SrtAsyncIo.TryTransfer(Handle, default, buffer.Span, false, false, ref control, out int bytes, out Exception? error, out _))
        {
            return error == null ? new(bytes) : new(Task.FromException<int>(error));
        }

        TransferOperation operation = Rent(sending: false);
        operation.Prepare(default, buffer, message: false, in control);
        short version = operation.Version;
        Start(operation, observed, cancellationToken);

        return new(operation, version);
    }

    internal ValueTask<SrtMessageResult> ReceiveMessageAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        NativeMessageControl control = NativeMessageControl.Default;

        if (ReceiveQueue.IsReady(out int observed)
            && SrtAsyncIo.TryTransfer(Handle, default, buffer.Span, false, true, ref control, out int bytes, out Exception? error, out _))
        {
            return error == null ? new(new SrtMessageResult(bytes, in control)) : new(Task.FromException<SrtMessageResult>(error));
        }

        TransferOperation operation = Rent(sending: false);
        operation.Prepare(default, buffer, message: true, NativeMessageControl.Default);
        short version = operation.Version;
        Start(operation, observed, cancellationToken);

        return new(operation, version);
    }

    internal ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        NativeMessageControl control = default;
        bool retrySoon = false;

        if (SendQueue.IsReady(out int observed)
            && SrtAsyncIo.TryTransfer(Handle, buffer.Span, default, true, false, ref control, out int bytes, out Exception? error, out retrySoon))
        {
            return error == null ? new(bytes) : new(Task.FromException<int>(error));
        }

        TransferOperation operation = Rent(sending: true);
        operation.Prepare(buffer, default, message: false, in control);
        short version = operation.Version;

        if (retrySoon)
        {
            RequestSendRetry();
        }

        Start(operation, observed, cancellationToken);

        return new(operation, version);
    }

    internal ValueTask<SrtMessageResult> SendMessageAsync(ReadOnlyMemory<byte> buffer, in NativeMessageControl template, CancellationToken cancellationToken)
    {
        NativeMessageControl control = template;
        bool retrySoon = false;

        if (SendQueue.IsReady(out int observed)
            && SrtAsyncIo.TryTransfer(Handle, buffer.Span, default, true, true, ref control, out int bytes, out Exception? error, out retrySoon))
        {
            return error == null ? new(new SrtMessageResult(bytes, in control)) : new(Task.FromException<SrtMessageResult>(error));
        }

        TransferOperation operation = Rent(sending: true);
        operation.Prepare(buffer, default, message: true, in template);
        short version = operation.Version;

        if (retrySoon)
        {
            RequestSendRetry();
        }

        Start(operation, observed, cancellationToken);

        return new(operation, version);
    }

    internal ValueTask<SrtSocket> AcceptAsync(CancellationToken cancellationToken)
    {
        if (ReceiveQueue.IsReady(out int observed) && SrtAsyncIo.TryAccept(Handle, out SrtSocket? accepted, out Exception? error))
        {
            return error == null ? new(accepted!) : new(Task.FromException<SrtSocket>(error));
        }

        AcceptOperation operation = new(this);
        short version = operation.Version;
        Start(operation, observed, cancellationToken);

        return new(operation, version);
    }

    internal ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        if (SendQueue.IsReady(out int observed) && SrtAsyncIo.TryCompleteConnect(Handle, out Exception? error))
        {
            return error == null ? default : new(Task.FromException(error));
        }

        ConnectOperation operation = new(this);
        short version = operation.Version;
        Start(operation, observed, cancellationToken);

        return new(operation, version);
    }

    internal void Return(TransferOperation operation)
    {
        if (operation.Sending)
        {
            Volatile.Write(ref cachedSend, operation);
        }
        else
        {
            Volatile.Write(ref cachedReceive, operation);
        }
    }

    internal SyncOperation RentSync(bool sending)
    {
        SyncOperation? operation = sending
            ? Interlocked.Exchange(ref cachedSyncSend, null)
            : Interlocked.Exchange(ref cachedSyncReceive, null);

        return operation ?? new(this, sending);
    }

    internal void ReturnSync(SyncOperation operation)
    {
        if (operation.Sending)
        {
            Volatile.Write(ref cachedSyncSend, operation);
        }
        else
        {
            Volatile.Write(ref cachedSyncReceive, operation);
        }
    }

    private static int RemainingMilliseconds(TimeSpan timeout, long started)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return Timeout.Infinite;
        }

        double elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        double remaining = Math.Ceiling(timeout.TotalMilliseconds - elapsed);

        return remaining <= 0 ? 0 : (int)Math.Min(int.MaxValue, remaining);
    }

    private static void InterlockedOr(ref int location, int value)
    {
        int current = Volatile.Read(ref location);

        while (true)
        {
            int previous = Interlocked.CompareExchange(ref location, current | value, current);

            if (previous == current)
            {
                return;
            }

            current = previous;
        }
    }

    private static void Start(SrtAsyncOperation operation, int observed, CancellationToken cancellationToken)
    {
        SrtOperationResult result = operation.Queue.StartOperation(operation, observed, cancellationToken);

        if (result != SrtOperationResult.Pending)
        {
            operation.OnCompleted(result);
        }
    }

    private void Execute()
    {
        // One batch per work item, as in System.Net.Sockets. Clearing the flag before taking the events (with a full
        // fence) lets an event that arrives during this batch, including during a user continuation that runs inline,
        // schedule a new work item instead of waiting behind it. Each queue's lock decides which thread owns an
        // operation, so batches for the same socket can safely overlap; an extra work item may find nothing to do.
        Interlocked.Exchange(ref scheduled, 0);
        int events = Interlocked.Exchange(ref pendingEvents, 0);

        if (events == 0)
        {
            return;
        }

        try
        {
            HandleEvents(events);
        }
        catch (Exception error)
        {
            // Processing records native failures on operations; reaching here is a defect. Fail this socket only.
            Abort(error);
        }
    }

    private void HandleEvents(int events)
    {
        SrtAsyncOperation? receive = (events & NativeConstants.SRT_EPOLL_IN) != 0 ? ReceiveQueue.ProcessInlineOrGet(inlineOnly: false) : null;
        SrtAsyncOperation? send = (events & NativeConstants.SRT_EPOLL_OUT) != 0 ? SendQueue.ProcessInlineOrGet(inlineOnly: false) : null;

        if (receive != null && send != null)
        {
            // Processing both here could delay the second; run the receive on another thread.
            receive.Dispatch();
            Process(send);
        }
        else if (receive != null)
        {
            Process(receive);
        }
        else if (send != null)
        {
            Process(send);
        }
    }

    private TransferOperation Rent(bool sending)
    {
        // One cached operation per direction covers the usual single outstanding operation; extras allocate.
        TransferOperation? operation = sending
            ? Interlocked.Exchange(ref cachedSend, null)
            : Interlocked.Exchange(ref cachedReceive, null);

        return operation ?? new(this, sending);
    }
}
