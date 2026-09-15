# Behaviour Graph Studio Changelog

Changes that affect Behaviour Graph Studio users are documented here.

New to BGS? See the [Getting Started Guide](https://prisma-user-interface-framework.github.io/Prisma2.0/tools/behaviourgraphstudio/guide/getting-started).

## Unreleased

* Editor workspace: a left activity rail (Home, Graph, Inspect, Animation, Project) with contextual secondary views, and one command bar for Open, Save, Undo, Redo and validation. Tree expand/collapse and the object filter live on Inspect → Tree. Graph find is a separate field on the Graph toolbar.
* Batch authoring and About sit in the command bar on the production window path, not on a second top strip.
* Tree/grid column headers now scroll horizontally with their rows.
* Added a bounded read-only stdio MCP server for behavior, object, animation, project-chain, project-search, and project-check inspection, plus a side-effect-free clip-animation preview.
* Added an optional in-app Assistant drawer with an OpenAI-compatible provider boundary, bounded editor context, cancellation, and sequential tool execution.
* Assistant clip changes require explicit UI approval and remain bound to the exact active-document revision, object, old value, and new value; normal undo and verified save behavior remain in force.
* External MCP has no write-capable tool, shell/process access, arbitrary filesystem write, or caller-controlled approval path.
* Bridge layout and drag-and-drop handling are platform-safe alongside the Assistant drawer, with deterministic mounted-turret mesh auto-resolution.
* Structure authoring now covers supported transition conditions, enter/exit notify events, global events, and non-empty state-machine creation and attachment through the verified save/reopen path.
* Playback resolves adjacent skeletons and matching mounted-turret meshes deterministically, including platform-safe single-file and multi-file Bridge drops.
* The ragdoll inspector exposes measured body frames, centres of mass, pivots, limits, engineering labels, and animation-to-ragdoll mapping; pinned body-frame distance measurement refuses self-pairs and stale selections.
* Drop/Recover remains a constrained engineering preview with lifecycle reset and stale-state protections rather than a full physics simulation.
* The workspace shell groups existing tools under Home, Graph, Inspect, Animation, and Project activities, keeps contextual secondary views reachable, and keeps command controls accessible at narrow widths.

### Current release boundaries

* Ragdoll support is inspection and constrained preview only: measured bodies, shapes, constraints, bone bindings, frames, and animation mappings are displayed, but ragdoll authoring, body collision solving, and full Havok dynamics are not implemented.
* Cloth support is inspection and validation only: bounded cloth structures and references can be read and checked, but cloth simulation, mesh deformation, cloth authoring, and cloth save/write-back are not implemented.
* Drop/Recover follows measured poses as an engineering preview; it is not a Havok solver, body-collision simulation, cloth interaction, or in-game behavior substitute.
* Skeleton-mapper serialization/write-back and chain mapper authoring remain incomplete; automatic bone pairing is not proven.
* Havok 2018 support remains incomplete, and unsupported layouts or conversions remain read-only or are refused.

## 1.1.0

August 27, 2026

The complete user-facing summary, authoring boundaries, compatibility notes, and known limitations
are in the [Behaviour Graph Studio 1.1.0 release notes](RELEASE-NOTES.md).

<details>
<summary><strong>Headless Load-Order Scanning</strong></summary>
<br>

Behaviour Graph Studio can now scan a whole load order from the command line, without opening the
window — for vetting a mod or gating a build in CI. See the
[Headless scanning guide](guide/scan/README.md) for full details, the JSON schema, and MO2 setup.

* `--scan-archives <folder>` walks every `.ba2` under a folder (plus loose files) and reports
  behaviour/character `.hkx` files that are corrupt or unreadable. No game install or MO2 instance
  required. See the [scan-archives guide](guide/scan/scan-archives.md).
* `--scan-modlist <MO2 instance>` layers a modlist's enabled mods over the base game the way the
  engine loads them, and reports character files whose declared animations no longer resolve. See
  the [scan-modlist guide](guide/scan/scan-modlist.md).
* `--scan-clips <MO2 instance>` checks, for every behaviour a mod actually loads, that each clip's
  animation resolves somewhere in the merged load order. It reports unresolved virtual references;
  it does not reproduce the game's animation-DB or binding behavior.
  See the [scan-clips guide](guide/scan/scan-clips.md).
* Every mode returns a clear exit code — `0` clean, `1` defects found, `2` usage/config error — so a
  scan can gate a script or CI job directly.
* Add `--json` to any mode for a single, versioned JSON report on stdout (progress goes to stderr,
  so the redirected output is always one parseable document). The report's `exitCode` always matches
  the process exit code.
* Large load orders scan faster: each mod archive's index is parsed once per run, and existence
  checks no longer decompress the file they are only confirming is present. Progress is streamed
  while a scan runs.

</details>

## 1.0.6

August 17, 2026

<details>
<summary><strong>Crash Log Diagnosis</strong></summary>
<br>

* `symrm crash <hash | crashlog.txt> --data <Data folder>` now resolves an AnimTextData subgraph hash (the `AnimationOffsets\<id>.txt` name a crash log complains about) to the subgraph it names.
* The resolver reads the game's own `AnimationFileData\<id>.txt` manifests from the .ba2 archives (loose files win), so no hash algorithm is reimplemented — the engine's ground truth is used.
* It reports the subgraph's behavior graph(s), the archive the manifest came from, whether its offset-data file exists, and which of the subgraph's animations are missing from the game data.
* Loose manifest overrides (e.g. a merged AnimTextData mod) are honored, matching how the engine resolves the tree.
* New `symrm hash <behavior.hkx> <sapt> [...]` computes the subgraph id itself from a race record's behavior graph and animation folder prefixes. The algorithm was reverse-engineered from the game binary and validated against the game's shipped ids: it is CRC-32 (reflected 0xEDB88320 table, init 0, no final xor) of the lowercased prefix list joined by `|` in the high half, and of the lowercased behavior path in the low half. Every base-game subgraph id recomputes exactly from its race record.
* `symrm crash` then resolves the subgraph to the specific per-weapon clip that is missing: it opens each behavior the manifest names (plus the behavior its `AnimationOffsets` file hints at) and runs the engine-search check, reporting the failing weapon chain prefix and whether a generic `Animations\<clip>` fallback exists for every unresolved clip.
* `symrm crash --mods <MO2 mods folder> [--profile <name>]` resolves the same hash against modded game data: enabled mods from the modlist (or `profiles/<name>/modlist.txt`) are layered over the base game the way the engine loads them — loose mod files and mod .ba2 archives override base data, the profile overwrite folder wins, and merged AnimTextData manifests shipped loose in a mod are honored.
* New `symrm sweep --data <Data folder> [--mods ...]` runs the crash resolution across every shipped subgraph hash and reports any that resolve to a per-weapon clip gap, as a regression sweep (the base game ships clean: 3401 manifests / 2238 weapon subgraphs, zero gaps).
* New `symrm diff --data <Data folder> --mods <MO2 mods folder>` compares vanilla and modded subgraph coverage side by side: which AnimationFileData manifests and behavior paths the modlist adds or drops, and which of the new behaviors are weapon subgraphs.
* Modded data lookups are indexed once, so per-file checks stay O(1) no matter how many mod roots are layered on.
* Resolving a crash hash on the Chain tab now floats a panel over the graph canvas: it names the subgraph's behaviors, the animation presence, and every per-weapon gap, and each gap has a **Jump** button that selects and centres the missing clip's node on the graph.
* The Chain tab's new **Sweep all subgraphs** button runs the same whole-load-order per-weapon check as `symrm sweep` in the app: it scans every AnimationFileData manifest and reports each subgraph with a per-weapon gap (behavior, id, and the exact missing clips), running off the UI thread so the studio stays responsive.
* The Chain tab has a **mods folder** field (the MO2 instance root): when set, game data, the crash-hash lookup, and the sweep all run against the modlist the way the engine loads it — enabled mods from `modlist.txt` (or the profile's), the profile overwrite folder, mod loose files and mod .ba2 archives overriding base data, and merged AnimTextData manifests honored.

</details>

## 1.0.5

August 15, 2026

<details>
<summary><strong>The Bridge</strong></summary>
<br>

* Added The Bridge, a new home tab that puts the main parts of BGS in one place.
* See your current file, jump directly to tools, and quickly find where something lives in the studio.
* Added a first-run tour. You can replay it from the Bridge header at any time or press `Escape` to close it.
* BGS now remembers which tab you were using and returns to it the next time you open the app.
* Added search to the Bridge. Stations and reference entries filter as you type.
* Drag and drop a `.hkx` file anywhere onto the Bridge to open it.
* Recent files are now available directly from the Bridge.

</details>

<details>
<summary><strong>Safer Saving</strong></summary>
<br>

* Saving no longer temporarily removes the original file while replacing it.
* If BGS or the system crashes during a save, the original file will not disappear as a result.
* BGS now detects if another program changes a file while it is being saved. The save is stopped instead of overwriting the newer version.
* Interrupted saves can be recovered the next time you save the same file.
* The version replaced during recovery is preserved as a backup.
* Recovery only touches files created by BGS during the save process. Other files next to your `.hkx` are left alone.
* A backup rotation problem no longer causes BGS to report the entire save as failed when the main file was written successfully.

</details>

<details>
<summary><strong>File Handling &amp; Safety</strong></summary>
<br>

* Large valid arrays load correctly again. BGS no longer rejects them because of an arbitrary element limit.
* Malformed arrays are rejected before memory is allocated for them, with an error explaining the problem.
* `.ba2` extraction now rejects unsafe paths that could write outside the folder you selected.

</details>

<details>
<summary><strong>Game Data Aware Validation</strong></summary>
<br>

* `symrm chain` and `symrm check` now accept `--data <Data folder>` and resolve every animation against the game's `.ba2` archives (loose files first, archives in plugin load order), so vanilla animations are no longer reported missing just because they are packed.
* The Chain tab has a Game Data folder field: set your game's `Data` folder once and every chain, graph check and project check resolves animations against its `.ba2` archives, with each animation marked as loose or naming the archive it came from.
* With game data attached, an animation that is genuinely absent anywhere becomes an error instead of a warning.
* Weapon subgraphs (behaviors that play `Animations\Weapon\...` clips) get one finding per genuinely missing clip, each resolved to the exact engine search: the failing weapon chain prefix the engine looked under (in fallback order, from the master's AnimationSetData) and whether a generic `Animations\<clip>` fallback exists. Adding the generic copy clears the warning. The Chain tab shows these gaps directly under a "weapon clips" group when game data is attached, with the same message text `symrm` reports.
* Borrowed `..\` animation paths (for example into `PowerArmor`) resolve correctly against the archives.
* The Playback tab can now play clips that only exist inside a `.ba2` archive: with the Game Data folder set, selecting a clip whose animation is packed reads the animation (and, when the rig is packed too, the `CharacterAssets` skeleton) straight out of the archive, and the summary names the archive it came from.

</details>

## 1.0.4

<details>
<summary><strong>Saving &amp; Reliability</strong></summary>
<br>

* Supported behaviour and animation edits can now be saved through BGS's native pipeline.
* Saving creates a `.bak` copy of the original file.
* If an edit cannot be written safely, BGS refuses the save before touching the source file.
* Refused saves tell you which field could not be written and why.
* Fixed corruption when creating a `variableBounds` array from an empty list.
* Added graph validation and supported-project checks before saving.
* Closing a modified file now gives you Save, Discard, or Cancel.
* If a save is refused, the file stays open.

</details>

<details>
<summary><strong>Editing &amp; Authoring</strong></summary>
<br>

* Create, edit, and remove states, transitions, generators, variables, events, and bindings.
* Copy graph subtrees between files.
* Save reusable templates from your own work.
* Edit animation frames, trim clips, and change clip duration.
* Compare behaviour files to see what was added, removed, or changed.
* Browse behaviour files in tree or graph views.
* Inspect project chains, symbol usage, playback paths, and validation results.
* The Symbols tab now shows the actual minimum and maximum values of variable bounds.

</details>

<details>
<summary><strong>Playback &amp; File Support</strong></summary>
<br>

* Large behaviour files can now open without freezing the UI.
* Graph validation runs in the background instead of blocking the window.
* Files that are not Fallout 4 packfiles are rejected before opening, with an explanation.
* Preview animations using skeletons and supported skinned meshes.
* Playback can use a sibling `CharacterAssets` folder when the behaviour file does not provide its own project rig.
* Open vanilla files directly from Bethesda `.ba2` archives for read-only inspection.

</details>

## 1.0.3

August 8, 2026

<details>
<summary><strong>Changes</strong></summary>
<br>

* Added graph simulation for event-driven states, transition timing, and blend weights.
* Added subtree copy and paste, reusable templates, and path highlighting.
* Added animation trimming, retiming, and improved frame editing.
* Improved animation playback, root-motion display, and mesh previews.

</details>

## 1.0.2

August 7, 2026

<details>
<summary><strong>Changes</strong></summary>
<br>

* Added native animation saving for supported frame edits.
* Added a frame browser with bone filtering and direct frame navigation.
* Improved animation details, playback controls, and validation feedback.

</details>

## 1.0.1

August 6, 2026

<details>
<summary><strong>Changes</strong></summary>
<br>

* Added archive browsing so vanilla behaviour files can be inspected without manually extracting them first.
* Added skeleton, mesh, and root-motion views.
* Improved graph editing, symbol management, field editing, and save safeguards.

</details>

## 1.0.0

August 4, 2026

<details>
<summary><strong>Initial Release</strong></summary>
<br>

* Initial release of Behaviour Graph Studio.
* Added graph editing, filtering, selection, validation markers, undo, and file comparison.
* Added animation controls, variables, events, and behaviour authoring tools.

</details>

## Help &amp; Feedback

<details>
<summary><strong>Getting Started, Documentation &amp; Bug Reports</strong></summary>
<br>

New to BGS?

Read the [Getting Started Guide](https://prisma-user-interface-framework.github.io/Prisma2.0/tools/behaviourgraphstudio/guide/getting-started).

Found a bug?

Open a report through [GitHub Issues](https://github.com/G-A-R-D-E-N/BGS/issues). Please include enough information to reproduce the problem.

</details>
