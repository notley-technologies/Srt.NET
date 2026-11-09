# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis

# Text edits shared by UpdateLibsrt.cmake and UpdateOpenSSL.cmake. They work on variables, not files: the update
# scripts read every file first, make every edit, and write the files only when all edits succeeded, with
# native/versions.json last. Every edit fails unless the text it replaces is found, so a file whose wording changed
# stops the update instead of leaving a stale version behind or a half-updated tree.

# Replaces the single occurrence of OLD in the variable named TEXT_VAR. WHAT names the file in error messages.
function(srtnet_replace_once TEXT_VAR WHAT OLD NEW)
    string(FIND "${${TEXT_VAR}}" "${OLD}" _srtnet_first)
    string(FIND "${${TEXT_VAR}}" "${OLD}" _srtnet_last REVERSE)

    if(_srtnet_first EQUAL -1 OR NOT _srtnet_first EQUAL _srtnet_last)
        message(FATAL_ERROR "Expected exactly one occurrence of \"${OLD}\" in ${WHAT}.")
    endif()

    string(REPLACE "${OLD}" "${NEW}" _srtnet_result "${${TEXT_VAR}}")
    set(${TEXT_VAR} "${_srtnet_result}" PARENT_SCOPE)
endfunction()

# Replaces every occurrence of OLD in the variable named TEXT_VAR, of which there must be at least one.
function(srtnet_replace_all TEXT_VAR WHAT OLD NEW)
    string(FIND "${${TEXT_VAR}}" "${OLD}" _srtnet_first)

    if(_srtnet_first EQUAL -1)
        message(FATAL_ERROR "Expected \"${OLD}\" in ${WHAT}.")
    endif()

    string(REPLACE "${OLD}" "${NEW}" _srtnet_result "${${TEXT_VAR}}")
    set(${TEXT_VAR} "${_srtnet_result}" PARENT_SCOPE)
endfunction()

