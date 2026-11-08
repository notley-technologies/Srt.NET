# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis
#
# Test environment for the linux-musl runtimes: the .NET 10 SDK on Alpine plus the .NET 8 runtime, so the
# net8.0 and net10.0 test targets both run against the musl build of libsrt, and the native toolchain that
# Native AOT publishing needs for the package test (test-package --aot).
#
#   docker build -t srtnet-test-musl -f native/docker/test-musl.Dockerfile native/docker
#   bash native/docker/test-linux.sh linux-musl-x64 srtnet-test-musl
#
# Both images are pinned by multi-architecture digest; update them together with matching Alpine releases.
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine3.24@sha256:31fb46f6ac03922a1156bcd9644b6e31a644add2e919bdea94444b4cc0aca21c AS runtime8

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine3.24@sha256:5562fd626eec39f6f53218f9b7d7856ab7195d51d846568304fbe85071a3b6e6
COPY --from=runtime8 /usr/share/dotnet/shared/Microsoft.NETCore.App/ /usr/share/dotnet/shared/Microsoft.NETCore.App/
RUN apk add --no-cache clang build-base zlib-dev
