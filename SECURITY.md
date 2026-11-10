# Security policy

## Supported versions

Only the latest minor release line receives security fixes; upgrade to its newest patch release. Before 1.0, fixes are not backported to older lines.

## Security model

[docs/security.md](docs/security.md) describes what Srt.NET's security does and does not provide (including the limits of SRT encryption: payloads are confidential but not integrity-protected), its threat model and trust boundaries, and the assurance case for its security requirements.

## Reporting a vulnerability

Report vulnerabilities privately through GitHub: <https://github.com/notley-technologies/Srt.NET/security/advisories/new> (the repository's **Security** tab, then **Report a vulnerability**).

Do not open a public issue, discussion, or pull request for a suspected vulnerability. Please include:

- the affected Srt.NET version and runtime identifier (RID);
- the impact and the conditions needed to trigger it (for example, an unauthenticated peer or a specific option);
- a minimal reproduction or proof of concept, if you have one.

The maintainer aims to acknowledge reports within 5 business days, agree on a fix and disclosure timeline with the reporter, and publish a GitHub security advisory (with a CVE when appropriate) together with the fixed release. Reporters are credited unless they prefer otherwise.

## Bundled native code

Every Srt.NET package ships native code that Srt.NET does not develop:

| Component | How it is shipped |
| --- | --- |
| Haivision libsrt | Built from an immutable upstream commit, without patches |
| OpenSSL (long-term support line) | Built from the signature-verified release archive and linked statically into libsrt |

The bundled versions, source revisions, archive checksums, and binary digests are recorded in [native/versions.json](native/versions.json), in each runtime's `runtimes/<rid>/native/srt.build-info.json`, and in the package's SBOM documents. Licenses and notices are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

- **Where to report.** Report vulnerabilities in libsrt or OpenSSL to those projects: libsrt through [Haivision/srt](https://github.com/Haivision/srt/security), OpenSSL as described in its [security policy](https://openssl-library.org/policies/general/security-policy/). If a vulnerability depends on how Srt.NET builds, configures, or calls them, report it here as well.
- **Monitoring.** A scheduled workflow (`.github/workflows/native-updates.yml`) compares the pinned versions with the latest OpenSSL and libsrt releases every day and opens a pull request for each available update. CI rebuilds and tests every runtime on it, including a build-time check that the new libsrt headers keep the structure layouts and constant values Srt.NET relies on, and a maintainer reviews and merges it ([native dependency updates](MAINTAINING.md#native-dependency-updates)). A fix still reaches users only in a new Srt.NET release.
- **Fixes.** Because OpenSSL is linked statically, applications cannot patch it independently: an OpenSSL or libsrt fix that affects the shipped configuration requires a rebuilt Srt.NET release, even when the managed code is unchanged. Srt.NET aims to publish such a patch release within 7 days of an upstream advisory rated High or Critical that applies to the shipped build, and records it under **Security** in [CHANGELOG.md](CHANGELOG.md).
- **Reduced exposure.** libsrt uses OpenSSL's crypto library only for SRT encryption (AES, key wrapping, PBKDF2 key derivation, and random numbers); no TLS is involved. OpenSSL is built without DSO, engine, or provider-module loading and does not load `openssl.cnf` automatically. Linux libraries export only the `srt_*` API.

## Verifying releases

Release builds compile libsrt and OpenSSL from source in the tagged workflow run (never from a cache) and create [GitHub artifact attestations](https://docs.github.com/actions/security-for-github-actions/using-artifact-attestations) signed through Sigstore. Attested files are the `.nupkg` and `.snupkg` release assets and, from inside the package, `Srt.NET.nuspec`, `buildTransitive/Srt.NET.targets`, every `lib/*/Srt.NET.dll`, and every native library (`srt.dll`, `libsrt.so`, `libsrt.dylib`). Verify a file with the [GitHub CLI](https://cli.github.com/):

```text
gh attestation verify Srt.NET.1.2.3.nupkg --repo notley-technologies/Srt.NET \
  --signer-workflow notley-technologies/Srt.NET/.github/workflows/build.yml --source-ref refs/tags/v1.2.3
```

The `.nupkg` release asset also has an SBOM attestation that carries the package's SPDX document; add `--predicate-type https://spdx.dev/Document/v2.2` to the command above to verify it.

nuget.org adds its repository signature to every package as a `.signature.p7s` entry, which changes the `.nupkg` digest but no other entry. To check a whole package downloaded from nuget.org, verify the matching GitHub release asset as shown above, then confirm that both packages contain the same files. Keep the two downloads in separate folders, here `release/` for the GitHub asset and `nuget/` for the nuget.org package: their file names differ only in letter case, so on Windows and macOS one would overwrite the other in a single folder.

```text
unzip -q release/Srt.NET.1.2.3.nupkg -d release-contents
unzip -q nuget/srt.net.1.2.3.nupkg -x .signature.p7s -d nuget-contents
diff -r release-contents nuget-contents && echo "Same contents as the attested release package"
```

This covers every entry, including the nuspec and its dependencies, the MSBuild targets that run in consumers' builds, the native libraries, and the notices. You can also verify the attested files inside an extracted package one by one, for example `gh attestation verify buildTransitive/Srt.NET.targets ...`, but that does not show that the package contains nothing else, so prefer the whole-package comparison. `dotnet nuget verify --all srt.net.1.2.3.nupkg` checks nuget.org's repository signature, which shows that the package has not changed since nuget.org signed it.

Each GitHub release also attaches the package SBOM (`Srt.NET.spdx.json`), one SPDX document per native runtime, and the Sigstore bundles (`*.sigstore.json`) for offline verification with `gh attestation verify --bundle`. `Srt.NET.provenance.intoto.jsonl` holds the same provenance statement as a bare in-toto DSSE envelope, for tools that read that format; verify the Sigstore bundle, which also carries the signing certificate.

The assembly's strong name identifies it; it is not a security boundary or tamper protection. Use the attestations.

## Security-related configuration for users

- Supply SRT passphrases (`SrtOptions.Passphrase`) from your application's secret store, never from source code.
- Native logs at Debug level can include addresses and stream IDs; review what your `SrtLogging` handler stores.
- Use connection admission (`SrtSocket.SetConnectionHandler`) to reject unknown stream IDs before accepting a connection.
- Keep Srt.NET current: native security fixes reach applications only through new Srt.NET releases.
