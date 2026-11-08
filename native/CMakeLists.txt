# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis

cmake_minimum_required(VERSION 3.24)
project(SrtNetNative LANGUAGES C CXX)

include(ExternalProject)
include(ProcessorCount)
find_package(Perl REQUIRED)

get_filename_component(REPOSITORY_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/.." ABSOLUTE)
set(SRTNET_RUNTIMES win-x64 linux-x64 linux-arm64 linux-musl-x64 linux-musl-arm64 osx-x64 osx-arm64)
string(REPLACE ";" ", " SRTNET_RUNTIME_LIST "${SRTNET_RUNTIMES}")
set(SRTNET_RID "" CACHE STRING "Native runtime: ${SRTNET_RUNTIME_LIST}")
set_property(CACHE SRTNET_RID PROPERTY STRINGS ${SRTNET_RUNTIMES})
set(SRTNET_ARTIFACTS_DIR "${REPOSITORY_ROOT}/artifacts" CACHE PATH "Directory that receives runtimes/, licenses/ and sbom/")
ProcessorCount(DEFAULT_JOBS)
set(SRTNET_BUILD_JOBS "${DEFAULT_JOBS}" CACHE STRING "Parallel native compilation and test jobs")
option(SRTNET_TEST_OPENSSL "Run OpenSSL's test suite before installing it (slow; enabled in CI)" OFF)

if(NOT SRTNET_BUILD_JOBS MATCHES "^[1-9][0-9]*$")
    message(FATAL_ERROR "SRTNET_BUILD_JOBS must be a positive integer.")
endif()

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "Srt.NET native builds require a 64-bit compiler.")
endif()

if(WIN32)
    set(HOST_OS win)
elseif(APPLE)
    set(HOST_OS osx)
elseif(CMAKE_SYSTEM_NAME STREQUAL "Linux")
    # glibc and musl binaries are not interchangeable, so the C library is part of the runtime identifier.
    execute_process(COMMAND "${CMAKE_C_COMPILER}" -dumpmachine
        OUTPUT_VARIABLE C_COMPILER_TARGET OUTPUT_STRIP_TRAILING_WHITESPACE COMMAND_ERROR_IS_FATAL ANY)

    if(C_COMPILER_TARGET MATCHES "musl")
        set(HOST_OS linux-musl)
    else()
        set(HOST_OS linux)
    endif()
else()
    message(FATAL_ERROR "This operating system is not supported by the native build.")
endif()

string(TOLOWER "${CMAKE_SYSTEM_PROCESSOR}" HOST_PROCESSOR)

if(HOST_PROCESSOR MATCHES "^(arm64|aarch64)$")
    set(HOST_ARCH arm64)
elseif(HOST_PROCESSOR MATCHES "^(amd64|x86_64)$")
    set(HOST_ARCH x64)
else()
    message(FATAL_ERROR "Unsupported processor: ${CMAKE_SYSTEM_PROCESSOR}")
endif()

if(NOT SRTNET_RID IN_LIST SRTNET_RUNTIMES OR NOT SRTNET_RID STREQUAL "${HOST_OS}-${HOST_ARCH}")
    message(FATAL_ERROR "Set SRTNET_RID to a supported runtime matching this host (${HOST_OS}-${HOST_ARCH}). "
        "Supported runtimes: ${SRTNET_RUNTIME_LIST}. Linux runtimes are built in containers: bash native/docker/build-native.sh <rid>.")
endif()

file(READ "${CMAKE_CURRENT_SOURCE_DIR}/versions.json" DEPENDENCIES)
set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/versions.json")
string(JSON SRT_REPOSITORY GET "${DEPENDENCIES}" srt repository)
string(JSON SRT_COMMIT GET "${DEPENDENCIES}" srt commit)
string(JSON OPENSSL_VERSION GET "${DEPENDENCIES}" openssl version)
string(JSON OPENSSL_ARCHIVE_URL GET "${DEPENDENCIES}" openssl archive)
string(JSON OPENSSL_SHA256 GET "${DEPENDENCIES}" openssl sha256)

