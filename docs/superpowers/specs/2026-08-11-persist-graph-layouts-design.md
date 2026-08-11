# Persist graph layouts per file

## Goal

Keep user-dragged Freeform graph node positions when the same HKX file is reloaded or the application restarts. Positions remain editor-only state and never alter the HKX file.

## Scope

- Store positions locally in the existing Behaviour Graph Studio settings file.
- Key each layout by the normalized full HKX path.
- Restore positions after the graph for that file has been built.
- Save positions after a user drag moves a node or its owned subtree.
- Ignore positions for node IDs absent from the current file.
- Treat malformed or unavailable settings as non-fatal: the graph opens with automatic layout and reports a concise preference-write warning where appropriate.

## Out of scope

- Sidecar layout files.
- Sharing layouts between users or machines.
- Writing UI state into HKX files.
- Persisting Structured Flow positions, because that mode is derived from graph structure rather than manual placement.

## Design

`Settings` gains a focused layout API that reads and writes a path-keyed collection of node ID and coordinate pairs. It uses the existing atomic settings write path. The key is derived from `Path.GetFullPath`, with platform-appropriate path comparison, so relative paths and equivalent absolute paths use one layout.

`GraphView` exposes a layout-changed callback and accepts a restored position map. Freeform placement continues to use `_placed`; the restored map is loaded before `Show` computes positions. A drag updates `_placed` for every node moved by graph ownership rules, then invokes the callback once for the completed move.

`MainWindow` owns the file path to layout mapping. After a successful open it loads that file's saved positions into the graph. When the graph reports a completed drag, it saves the active file's current positions through `Settings.TrySet` or an equivalent layout-specific safe write and surfaces only a concise warning on failure.

## Tests

Add a UI smoke regression that opens a disposable synthetic HKX, moves a node, reloads that same path in a fresh window, and verifies the node returns to the saved coordinates. The test must fail before persistence is implemented. Add focused settings serialization coverage for path isolation and invalid saved values if the new API has logic not naturally covered by that end-to-end smoke test.
