# `symrm` CLI reference

`symrm` is Behaviour Graph Studio's native verification and diagnostic command-line harness.
It is primarily a developer/reverse-engineering tool. Run it from `BGS/` with:

```bash
dotnet run --project tools/symrm/symrm.csproj -- <command> [arguments]
```

For the complete overview:

```bash
dotnet run --project tools/symrm/symrm.csproj -- --help
```

Every command has its own usage, flags, and example:

```bash
dotnet run --project tools/symrm/symrm.csproj -- <command> --help
```

The installed/published executable uses the shorter equivalent `symrm <command> --help`.

## Commands

| Command | Purpose |
| --- | --- |
| `corpus` | Extract matching HKX files from a BA2 corpus. |
| `check` | Validate native HKX data and optionally resolve game-data references. |
| `states` | Summarize state generators and dangling state references in unpacked XML. |
| `events` | Audit event declarations and their roles in unpacked behaviour XML. |
| `frames` | Inspect decoded animation frame/track data or print a compact digest. |
| `scale` | Inspect decoded animation scale channels. |
| `skeleton` | Print skeleton bones and reference transforms. |
| `rig` | Audit rig/skeleton data across one file or a corpus. |
| `extract` | Extract matching BA2 entries, optionally preserving their directory tree. |
| `ba2` | Inspect a Bethesda BA2 archive and its entries. |
| `motion` | Inspect decoded root-motion data. |
| `pose` | Pose a decoded animation against a skeleton and print the result. |
| `channels` | Compare animation channels with the rig they drive. |
| `packfile` | Inspect packfile sections, fixups, and round-trip structure. |
| `nullsave` | Run the diagnostic no-op native save and byte comparison. |
| `layout` | Audit native object layout and placement. |
| `relayout` | Exercise native relayout/rebuild behavior. |
| `ground` | Inspect pointer/fixup grounding for a native packfile. |
| `offsets` | Inspect reflected member offsets against shipped layout data. |
| `convert` | Convert a packfile between 32-bit and 64-bit pointer layouts. |
| `compare` | Byte/section compare two native packfiles. |
| `delete` | Exercise native object deletion on deletable graph objects. |
| `paste` | Exercise native object paste/copy authoring paths. |
| `template` | Exercise graph-template authoring against native files. |
| `conditions` | Inspect and exercise transition-condition authoring. |
| `savedelete` | Delete an eligible object, save, reload, and verify the result. |
| `classcheck` | Compare a class/layout dump with the shipped Havok class table. |
| `types` | Inspect class metadata carried by a packfile. |
| `chain` | Trace behaviour-to-animation resolution and report missing links. |
| `crash` | Resolve an AnimTextData subgraph crash hash and report missing animations. |
| `hash` | Compute the Fallout 4 AnimTextData subgraph id for a behaviour path and SAPT prefixes. |
| `sweep` | Sweep discovered subgraph manifests for animation coverage gaps. |
| `diff` | Compare vanilla and modded subgraph/behaviour coverage. |
| `notes` | Inspect animation annotations/notes in native files. |
| `saveevent` | Exercise event edits through native save/reload verification. |
| `savewide` | Exercise wide/vector field edits through native save/reload verification. |
| `savenumbers` | Exercise numeric-array edits through native save/reload verification. |
| `walk` | Walk native objects and report reachability/reference structure. |
| `signatures` | Inspect Havok class signatures used by native files. |
| `paths` | Audit serialized object/reference paths. |
| `elements` | Inspect inline array/struct elements and their serialization. |
| `nesting` | Inspect native object nesting/ownership relationships. |
| `objects` | List native objects, optionally filtering by Havok class. |
| `capacity` | Audit Havok array capacity/count serialization. |
| `qstransform` | Inspect `hkQsTransform` values and layout behavior. |
| `splinestats` | Summarize spline-compressed animation structure and error metrics. |
| `spline` | Decode and inspect spline-compressed animation data. |
| `savespline` | Exercise spline-animation edits through native save/reload verification. |
| `editframe` | Exercise decoded animation frame edits and verify the result. |
| `trim` | Exercise animation trimming and verify decoded output. |
| `retime` | Exercise animation retiming and verify decoded output. |
| `run` | Run the behaviour simulation harness and optionally send events. |
| `weights` | Inspect runtime generator/blend weights. |
| `cliptime` | Resolve clip timing from animation data beside a behaviour. |
| `cliptrim` | Audit clip crop/trim values against resolved animation duration. |
| `mesh` | Inspect NIF mesh geometry and skinning data. |
| `meshpng` | Render a skinned-mesh diagnostic image. |
| `lifecycle` | Run the native open/edit/save/reload/validate/render lifecycle gate. |
| `test` | Run the native/Havok regression harness used by BGS CI. |
| `defaults` | Compare game class-registration defaults with the shipped table. |

## Command-specific switches

The catalog exposes the switches currently parsed by `symrm`:

- `frames`: `--digest`
- `extract`: `--tree`
- `types`: `--members`
- `defaults`: `--write`
- `check` and `chain`: `--data`, `--plugins`
- `crash` and `sweep`: `--data`, `--mods`, `--profile`
- `diff`: `--data`, `--mods`, `--profile`

Commands not listed in that section currently use positional arguments only. Use `<command> --help`
rather than relying on this summary when scripting a command; the executable help is the canonical
usage/flags/example source.
