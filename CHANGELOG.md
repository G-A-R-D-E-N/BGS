# Changelog

This page records changes that affect people using Behaviour Graph Studio. For instructions, read
the public [getting started guide](https://prisma-user-interface-framework.github.io/Prisma2.0/tools/behaviourgraphstudio/guide/getting-started).

## 1.0.5, August 15, 2026

### The Bridge home

- The Bridge tab gathers every part of the studio on one screen: a current-file card, task-grouped station cards that jump straight to each tool, and a where-everything-is reference table.
- A first-run spotlight tour walks through the stations; replay it any time from the Bridge header, or press Escape to skip.
- The app reopens on the tab you were using last time.
- Type in the Bridge search box to filter stations and the reference table as you type.
- Drag a `.hkx` file anywhere onto the Bridge to open it, with a drop hint on the current-file card.
- Jump back in to your recent files with one click.

### Saving and reliability

- A save no longer replaces the file in two steps, so the file on disk is never briefly missing and a crash mid-save cannot leave it gone.
- If another program changes the file while a save is being written, the save is refused and that program's version is left in place rather than overwritten.
- A save interrupted by a crash is recovered the next time you save the same file, and the version it displaced is kept as the backup.
- Recovery only ever touches the files a save itself created, so a file of your own that happens to sit beside the source is left alone.
- A backup that cannot be rotated no longer reports a save as failed when the file was written correctly.
- Large but valid arrays load again: a file is no longer refused for declaring more elements than an arbitrary limit allowed.
- A malformed array is refused before any memory is reserved for it, and reports the problem instead of failing later.
- Extracting from a `.ba2` archive refuses entry names that point outside the chosen folder, including drive-qualified and rooted names.

## 1.0.4

### Saving and reliability

- Supported behaviour and animation edits save through the built-in native pipeline.
- Saves create a `.bak` copy of the original file.
- An edit that cannot be written safely is refused before the source file changes.
- A refused save names the exact field and why it cannot be written in place.
- Growing an empty array keeps the storage-is-not-owned flag, so authoring a `variableBounds` list from nothing does not corrupt the file.
- Graph validation and supported project checks are available before saving.
- Closing a changed file gives you a clear save, discard, or cancel choice. A refused save keeps the file open.

### Editing and playback

- Create, edit, and remove states, transitions, generators, variables, events, and bindings.
- Copy a graph subtree, paste it into another file, and save reusable templates from your own work.
- Edit animation frames, trim clips, and change clip duration.
- Compare two behaviour files to identify added, removed, and changed objects.
- Browse behaviour files in tree and graph views.
- Opening a large behaviour file no longer freezes the window, and graph checks run off the UI thread.
- The Symbols tab shows the real minimum and maximum of each variable bound.
- Files that are not Fallout 4 packfiles are refused before opening, with an explanation of why.
- Preview animations on skeletons and supported skinned meshes.
- Playback can use the selected animation's sibling `CharacterAssets` folder when the behaviour file does not provide a project rig.
- Inspect project chains, symbol usage, playback paths, and graph validation results.
- Open vanilla files directly from a Bethesda `.ba2` archive for read-only inspection.

## 1.0.3, August 8, 2026

- Added graph simulation for event-driven states, transition timing, and blend weights.
- Added subtree copy and paste, reusable templates, and path highlighting.
- Added animation trimming, retiming, and improved frame editing.
- Improved animation playback, root-motion display, and mesh preview.

## 1.0.2, August 7, 2026

- Added native animation saving for supported frame edits.
- Added a frame browser with bone filtering and direct frame navigation.
- Improved animation details, playback controls, and validation feedback.

## 1.0.1, August 6, 2026

- Added archive browsing for inspecting vanilla behaviour files without manual extraction.
- Added skeleton, mesh, and root-motion views.
- Improved graph editing, symbol management, field editing, and save safeguards.

## 1.0.0, August 4, 2026

- Added graph editing, filtering, selection, validation markers, undo, and compare support.
- Added support for animation controls, variables, events, and behaviour authoring workflows.

## Help and feedback

- Read the [getting started guide](https://prisma-user-interface-framework.github.io/Prisma2.0/tools/behaviourgraphstudio/guide/getting-started).
- Report a reproducible problem through [GitHub Issues](https://github.com/G-A-R-D-E-N/BGS/issues).