# Records an update of a bundled component in the Unreleased section of the CHANGELOG text in TEXT_VAR.
#
# Until the first release, an unreleased bullet describes the bundled versions (BUNDLED_TEXT, for example
# "Bundles libsrt 1.5.7 (commit `...`)"); it is edited in place to UPDATED_TEXT, and the update fails if it was reworded.
# After a release, the section gets a "- Updates the bundled <component> from <old> to <new><NEW_DETAIL>" bullet under
# Changed. A later update before the next release extends that bullet only while it is still exactly the generated text
# (OLD_DETAIL describes the version it named); a bullet that a maintainer edited is kept and a new bullet is added.
function(srtnet_changelog TEXT_VAR COMPONENT OLD_VERSION NEW_VERSION BUNDLED_TEXT UPDATED_TEXT OLD_DETAIL NEW_DETAIL)
    if(OLD_VERSION STREQUAL NEW_VERSION)
        return()
    endif()

    set(_srtnet_text "${${TEXT_VAR}}")
    string(FIND "${_srtnet_text}" "\n## [Unreleased]\n" _srtnet_start)

    if(_srtnet_start EQUAL -1)
        message(FATAL_ERROR "CHANGELOG.md has no \"## [Unreleased]\" section.")
    endif()

    math(EXPR _srtnet_start "${_srtnet_start} + 1")
    string(SUBSTRING "${_srtnet_text}" ${_srtnet_start} -1 _srtnet_rest)
    string(LENGTH "## [Unreleased]\n" _srtnet_heading)
    string(SUBSTRING "${_srtnet_rest}" ${_srtnet_heading} -1 _srtnet_body)

    # The section ends at the next release heading or at the link definitions.
    string(LENGTH "${_srtnet_body}" _srtnet_end)

    foreach(_srtnet_marker IN ITEMS "\n## [" "\n[Unreleased]:")
        string(FIND "${_srtnet_body}" "${_srtnet_marker}" _srtnet_position)

        if(NOT _srtnet_position EQUAL -1 AND _srtnet_position LESS _srtnet_end)
            set(_srtnet_end ${_srtnet_position})
        endif()
    endforeach()

    math(EXPR _srtnet_end "${_srtnet_end} + ${_srtnet_heading}")
    string(SUBSTRING "${_srtnet_rest}" 0 ${_srtnet_end} _srtnet_section)
    string(SUBSTRING "${_srtnet_rest}" ${_srtnet_end} -1 _srtnet_tail)
    string(SUBSTRING "${_srtnet_text}" 0 ${_srtnet_start} _srtnet_head)

    string(REGEX MATCH "\n## \\[[0-9]+\\.[0-9]+\\.[0-9]+\\]" _srtnet_released "${_srtnet_text}")
    string(FIND "${_srtnet_section}" "${BUNDLED_TEXT}" _srtnet_first)
    string(FIND "${_srtnet_section}" "${BUNDLED_TEXT}" _srtnet_last REVERSE)
    string(REPLACE "." "\\." _srtnet_old_pattern "${OLD_VERSION}")
    set(_srtnet_prefix "- Updates the bundled ${COMPONENT} from ")
    set(_srtnet_done FALSE)

    if(NOT _srtnet_first EQUAL _srtnet_last)
        message(FATAL_ERROR "\"${BUNDLED_TEXT}\" appears more than once in the Unreleased section of CHANGELOG.md.")
    elseif(NOT _srtnet_first EQUAL -1)
        string(REPLACE "${BUNDLED_TEXT}" "${UPDATED_TEXT}" _srtnet_section "${_srtnet_section}")
        set(_srtnet_done TRUE)
    elseif(_srtnet_released STREQUAL "")
        # Until the first release that bullet is the only record of the bundled version, so it must not go stale.
        message(FATAL_ERROR "The Unreleased section of CHANGELOG.md no longer contains \"${BUNDLED_TEXT}\". "
            "Update the bullet that describes the bundled versions by hand.")
    elseif(_srtnet_section MATCHES "\n- Updates the bundled ${COMPONENT} from ([0-9.]+) to ${_srtnet_old_pattern}[^\n]*")
        set(_srtnet_previous "${CMAKE_MATCH_0}")
        set(_srtnet_from "${CMAKE_MATCH_1}")

        if(_srtnet_previous STREQUAL "\n${_srtnet_prefix}${_srtnet_from} to ${OLD_VERSION}${OLD_DETAIL}")
            string(REPLACE "${_srtnet_previous}" "\n${_srtnet_prefix}${_srtnet_from} to ${NEW_VERSION}${NEW_DETAIL}"
                _srtnet_section "${_srtnet_section}")
            set(_srtnet_done TRUE)
        endif()
    endif()

    if(NOT _srtnet_done)
        set(_srtnet_bullet "${_srtnet_prefix}${OLD_VERSION} to ${NEW_VERSION}${NEW_DETAIL}")
        string(FIND "${_srtnet_section}" "\n### Changed\n\n" _srtnet_changed)

        if(NOT _srtnet_changed EQUAL -1)
            string(REPLACE "\n### Changed\n\n" "\n### Changed\n\n${_srtnet_bullet}\n" _srtnet_section "${_srtnet_section}")
        else()
            # Keep a Changelog orders the groups Added, Changed, Deprecated, Removed, Fixed, Security.
            string(LENGTH "${_srtnet_section}" _srtnet_insert)

            foreach(_srtnet_group IN ITEMS Deprecated Removed Fixed Security)
                string(FIND "${_srtnet_section}" "\n### ${_srtnet_group}\n" _srtnet_position)

                if(NOT _srtnet_position EQUAL -1 AND _srtnet_position LESS _srtnet_insert)
                    set(_srtnet_insert ${_srtnet_position})
                endif()
            endforeach()

            string(SUBSTRING "${_srtnet_section}" 0 ${_srtnet_insert} _srtnet_before)
            string(SUBSTRING "${_srtnet_section}" ${_srtnet_insert} -1 _srtnet_after)
            # The section ends with one newline; the blank line before the next heading starts the tail.
            string(REGEX REPLACE "\n+$" "" _srtnet_before "${_srtnet_before}")
            set(_srtnet_section "${_srtnet_before}\n\n### Changed\n\n${_srtnet_bullet}\n${_srtnet_after}")
        endif()
    endif()

    set(${TEXT_VAR} "${_srtnet_head}${_srtnet_section}${_srtnet_tail}" PARENT_SCOPE)
endfunction()
