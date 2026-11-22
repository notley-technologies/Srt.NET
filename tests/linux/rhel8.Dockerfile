# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis
#
# Compatibility test environment for the glibc runtimes: RHEL 8 (glibc 2.28, the oldest supported baseline)
# with Red Hat's .NET 10 SDK and .NET 8 runtime, so the net8.0 and net10.0 test targets both run.
#
#   docker build -t srtnet-test-rhel8 -f tests/linux/rhel8.Dockerfile tests/linux
#   bash tests/linux/run-tests.sh linux-x64 srtnet-test-rhel8
#
# The SDK reports the rhel.8-x64 runtime identifier, so always pass -p:NativeRuntimeIdentifier explicitly.
FROM registry.access.redhat.com/ubi8/ubi:8.10@sha256:31854d90b8a474b0253a2d4a1f71659097654d5992d6d8b811a679f4ff0084bc

RUN dnf -y install dotnet-sdk-10.0 dotnet-runtime-8.0 git tar \
    && dnf clean all \
    && rm -rf /var/cache/dnf
