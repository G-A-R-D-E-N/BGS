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
- Self-contained Windows and Linux packages with build identity shown by `--version` and About.

## Authoring

- Supported behaviour and animation edits continue to use the verified native save pipeline.
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
- Engineering frame mode and constrained Drop/Recover preview for measured ragdoll data.

The Drop/Recover control is an engineering preview and is explicitly not a full Havok simulation.

## Save safety and compatibility

- Supported saves retain backup and recovery behavior, verify output before publication, and refuse
  unsafe or unverified writes without modifying the source.
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
  implemented. Some physics and cloth systems remain inspection-only.
- Havok 2018 support is incomplete. Current Fallout 76 evidence reflects Havok 2015.1.0, not a
  completed Havok 2018 implementation.
- Unsupported Havok layouts/classes and conversion paths remain read-only or are refused. BGS never
  writes guessed data; unsupported editing reports the reason and leaves the source unchanged.

These limitations are intentional release boundaries for 1.1.0 and are documented in the UI or
specialist notes where the relevant tool is opened.
