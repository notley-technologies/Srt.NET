// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Srt.Net.Interop;

internal static unsafe class SocketOptionCodec
{
    private const int StackBufferLimit = 1024;

    // Values set by the application must be valid UTF-8. Values read back can hold peer-supplied bytes (the stream ID of
    // an accepted socket), so reads replace invalid sequences with U+FFFD like SrtConnectionRequest does.
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void Set<T>(int socket, SrtOption<T> option, T value)
    {
        ValidateOption(option, true);

        if (typeof(T) == typeof(string))
        {
            SetString(socket, option, Unsafe.As<T, string>(ref value));
        }
        else if (typeof(T) == typeof(bool))
        {
            SetValue(socket, option, Unsafe.As<T, bool>(ref value) ? (byte)1 : (byte)0);
        }
        else if (typeof(T) == typeof(int) || typeof(T) == typeof(SrtTransmissionType) || typeof(T) == typeof(SrtKeyMaterialState))
        {
            SetValue(socket, option, Unsafe.As<T, int>(ref value));
        }
        else if (typeof(T) == typeof(long))
        {
            SetValue(socket, option, Unsafe.As<T, long>(ref value));
        }
        else if (typeof(T) == typeof(SrtLinger))
        {
            SrtLinger linger = Unsafe.As<T, SrtLinger>(ref value);
            int* parts = stackalloc int[2];
            int length;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ushort* windows = (ushort*)parts;
                windows[0] = (ushort)(linger.Enabled ? 1 : 0);
                windows[1] = (ushort)linger.Seconds;
                length = 4;
            }
            else
            {
                parts[0] = linger.Enabled ? 1 : 0;
                parts[1] = linger.Seconds;
                length = 8;
            }

            SetBytes(socket, option, (byte*)parts, length);
        }
        else
        {
            throw new ArgumentException("Unsupported socket option type.", nameof(option));
        }
    }

    internal static T Get<T>(int socket, SrtOption<T> option)
    {
        ValidateOption(option, false);

        if (typeof(T) == typeof(string))
        {
            string value = GetString(socket, option);

            return Unsafe.As<string, T>(ref value);
        }

        if (typeof(T) == typeof(bool))
        {
            bool value = GetValue<byte, T>(socket, option) != 0;

            return Unsafe.As<bool, T>(ref value);
        }

        if (typeof(T) == typeof(int) || typeof(T) == typeof(SrtTransmissionType) || typeof(T) == typeof(SrtKeyMaterialState))
        {
            int value = GetValue<int, T>(socket, option);

            return Unsafe.As<int, T>(ref value);
        }

        if (typeof(T) == typeof(long))
        {
            long value = GetValue<long, T>(socket, option);

            return Unsafe.As<long, T>(ref value);
        }

        if (typeof(T) == typeof(SrtLinger))
        {
            int* parts = stackalloc int[2];
            parts[0] = 0;
            parts[1] = 0;
            bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            int expected = windows ? 4 : 8;
            int length = GetBytes(socket, option, (byte*)parts, expected);
            ValidateLength(length, expected);
            ushort* shorts = (ushort*)parts;
            SrtLinger value = windows ? new(shorts[0] != 0, shorts[1]) : new(parts[0] != 0, parts[1]);

            return Unsafe.As<SrtLinger, T>(ref value);
        }

        throw new ArgumentException("Unsupported socket option type.", nameof(option));
    }

    private static void SetValue<TValue, TOption>(int socket, SrtOption<TOption> option, TValue value)
        where TValue : unmanaged
    {
        SetBytes(socket, option, (byte*)&value, sizeof(TValue));
    }

    private static TValue GetValue<TValue, TOption>(int socket, SrtOption<TOption> option)
        where TValue : unmanaged
    {
        TValue value = default;
        int length = GetBytes(socket, option, (byte*)&value, sizeof(TValue));
        ValidateLength(length, sizeof(TValue));

        return value;
    }

    private static void SetString<T>(int socket, SrtOption<T> option, string value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (value.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("Socket option strings cannot contain a null character.", nameof(value));
        }

        int length = StrictUtf8.GetByteCount(value);

        if (length > option.MaximumBytes)
        {
            throw new ArgumentException($"{option.Name} accepts at most {option.MaximumBytes} UTF-8 bytes.", nameof(value));
        }

        byte[]? rented = null;
        Span<byte> bytes = length < StackBufferLimit ? stackalloc byte[length + 1]
            : (rented = ArrayPool<byte>.Shared.Rent(length + 1)).AsSpan(0, length + 1);

        try
        {
            fixed (char* text = value)
            fixed (byte* pointer = bytes)
            {
                StrictUtf8.GetBytes(text, value.Length, pointer, length);
                pointer[length] = 0;
                SetBytes(socket, option, pointer, length);
            }
        }
        finally
        {
            if (option.IsSecret)
            {
                bytes.Clear();
            }

            if (rented != null)
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: option.IsSecret);
            }
        }
    }

    private static string GetString<T>(int socket, SrtOption<T> option)
    {
        int capacity = option.MaximumBytes + 1;
        byte[]? rented = null;
        Span<byte> bytes = capacity <= StackBufferLimit ? stackalloc byte[capacity]
            : (rented = ArrayPool<byte>.Shared.Rent(capacity)).AsSpan(0, capacity);
        bytes.Clear();

        try
        {
            fixed (byte* pointer = bytes)
            {
                int length = GetBytes(socket, option, pointer, capacity);

                if (length < 0 || length > capacity)
                {
                    throw new InvalidOperationException("libsrt returned an invalid option length.");
                }

                int terminator = bytes.Slice(0, length).IndexOf((byte)0);

                return Encoding.UTF8.GetString(pointer, terminator < 0 ? length : terminator);
            }
        }
        finally
        {
            if (rented != null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static void SetBytes<T>(int socket, SrtOption<T> option, byte* pointer, int length)
    {
        if (NativeMethods.srt_setsockflag(socket, option.Id, pointer, length) == NativeConstants.SRT_ERROR)
        {
            throw SrtException.LastError($"set {option.Name}");
        }
    }

    private static int GetBytes<T>(int socket, SrtOption<T> option, byte* pointer, int capacity)
    {
        int length = capacity;

        if (NativeMethods.srt_getsockflag(socket, option.Id, pointer, ref length) == NativeConstants.SRT_ERROR)
        {
            throw SrtException.LastError($"read {option.Name}");
        }

        return length;
    }

    private static void ValidateLength(int length, int expected)
    {
        if (length != expected)
        {
            throw new InvalidOperationException("libsrt returned an unexpected option size.");
        }
    }

    private static void ValidateOption<T>(SrtOption<T> option, bool writing)
    {
        if (option == null)
        {
            throw new ArgumentNullException(nameof(option));
        }

        if (writing ? !option.CanWrite : !option.CanRead)
        {
            throw new ArgumentException(writing ? "This option is read-only." : "This option is write-only.", nameof(option));
        }
    }
}
