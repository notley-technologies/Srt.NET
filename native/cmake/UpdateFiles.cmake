# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis

# Text edits for UpdateLibsrt.cmake. They work on variables, not files: the update script reads every file first,
# makes every edit, and writes the files only when all edits succeeded, with native/versions.json last. Every edit
# fails unless the text it replaces is found, so a file whose wording changed stops the update instead of leaving a
# stale version behind or a half-updated tree.

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
