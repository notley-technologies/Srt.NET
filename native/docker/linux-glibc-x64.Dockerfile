# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 Noah Davis
#
# Build environment for the linux-x64 runtime (used by native/build-linux.sh).
#
# manylinux_2_28 is AlmaLinux 8 with glibc 2.28 and the GCC 14 toolset, so the library runs on glibc 2.28 and
# newer (RHEL 8, Debian 10, Ubuntu 20.04 and later). The image is pinned by digest; update tag and digest
# together, then rebuild every Linux runtime.
FROM quay.io/pypa/manylinux_2_28_x86_64:2026.11.07-1@sha256:9555ba6c479fa69a2aa80ad1dc57154a60d562b19df159361d9379a02beb1e36

# Perl modules required by OpenSSL's Configure script and test suite.
RUN dnf -y install \
        perl-Digest-SHA \
        perl-File-Temp \
        perl-IPC-Cmd \
        perl-Math-BigInt \
        perl-Module-Load-Conditional \
        perl-Pod-Html \
        perl-Test-Harness \
        perl-Test-Simple \
        perl-Text-Template \
        perl-Time-HiRes \
        perl-Time-Piece \
        perl-bignum \
    && dnf clean all \
    && rm -rf /var/cache/dnf

# Mount point for a build-tree volume; native/build-linux.sh uses one where host mounts are slow (Docker Desktop on Windows).
RUN install -d -m 1777 /build
