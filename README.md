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
  Save writes back to `.hkx` and keeps the original as `.bak`. Before it overwrites anything it reads
  the file hkxpack just produced back out and counts it; if objects went missing on the way through,
  nothing is written and it says what was lost.
- **Variable bindings on the node**: a node bound to a graph variable says so, in the form
  `userControlledTimeFraction driven by fRadLevel`, with the variable resolved to its name.
- **Adding nodes**: select a node in the graph, type a name, and press one of the add buttons. The
  new clip, blender, modifier or selector is attached to whatever the selection can hold it as, and
  the toolbar says which slot that will be before you press it. With nothing selected the node is
  created unattached, and unattached nodes are drawn in a column of their own rather than vanishing.
  Delete refuses while anything still points at the node, and names what.
- **Symbols tab**: every variable and event with its index, type, initial value, and what references
  it. Add, rename, retype the value, or remove. Removing renumbers every reference above it. Expand an
  event to see what the file does with it: raised here, listened for here, or written somewhere with
  no established direction, each naming the class and member. No verdict comes with it, for the reason
  under Validating.
- **Chain tab**: project to character to behaviour, skeleton and animations, what is missing, and the
  skeleton's bone list.
- **Animation tab**: for an animation file, its class, duration, frame count, annotations, and a row
  per bone with each frame's position, rotation and scale, named from a sibling skeleton. Frames page
  in blocks of 300. Scale prints only on the tracks that carry one, because almost every track in the
  game is a flat 1,1,1 and printing all of them hides the ones that are not.
- **Check graph**: looks for the mistakes hkxpack cannot, listed under Validating below. With a real
  project folder around the file it also checks every clip's animation against the folder on disk.
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
| Create a node and attach it in one step | `GraphAuthor` | Picks the right slot for the parent's class, and lists nodes nothing points at |
| Variable values, add and rename variables and events | `SymbolEditor` | Renames preserve indices; values are 32 bit words |
| Remove a variable or an event | `SymbolEditor`, `SymbolIndexFixup` | Renumbers every reference above it; refuses while anything points at the exact index |
| Check a graph before repacking | `GraphValidator` | See Validating below |

Things the format makes easy to get wrong, all handled here:

- **A symbol lives in up to four arrays at once.** Names in `hkbBehaviorGraphStringData`, one info
  element per name in `hkbBehaviorGraphData`, one value per variable in `hkbVariableValueSet`, and
  sometimes a `variableBounds` element as well. Add a name without the others and the engine reads a
  variable with no declared type. `SymbolEditor.Audit` reports every length.
- **`variableBounds` is not reliably parallel.** It is empty in some files, the same length as the
  variable list in others, and in `MTBehavior` it is 19 entries against 67 variables that do not line
  up by position. Nothing here edits a partial bounds array, because a positional edit would be a
  guess.
- **Event ids hide under a member called `id`.** Every scalar named `*EventId` carries one, but so
  does the plain `id` member of an `hkbEventProperty` or `hkbEvent`, and that accounts for roughly a
  third of the event references in a typical graph. The field table in `SymbolIndexFixup` was read
  out of 132 vanilla files rather than recalled, and an index field it does not recognise makes a
  removal refuse rather than renumber around it.
- **Values are words, not text.** A float goes in as its bit pattern: `0.25` is stored as
  `1048576000`.
- **Renames must not reorder.** Transitions reference events by `eventId`, so a rename that shuffled
  the array would silently repoint every transition in the file.
- **A blender does not hold generators directly.** It holds `hkbBlenderGeneratorChild` wrappers that
  carry the weight. A raw generator reference in `children` passes hkxpack and gives the engine
  something it cannot read.
- **hkxpack reassigns object ids on repack.** Anything that remembers an id across a save is wrong;
  identify by class and name.

Removal was checked the hard way: on `MTBehavior`, dropping one variable moved 82 references and
dropping one event moved 251, and after a repack every binding and every transition still resolved to
the same name it did before.

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

Open that file in the Symbols tab to see it. Bindings can be created from the properties panel, and
the variable is declared for you if it does not exist yet.

## Doors, lifts and switches are driven by events, not variables

The Pip-Boy pattern above is the exception, not the rule, and it is worth knowing which one a job
needs before building against the wrong half of the format.

