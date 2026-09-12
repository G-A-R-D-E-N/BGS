# `--scan-modlist` — find missing declared animations

```bash
BehaviourGraphStudio.exe --scan-modlist "<MO2 instance>" [--json]
```

Opens a [Mod Organizer 2 instance](README.md#mo2-instance-setup), layers the enabled mods over the
base game the way the engine loads them, and for every **character** file provided by an enabled mod
or the overwrite folder, checks that each animation the character *declares* still resolves to a file
somewhere in the merged load order — including base‑game `Data`.

A character file (`hkbCharacterStringData`) lists the animation set it expects. If a mod removes or
replaces a file so that one of those declared animations no longer resolves, the game is missing an
animation it believes it has — this mode catches that before you launch.

## What it finds

| Result | Meaning |
|---|---|
| `MISSING ANIMATIONS` | The winning character file declares animations that resolve nowhere in the load order. Each missing path is listed beneath it. |
| `UNREADABLE CHARACTER` | The winning copy could not be read or parsed (bad data, or an HKX class layout BGS does not support). |

### Reading the output

```
profile Play; 12 active mod root(s); Data C:\MO2\Instances\Fallout 4\Stock Folder\Data
indexing mod archives for characters...
processed 3 archive(s); found 40 character file(s) to check
  scanned 40/40 character(s)...
MISSING ANIMATIONS  meshes/actors/character/characters/defaultmale.hkx  [loose:MyMod]  (3 of 250)
    Animations/MyMod/idle.hkx
    Animations/MyMod/walk.hkx
    Animations/MyMod/run.hkx
done  40 winning character file(s) checked, 1 with missing animations, 3 missing reference(s), 0 unreadable
```

- The header line shows the resolved profile, how many mod roots are active, and the `Data` folder
  used — check these match the load order you meant to scan.
- `[loose:MyMod]` / `[Archive.ba2]` after the path is **where the winning copy came from**.
- `(3 of 250)` means 3 of the 250 animations the file declares are missing. Human output lists up
  to 40 missing paths per file; `--json` carries the full list in `details`.

## What is scanned

`--scan-modlist` scans the character files supplied by your enabled mods and the overwrite folder —
not pure base‑game character files. Each scanned file's *declared* animations are then resolved
against the **full merged view**, base‑game `Data` included, so a mod that removes or replaces a
file a character depends on is caught even when the character itself is a base file overridden by a
mod. (Same principle as `--scan-clips`, which likewise scans mod/overwrite‑provided behaviours.)

## JSON summary and findings

- `summary`: `checked` (character files with declared animations), `broken` (files with at least
  one missing), `missing` (total missing references), `unreadable`.
- Finding `kind`s: `missing-animations`, `unreadable-character`, plus `archive-warning` for a mod
  archive that could not be indexed.
- Exit `1` when `missing > 0` or `unreadable > 0`; otherwise `0`.
