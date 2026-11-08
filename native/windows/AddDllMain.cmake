# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis
#
# Adds dllmain.c to libsrt's srt_shared target on Windows without changing libsrt's sources.
#
# native/CMakeLists.txt passes this file to the libsrt build as CMAKE_PROJECT_SRT_INCLUDE, so CMake includes it right
# after libsrt's project(SRT) call. srt_shared does not exist yet at that point, so the source is added by a call
# deferred to the end of libsrt's top-level CMakeLists.txt. The build fails if the target is missing.

set(SRTNET_DLLMAIN_SOURCE "${CMAKE_CURRENT_LIST_DIR}/dllmain.c")

function(srtnet_add_dllmain)
    target_sources(srt_shared PRIVATE "${SRTNET_DLLMAIN_SOURCE}")
    # libsrt adds the OpenSSL headers to its object library only.
    target_include_directories(srt_shared PRIVATE "${OPENSSL_INCLUDE_DIR}")
endfunction()

cmake_language(DEFER CALL srtnet_add_dllmain)
