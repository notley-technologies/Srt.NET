/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Noah Davis
 */

/*
 * DllMain for srt.dll, linked in by AddDllMain.cmake.
 *
 * OpenSSL keeps state for each thread that uses it, such as that thread's random number generators. On Windows it
 * frees that state only when OPENSSL_thread_stop() runs on the exiting thread. A shared libcrypto does this from its
 * own DllMain (crypto/dllmain.c), but that file is not part of the static libcrypto linked into srt.dll. libsrt
 * starts and stops worker threads with outbound connections (each caller gets its own multiplexer), and those threads
 * create key material, so without this function each encrypted outbound connection would leak a few kilobytes.
 *
 * OPENSSL_thread_stop() does nothing on threads that never used OpenSSL, and it is what libcrypto's DllMain calls
 * under the same loader lock. Thread notifications must stay enabled: never call DisableThreadLibraryCalls here.
 */
#include <windows.h>

#include <openssl/crypto.h>

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)instance;
    (void)reserved;

    if (reason == DLL_THREAD_DETACH)
    {
        OPENSSL_thread_stop();
    }

    return TRUE;
}
