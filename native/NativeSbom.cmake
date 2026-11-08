# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis
#
# SPDX 2.2 description of a staged native library, included by StageNative.cmake.
#
# write_native_sbom() records the components built into the library, which package-level SBOM tools cannot
# see: libsrt (compiled from its pinned commit), the statically linked OpenSSL release and the statically linked
# compiler runtime (the GCC runtime libraries on Linux, the MSVC runtime libraries on Windows). The document
# namespace is derived from the binary's SHA-256, and the creation time is UTC (CMake honors SOURCE_DATE_EPOCH for
# reproducible output).
#
# libsrt declares MPL-2.0, but the files compiled into the library also carry the University of Illinois UDT
# license (BSD-3-Clause), the Aladdin MD5 license (Zlib) and, in atomic.h and atomic_msvc.h, the Unlicense; the
# concluded license lists all four.
#
# srt.dll (win-x64) also contains native/windows/dllmain.c, which this project wrote (MPL-2.0); it is listed as its
# own package. Components that are not listed:
#   - glibc runtimes: the libc.so linker script also links libc_nonshared.a statically, so the members that the
#     library references (small wrappers such as atexit or pthread_atfork) are copied into it. glibc is
#     LGPL-2.1-or-later, and those source files add a special exception that gives unlimited permission to link
#     their compiled form into other programs and distribute them; SPDX has no exactly matching identifier for that
#     exception (GCC-exception-2.0 is the closest).
#   - The C library's startup objects (crti.o, crtn.o: glibc under the same exception, musl under MIT) and GCC's
#     crtbeginS.o and crtendS.o (GCC Runtime Library Exception, like the GCC runtime package below).
#   - The Windows import libraries for the system DLLs that srt.dll loads.

set(SRTNET_SBOM_NAMESPACE_BASE "https://github.com/notley-technologies/Srt.NET/sbom")

function(_spdx_json_string OUTPUT VALUE)
    string(REPLACE "\\" "\\\\" VALUE "${VALUE}")
    string(REPLACE "\"" "\\\"" VALUE "${VALUE}")
    set(${OUTPUT} "\"${VALUE}\"" PARENT_SCOPE)
endfunction()

