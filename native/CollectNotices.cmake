# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis

function(collect_notices SOURCE DESTINATION)
    set(NOTICE_IDS "")

    foreach(DIRECTORY IN LISTS ARGN)
        file(GLOB_RECURSE SOURCE_FILES LIST_DIRECTORIES FALSE "${SOURCE}/${DIRECTORY}/*")
        list(SORT SOURCE_FILES)

        foreach(SOURCE_FILE IN LISTS SOURCE_FILES)
            if(NOT SOURCE_FILE MATCHES "\\.(c|cpp|h|hpp|S|pl)$")
                continue()
            endif()

            file(READ "${SOURCE_FILE}" REMAINING)
            string(REPLACE "\r\n" "\n" REMAINING "${REMAINING}")

            # Preserve complete leading license comments, including embedded semicolons.
            while(TRUE)
                string(REGEX REPLACE "^[ \t\r\n]+" "" REMAINING "${REMAINING}")

                if(SOURCE_FILE MATCHES "\\.pl$" AND REMAINING MATCHES "^(#[^\n]*(\n|$))+")
                    set(COMMENT "${CMAKE_MATCH_0}")
                elseif(NOT SOURCE_FILE MATCHES "\\.pl$" AND REMAINING MATCHES "^/\\*")
                    string(FIND "${REMAINING}" "*/" COMMENT_END)

                    if(COMMENT_END EQUAL -1)
                        message(FATAL_ERROR "Unterminated leading comment in ${SOURCE_FILE}")
                    endif()

                    math(EXPR COMMENT_LENGTH "${COMMENT_END} + 2")
                    string(SUBSTRING "${REMAINING}" 0 ${COMMENT_LENGTH} COMMENT)
                elseif(NOT SOURCE_FILE MATCHES "\\.pl$" AND REMAINING MATCHES "^(//[^\n]*(\n|$))+")
                    set(COMMENT "${CMAKE_MATCH_0}")
                else()
                    break()
                endif()

                string(TOLOWER "${COMMENT}" LOWER_COMMENT)

                if(LOWER_COMMENT MATCHES "copyright|redistribution|license|public domain|permission is")
                    string(SHA256 NOTICE_ID "${COMMENT}")
                    file(RELATIVE_PATH RELATIVE_FILE "${SOURCE}" "${SOURCE_FILE}")

                    if(NOT NOTICE_ID IN_LIST NOTICE_IDS)
                        list(APPEND NOTICE_IDS "${NOTICE_ID}")
                        set(NOTICE_${NOTICE_ID} "${COMMENT}")
                        set(FILES_${NOTICE_ID} "${RELATIVE_FILE}")
                    else()
                        string(APPEND FILES_${NOTICE_ID} ", ${RELATIVE_FILE}")
                    endif()
                endif()

                string(LENGTH "${COMMENT}" COMMENT_LENGTH)
                string(SUBSTRING "${REMAINING}" ${COMMENT_LENGTH} -1 REMAINING)
            endwhile()
        endforeach()
    endforeach()

    if(NOT NOTICE_IDS)
        message(FATAL_ERROR "No copyright notices found in ${SOURCE}")
    endif()

    set(NOTICE_TEXT "")

    foreach(NOTICE_ID IN LISTS NOTICE_IDS)
        string(APPEND NOTICE_TEXT "Source files: ${FILES_${NOTICE_ID}}\n\n${NOTICE_${NOTICE_ID}}\n\n")
    endforeach()

    string(RANDOM LENGTH 16 ALPHABET 0123456789abcdef TEMP_SUFFIX)
    set(TEMP_FILE "${DESTINATION}.${TEMP_SUFFIX}.tmp")
    file(WRITE "${TEMP_FILE}" "${NOTICE_TEXT}")
    file(RENAME "${TEMP_FILE}" "${DESTINATION}")
endfunction()
