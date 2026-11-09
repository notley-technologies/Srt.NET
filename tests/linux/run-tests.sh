#!/usr/bin/env bash
# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis
#
# Runs the managed test suite in a Linux container against a staged native build.
#
# Usage: bash tests/linux/run-tests.sh <rid> <image> [extra dotnet test arguments...]
#   <rid>    linux-x64 | linux-arm64 | linux-musl-x64 | linux-musl-arm64 (artifacts/runtimes/<rid> must exist)
#   <image>  an image with the .NET 10 SDK: mcr.microsoft.com/dotnet/sdk:10.0, or one built from
#            tests/linux/rhel8.Dockerfile (glibc 2.28) or tests/linux/musl.Dockerfile
#   Example: bash tests/linux/run-tests.sh linux-musl-x64 srtnet-test-musl --framework net10.0
#   Tests the whole solution unless the arguments include --project or --solution.
#
# The repository is mounted read-only. The sources (without bin/, obj/ and artifacts/) and the staged runtime
# are copied into the container and built there, so a Linux restore never rewrites the host's obj/ folders.
# Test results are copied to artifacts/test-results/<rid>/<image>/. Runs on Linux and from Git Bash on
# Windows with Docker Desktop.
set -euo pipefail

if [ $# -lt 2 ]; then
    sed -n '8,15s/^# \{0,1\}//p' "${BASH_SOURCE[0]}" >&2
    exit 2
fi

rid=$1
image=$2
shift 2

case "$rid" in
    linux-x64 | linux-musl-x64) platform=linux/amd64 ;;
    linux-arm64 | linux-musl-arm64) platform=linux/arm64 ;;
    *)
        echo "error: '$rid' is not a Linux runtime." >&2
        exit 2
        ;;
esac

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd "$script_dir/../.." && pwd)

if [ ! -f "$repo_root/artifacts/runtimes/$rid/native/libsrt.so" ]; then
    echo "error: artifacts/runtimes/$rid/native/libsrt.so is missing. Run bash native/build-linux.sh $rid first." >&2
    exit 1
fi

results_name=$(printf '%s' "$image" | tr -c 'A-Za-z0-9._-' '-')
results_dir="$repo_root/artifacts/test-results/$rid/$results_name"
mkdir -p "$results_dir"

# Git Bash rewrites arguments that look like POSIX paths (for example /repo), so hand Docker native paths instead.
case "$(uname -s)" in
    MINGW* | MSYS* | CYGWIN*)
        export MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*'
        host_repo=$(cd "$repo_root" && pwd -W)
        host_results=$(cd "$results_dir" && pwd -W)
        ;;
    *)
        host_repo=$repo_root
        host_results=$results_dir
        ;;
esac

# shellcheck disable=SC2016 # Expanded by the container shell.
docker run --rm --init --platform "$platform" \
    --user "$(id -u):$(id -g)" \
    --env HOME=/tmp --env DOTNET_CLI_HOME=/tmp --env NUGET_PACKAGES=/tmp/nuget \
    --env DOTNET_NOLOGO=1 --env DOTNET_CLI_TELEMETRY_OPTOUT=1 --env DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    --env GIT_CONFIG_COUNT=1 --env GIT_CONFIG_KEY_0=safe.directory --env GIT_CONFIG_VALUE_0='*' \
    --volume "$host_repo:/repo:ro" --volume "$host_results:/out" \
    "$image" sh -euc '
        rid=$1
        shift
        work=/tmp/work
        mkdir -p "$work/artifacts/runtimes"
        cd /repo
        tar --exclude=./artifacts --exclude=./.vs --exclude=bin --exclude=obj -cf - . | tar -C "$work" -xf -
        cp -R "/repo/artifacts/runtimes/$rid" "$work/artifacts/runtimes/"

        if [ -d /repo/artifacts/licenses ]; then
            cp -R /repo/artifacts/licenses "$work/artifacts/"
        fi

        cd "$work"
        target="--solution Srt.NET.slnx"

        for argument in "$@"; do
            case "$argument" in
                --project | --solution) target= ;;
            esac
        done

        echo "Testing $rid on $(uname -m) with .NET SDK $(dotnet --version)"
        status=0
        # shellcheck disable=SC2086 # The default target is two words.
        dotnet test $target -c Release -p:NativeRuntimeIdentifier="$rid" \
            --results-directory /tmp/results "$@" || status=$?

        if [ -d /tmp/results ]; then
            cp -R /tmp/results/. /out/
        fi

        exit "$status"
    ' run-tests "$rid" "$@"
