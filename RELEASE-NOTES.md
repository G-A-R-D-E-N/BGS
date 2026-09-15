# Behaviour Graph Studio 1.1.0

Behaviour Graph Studio 1.1.0 is a release-hardening update focused on dependable Fallout 4
inspection, measured authoring, and safe diagnostics. Unsupported operations remain read-only or
fail closed instead of writing guessed data.

## Major changes

- Headless archive, modlist/load-order, and clip scans for CI and mod validation.
- Versioned JSON reports, progress reporting, archive indexing/caching, extraction, and deterministic
  compare exports.
- Crash-analysis helpers for AnimTextData subgraphs and missing animation coverage.
- Round-trip diagnostics, save-fidelity fixtures, packfile conversion checks, and stronger save
  transaction safety.
- A read-only stdio MCP server with bounded behavior, object, animation, project, and search
  inspection, plus a side-effect-free clip-animation preview.
- An optional in-app Assistant drawer with provider integration, bounded editor context, and
  explicit approval for revision-bound clip changes.
- A platform-safe Bridge layout with drag-and-drop handling that remains available alongside the
  Assistant drawer, including deterministic mounted-turret mesh resolution.
- A workspace shell with Home, Graph, Inspect, Animation, and Project activities, contextual
  secondary views, grouped command controls, and horizontally aligned grid headers.
- Measured ragdoll body-frame and centre-of-mass inspection, frame-distance measurement, and
  constrained Drop/Recover preview controls with lifecycle and stale-state protection.
- Verified structure-authoring save/reopen coverage for supported graph data, with explicit refusal
  of unsupported or unverified writes.
- Self-contained Windows and Linux packages with build identity shown by `--version` and About.

## Authoring

- Supported behaviour and animation edits continue to use the verified native save pipeline.
- Structure authoring supports expression transition conditions, enter/exit notify events, global events, and non-empty state-machine creation and attachment, with verified save/reopen coverage.
- Native graph authoring, templates, variables, transitions, symbols, and supported arrays are
  covered by validation and save/reopen checks.
- Skeleton editing and validation, skin-weight editing, and NIF skin-weight persistence are included.
- Simple skeleton-mapper row authoring is included for explicit caller-provided pairs, with measured
  validation and conservative refusal of malformed or unsafe inputs.

## Fallout 4 analysis tools

- Scan archives, enabled modlists, and merged clip resolution without opening the desktop UI.
- Inspect project chains, animation references, crash-log subgraphs, and machine-readable reports.
- Read vanilla files directly from BA2 archives while preserving read-only behavior for archive copies.
- Compare behaviour files with filtering and deterministic exports.

## Animation / skeleton / skin

- Animation playback, frame editing, trimming, retiming, spline diagnostics, and skeleton rendering.
- Skeleton hierarchy editing and validation, retarget/skeleton-mapper reading, and measured simple
  mapper-row construction.
- Skin influence inspection/editing and verified NIF weight persistence.
- Embedded NIF Havok payload reading and bounded Havok tagfile groundwork.

## Physics / ragdoll

- Read-only physics and cloth inspection, including bodies, shapes, constraints, body frames, centre
  of mass, pivots, limits, engineering labels, and animation-to-ragdoll mapping.
- Pin two body frames to inspect their measured distance, with self-pair refusal and state that
  remains stable while inspecting constraints or hovering the viewport.
- Engineering frame mode and constrained Drop/Recover preview for measured ragdoll data.

The Drop/Recover control is an engineering preview and is explicitly not a full Havok simulation.

## Save safety and compatibility

- Supported saves retain backup and recovery behavior, verify output before publication, and refuse
  unsafe or unverified writes without modifying the source.
- Assistant clip changes stay in the active document until explicit UI approval; saving remains a
  separate verified action with normal undo, backup, and source-stamp guarantees.
- External MCP remains read-only: it has no write-capable tool, shell access, arbitrary filesystem
  writes, or caller-controlled approval path.
- Playback can resolve adjacent skeletons and matching mounted-turret meshes deterministically, and
  Bridge file drops accept the platform's single-file and multi-file storage payloads.
- Round-trip and save-fidelity coverage protects unknown data and existing file structure where the
  current reader/writer can prove preservation.
- Fallout 4 packfiles are the primary supported Havok target. Self-contained packages target Windows
  x64 and Linux x64.

## Known limitations

- Skeleton-mapper serialization/write-back is not complete, and chain mapper authoring is disabled.
  Shipped Fallout 4 assets do contain chain mappings and unmapped-bone data; their complete authoring
  semantics are not yet reduced.
- Automatic mapper bone pairing is not proven. Simple mapper authoring requires explicit pairs.
- Reciprocal mapper generation is a construction tool. The measured human fixture reproduces its
  shipped reciprocal, but shipped mapper pairs are not universally mirror-symmetric.
- Full ragdoll authoring, body-to-body collision simulation, and a full Havok physics solver are not
  implemented. Current ragdoll support extracts measured bodies, shapes, constraints, bone
  bindings, frames, and animation mappings for inspection and a constrained preview; it does not
  write ragdoll data or run a complete physics simulation.
- Cloth support is inspection and validation only. BGS can read bounded cloth shapes, particles,
  buffers, collidables, constraints, operators, states, and simulation-cloth references, but it
  does not simulate cloth, deform meshes, author cloth data, or save cloth changes.
- The Drop/Recover control is a static engineering preview that follows measured poses; it is not
  Havok dynamics, body collision, cloth interaction, or a replacement for in-game validation.
- Havok 2018 support is incomplete. Current Fallout 76 evidence reflects Havok 2015.1.0, not a
  completed Havok 2018 implementation.
- Unsupported Havok layouts/classes and conversion paths remain read-only or are refused. BGS never
  writes guessed data; unsupported editing reports the reason and leaves the source unchanged.

These limitations are intentional release boundaries for 1.1.0 and are documented in the UI or
specialist notes where the relevant tool is opened.

## Manual Windows smoke checklist

CI cannot validate a real Fallout 4 desktop session. Before publishing, run this checklist with
the packaged Windows build and vanilla Fallout 4 assets:

- [ ] Launch BGS and confirm About / `--version` reports `1.1.0` and the expected build.
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
