# Contributing to Srt.NET

Thank you for helping improve Srt.NET. This guide covers the toolchain, the build and test commands that CI runs, and the rules every change follows.

## License and contribution terms

Srt.NET is licensed under the [Mozilla Public License 2.0](LICENSE.txt). Bundled libsrt, OpenSSL, and the compiler runtime libraries keep their own licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

- Contributions are accepted under the same license (inbound = outbound): by submitting a pull request you license your contribution under MPL-2.0.
- Sign off every commit (`git commit -s`) to certify the [Developer Certificate of Origin](https://developercertificate.org/): that you wrote the change or otherwise have the right to submit it under the project's license.
- Every source file starts with the MPL notice (Exhibit A of the license). For C# files the build enforces it (`file_header_template` and IDE0073 in `.editorconfig`), and `dotnet format` adds it. Add the same notice, as a comment, to new scripts, native sources, workflows, and MSBuild files.
- Copyright lines follow the notice and are optional. When you write a new file or make a substantial change to one, you may add your own line, `Copyright (c) <year> <name>`, below the existing ones; never remove or change another author's line. In C# files keep the copyright lines in a separate comment after a blank line, so the build's header check still sees the exact MPL notice:

    ```csharp
    // This Source Code Form is subject to the terms of the Mozilla Public
    // License, v. 2.0. If a copy of the MPL was not distributed with this
    // file, You can obtain one at https://mozilla.org/MPL/2.0/.

    // Copyright (c) 2026 Noah Davis
    ```

- Do not copy code under licenses that are incompatible with MPL-2.0. Open an issue before starting significant work.

## Finding something to work on

Issues labelled [good first issue](https://github.com/notley-technologies/Srt.NET/labels/good%20first%20issue) are small, self-contained tasks with a clear definition of done, suitable for a first contribution. [ROADMAP.md](ROADMAP.md) lists the larger plans; comment on an issue before starting so that work is not duplicated.

## Reporting bugs and security issues

Use the issue forms for bugs and feature requests. Report vulnerabilities privately as described in [SECURITY.md](SECURITY.md), never in a public issue.

## Repository layout

| Path | Contents |
| --- | --- |
| `src/Srt.NET/` | The library: public API at the root, `Interop/` (native calls, structures, codecs, handle lifetime), `Async/` (the readiness engine and operation queues), `Polyfills/`, `PublicAPI/` baselines, the package readme and `buildTransitive/` MSBuild targets |
| `tests/Srt.NET.Tests/` | Unit and integration tests against the real libsrt; shared helpers are in `Infrastructure/` |
| `tests/PackageConsumers/` | Consumer application templates that the package tests install the packed package into |
| `tests/linux/` | RHEL 8 and Alpine test containers and `run-tests.sh` |
| `tools/Srt.NET.PackageValidation/` | Package verification and installation tests (`verify-package`, `test-package`, `verify-native`) |
| `benchmarks/Srt.NET.Benchmarks/` | BenchmarkDotNet benchmarks |
| `examples/` | Asynchronous and synchronous client and server examples |
| `native/` | The native build of libsrt and OpenSSL; see [native/README.md](native/README.md) |
| `docs/` | User documentation, packed into the NuGet package |
| `build/` | MSBuild settings shared by tests, examples and benchmarks |
| `.github/` | Workflows, Dependabot, CODEOWNERS, issue and pull request templates |

Build outputs go to `bin/`, `obj/` and `artifacts/`, which git ignores.

## Prerequisites

| Tool | Needed for |
| --- | --- |
| .NET 10 SDK (the version `global.json` selects) and the .NET 8 runtime | Building and testing (tests run on .NET 8 and .NET 10) |
| Git | MinVer computes versions from tags, so clone the full history (no `--depth`) |
| CMake 3.24 or later | Windows and macOS native builds |
| Docker | Linux native builds, musl and RHEL 8 test containers, workflow linting |
| Windows: Visual Studio 2022 with the x64 C++ tools | `win-x64` native build (`native/build-windows.cmd` downloads pinned Perl and NASM) |
| macOS: Xcode command line tools, Perl, Make | `osx-x64` and `osx-arm64` native builds |
| clang and zlib development headers (Linux) | Native AOT package tests (`apt-get install clang zlib1g-dev`) |

Run `dotnet tool restore` once to install the local tools in `.config/dotnet-tools.json` (ReportGenerator).

## Building native libraries

Tests and packages need the native library for your runtime in `artifacts/runtimes/<rid>/native/`. CMake downloads the pinned OpenSSL release, verifies it, and builds libsrt against it; both sources stay unmodified. Each build also writes `srt.build-info.json`, the dependency notices in `artifacts/licenses/`, and an SPDX document in `artifacts/sbom/native-<rid>.spdx.json`.

**Linux** (`linux-x64`, `linux-arm64`, `linux-musl-x64`, `linux-musl-arm64`) builds run in pinned containers (manylinux_2_28 for glibc, Alpine for musl) as your user, from Linux or from Git Bash on Windows with Docker Desktop:

```bash
bash native/build-linux.sh linux-x64
bash native/build-linux.sh linux-arm64
bash native/build-linux.sh linux-musl-x64
bash native/build-linux.sh linux-musl-arm64
```

The script fails if the library is not portable: it must need glibc 2.28 or older (or musl), must not depend on `libstdc++` or `libgcc_s`, and must export only `srt_*` symbols. A runtime for another CPU architecture builds under QEMU emulation, which is much slower; CI builds each architecture on a matching runner.

**Windows** (`win-x64`), from any command prompt. The script finds Visual Studio 2022 with the x64 C++ tools through `vswhere`, enters its developer environment, downloads the pinned Perl and NASM, and builds in `artifacts\native-build\win-x64`:

```bat
native\build-windows.cmd
```

**macOS** (`osx-x64` on Intel, `osx-arm64` on Apple Silicon):

```bash
bash native/build-macos.sh osx-arm64
```

CI runs the same three scripts. Each passes extra arguments to the CMake configure step: add `-DSRTNET_TEST_OPENSSL=ON` to also run OpenSSL's test suite, as CI does (it is slow), or `-DSRTNET_BUILD_JOBS=8` to limit parallel native work.

## Testing

Tests use xUnit.net v3 on Microsoft.Testing.Platform, which `global.json` selects for `dotnet test`. They make real native connections and fail if the native library is missing. Windows also runs them on .NET Framework 4.8.

```bash
dotnet test --solution Srt.NET.slnx -c Release
dotnet test --solution Srt.NET.slnx -c Release -p:NativeRuntimeIdentifier=linux-arm64
```

`NativeRuntimeIdentifier` selects which `artifacts/runtimes/<rid>` library the tests load; pass it whenever the SDK's runtime identifier is not one of the seven Srt.NET runtimes (for example `rhel.8-x64`). Test-platform options go after `--`. CI runs:

```bash
dotnet test --solution Srt.NET.slnx -c Release -p:NativeRuntimeIdentifier=<rid> \
  --results-directory artifacts/test-results -- \
  --report-xunit-trx \
  --coverage --coverage-output-format cobertura --coverage-settings "$PWD/tests/coverage.config" \
  --crashdump --crashdump-type Mini \
  --hangdump --hangdump-timeout 20m --hangdump-type-if-supported Mini
dotnet reportgenerator "-reports:artifacts/test-results/**/*.cobertura.xml" -targetdir:artifacts/coverage \
  "-reporttypes:HtmlInline;MarkdownSummaryGithub"
```

CI collects coverage on `win-x64`, `linux-x64`, and `osx-x64` and writes the ReportGenerator summary to each job's summary page. The coverage extension has no arm64 instrumentation for Linux or macOS, and in the musl test container (a non-root user) .NET 8 cannot create its named mutex, so those jobs run without `--coverage`. Use xUnit's `--filter-class`, `--filter-method`, or `--filter-trait` options after `--` to run a subset.

**musl and RHEL 8.** CI runs the musl tests in an Alpine container and the `linux-x64` tests again on RHEL 8 (glibc 2.28, the oldest supported baseline):

```bash
docker build -t srtnet-test-musl -f tests/linux/musl.Dockerfile tests/linux
docker build -t srtnet-test-rhel8 -f tests/linux/rhel8.Dockerfile tests/linux
bash tests/linux/run-tests.sh linux-musl-x64 srtnet-test-musl
bash tests/linux/run-tests.sh linux-x64 srtnet-test-rhel8
```

`run-tests.sh` copies the sources into the container and builds there, so it never rewrites your `obj/` folders, and copies the results to `artifacts/test-results/<rid>/<image>/`. Extra arguments are passed to `dotnet test`. CI uses `.github/scripts/test-env.sh` instead, which runs commands (tests, packing, and package tests) in the container with the checkout mounted read-write; only use it in a clone that you do not also build on Windows or macOS.

## Formatting and analyzers

`.editorconfig` defines formatting and language preferences; builds enforce code style, the .NET analyzers, selected StyleCop rules, and public API tracking, with warnings treated as errors. Before pushing:

```bash
dotnet format Srt.NET.slnx --verify-no-changes   # dotnet format Srt.NET.slnx applies the fixes
```

Markdown is formatted with [Prettier](https://prettier.io/) (`.prettierrc.json`), which writes each paragraph and list item on one line instead of wrapping it at a fixed width. Editors soft-wrap long lines for reading. CI checks it with the same pinned version (needs Node.js):

```bash
npx prettier@3.6.2 --check "**/*.md"   # --write applies the formatting
```

Workflow changes must pass actionlint (CI runs the same pinned image):

```bash
docker run --rm -v "$PWD:/repo" -w /repo rhysd/actionlint:1.7.12 -color
```

## Packaging

Pack the runtimes you have built and test the package as an application would consume it:

```bash
dotnet pack src/Srt.NET/Srt.NET.csproj -c Release -o artifacts/packages -p:RequireAllNativeRuntimes=false
dotnet run --project tools/Srt.NET.PackageValidation -c Release -- verify-package artifacts/packages/Srt.NET.<version>.nupkg linux-x64
dotnet run --project tools/Srt.NET.PackageValidation -c Release -- test-package artifacts/packages/Srt.NET.<version>.nupkg linux-x64 --aot
```

`verify-package` checks managed and native assets, architectures, exports, hashes, and notices; with no runtime arguments it requires all seven runtimes. `test-package` installs the package into fresh consumer applications and runs them; `--aot` also publishes and runs a Native AOT consumer for the host runtime (it needs a Microsoft-built SDK and the native toolchain listed under prerequisites). Never distribute a partial package (`RequireAllNativeRuntimes=false`) or the `Srt.NET.PackageWrapper` test fixture. A release pack needs all seven runtimes and adds an SPDX SBOM with `-p:GenerateSBOM=true`. See [tools/README.md](tools/README.md).

## Rules for changes

- **Tests.** Add or update tests for every behavior change. Native-facing changes need tests that make real libsrt calls.
- **Public API.** Public API changes must be recorded in `src/Srt.NET/PublicAPI/<target framework>/PublicAPI.Unshipped.txt` for each target framework (`netstandard2.0` and `net8.0`); the build fails otherwise (RS0016 and related rules). `dotnet format analyzers src/Srt.NET/Srt.NET.csproj --diagnostics RS0016` adds missing entries. Never add optional parameters to a method that has shipped: add an overload instead.
- **CHANGELOG.** Add an entry under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md) for every user-visible change, using the Keep a Changelog sections (Added, Changed, Deprecated, Removed, Fixed, Security). Describe changes relative to the last published release; until the first release, describe current behavior under Added and Security. After that, record bundled native updates under Security when they fix vulnerabilities and under Changed otherwise. The section becomes the GitHub release notes, so link to repository files with absolute `https://github.com/notley-technologies/Srt.NET/blob/main/...` URLs.
- **Documentation.** Keep `README.md`, the package readme `src/Srt.NET/PACKAGE.md`, `docs/`, and XML documentation comments in step with the code.
- **Documentation-only pull requests.** A pull request that changes only files on the allow-list in the `changes` job of [build.yml](.github/workflows/build.yml) (READMEs outside the package, CONTRIBUTING, SECURITY, CODEOWNERS, and the issue and pull request templates) skips the build, test, and package jobs; lint still runs. Files packed into the package or read by the release are never skippable.
- **Workflows.** Pin every action to a full commit SHA with a `# vX.Y.Z` comment (Dependabot updates both), keep `persist-credentials: false` on checkout, and grant each job only the permissions it needs.
- **Pull requests.** Keep each pull request focused, fill in the template, and make sure the `CI result` check passes.

## Updating native dependencies

libsrt and OpenSSL are pinned in `native/versions.json`, which Dependabot cannot update. The [Native dependency updates](.github/workflows/native-updates.yml) workflow compares the pins with upstream releases every day and opens one pull request per component when a newer release exists: OpenSSL releases on the pinned 3.5 LTS line, and libsrt releases tagged `vX.Y.Z` (release candidates are ignored), preferring the newest patch on the pinned minor line. Each pull request runs the update script below, so CI builds the new sources on every runtime and runs the native ABI check, the full test suite, the RHEL 8 and Alpine compatibility tests and the package tests. Patch updates are set to merge automatically once the required checks pass; a new libsrt minor or major release waits for review on its own branch (`native-updates/libsrt-<major.minor>`), so a patch release never takes over that pull request. While an update is pending, the workflow refreshes its pull request daily onto the latest `main`. To hold a patch update, turn off auto-merge on its pull request; it stays on hold until a newer release replaces it. To decline a release, close its pull request without merging; the workflow does not reopen it, and the next release opens a new one. Once someone else pushes to an update branch, the workflow leaves that pull request alone. If the workflow cannot open or refresh a pull request (the [native dependency update App](#one-time-setup) is missing, or an update fails), it opens a tracking issue instead.

The update scripts can also be run by hand:

- **OpenSSL** (needs GnuPG): `cmake -DOPENSSL_VERSION=3.5.<n> -P native/cmake/UpdateOpenSSL.cmake`. It downloads the official archive, verifies its signature against `native/openssl-release-key.asc` and its version, then records the version and SHA-256 in `native/versions.json` and updates `THIRD_PARTY_NOTICES.md`, `SECURITY.md` and the CHANGELOG. Signing-key changes need separate review against the [OpenSSL release-signing fingerprints](https://openssl-library.org/source/).
- **libsrt**: `cmake -DSRT_VERSION=<x.y.z> -P native/cmake/UpdateLibsrt.cmake`. It resolves the release tag to its commit, refuses a release tag that moved away from the pinned commit, then records the version and commit in `native/versions.json`, raises the statistics version check (`NativeAbi.VerifiedVersion`), and updates `THIRD_PARTY_NOTICES.md`, `SECURITY.md`, the libsrt documentation links and the CHANGELOG. The native build compiles `native/abi/managed_abi.c` against the new headers, so a release that changes a checked structure layout, field type or constant fails CI instead of being bundled.
- The full CI matrix must pass. Changes under `native/` invalidate the CI native cache automatically; the Windows and macOS keys also include the runner image version.
- Until the first release, the scripts update the versions in the "Bundles libsrt ... with OpenSSL ... linked statically" bullet under Security, and stop if that bullet was reworded. After a release, they add, or extend, an "Updates the bundled ..." bullet under Changed; a bullet someone edited is kept and a new one is added. Before the next release, read the upstream release notes and move the entry under Security when the update fixes vulnerabilities. OpenSSL security fixes require a new Srt.NET release even when the managed code is unchanged.

Build container images are pinned by digest in `native/docker/`. Dependabot proposes digest refreshes and manylinux date tags, and each update rebuilds the Linux runtimes. Moving to a new .NET major or minor version or a new Alpine branch is excluded in `.github/dependabot.yml` and done by hand, together with `global.json`, the test target frameworks, and the musl baseline in the README.

The netstandard2.0 dependency minimums (Microsoft.Bcl.AsyncInterfaces 8.0.0 and System.Memory 4.5.5) are set with `VersionOverride` in `src/Srt.NET/Srt.NET.csproj`, and Dependabot ignores both packages. Raise them only on purpose, with a CHANGELOG entry. Their central versions in `Directory.Packages.props` only pin what tests and tools resolve, and are updated by hand.

## Code review

Every change reaches `main` through a pull request. Patch updates of the bundled native dependencies can merge before review (see [updating native dependencies](#updating-native-dependencies)) and are reviewed before the next release. Reviewers check, and authors should check before asking for review:

- **Correctness:** the change does what it says, including error paths, cancellation, disposal and concurrent use; native calls match the bundled libsrt's C API and its threading rules.
- **Tests:** behaviour changes come with tests that make real libsrt calls and would fail without the change; timing-sensitive tests bound their waits and do not depend on scheduling luck.
- **Security:** inputs from the network, libsrt and callers are validated; native memory is accessed only through bounded, pinned buffers and socket leases; secrets are not logged; the [security model](docs/security.md) still holds.
- **Compatibility:** public API changes are recorded in the PublicAPI files, and shipped signatures are not broken; package, platform and dependency changes keep the package tests passing.
- **Documentation:** README, the package readme, `docs/` and XML comments describe the new behaviour, and the CHANGELOG has an entry for user-visible changes.
- **Supply chain:** new dependencies, actions and images are pinned, licensed compatibly with MPL-2.0, and justified.

Reviewers approve only when every point holds, and leave comments for anything that does not. The [governance](GOVERNANCE.md) page describes who can approve.

## Code conventions

- Use explicit types (no `var`), target-typed `new()`, and collection expressions where the target type is known.
- Give each type its own file and use file-scoped namespaces.
- Put braces on separate lines, separate logical steps with blank lines, and put a blank line before a comment that follows code.
- Keep comments focused on constraints and decisions rather than restating the code.
- Library code uses `ConfigureAwait(false)` and forwards cancellation tokens; tests bound waits with explicit deadlines.
- Keep native handles and mutable native structures out of the public API.

## Releases (maintainers)

The repository is [notley-technologies/Srt.NET](https://github.com/notley-technologies/Srt.NET), and the package is published to nuget.org by the [NotleyTechnologies](https://www.nuget.org/profiles/NotleyTechnologies) organization.

### One-time setup

- **Maintainers team.** `.github/CODEOWNERS` names the `notley-technologies/srt-net-maintainers` team. It must be a visible team with Write (or higher) access to the repository.
- **Signing.** Maintainers sign release tags (`git tag -s`), so each needs a GPG or SSH signing key configured in git (`user.signingkey`, and `gpg.format ssh` for SSH keys) and added to their GitHub account.
- **Executable scripts.** Windows checkouts do not record the executable bit. When a shell script is added, mark it in the index before committing: `git update-index --chmod=+x <script>`.
- **`nuget` environment** (Settings > Environments): required reviewers from the maintainers team, deployments only from tags matching `v*`, and an environment variable `NUGET_USER` set to the nuget.org username (not the email address) of the account that created the trusted publishing policy.
- **Native dependency update App.** The native dependency workflow opens its pull requests with a GitHub App token, because pull requests opened with the workflow's own token only start CI runs that wait for approval, and its commits must be signed. An organization owner creates the App once: Organization settings > Developer settings > GitHub Apps > New GitHub App, with the repository URL as the homepage, the webhook turned off, the repository permissions **Contents: Read and write** and **Pull requests: Read and write**, and installation limited to this account. Generate a private key, install the App on this repository only, then store its Client ID and key: `gh variable set NATIVE_UPDATES_CLIENT_ID --body <client-id>` and `gh secret set NATIVE_UPDATES_PRIVATE_KEY < <key>.pem`. Set the variable `NATIVE_UPDATES_AUTOMERGE` to `false` to review every update before it merges. Auto-merge relies on the `main` ruleset's required checks; keep them in place.
- **Trusted publishing** on nuget.org, under the NotleyTechnologies organization: repository owner `notley-technologies`, repository `Srt.NET`, workflow file `build.yml`, environment `nuget`.
- **Rulesets** (after the first green CI and dependency review runs, so the checks exist): `main` requires a pull request (no approvals), the `CI result` and `dependency-review` checks with branches up to date before merging, signed commits, CodeQL code scanning results and squash merges, and blocks force pushes and deletion. Native dependency auto-merge relies on these rules. Tags matching `v*` can only be created by maintainers and cannot be updated or deleted.
- **OpenSSF Scorecard.** The [Scorecard workflow](.github/workflows/scorecard.yml) publishes results to the [Scorecard viewer](https://scorecard.dev/viewer/?uri=github.com/notley-technologies/Srt.NET); run it with `gh workflow run scorecard.yml` after changing settings. Several checks reflect repository settings and history rather than files:
    - **Branch-Protection** scores the `main` protection in tiers: (1) block force pushes and deletion; (2) require a pull request with at least one approval, approval of the most recent push, and an up-to-date branch before merging; (3) require at least one status check (`CI result`); (4) require two approvals and code owner review; (5) dismiss stale approvals on new commits and allow no administrator bypass. Approvals need a second maintainer: until then a sole maintainer cannot approve their own pull requests, so tiers 2 and above stay out of reach.
    - **Code-Review** and **CI-Tests** score recent merges: merge every change through a pull request that passes CI and, once there is a second maintainer, has an approving review.
    - **CII-Best-Practices:** the project is registered at [bestpractices.dev](https://www.bestpractices.dev/projects/16461), which Scorecard looks up by repository URL. Keep its answers current when the release, security, or testing process changes.
    - **Signed-Releases:** each release attaches the Sigstore bundles and the provenance envelope (`*.intoto.jsonl`), so keep publishing releases through the release workflow. Newer Scorecard versions also credit [immutable releases](https://docs.github.com/repositories/releasing-projects-on-github/about-releases), which also protect the attached assets; the release workflow only replaces draft releases, so it works with them.
    - **Maintained** requires the repository to be at least 90 days old with recent commit or issue activity. **Contributors** counts the companies or organizations on the GitHub profiles of recent commit authors; settings cannot change it.

### Each release

1. Review the native dependency updates merged since the last release (pull requests labelled `native-dependencies`): read the upstream release notes and changes, move CHANGELOG entries under Security when an update fixes vulnerabilities, and check `srt.h` for new functions and `SRTO_*` options to add to [the API coverage audit](docs/api-coverage.md).
2. In `CHANGELOG.md`, rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` and add a new empty `## [Unreleased]` above it. At the end of the file, add `[X.Y.Z]: https://github.com/notley-technologies/Srt.NET/releases/tag/vX.Y.Z` and change the `[Unreleased]` link to `https://github.com/notley-technologies/Srt.NET/compare/vX.Y.Z...HEAD`. The release workflow publishes that section as the release notes and fails without it.
3. Move every entry from each target framework's `PublicAPI.Unshipped.txt` into its `PublicAPI.Shipped.txt`, leaving only `#nullable enable` in the Unshipped files, and build to confirm there are no RS0016, RS0017, or RS0025 errors. Tag builds fail while an Unshipped file has entries.
4. Review `src/Srt.NET/PACKAGE.md`, the package readme that nuget.org shows. It ships inside the package and cannot be changed after publishing, so it must describe this release, use absolute `https://` links, and use only images from domains that nuget.org allows. Tag builds pin its `blob/main` links to the release tag.
5. Merge to `main` and wait for a green `CI result` run on that commit. Then create and push the signed tag `vX.Y.Z` on it (`git tag -s vX.Y.Z`, `git push origin vX.Y.Z`).
6. The CI workflow rebuilds every native library from source, tests, packs with an SBOM, attests the outputs, and creates a draft GitHub release with the CHANGELOG section as its notes.
7. Approve the `nuget` environment deployment. The workflow publishes to nuget.org with trusted publishing and then publishes the GitHub release.
8. Once nuget.org lists the new version, set `<PackageValidationBaselineVersion>` in `src/Srt.NET/Srt.NET.csproj` to it in a follow-up commit, so package validation compares later builds with the published API. Never set a version that is not on nuget.org yet: restore downloads the baseline package and fails. Record an intentional breaking change in a `CompatibilitySuppressions.xml` file (`dotnet pack -p:ApiCompatGenerateSuppressionFile=true` writes it) instead of removing the baseline.

Tag builds fail if the package has no license; `verify-package` also checks that its license expression is MPL-2.0.

If a release run fails, fix the cause and re-run the failed jobs. The release job replaces a draft release left by an earlier attempt. Never delete or move a pushed tag: if the tagged commit itself is wrong, release a new patch version instead.
