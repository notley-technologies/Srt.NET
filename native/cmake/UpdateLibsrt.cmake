# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis

# Pins a libsrt release: resolves its vX.Y.Z tag to the immutable commit and updates the files that name the bundled
# release (native/versions.json, THIRD_PARTY_NOTICES.md, SECURITY.md, the upstream links in README.md and
# docs/statistics.md, the statistics version check in NativeAbi.cs, and CHANGELOG.md). The native build then compiles
# native/abi/managed_abi.c against the new headers, so a release that changes a checked layout or constant fails the
# build instead of being bundled.
#
#   cmake -DSRT_VERSION=1.5.8 -P native/cmake/UpdateLibsrt.cmake

cmake_minimum_required(VERSION 3.21)

if(NOT SRT_VERSION MATCHES "^([0-9]+)\\.([0-9]+)\\.([0-9]+)$")
    message(FATAL_ERROR "Specify a libsrt release: cmake -DSRT_VERSION=1.5.8 -P native/cmake/UpdateLibsrt.cmake")
endif()

set(NEW_ABI_VERSION "new(${CMAKE_MATCH_1}, ${CMAKE_MATCH_2}, ${CMAKE_MATCH_3})")
get_filename_component(ROOT "${CMAKE_CURRENT_LIST_DIR}/../.." ABSOLUTE)
include("${CMAKE_CURRENT_LIST_DIR}/UpdateFiles.cmake")

file(READ "${ROOT}/native/versions.json" MANIFEST)
string(JSON REPOSITORY GET "${MANIFEST}" srt repository)
string(JSON OLD_VERSION GET "${MANIFEST}" srt version)
string(JSON OLD_COMMIT GET "${MANIFEST}" srt commit)

if(NOT OLD_VERSION MATCHES "^([0-9]+)\\.([0-9]+)\\.([0-9]+)$")
    message(FATAL_ERROR "native/versions.json pins an unexpected libsrt version: ${OLD_VERSION}")
endif()

set(OLD_ABI_VERSION "new(${CMAKE_MATCH_1}, ${CMAKE_MATCH_2}, ${CMAKE_MATCH_3})")

if(SRT_VERSION VERSION_LESS OLD_VERSION)
    message(FATAL_ERROR "libsrt ${SRT_VERSION} is older than the pinned ${OLD_VERSION}.")
endif()

# Release tags are vX.Y.Z (release candidates add a suffix and never match). An annotated tag is peeled to its commit.
find_package(Git REQUIRED)
execute_process(COMMAND "${GIT_EXECUTABLE}" ls-remote "${REPOSITORY}" "refs/tags/v${SRT_VERSION}" "refs/tags/v${SRT_VERSION}^{}"
    OUTPUT_VARIABLE TAGS COMMAND_ERROR_IS_FATAL ANY)
string(REPLACE "." "\\." VERSION_PATTERN "${SRT_VERSION}")

if(TAGS MATCHES "([0-9a-f]+)\trefs/tags/v${VERSION_PATTERN}\\^{}")
    set(NEW_COMMIT "${CMAKE_MATCH_1}")
elseif(TAGS MATCHES "([0-9a-f]+)\trefs/tags/v${VERSION_PATTERN}\n")
    set(NEW_COMMIT "${CMAKE_MATCH_1}")
else()
    message(FATAL_ERROR "${REPOSITORY} has no tag v${SRT_VERSION}.")
endif()

string(LENGTH "${NEW_COMMIT}" COMMIT_LENGTH)

if(NOT COMMIT_LENGTH EQUAL 40)
    message(FATAL_ERROR "Tag v${SRT_VERSION} resolved to an unexpected object name: ${NEW_COMMIT}")
endif()

if(SRT_VERSION STREQUAL OLD_VERSION)
    if(NOT NEW_COMMIT STREQUAL OLD_COMMIT)
        message(FATAL_ERROR "Tag v${SRT_VERSION} now points to ${NEW_COMMIT}, not the pinned ${OLD_COMMIT}. "
            "Upstream moved a release tag; investigate before changing the pin.")
    endif()

    message(STATUS "libsrt ${SRT_VERSION} (${OLD_COMMIT}) is already pinned.")
    return()
