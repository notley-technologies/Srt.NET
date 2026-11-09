# Governance

Srt.NET is an open-source project owned by Notley Technologies and maintained in the open at [notley-technologies/Srt.NET](https://github.com/notley-technologies/Srt.NET). This page describes who makes decisions and how.

## Roles

| Role | Who | Responsibilities |
| --- | --- | --- |
| **Owner** | Notley Technologies | Owns the GitHub organization, the repository and the [NotleyTechnologies](https://www.nuget.org/profiles/NotleyTechnologies) nuget.org organization; appoints maintainers. |
| **Maintainers** | Members of the [`srt-net-maintainers`](https://github.com/orgs/notley-technologies/teams/srt-net-maintainers) team: Noah Davis ([@noahdavis319](https://github.com/noahdavis319)) | Review and merge pull requests, triage issues, handle vulnerability reports, cut releases, keep dependencies and CI current, and enforce the contribution rules. Maintainers are the code owners in [`.github/CODEOWNERS`](.github/CODEOWNERS). |
| **Contributors** | Anyone | Report issues, propose changes through pull requests under the [contribution terms](CONTRIBUTING.md#license-and-contribution-terms), and review others' pull requests. |

## Decisions

- Day-to-day changes are decided in pull requests, reviewed against the [code review standards](CONTRIBUTING.md#code-review). A change merges when it passes the required checks and a maintainer approves it. While there is one maintainer, that maintainer reviews and merges their own changes; once there are two, every change needs the approval of a maintainer other than its author. Maintainers aim for consensus with the contributors involved.
- Larger changes (public API design, supported platforms, bundled dependencies, licensing, release policy) start as an issue so that users can comment before work begins.
- If contributors and maintainers disagree, the maintainers decide, and record the reasoning in the issue or pull request. The owner settles disagreements among maintainers.
- Security decisions follow [SECURITY.md](SECURITY.md); vulnerability details stay private until a fix is released.

## Becoming a maintainer

Contributors who have made sustained, high-quality contributions and reviews can be invited by the existing maintainers with the owner's agreement. New maintainers join the `srt-net-maintainers` team, receive the same repository and release permissions, and must use two-factor authentication and sign their release tags.

## Continuity

Everything needed to continue the project is held by the organization, not by individuals: the repository, the release workflow, the trusted-publishing policy on nuget.org (no long-lived API keys), and the `nuget` deployment environment. Release tags are signed with each maintainer's own key, so a new maintainer signs with theirs. The project currently has one maintainer; adding a second maintainer, and a second owner of the GitHub and nuget.org organizations, is on the [roadmap](ROADMAP.md) so that the project can continue if any one person becomes unavailable.

## Changes to this document

Changes to governance are made by pull request and require the owner's approval.
