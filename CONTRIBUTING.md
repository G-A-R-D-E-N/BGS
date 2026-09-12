# Contributing to Behaviour Graph Studio

Behaviour Graph Studio is the .NET 8 application under `BGS/`. Run the commands below from the
`BGS/` directory unless a command says otherwise.

## Development setup

BGS pins the .NET SDK in `global.json`:

- .NET SDK **8.0.424**
- prerelease SDKs disabled
- SDK roll-forward disabled

Check the active SDK before changing code:

```bash
dotnet --version
```

It should print `8.0.424`. Restore the locked dependency graph before the first build:

```bash
dotnet restore src/BehaviourGraph.Core.csproj --locked-mode
dotnet restore app/BehaviourStudio.csproj --locked-mode
dotnet restore tools/symrm/symrm.csproj --locked-mode
dotnet restore tools/tests/BehaviourGraph.Tests.csproj --locked-mode
dotnet restore tools/uismoke/uismoke.csproj --locked-mode
```

Start the desktop app with:

```bash
dotnet run --project app/BehaviourStudio.csproj
```

A normal local build has no CI commit stamp, so **About** / `--version` reports `build local`.
CI-published packages are stamped with the exact workflow commit.

## Test surface

The BGS CI lane has three regression surfaces. Run all three before opening a PR that changes
runtime behavior.

### xUnit suite

`tools/tests` contains the broad unit/regression suite, including native-save fidelity, Havok parsing,
conversion, scan/report, playback, project analysis, and transaction tests.

```bash
dotnet test tools/tests/BehaviourGraph.Tests.csproj \
  --configuration Release \
  --logger "console;verbosity=normal"
```

### `symrm` native regression harness

`symrm test` exercises the lower-level native/Havok regression harness used by CI:

```bash
dotnet run --project tools/symrm/symrm.csproj \
  --configuration Release -- test
```

`symrm` also contains diagnostic commands used during reverse engineering and fixture work. These
commands are developer tools; do not turn an exploratory diagnostic into a save path without adding
normal validation and tests. See the [symrm CLI reference](guide/cli/symrm.md) for the complete
command list; `symrm <command> --help` is the canonical usage/flags/example source.

### Headless UI smoke

`tools/uismoke` boots Avalonia headlessly and drives important UI workflows without a display. It
covers the Bridge, graph/workspace behavior, native authoring, save/discard flows, playback, archive
browsing, and other app-shell regressions.

```bash
dotnet run --project tools/uismoke/uismoke.csproj \
  --configuration Release
```

When adding or changing a user-facing workflow, prefer extending this harness through the production
window path instead of testing a disconnected copy of the UI logic.

## CI-parity check

For a local pass that mirrors the order used by `.github/workflows/bgs-ci.yml`:

```bash
dotnet build app/BehaviourStudio.csproj --configuration Release --no-restore
dotnet run --project tools/symrm/symrm.csproj --configuration Release --no-restore -- test
dotnet test tools/tests/BehaviourGraph.Tests.csproj --configuration Release --no-restore --logger "console;verbosity=normal"
dotnet run --project tools/uismoke/uismoke.csproj --configuration Release --no-restore
```

Use the locked restores from the setup section first. Do not update lock files as a side effect of an
unrelated change.

## Publish reference

BGS ships self-contained single-file packages for `win-x64` and `linux-x64`. There is no installer;
the package root contains `BehaviourGraphStudio.exe` on Windows or `BehaviourGraphStudio` on Linux.

A straightforward local publish is:

```bash
# Windows x64
dotnet publish app/BehaviourStudio.csproj \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  --output out/win-x64

# Linux x64
dotnet publish app/BehaviourStudio.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  --output out/linux-x64
```

Verify the package you built, not the project output:

```bash
./out/linux-x64/BehaviourGraphStudio --version
```

On Windows:

```text
out\win-x64\BehaviourGraphStudio.exe --version
```

The repository CI uses the same self-contained publish path with two release-package size controls:
managed debug symbols are disabled for publish and native `.pdb` files supplied by dependencies are
removed afterward. It also injects `BuildCommit` and verifies that the published executable reports
that commit through `--version`.

CI artifact names are derived from `app/BehaviourStudio.csproj`'s `<Version>` property:

```text
BehaviourGraphStudio-<version>-win-x64
BehaviourGraphStudio-<version>-linux-x64
```

PR artifact upload is deliberately non-gating when GitHub artifact storage is unavailable. Push and
manual `workflow_dispatch` uploads are strict. A green PR therefore proves build/test/package health,
but only an actual stored artifact proves the downloadable-artifact path.

## Changes and pull requests

- Keep changes scoped. Do not mix generated/local output into product commits.
- `bin/`, `obj/`, `out/`, `dist/`, release zips, test results, and local `docs/` planning notes are
  ignored intentionally.
- Preserve existing file formats and unknown data when the reader/writer does not model them.
- Prefer an explicit refusal over guessing at Havok layouts, values, or conversion rules.
- Add regression coverage for a bug before or with the fix when practical.
- For real-format work, use a real fixture or recorded runtime evidence rather than inferring an
  undocumented container/layout.
- Run the relevant test surface before requesting review. For app/runtime changes, run all three.

## Useful entry points

- Desktop app: `app/BehaviourStudio.csproj`
- Core Havok/model code: `src/`
- xUnit tests: `tools/tests/`
- Native/diagnostic CLI: `tools/symrm/`
- CLI command reference: `guide/cli/symrm.md`
- Headless UI regression harness: `tools/uismoke/`
- Active GitHub Actions lane: `../.github/workflows/bgs-ci.yml`
- Headless load-order scan guide: `guide/scan/README.md`
