# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis

cmake_minimum_required(VERSION 3.24)

foreach(REQUIRED IN ITEMS SRTNET_RID INSTALL_ROOT SRT_SOURCE OPENSSL_SOURCE OPENSSL_ARCHIVE)
    if(NOT DEFINED ${REQUIRED} OR "${${REQUIRED}}" STREQUAL "")
        message(FATAL_ERROR "Specify ${REQUIRED} when staging native artifacts.")
    endif()
endforeach()

# Optional inputs: build details for the manifest and SBOM, the compilers, and readelf (searched on PATH when omitted).
foreach(OPTIONAL_INPUT IN ITEMS OPENSSL_CONFIGURATION COMPILER_ID COMPILER_VERSION C_COMPILER CXX_COMPILER READELF)
    if(NOT DEFINED ${OPTIONAL_INPUT})
        set(${OPTIONAL_INPUT} "")
    endif()
endforeach()

if(SRTNET_RID STREQUAL "win-x64")
    set(NATIVE_FILE srt.dll)
elseif(SRTNET_RID MATCHES "^linux-(musl-)?(x64|arm64)$")
    set(NATIVE_FILE libsrt.so)
elseif(SRTNET_RID MATCHES "^osx-(x64|arm64)$")
    set(NATIVE_FILE libsrt.dylib)
else()
    message(FATAL_ERROR "Unsupported native runtime: ${SRTNET_RID}")
endif()

get_filename_component(REPOSITORY_ROOT "${CMAKE_CURRENT_LIST_DIR}/.." ABSOLUTE)

if(NOT DEFINED ARTIFACTS_ROOT OR ARTIFACTS_ROOT STREQUAL "")
    set(ARTIFACTS_ROOT "${REPOSITORY_ROOT}/artifacts")
endif()

file(READ "${CMAKE_CURRENT_LIST_DIR}/versions.json" DEPENDENCIES)
string(JSON SRT_COMMIT GET "${DEPENDENCIES}" srt commit)
string(JSON OPENSSL_SHA256 GET "${DEPENDENCIES}" openssl sha256)
find_package(Git REQUIRED)

execute_process(COMMAND "${GIT_EXECUTABLE}" -C "${SRT_SOURCE}" rev-parse HEAD
    OUTPUT_VARIABLE ACTUAL_COMMIT OUTPUT_STRIP_TRAILING_WHITESPACE COMMAND_ERROR_IS_FATAL ANY)
execute_process(COMMAND "${GIT_EXECUTABLE}" -C "${SRT_SOURCE}" status --porcelain --untracked-files=all
    OUTPUT_VARIABLE SOURCE_CHANGES OUTPUT_STRIP_TRAILING_WHITESPACE COMMAND_ERROR_IS_FATAL ANY)

if(NOT ACTUAL_COMMIT STREQUAL SRT_COMMIT OR NOT SOURCE_CHANGES STREQUAL "")
    message(FATAL_ERROR "The libsrt source must match its pinned revision without local changes: ${SRT_SOURCE}")
endif()

file(SHA256 "${OPENSSL_ARCHIVE}" ACTUAL_SHA256)

if(NOT ACTUAL_SHA256 STREQUAL OPENSSL_SHA256)
    message(FATAL_ERROR "The OpenSSL source archive does not match its approved checksum.")
endif()

file(GLOB_RECURSE CANDIDATES LIST_DIRECTORIES FALSE "${INSTALL_ROOT}/${NATIVE_FILE}")
list(LENGTH CANDIDATES CANDIDATE_COUNT)

if(NOT CANDIDATE_COUNT EQUAL 1)
    message(FATAL_ERROR "Expected one installed ${NATIVE_FILE}; found ${CANDIDATE_COUNT}.")
endif()

list(GET CANDIDATES 0 SOURCE_BINARY)
file(REAL_PATH "${SOURCE_BINARY}" SOURCE_BINARY)
set(LIBC "")

# Reject a non-portable Linux or macOS library before it can reach runtimes/ and the package.
if(SRTNET_RID MATCHES "^linux-")
    include("${CMAKE_CURRENT_LIST_DIR}/VerifyElf.cmake")
    verify_elf("${SOURCE_BINARY}" "${SRTNET_RID}" "${READELF}" LIBC)
