// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;

namespace Srt.Net.Interop;

/// <summary>Single nonblocking native attempts shared by inline and engine-driven asynchronous operations.</summary>
/// <remarks>Each method returns false only when the operation would block; otherwise it records a result or an exception.</remarks>
internal static unsafe class SrtAsyncIo
{
    /// <remarks>
    /// The retrySoon output is set when a send would block while libsrt still reports the socket writable. That happens on
    /// a message-API socket, through srt_send as well as srt_sendmsg2, when the message needs more free buffer than is
    /// available; libsrt raises no new writable edge as space frees, so the caller must retry on a timer. In stream and
    /// live modes a send blocks only once libsrt has cleared writability, so the flag stays false.
    /// </remarks>
    internal static bool TryTransfer(SrtSocketHandle handle, ReadOnlySpan<byte> source, Span<byte> destination, bool sending, bool message,
        ref NativeMessageControl control, out int bytes, out Exception? error, out bool retrySoon)
    {
        bytes = 0;
        retrySoon = false;

        if (!TryBorrow(handle, out int socket, out error))
        {
            return true;
        }

        try
        {
            int result;

            fixed (byte* sourcePointer = source)
            fixed (byte* destinationPointer = destination)
            {
                if (message)
                {
                    result = sending
                        ? NativeMethods.srt_sendmsg2(socket, sourcePointer, source.Length, ref control)
                        : NativeMethods.srt_recvmsg2(socket, destinationPointer, destination.Length, ref control);
                }
                else
                {
                    result = sending
                        ? NativeMethods.srt_send(socket, sourcePointer, source.Length)
                        : NativeMethods.srt_recv(socket, destinationPointer, destination.Length);
                }
            }

            if (result != NativeConstants.SRT_ERROR)
            {
                bytes = result;

                return true;
            }

            int code = NativeMethods.srt_getlasterror(out int systemError);

            if (code == (int)(sending ? SrtErrorCode.SendWouldBlock : SrtErrorCode.ReceiveWouldBlock))
            {
                retrySoon = sending && IsWritable(socket);

                return false;
            }

            string operation = (sending, message) switch
            {
                (true, true) => "send an SRT message",
                (true, false) => "send SRT data",
                (false, true) => "receive an SRT message",
                _ => "receive SRT data"
            };
            error = SrtException.LastError(operation, code, systemError);

            return true;
        }
        finally
        {
            handle.DangerousRelease();
        }
    }

    internal static bool TryAccept(SrtSocketHandle handle, out SrtSocket? accepted, out Exception? error)
    {
        accepted = null;

        if (!TryBorrow(handle, out int socket, out error))
        {
            return true;
        }

        try
        {
            // Calling srt_accept on an empty queue logs an error in libsrt, so check readiness first (see IsAcceptReady
            // for the one case libsrt leaves stale). A socket that is not listening still calls it, to report libsrt's error.
            if (!handle.IsAcceptReady(socket) && NativeMethods.srt_getsockstate(socket) == SrtSocketState.Listening)
            {
                return false;
            }

            byte* address = stackalloc byte[128];
            int result;

            while (true)
            {
                int length = 128;
                result = NativeMethods.srt_accept(socket, address, ref length);

                if (result != NativeConstants.SRT_INVALID_SOCK)
                {
                    break;
                }

                int code = NativeMethods.srt_getlasterror(out int systemError);

                if (code == (int)SrtErrorCode.ReceiveWouldBlock)
                {
                    return false;
                }

                // A queued connection that broke before it was accepted is discarded with SRT_ESCLOSED (libsrt logs one
                // error for it). Skip it rather than failing an accept loop because one client disconnected early.
                if (code == (int)SrtErrorCode.SocketClosed && NativeMethods.srt_getsockstate(socket) == SrtSocketState.Listening)
                {
                    if (!handle.IsAcceptReady(socket))
                    {
                        return false;
                    }

                    continue;
                }

                error = SrtException.LastError("accept an SRT connection", code, systemError);

                return true;
            }

            try
            {
                accepted = SrtSocket.FromAccepted(result);
            }
            catch (Exception failure)
            {
                error = failure;
            }

            return true;
        }
        catch (Exception failure)
        {
            error = failure;

            return true;
        }
        finally
        {
            handle.DangerousRelease();
        }
    }

    internal static bool TryCompleteConnect(SrtSocketHandle handle, out Exception? error)
    {
        if (!TryBorrow(handle, out int socket, out error))
        {
            return true;
        }

        try
        {
            SrtSocketState state = NativeMethods.srt_getsockstate(socket);

            if (state == SrtSocketState.Connecting)
            {
                return false;
            }

            if (state != SrtSocketState.Connected)
            {
                error = SrtException.ConnectionFailed("connect the SRT socket", NativeMethods.srt_getrejectreason(socket));
            }

            return true;
        }
        finally
        {
            handle.DangerousRelease();
        }
    }

    private static bool IsWritable(int socket)
    {
        // SRTO_EVENT reports OUT whenever the send buffer has any free space, matching libsrt's writable state.
        int events = 0;
        int length = sizeof(int);

        return NativeMethods.srt_getsockflag(socket, NativeConstants.SRTO_EVENT, (byte*)&events, ref length) != NativeConstants.SRT_ERROR
            && (events & NativeConstants.SRT_EPOLL_OUT) != 0;
    }

    private static bool TryBorrow(SrtSocketHandle handle, out int socket, out Exception? error)
    {
        try
        {
            socket = handle.AcquireReference();
            error = null;

            return true;
        }
        catch (ObjectDisposedException disposed)
        {
            socket = 0;
            error = disposed;

            return false;
        }
    }
}
