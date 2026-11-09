# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis

cmake_minimum_required(VERSION 3.21)

if(NOT EXISTS "${ARCHIVE}" OR NOT EXISTS "${ARCHIVE}.asc")
    message(FATAL_ERROR "Specify an OpenSSL archive and its detached .asc signature with -DARCHIVE=<path>.")
endif()

find_program(GPG_EXECUTABLE gpg REQUIRED)
set(SIGNING_KEY "B146647E45A7B33947AB226B2A2C87D161692D40")
get_filename_component(DOWNLOAD_DIR "${ARCHIVE}" DIRECTORY)
set(KEYRING "${DOWNLOAD_DIR}/gnupg")
file(MAKE_DIRECTORY "${KEYRING}")

execute_process(
    COMMAND "${GPG_EXECUTABLE}" --batch --no-options --no-autostart
        --homedir "${KEYRING}" --import "${CMAKE_CURRENT_LIST_DIR}/../openssl-release-key.asc"
    COMMAND_ERROR_IS_FATAL ANY)

execute_process(
    COMMAND "${GPG_EXECUTABLE}" --batch --no-options --no-autostart
        --homedir "${KEYRING}" --no-auto-key-retrieve --status-fd 1
        --verify "${ARCHIVE}.asc" "${ARCHIVE}"
    RESULT_VARIABLE VERIFY_RESULT
    OUTPUT_VARIABLE VERIFY_STATUS
    ERROR_VARIABLE VERIFY_DETAILS)

if(NOT VERIFY_RESULT EQUAL 0
   OR NOT VERIFY_STATUS MATCHES "VALIDSIG [^\n]* ${SIGNING_KEY}[\r\n]")
    message(FATAL_ERROR "The release is not signed by the approved OpenSSL key.\n${VERIFY_DETAILS}")
endif()

if(DEFINED OPENSSL_VERSION)
    if(NOT OPENSSL_VERSION MATCHES "^[0-9]+\\.[0-9]+\\.[0-9]+$")
        message(FATAL_ERROR "The expected OpenSSL version must contain three numeric parts.")
    endif()

    set(VERSION_ENTRY "openssl-${OPENSSL_VERSION}/VERSION.dat")
    execute_process(COMMAND "${CMAKE_COMMAND}" -E tar tf "${ARCHIVE}"
        OUTPUT_VARIABLE ARCHIVE_ENTRIES COMMAND_ERROR_IS_FATAL ANY)
    string(REPLACE "\r\n" "\n" ARCHIVE_ENTRIES "${ARCHIVE_ENTRIES}")
    string(FIND "\n${ARCHIVE_ENTRIES}" "\n${VERSION_ENTRY}\n" VERSION_ENTRY_INDEX)

    if(VERSION_ENTRY_INDEX EQUAL -1)
        message(FATAL_ERROR "The signed archive does not contain the requested OpenSSL ${OPENSSL_VERSION} release.")
    endif()

    file(ARCHIVE_EXTRACT INPUT "${ARCHIVE}" DESTINATION "${DOWNLOAD_DIR}/metadata"
        PATTERNS "${VERSION_ENTRY}")
    file(READ "${DOWNLOAD_DIR}/metadata/${VERSION_ENTRY}" VERSION_DATA)

    foreach(PART IN ITEMS MAJOR MINOR PATCH)
        string(REGEX MATCH "(^|\n)${PART}=([0-9]+)(\r?\n|$)" VERSION_MATCH "${VERSION_DATA}")
        set(VERSION_${PART} "${CMAKE_MATCH_2}")
    endforeach()

    if(NOT "${VERSION_MAJOR}.${VERSION_MINOR}.${VERSION_PATCH}" STREQUAL OPENSSL_VERSION)
        message(FATAL_ERROR "The signed source version does not match OpenSSL ${OPENSSL_VERSION}.")
    endif()
endif()
