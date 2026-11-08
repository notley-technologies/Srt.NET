# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis

cmake_minimum_required(VERSION 3.24)

get_filename_component(ROOT "${CMAKE_CURRENT_LIST_DIR}/.." ABSOLUTE)
set(TOOLS "${ROOT}/artifacts/build-tools")
file(MAKE_DIRECTORY "${TOOLS}/downloads")

function(install_tool NAME URL SHA256)
    get_filename_component(FILENAME "${URL}" NAME)
    set(ARCHIVE "${TOOLS}/downloads/${FILENAME}")
    file(DOWNLOAD "${URL}" "${ARCHIVE}"
        TLS_VERIFY ON EXPECTED_HASH "SHA256=${SHA256}")

    if(NOT EXISTS "${TOOLS}/${NAME}/.verified-${SHA256}")
        file(ARCHIVE_EXTRACT INPUT "${ARCHIVE}" DESTINATION "${TOOLS}/${NAME}")
        file(WRITE "${TOOLS}/${NAME}/.verified-${SHA256}" "${URL}\n")
    endif()
endfunction()

install_tool(perl
    "https://github.com/StrawberryPerl/Perl-Dist-Strawberry/releases/download/SP_54231_64bit/strawberry-perl-5.42.3.1-64bit-portable.zip"
    "6a081a811781c30aca51dbc036afd93092af91e3297901f02c17043795a10690")
install_tool(nasm
    "https://www.nasm.us/pub/nasm/releasebuilds/3.01/win64/nasm-3.01-win64.zip"
    "e0ba5157007abc7b1a65118a96657a961ddf55f7e3f632ee035366dfce039ca4")

message(STATUS "Windows prerequisites are in ${TOOLS}. Add perl/perl/bin and nasm/nasm-3.01 to PATH for this build.")
