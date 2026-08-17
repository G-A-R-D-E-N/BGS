# Behaviour Graph Studio Changelog

Changes that affect Behaviour Graph Studio users are documented here.

New to BGS? See the [Getting Started Guide](https://prisma-user-interface-framework.github.io/Prisma2.0/tools/behaviourgraphstudio/guide/getting-started).

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
* Modded data lookups are indexed once, so per-file checks stay O(1) no matter how many mod roots are layered on.
* Resolving a crash hash on the Chain tab now floats a panel over the graph canvas: it names the subgraph's behaviors, the animation presence, and every per-weapon gap, and each gap has a **Jump** button that selects and centres the missing clip's node on the graph.

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
* Weapon subgraphs (behaviors that play `Animations\Weapon\...` clips) get one finding per genuinely missing clip, each resolved to the exact engine search: the failing weapon chain prefix the engine looked under (in fallback order, from the master's AnimationSetData) and whether a generic `Animations\<clip>` fallback exists — a clip with no chain copy and no generic fallback is marked as a crash for that weapon type. Adding the generic copy clears the warning. The Chain tab shows these gaps directly under a "weapon clips" group when game data is attached, with the same message text `symrm` reports.
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