# write_native_sbom(<output> <rid> <binary> <package-path> <dependencies-json> <openssl-configuration>
#                   <compiler-id> <compiler-version>)
function(write_native_sbom OUTPUT RID BINARY PACKAGE_PATH DEPENDENCIES OPENSSL_CONFIGURATION COMPILER_ID COMPILER_VERSION)
    string(JSON SRT_VERSION GET "${DEPENDENCIES}" srt version)
    string(JSON SRT_REPOSITORY GET "${DEPENDENCIES}" srt repository)
    string(JSON SRT_COMMIT GET "${DEPENDENCIES}" srt commit)
    string(JSON OPENSSL_VERSION GET "${DEPENDENCIES}" openssl version)
    string(JSON OPENSSL_ARCHIVE GET "${DEPENDENCIES}" openssl archive)
    string(JSON OPENSSL_SHA256 GET "${DEPENDENCIES}" openssl sha256)
    file(SHA1 "${BINARY}" BINARY_SHA1)
    file(SHA256 "${BINARY}" BINARY_SHA256)
    string(TIMESTAMP CREATED "%Y-%m-%dT%H:%M:%SZ" UTC)
    string(REGEX REPLACE "^https://github\\.com/([^/]+)/([^/.]+)(\\.git)?$" "pkg:github/\\1/\\2" SRT_PURL "${SRT_REPOSITORY}")
    string(TOLOWER "${SRT_PURL}@v${SRT_VERSION}" SRT_PURL)
    set(SRT_DOWNLOAD "git+${SRT_REPOSITORY}@${SRT_COMMIT}")
    set(SRT_SOURCE_INFO "Built from commit ${SRT_COMMIT} with USE_ENCLIB=openssl-evp as a shared library.")
    set(FILE_ID "SPDXRef-File-${RID}")
    set(SRT_LICENSE "MPL-2.0 AND BSD-3-Clause AND Zlib AND Unlicense")
    set(SRT_COMMENT "Declared MPL-2.0. The UDT-derived files carry the University of Illinois BSD-3-Clause notice,")
    string(APPEND SRT_COMMENT " srtcore/md5.cpp the Aladdin (Zlib) notice, and srtcore/atomic.h and atomic_msvc.h")
    string(APPEND SRT_COMMENT " the Unlicense.")
    set(FILE_LICENSE "${SRT_LICENSE} AND Apache-2.0")
    set(OPENSSL_PURL "pkg:generic/openssl@${OPENSSL_VERSION}?download_url=${OPENSSL_ARCHIVE}&checksum=sha256:${OPENSSL_SHA256}")
    set(OPENSSL_COMMENT "Linked statically into the library.")

    if(NOT OPENSSL_CONFIGURATION STREQUAL "")
        string(APPEND OPENSSL_COMMENT " Configure: ${OPENSSL_CONFIGURATION}")
    endif()

    foreach(NAME IN ITEMS RID PACKAGE_PATH SRT_VERSION SRT_DOWNLOAD SRT_SOURCE_INFO SRT_PURL SRT_COMMENT
                          OPENSSL_VERSION OPENSSL_ARCHIVE OPENSSL_SHA256 OPENSSL_PURL OPENSSL_COMMENT COMPILER_VERSION)
        _spdx_json_string(${NAME}_JSON "${${NAME}}")
    endforeach()

    set(EXTRA_PACKAGES "")
    set(EXTRA_RELATIONSHIPS "")

    if(RID MATCHES "^linux-")
        # -static-libstdc++ -static-libgcc copy the GCC runtime into the library under the GCC Runtime Library Exception.
        set(GCC_RUNTIME_LICENSE "GPL-3.0-or-later WITH GCC-exception-3.1")
        string(APPEND FILE_LICENSE " AND (${GCC_RUNTIME_LICENSE})")

        if(COMPILER_ID STREQUAL "GNU" AND NOT COMPILER_VERSION STREQUAL "")
            set(GCC_VERSION_JSON "${COMPILER_VERSION_JSON}")
        else()
            set(GCC_VERSION_JSON "\"NOASSERTION\"")
        endif()

        string(APPEND EXTRA_PACKAGES ",
    {
      \"SPDXID\": \"SPDXRef-Package-gcc-runtime\",
      \"name\": \"GCC runtime libraries (libstdc++, libgcc)\",
      \"versionInfo\": ${GCC_VERSION_JSON},
      \"supplier\": \"NOASSERTION\",
      \"downloadLocation\": \"NOASSERTION\",
      \"filesAnalyzed\": false,
      \"licenseConcluded\": \"${GCC_RUNTIME_LICENSE}\",
      \"licenseDeclared\": \"${GCC_RUNTIME_LICENSE}\",
      \"copyrightText\": \"NOASSERTION\",
      \"comment\": \"Linked statically from the build toolchain with -static-libstdc++ -static-libgcc and hidden from the export table.\"
    }")
        string(APPEND EXTRA_RELATIONSHIPS ",
    { \"spdxElementId\": \"${FILE_ID}\", \"relationshipType\": \"STATIC_LINK\", \"relatedSpdxElement\": \"SPDXRef-Package-gcc-runtime\" }")
    elseif(RID MATCHES "^win-")
        # /MT (CMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded) copies the MSVC runtime libraries into srt.dll. They are
        # redistributed under the Visual Studio license terms, which have no SPDX identifier, so the license is
        # NOASSERTION and stays out of the file's license expression.
        if(COMPILER_ID STREQUAL "MSVC" AND NOT COMPILER_VERSION STREQUAL "")
            set(MSVC_VERSION_JSON "${COMPILER_VERSION_JSON}")
        else()
            set(MSVC_VERSION_JSON "\"NOASSERTION\"")
        endif()

        string(APPEND EXTRA_PACKAGES ",
    {
      \"SPDXID\": \"SPDXRef-Package-msvc-runtime\",
      \"name\": \"Microsoft Visual C++ runtime libraries (C runtime, Universal CRT, C++ standard library)\",
      \"versionInfo\": ${MSVC_VERSION_JSON},
      \"supplier\": \"Organization: Microsoft Corporation\",
      \"downloadLocation\": \"NOASSERTION\",
      \"filesAnalyzed\": false,
      \"licenseConcluded\": \"NOASSERTION\",
      \"licenseDeclared\": \"NOASSERTION\",
      \"copyrightText\": \"NOASSERTION\",
      \"comment\": \"Linked statically from the Visual Studio build tools with /MT; versionInfo is the MSVC compiler version.\"
    }")
        string(APPEND EXTRA_RELATIONSHIPS ",
    { \"spdxElementId\": \"${FILE_ID}\", \"relationshipType\": \"STATIC_LINK\", \"relatedSpdxElement\": \"SPDXRef-Package-msvc-runtime\" }")

        # DllMain that frees OpenSSL's per-thread state (see native/windows/dllmain.c); its license is already part
        # of the file's license expression through libsrt's MPL-2.0.
        string(APPEND EXTRA_PACKAGES ",
    {
      \"SPDXID\": \"SPDXRef-Package-srtnet-dllmain\",
      \"name\": \"Srt.NET native/windows/dllmain.c\",
      \"versionInfo\": \"NOASSERTION\",
      \"supplier\": \"Organization: Srt.NET contributors\",
      \"downloadLocation\": \"NOASSERTION\",
      \"filesAnalyzed\": false,
      \"licenseConcluded\": \"MPL-2.0\",
      \"licenseDeclared\": \"MPL-2.0\",
      \"copyrightText\": \"Copyright (c) 2026 Noah Davis\",
      \"comment\": \"Compiled into srt.dll by native/windows/AddDllMain.cmake; calls OPENSSL_thread_stop() for every exiting thread.\"
    }")
        string(APPEND EXTRA_RELATIONSHIPS ",
    { \"spdxElementId\": \"${FILE_ID}\", \"relationshipType\": \"GENERATED_FROM\", \"relatedSpdxElement\": \"SPDXRef-Package-srtnet-dllmain\" }")
    endif()

    string(CONFIGURE [=[{
  "spdxVersion": "SPDX-2.2",
  "dataLicense": "CC0-1.0",
  "SPDXID": "SPDXRef-DOCUMENT",
  "name": "Srt.NET native libsrt @RID@",
  "documentNamespace": "@SRTNET_SBOM_NAMESPACE_BASE@/native/@RID@/@BINARY_SHA256@",
  "documentDescribes": [ "@FILE_ID@" ],
  "creationInfo": {
    "created": "@CREATED@",
    "creators": [ "Organization: Srt.NET contributors", "Tool: Srt.NET-native-StageNative.cmake" ]
  },
  "packages": [
    {
      "SPDXID": "SPDXRef-Package-libsrt",
      "name": "libsrt",
      "versionInfo": @SRT_VERSION_JSON@,
      "supplier": "Organization: Haivision",
      "downloadLocation": @SRT_DOWNLOAD_JSON@,
      "sourceInfo": @SRT_SOURCE_INFO_JSON@,
      "filesAnalyzed": false,
      "licenseConcluded": "@SRT_LICENSE@",
      "licenseDeclared": "MPL-2.0",
      "copyrightText": "NOASSERTION",
      "comment": @SRT_COMMENT_JSON@,
      "externalRefs": [
        { "referenceCategory": "PACKAGE-MANAGER", "referenceType": "purl", "referenceLocator": @SRT_PURL_JSON@ }
      ]
    },
    {
      "SPDXID": "SPDXRef-Package-OpenSSL",
      "name": "OpenSSL",
      "versionInfo": @OPENSSL_VERSION_JSON@,
      "supplier": "Organization: OpenSSL Project",
      "downloadLocation": @OPENSSL_ARCHIVE_JSON@,
      "checksums": [ { "algorithm": "SHA256", "checksumValue": @OPENSSL_SHA256_JSON@ } ],
      "filesAnalyzed": false,
      "licenseConcluded": "Apache-2.0",
      "licenseDeclared": "Apache-2.0",
      "copyrightText": "NOASSERTION",
      "comment": @OPENSSL_COMMENT_JSON@,
      "externalRefs": [
        { "referenceCategory": "SECURITY", "referenceType": "cpe23Type", "referenceLocator": "cpe:2.3:a:openssl:openssl:@OPENSSL_VERSION@:*:*:*:*:*:*:*" },
        { "referenceCategory": "PACKAGE-MANAGER", "referenceType": "purl", "referenceLocator": @OPENSSL_PURL_JSON@ }
      ]
    }@EXTRA_PACKAGES@
  ],
  "files": [
    {
      "SPDXID": "@FILE_ID@",
      "fileName": @PACKAGE_PATH_JSON@,
      "fileTypes": [ "BINARY" ],
      "checksums": [
        { "algorithm": "SHA1", "checksumValue": "@BINARY_SHA1@" },
        { "algorithm": "SHA256", "checksumValue": "@BINARY_SHA256@" }
      ],
      "licenseConcluded": "@FILE_LICENSE@",
      "licenseInfoInFiles": [ "NOASSERTION" ],
      "copyrightText": "NOASSERTION"
    }
  ],
  "relationships": [
    { "spdxElementId": "SPDXRef-DOCUMENT", "relationshipType": "DESCRIBES", "relatedSpdxElement": "@FILE_ID@" },
    { "spdxElementId": "@FILE_ID@", "relationshipType": "GENERATED_FROM", "relatedSpdxElement": "SPDXRef-Package-libsrt" },
    { "spdxElementId": "@FILE_ID@", "relationshipType": "STATIC_LINK", "relatedSpdxElement": "SPDXRef-Package-OpenSSL" }@EXTRA_RELATIONSHIPS@
  ]
}
]=] DOCUMENT @ONLY)

    # Fail on malformed JSON rather than shipping it.
    string(JSON SPDX_VERSION GET "${DOCUMENT}" spdxVersion)
    string(JSON PACKAGE_COUNT LENGTH "${DOCUMENT}" packages)

    get_filename_component(OUTPUT_DIRECTORY "${OUTPUT}" DIRECTORY)
    file(MAKE_DIRECTORY "${OUTPUT_DIRECTORY}")
    file(WRITE "${OUTPUT}" "${DOCUMENT}")
    message(STATUS "Wrote ${SPDX_VERSION} document with ${PACKAGE_COUNT} packages: ${OUTPUT}")
endfunction()
