#!/usr/bin/env bash
# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis
#
# Runs a command on the runner, or inside the container image named by SRTNET_TEST_IMAGE (the musl and RHEL 8 jobs,
# built from tests/linux/musl.Dockerfile and tests/linux/rhel8.Dockerfile).
# The container runs as the invoking user and mounts the workspace at the same absolute path, so absolute and
# relative paths mean the same thing in both places and every output stays owned by the runner user.
#
# Usage: SRTNET_TEST_IMAGE=<image> bash .github/scripts/test-env.sh <command> [arguments...]
set -euo pipefail

if [ -z "${SRTNET_TEST_IMAGE:-}" ]; then
    exec "$@"
fi

# --init reaps the test hosts and dump helpers that dotnet test starts. MinVer runs git in the container, and the
# GIT_CONFIG_* variables trust the mounted checkout for this container only.
exec docker run --rm --init \
    --user "$(id -u):$(id -g)" \
    --env GIT_CONFIG_COUNT=1 \
    --env GIT_CONFIG_KEY_0=safe.directory \
    --env GIT_CONFIG_VALUE_0='*' \
    --env HOME=/tmp \
    --env DOTNET_CLI_HOME=/tmp \
    --env "NUGET_PACKAGES=$PWD/artifacts/nuget" \
    --env CI \
    --env GITHUB_ACTIONS \
    --env GITHUB_REF_TYPE \
    --env DOTNET_NOLOGO \
    --env DOTNET_CLI_TELEMETRY_OPTOUT \
    --env DOTNET_SKIP_FIRST_TIME_EXPERIENCE \
    --volume "$PWD:$PWD" \
    --workdir "$PWD" \
    "$SRTNET_TEST_IMAGE" "$@"