set(INSTALL_ROOT "${CMAKE_CURRENT_BINARY_DIR}/install")
set(OPENSSL_DESTDIR "${CMAKE_CURRENT_BINARY_DIR}/openssl-destdir")
set(OPENSSL_SOURCE "${CMAKE_CURRENT_BINARY_DIR}/sources/openssl")
set(OPENSSL_ARCHIVE "${CMAKE_CURRENT_BINARY_DIR}/downloads/openssl-${OPENSSL_VERSION}.tar.gz")
set(SRT_SOURCE "${CMAKE_CURRENT_BINARY_DIR}/sources/srt")

# OpenSSL is linked statically and libsrt uses only libcrypto's EVP cipher, key-derivation and random APIs, so
# nothing may be loaded at run time: no DSO support (engines and provider modules), no ENGINE API, and no
# automatic openssl.cnf loading. Documentation is never installed, and test programs are built only when run.
#
# OpenSSL compiles the module and engine directories it derives from its prefix (MODULESDIR, ENGINESDIR) into
# libcrypto. They are inert here, because nothing can be loaded, but they would embed the build tree. The prefix is
# therefore a fixed system path and OpenSSL is installed below it with DESTDIR: /usr on Linux and macOS (root-owned,
# like the fixed /etc/ssl configuration directory), and OpenSSL's own default prefix on Windows, beside its default
# configuration directory under C:/Program Files/Common Files/SSL. On some systems these directories exist (Alpine's
# openssl package installs /usr/lib/ossl-modules); they stay unused only because OpenSSL is built with no-module,
# no-dso and no-engine, so keep those options.
if(WIN32)
    set(OPENSSL_PREFIX "C:/Program Files/OpenSSL")
    set(OPENSSL_INSTALL "${OPENSSL_DESTDIR}/Program Files/OpenSSL")
    file(TO_NATIVE_PATH "${OPENSSL_DESTDIR}" OPENSSL_DESTDIR_NATIVE)
else()
    set(OPENSSL_PREFIX /usr)
    set(OPENSSL_INSTALL "${OPENSSL_DESTDIR}/usr")
    set(OPENSSL_DESTDIR_NATIVE "${OPENSSL_DESTDIR}")
endif()

set(OPENSSL_OPTIONS no-shared no-module no-dso no-engine no-autoload-config no-docs
    "--prefix=${OPENSSL_PREFIX}" --libdir=lib)

if(NOT SRTNET_TEST_OPENSSL)
    list(APPEND OPENSSL_OPTIONS no-tests)
endif()

set(OPENSSL_ENV "${CMAKE_COMMAND}" -E env "LC_ALL=C")
set(COMMON_ARGS
    "-DCMAKE_BUILD_TYPE:STRING=Release"
    "-DCMAKE_INSTALL_PREFIX:PATH=${INSTALL_ROOT}"
    "-DCMAKE_POSITION_INDEPENDENT_CODE:BOOL=ON")
set(SRT_PLATFORM_ARGS "")

