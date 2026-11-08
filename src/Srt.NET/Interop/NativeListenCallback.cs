// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Runtime.InteropServices;

namespace Srt.Net.Interop;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int NativeListenCallback(IntPtr context, int socket, int handshakeVersion, IntPtr peer, IntPtr streamId);
