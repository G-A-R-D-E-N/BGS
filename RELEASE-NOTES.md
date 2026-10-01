# Behaviour Graph Studio 1.3.0

This update brings skeleton and skin tools into the editor, adds interactive collision-based physics preview, and lets the assistant propose approved actions across editor workspaces. Home's tour explains how to use the AI chat.

## Major changes

- Home's tour includes an AI chat guide for provider setup, clear requests, reviewing approvals and verifying edits and saves. Take the tour can be replayed.

- The assistant can operate editor and authoring controls through approved actions, including graph/rig gestures and explicit file paths. Existing asset checks and save behavior apply. Dispatch means an interaction was sent; the assistant must inspect the resulting editor state before claiming success. Credentials and approval controls are excluded.
- Top toolbar scrollbars sit below their controls without overlapping buttons or fields.
- Skeleton, skin and physics previews resize with the window, stay inside their viewport and offer Fit preview. Settings scroll separately from the preview.
- Home includes an Authoring tools section with direct skeleton, skin, physics, structure and batch editor shortcuts.
- Top workspace toolbars: Home, Graph, Inspect, Animation and Project select grouped commands above the editing area. Wide command groups scroll horizontally instead of filling the middle of the window.
- Graph hosts batch and structure authoring; Animation hosts skeleton/skin authoring. Duplicate workspace tab headers are removed; inspectors, results and the playback timeline remain in the workspace.
- Skeleton authoring: bone names, reference transforms, translation locks, parent changes, validation, mirror-pair inspection, undo/redo and verified save/reopen.
- Standalone skeletons support bone addition/removal and reordering. Dependent rigs refuse edits that need animation, mapper or physics index remapping.
- Skin authoring: per-vertex influences, normalization, pruning, bone-weight copying, mirror matching, mesh inspection, undo/redo and weight-only NIF persistence.
- Interactive physics: gravity, ground and body collisions, preview joints, pause/step/reset and selected-body impulses using BEPU 2.4.0.

## Safety and compatibility

- The loopback assistant bridge authenticates before accepting bodies or sending 100 Continue. Invalid content lengths are refused, active clients are bounded, and unauthenticated header waits have a short deadline.

- Assistant CLI output is capped per stream, cancellation is distinguished from timeout, and Windows npm OpenCode launches its native package executable directly.
- Assistant diff exports refuse linked/reparse-point destinations and ancestors and repeat the check immediately before writing. This validation does not make filesystem inspection and opening atomic against concurrent changes by another process.

- Assistant transcripts and provider bridge configs have owner-only permissions. Failed staged saves preserve the previous chat and remove temporary files.
- Cancelled provider requests retain prompts, active chats respect the saved history limit, missing CLI executables report an error, and API key changes refresh the combined model list. Codex isolation requires an explicitly empty workspace-root list.

- Skeleton and skin saves retain backups and refuse external file changes. Edits are verified in memory and after rebuilding the native file.
- NIF saving supports Fallout 4 BSVersion 130 vertex skin payloads. It preserves geometry, binding transforms and every byte outside the owned weight/index slots.
- Physics uses measured convex vertices and complete joint frames. Users explicitly set simulation masses and preview joint limits; inertia assumes uniform hull density.
- CI exercises the mounted editor save/reopen and physics reset/close lifecycle on both platforms.

## Known limitations

- Index-changing edits in dependent rigs require asset remapping and are refused. Skeleton mapper write-back remains unsupported.
- Physics settings stay in the preview window. Native angular atoms, motors, mass/inertia, collision materials and game solver behavior are not imported or modified.
- Missing/degenerate hulls, incomplete frames, compound/mesh geometry and unsupported native layouts are refused.
- The existing Drop/Recover control remains a constrained preview. The Physics simulation tab provides the separate collision-based simulation.
- Cloth simulation and cloth write-back are not implemented.

See the [rig authoring guide](guide/rig-authoring.md) for editor steps and save boundaries.

## Manual Windows smoke checklist

CI cannot validate a real Fallout 4 desktop session. Before publishing, run this checklist with
the packaged Windows build and vanilla Fallout 4 assets:

- [ ] Launch BGS and confirm About / `--version` reports `1.3.0` and the expected build.
- [ ] Open a vanilla behaviour HKX; browse Tree and Graph.
- [ ] Open and play an animation; verify skeleton rendering.
- [ ] Open the physics/ragdoll inspector; verify bodies, constraints, pivots, centre-of-mass and
      frame data.
- [ ] Exercise frame-distance/body-frame engineering controls and Drop/Recover; confirm the UI
      presents Drop/Recover as an engineering preview, not a full solver.
- [ ] Make one supported behaviour edit, save, reopen, and verify the edit and backup.
- [ ] Make one supported skeleton or skin edit if exposed by the build, save/reopen, and verify it.
- [ ] Attempt one unsupported operation and confirm it refuses without changing the source.
- [ ] Verify Compare and one headless scan command.
