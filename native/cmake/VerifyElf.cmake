# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis
#
# Portability gate for Linux libsrt.so builds, included by StageNative.cmake.
#
# verify_elf(<binary> <rid> <readelf> <libc-variable>) fails the build unless the library:
#   - is a 64-bit little-endian shared object for the runtime's architecture;
#   - needs only the C library of its runtime (glibc: libc, libm, libpthread, libdl, librt and the dynamic
#     loader; musl: libc.musl-<arch>) and, for glibc, symbol versions no newer than SRTNET_GLIBC_BASELINE;
#   - needs no libstdc++ or libgcc_s symbol versions (both runtimes must be linked statically);
#   - exports only srt_* symbols;
#   - is linked with full RELRO, a non-executable stack and no text relocations.
# On success it sets <libc-variable> to the C library requirement, for example "glibc 2.28" or "musl".

set(SRTNET_GLIBC_BASELINE 2.28)

function(verify_elf BINARY RID READELF LIBC_VARIABLE)
    if(RID MATCHES "^linux-musl-")
        set(ALLOWED_NEEDED libc.musl-x86_64.so.1 libc.musl-aarch64.so.1)
        set(REQUIRED_LIBC_PATTERN "^libc\\.musl-")
    elseif(RID MATCHES "^linux-")
        set(ALLOWED_NEEDED
            libc.so.6 libm.so.6 libpthread.so.0 libdl.so.2 librt.so.1 ld-linux-x86-64.so.2 ld-linux-aarch64.so.1)
        set(REQUIRED_LIBC_PATTERN "^libc\\.so\\.6$")
    else()
        message(FATAL_ERROR "ELF verification applies to Linux runtimes only, not ${RID}.")
    endif()

    if(RID MATCHES "-arm64$")
        set(EXPECTED_MACHINE "AArch64")
    else()
        set(EXPECTED_MACHINE "Advanced Micro Devices X86-64")
    endif()

    if(READELF STREQUAL "" OR NOT EXISTS "${READELF}")
        find_program(READELF_EXECUTABLE readelf REQUIRED)
        set(READELF "${READELF_EXECUTABLE}")
    endif()

    foreach(REPORT IN ITEMS file-header dynamic program-headers version-info dyn-syms)
        execute_process(COMMAND "${READELF}" --${REPORT} --wide "${BINARY}"
            OUTPUT_VARIABLE OUTPUT COMMAND_ERROR_IS_FATAL ANY)
        # Keep CMake list handling away from brackets and separators in readelf output.
        string(REPLACE ";" "," OUTPUT "${OUTPUT}")
        string(REPLACE "[" "<" OUTPUT "${OUTPUT}")
        string(REPLACE "]" ">" OUTPUT "${OUTPUT}")
        string(MAKE_C_IDENTIFIER "${REPORT}" REPORT_ID)
        set(${REPORT_ID} "${OUTPUT}")
    endforeach()

    set(ERRORS "")

    if(NOT file_header MATCHES "Class:[ \t]+ELF64"
       OR NOT file_header MATCHES "Data:[^\n]*little endian"
       OR NOT file_header MATCHES "Type:[ \t]+DYN "
       OR NOT file_header MATCHES "Machine:[ \t]+${EXPECTED_MACHINE}\n")
        list(APPEND ERRORS "not a 64-bit little-endian ${EXPECTED_MACHINE} shared object")
    endif()

    # Shared library dependencies.
    string(REGEX MATCHALL "\\(NEEDED\\)[^\n]*<[A-Za-z0-9._+-]+>" NEEDED_LINES "${dynamic}")
    set(NEEDED "")
    set(HAS_REQUIRED_LIBC FALSE)

    foreach(LINE IN LISTS NEEDED_LINES)
        string(REGEX REPLACE ".*<([A-Za-z0-9._+-]+)>$" "\\1" LIBRARY "${LINE}")
        list(APPEND NEEDED "${LIBRARY}")

        if(NOT LIBRARY IN_LIST ALLOWED_NEEDED)
            list(APPEND ERRORS "needs ${LIBRARY}, which is outside the ${RID} system library allowlist")
        endif()

        if(LIBRARY MATCHES "${REQUIRED_LIBC_PATTERN}")
            set(HAS_REQUIRED_LIBC TRUE)
        endif()
    endforeach()

    if(NOT HAS_REQUIRED_LIBC)
        list(APPEND ERRORS "does not need the C library of ${RID} (NEEDED: ${NEEDED})")
    endif()

    if(dynamic MATCHES "\\(TEXTREL\\)" OR dynamic MATCHES "\\(FLAGS\\)[^\n]*TEXTREL")
        list(APPEND ERRORS "contains text relocations")
    endif()

    if(NOT dynamic MATCHES "\\(FLAGS\\)[^\n]*BIND_NOW" AND NOT dynamic MATCHES "\\(FLAGS_1\\)[^\n]*NOW")
        list(APPEND ERRORS "is not linked with -z now")
    endif()

    if(NOT program_headers MATCHES "\n[ \t]*GNU_RELRO ")
        list(APPEND ERRORS "has no GNU_RELRO segment")
    endif()

    if(NOT program_headers MATCHES "\n[ \t]*GNU_STACK [^\n]* RW  " OR program_headers MATCHES "\n[ \t]*GNU_STACK [^\n]* RWE ")
        list(APPEND ERRORS "does not have a non-executable stack")
    endif()

    # Symbol versions needed from other libraries (only the .gnu.version_r section).
    set(VERSION_NEEDS "")
    string(FIND "${version_info}" "Version needs section" NEEDS_START)

    if(NOT NEEDS_START EQUAL -1)
        string(SUBSTRING "${version_info}" ${NEEDS_START} -1 VERSION_NEEDS)
        string(FIND "${VERSION_NEEDS}" "\nVersion " NEEDS_END)

        if(NOT NEEDS_END EQUAL -1)
            string(SUBSTRING "${VERSION_NEEDS}" 0 ${NEEDS_END} VERSION_NEEDS)
        endif()
    endif()

    string(REGEX MATCHALL "Name: [A-Za-z0-9_.]+" VERSION_NAMES "${VERSION_NEEDS}")
    list(TRANSFORM VERSION_NAMES REPLACE "^Name: " "")
    list(REMOVE_DUPLICATES VERSION_NAMES)
    set(MAXIMUM_GLIBC "")

    foreach(VERSION_NAME IN LISTS VERSION_NAMES)
        if(RID MATCHES "^linux-musl-" OR NOT VERSION_NAME MATCHES "^GLIBC_([0-9]+(\\.[0-9]+)+)$")
            list(APPEND ERRORS "needs symbol version ${VERSION_NAME}")
            continue()
        endif()

        set(GLIBC_VERSION "${CMAKE_MATCH_1}")

        if(GLIBC_VERSION VERSION_GREATER SRTNET_GLIBC_BASELINE)
            list(APPEND ERRORS "needs ${VERSION_NAME}, newer than the glibc ${SRTNET_GLIBC_BASELINE} baseline")
        endif()

        if(MAXIMUM_GLIBC STREQUAL "" OR GLIBC_VERSION VERSION_GREATER MAXIMUM_GLIBC)
            set(MAXIMUM_GLIBC "${GLIBC_VERSION}")
        endif()
    endforeach()

    # Dynamic symbols: exports must be srt_*; glibc imports must be versioned unless weak.
    string(REPLACE "\n" ";" SYMBOL_LINES "${dyn_syms}")
    set(SYMBOL_PATTERN "^ *[0-9]+: +[0-9a-fA-F]+ +(0x)?[0-9a-fA-F]+ +([A-Z_]+) +([A-Z_]+) +([A-Z_]+) +([A-Z0-9_]+) +([^ ]+)")
    set(EXPORTS "")
    set(FOREIGN_EXPORTS "")
    set(UNVERSIONED_IMPORTS "")

    foreach(LINE IN LISTS SYMBOL_LINES)
        if(NOT LINE MATCHES "${SYMBOL_PATTERN}")
            continue()
        endif()

        set(TYPE "${CMAKE_MATCH_2}")
        set(BINDING "${CMAKE_MATCH_3}")
        set(VISIBILITY "${CMAKE_MATCH_4}")
        set(SECTION "${CMAKE_MATCH_5}")
        set(NAME "${CMAKE_MATCH_6}")

        if(SECTION STREQUAL "UND")
            if(BINDING STREQUAL "GLOBAL" AND NOT NAME MATCHES "@" AND NOT RID MATCHES "^linux-musl-")
                list(APPEND UNVERSIONED_IMPORTS "${NAME}")
            endif()

            continue()
        endif()

        if(NOT BINDING MATCHES "^(GLOBAL|WEAK|UNIQUE)$"
           OR NOT VISIBILITY MATCHES "^(DEFAULT|PROTECTED)$"
           OR TYPE MATCHES "^(SECTION|FILE)$")
            continue()
        endif()

        string(REGEX REPLACE "@.*$" "" NAME "${NAME}")

        if(NAME MATCHES "^srt_")
            list(APPEND EXPORTS "${NAME}")
        else()
            list(APPEND FOREIGN_EXPORTS "${NAME}")
        endif()
    endforeach()

    list(LENGTH FOREIGN_EXPORTS FOREIGN_EXPORT_COUNT)

    if(FOREIGN_EXPORT_COUNT GREATER 0)
        list(SUBLIST FOREIGN_EXPORTS 0 20 FOREIGN_EXAMPLES)
        list(JOIN FOREIGN_EXAMPLES " " FOREIGN_EXAMPLES)
        list(APPEND ERRORS "exports ${FOREIGN_EXPORT_COUNT} symbols outside srt_*: ${FOREIGN_EXAMPLES}")
    endif()

    if(NOT "srt_startup" IN_LIST EXPORTS)
        list(APPEND ERRORS "does not export the SRT API (srt_startup is missing)")
    endif()

    if(NOT UNVERSIONED_IMPORTS STREQUAL "")
        list(JOIN UNVERSIONED_IMPORTS " " UNVERSIONED_IMPORTS)
        list(APPEND ERRORS "imports unversioned global symbols: ${UNVERSIONED_IMPORTS}")
    endif()

    if(NOT ERRORS STREQUAL "")
        list(JOIN ERRORS "\n  - " ERROR_TEXT)
        message(FATAL_ERROR "${BINARY} is not a portable ${RID} library:\n  - ${ERROR_TEXT}")
    endif()

    list(LENGTH EXPORTS EXPORT_COUNT)
    list(JOIN NEEDED " " NEEDED_TEXT)

    if(RID MATCHES "^linux-musl-")
        set(LIBC "musl")
    elseif(MAXIMUM_GLIBC STREQUAL "")
        set(LIBC "glibc")
    else()
        set(LIBC "glibc ${MAXIMUM_GLIBC}")
    endif()

    message(STATUS "Verified ${RID} ELF: needs ${NEEDED_TEXT}; requires ${LIBC}; exports ${EXPORT_COUNT} srt_* symbols")
    set(${LIBC_VARIABLE} "${LIBC}" PARENT_SCOPE)
endfunction()
