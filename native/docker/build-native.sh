#!/usr/bin/env bash
# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis
#
# Builds libsrt with statically linked OpenSSL for a Linux runtime inside a pinned build container.
#
# Usage: bash native/docker/build-native.sh <rid> [extra cmake configure arguments...]
#   <rid>  linux-x64 | linux-arm64 | linux-musl-x64 | linux-musl-arm64
#   Example: bash native/docker/build-native.sh linux-musl-x64 -DSRTNET_TEST_OPENSSL=ON
#
# Writes artifacts/runtimes/<rid>/native/{libsrt.so,srt.build-info.json}, artifacts/licenses/ and
# artifacts/sbom/native-<rid>.spdx.json. The staging step fails if the library is not portable (see
# native/VerifyElf.cmake).
#
# Runs on Linux and from Git Bash on Windows with Docker Desktop. The container runs as the invoking user
# with the repository mounted at /src and the build tree at /build/<rid>; that fixed path keeps the output
# identical wherever it is built. The build tree is artifacts/native-build/<rid>, except on Windows: Docker
# Desktop's drive mounts are too slow for OpenSSL's many small files, so it lives in the Docker volume
# srtnet-native-build instead (remove it with: docker volume rm srtnet-native-build). Set SRTNET_BUILD_VOLUME
# to another volume name, or to "none" for artifacts/native-build/<rid>.
#
# A runtime for another architecture builds under QEMU emulation, which is much slower; CI builds arm64 on
# arm64 runners. Windows and macOS runtimes are built natively with CMake (see README.md).
set -euo pipefail

usage() {
    sed -n '8,13s/^# \{0,1\}//p' "${BASH_SOURCE[0]}" >&2
}

rid="${1:-}"

case "$rid" in
    linux-x64) dockerfile=linux-glibc-x64.Dockerfile platform=linux/amd64 ;;
    linux-arm64) dockerfile=linux-glibc-arm64.Dockerfile platform=linux/arm64 ;;
    linux-musl-x64) dockerfile=linux-musl.Dockerfile platform=linux/amd64 ;;
    linux-musl-arm64) dockerfile=linux-musl.Dockerfile platform=linux/arm64 ;;
    "" | -h | --help)
        usage
        exit 2
        ;;
    *)
        echo "error: '$rid' is not a Linux runtime. This script builds linux-x64, linux-arm64, linux-musl-x64 and" \
            "linux-musl-arm64; build win-x64, osx-x64 and osx-arm64 natively with CMake (see README.md)." >&2
        exit 2
        ;;
esac

shift
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd "$script_dir/../.." && pwd)

# Git Bash rewrites arguments that look like POSIX paths (for example /src), so hand Docker native paths instead.
case "$(uname -s)" in
    MINGW* | MSYS* | CYGWIN*)
        export MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*'
        host_repo=$(cd "$repo_root" && pwd -W)
        default_volume=srtnet-native-build
        ;;
    *)
        host_repo=$repo_root
        default_volume=none
        ;;
esac

build_volume=${SRTNET_BUILD_VOLUME:-$default_volume}

if [ "$build_volume" = none ]; then
    # Create the directory first; Docker would create a missing bind source owned by root.
    mkdir -p "$repo_root/artifacts/native-build"
    build_volume="$host_repo/artifacts/native-build"
else
    echo "Using Docker volume $build_volume for the build tree (staged outputs still go to artifacts/)."
fi

image="srtnet-native-$rid"
docker build --platform "$platform" --tag "$image" --file "$host_repo/native/docker/$dockerfile" "$host_repo/native/docker"

env_args=(--env HOME=/tmp)

# The build tree may be owned by another uid (CI checkouts, Windows drive mounts); trust it for this container only.
env_args+=(--env GIT_CONFIG_COUNT=1 --env GIT_CONFIG_KEY_0=safe.directory --env GIT_CONFIG_VALUE_0='*')

if [ -n "${SOURCE_DATE_EPOCH:-}" ]; then
    env_args+=(--env "SOURCE_DATE_EPOCH=$SOURCE_DATE_EPOCH")
fi

# shellcheck disable=SC2016 # Expanded by the container shell.
docker run --rm --init --platform "$platform" \
    --user "$(id -u):$(id -g)" "${env_args[@]}" \
    --volume "$host_repo:/src" --volume "$build_volume:/build" --workdir /src \
    "$image" sh -euc '
        rid=$1
        build=/build/$rid
        shift

        if [ -f "$build/CMakeCache.txt" ] && ! grep -qx "CMAKE_HOME_DIRECTORY:INTERNAL=/src/native" "$build/CMakeCache.txt"; then
            echo "error: the $rid build tree was configured outside the build container. Delete" \
                "artifacts/native-build/$rid (or $rid in the build volume) and run this script again." >&2
            exit 1
        fi

        echo "Building $rid with $(cc -dumpmachine) $(cc -dumpfullversion 2>/dev/null || cc -dumpversion)"
        cmake -S /src/native -B "$build" -DSRTNET_RID="$rid" "$@"
        cmake --build "$build" --parallel
    ' build-native "$rid" "$@"