elseif(SRTNET_RID MATCHES "^osx-")
    include("${CMAKE_CURRENT_LIST_DIR}/VerifyMachO.cmake")
    verify_macho("${SOURCE_BINARY}" "${SRTNET_RID}")
endif()

# The Alpine image installs its whole toolchain from the live repository, and the manylinux images may update
# packages such as glibc-devel when the image is built, so the image digest alone does not identify them. Record the
# packages that own the compilers, binutils, the static C++ runtime, the C library startup files and, on Alpine, the
# fortify headers.
set(TOOLCHAIN_PACKAGES "")

if(SRTNET_RID MATCHES "^linux-musl-")
    find_program(PACKAGE_QUERY apk)
    set(PACKAGE_QUERY_ARGUMENTS info --who-owns)
elseif(SRTNET_RID MATCHES "^linux-")
    find_program(PACKAGE_QUERY rpm)
    set(PACKAGE_QUERY_ARGUMENTS -qf --queryformat "%{NAME}-%{VERSION}-%{RELEASE}")
endif()

if(PACKAGE_QUERY AND NOT C_COMPILER STREQUAL "" AND NOT CXX_COMPILER STREQUAL "")
    if(READELF STREQUAL "" OR NOT EXISTS "${READELF}")
        find_program(READELF_EXECUTABLE readelf REQUIRED)
        set(READELF "${READELF_EXECUTABLE}")
    endif()

    execute_process(COMMAND "${CXX_COMPILER}" -print-file-name=libstdc++.a
        OUTPUT_VARIABLE STATIC_CXX_RUNTIME OUTPUT_STRIP_TRAILING_WHITESPACE COMMAND_ERROR_IS_FATAL ANY)
    execute_process(COMMAND "${C_COMPILER}" -print-file-name=crt1.o
        OUTPUT_VARIABLE C_STARTUP OUTPUT_STRIP_TRAILING_WHITESPACE COMMAND_ERROR_IS_FATAL ANY)
    set(TOOLCHAIN_FILES "${C_COMPILER}" "${CXX_COMPILER}" "${READELF}" "${STATIC_CXX_RUNTIME}" "${C_STARTUP}")

    if(EXISTS "/usr/include/fortify/string.h")
        list(APPEND TOOLCHAIN_FILES "/usr/include/fortify/string.h")
    endif()

    foreach(TOOLCHAIN_FILE IN LISTS TOOLCHAIN_FILES)
        file(REAL_PATH "${TOOLCHAIN_FILE}" TOOLCHAIN_FILE)
        execute_process(COMMAND "${PACKAGE_QUERY}" ${PACKAGE_QUERY_ARGUMENTS} "${TOOLCHAIN_FILE}"
            OUTPUT_VARIABLE OWNER OUTPUT_STRIP_TRAILING_WHITESPACE RESULT_VARIABLE QUERY_RESULT ERROR_QUIET)
        # apk prints "<file> is owned by <package>-<version>"; rpm prints only the package.
        string(REGEX REPLACE "^.* is owned by " "" OWNER "${OWNER}")

        if(QUERY_RESULT EQUAL 0 AND OWNER MATCHES "^[A-Za-z0-9][A-Za-z0-9._+-]*$")
            list(APPEND TOOLCHAIN_PACKAGES "${OWNER}")
        else()
            message(WARNING "No package owns ${TOOLCHAIN_FILE}; the manifest will not list it.")
        endif()
    endforeach()

    list(REMOVE_DUPLICATES TOOLCHAIN_PACKAGES)
    list(SORT TOOLCHAIN_PACKAGES)
endif()

set(OUTPUT_DIRECTORY "${ARTIFACTS_ROOT}/runtimes/${SRTNET_RID}/native")
set(OUTPUT_BINARY "${OUTPUT_DIRECTORY}/${NATIVE_FILE}")
file(MAKE_DIRECTORY "${OUTPUT_DIRECTORY}" "${ARTIFACTS_ROOT}/licenses")
file(COPY_FILE "${SOURCE_BINARY}" "${OUTPUT_BINARY}" ONLY_IF_DIFFERENT)

