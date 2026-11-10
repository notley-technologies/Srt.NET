## Summary

<!-- What does this change do, and why? Link the issue it resolves (for example, "Fixes #123"). -->

## Testing

<!-- How did you verify the change? Name the runtimes (RIDs) and target frameworks you ran. -->

## Checklist

- [ ] Tests are added or updated. Run them locally if you have the native library; CI runs every runtime.
- [ ] `dotnet format Srt.NET.slnx --verify-no-changes` passes.
- [ ] Public API changes, if any, are recorded in `src/Srt.NET/PublicAPI/<target framework>/PublicAPI.Unshipped.txt` for every target framework.
- [ ] Optional: a line under `[Unreleased]` in `CHANGELOG.md` for user-visible changes. A maintainer adjusts it before release.
