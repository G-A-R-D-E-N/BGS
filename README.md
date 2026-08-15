# Behaviour Graph Studio

[![Pipeline status](https://git.nomadicinteractive.dev/nomadic-interactive/behaviortoolstandalone/badges/main/pipeline.svg)](https://git.nomadicinteractive.dev/nomadic-interactive/behaviortoolstandalone/-/pipelines)

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

## Help and feedback

- Read the [public guide](https://prisma-user-interface-framework.github.io/Prisma2.0/tools/behaviourgraphstudio/guide/getting-started).
- Report reproducible problems through [GitHub Issues](https://github.com/G-A-R-D-E-N/BGS/issues).
