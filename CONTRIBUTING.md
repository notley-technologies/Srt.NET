# Contributing to Srt.NET

Thank you for helping improve Srt.NET. This guide covers the toolchain, the build and test commands that CI runs, and the rules every change follows. Repository settings, releases and native dependency updates are maintainer tasks, described in [MAINTAINING.md](MAINTAINING.md).

## Quick start for managed changes

Changes to the managed library, tests, examples or documentation do not need your own native build: CI uploads the native library it builds for each runtime as an artifact named `native-<rid>`.

1. Install the .NET SDK and runtimes listed under [prerequisites](#prerequisites) and the [GitHub CLI](https://cli.github.com/), then run `gh auth login`; downloading artifacts needs it even for a public repository.
2. Find a successful run of the CI workflow for a push to `main` at the commit your branch is based on (`--commit` needs the full SHA, for example from `git merge-base HEAD upstream/main`, where `upstream` is the remote for this repository), or rebase onto the commit of a recent successful run. The run must match your base, because `AbiTests` checks that the libsrt version in the downloaded `srt.build-info.json` equals `NativeAbi.VerifiedVersion` in your source. Use only push runs: pull request runs from forks also list `main` as their branch when the fork's branch has that name.

    ```bash
    gh run list --repo notley-technologies/Srt.NET --workflow build.yml --branch main --event push --status success --limit 5
    gh run list --repo notley-technologies/Srt.NET --workflow build.yml --branch main --event push --status success --commit <sha>
    ```

3. Download the native library for your runtime (`win-x64`, `linux-x64`, `linux-arm64`, `linux-musl-x64`, `linux-musl-arm64`, `osx-x64` or `osx-arm64`) into `artifacts/`. This creates `artifacts/runtimes/<rid>/native/`, where `build/NativeRuntime.props` looks for it, with the dependency notices and the native SBOM. `gh run download` does not overwrite files, so first remove those from an earlier download or native build:

    ```bash
    rm -rf artifacts/runtimes/<rid> artifacts/licenses artifacts/sbom
    gh run download <run-id> --repo notley-technologies/Srt.NET --name native-<rid> --dir artifacts
    ```

4. Run the tests:

    ```bash
    dotnet test --solution Srt.NET.slnx -c Release
    ```

CI keeps these artifacts for 90 days. Changes under `native/`, including `native/abi/managed_abi.c`, still need a [native build](#building-native-libraries). Without a native library, building the solution fails early, at the examples' native library check.

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
| `tools/Srt.NET.PackageValidation/` | Package verification and installation tests (`verify-package`, `test-package`) |
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
| GitHub CLI (`gh`) | Downloading CI's native libraries in the [quick start](#quick-start-for-managed-changes) |
| CMake 3.24 or later | Windows and macOS native builds |
| Docker | Linux native builds, musl and RHEL 8 test containers, workflow linting |
| Windows: Visual Studio 2022 or 2026 with the x64 C++ tools | `win-x64` native build (`native/build-windows.cmd` downloads pinned Perl and NASM) |
| Windows: Git Bash (part of Git for Windows) | Running the `.sh` scripts, such as the Linux native builds and the test containers |
| macOS: Xcode command line tools, Perl, Make | `osx-x64` and `osx-arm64` native builds |
| clang and zlib development headers (Linux) | Native AOT package tests (`apt-get install clang zlib1g-dev`) |
| Node.js (optional) | Checking Markdown formatting with Prettier before CI does |

Only coverage reports need the local tools in `.config/dotnet-tools.json` (ReportGenerator); `dotnet tool restore` installs them.

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

**Windows** (`win-x64`), from any command prompt. The script finds the newest Visual Studio 2022 or 2026 with the x64 C++ tools through `vswhere` (in an x64 developer prompt, it uses that prompt's Visual Studio), enters its developer environment, downloads the pinned Perl and NASM, and builds in `artifacts\native-build\win-x64` with the matching CMake generator (`Visual Studio 17 2022` or `Visual Studio 18 2026`):

```bat
native\build-windows.cmd
```

Visual Studio 2026 needs a CMake that offers its generator: the CMake bundled with Visual Studio 2026 does, but a standalone CMake earlier on `PATH` takes precedence and must then be 4.2 or later. To switch an existing build tree to another Visual Studio version, delete `artifacts\native-build\win-x64` first. CI builds the released `win-x64` library with Visual Studio 2022.

**macOS** (`osx-x64` on Intel, `osx-arm64` on Apple Silicon):

```bash
bash native/build-macos.sh osx-arm64
```

CI runs the same three scripts. Each passes extra arguments to the CMake configure step: add `-DSRTNET_TEST_OPENSSL=ON` to also run OpenSSL's test suite, as CI does (it is slow), or `-DSRTNET_BUILD_JOBS=8` to limit parallel native work.

## Testing

Tests use xUnit.net v3 on Microsoft.Testing.Platform, which `global.json` selects for `dotnet test`. They make real native connections, so they need the native library for your runtime, from the [quick start](#quick-start-for-managed-changes) or a native build; without it, building the solution fails early, at the examples' native library check. Windows also runs the tests on .NET Framework 4.8.

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

`verify-package` checks the package layout, that each native library matches its build manifest, the Windows DLL's security settings, the license expression, the notices, and the package readme; with no runtime arguments it requires all seven runtimes. `test-package` installs the package into fresh consumer applications and runs them; `--aot` also publishes and runs a Native AOT consumer for the host runtime (it needs a Microsoft-built SDK and the native toolchain listed under prerequisites). Never distribute a partial package (`RequireAllNativeRuntimes=false`) or the `Srt.NET.PackageWrapper` test fixture. A release pack needs all seven runtimes and adds an SPDX SBOM with `-p:GenerateSBOM=true`. See [tools/README.md](tools/README.md).

## Rules for changes

- **Tests.** Add or update tests for every behavior change. Native-facing changes need tests that make real libsrt calls.
- **Public API.** Public API changes must be recorded in `src/Srt.NET/PublicAPI/<target framework>/PublicAPI.Unshipped.txt` for each target framework (`netstandard2.0` and `net8.0`); the build fails otherwise (RS0016 and related rules). `dotnet format analyzers src/Srt.NET/Srt.NET.csproj --diagnostics RS0016` adds missing entries. Never add optional parameters to a method that has shipped: add an overload instead.
- **CHANGELOG.** A line under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md) is welcome for user-visible changes, in the Keep a Changelog section that fits (Added, Changed, Deprecated, Removed, Fixed, Security); a maintainer adjusts it before release ([changelog entries](MAINTAINING.md#changelog-entries)).
- **Documentation.** Keep `README.md`, the package readme `src/Srt.NET/PACKAGE.md`, `docs/`, and XML documentation comments in step with the code.
- **Documentation-only pull requests.** Pull requests that change only files on the allow-list in the `changes` job of [build.yml](.github/workflows/build.yml) skip the build, test, and package jobs; files packed into the package or read by the release are never on it.
- **Workflows.** Pin every action to a full commit SHA with a `# vX.Y.Z` comment (Dependabot updates both), keep `persist-credentials: false` on checkout, and grant each job only the permissions it needs.
- **Shell scripts.** Windows checkouts do not record the executable bit, so mark a new script in the index before committing: `git update-index --chmod=+x <script>`.
- **Pull requests.** Keep each pull request focused, fill in the template, and make sure the `CI result` check passes.

## Code review

Every change reaches `main` through a pull request. Reviewers check, and authors should check before asking for review:

- **Correctness:** the change does what it says, including error paths, cancellation, disposal and concurrent use; native calls match the bundled libsrt's C API and its threading rules.
- **Tests:** behaviour changes come with tests that make real libsrt calls and would fail without the change; timing-sensitive tests bound their waits and do not depend on scheduling luck.
- **Security:** inputs from the network, libsrt and callers are validated; native memory is accessed only through bounded, pinned buffers and socket leases; secrets are not logged; the [security model](docs/security.md) still holds.
- **Compatibility:** public API changes are recorded in the PublicAPI files, and shipped signatures are not broken; package, platform and dependency changes keep the package tests passing.
- **Documentation:** README, the package readme, `docs/` and XML comments describe the new behaviour, and user-visible changes have a CHANGELOG entry (a maintainer can add it).
- **Supply chain:** new dependencies, actions and images are pinned, licensed compatibly with MPL-2.0, and justified.

Reviewers approve only when every point holds, and leave comments for anything that does not. The [governance](GOVERNANCE.md) page describes who can approve.

## Code conventions

- Use explicit types (no `var`), target-typed `new()`, and collection expressions where the target type is known.
- Give each type its own file and use file-scoped namespaces.
- Put braces on separate lines, separate logical steps with blank lines, and put a blank line before a comment that follows code.
- Keep comments focused on constraints and decisions rather than restating the code.
- Library code uses `ConfigureAwait(false)` and forwards cancellation tokens; tests bound waits with explicit deadlines.
- Keep native handles and mutable native structures out of the public API.

## FAQ

### Why is the strong-name key committed?

`Srt.NET.snk` strong-names the assembly so that strong-named .NET Framework and .NET Standard consumers can reference Srt.NET, following [Microsoft's guidance for open-source libraries](https://learn.microsoft.com/dotnet/standard/library-guidance/strong-naming). It is assembly identity, not security: the key is public on purpose and must never change.