Every animated door, lift, periscope and switch checked declares **no variables at all**. They are
state machines driven entirely by named events, and Papyrus sends those events:
`ObjectReference.PlayAnimation(name)` is documented as "the name of the event to send to the object's
animation graph", and `PlayAnimationAndWait(name, endEvent)` waits for one coming back. 177 vanilla
base scripts drive animation this way.

The names line up exactly on both sides:

| behaviour file | events it declares | script that sends them |
|---|---|---|
| `SwitchDoors\SwitchDoorExLarge01` | `Play01 Trans01 Done Play02 StartOpen StartClosed Playing SoundPlay` | `DN151_DoorSeal.psc` sends `StartOpen`, `Open`, `StartClosed`, `Close` |
| `Vault\Doors\VltGearDoor` | `stage1 stage2 stage3 stage4 reset SoundPlay SoundPlayAt KlaxonStop GameStart` | `DN142_GearDoorConsoleScript.psc` sends `stage2`, `stage3`, `reset` |
| `GenericBehaviors\SpecialCaseDoors` | `Open Opened Close Closed reset SoundPlay SoundPlayAt AlternateClose AlternateClosed` | the garage door family |

`MuseumDoorAnim01` shows the whole shape in four states. It starts in `Closed`, whose generator is
`Open.hkt` in `MODE_USER_CONTROLLED` with nothing bound to it, so it holds frame zero: that is the
closed pose, not a fault. Event `Open` moves it to a `MODE_SINGLE_PLAY` of the same animation, whose
clip trigger fires `Done` and `Trans01` at the end of the clip, and `Trans01` carries it into a
looping `Opened`. `Close` runs the mirror of that back to `Closed`.

So an unbound `MODE_USER_CONTROLLED` clip in a graph with no variables is a held rest pose and is
normal. Check graph only mentions one when the graph does declare variables, where it might really
have meant to bind one.

## Using it

Download the release for your platform, unzip it, and run `BehaviourGraphStudio` (or
`BehaviourGraphStudio.exe`). **Nothing to install.** It is one file with the .NET runtime inside it,
and it does not need a game, a game engine, or an SDK. Keep the `tools/` folder next to it.

Opening and reading a file needs nothing else at all. **Saving additionally needs a Java runtime**,
because writing goes back through hkxpack. hkxpack itself ships in the release, in `tools/` beside
the binary; Java does not, so install one if you intend to save. Without it the tool opens the file
read only and says so in the status line rather than pretending.

There is a terminal mode for scripting and for proving a change without a display:

```
BehaviourGraphStudio --version
BehaviourGraphStudio --headless /path/to/Behavior.hkx    summary, node count, symbols, validator
```

It exits non zero when the validator finds errors, so it can gate a build.

## Building it yourself

```
dotnet run --project app/BehaviourStudio.csproj                    run from source
dotnet run --project tools/symrm/symrm.csproj -- test              the format checks
dotnet run --project tools/uismoke/uismoke.csproj                  build the window headlessly
dotnet publish app/BehaviourStudio.csproj -c Release -r linux-x64 -o out
dotnet publish app/BehaviourStudio.csproj -c Release -r win-x64 -o out
```

A .NET 8 SDK is the only requirement, on any platform. The Windows build cross compiles from Linux,
because a self contained publish emits the target's own host binary rather than reusing the build
machine's.

Nobody has to do any of that to get a release. `.gitlab-ci.yml` runs the format checks and the
window smoke test, publishes both platforms, drops hkxpack in beside each binary, then unzips the
Linux one on a bare Debian image with no .NET and no build tools to prove it actually starts.

## Requirements

- .NET 8 SDK to build. Nothing to build with, to run a release.
- **A Java runtime** for anything beyond structure. The tree and the graph come from the native C#
  reader and work without Java, but field-level editing and saving go through hkxpack. Without it the
  tool stays read-only and says so in the status line rather than pretending. hkxpack itself is
  bundled at `tools/hkxpack-cli.jar` (MIT, see `THIRD_PARTY_NOTICES.md`) and is found automatically
  next to the executable, so only Java has to be supplied.

## Layout