endif()

# Every edit is made in memory first, so a file whose wording changed stops the update before anything is written.
file(READ "${ROOT}/THIRD_PARTY_NOTICES.md" NOTICES)
file(READ "${ROOT}/SECURITY.md" SECURITY)
file(READ "${ROOT}/README.md" README)
file(READ "${ROOT}/docs/statistics.md" STATISTICS)
file(READ "${ROOT}/src/Srt.NET/Interop/NativeAbi.cs" ABI)
file(READ "${ROOT}/CHANGELOG.md" CHANGELOG)

srtnet_replace_once(NOTICES THIRD_PARTY_NOTICES.md "| Haivision libsrt | ${OLD_VERSION} |" "| Haivision libsrt | ${SRT_VERSION} |")
srtnet_replace_once(NOTICES THIRD_PARTY_NOTICES.md
    "https://github.com/Haivision/srt/tree/${OLD_COMMIT}" "https://github.com/Haivision/srt/tree/${NEW_COMMIT}")
srtnet_replace_once(SECURITY SECURITY.md "| Haivision libsrt | ${OLD_VERSION} |" "| Haivision libsrt | ${SRT_VERSION} |")
srtnet_replace_all(README README.md "github.com/Haivision/srt/blob/v${OLD_VERSION}/" "github.com/Haivision/srt/blob/v${SRT_VERSION}/")
srtnet_replace_all(README README.md "github.com/Haivision/srt/tree/v${OLD_VERSION}/" "github.com/Haivision/srt/tree/v${SRT_VERSION}/")
srtnet_replace_all(STATISTICS docs/statistics.md
    "github.com/Haivision/srt/blob/v${OLD_VERSION}/" "github.com/Haivision/srt/blob/v${SRT_VERSION}/")

# GetStatistics accepts the bundled release; the native ABI check verifies its statistics layout.
srtnet_replace_once(ABI src/Srt.NET/Interop/NativeAbi.cs
    "internal static readonly Version VerifiedVersion = ${OLD_ABI_VERSION};"
    "internal static readonly Version VerifiedVersion = ${NEW_ABI_VERSION};")

srtnet_changelog(CHANGELOG libsrt "${OLD_VERSION}" "${SRT_VERSION}"
    "Bundles libsrt ${OLD_VERSION} (commit `${OLD_COMMIT}`)"
    "Bundles libsrt ${SRT_VERSION} (commit `${NEW_COMMIT}`)"
    " (commit `${OLD_COMMIT}`); see the [libsrt ${OLD_VERSION} release notes](https://github.com/Haivision/srt/releases/tag/v${OLD_VERSION})."
    " (commit `${NEW_COMMIT}`); see the [libsrt ${SRT_VERSION} release notes](https://github.com/Haivision/srt/releases/tag/v${SRT_VERSION}).")

string(JSON MANIFEST SET "${MANIFEST}" srt version "\"${SRT_VERSION}\"")
string(JSON MANIFEST SET "${MANIFEST}" srt commit "\"${NEW_COMMIT}\"")

file(WRITE "${ROOT}/THIRD_PARTY_NOTICES.md" "${NOTICES}")
file(WRITE "${ROOT}/SECURITY.md" "${SECURITY}")
file(WRITE "${ROOT}/README.md" "${README}")
file(WRITE "${ROOT}/docs/statistics.md" "${STATISTICS}")
file(WRITE "${ROOT}/src/Srt.NET/Interop/NativeAbi.cs" "${ABI}")
file(WRITE "${ROOT}/CHANGELOG.md" "${CHANGELOG}")
# Last, so an interrupted update leaves the old pin and the next run redoes every edit.
file(WRITE "${ROOT}/native/versions.json" "${MANIFEST}\n")

message(STATUS "Pinned libsrt ${SRT_VERSION} (${NEW_COMMIT}), replacing ${OLD_VERSION}. Review the upstream changes: "
    "https://github.com/Haivision/srt/compare/${OLD_COMMIT}...${NEW_COMMIT}")