if(SRTNET_RID MATCHES "^osx-")
    find_program(INSTALL_NAME_TOOL install_name_tool REQUIRED)
    find_program(CODESIGN codesign REQUIRED)
    execute_process(COMMAND "${INSTALL_NAME_TOOL}" -id "@rpath/libsrt.dylib" "${OUTPUT_BINARY}"
        COMMAND_ERROR_IS_FATAL ANY)
    execute_process(COMMAND "${CODESIGN}" --force --sign - "${OUTPUT_BINARY}"
        COMMAND_ERROR_IS_FATAL ANY)
endif()

# Git may check libsrt out with CRLF line endings (core.autocrlf on Windows), so license texts are written with LF
# line endings: every build then stages identical files.
function(copy_text_with_lf SOURCE DESTINATION)
    file(READ "${SOURCE}" TEXT)
    string(REPLACE "\r\n" "\n" TEXT "${TEXT}")
    string(RANDOM LENGTH 16 ALPHABET 0123456789abcdef TEMP_SUFFIX)
    set(TEMP_FILE "${DESTINATION}.${TEMP_SUFFIX}.tmp")
    file(WRITE "${TEMP_FILE}" "${TEXT}")
    file(COPY_FILE "${TEMP_FILE}" "${DESTINATION}" ONLY_IF_DIFFERENT)
    file(REMOVE "${TEMP_FILE}")
endfunction()

copy_text_with_lf("${SRT_SOURCE}/LICENSE" "${ARTIFACTS_ROOT}/licenses/libsrt-MPL-2.0.txt")
copy_text_with_lf("${OPENSSL_SOURCE}/LICENSE.txt" "${ARTIFACTS_ROOT}/licenses/openssl-LICENSE.txt")
include("${CMAKE_CURRENT_LIST_DIR}/CollectNotices.cmake")
collect_notices("${SRT_SOURCE}" "${ARTIFACTS_ROOT}/licenses/libsrt-COPYRIGHT.txt" srtcore haicrypt common)
collect_notices("${OPENSSL_SOURCE}" "${ARTIFACTS_ROOT}/licenses/openssl-COPYRIGHT.txt" include crypto providers ssl)

# The manifest is packed beside the library and copied into application output, so it needs a name that
# cannot collide with other packages' files (a bare build-info.json triggers NETSDK1152 on publish).
file(SHA256 "${OUTPUT_BINARY}" BINARY_SHA256)
string(JSON BUILD_INFO SET "{}" rid "\"${SRTNET_RID}\"")
string(JSON BUILD_INFO SET "${BUILD_INFO}" dependencies "${DEPENDENCIES}")
string(JSON BUILD_INFO SET "${BUILD_INFO}" encryption "\"openssl-evp (static)\"")
string(JSON BUILD_INFO SET "${BUILD_INFO}" sha256 "\"${BINARY_SHA256}\"")

if(NOT OPENSSL_CONFIGURATION STREQUAL "")
    string(JSON BUILD_INFO SET "${BUILD_INFO}" opensslConfiguration "\"${OPENSSL_CONFIGURATION}\"")
endif()

if(NOT COMPILER_ID STREQUAL "")
    string(JSON BUILD_INFO SET "${BUILD_INFO}" compiler "\"${COMPILER_ID} ${COMPILER_VERSION}\"")
endif()

if(NOT LIBC STREQUAL "")
    string(JSON BUILD_INFO SET "${BUILD_INFO}" libc "\"${LIBC}\"")
endif()

if(NOT TOOLCHAIN_PACKAGES STREQUAL "")
    list(JOIN TOOLCHAIN_PACKAGES "\", \"" TOOLCHAIN_JSON)
    string(JSON BUILD_INFO SET "${BUILD_INFO}" toolchain "[ \"${TOOLCHAIN_JSON}\" ]")
endif()

file(REMOVE "${OUTPUT_DIRECTORY}/build-info.json")
file(WRITE "${OUTPUT_DIRECTORY}/srt.build-info.json" "${BUILD_INFO}\n")

include("${CMAKE_CURRENT_LIST_DIR}/NativeSbom.cmake")
write_native_sbom("${ARTIFACTS_ROOT}/sbom/native-${SRTNET_RID}.spdx.json" "${SRTNET_RID}" "${OUTPUT_BINARY}"
    "./runtimes/${SRTNET_RID}/native/${NATIVE_FILE}" "${DEPENDENCIES}" "${OPENSSL_CONFIGURATION}"
    "${COMPILER_ID}" "${COMPILER_VERSION}")
message(STATUS "Staged ${OUTPUT_BINARY}")
