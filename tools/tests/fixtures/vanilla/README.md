# Vanilla save-fidelity corpus

Committed Fallout 4 **vanilla** behaviour and character fixtures consumed by
`SaveFidelityCorpusTests` (the no-op save regression) and used as the golden
input for `symrm nullsave`. Every file here is a genuine Bethesda-shipped file,
not a synthetic fixture.

## Provenance

| Field | Value |
|---|---|
| Source install | `D:/Fallout4Backup/Fallout 4` — pristine, unmodded: base game + all DLC |
| Base archive | `Data/Fallout4 - Animations.ba2` (version 1, 29,716 entries) |
| DLC archive | `Data/DLCCoast - Main.ba2` (`DLCCoast` = DLC03) |
| Extraction | `symrm extract <archive> <path> <dir> --tree` (preserves archive paths) |
| Verification | `symrm nullsave` + `symrm packfile` + `symrm relayout` against the whole extracted pool |

Note: in Fallout 4 every `.hkx` — behaviours, skeletons, animations alike —
lives in `Fallout4 - Animations.ba2` (and DLC `- Main.ba2` files); the
`- Meshes.ba2` archive holds `.nif`/`.bto`/`.ssf` only.

## Why these eleven files

Selection targets layout diversity within the vanilla corpus, per the #195
feasibility pass. Every vanilla FO4 packfile carries the same three sections
(`__classnames__`, `__types__`, `__data__`, version 11, layout `8 1 0 1`,
8-byte pointers), so the diversity is in content and scale:

| File | Kind |
|---|---|
| `Character/Behaviors/WeaponBehavior.hkx` (1.1 MB) | largest character behaviour; heavy `hkbBehaviorGraph` state machine |
| `Character/Behaviors/RaiderRootBehavior.hkx` | the player-character root behaviour |
| `Character/Behaviors/Locomotion_8wayBlend.hkx` | 8-way blend trees (`hkbBlenderGenerator`) |
| `Character/Behaviors/SingleAnimFurniture.hkx` | minimal single-animation behaviour |
| `Character/Behaviors/SyncedAnimBehavior.hkx` | synced-animation behaviour |
| `RadRoach/Characters/RadRoachCharacter.hkx` | small creature behaviour |
| `RadRoach/Animations/TurnLeft90.hkx` | creature animation (`hkaSplineCompressedAnimation` container) |
| `Character/Animations/Paired/PairedKill2HMBashKneeAndHead_AttackerLead.hkx` | lossless-compressed paired kill animation (`hkaLosslessCompressedAnimation`) |
| `DLC03/RadChicken/Behaviors/CritterRootBehavior.hkx` | DLC critter behaviour (RadRabbit ships the identical file — kept once) |
| `Character/CharacterAssets/skeleton.hkx` | character skeleton (`hkaSkeleton` in `hkaAnimationContainer`) |
| `Character/CharacterAssets/Hair/Female/FemaleHair04.hkx` | cloth hair FK file |

## Fidelity contract (per file, in `manifest.json`)

The no-op save under test is `NativeSave.Apply(bytes, emptyPlan)` — the exact
serialization the app writes for an unchanged document
(`Read → NativeLayout.RequireWritable → signature check → FixupOrder.Reorder
→ Rebuild`).

* **`byte`** — the no-op save is byte-identical to the source. Step-0 result:
  every behaviour and animation in the extracted pool (130 of 160 files,
  including all 44 character behaviours and all 33 DLC behaviours) round-trips
  byte-identically, so this is the default expectation for canonical FO4 files.
* **`structural`** — a legal rebuild re-lays layout while preserving the object
  graph. Step-0 finding: skeleton files (both the character and radroach
  `skeleton.hkx`, plus all 28 hair/FK files under pure re-layout) rebuild
  byte-differently because `FixupOrder.Reorder` strips 0xFF filler from fixup
  tables, shifting pointer values. The XML rendering of the saved file is
  **identical** to the source — verified by `symrm nullsave` — so the
  structural contract is: same class names, same instances, clean signatures,
  identical XML.
* **`refused`** — the save path refuses the file cleanly. Step-0 finding: the
  hair/FK files carry `hcl*` (cloth) classes this build does not define;
  `NativeSave.Apply` throws before touching any bytes. The test asserts the
  refusal is explicit and no bytes come out.

## Step-0 measurement (2026-08-28)

Across the full extracted pool (160 files from base + DLC03):

```
160 file(s): 130 saved byte-identically, 2 differed, 28 refused
```

The 2 differed files are the two `skeleton.hkx` files (both re-lay the fixup
tables, XML identical); the 28 refused are the hair/FK cloth files. `symrm
packfile` (pure `Read → Rebuild`, no fixup reorder) round-trips **all** 160
byte-identically, which isolates the fixup canonicalization as the only source
of byte drift.

The lossless-class member (`PairedKill2HMBashKneeAndHead_AttackerLead.hkx`)
was located by scanning the character animation subtree (6,468 files) for the
`hkaLosslessCompressedAnimation` class name; it reads as lossless under BGS's
own reader and round-trips byte-identically. A 4-byte-layout fixture is not
available in PC vanilla data (console-only), so the corpus's pointer-width
coverage stays with the 8-byte layout plus the synthetic 4-byte cases already
exercised in `symrm` self-tests.

## Regenerating or extending

```bash
# extract a candidate pool from a vanilla install
symrm extract "D:/Fallout4/Data/Fallout4 - Animations.ba2" "Actors/Character/Behaviors" pool --tree
# classify it
symrm nullsave pool
# copy the chosen files here, update manifest.json with their sha256 + fidelity
```

`symrm nullsave <file-or-dir>` is the CLI twin of the corpus test: it reports
`byte-identically / differed / refused` per file and flags whether a differed
file is structurally identical (XML) or genuinely different.

## Licensing note

These are Bethesda-shipped vanilla assets, committed to this **private**
repository for regression testing only, consistent with the existing committed
game fixtures in this repo (`tools/symrm/samples/TurretStandingSkeleton.hkx`,
`Radicle/testdata/*.esp`). No modlist or third-party content is included; do
not redistribute outside this repository.
