# Behaviour Graph Studio

Behaviour Graph Studio is a standalone desktop editor for Fallout 4 Havok behaviour files (`.hkx`).
It runs outside the game and lets you inspect, edit, validate, compare, and preview behaviour graphs
and animations.

## Get started

Download a release from [Nexus Mods](https://www.nexusmods.com/fallout4/mods/107691), extract it
anywhere on your computer, and run `BehaviourGraphStudio` or `BehaviourGraphStudio.exe`.

There is no installer and nothing needs to be added to your Fallout 4 folder.

For step-by-step help, see the public
[Behaviour Graph Studio guide](https://prisma-user-interface-framework.github.io/Prisma2.0/tools/behaviourgraphstudio/guide/getting-started).

## What you can do

- Open Fallout 4 behaviour, character, project, skeleton, and animation `.hkx` files.
- Browse a file in a tree or graph view and inspect each object's editable fields.
- Open vanilla files directly from a Bethesda `.ba2` archive without extracting the archive first.
- Edit supported values, names, symbols, bindings, states, transitions, generators, and arrays.
- Preview animation playback on a skeleton or skinned mesh.
- Check a graph or an entire project for broken references and missing animation files.
- Compare two behaviour files to find added, removed, and changed objects.
- Undo and redo changes before saving.

Supported edits are written through the native save pipeline. The original file is backed up as
`.bak`; if an edit cannot be written safely, the application refuses to save and leaves the source
file unchanged.

## Quick start

1. Download and extract a release.
2. Start Behaviour Graph Studio.
3. Use **Open** to select an extracted `.hkx` file, or **From archive...** to browse a `.ba2`.
4. Select an object in the tree or graph to inspect it.
5. Make an edit, run **Check graph**, then save when the result is valid.

Files opened from a `.ba2` are read-only copies. Copy one to a folder you control before editing it.

## Known limitations and experimental features

BGS is conservative about unsupported formats and edits. Areas that are not fully reduced or
validated are read-only, refused, or clearly marked in the application:

- Skeleton-mapper serialization/write-back and chain-mapper authoring are not complete. Shipped
  Fallout 4 files do contain chain and unmapped-bone data; automatic bone pairing is not proven.
- Reciprocal mapper generation is a construction tool based on the measured human fixture. It does
  not claim that every shipped mapper pair is symmetrical.
- Ragdoll and physics tools are primarily inspection and engineering previews. Drop/Recover is a
  constrained preview, not a full Havok solver; body-to-body collision simulation and full ragdoll
  authoring are not implemented.
- Some physics and cloth systems remain inspection-only. Havok 2018 support is incomplete; current
  Fallout 76 evidence covers Havok 2015.1.0, not a completed Havok 2018 implementation.
- Unsupported Havok layouts, classes, and conversion paths fail closed. BGS does not write guessed
  data; an unsupported operation leaves the source unchanged and reports why.

These boundaries are intentional for 1.1.0. See [RELEASE-NOTES.md](RELEASE-NOTES.md) and the
specialist notes under `docs/` for the measured scope of the experimental tools.

## Scan a load order from the command line

Behaviour Graph Studio can also scan a whole load order without opening the window — to vet a mod or
gate a build in CI:

```bash
BehaviourGraphStudio.exe --scan-archives "D:\Mods\MyAnimationMod"
BehaviourGraphStudio.exe --scan-clips  "C:\MO2\Instances\Fallout 4"
```

`--scan-archives` finds corrupt behaviour files, `--scan-modlist` finds missing declared
animations, and `--scan-clips` finds clips whose animation is absent from the merged load order.
These are static findings rather than a reproduction of the game's runtime load path. Each mode
returns an exit code (`0`/`1`/`2`) and takes `--json` for a
machine-readable report. See the [Headless scanning guide](guide/scan/README.md).

## Requirements

- Windows 64-bit or Linux x64.
- A Fallout 4 installation is useful for opening game files, but it is not required to run the tool.
- No separate Havok tools, Java runtime, game SDK, or installation into the game directory is needed.

## Build from source

Building requires the .NET 8 SDK.

```bash
dotnet run --project app/BehaviourStudio.csproj
dotnet test tools/tests/BehaviourGraph.Tests.csproj
dotnet publish app/BehaviourStudio.csproj -c Release -r linux-x64 -o out
```

For the pinned SDK, locked restores, the full three-part test surface, CI-parity commands, and both
Windows/Linux publish flows, see [CONTRIBUTING.md](CONTRIBUTING.md).

## Help and feedback

- Read the [public guide](https://prisma-user-interface-framework.github.io/Prisma2.0/tools/behaviourgraphstudio/guide/getting-started).
- Read [CONTRIBUTING.md](CONTRIBUTING.md) before changing or packaging BGS.
- Report reproducible problems through [GitHub Issues](https://github.com/G-A-R-D-E-N/BGS/issues).
