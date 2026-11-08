// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Runtime.InteropServices;

// The package's runtime assets and the application directory are probed first. After that, look only beside
// Srt.NET.dll and in the safe DLL directories, so on Windows srt.dll is never loaded from the current directory or PATH.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]

namespace Srt.Net.Interop;

internal static unsafe class NativeMethods
{
    private const string Library = "srt";

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void srt_setloglevel(int level);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void srt_setloghandler(IntPtr context, NativeLogCallback? callback);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void srt_setlogflags(int flags);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void srt_addlogfa(int area);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void srt_dellogfa(int area);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void srt_resetlogfa(int[] areas, UIntPtr count);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_startup();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_cleanup();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern uint srt_getversion();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_create_socket();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_close(int socket);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_bind(int socket, byte* address, int length);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_connect(int socket, byte* address, int length);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_listen(int socket, int backlog);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_accept(int socket, byte* address, ref int length);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_listen_callback(int socket, NativeListenCallback? callback, IntPtr context);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_setrejectreason(int socket, int reason);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_getsockname(int socket, byte* address, ref int length);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_getpeername(int socket, byte* address, ref int length);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_setsockflag(int socket, int option, byte* value, int length);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_getsockflag(int socket, int option, byte* value, ref int length);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_send(int socket, byte* buffer, int length);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_recv(int socket, byte* buffer, int length);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void srt_msgctrl_init(out NativeMessageControl control);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_sendmsg2(int socket, byte* buffer, int length, ref NativeMessageControl control);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_recvmsg2(int socket, byte* buffer, int length, ref NativeMessageControl control);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_bistats(int socket, out NativeStatistics statistics, int clear, int instantaneous);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_getsndbuffer(int socket, out UIntPtr blocks, out UIntPtr bytes);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern long srt_time_now();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern long srt_connection_time(int socket);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr srt_strerror(int errorCode, int systemErrorCode);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern SrtSocketState srt_getsockstate(int socket);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_getlasterror(out int systemError);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr srt_getlasterror_str();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_getrejectreason(int socket);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr srt_rejectreason_str(int reason);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_epoll_create();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_epoll_set(int poll, int flags);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_epoll_add_usock(int poll, int socket, ref int events);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_epoll_update_usock(int poll, int socket, ref int events);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_epoll_remove_usock(int poll, int socket);

    [DllImport(Library, EntryPoint = "srt_epoll_uwait", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_epoll_uwait_buffer(int poll, NativePollEvent* events, int capacity, long timeout);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_epoll_uwait(int poll, out NativePollEvent events, int capacity, long timeout);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int srt_epoll_release(int poll);
}

