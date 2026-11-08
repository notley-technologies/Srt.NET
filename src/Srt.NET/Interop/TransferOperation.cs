// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Threading.Tasks.Sources;

namespace Srt.Net.Interop;

/// <summary>A reusable pending asynchronous send or receive, awaited as a byte count or as message metadata.</summary>
internal sealed class TransferOperation : SrtAsyncOperation, IValueTaskSource<int>, IValueTaskSource<SrtMessageResult>
{
    private ManualResetValueTaskSourceCore<SrtMessageResult> core;
    private ReadOnlyMemory<byte> source;
    private Memory<byte> destination;
    private bool message;
    private NativeMessageControl template;
    private SrtMessageResult result;
    private Exception? error;
    private bool reusable;

    internal TransferOperation(SrtAsyncContext context, bool sending) : base(context, sending)
    {
        // Completions run on a thread pool thread that owns no locks, so continuations may run inline there.
        core.RunContinuationsAsynchronously = false;
    }

    internal short Version => core.Version;

    internal void Prepare(ReadOnlyMemory<byte> source, Memory<byte> destination, bool message, in NativeMessageControl template)
    {
        ResetState();
        this.source = source;
        this.destination = destination;
        this.message = message;
        this.template = template;
        result = default;
        error = null;
        reusable = false;
    }

    public SrtMessageResult GetResult(short token)
    {
        // Only the valid, completed token may recycle the operation; misuse must not reset an operation in flight.
        bool owned = token == core.Version && core.GetStatus(token) != ValueTaskSourceStatus.Pending;

        try
        {
            return core.GetResult(token);
        }
        finally
        {
            if (owned)
            {
                Release();
            }
        }
    }

    int IValueTaskSource<int>.GetResult(short token)
    {
        return GetResult(token).BytesTransferred;
    }

    public ValueTaskSourceStatus GetStatus(short token)
    {
        return core.GetStatus(token);
    }

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        core.OnCompleted(continuation, state, token, flags);
    }

    internal override void OnCompleted(SrtOperationResult outcome)
    {
        if (outcome != SrtOperationResult.Completed)
        {
            // An ended operation may still be linked in its queue, so it must not return to the cache.
            reusable = false;
            core.SetException(CreateEndedException());

            return;
        }

        reusable = true;

        if (error != null)
        {
            core.SetException(error);
        }
        else
        {
            core.SetResult(result);
        }
    }

    protected override bool TryCompleteOperation()
    {
        NativeMessageControl control = template;

        if (!SrtAsyncIo.TryTransfer(Context.Handle, source.Span, destination.Span, Sending, message, ref control, out int bytes, out error, out bool retrySoon))
        {
            if (retrySoon)
            {
                Context.RequestSendRetry();
            }

            return false;
        }

        if (error == null)
        {
            result = new(bytes, in control);
        }

        return true;
    }

    protected override void RecordFailure(Exception failure)
    {
        error = failure;
    }

    private void Release()
    {
        bool reuse = reusable;

        // Drop references to caller buffers before the operation can be reused or collected.
        source = default;
        destination = default;
        error = null;

        if (reuse)
        {
            Registration.Dispose();
            core.Reset();
            Context.Return(this);
        }
    }
}