```
src/Hkx/     packfile readers and editors, self-contained, no project references out
app/         the application: Ux.cs palette, HkGrid.cs column grid, GraphView.cs node canvas,
             MainWindow.cs the window itself, Program.cs the terminal mode
tools/symrm/   format checks and the corpus harness
tools/uismoke/ builds the window on a headless display and walks it
tools/         sync_hkx_readers.sh, optional, re-pulls the readers from an OpenCommonwealth checkout
```

The window is Avalonia, drawing straight onto a canvas. Nothing about the tool depends on any game
engine, and it never did anything that needed one.

`src/Hkx` keeps the `OpenCommonwealth.Services.Hkx` namespace on purpose. The same readers exist in
that project, byte identical, so a fix on either side is a clean diff away from the other. That is a
convenience, not a dependency: there is no project reference, no shared path, and nothing here reads
from an OpenCommonwealth checkout unless you explicitly hand `tools/sync_hkx_readers.sh` a path to one.

## Validating

hkxpack checks shape and signatures. It does not check meaning, so it will write a file whose
transitions point at states that do not exist, or whose event ids run off the end of the event list.
Those load without complaint and then behave wrongly, which is the worst kind of fault to chase from
inside the game. **Check graph** looks for:

- references to objects that are not in the file
- event or variable indices past the end of what the graph declares
- the symbol arrays disagreeing with each other
- two states in one machine sharing a stateId, a transition to a stateId nothing has, a startStateId
  that does not exist
- a state with no generator
- a blender child that is not an `hkbBlenderGeneratorChild` wrapper
- a clip with no animation
- nodes nothing points at
- a state no transition in its machine can reach, which being referenced can never catch, because a
  machine always lists its own states

and, when the file sits in a real project folder rather than on its own:

- a clip whose animation is not on disk, which is what renaming or cloning a behaviour folder
  breaks and what nothing else here can see
- a clip playing an animation the character file does not declare

It reports no errors at all on 132 vanilla behaviour files, which is the bar a check has to clear
before it is worth reading. Passing it is not a promise the game will load the file.

Widening that to all 328 behaviours reachable through a project folder turns up 11 errors on 2 files,
all of them `hkbVariableBindingSet.variableIndex` pointing past the end of the variable list, in
`SharedCoreBehavior.hkx` and one `Behavior00.hkx`. That predates the checks described below and is
not explained yet: either those files really are wrong, or the symbol index check is missing a way a
graph can reach variables it does not declare itself. Worth knowing before trusting a clean run.

The two animation checks are warnings rather than errors because vanilla trips them: across all 215
project roots in `Fallout4 - Animations.ba2`, 328 behaviours produce 111 of them. They are real, not
false alarms. Shared behaviours reference per creature animations that not every creature has, and
some clips point at content that shipped in neither form, Dogmeat's `Animations\WalkForward_B.hkt`
among them. A handful is normal. A file suddenly full of them means the folder moved.

127 of those 215 characters declare no animations at all, which is also normal: an empty
`animationBundleNameData` is how a behaviour that plays no clips of its own is written.

The unreachable state check is a warning for the same reason, and the reason is worth writing down
because it is not what you would assume. A state is reachable if the machine starts in it, or some
transition targets it, following normal transitions from their own state and wildcards from
anywhere. Vanilla still trips it 123 times across 56 of the 328 behaviours. Every case checked is a
state the engine enters from outside the graph rather than through a transition: `RagdollAndGetUp`
21 times, the `SharedCore` wrapper state 18, `PairedState` 14, plus death variants and teleport
landings. None of them is reachable by any mechanism the file describes. `startStateIdSelector` is
null, `startStateId` is not variable bound, and `returnToPreviousStateEventId`,
`randomTransitionEventId`, `transitionToNextHigherStateEventId` and `transitionToNextLowerStateEventId`
are all -1, so Havok's own implicit transitions are not doing it either. The game simply sets those
states.

Events go one step further and are not a check at all. A transition listening for an event nothing
sends looks dead, and almost never is: Papyrus sends events by name through
`ObjectReference.PlayAnimation`, which 177 vanilla base scripts call, and the engine sends more
itself. Across the 314 behaviour files, 4799 events are used inside their own file and 2912 of those
are listened for with nothing in the file sending them. A check reporting that would be wrong three
times in five. So the Symbols tab reports it as information and says nothing about whether it is
right.

