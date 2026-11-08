# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis
#
# Build environment for the linux-musl-x64 and linux-musl-arm64 runtimes (used by build-native.sh).
#
# Alpine 3.22 is the oldest supported Alpine branch (musl 1.2.5, GCC 14); .NET 10 itself requires musl 1.2.3
# or newer. The digest is the multi-architecture index, so the same line serves both runtimes; build-native.sh
# selects the platform. Update tag and digest together, then rebuild every Linux runtime.
FROM alpine:3.22@sha256:bfe7ceb9f09ec0566c48fb93356e45a47a00be5a1dd14fa5b909e7709c66b034

# build-base provides GCC, binutils (readelf), make, static libstdc++ and fortify-headers; perl runs OpenSSL's
# Configure script and test suite; git fetches libsrt.
RUN apk add --no-cache build-base cmake git linux-headers perl

# Mount point for a build-tree volume; build-native.sh uses one where host mounts are slow (Docker Desktop on Windows).
RUN install -d -m 1777 /build
