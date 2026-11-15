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
#   docker build -t srtnet-test-musl -f tests/linux/musl.Dockerfile tests/linux
#   bash tests/linux/run-tests.sh linux-musl-x64 srtnet-test-musl
#
# Both images are pinned by multi-architecture digest; update them together with matching Alpine releases.
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine3.24@sha256:a088dd404d4b1c7f7cad246d665eccfd5c05780a99a237a8643063d72dca2b0f AS runtime8

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine3.24@sha256:0294f285d062afacc8b5f6d7febb0cd4ee879320e5585fa15f2f4ebf1feb9ba7
COPY --from=runtime8 /usr/share/dotnet/shared/Microsoft.NETCore.App/ /usr/share/dotnet/shared/Microsoft.NETCore.App/
RUN apk add --no-cache clang build-base zlib-dev
