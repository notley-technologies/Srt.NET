# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis
#
# Portability gate for macOS libsrt.dylib builds, included by StageNative.cmake.
#
# verify_macho(<binary> <rid>) fails the build unless the library:
#   - is a thin dynamic library for the runtime's architecture (arm64 or x86_64);
#   - has the install name SRTNET_MACOS_INSTALL_NAME, which StageNative.cmake sets before it runs this gate;
#   - loads only /usr/lib/libc++.1.dylib and /usr/lib/libSystem.B.dylib (OpenSSL is linked statically);
#   - has no LC_RPATH, so it cannot search other directories for libraries;
#   - targets macOS SRTNET_MACOS_BASELINE or earlier (LC_BUILD_VERSION minos, or LC_VERSION_MIN_MACOSX);
#   - exports only _srt_* symbols, including the SRT API.
# It uses otool, nm and lipo from the Xcode command line tools.

set(SRTNET_MACOS_BASELINE 13.0)
set(SRTNET_MACOS_INSTALL_NAME "@rpath/libsrt.dylib")

function(verify_macho BINARY RID)
    if(RID STREQUAL "osx-arm64")
        set(EXPECTED_ARCH arm64)
    elseif(RID STREQUAL "osx-x64")
        set(EXPECTED_ARCH x86_64)
    else()
        message(FATAL_ERROR "Mach-O verification applies to macOS runtimes only, not ${RID}.")
    endif()

    set(ALLOWED_DYLIBS /usr/lib/libc++.1.dylib /usr/lib/libSystem.B.dylib)
    set(DYLIB_COMMANDS LC_LOAD_DYLIB LC_LOAD_WEAK_DYLIB LC_REEXPORT_DYLIB LC_LAZY_LOAD_DYLIB LC_LOAD_UPWARD_DYLIB)
    find_program(OTOOL_EXECUTABLE otool REQUIRED)
    find_program(NM_EXECUTABLE nm REQUIRED)
    find_program(LIPO_EXECUTABLE lipo REQUIRED)
    set(ERRORS "")

    execute_process(COMMAND "${LIPO_EXECUTABLE}" -archs "${BINARY}"
        OUTPUT_VARIABLE ARCHITECTURES OUTPUT_STRIP_TRAILING_WHITESPACE COMMAND_ERROR_IS_FATAL ANY)

    if(NOT ARCHITECTURES STREQUAL EXPECTED_ARCH)
        list(APPEND ERRORS "contains the architectures '${ARCHITECTURES}', but ${RID} requires only ${EXPECTED_ARCH}")
    endif()

    execute_process(COMMAND "${OTOOL_EXECUTABLE}" -h -v "${BINARY}"
        OUTPUT_VARIABLE HEADER COMMAND_ERROR_IS_FATAL ANY)

    if(NOT HEADER MATCHES "MH_MAGIC_64[^\n]* DYLIB ")
        list(APPEND ERRORS "is not a 64-bit Mach-O dynamic library")
    endif()

    # otool -D prints "<file>:" and then the LC_ID_DYLIB install name, if there is one.
    execute_process(COMMAND "${OTOOL_EXECUTABLE}" -D "${BINARY}"
        OUTPUT_VARIABLE INSTALL_NAME OUTPUT_STRIP_TRAILING_WHITESPACE COMMAND_ERROR_IS_FATAL ANY)
    string(REGEX REPLACE "^[^\n]*:\n?" "" INSTALL_NAME "${INSTALL_NAME}")

    if(NOT INSTALL_NAME STREQUAL SRTNET_MACOS_INSTALL_NAME)
        list(APPEND ERRORS "has the install name '${INSTALL_NAME}' instead of ${SRTNET_MACOS_INSTALL_NAME}")
    endif()

    # otool -l prints each load command as "cmd <name>" followed by indented "<field> <value>" lines.
    execute_process(COMMAND "${OTOOL_EXECUTABLE}" -l "${BINARY}"
        OUTPUT_VARIABLE LOAD_COMMANDS COMMAND_ERROR_IS_FATAL ANY)
    string(REPLACE ";" "," LOAD_COMMANDS "${LOAD_COMMANDS}")
    string(REPLACE "[" "<" LOAD_COMMANDS "${LOAD_COMMANDS}")
    string(REPLACE "]" ">" LOAD_COMMANDS "${LOAD_COMMANDS}")
    string(REPLACE "\n" ";" LOAD_COMMAND_LINES "${LOAD_COMMANDS}")
    set(LOAD_COMMAND "")
    set(DYLIBS "")
    set(RPATHS "")
    set(PLATFORMS "")
    set(MINIMUM_VERSIONS "")

    foreach(LINE IN LISTS LOAD_COMMAND_LINES)
        if(LINE MATCHES "^[ \t]*cmd[ \t]+([A-Z0-9_]+)[ \t]*$")
            set(LOAD_COMMAND "${CMAKE_MATCH_1}")
        elseif(LOAD_COMMAND IN_LIST DYLIB_COMMANDS AND LINE MATCHES "^[ \t]*name[ \t]+(.+)[ \t]+\\(offset [0-9]+\\)[ \t]*$")
            list(APPEND DYLIBS "${CMAKE_MATCH_1}")
        elseif(LOAD_COMMAND STREQUAL "LC_RPATH" AND LINE MATCHES "^[ \t]*path[ \t]+(.+)[ \t]+\\(offset [0-9]+\\)[ \t]*$")
            list(APPEND RPATHS "${CMAKE_MATCH_1}")
        elseif(LOAD_COMMAND STREQUAL "LC_BUILD_VERSION" AND LINE MATCHES "^[ \t]*platform[ \t]+([A-Za-z0-9_]+)[ \t]*$")
            list(APPEND PLATFORMS "${CMAKE_MATCH_1}")
        elseif(LOAD_COMMAND STREQUAL "LC_BUILD_VERSION" AND LINE MATCHES "^[ \t]*minos[ \t]+([0-9.]+)[ \t]*$")
            list(APPEND MINIMUM_VERSIONS "${CMAKE_MATCH_1}")
        elseif(LOAD_COMMAND STREQUAL "LC_VERSION_MIN_MACOSX" AND LINE MATCHES "^[ \t]*version[ \t]+([0-9.]+)[ \t]*$")
            list(APPEND PLATFORMS 1)
            list(APPEND MINIMUM_VERSIONS "${CMAKE_MATCH_1}")
        endif()
    endforeach()

    foreach(DYLIB IN LISTS DYLIBS)
        if(NOT DYLIB IN_LIST ALLOWED_DYLIBS)
            list(APPEND ERRORS "loads ${DYLIB}, which is outside the macOS system library allowlist")
        endif()
    endforeach()

    if(NOT "/usr/lib/libSystem.B.dylib" IN_LIST DYLIBS)
        list(APPEND ERRORS "does not load /usr/lib/libSystem.B.dylib (loads: ${DYLIBS})")
    endif()

    if(NOT RPATHS STREQUAL "")
        list(JOIN RPATHS " " RPATH_TEXT)
        list(APPEND ERRORS "has LC_RPATH entries: ${RPATH_TEXT}")
    endif()

    # otool prints the platform as a number (PLATFORM_MACOS is 1); newer versions may print its name.
    list(LENGTH MINIMUM_VERSIONS VERSION_COUNT)

    if(NOT VERSION_COUNT EQUAL 1)
        list(APPEND ERRORS "has ${VERSION_COUNT} minimum OS version load commands instead of one for macOS")
    else()
        list(GET PLATFORMS 0 PLATFORM)
        list(GET MINIMUM_VERSIONS 0 MINIMUM_VERSION)

        if(NOT PLATFORM MATCHES "^(1|MACOS|macos|macOS)$")
            list(APPEND ERRORS "is built for platform ${PLATFORM}, not macOS")
        endif()

        if(MINIMUM_VERSION VERSION_GREATER SRTNET_MACOS_BASELINE)
            list(APPEND ERRORS "requires macOS ${MINIMUM_VERSION}, newer than the macOS ${SRTNET_MACOS_BASELINE} baseline")
        endif()
    endif()

    # Exported symbols: nm -g (external) -U (defined only) -j (names only).
    execute_process(COMMAND "${NM_EXECUTABLE}" -g -U -j "${BINARY}"
        OUTPUT_VARIABLE SYMBOLS COMMAND_ERROR_IS_FATAL ANY)
    string(REPLACE ";" "," SYMBOLS "${SYMBOLS}")
    string(REPLACE "[" "<" SYMBOLS "${SYMBOLS}")
    string(REPLACE "]" ">" SYMBOLS "${SYMBOLS}")
    string(REPLACE "\n" ";" SYMBOLS "${SYMBOLS}")
    set(EXPORTS "")
    set(FOREIGN_EXPORTS "")

    foreach(SYMBOL IN LISTS SYMBOLS)
        string(STRIP "${SYMBOL}" SYMBOL)

        if(SYMBOL STREQUAL "")
            continue()
        elseif(SYMBOL MATCHES "^_srt_")
            list(APPEND EXPORTS "${SYMBOL}")
        else()
            list(APPEND FOREIGN_EXPORTS "${SYMBOL}")
        endif()
    endforeach()

    list(LENGTH FOREIGN_EXPORTS FOREIGN_EXPORT_COUNT)

    if(FOREIGN_EXPORT_COUNT GREATER 0)
        list(SUBLIST FOREIGN_EXPORTS 0 20 FOREIGN_EXAMPLES)
        list(JOIN FOREIGN_EXAMPLES " " FOREIGN_EXAMPLES)
        list(APPEND ERRORS "exports ${FOREIGN_EXPORT_COUNT} symbols outside _srt_*: ${FOREIGN_EXAMPLES}")
    endif()

    if(NOT "_srt_startup" IN_LIST EXPORTS)
        list(APPEND ERRORS "does not export the SRT API (_srt_startup is missing)")
    endif()

    if(NOT ERRORS STREQUAL "")
        list(JOIN ERRORS "\n  - " ERROR_TEXT)
        message(FATAL_ERROR "${BINARY} is not a portable ${RID} library:\n  - ${ERROR_TEXT}")
    endif()

    list(LENGTH EXPORTS EXPORT_COUNT)
    list(JOIN DYLIBS " " DYLIB_TEXT)
    message(STATUS "Verified ${RID} Mach-O: loads ${DYLIB_TEXT}; minimum macOS ${MINIMUM_VERSION}; "
        "exports ${EXPORT_COUNT} _srt_* symbols")
endfunction()
