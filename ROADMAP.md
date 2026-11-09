# Roadmap

This is the maintainers' plan for roughly the next year (through late 2027). It is a statement of intent, not a commitment; priorities can change with user feedback. Discuss items in [issues](https://github.com/notley-technologies/Srt.NET/issues).

## Releases

- **0.1.0:** the first public release on nuget.org, with the API, platforms and packaging described in [CHANGELOG.md](CHANGELOG.md).
- **0.x releases:** fixes and additive features driven by early users. Breaking changes stay possible before 1.0 and are listed in the CHANGELOG.
- **1.0:** once the API has had real-world use, commit to Semantic Versioning compatibility, with package validation against the released API.

## libsrt and security

- Track libsrt 1.5.x releases and OpenSSL 3.5 updates; rebuild promptly for security fixes, as [SECURITY.md](SECURITY.md) describes.
- Offer SRT's authenticated encryption (AES-GCM, `SRTO_CRYPTOMODE`) once libsrt supports it outside its preview build option, so that encrypted payloads are integrity-protected.
- Add a CI job that runs the test suite against a debug build of libsrt with its internal assertions enabled.

## Features

- Design an API for SRT socket groups (connection bonding), which [api-coverage.md](docs/api-coverage.md) lists as deferred, if users need it.
- Evaluate a native `win-arm64` runtime.
- Complete `<param>`, `<returns>` and `<exception>` XML documentation for every public member.

## Project health

- Add a second maintainer and a second owner of the GitHub and nuget.org organizations, so that every change gets a second reviewer and the project does not depend on one person.
- Raise test coverage to at least 90% of statements and 80% of branches, and keep it there.
- Replace the `macos-15-intel` CI runner before GitHub retires it.
