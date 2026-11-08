## Summary

<!-- What does this change do, and why? Link the issue it resolves (for example, "Fixes #123"). -->

## Testing

<!-- How did you verify the change? Name the runtimes (RIDs) and target frameworks you ran. -->

## Checklist

- [ ] Tests are added or updated, and `dotnet test --solution Srt.NET.slnx -c Release` passes locally.
- [ ] `dotnet format Srt.NET.slnx --verify-no-changes` passes.
- [ ] Public API changes are recorded in `src/Srt.NET/PublicAPI/<target framework>/PublicAPI.Unshipped.txt` for every target framework, and no optional parameters were added to shipped methods.
- [ ] `CHANGELOG.md` has an entry under `[Unreleased]` for user-visible changes.
- [ ] Documentation (`README.md`, the package readme `src/Srt.NET/PACKAGE.md`, `docs/`, XML comments) is updated.
- [ ] Native changes only: `native/versions.json`, the dependency notices, and the native version test are updated, and the full CI matrix passed.
- [ ] Workflow changes only: actions are pinned to full commit SHAs with a version comment, and actionlint passes.

<!-- Temporary change to check that documentation-only pull requests skip the build. -->