if(MSVC)
    find_program(OPENSSL_MAKE nmake REQUIRED)
    find_program(NASM_EXECUTABLE nasm REQUIRED)
    set(OPENSSL_TARGET VC-WIN64A)
    set(OPENSSL_BUILD_COMMAND "${OPENSSL_MAKE}")
    set(OPENSSL_CRYPTO "${OPENSSL_INSTALL}/lib/libcrypto.lib")
    set(OPENSSL_SSL "${OPENSSL_INSTALL}/lib/libssl.lib")
    list(APPEND COMMON_ARGS
        "-DCMAKE_POLICY_DEFAULT_CMP0091:STRING=NEW"
        "-DCMAKE_MSVC_RUNTIME_LIBRARY:STRING=MultiThreaded")

    execute_process(COMMAND "${PERL_EXECUTABLE}" -e "print $^O"
        OUTPUT_VARIABLE PERL_PLATFORM COMMAND_ERROR_IS_FATAL ANY)

    if(NOT PERL_PLATFORM STREQUAL "MSWin32")
        message(FATAL_ERROR "Use native Windows Perl (for example Strawberry Perl) in an x64 Visual Studio developer prompt.")
    endif()

    # The static libcrypto frees a thread's OpenSSL state only when OPENSSL_thread_stop() runs on that thread, so
    # srt.dll gets a DllMain that calls it for every exiting thread (see windows/dllmain.c).
    set(SRT_DLLMAIN_INCLUDE "${CMAKE_CURRENT_SOURCE_DIR}/windows/AddDllMain.cmake")
    set(SRT_DLLMAIN_SOURCE "${CMAKE_CURRENT_SOURCE_DIR}/windows/dllmain.c")

    # Control Flow Guard for OpenSSL and libsrt (/guard:cf when compiling and linking) and CET shadow stack
    # compatibility for srt.dll (/CETCOMPAT). OpenSSL's Configure passes options that start with / to the compiler;
    # it is linked statically, so only srt.dll's link needs the linker options. The *_FLAGS_INIT variables extend
    # CMake's MSVC defaults (/DWIN32 /D_WINDOWS /EHsc ...) for libsrt instead of replacing them.
    #
    # MSVC has no documented option that maps source paths, so __FILE__ in libsrt's logging and OpenSSL's error
    # records still holds the build tree's source paths (/d1trimfile is undocumented and not used). Release builds
    # write no PDB, so no PDB path is embedded.
    list(APPEND OPENSSL_OPTIONS /guard:cf)
    set(SRT_PLATFORM_ARGS
        "-DCMAKE_PROJECT_SRT_INCLUDE:FILEPATH=${SRT_DLLMAIN_INCLUDE}"
        "-DCMAKE_C_FLAGS_INIT:STRING=/guard:cf"
        "-DCMAKE_CXX_FLAGS_INIT:STRING=/guard:cf"
        "-DCMAKE_SHARED_LINKER_FLAGS_INIT:STRING=/guard:cf /CETCOMPAT")
