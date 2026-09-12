# `--scan-archives` — find corrupt behaviour files

```bash
BehaviourGraphStudio.exe --scan-archives "<folder>" [--errors-only] [--json]
```

Walks every `.ba2` archive under `<folder>` (recursively) **plus** every loose `.hkx`, and runs a
structure check on the behaviour/character files it can describe — the kind of corruption that makes
a file fail to load. Non‑behaviour files are ignored. Use it to vet a mod, a download, or a build
output folder before shipping or installing it.

Unlike the modlist modes, this mode does **not** need an MO2 instance or a game install — it just
reads the folder you point it at.

> **What "clean" means here.** The structure check runs on Fallout 4 behaviour/character HKX files
> that BGS can describe. A file that is not a recognised FO4 HKX, or a FO4 file whose class layout
> BGS cannot yet describe, is **not** currently reported as corrupt by this mode — it is passed over,
> not validated. So a clean `--scan-archives` means "no describable FO4 behaviour file failed its
> structure check," not "every file present is valid."

## What it finds

| Result | Meaning |
|---|---|
| `CORRUPT` | The file parsed but failed a behaviour structure check. The indented lines below name each problem. |
| `UNREADABLE` | An archive entry could not be extracted/read at all (bad compression, truncated data). |
| `skip` | A `.ba2` could not be opened. This is a **warning**, not a failure — see below. |

### Reading the output

```
scanning 2 .ba2 archive(s) under D:\Mods\MyAnimationMod
  opened 1/2 archive(s)...
CORRUPT  Meshes.ba2 :: meshes/actors/character/behaviors/0_master.hkx
    root object is not hkbBehaviorGraph
  opened 2/2 archive(s)...
skip     Textures.ba2: Textures.ba2 is a DX10 archive, not GNRL. Textures are stored differently and nothing here reads them.
scanning 3 loose behaviour/character .hkx
  checked 1/3 loose file(s)...
  checked 2/3 loose file(s)...
  checked 3/3 loose file(s)...
done  37 behaviour/character hkx scanned across 1 archive(s) + loose, 1 with errors
```

- `CORRUPT ... :: <entry>` is a file **inside** an archive; `CORRUPT ... (loose)` is a loose file.
- The `  opened N/N` and `  checked N/N` lines are progress written to **stderr** — one per step for
  a small folder like this, throttled to ~20 updates total on a large one; everything else is on
  stdout. Under `--json` the progress stays on stderr and stdout carries only the report.
- The final `done` line summarises: how many individual behaviour/character `.hkx` were scanned,
  across how many archives (plus loose), and how many had errors. "archives" here counts only those
  that held at least one relevant file (see the [JSON summary](#json-summary-and-findings) below).

## Flags

- `--errors-only` — suppress the per‑`skip` lines in human output (only affects rendering; a `skip`
  is still recorded in `--json`). Useful when a folder has many non‑behaviour archives.
- `--json` — emit one JSON report to stdout (see the [overview](README.md#machine-readable-output-json)).

## The `skip` warning

A `.ba2` that cannot be opened is reported as `skip` but is **not** counted as a defect, so it does
not change the exit code. That is deliberate: the most common reason an archive won't open here is
that it simply isn't a behaviour archive (a DX10 texture archive, for example), which is not an
error. The reason is included in the finding's `details`.

If your workflow must treat "could not even inspect an archive" as a failure, run with `--json` and
fail when the report contains any `skip` finding, rather than relying on the exit code.

## JSON summary and findings

- `summary`:
  - `archives` — the number of `.ba2` archives that **contained at least one** relevant
    behaviour/character `.hkx`. An archive that opened but held nothing relevant is not counted, and
    one that could not be opened is a `skip`, not an `archive`.
  - `scanned` — individual behaviour/character `.hkx` checked (archive entries + loose).
  - `corrupt` — how many of those failed (structure errors + unreadable entries).
- Finding `kind`s: `corrupt`, `unreadable`, `skip`.
- Exit `1` when `corrupt > 0` (unreadable entries count as corrupt); otherwise `0`.
