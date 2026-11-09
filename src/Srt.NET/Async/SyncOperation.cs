// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Threading;
using Srt.Net.Interop;

namespace Srt.Net.Async;

/// <summary>A blocked synchronous call queued alongside asynchronous operations.</summary>
/// <remarks>
/// Other threads never run this operation; they only signal <see cref="SrtAsyncOperation.SyncEvent"/> when it reaches
/// the head and may proceed. The blocked caller then runs the native call itself, using buffers it keeps pinned for
/// the whole call, which is why a raw pointer is safe here.
/// </remarks>
internal sealed unsafe class SyncOperation : SrtAsyncOperation
{
    private Kind kind;
    private byte* buffer;
    private int length;
    private bool message;
    private NativeMessageControl template;

    internal SyncOperation(SrtAsyncContext context, bool sending) : base(context, sending)
    {
        SyncEvent = new(false, 0);
    }

    private enum Kind
    {
        Transfer,
        Accept,
        Connect
    }

    internal int Bytes
    {
        get; private set;
    }

    internal NativeMessageControl Control
    {
        get; private set;
    }

    internal SrtSocket? Accepted
    {
        get; private set;
    }

    internal Exception? Error
    {
        get; private set;
    }

    internal void PrepareTransfer(byte* buffer, int length, bool message, in NativeMessageControl template)
    {
        Prepare(Kind.Transfer);
        this.buffer = buffer;
        this.length = length;
        this.message = message;
        this.template = template;
    }

    internal void PrepareAccept()
    {
        Prepare(Kind.Accept);
    }

    internal void PrepareConnect()
    {
        Prepare(Kind.Connect);
    }

    /// <summary>Clears per-call state, including the pinned pointer, after the call returns.</summary>
    internal void Clear()
    {
        buffer = null;
        Accepted = null;
        Error = null;
    }

    internal override void OnCompleted(SrtOperationResult result)
    {
        // The blocked caller observes the outcome directly.
    }

    protected override bool TryCompleteOperation()
    {
        switch (kind)
        {
            case Kind.Accept:
                bool acceptDone = SrtAsyncIo.TryAccept(Context.Handle, out SrtSocket? accepted, out Exception? acceptError);
                Accepted = accepted;
                Error = acceptError;

                return acceptDone;

            case Kind.Connect:
                bool connectDone = SrtAsyncIo.TryCompleteConnect(Context.Handle, out Exception? connectError);
                Error = connectError;

                return connectDone;

            default:
                NativeMessageControl control = template;
                ReadOnlySpan<byte> source = Sending ? new(buffer, length) : default;
                Span<byte> destination = Sending ? default : new(buffer, length);

                if (!SrtAsyncIo.TryTransfer(Context.Handle, source, destination, Sending, message, ref control, out int bytes, out Exception? error, out bool retrySoon))
                {
                    if (retrySoon)
                    {
                        Context.RequestSendRetry();
                    }

                    return false;
                }

                Bytes = bytes;
                Control = control;
                Error = error;

                return true;
        }
    }

    protected override void RecordFailure(Exception error)
    {
        Error = error;
    }

    private void Prepare(Kind operationKind)
    {
        ResetState();
        SyncEvent!.Reset();
        kind = operationKind;
        Bytes = 0;
        Control = default;
        Accepted = null;
        Error = null;
    }
}
