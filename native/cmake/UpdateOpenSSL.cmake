# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis

# Pins an OpenSSL 3.5 LTS release: downloads the official archive, verifies its signature against
# native/openssl-release-key.asc, and updates native/versions.json and THIRD_PARTY_NOTICES.md.
#
#   cmake -DOPENSSL_VERSION=3.5.9 -P native/cmake/UpdateOpenSSL.cmake

cmake_minimum_required(VERSION 3.21)

if(NOT OPENSSL_VERSION MATCHES "^3\\.5\\.[0-9]+$")
    message(FATAL_ERROR "Specify an OpenSSL 3.5 LTS release: cmake -DOPENSSL_VERSION=3.5.9 -P native/cmake/UpdateOpenSSL.cmake")
endif()

get_filename_component(ROOT "${CMAKE_CURRENT_LIST_DIR}/../.." ABSOLUTE)
file(READ "${CMAKE_CURRENT_LIST_DIR}/../versions.json" MANIFEST)
string(JSON OLD_VERSION GET "${MANIFEST}" openssl version)

if(OPENSSL_VERSION VERSION_LESS OLD_VERSION)
    message(FATAL_ERROR "OpenSSL ${OPENSSL_VERSION} is older than the pinned ${OLD_VERSION}.")
endif()

set(DOWNLOAD_DIR "${ROOT}/artifacts/openssl-verification/${OPENSSL_VERSION}")
set(ARCHIVE_NAME "openssl-${OPENSSL_VERSION}.tar.gz")
set(ARCHIVE_URL "https://github.com/openssl/openssl/releases/download/openssl-${OPENSSL_VERSION}/${ARCHIVE_NAME}")
set(ARCHIVE "${DOWNLOAD_DIR}/${ARCHIVE_NAME}")
file(MAKE_DIRECTORY "${DOWNLOAD_DIR}")

foreach(SUFFIX IN ITEMS "" ".asc")
    file(DOWNLOAD "${ARCHIVE_URL}${SUFFIX}" "${ARCHIVE}${SUFFIX}"
        TLS_VERIFY ON STATUS DOWNLOAD_STATUS)
    list(GET DOWNLOAD_STATUS 0 DOWNLOAD_RESULT)

    if(NOT DOWNLOAD_RESULT EQUAL 0)
        message(FATAL_ERROR "Unable to download the OpenSSL release: ${DOWNLOAD_STATUS}")
    endif()
endforeach()

include("${CMAKE_CURRENT_LIST_DIR}/VerifyOpenSSL.cmake")

file(SHA256 "${ARCHIVE}" SOURCE_HASH)
set(OPENSSL "{\n  \"repository\": \"https://github.com/openssl/openssl.git\",\n  \"version\": \"${OPENSSL_VERSION}\",\n  \"archive\": \"${ARCHIVE_URL}\",\n  \"sha256\": \"${SOURCE_HASH}\",\n  \"signingKey\": \"${SIGNING_KEY}\"\n}")
string(JSON MANIFEST SET "${MANIFEST}" openssl "${OPENSSL}")
file(READ "${ROOT}/THIRD_PARTY_NOTICES.md" NOTICES)
string(REGEX MATCH "\\| OpenSSL \\| [^\n]*" OPENSSL_NOTICE "${NOTICES}")

if(OPENSSL_NOTICE STREQUAL "")
    message(FATAL_ERROR "The OpenSSL entry is missing from THIRD_PARTY_NOTICES.md.")
endif()

string(REPLACE "${OPENSSL_NOTICE}"
    "| OpenSSL | ${OPENSSL_VERSION} | Apache License 2.0 | [Official release](https://github.com/openssl/openssl/releases/tag/openssl-${OPENSSL_VERSION}) |"
    NOTICES "${NOTICES}")

file(WRITE "${ROOT}/THIRD_PARTY_NOTICES.md" "${NOTICES}")
# Last, so an interrupted update leaves the old pin and the next run redoes every edit.
file(WRITE "${CMAKE_CURRENT_LIST_DIR}/../versions.json" "${MANIFEST}\n")
message(STATUS "Verified OpenSSL ${OPENSSL_VERSION}; updated native/versions.json and THIRD_PARTY_NOTICES.md. Review the version and SHA-256 changes before release.")
