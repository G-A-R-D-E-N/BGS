# Headless scanning

Behaviour Graph Studio can scan a whole load order from the command line, without opening the
window, and report behaviour/animation defects that would otherwise only show up as an in‑game
crash or a broken animation. This is meant for load‑order authors and for gating a build in CI.

There are three scan modes, each on its own page:

| Mode | Answers | Page |
|---|---|---|
| `--scan-archives` | Are any behaviour/character `.hkx` files in these archives (or loose) corrupt or unreadable? | [scan-archives](scan-archives.md) |
| `--scan-modlist` | Do the character files a modlist actually loads still have all the animations they declare? | [scan-modlist](scan-modlist.md) |
| `--scan-clips` | Does every clip that a loaded behaviour plays resolve to an animation that exists in the load order? | [scan-clips](scan-clips.md) |

`--scan-modlist` and `--scan-clips` read a **Mod Organizer 2 instance** — see
[MO2 instance setup](#mo2-instance-setup) below. `--scan-archives` just walks a folder.

## Running a scan

The scan modes are built into the same executable as the GUI. Passing a `--scan-*` flag runs the
scan and exits instead of opening the window:

```bash
BehaviourGraphStudio.exe --scan-archives "D:\Mods\MyAnimationMod"
BehaviourGraphStudio.exe --scan-modlist "C:\MO2\Instances\Fallout 4"
BehaviourGraphStudio.exe --scan-clips  "C:\MO2\Instances\Fallout 4"
```

On Linux, run `BehaviourGraphStudio` (no `.exe`). Quote any path that contains spaces.

## Exit codes

Every mode returns the same three exit codes, so a scan can gate a script or a CI job:

| Exit | Meaning |
|---|---|
| `0` | Clean — nothing inspected was defective. |
| `1` | Defects found — corrupt files, missing animations, or unresolved clips. |
| `2` | Usage or configuration error — a missing/omitted path argument, or an MO2 instance that could not be opened. |

```bash
BehaviourGraphStudio.exe --scan-clips "C:\MO2\Instances\Fallout 4"
if [ $? -ne 0 ]; then echo "load order has behaviour defects"; fi
```

## Machine‑readable output (`--json`)

Add `--json` to any mode to write a single JSON report to **stdout**. All progress and layout
lines go to **stderr**, so redirecting stdout always yields one parseable document:

```bash
BehaviourGraphStudio.exe --scan-clips "C:\MO2\Instances\Fallout 4" --json > report.json
```

The report is stable and versioned (`schemaVersion`), so CI consumers can detect envelope changes:

```json
{
  "schemaVersion": 1,
  "mode": "scan-clips",
  "context": { "target": "C:\\MO2\\Instances\\Fallout 4", "profile": "Play", "dataFolder": "...", "modRoots": 12 },
  "summary": { "checked": 40, "broken": 2, "missingClips": 5, "unreadable": 0 },
  "findings": [
    { "kind": "missing-clip-animations", "path": "meshes/actors/.../y.hkx",
      "source": "loose:MyMod", "details": ["clip 'c' plays 'a' - resolves nowhere"] }
  ],
  "exitCode": 1
}
```

- `exitCode` in the report always matches the process exit code, so CI can gate on either.
- Keys are camelCase; fields that do not apply (`profile` / `dataFolder` for `--scan-archives`, a
  finding `source`) are omitted rather than set to `null`.
- **Even a usage/config failure emits JSON** — a bad path or an unopenable instance produces a valid
  report with `exitCode: 2` and a `usage` or `config` finding, never a bare stderr line. Redirected
  stdout is always JSON.

Each mode page lists its own `summary` field set and the finding `kind`s it produces.

## MO2 instance setup

`--scan-modlist` and `--scan-clips` take the **root of a Mod Organizer 2 instance** — the folder
that contains `mods/` and `profiles/`. The scan layers the enabled mods over the base game exactly
the way MO2 and the engine do, so it sees the same winning files the game would load.

For a scan to open an instance it needs:

- **`mods/`** — the mod library. Required.
- **A modlist.** The active profile is read from `ModOrganizer.ini`; its
  `profiles/<profile>/modlist.txt` decides which mods are enabled and in what order (highest
  priority first). If the INI does not name an active profile, a root `modlist.txt`, then
  `profiles/Default/modlist.txt`, then a single profile that has a `modlist.txt` are tried in turn.
- **A Fallout 4 `Data` folder.** Resolved from the `gamePath` in `ModOrganizer.ini`; if that is not
  available, a `Stock Folder/Data` beside the instance is used. This provides the base‑game files
  that mods override.
- **The overwrite folder** (optional) — taken from the INI's `overwrite_directory`, else
  `overwrite/` under the instance. It wins over every mod, matching MO2.

If none of these resolve, the scan exits `2` with a message naming what was missing (for example
`no mods/ directory under <instance>` or `could not resolve Fallout 4 Data`).

A portable/standalone instance works out of the box. For a global MO2 install, point the scan at
the specific instance directory under `%LOCALAPPDATA%\ModOrganizer\<instance>`.

## What a scan does and does not prove

These scans validate **virtual‑path resolution and the structure of the files they can describe**:
does the declared animation resolve to a loose file or an archive entry, and does a describable
Fallout 4 behaviour/character file pass its structure check. Two boundaries are worth knowing:

- **Existence is not loadability.** Whether a *resolved* animation's payload can actually be decoded
  and bound at runtime is a separate concern tracked in issue #201 (and its follow‑ups).
- **Undescribable layouts are skipped, not validated.** `--scan-archives` only structure‑checks FO4
  HKX files BGS can describe; an unrecognised or unsupported layout is passed over rather than
  flagged (see [scan-archives](scan-archives.md)).

So a clean scan means "every referenced animation is present, and every describable behaviour file
passed its structure check" — not "the load order cannot possibly fault."
