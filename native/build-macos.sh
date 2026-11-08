#!/usr/bin/env bash
# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis
#
# Builds libsrt with statically linked OpenSSL for a macOS runtime on a Mac of the same architecture.
#
# Usage: bash native/build-macos.sh <rid> [extra cmake configure arguments...]
#   <rid>  osx-x64 (Intel) | osx-arm64 (Apple Silicon)
#   Example: bash native/build-macos.sh osx-arm64 -DSRTNET_TEST_OPENSSL=ON
#
# Needs the Xcode command line tools, CMake 3.24 or later, Perl and Make. Writes
# artifacts/runtimes/<rid>/native/{libsrt.dylib,srt.build-info.json}, artifacts/licenses/ and
# artifacts/sbom/native-<rid>.spdx.json, and builds in artifacts/native-build/<rid>. CI runs this script; its native
# cache key combines a hash of native/ with the arguments the workflow passes.
set -euo pipefail

rid="${1:-}"

case "$rid" in
    osx-x64 | osx-arm64) ;;
    "" | -h | --help)
        sed -n '8,12s/^# \{0,1\}//p' "${BASH_SOURCE[0]}" >&2
        exit 2
        ;;
    *)
        echo "error: '$rid' is not a macOS runtime. This script builds osx-x64 and osx-arm64." >&2
        exit 2
        ;;
esac

shift
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

cmake -S native -B "artifacts/native-build/$rid" "-DSRTNET_RID=$rid" "$@"
cmake --build "artifacts/native-build/$rid" --config Release --parallel
