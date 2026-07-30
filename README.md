# Behaviour Graph Studio

A standalone viewer and editor for Fallout 4 Havok behaviour graphs (`hk_2014.1.0-r1` packfiles).
Split out of OpenCommonwealth's in-editor tool so it can be used without the game-conversion project.

Fallout 4 keeps its animation logic in behaviour files: which clip plays, when, how it blends, what
events fire. Havok never released the authoring tool for this format, and the editors that do exist
(Skyrim Behavior Editor, Haviour) target `hk_2010.2.0-r1`, which is Skyrim, and will not open a
Fallout 4 file. This reads the Fallout 4 format directly.

## What it does

- Opens any FO4 behaviour, character or project `.hkx` and shows the object graph.
- **Tree view**: nesting, Havok class per row, the animation each clip points at, file offset.
- **Graph view**: a node canvas laid out in columns by depth from the root, edges drawn from the real
  reference fields and labelled with the field that owns each link, so an edge says why it exists.
  Nodes are coloured by class family. Clip nodes show their animation path and any non-default
  playback speed inline.
- **Editable nodes**: the common fields (`mode`, `playbackSpeed`, `userControlledTimeFraction`,
  crop times, `startTime`, `enable`, `weight`, ids) are text boxes on the node itself. Type, tab out,
  and the change is staged. Every other field is editable from the properties panel in the tree view.
  Save writes back to `.hkx` and keeps the original as `.bak`.
- **Variable bindings on the node**: a node bound to a graph variable says so, in the form
  `userControlledTimeFraction driven by fRadLevel`, with the variable resolved to its name.
- **Variables tab**: every graph variable with its index, its initial value, and which node members it
  drives. This is the list you call from code.
- **Chain tab**: project to character to behaviour, skeleton and animations, what is missing, and the
  skeleton's bone list.
- Filter by name, class or animation.

## Structural editing

Beyond changing field values, the editing layer under `src/Hkx` can change the shape of a graph.
Every operation below was checked by repacking with hkxpack and reading the binary back, not by
hkxpack merely accepting the file.

| What | Where | Notes |
|---|---|---|
| Create and delete variable bindings | `BindingEditor` | Builds `hkbVariableBindingSet`, hooks the owner, unhooks it when the set empties |
| Add and remove states | `StateEditor` | Removing a state strips every transition pointing at its state id |
| Add and remove transitions | `StateEditor` | Normal transitions live on the source state, wildcards on the machine |
| Add and remove generators | `GeneratorEditor` | Clip, blender, modifier, manual selector. Deleting refuses while anything still references the object, and reports what |
| Variable values, add and rename variables and events | `SymbolEditor` | Renames preserve indices; values are 32 bit words |

Things the format makes easy to get wrong, all handled here:

- **A symbol lives in three arrays at once.** Names in `hkbBehaviorGraphStringData`, one info element
  per name in `hkbBehaviorGraphData`, one value per variable in `hkbVariableValueSet`. Add a name
  without the other two and the engine reads a variable with no declared type. `SymbolEditor.Audit`
  reports all three lengths.
- **Values are words, not text.** A float goes in as its bit pattern: `0.25` is stored as
  `1048576000`.
- **Renames must not reorder.** Transitions reference events by `eventId`, so a rename that shuffled
  the array would silently repoint every transition in the file.
- **A blender does not hold generators directly.** It holds `hkbBlenderGeneratorChild` wrappers that
  carry the weight. A raw generator reference in `children` passes hkxpack and gives the engine
  something it cannot read.
- **hkxpack reassigns object ids on repack.** Anything that remembers an id across a save is wrong;
  identify by class and name.

Not implemented: removing a variable or an event. Both would shift every index after them, and every
transition and binding referencing a later index would need rewriting in the same pass.

## Forcing an animation frame from a variable

The pattern for driving an animation by hand (a gauge needle, a watch hand, a dial) rather than
letting it play, taken from Fallout 4's own Pip-Boy graph:

```
hkbClipGenerator  mode = MODE_USER_CONTROLLED
                  variableBindingSet -> memberPath "userControlledTimeFraction"
                                        variableIndex <your float variable>
                                        bindingType BINDING_TYPE_VARIABLE
```

`userControlledTimeFraction` is 0 to 1 across the whole clip, so setting the variable to 0.25 puts the
clip at a quarter of its length and holds it there. The animation needs no special frame data and is
never "played", it is sampled.

Verified in `Meshes\Pipboy\Behaviors\PipboyBehavior.hkx`, which declares four variables
(`iTabSync`, `iCatSync`, `fRadioTune`, `fRadLevel`) and uses exactly this on two clips:

| clip | mode | bound member | variable |
|---|---|---|---|
| `RadMeterTurning` | `MODE_USER_CONTROLLED` | `userControlledTimeFraction` | `fRadLevel` |
| `TuneRadio` | `MODE_USER_CONTROLLED` | `userControlledTimeFraction` | `fRadioTune` |

Open that file in the Variables tab to see it. Creating a new binding is not yet possible in the tool,
only reading and retargeting an existing one; see the issue tracker.

## Running

```
./run.sh                                  open empty
./run.sh /path/to/Behavior00.hkx          open a file on start
./run.sh --headless file.hkx --quit-after 90    parse and print the summary, no window
```

`run.sh` uses `engine/godot.linuxbsd.editor.double.x86_64.mono`, the copy that lives inside this
project. Nothing here reaches outside its own folder at runtime. Set `BGS_GODOT` to override.

The engine must be a **double-precision mono** build, because the assembly is compiled against the
`4.7.1-double` packages vendored in `nuget/`, and a stock single-precision editor will refuse to load
it. `engine/` is 239MB of build output so it is gitignored, not committed. On a fresh clone, drop such
a build in `engine/` (binary plus its `GodotSharp/` folder alongside it) or point `BGS_GODOT` at one.

## Requirements

- A Godot 4.7.1 double-precision mono build, in `engine/` or via `BGS_GODOT`.
- .NET 8 SDK to build (`dotnet build BehaviourGraphStudio.csproj`).
- **A Java runtime and `hkxpack-cli.jar`** for anything beyond structure. The tree and the graph come
  from the native C# reader and work without Java, but field-level editing and saving go through
  hkxpack. Without it the tool stays read-only and says so in the status line rather than pretending.

## Layout

```
src/Hkx/     packfile readers, self-contained, no project references out
src/Ui/      Ux.cs design system, GraphCanvas.cs node canvas, StudioRoot.cs the app itself
tools/       sync_hkx_readers.sh, optional, re-pulls the readers from an OpenCommonwealth checkout
nuget/       vendored double-precision Godot packages
engine/      the Godot binary this tool runs on (gitignored)
```

`src/Hkx` keeps the `OpenCommonwealth.Services.Hkx` namespace on purpose. The same readers exist in
that project, byte identical, so a fix on either side is a clean diff away from the other. That is a
convenience, not a dependency: there is no project reference, no shared path, and nothing here reads
from an OpenCommonwealth checkout unless you explicitly hand `tools/sync_hkx_readers.sh` a path to one.

## Known limits

- Structural editing (adding or removing states and transitions) is not implemented. Field edits only.
- Reading is proven against all 531 vanilla behaviour files; 5292 of 5323 states resolve to a
  generator we understand and every transition resolves its event name. Numbers and method are in
  OpenCommonwealth's `docs/BEHAVIOR_GRAPH_RESEARCH.md`.
- hkxpack will happily repack a graph the game then rejects. Nothing here validates that a saved
  file still loads. Keep the `.bak`.
