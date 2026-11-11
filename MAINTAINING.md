# Maintaining Srt.NET

This page is for maintainers: the settings that live outside git, releases, native dependency updates, recurring duties, who holds which account or key, and what to do when something fails. [CONTRIBUTING.md](CONTRIBUTING.md) covers building, testing and the rules for changes, and [GOVERNANCE.md](GOVERNANCE.md) who decides what.

The repository is [notley-technologies/Srt.NET](https://github.com/notley-technologies/Srt.NET), and the package is published to nuget.org by the [NotleyTechnologies](https://www.nuget.org/profiles/NotleyTechnologies) organization.

## One-time setup

These settings are not stored in git. Each item ends with a read-only command that shows the current setting.

- **Maintainers team.** `.github/CODEOWNERS` names the `notley-technologies/srt-net-maintainers` team. It must be visible (`closed` in the API) and have Write access or higher. Check: `gh api repos/notley-technologies/Srt.NET/teams --jq '.[] | {slug, permission, privacy}'`.
- **Rulesets**, created after the first green CI and dependency review runs so that the checks exist:
    - `main` requires a pull request, with no required approvals, dismisses stale approvals on new commits, requires review threads to be resolved, and allows only squash merges. The `CI result` and `dependency-review` checks must pass on a branch that is up to date with `main`, and CodeQL must report no errors and no security alerts of high severity or above. Commits must be signed and history linear, and force pushes and deletion are blocked. Nobody can bypass it.
    - `tag` blocks creating, updating and deleting `v*` tags for everyone except the `srt-net-maintainers` team, which bypasses it.

    Check: `gh api repos/notley-technologies/Srt.NET/rulesets` lists them, and `gh api repos/notley-technologies/Srt.NET/rulesets/<id>` shows each one's rules.

- **Workflow permissions** (Settings > Actions > General): the default `GITHUB_TOKEN` is read-only, and **Allow GitHub Actions to create and approve pull requests** is on, so that the [native dependency workflow](#native-dependency-updates) can open its pull requests. An organization owner may need to allow it for the organization first. The setting also lets any workflow's `GITHUB_TOKEN` approve pull requests. Check: `gh api repos/notley-technologies/Srt.NET/actions/permissions/workflow` shows `"default_workflow_permissions":"read"` and `"can_approve_pull_request_reviews":true`.
- **Security features:** private vulnerability reporting, which [SECURITY.md](SECURITY.md) relies on, and secret scanning with push protection. Check: `gh api repos/notley-technologies/Srt.NET/private-vulnerability-reporting` and `gh api repos/notley-technologies/Srt.NET --jq .security_and_analysis`.
- **Automatic dependency submission** (Settings > Advanced Security, under Dependency graph): NuGet versions live in `Directory.Packages.props`, which the dependency graph cannot read, so without the submitted snapshot dependency review sees NuGet packages without versions and cannot flag vulnerable ones. GitHub runs it on `main` and restores every project file without properties, so each one must restore that way, and a failed run notifies no one. Check: `gh api repos/notley-technologies/Srt.NET/dependency-graph/sbom --jq '[.sbom.packages[] | select(.versionInfo == ">= 0")] | length'` prints `0`, and `gh run list --repo notley-technologies/Srt.NET --workflow "Automatic Dependency Submission" --limit 1` shows the latest run succeeded.
- **`nuget` environment** (Settings > Environments): required reviewers from the maintainers team, deployments only from tags matching `v*`, and an environment variable `NUGET_USER` set to the nuget.org username (not the email address) of the account that created the trusted publishing policy. Check: `gh api repos/notley-technologies/Srt.NET/environments/nuget --jq '{protection_rules, deployment_branch_policy}'`, `gh api repos/notley-technologies/Srt.NET/environments/nuget/deployment-branch-policies --jq '.branch_policies[] | {name, type}'` and `gh variable list --env nuget`.
- **Trusted publishing** on nuget.org, under the NotleyTechnologies organization: repository owner `notley-technologies`, repository `Srt.NET`, workflow file `build.yml`, environment `nuget`. Check it on nuget.org (Trusted Publishing in the account menu); GitHub cannot show it.
- **Signing keys.** Maintainers sign release tags (`git tag -s`), so each needs a GPG or SSH signing key configured in git (`user.signingkey`, and `gpg.format ssh` for SSH keys) and added to their GitHub account. Check: `gh api users/<login>/gpg_keys` or `gh api users/<login>/ssh_signing_keys`.

## Releases

### Changelog entries

`CHANGELOG.md` follows Keep a Changelog. Contributors may add lines under `## [Unreleased]`; a maintainer writes or adjusts each entry before the release:

- Describe changes relative to the last published release. Until the first release, describe current behavior under Added and Security: a fix to something not yet released edits its Added bullet instead of adding a Fixed one.
- Record bundled native updates under Security when they fix vulnerabilities and under Changed otherwise. The update scripts do not edit `CHANGELOG.md`; until the first release, keep the "Bundles libsrt ... with OpenSSL ... linked statically" bullet under Security in step with `native/versions.json` instead.
- The release workflow publishes the section as the GitHub release notes, so link to repository files with absolute `https://github.com/notley-technologies/Srt.NET/blob/main/...` URLs.

### Each release

1. Check the [one-time setup](#one-time-setup) with its commands. The `nuget` environment and the trusted publishing policy must exist before the first tag: when a workflow references an environment that does not exist, GitHub creates it without protection rules.
2. Check that the scheduled workflows are still enabled: `gh workflow list --all` shows each workflow's state, and `gh workflow enable <workflow>` turns one back on. GitHub disables scheduled workflows in a public repository after 60 days without activity.
3. Review the native dependency updates merged since the last release (pull requests labelled `native-dependencies`): read the upstream release notes and changes, write or adjust their [CHANGELOG entries](#changelog-entries), and check `srt.h` for new functions and `SRTO_*` options to add to [the API coverage audit](docs/api-coverage.md).
4. In `CHANGELOG.md`, rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` and add a new empty `## [Unreleased]` above it. At the end of the file, add `[X.Y.Z]: https://github.com/notley-technologies/Srt.NET/releases/tag/vX.Y.Z` and change the `[Unreleased]` link to `https://github.com/notley-technologies/Srt.NET/compare/vX.Y.Z...HEAD`. The release workflow publishes that section as the release notes and fails without it.
5. Move every entry from each target framework's `PublicAPI.Unshipped.txt` into its `PublicAPI.Shipped.txt`, leaving only `#nullable enable` in the Unshipped files, and build to confirm there are no RS0016, RS0017, or RS0025 errors. Tag builds fail while an Unshipped file has entries.
6. Review `src/Srt.NET/PACKAGE.md`, the package readme that nuget.org shows. It ships inside the package and cannot be changed after publishing, so it must describe this release, use absolute `https://` links, and use only images from domains that nuget.org allows. Tag builds pin its `blob/main` links to the release tag.
7. Merge to `main` and wait for a green `CI result` run on that commit. Then create and push the signed tag `vX.Y.Z` on it (`git tag -s vX.Y.Z`, `git push origin vX.Y.Z`).
8. The CI workflow rebuilds every native library from source, tests, packs with an SBOM, verifies the package (including its `MPL-2.0` license expression), attests the outputs, and creates a draft GitHub release with the CHANGELOG section as its notes.
9. Approve the `nuget` environment deployment. The workflow publishes to nuget.org with trusted publishing and then publishes the GitHub release.
10. Once nuget.org lists the new version, set `<PackageValidationBaselineVersion>` in `src/Srt.NET/Srt.NET.csproj` to it in a follow-up commit, so package validation compares later builds with the published API. Never set a version that is not on nuget.org yet: restore downloads the baseline package and fails. Record an intentional breaking change in a `CompatibilitySuppressions.xml` file (`dotnet pack -p:ApiCompatGenerateSuppressionFile=true` writes it) instead of removing the baseline.

## Native dependency updates

libsrt and OpenSSL are pinned in `native/versions.json`, which Dependabot cannot update. Every day at 05:41 UTC the [Native dependency updates](.github/workflows/native-updates.yml) workflow compares the pins with upstream releases: OpenSSL releases on the pinned 3.5 LTS line, and libsrt releases tagged `vX.Y.Z` (release candidates are ignored), preferring the newest patch on the pinned minor line. For each component with a newer release, it runs the update script below and opens a pull request, labelled `native-dependencies`, with the workflow's `GITHUB_TOKEN`. Each component has its own branch, `native-updates/openssl` or `native-updates/libsrt`, and a new libsrt minor or major release gets `native-updates/libsrt-<major.minor>`, so a patch release never takes over a pull request under review. Nothing merges automatically.

- **Starting CI.** GitHub holds the CI runs of a pull request that a `GITHUB_TOKEN` opens or updates until someone with write access selects **Approve workflows to run** on it, so approve again each time the workflow updates the pull request. CI then builds the new sources on every runtime and runs the native ABI check (`native/abi/managed_abi.c`), the full test suite, the RHEL 8 and Alpine compatibility tests and the package tests. It does not test interoperability with peers that run other libsrt versions.
- **Review and merge.** Read the upstream release notes and, for libsrt, check `srt.h` for new functions and `SRTO_*` options. A new minor or major release needs a closer look, because the API, the ABI or behavior may change. The workflow does not refresh an open pull request, so update its branch if `main` has moved, then merge it. If it conflicts with `main`, for example after the other component's update merged, the next run regenerates it from `main`.
- **Declining and replacing.** Closing a pull request without merging declines that release: the workflow does not propose it again. Declining the newest libsrt patch release also holds back proposals of a newer libsrt minor or major release until the next patch release, because the workflow proposes patch releases first; update by hand to take the newer release. A newer release replaces the branch, title and description of an older open pull request, discarding any commits pushed to that branch, and so does regenerating a conflicting one. Only pull requests from this repository's own branches count; a fork's pull request with the same title does not.
- **Repository setting.** Opening the pull requests needs **Allow GitHub Actions to create and approve pull requests** (see [workflow permissions](#one-time-setup)).
- **Failures.** If the check or an update fails on `main`, the workflow opens an issue titled "Native dependency update failed", or comments on it while it is open. The issue does not close itself; close it once an update works again. Runs on other branches are dry runs that only log the comparison, and `gh workflow run native-updates.yml` runs the workflow on `main` on demand.
- **CHANGELOG.** The update scripts do not edit `CHANGELOG.md` or `SECURITY.md`. A maintainer writes the CHANGELOG line before the release, under Security when the update fixes vulnerabilities ([changelog entries](#changelog-entries)). OpenSSL security fixes require a new Srt.NET release even when the managed code is unchanged; [SECURITY.md](SECURITY.md#bundled-native-code) gives the target time.

### Updating by hand

Change the pins only with the update scripts, which the workflow also runs:

- **OpenSSL** (needs GnuPG): `cmake -DOPENSSL_VERSION=3.5.<n> -P native/cmake/UpdateOpenSSL.cmake`. It downloads the official archive, verifies its signature against `native/openssl-release-key.asc` and its version, then records the version and SHA-256 in `native/versions.json` and updates `THIRD_PARTY_NOTICES.md`. Signing-key changes need separate review against the [OpenSSL release-signing fingerprints](https://openssl-library.org/source/).
- **libsrt**: `cmake -DSRT_VERSION=<x.y.z> -P native/cmake/UpdateLibsrt.cmake`. It resolves the release tag to its commit, refuses a release tag that moved away from the pinned commit, then records the version and commit in `native/versions.json`, raises the statistics version check (`NativeAbi.VerifiedVersion`), and updates `THIRD_PARTY_NOTICES.md` and the libsrt links in `README.md` and `docs/statistics.md`. The native build compiles `native/abi/managed_abi.c` against the new headers, so a release that changes a checked structure layout, field type or constant fails CI instead of being bundled.

The full CI matrix must pass. Changes under `native/` invalidate the CI native cache automatically; the Windows and macOS keys also include the runner image version.

## Recurring duties

- Review and merge the native dependency pull requests and the Dependabot pull requests, which `.github/dependabot.yml` groups. Some moves are excluded there and done by hand: a new .NET major or minor version or a new Alpine branch, as [updating the build images](native/README.md#updating-the-build-images) describes, and minor or major updates of Microsoft.CodeAnalysis.PublicApiAnalyzers, which move together with the SDK feature band.
- The netstandard2.0 dependency minimums (Microsoft.Bcl.AsyncInterfaces 8.0.0 and System.Memory 4.5.5) are set with `VersionOverride` in `src/Srt.NET/Srt.NET.csproj`, and Dependabot ignores both packages. Raise them only on purpose, with a CHANGELOG entry. Their central versions in `Directory.Packages.props` only pin what tests and tools resolve, and are updated by hand.
- [ROADMAP.md](ROADMAP.md) lists the dated items, such as replacing the `macos-15-intel` CI runner.
- The [Scorecard viewer](https://scorecard.dev/viewer/?uri=github.com/notley-technologies/Srt.NET) shows the OpenSSF Scorecard results (run `gh workflow run scorecard.yml` after changing settings). Update the project's answers at [bestpractices.dev](https://www.bestpractices.dev/projects/16461) when the release, security or testing process changes.

## Accounts, keys and secrets

| What | Held by |
| --- | --- |
| The `notley-technologies` GitHub organization: repository settings, rulesets and teams | Its owners (see [GOVERNANCE.md](GOVERNANCE.md)) |
| The `srt-net-maintainers` team: the code owners, and the only accounts that can create release tags | The maintainers |
| The NotleyTechnologies nuget.org organization, which owns the `Srt.NET` package ID | Its members |
| The trusted publishing policy, and `NUGET_USER`, which names its account | The one nuget.org account that created the policy. NuGet deactivates the policy if that account leaves the organization; another member then creates a new policy and updates `NUGET_USER`. |
| Release tag signing keys | Each maintainer, on their own GitHub account |
| Repository secrets | None: publishing uses short-lived OIDC credentials, and every workflow uses its own `GITHUB_TOKEN` |
| `Srt.NET.snk` and `native/openssl-release-key.asc` | Committed and public on purpose. Never change the strong-name key ([why](CONTRIBUTING.md#why-is-the-strong-name-key-committed)). |

## Recovery

- **A release run fails.** Fix the cause and re-run the failed jobs. The release job replaces a draft release left by an earlier attempt and refuses to touch a published one, and the publish job pushes with `--skip-duplicate`, so re-running it after nuget.org accepted the package only publishes the GitHub release. Never delete or move a pushed tag: if the tagged commit itself is wrong, release a new patch version instead.
- **A native dependency update fails.** Open the run linked from the "Native dependency update failed" issue. If creating the pull request failed with "GitHub Actions is not permitted to create or approve pull requests", turn on the [workflow permissions](#one-time-setup) setting. Otherwise fix the cause, or [update by hand](#updating-by-hand) on a branch and open the pull request yourself, then close the issue.
- **A native build fails in CI.** Run the same build script locally (see [building native libraries](CONTRIBUTING.md#building-native-libraries)); CI adds `-DSRTNET_TEST_OPENSSL=ON`. CI restores native builds from a cache whose key covers `native/` (except Markdown), the extra CMake arguments and, on Windows and macOS, the runner image; tag builds never use the cache.
