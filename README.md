# Behaviour Graph Studio

A standalone viewer and editor for Fallout 4 Havok behaviour graphs (`hk_2014.1.0-r1` packfiles).
Split out of OpenCommonwealth's in-editor tool so it can be used without the game-conversion project.

Fallout 4 keeps its animation logic in behaviour files: which clip plays, when, how it blends, what
events fire. Havok never released the authoring tool for this format, and the editors that do exist
(Skyrim Behavior Editor, Haviour) target `hk_2010.2.0-r1`, which is Skyrim, and will not open a
Fallout 4 file. This reads the Fallout 4 format directly.

This README covers what the tool is and how to use it. What we have worked out about the format and
the game, with the evidence behind it, lives in the
[wiki](https://git.nomadicinteractive.dev/nomadic-interactive/behaviortoolstandalone/-/wikis/home).

The supported application path is entirely native C#. It does not require Java, a JAR, or hkxpack.
The implementation timeline later in this document is historical context only.

## What it does

- Opens any FO4 behaviour, character or project `.hkx` and shows the object graph.
- **Straight out of a `.ba2`**: "From archive..." reads a Bethesda archive's index and lists what is
  in it, so a vanilla behaviour can be opened without unpacking the archive around it. Every
  behaviour in the game is inside `Fallout4 - Animations.ba2`, which holds 29,716 entries; reading
  the index takes about a second and touches none of the file data. Type words in any order to
  narrow the list, since the useful query is "dogmeat behavior" and the archive stores that as
  `meshes/actors/dogmeat/behaviors/...`. A file opened this way is **read only**: it is a copy in a
  temporary folder, Save is greyed out, and the window says where the copy went so it can be put
  somewhere of your own if you want to edit it.
- **Tree view**: nesting, Havok class per row, the animation each clip points at, file offset.
- **Graph view**: a node canvas laid out in columns by depth from the root, edges drawn from the real
  reference fields and labelled with the field that owns each link, so an edge says why it exists.
  Nodes are coloured by class family. Clip nodes show their animation path and any non-default
  playback speed inline.
- **Editable nodes**: click a node and every field it has appears in the properties panel beside the
  canvas, one text box each: `animationName`, `mode`, `playbackSpeed`, `userControlledTimeFraction`,
  the crop times, `startTime`, `flags`, `weight`, ids, all of them. Double click a node to jump
  straight into the first box. Type, tab out, and the change is staged. A field the file leaves empty,
  such as `animationBundleName`, is offered as an empty box rather than hidden, so it can be given a
  value. The same panel sits beside the tree view.
  Save writes back to `.hkx` and keeps the original as `.bak`. A changed value goes straight into the
  file's own bytes; a changed name goes on the end of the file and its pointer is aimed at it, which
  is how a longer name is written without moving anything already there. An array at a new length is
  written the same way, whether it holds pointers or structs: a run of the new length goes on the end
  and the array is aimed at it, so nothing already in the file moves. Only an edit that changes the
  number of objects, or the length of an array of text, is not written until the native writer can
  express it safely. In that case the original file is left untouched and the window explains why.
- **Highlight one state's paths**: right click a node and pick "Highlight the paths of ...". Every
  wire that does not touch that node drops to half opacity and every unrelated node dims, so a single
  state's routes are readable in a graph that draws a few hundred wires over each other. Escape, or
  right click again, clears it.
- **Variable bindings on the node**: a node bound to a graph variable says so, in the form
  `userControlledTimeFraction driven by fRadLevel`, with the variable resolved to its name.
- **Adding nodes**: select a node in the graph, type a name, and press one of the add buttons. The
  new clip, blender, modifier or selector is attached to whatever the selection can hold it as, and
  the toolbar says which slot that will be before you press it. With nothing selected the node is
  created unattached, and unattached nodes are drawn in a column of their own rather than vanishing.
  Delete refuses while anything still points at the node, and names what.
- **Templates**: "Save as template" keeps the selected node and everything it owns, so the same shape
  can be put into another file later. Pick one from the box and the line beside it says whether it
  fits the file that is open before you apply anything, because a template carries the event and
  variable names of the file it came from and is not self contained. A shape that shares an object
  with the rest of its file is refused when you try to keep it rather than when you try to use it,
  since it could never go into a different file. Templates are yours: they are lifted from your own
  files into your own application data, and none ship with the tool.
- **Symbols tab**: every variable and event with its index, type, initial value, and what references
  it. Add, rename, retype the value, bound it, or remove. Declaring one uses the native writer: the
  array of names is written by appending a longer run, proved by declaring an event in each of the
  328 vanilla behaviours that have somewhere to put one and reading every name back from the bytes. **Set bounds** gives a variable a min and a
  max, extending `variableBounds` to reach it when the array stops short, which it usually does: of
  the 531 vanilla files it is empty in 224 and shorter than the variable list in 87. The entries
  written in between are `0` to `0`, which is what the file already means by an unbounded variable
  inside the array. Both go straight into the file's own bytes, whether the bound is one the array
  already holds or one it has to be lengthened to reach.
  Removing renumbers every reference above it. Expand an
  event to see what the file does with it: raised here, listened for here, or written somewhere with
  no established direction, each naming the class and member. No verdict comes with it, for the reason
  under Validating.
- **Chain tab**: project to character to behaviour, skeleton and animations, what is missing, and the
  skeleton's bone list.
- **Animation tab**: for an animation file, its class, duration, frame count, annotations, and a row
  per bone with each frame's position, rotation and scale, named from a sibling skeleton. Frames page
  in blocks of 300. Filter to a bone by name, because a character animation has 95 tracks and reading
  one bone's motion means seeing only that bone. Type a `userControlledTimeFraction` and it says which
  frame that is, jumps the page to it and marks the row, which is the question a variable driven clip
  asks. Scale prints only on the tracks that carry one, because almost every track in the game is a
  flat 1,1,1 and printing all of them hides the ones that are not. A clip's frames can also be
  **written back**, which is what `NativeAnimation` does: the clip is decoded, whatever changed in it
  is kept, and it is written out as `hkaInterleavedUncompressedAnimation`, every frame of every track
  stored as it is. Nothing here re-encodes a compressed animation, so this is the way out of "reads a
  clip and cannot change one". The file gets much larger, which is the honest cost of not having an
  encoder, and the clip is exact. Fallout 4 registers the class at startup, so the engine has the code
  to read one; it has not been loaded in game, which is #19's question and not this one's.
- **Check graph**: looks for structural mistakes, listed under Validating below. With a real
  project folder around the file it also checks every clip's animation against the folder on disk.
  Findings do not just print: the node each one is about is **outlined red for an error and amber for
  a warning** on the canvas, and the problem list under it names them all. Click a row and the canvas
  centres on that node and selects it, which matters because the node that is wrong is usually the
  one off screen. Marks survive edits until the next check, so fixing one thing does not clear the
  rest of the list.
- **Seeing it without the window**: `symrm meshpng <mesh.nif> <skeleton.hkx> <out.png> [bone...]`
  draws the posed mesh to a picture, front and side, with any bones named on the command line drawn
  in their own colour. That exists because "does this look right" kept being a question only a person
  with the program open could answer, and it is not: the answer is in the data. It is what showed the
  male body drawing as an unrecognisable column before the placement fix, and what showed both toes
  sitting correctly on their own feet after it.
- **The character, not just the bones**: point Playback at a `.nif`, with the Mesh button or by naming
  it on the command line beside the `.hkx`, and the mesh is skinned to the skeleton and posed with the
  clip. Wireframe, drawn on the same 2D surface as the rest of the window, so the tool takes on no
  new dependency. Bones are matched to the skeleton by name; any that do not match are named rather
  than dropped, and vertices weighted only to those stay at their rest position. Nothing names a mesh
  from inside a behaviour, a character or a skeleton, so it has to be pointed at one; the race record
  lookup that would find it automatically is a later job.
- **Where a clip takes you**: motion is extracted in this format, so a walk plays on the spot and
  carries its displacement in a separate track that never reaches a bone. Measured: a Dogmeat walk
  that travels 1,060 units moves its root bone 0.000 and its centre of mass 0.312. That makes travel
  invisible in a viewport, so Playback says it in words on every clip, as `travels 187 units` or
  `stays on the spot`, and **Follow travel** puts the two back together and walks the character along
  its own path. Of 619 vanilla walk animations, 608 carry motion and 11 stay put; a clip named
  `TurnLeft90` reads back as exactly 90 degrees.
- **Playback tab**: select a clip generator and the animation it names is drawn on its own skeleton,
  as lines between joints, with play, pause, step and a scrub bar. The rig comes off the project
  chain, because a behaviour file names no skeleton and the character does. Drag to orbit, right
  drag to pan, wheel to zoom, hover a joint for its bone name, and tick Reference pose to draw the
  rest pose behind the animated one. Opening an animation file on its own plays it directly. Nothing
  here writes to the file: scrubbing is a view, so it takes no undo step and cannot arm Save. An
  animation authored against a different rig says which bone it wanted and shows the rest pose rather
  than drawing a wrong one.
- **Undo and redo**: Ctrl+Z and Ctrl+Y, or the buttons beside Save, back to a hundred steps. Every
  editing path goes through one place, so nothing can change the document behind the stack's back.
  Creating a node and wiring it up is one step, not two. The unsaved marker is measured against what
  was last written rather than latched on, so undoing back past a save says the file matches disk.
- **Where a symbol is used**: expand a variable or an event in the Symbols tab and every place the
  file names it is listed, each one naming the object it sits in. Click a row and the canvas centres
  on that node, the same jump the check results use. The other direction is on the node: a selected
  node's panel lists the symbols it reads, writes or fires, resolved to their declared names.
- **Which scripts send an event**: point "Scripts folder..." at a folder of Papyrus `.psc` sources and
  each event says which scripts name it. Reported as information, never as a verdict, because the
  engine sends plenty of events itself; a name no script sends is not evidence of anything. Silent
  when no folder is set.
- **Compare tab**: pick another copy of the open behaviour and read what differs, which is how a mod
  conflict gets answered without unpacking both by hand. Added objects, removed objects, and changed
  values with both sides shown. Ids are meaningless across files, so matching is on class and
  contents, and hkxpack's renumbering reads as no difference at all.
- **Check project**: the same checks run over every behaviour in the project, reported grouped by
  file. Most real problems only exist between files: a clip that plays an animation no file in the
  chain provides reads as fine one file at a time.
- **Filter by name, class or animation**, on whichever view is showing. The tree narrows to the
  matches; on the canvas the matches stay lit, everything else dims, and a wire touching a match stays
  lit so you can see where it connects. Enter goes to the first match.

## Structural editing

Beyond changing field values, the editing layer under `src/Hkx` can change the shape of a graph.
Every operation below is checked by writing native bytes and reading them back through the native
reader.

| What | Where | Notes |
|---|---|---|
| Create and delete variable bindings | `BindingEditor` | Builds `hkbVariableBindingSet`, hooks the owner, unhooks it when the set empties |
| Add and remove states | `StateEditor` | Removing a state strips every transition pointing at its state id |
| Add and remove transitions | `StateEditor` | Normal transitions live on the source state, wildcards on the machine |
| Add and remove generators | `GeneratorEditor` | Clip, blender, modifier, manual selector. Deleting refuses while anything still references the object, and reports what |
| Create a node and attach it in one step | `GraphAuthor` | Picks the right slot for the parent's class, and lists nodes nothing points at |
| Variable values, add and rename variables and events | `SymbolEditor` | Renames preserve indices; values are 32 bit words |
| Remove a variable or an event | `SymbolEditor`, `SymbolIndexFixup` | Renumbers every reference above it; refuses while anything points at the exact index |
| Read a transition's condition | `Expression`, `GraphRun` | Parses the expression language the corpus uses and answers True, False or Unknown. Unknown always means the transition still fires |
| Copy and paste a subtree | `NativePaste` | Takes what the root owns, gives every copied object a fresh id and aims every reference inside the copy at the copies. Works between two files, and refuses naming what is missing |
| Check a graph before saving | `GraphValidator` | See Validating below |

Things the format makes easy to get wrong, all handled here:

- **A symbol lives in up to four arrays at once.** Names in `hkbBehaviorGraphStringData`, one info
  element per name in `hkbBehaviorGraphData`, one value per variable in `hkbVariableValueSet`, and
  sometimes a `variableBounds` element as well. Add a name without the others and the engine reads a
  variable with no declared type. `SymbolEditor.Audit` reports every length.
- **`variableBounds` is positional but often stops short.** Across the 531 vanilla files it is empty
  in 224, the same length as the variable list in 17, and shorter in 87, at its most extreme 19
  entries against `MTBehavior`'s 67 variables. Short does not mean differently keyed: `hkbVariableBounds`
  is 8 bytes holding `min` and `max` and nothing else, read out of the class the engine registers for
  it, so the struct has no field that could name a variable and position is the only key there can
  be. A short array means the variables past its end have no bound, and an unbounded variable inside
  it is written `0..0`. Removing a variable inside the array takes its bound with it; removing one
  past the end leaves the array alone. Lengthening it to reach a variable is written into the file's
  own bytes: `symrm grow` does it on every vanilla behaviour that has a variable the bounds do not
  reach, **180 files, every one written, hkxpack agreeing about every value in the file afterwards
  and nothing moved that was not asked to move**, and the same 180 with Java hidden.
- **Event ids hide under a member called `id`.** Every scalar named `*EventId` carries one, but so
  does the plain `id` member of an `hkbEventProperty` or `hkbEvent`, and that accounts for roughly a
  third of the event references in a typical graph. The field table in `SymbolIndexFixup` is checked
  against corpus coverage, and an index field it does not recognise makes a
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

A clip can be sampled by a graph variable rather than played, which is how a gauge needle or a dial is
driven. Set `mode = MODE_USER_CONTROLLED` and bind `userControlledTimeFraction` to a float variable;
0.25 puts the clip a quarter of the way in and holds it there.

Bindings can be created from the properties panel, and the variable is declared for you if it does not
exist yet.

## Doors, lifts and switches are driven by events, not variables

The user-controlled clip pattern above is the exception, not the rule, and it is worth knowing which
one a job needs before building against the wrong half of the format.

Every animated door, lift, periscope and switch checked declares **no variables at all**. They are
state machines driven entirely by named events, and Papyrus sends those events:
`ObjectReference.PlayAnimation(name)` is documented as "the name of the event to send to the object's
animation graph", and `PlayAnimationAndWait(name, endEvent)` waits for one coming back. Many base
scripts drive animation this way.

The names line up exactly on both sides:

| behaviour shape | events it declares | script pattern that sends them |
|---|---|---|
| switch door style graph | `Play01 Trans01 Done Play02 StartOpen StartClosed Playing SoundPlay` | script sends open, close, and finished events |
| staged gear door style graph | `stage1 stage2 stage3 stage4 reset SoundPlay SoundPlayAt KlaxonStop GameStart` | script sends stage and reset events |
| shared special-case door graph | `Open Opened Close Closed reset SoundPlay SoundPlayAt AlternateClose AlternateClosed` | door scripts send the standard route events |

The small animated-door shape has four states. It starts in `Closed`, whose generator is
user-controlled with nothing bound to it, so it holds frame zero: that is the closed pose, not a
fault. Event `Open` moves it to a single-play version of the same animation, whose clip trigger fires
the completion events at the end of the clip, and the completion route carries it into a
looping `Opened`. `Close` runs the mirror of that back to `Closed`.

So an unbound `MODE_USER_CONTROLLED` clip in a graph with no variables is a held rest pose and is
normal. Check graph only mentions one when the graph does declare variables, where it might really
have meant to bind one.

## Using it

Download the release for your platform, unzip it, and run `BehaviourGraphStudio` (or
`BehaviourGraphStudio.exe`). **Nothing to install.** It is one file with the .NET runtime inside it,
and it does not need a game, a game engine, or an SDK. Keep the `tools/` folder next to it.

Opening, reading, editing, comparing, validation, and supported saves use the native C# pipeline.
An edit the native writer cannot safely express is refused before the original file changes.

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
window smoke test, publishes both platforms, then unzips the Linux one on a bare Debian image with
no .NET and no build tools to prove it actually starts.

## Requirements

- .NET 8 SDK to build. Nothing to build with, to run a release.
- **Optional: a folder of Papyrus `.psc` sources**, only for showing which scripts send each animation
  event. Nothing else needs it and nothing changes when it is not set.

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
- a state with no generator, which crashes the game while the graph loads
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
dotnet run --project tools/symrm/symrm.csproj -- remove /tmp/beh/Meshes_Actors_Character_Behaviors_Behavior.hkx
```

The animation and repack checks need whole folders rather than loose files, so they have their own
commands. Point `anims` at one behaviour, or at a directory to sweep every project root beneath it:

```
dotnet run --project tools/symrm/symrm.csproj -- anims  <Data>/Meshes/Actors/Character/Behaviors/Behavior.hkx
dotnet run --project tools/symrm/symrm.csproj -- anims  <extracted Data folder>
dotnet run --project tools/symrm/symrm.csproj -- repack <Data>/Meshes/Actors/Character/Behaviors/Behavior.hkx
```

`cliptime` needs the same, and for the same reason: how long a clip plays for is in the animation file
the project points at rather than in the behaviour, so it needs the folders around the file rather
than a pile of loose ones. Extract with `--tree` and point it at the root:

```
dotnet run --project tools/symrm/symrm.csproj -- extract "<Data>/Fallout4 - Animations.ba2" "" /tmp/tree .hkx --tree
dotnet run --project tools/symrm/symrm.csproj -- cliptime /tmp/tree
```

`corpus` writes 531 files. `unpack 4` takes every fourth, which is the 132 the numbers here come
from; pass 1 for all of them, and expect it to take a while, because it runs one JVM at a time
deliberately. `remove` is the round trip that proves a symbol removal renumbered everything it had
to: it exits non zero if any binding or transition comes back resolving to a different name.

## Known limits

- The Playback viewport draws a wireframe, not a shaded character. What it is for is seeing which
  animation a clip actually names and roughly what that animation does, not judging how it looks in
  game.
- A mesh only follows the skeleton for the bones the two agree on by name. Creature meshes match
  cleanly: Dogmeat's 118 bone references and the Mirelurk's 95 across eight shapes all found a
  skeleton bone. The human body mesh does not, and this is a real limit rather than a fault: it
  weights 45 of its 58 bones to skin helper bones, `Chest_skin` and the like, which live in the mesh
  skeleton and not in the Havok one, so those vertices cannot be animated. `symrm mesh` reports the
  shortfall by name. They are still drawn in the right place: a mesh does not have to be authored at
  the origin, and the male body is authored with its origin at the neck, so its vertices run from
  -120 to -6 and the bind lifts the whole thing onto the ground.
- A mesh's placement is not a fault, and reading it as one is what made the male body report 120 units
  of drift for a while. What has to hold is that the bones agree with each other on the skeleton's own
  reference pose, since the mesh is rigid in the space it was authored in. They do: 12 of the male
  body's 13 matched bones agree to within a fifth of a unit, and Dogmeat's 21 agree to within a
  thousandth. The thirteenth is `LLeg_Toe1`, 5.140 units from where the rest agree, while its right
  hand twin is 0.172, so the mesh and the skeleton disagree about that one toe. Reading the stored
  rotation any of the three wrong ways still fails the check, at 97 percent of bones disagreeing and
  166 units.
- The pose itself is checked against known numbers and against real game files: a three bone rig with
  hand-worked positions in `symrm test`, and the vanilla 95 bone character skeleton posed through
  `symrm pose`, where the composed frame puts the pelvis at z 65, the head at z 101 and the feet near
  the floor. What **cannot** be checked without eyes on a window is whether the projection reads
  correctly: orbit, pan and zoom feel, whether the ground grid helps or clutters, and whether the
  joint hover radius is comfortable. Those are the parts to look at first.
- An animation whose class the reader cannot decode has no frames, so it cannot be drawn. That is the
  same list the Animation tab reports and it is not specific to playback.
- **The properties panel reads the file, and nothing falls back any more.** The byte reader handles
  every field type in a behaviour, including a struct written inline, whose class the class dump does
  not name and the class table does: references between objects, arrays of all kinds, enums and flags
  by name, strings, and every width of number. Measured against hkxpack over all 533 vanilla
  behaviours: `symrm crosscheck` compares the reader, **274,107 values and all of them agreeing**;
  `symrm panel` compares what the panel itself would display, **509,557 values, every one of them read
  from the file's own bytes with none falling back**, all agreeing. An enum field offers its declared
  values rather than asking for the name to be typed, which covers 42,733 of those fields.
- **Hovering a field's name says what it is.** The address the edit will be written to,
  `transitions[24].flags`, then what the field is, taken from the class table and so true by
  construction: what a pointer points at, what an array holds, how many values an enum declares,
  which class in the chain declares it. That covers **485,793 of 485,793 fields** across the vanilla
  corpus.
  A sentence about what a field *means* is a different thing and is only there for the fields this
  project has established, each carrying where it came from. That is **7.7%** of them, and the number
  is printed by `symrm notes` rather than left as an impression. The rest say nothing: a plausible
  sentence written from a field's name would read with exactly the authority of a measured one, and
  nobody could tell them apart. No installed reference establishes the rest of those meanings.
- **Historical validation before retirement.** The graph, the tree, the properties,
  the symbols, what each event is used for, and the whole checker are read from the file's own bytes,
  and the text form an edit is made through is written from those bytes as well rather than unpacked.
  That text was set against hkxpack's own line by line over every vanilla behaviour: **of the 370
  files hkxpack reads correctly, all 370 come out identical, 385,773 lines of them**. The other 128
  hold a class hkxpack strides wrongly, so its text is misaligned and there is nothing to match.
  The Chain tab and Check project were the last two that still asked for it, because they read the
  *other* files in a project and were still unpacking those. They read them the same way now.
  `symrm chain` runs both halves and prints them, and its output is identical with Java on PATH and
  with Java and hkxpack both hidden: 4 links, 100 animations, 65 bones, 4 behaviours checked, none
  unread. The window's own smoke test now runs 129 checks either way, where without Java it used to
  run 120.
  Before retirement, edits outside the native writer's supported set used the former external path.
  The current application refuses unsupported edits without modifying the original. Supported edits
  go into the bytes: values, wide values, pointers,
  arrays of children, arrays of struct elements, arrays of names, arrays of numbers, strings, adding
  an object and deleting one. See #32 and #34.
- **The wide fixed width fields are written where they sit.** A `vector4` is sixteen bytes wherever
  it is and a `qstransform` forty eight, so writing one over another moves nothing, and they were
  refused only because nothing read the spelling back. The spelling is the one the panel already
  shows, floats four to a bracket, `(1.5 -2.25 3.75 0.5)`. Proved by changing a vector in each of the
  243 vanilla behaviours that carry one and reading it back, with the file exactly as long as it was.
  A value of the wrong length is refused rather than part written.
- **An array of numbers grows the same way**, one run appended and the array aimed at it, proved on
  the 56 vanilla behaviours that carry one. That run turned up a reading fault worth naming: a field
  narrower than four bytes was read as four bytes and masked down, which is right everywhere except
  the last bytes of a section. Nothing in a vanilla file sits there, so it never showed until an
  appended run ended flush with the section and its final element read as blank while the count
  beside it said otherwise. Narrow fields are read at their own width now.
- **The class table is not just self consistent, it agrees with the game.** Every offset written into
  a file comes from `HavokClassTypes.json`, which was built from hkxpack's class data. Fallout 4's
  own startup initializers carry the same information, read out of the binary rather than out of any
  tool, and the historical class check set the two against each other: **900 classes in both, every size
  agreeing, 7,062 of 7,080 members at the same offset and none disagreeing.** The 18 unmatched
  members and the 8 classes only this build carries are physics and container templates that appear
  in no vanilla behaviour file, checked rather than assumed.
- Reading is measured, not assumed, over the whole game rather than a subset. All 531 behaviour files
  in `Fallout4 - Animations.ba2`, all 5329 states: every one resolves to a generator that exists in
  its own file, across 15 generator classes, and every transition resolves its event name. Nothing is
  unresolved. Re-run it with `symrm corpus`, `symrm unpack <dir> 1` and `symrm states`, which walks
  with the tool's own model rather than a script, so the number is about this reader.
  An older figure of "5292 of 5323 states resolve to a generator we understand" circulated here and
  elsewhere. It is real, it is not about this tool, and the shortfall is not a reading failure: it
  counted how many states OpenCommonwealth's Godot converter could map to an animation node, and the
  34 it could not are all `BSBehaviorGraphSwapGenerator` with a null `pDefaultGenerator`, a count that
  reproduces here exactly. This reader parses all 34 of them. See #18.
- **One edit made with this tool has been loaded by Fallout 4 and worked**: a door graph held
  permanently half open exactly as the edit asked, with no interaction needed. The edit was three
  scalar values on one existing sequence generator. The file kept the same object count, state count,
  event count, and byte size. So what the engine has accepted is a **field value edit on an existing object**, written
  by this tool before the external packer was retired. That is the first time anything here was proven against
  the engine rather than against hkxpack, and it moves the tool from "the file reads back correctly"
  to "the game accepted at least one of these".
  It says nothing about structural editing. Rewiring a pointer, resizing an array of children,
  appending an object, attaching one and orphaning one are all written into the file's own bytes now,
  and all of them were historically checked against the former packer on real vanilla files. **None of them has been put in
  front of the game.** Neither has renumbering a symbol, and `symrm door`'s additive edit in
  particular has not. Everything else has been round tripped through hkxpack and read back from the
  binary and no further, and hkxpack accepting a file is still not the engine accepting it. Keep the
  `.bak`.
- **Deleting a node now takes it out of the file.** It used to be orphaned instead, its pointers
  cleared and its bytes left where they were, because taking an object out moves every object after
  it and nothing knew where they would land. Laying the data section out from nothing settles that,
  so a deletion drops the object's entry, its bytes, and everything it alone pointed at, and the rest
  of the file is placed again around the hole. Across all 531 vanilla behaviours an object is taken
  out and the result reads back with that object gone, every byte accounted for and no pointer aiming
  into the hole; 439 of them go the whole way through the window's own save path, with no Java
  anywhere in it. hkxpack agrees, reading Dogmeat's result as 1518 objects against 1519 and one fewer
  `hkbBlendingTransitionEffect`.
  An element of a pointer array is still dropped and the array shrunk rather than set to null,
  because a null child is a crash on load rather than an empty slot. A pointer inside an element of
  an array of structs, which is where a transition keeps its effect, is cleared to null instead:
  dropping the element would delete a route between two states rather than the effect on it.
  **What this does not settle is renumbering.** Every id above the hole shifts, and no check here can
  say what Fallout 4 makes of that. Orphaning is still there for anyone who would rather not move
  anything. See #19 and #34.
- Clearing the last pointer into a node can still leave, say, a state with no generator.
  **Fallout 4 crashes while loading any graph that contains one**, before a state is entered, so
  reachability does not save it. Runtime testing showed the graph walk can dereference a null child
  during load. The tree and the graph mark such a state, Check graph reports it as an error, and
  Save refuses to write the file at all. The refusal names the states and the machines they sit in,
  and says both ways out, because being stopped without being told which state or what to do about it
  is worse than not checking. Give each one a generator, or delete the state. See #16.
- A short `variableBounds` array is kept lined up rather than left alone: see the note in
  `SymbolEditor`. What is still not done is authoring a bound, since nothing in the window sets one.
- Animation scale is decoded and shown, and for **spline compressed** animations it is checked against
  real data: 130 of the 13133 vanilla ones carry a scale that is not the identity, none contains a
  zero, and the static case was confirmed against the raw bytes rather than against the reader. The
  crow's `PerchedIdle` folds both wings to 0.4599 on all three axes, left and right identical, and
  those float32s sit at 0x714 and 0x794 in the file.
- **Scale on lossless compressed animations is confirmed against the engine, but has still never
  decoded a real value.** No vanilla file exercises it: all 856 leave both scale arrays empty with
  every scale word clear, so only the "no scale here" case has ever run on real data. Rather than
  leave it at that, the branch was checked against `hkaLosslessCompressedAnimation::getFrameTransform`
  in the 1.10.163 unpacked binary, and it agrees on every point: the word array at +0xb8, statics at
  +0xa8, dynamics at +0x98, stride as the dynamic array's length divided by the frame count at +0xd8,
  `(offset << 2) | type` packed four to a 64 bit word, and frame major indexing as
  `offset + frame * stride`. The clear case returns 1,1,1 because the engine prefills the transform
  with scale 1,1,1,1 before it reads anything, from the constant at 0x143828480. So the decode is not
  guesswork, but the words it decodes have only ever said "nothing here". `symrm scale <Data folder>`
  reports every animation whose scale is not the identity, which is how you would find the first file
  that proves it end to end.

## Licence

MIT, in `LICENSE`. Software written by others and shipped with it is listed separately in
`THIRD_PARTY_NOTICES.md`, hkxpack among them.
