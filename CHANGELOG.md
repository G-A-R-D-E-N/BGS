# Changelog

This page lists user-visible changes to Behaviour Graph Studio. For instructions, see the public
[guide](https://prisma-user-interface-framework.github.io/Prisma2.0/tools/behaviourgraphstudio/guide/getting-started).

## Current release

### Saving and reliability

- Supported behaviour and animation edits now save through the built-in native pipeline.
- Saves create a `.bak` copy of the original file.
- An edit that cannot be written safely is refused before the source file changes.
- A refused save now names the exact field and why it cannot be written in place, instead of a generic line.
- Growing an empty array keeps the storage-is-not-owned flag, so authoring a `variableBounds` list from nothing cannot corrupt a release.
- Graph validation and supported project checks are available before saving.

### Editing and authoring

- Create, edit, and remove states, transitions, generators, variables, events, and bindings.
- Copy a graph subtree, paste it into another file, and save reusable templates from your own work.
- Edit animation frames, trim clips, and change clip duration.
- Compare two behaviour files to identify added, removed, and changed objects.

### Viewing and playback

- Browse behaviour files in tree and graph views.
- Opening a large behaviour file no longer freezes the window, and graph checks run off the UI thread.
- The Symbols tab shows the real minimum and maximum of each variable bound.
- Files that are not Fallout 4 packfiles — wrong extension or wrong content — are refused up front with a dialog explaining why and where to report it.
- Preview animations on skeletons and supported skinned meshes.
- Inspect project chains, symbol usage, playback paths, and graph validation results.
- Open vanilla files directly from a Bethesda `.ba2` archive for read-only inspection.

## Recent releases

### August 8, 2026

- Added graph simulation for event-driven states, transition timing, and blend weights.
- Added subtree copy and paste, reusable templates, and path highlighting.
- Added animation trimming, retiming, and improved frame editing.
- Improved animation playback, root-motion display, and mesh preview.

### August 7, 2026

- Added native animation saving for supported frame edits.
- Added a frame browser with bone filtering and direct frame navigation.
- Improved animation details, playback controls, and validation feedback.

### August 6, 2026

- Added archive browsing for inspecting vanilla behaviour files without manual extraction.
- Added skeleton, mesh, and root-motion views.
- Improved graph editing, symbol management, field editing, and save safeguards.

### August 4, 2026

- Added graph editing, filtering, selection, validation markers, undo, and compare support.
- Added support for animation controls, variables, events, and behaviour authoring workflows.

## Help and feedback

- Read the [public guide](https://prisma-user-interface-framework.github.io/Prisma2.0/tools/behaviourgraphstudio/guide/getting-started).
- Report reproducible problems through [GitHub Issues](https://github.com/NomadsReach/BehaviorGraphStudio/issues).
