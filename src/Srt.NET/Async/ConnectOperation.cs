// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Threading.Tasks.Sources;

namespace Srt.Net.Async;

/// <summary>A pending asynchronous connection attempt, completed by Connect (OUT) or Error readiness.</summary>
internal sealed class ConnectOperation : SrtAsyncOperation, IValueTaskSource
{
    private ManualResetValueTaskSourceCore<bool> core;
    private Exception? error;

    internal ConnectOperation(SrtAsyncContext context) : base(context, sending: true)
    {
        core.RunContinuationsAsynchronously = false;
    }

    internal short Version => core.Version;

    public void GetResult(short token)
    {
        core.GetResult(token);
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
            core.SetException(CreateEndedException());
        }
        else if (error != null)
        {
            core.SetException(error);
        }
        else
        {
            core.SetResult(true);
        }
    }

    protected override bool TryCompleteOperation()
    {
        return SrtAsyncIo.TryCompleteConnect(Context, out error);
    }

    protected override void RecordFailure(Exception failure)
    {
        error = failure;
    }
}