else()
    find_program(OPENSSL_MAKE NAMES gmake make REQUIRED)
    set(OPENSSL_BUILD_COMMAND "${OPENSSL_MAKE}" "-j${SRTNET_BUILD_JOBS}")
    set(OPENSSL_CRYPTO "${OPENSSL_INSTALL}/lib/libcrypto.a")
    set(OPENSSL_SSL "${OPENSSL_INSTALL}/lib/libssl.a")
    # A fixed, root-owned configuration directory instead of the build tree, which may be world-writable (/tmp).
    list(APPEND OPENSSL_OPTIONS --openssldir=/etc/ssl -fPIC "CC=${CMAKE_C_COMPILER}")
    # Build libsrt with the same compilers as OpenSSL and this project.
    list(APPEND COMMON_ARGS
        "-DCMAKE_C_COMPILER:FILEPATH=${CMAKE_C_COMPILER}"
        "-DCMAKE_CXX_COMPILER:FILEPATH=${CMAKE_CXX_COMPILER}")

    # Compiler and linker flags are space-separated strings, so the paths they contain cannot contain spaces.
    if(CMAKE_CURRENT_SOURCE_DIR MATCHES " " OR SRT_SOURCE MATCHES " ")
        message(FATAL_ERROR "Linux and macOS native builds require source and build directories without spaces.")
    endif()

    if(APPLE)
        if(HOST_ARCH STREQUAL "arm64")
            set(OPENSSL_TARGET darwin64-arm64-cc)
        else()
            set(OPENSSL_TARGET darwin64-x86_64-cc)
        endif()

        list(APPEND OPENSSL_OPTIONS -mmacosx-version-min=13.0)
        list(APPEND COMMON_ARGS "-DCMAKE_OSX_DEPLOYMENT_TARGET:STRING=13.0")

        # As on Linux, only the srt_* API is exported (libsrt.exp) and OpenSSL, linked statically, stays hidden;
        # unused code is discarded and the prefix map keeps build-machine paths out of libsrt's log locations. The
        # C++ runtime is the system libc++, and Apple's compiler enables stack protection and _FORTIFY_SOURCE by
        # default. StageNative.cmake checks the result (VerifyMachO.cmake).
        set(SRT_EXPORTED_SYMBOLS "${CMAKE_CURRENT_SOURCE_DIR}/libsrt.exp")
        set(SRT_C_FLAGS "-fvisibility=hidden -ffile-prefix-map=${SRT_SOURCE}=srt")
        set(SRT_CXX_FLAGS "${SRT_C_FLAGS} -fvisibility-inlines-hidden")
        set(SRT_SHARED_LINKER_FLAGS "-Wl,-exported_symbols_list,${SRT_EXPORTED_SYMBOLS} -Wl,-dead_strip")
        set(SRT_PLATFORM_ARGS
            "-DCMAKE_C_FLAGS:STRING=${SRT_C_FLAGS}"
            "-DCMAKE_CXX_FLAGS:STRING=${SRT_CXX_FLAGS}"
            "-DCMAKE_SHARED_LINKER_FLAGS:STRING=${SRT_SHARED_LINKER_FLAGS}")
        set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${SRT_EXPORTED_SYMBOLS}")
    else()
        if(HOST_ARCH STREQUAL "arm64")
            set(OPENSSL_TARGET linux-aarch64)
        else()
            set(OPENSSL_TARGET linux-x86_64)
        endif()

        set(SRT_VERSION_SCRIPT "${CMAKE_CURRENT_SOURCE_DIR}/libsrt.map")

        # Stack protection and fortified libc calls for OpenSSL and libsrt alike; the manylinux compiler enables
        # neither by default. OpenSSL's Configure turns -D options into preprocessor flags, which follow the other
        # compiler flags, so -U_FORTIFY_SOURCE still comes before -D_FORTIFY_SOURCE=2.
        set(HARDENING_FLAGS -fstack-protector-strong -U_FORTIFY_SOURCE -D_FORTIFY_SOURCE=2)
        list(APPEND OPENSSL_OPTIONS ${HARDENING_FLAGS})
        list(JOIN HARDENING_FLAGS " " HARDENING_COMPILE_FLAGS)

        # A portable, hardened shared library: only the srt_* API is exported; the C++ and GCC runtimes and
        # OpenSSL are linked statically and hidden; unused sections are discarded; libsrt and OpenSSL are compiled
        # with the hardening flags above (the static C++ and GCC runtimes come precompiled with the toolchain);
        # relocations are read-only after loading and the stack is not executable. The prefix map keeps
        # build-machine paths out of libsrt's log locations.
        set(SRT_C_FLAGS "-fvisibility=hidden -ffunction-sections -fdata-sections ${HARDENING_COMPILE_FLAGS}")
        string(APPEND SRT_C_FLAGS " -ffile-prefix-map=${SRT_SOURCE}=srt")
        set(SRT_CXX_FLAGS "${SRT_C_FLAGS} -fvisibility-inlines-hidden")
        set(SRT_SHARED_LINKER_FLAGS "-static-libstdc++ -static-libgcc -Wl,--exclude-libs,ALL")
        string(APPEND SRT_SHARED_LINKER_FLAGS " -Wl,--version-script=${SRT_VERSION_SCRIPT} -Wl,--no-undefined")
        string(APPEND SRT_SHARED_LINKER_FLAGS " -Wl,--gc-sections -Wl,-z,relro -Wl,-z,now -Wl,-z,noexecstack")
        set(SRT_PLATFORM_ARGS
            "-DCMAKE_C_FLAGS:STRING=${SRT_C_FLAGS}"
            "-DCMAKE_CXX_FLAGS:STRING=${SRT_CXX_FLAGS}"
            "-DCMAKE_SHARED_LINKER_FLAGS:STRING=${SRT_SHARED_LINKER_FLAGS}")
        set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${SRT_VERSION_SCRIPT}")
    endif()
endif()

set(OPENSSL_TEST_ARGUMENTS "")

if(SRTNET_TEST_OPENSSL)
    set(OPENSSL_TEST_ARGUMENTS
        TEST_BEFORE_INSTALL TRUE
        TEST_COMMAND ${OPENSSL_ENV} "${OPENSSL_MAKE}" test "HARNESS_JOBS=${SRTNET_BUILD_JOBS}")
endif()

ExternalProject_Add(openssl
    URL "${OPENSSL_ARCHIVE_URL}"
    URL_HASH "SHA256=${OPENSSL_SHA256}"
    TLS_VERIFY ON
    DOWNLOAD_DIR "${CMAKE_CURRENT_BINARY_DIR}/downloads"
    DOWNLOAD_NAME "openssl-${OPENSSL_VERSION}.tar.gz"
    DOWNLOAD_EXTRACT_TIMESTAMP FALSE
    SOURCE_DIR "${OPENSSL_SOURCE}"
    BINARY_DIR "${CMAKE_CURRENT_BINARY_DIR}/openssl"
    CONFIGURE_COMMAND "${PERL_EXECUTABLE}" <SOURCE_DIR>/Configure "${OPENSSL_TARGET}" ${OPENSSL_OPTIONS}
    BUILD_COMMAND ${OPENSSL_ENV} ${OPENSSL_BUILD_COMMAND}
    ${OPENSSL_TEST_ARGUMENTS}
    INSTALL_COMMAND ${OPENSSL_ENV} "${OPENSSL_MAKE}" install_sw "DESTDIR=${OPENSSL_DESTDIR_NATIVE}")

