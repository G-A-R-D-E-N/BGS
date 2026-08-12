# Behaviour Graph Studio 1.0.4

Behaviour Graph Studio is a standalone editor for Fallout 4 behaviour and animation HKX files.
Open an extracted file, make a supported change, check it, and save it without installing Havok
tools or putting anything in the game folder.

## What this release does

### Edit and save safely

- Edit states, transitions, generators, variables, events, bindings, and supported animation data.
- Save through the native writer. A successful save creates a `.bak` beside the original file.
- If a change cannot be written safely, the tool refuses it before it touches the source file and
  tells you what blocked the save.
- Run graph and project checks before committing a change.

### Inspect real behaviour projects

- Browse behaviour files in tree and graph views, inspect symbols, follow project chains, and
  compare two files.
- Open files from disk or inspect vanilla files directly from a `.ba2` archive. Archive files are
  opened read-only.
- Preview supported animations on a skeleton or supported skinned mesh.
- Use the playback, animation, and frame tools to inspect timing, root motion, and transforms.

### Build and reuse graph work

- Copy a graph subtree into another compatible file.
- Save reusable templates from your own graph work.
- Add, edit, and remove supported graph objects, then undo or redo before saving.
- Trim clips, retime animations, and edit supported animation frames.

## Limits to know about

This is a Fallout 4 HKX tool, not a general Havok editor. It supports the data the editor can read,
validate, and write safely. If a file, field, or edit is outside that boundary, the tool leaves the
source alone and explains why.

## Help and feedback

- Read the [getting started guide](https://prisma-user-interface-framework.github.io/Prisma2.0/tools/behaviourgraphstudio/guide/getting-started).
- Report a reproducible problem through [GitHub Issues](https://github.com/NomadsReach/BehaviorGraphStudio/issues).
