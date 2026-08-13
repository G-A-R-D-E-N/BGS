# Releasing Behaviour Graph Studio

Releases are built by GitHub Actions from an immutable version tag. Do not upload locally built binaries and do not move a published tag.

## Release order

1. Merge the release changes to `main` only after CI and CodeQL are green.
2. Update `app/BehaviourStudio.csproj` so `<Version>` is the version being released.
3. Update `CHANGELOG.md` for that version.
4. Create and push a new `vX.Y.Z` tag on the exact `main` commit that passed CI.
5. The `Release` workflow verifies that the tag and application version match, restores the locked dependency graph, builds Windows and Linux self-contained packages, runs each packaged executable with `--version`, creates SHA-256 checksums and build-provenance attestations, then publishes the GitHub release.
6. If anything is wrong after publication, fix it on `main` and release a new version. Never retarget or replace a published tag or release.

## Before tagging

The following must be green on the release commit:

- CI on Ubuntu and Windows.
- Native regression harness.
- xUnit suite.
- UI smoke harness.
- Self-contained publish check for `linux-x64` and `win-x64`.
- CodeQL.

The public test suite intentionally does not contain Bethesda assets. Run the private/local Fallout 4 corpus pass before tagging when a change touches HKX, NIF, BA2, animation decoding, mesh discovery, or save behavior.