ExternalProject_Add(srt
    DEPENDS openssl
    GIT_REPOSITORY "${SRT_REPOSITORY}"
    GIT_TAG "${SRT_COMMIT}"
    GIT_SUBMODULES ""
    SOURCE_DIR "${SRT_SOURCE}"
    BINARY_DIR "${CMAKE_CURRENT_BINARY_DIR}/srt"
    CMAKE_ARGS ${COMMON_ARGS} ${SRT_PLATFORM_ARGS}
        "-DOPENSSL_ROOT_DIR:PATH=${OPENSSL_INSTALL}"
        "-DOPENSSL_INCLUDE_DIR:PATH=${OPENSSL_INSTALL}/include"
        "-DOPENSSL_CRYPTO_LIBRARY:FILEPATH=${OPENSSL_CRYPTO}"
        "-DOPENSSL_SSL_LIBRARY:FILEPATH=${OPENSSL_SSL}"
        "-DUSE_OPENSSL_PC:BOOL=OFF"
        "-DUSE_ENCLIB:STRING=openssl-evp"
        "-DSRT_USE_OPENSSL_STATIC_LIBS:BOOL=ON"
        "-DENABLE_ENCRYPTION:BOOL=ON"
        "-DENABLE_SHARED:BOOL=ON"
        "-DENABLE_STATIC:BOOL=OFF"
        "-DENABLE_APPS:BOOL=OFF"
    BUILD_COMMAND "${CMAKE_COMMAND}" --build <BINARY_DIR> --config Release --parallel "${SRTNET_BUILD_JOBS}"
    INSTALL_COMMAND "${CMAKE_COMMAND}" --install <BINARY_DIR> --config Release)

if(MSVC)
    ExternalProject_Add_StepDependencies(srt configure "${SRT_DLLMAIN_INCLUDE}")
    ExternalProject_Add_StepDependencies(srt build "${SRT_DLLMAIN_SOURCE}")
endif()

# Build-machine paths and the test switch do not describe the binary, so they stay out of its manifest.
set(OPENSSL_RECORDED_OPTIONS ${OPENSSL_OPTIONS})
list(FILTER OPENSSL_RECORDED_OPTIONS EXCLUDE REGEX "^(--prefix=|CC=|no-tests$)")
string(REPLACE ";" " " OPENSSL_CONFIGURATION "${OPENSSL_TARGET};${OPENSSL_RECORDED_OPTIONS}")

add_custom_target(stage-native ALL
    COMMAND "${CMAKE_COMMAND}"
        "-DSRTNET_RID=${SRTNET_RID}"
        "-DARTIFACTS_ROOT=${SRTNET_ARTIFACTS_DIR}"
        "-DINSTALL_ROOT=${INSTALL_ROOT}"
        "-DSRT_SOURCE=${SRT_SOURCE}"
        "-DOPENSSL_SOURCE=${OPENSSL_SOURCE}"
        "-DOPENSSL_ARCHIVE=${OPENSSL_ARCHIVE}"
        "-DOPENSSL_CONFIGURATION=${OPENSSL_CONFIGURATION}"
        "-DCOMPILER_ID=${CMAKE_CXX_COMPILER_ID}"
        "-DCOMPILER_VERSION=${CMAKE_CXX_COMPILER_VERSION}"
        "-DC_COMPILER=${CMAKE_C_COMPILER}"
        "-DCXX_COMPILER=${CMAKE_CXX_COMPILER}"
        "-DREADELF=${CMAKE_READELF}"
        -P "${CMAKE_CURRENT_SOURCE_DIR}/StageNative.cmake"
    DEPENDS srt
    WORKING_DIRECTORY "${REPOSITORY_ROOT}"
    VERBATIM)