The roles it does report are not guessed either. Those 314 files write an event index in 43 distinct
class and member pairs, and all 43 are in the table, so the only thing that lands in "referenced" on
vanilla data is `BSLimbCycleModifier`, whose three event members do not say which way they run.
Anything the table has never seen reports the same way rather than being assigned a direction.
`symrm events <xmlDir>` reprints the whole measurement.

Two things follow. A machine with no transitions at all is skipped, because it is not transition
driven and saying nothing transitions to its states is true and useless; that alone takes the count
from 477 to 123. And a state named as a `toNestedStateId` target anywhere in the file is exempt,
because a transition in one machine can enter a nested machine's state directly. What is left is a
warning worth reading, not an error worth blocking on.

Run that yourself with `tools/symrm`, which pulls the corpus out of the game archive, unpacks it,
and checks it:

```
dotnet run --project tools/symrm/symrm.csproj -- corpus "<Data>/Fallout4 - Animations.ba2" /tmp/beh
dotnet run --project tools/symrm/symrm.csproj -- unpack /tmp/beh 4
dotnet run --project tools/symrm/symrm.csproj -- check  /tmp/beh/xml
dotnet run --project tools/symrm/symrm.csproj -- remove /tmp/beh/Meshes_Actors_Character_Behaviors_MTBehavior.hkx
```

The animation and repack checks need whole folders rather than loose files, so they have their own
commands. Point `anims` at one behaviour, or at a directory to sweep every project root beneath it:

```
dotnet run --project tools/symrm/symrm.csproj -- anims  <Data>/Meshes/Actors/Dogmeat/Behaviors/DogmeatDefault.hkx
dotnet run --project tools/symrm/symrm.csproj -- anims  <extracted Data folder>
dotnet run --project tools/symrm/symrm.csproj -- repack <Data>/Meshes/Actors/Dogmeat/Behaviors/DogmeatDefault.hkx
```

`corpus` writes 531 files. `unpack 4` takes every fourth, which is the 132 the numbers here come
from; pass 1 for all of them, and expect it to take a while, because it runs one JVM at a time
deliberately. `remove` is the round trip that proves a symbol removal renumbered everything it had
to: it exits non zero if any binding or transition comes back resolving to a different name.

## Known limits

- Reading is proven against all 531 vanilla behaviour files; 5292 of 5323 states resolve to a
  generator we understand and every transition resolves its event name. Numbers and method are in
  OpenCommonwealth's `docs/BEHAVIOR_GRAPH_RESEARCH.md`.
- Every edit here has been round tripped through hkxpack and read back from the binary. **None of it
  has been loaded by Fallout 4.** hkxpack accepting a file is not the engine accepting it. Keep the
  `.bak`.
- Deleting a node leaves whatever pointed at it holding null. Delete refuses while references exist,
  but detaching by hand first and then deleting can still leave, say, a state with no generator.
  Check graph finds that.
- A partial `variableBounds` array is never edited, only reported. See the note in `SymbolEditor`.
- Animation scale is decoded and shown, and for **spline compressed** animations it is checked against
  real data: 130 of the 13133 vanilla ones carry a scale that is not the identity, none contains a
  zero, and the static case was confirmed against the raw bytes rather than against the reader. The
  crow's `PerchedIdle` folds both wings to 0.4599 on all three axes, left and right identical, and
  those float32s sit at 0x714 and 0x794 in the file.
- **Scale on lossless compressed animations is UNPROVEN. Do not read a scale of 1,1,1 from one as
  confirmation that anything works.** No vanilla file exercises it: all 856 leave both scale arrays
  empty with every scale word clear, so only the "no scale here" case has ever run, and 1,1,1 is what
  that branch returns whether or not the code beside it is correct. The static and dynamic branches
  have never decoded a real value in any file that ships with the game. If you open a lossless
  animation that does scale something, treat what the tool prints as unverified and check it against
  the bytes yourself. If scale is ever wrong anywhere, this is where it will be wrong.
  `symrm scale <Data folder>` sweeps a folder and reports every animation whose scale is not the
  identity, which is how the numbers above were produced and how you would find a counterexample.

## Licence

MIT, in `LICENSE`. Software written by others and shipped with it is listed separately in
`THIRD_PARTY_NOTICES.md`, hkxpack among them.
