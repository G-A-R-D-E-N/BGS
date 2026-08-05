# Changelog

Notable changes, newest first. Read the commit messages for the detail; this is the shape of the
work rather than a list of every edit.

## 2026-08-04, review pass before the beta

Four things a read of the session's own changes turned up, all of them things the canvas remembered
when it should not have:

**Opening a second file kept the first one's positions.** Everything the canvas holds is keyed by
object id, and the next file numbers its objects from one as well, so a node dragged in one file
pinned whichever node happened to hold that number in the next. The highlight, the filter and the
marks had the same problem. A load clears all of it now.

**A file with no text form left the last graph on screen.** The canvas is only refilled once a file
has been unpacked, so opening something that cannot be unpacked showed the previous file's nodes
under the new file's name.

**The tree marked no empty states on load.** It was built before the file was unpacked, so the
answer was always "none", and it was only ever right after something else forced a rebuild. It is
built again once the text form exists.

**Typing in the filter reparsed the file.** Working out which states are empty is a question about
the file, not about the filter, and it was being answered on every keystroke: seven megabytes,
six times, to type "Sprint". Six keystrokes now cost 444ms on the weapon behaviour rather than well
over a second. The properties panel also stopped being wiped by typing, which is the last thing you
want when the reason you are searching is the node whose fields are open.

## 2026-08-04, the search box works on the canvas, and the weapon graph is usable

**The filter box only ever drove the tree.** It sits above the tabs, so on the Graph tab typing in it
did nothing at all. It now filters whichever view is showing: matching nodes stay lit, everything else
dims, and a wire touching a match stays lit because where a match connects is the question being
asked. Nodes dim rather than disappear, since a node's place in the graph is most of what it tells
you. Enter moves the view onto the first match and selects it; typing alone does not yank the view
around.

**The canvas drew 400 nodes.** `WeaponBehavior.hkx` lays out 3978, so nine tenths of it was never
drawn and the search could not find a node that is plainly in the file. The cap is 4000 now. Wires
off screen are dropped before their geometry is built, which is what makes that affordable: ten full
redraws of all 3978 nodes measure 240ms.

**Nodes were drawn on top of each other.** A column placed its nodes at row number times *this* node's
height, and a node is as tall as its slot count, so anything shorter than its neighbour overlapped the
one below. Now each column keeps a running offset. On a small graph this was barely visible; on the
weapon graph it was most of why the canvas looked like a mess.

**Opening the weapon behaviour took about two minutes.** The Symbols tab asked "what references this
symbol" one symbol at a time, and each ask rescanned seven megabytes of text: 873 symbols, roughly 110
seconds of pure scanning. One pass builds the whole table now. Selecting a node also parsed the file
twice, once for the fields and once for the status line. The file opens in a couple of seconds.

## 2026-08-04, a new node lands where it was dropped

**Dragging a wire out to empty canvas put the node at the far end of the graph.** The canvas lays
nodes out by their depth from the root, and a node nothing points at yet has no depth, so it went into
a column of its own past everything else, nowhere near the cursor that asked for it. The drop point is
now carried through the menu and pinned before the canvas rebuilds, the same way a node dragged by
hand keeps its place.

**And it is wired into the slot the drag came from.** The slot was being collected and then thrown
away: the new node was attached to whatever happened to be selected, by whichever slot its class would
normally take, so dragging off a clip's `triggers` could hang a generator somewhere else entirely. A
drag now names the slot, and if that slot will not take the node it says so and leaves it unattached
rather than putting it somewhere it was not asked for.

## 2026-08-04, the fields are next to the node now, and one state at a time

**The properties panel was in the wrong tab.** Clicking a node on the canvas filled a panel that only
existed beside the tree, so the fields were built, correct, and invisible unless you switched tabs and
lost the node you were looking at. The panel is now a control rather than a loose stack, and there is
one beside the canvas as well as one beside the tree. Double clicking a node puts the caret in the
first box instead of nudging the node a pixel.

**An empty field could not be given a value.** hkxpack writes an empty string as a self closing tag,
and the reader only matched `<hkparam name="x">value</hkparam>`, so `animationBundleName` and every
other empty field was missing from the panel entirely. Both shapes are now read in one pass, so the
order fields appear in is still the order they sit in the file, and writing an empty value puts the
self closing form back rather than leaving `<hkparam name="x"></hkparam>` behind. Proved by editing
`PipboyBehavior.hkx` #98 through a real hkxpack repack and reading it back: the value survives, and
clearing it repacks clean. Arrays are excluded for free, since a `numelements` attribute sits between
the name and the slash.

**Highlighting one state.** Right click a node, "Highlight the paths of ...", and every wire not
touching it drops to half opacity while unrelated nodes dim to 40%. The lit wires are drawn in a
second pass so they sit on top of the dimmed ones rather than being crossed by them. Escape clears it.
A shipped graph draws a few hundred wires over each other and following one state through that was the
thing the canvas was worst at.

## 2026-08-04, variableBounds is positional after all

**The struct settles it.** The open question was what a short `variableBounds` array keys off, since
`MTBehavior` carries 19 entries against 67 variables and the entries do not all look right for the
variables they would land on. The answer is that it cannot key off anything: `hkbVariableBounds` is 8
bytes holding `min` at offset 0 and `max` at offset 4 and nothing else, read out of the class the
engine registers for it at startup rather than guessed. There is no field in it that could name a
variable, so position is the only key there can be.

So a short array means the variables past its end have no bound, and an unbounded variable inside it
is written `0..0`. Measured over the 531 vanilla files: 224 empty, 17 the same length as the variable
list, 87 shorter. In 85 of those 87 the last entry is a real bound rather than `0..0`, which is what a
trailing-trimmed positional array looks like.

Two statistical attempts to find a different key are recorded as having failed to separate anything:
scoring bounds against the type of the variable at each alignment gives 79.6% for positional and
79.7% for a one-place shift, and scoring against what the variable's name implies gives 33% and 38%.
Neither is evidence, which is why the struct layout is what this rests on.

**A removal was mishandling it, and worse than the ticket assumed.** Removing a variable took its
bound with it only when the array was full length. The audit it tested was taken after the name had
already been removed, so a file with three variables and two bounds looked parallel at that moment
and the bound was removed anyway; removing the last variable then tried to remove a bounds entry that
was never there and threw. Both are fixed by asking the only question that matters, whether the
removed index is inside the array.

Adding was already right and is unchanged: a new variable goes on the end, past a short array, so it
needs no entry.

Still not done, and now the only thing left on the ticket: nothing authors a bound. The window has no
way to set one.

## 2026-08-04, a plain build was silently read only

**The jar the editing layer needs was never copied next to the program.** Only the release zip
carried it, so anything run out of the build directory quietly dropped to read only: the tree still
drew, because that is read straight from the binary, and the Graph, Symbols, Chain and Animation tabs
were simply empty, because all four come from the unpacked text form. Save was off. It looked exactly
like a tool that does not work.

The build copies `tools/hkxpack-cli.jar` and both licence files to the output now, so a build and a
release behave the same way.

The message made it worse. It said editing needs a Java runtime, when Java was installed and present
on PATH the whole time and the jar was the missing half. It names which one is missing now, says the
four tabs are empty because of it and that the tree does not need it, says where to put the jar, and
is drawn in the warning colour rather than as muted text nobody reads. Save's own refusal was
similarly folded into one message and is now two.

## 2026-08-04, check graph marks the canvas

**A finding now points at a node instead of scrolling past in a status line.** Check graph outlines
the node it is about, red for an error and amber for a warning, with a soft halo outside the border so
it is still findable zoomed out where a one pixel edge is one pixel. The problem list under the canvas
names every finding, and clicking a row centres the view on that node and selects it, which is the
part that matters: the node that is wrong is almost always the one off screen.

Getting there needed the findings to know what they were about. Every one already started its location
with the object id, so a `Finding` now carries that id, taken from the text rather than threaded
through the forty odd places that build one. Errors beat warnings on the same node, or a node with one
of each would draw amber and read as something that can be left alone.

Measured rather than assumed: over the 531 vanilla behaviour files the checker produces 208 findings
and **all 208 can be placed on a node**. It was 197 before this. The last 11 were symbol index
references past the end of the declared list, which named the class and the member but not which of
the file's objects carried it, so the one fault nobody could locate was the one that needed locating
most. The scanner tracks the enclosing object now.

Marks are kept across rebuilds, so fixing one thing does not silently clear the rest of the list.

## 2026-08-04, the refusal now says which state and what to do

Save blocking a file the game cannot load is right, but the first version of that block only said how
many states were empty and told you to go and run Check graph. Being stopped without being told which
state, or what to do about it, is worse than not checking at all: it turns a two second fix into a
hunt through the tree.

It now names them, with the machine each one sits in, four at a time and a count for the rest, and
spells out both ways out: give the state a generator, or delete the state. Check graph's own wording
carries the same advice, so the two do not read differently for the same fault.

Nothing about when it refuses changed. Four checks hold the message to naming the state, naming its
machine, and offering both fixes, and all four fail if the message goes back to a bare count.

## 2026-08-04, the state resolution figure, measured over the whole game

**All 531 behaviour files, all 5329 states, nothing unresolved.** The README had been quoting a
subset, 314 files and 4881 states, because that was what happened to be extracted at the time, and
before that it quoted "5292 of 5323" from a document in another repository that nobody could check
from here. Both are replaced by a number this repo can reproduce.

    5329 states across 531 files
      0 with no generator
      0 pointing at an object not in the file
      15 generator classes

The 31 that supposedly did not resolve were never a reading failure. That figure came from
OpenCommonwealth's whole-library conversion run, where "understand" meant "map to a Godot animation
node", and its own numbers name the cause: 34 unmapped generators, all `BSBehaviorGraphSwapGenerator`
with a null `pDefaultGenerator`. Counting those here gives 34 out of 34, exactly, which is what
settles it. This reader parses every one; the Godot converter had nothing to point them at. The 31
was that 34 arrived at by subtraction against a different denominator.

`symrm states` is the new command, so the claim in the README is re-runnable rather than asserted.
It walks with `BehaviourGraphModel` and `StateEditor`, the same code the window uses, which is the
point: a separate script agreeing with itself proves nothing about the tool. An independent walk over
the raw XML was run alongside it and agrees on all four numbers.

## 2026-08-04, a state with no generator crashes the game

**Fallout 4 crashes while loading a graph that contains one**, so Save refuses to write the file
instead of warning about it. The tree and the graph still mark the state, and Check graph still names
it, but nothing reaches disk while one exists.

That was the open question on the ticket. Marking a state is easy; deciding whether an empty state is
a mistake worth blocking on was not, because no file with one had ever been in front of the game. It
has now. The Red Rocket garage door's `Closed` state had its generator link cleared through the
tool's own unlink and nothing else touched: 30 objects in, 30 objects out, same 7 states and 11
events as vanilla. Approaching the door takes the game down.

**It crashes on the load, not on entering the state**, which is the part worth keeping. The crash log
puts it in `BShkbUtils::GraphTraverser::Next` at `Fallout4.exe+0x1705DDF`, an access violation
reading address 0, under `LoadBehaviorHelper` → `BShkbAnimationGraph::InitImpl` →
`QueuedReference::BackgroundClone`, with `GenericBehaviors\SpecialCaseDoors\SpecialCaseDoors.hkx` on
the stack. The disassembly says why: the traverser pops each child a node reports off its own stack
and immediately reads that pointer's vtable to make a virtual call, with no null check anywhere on
the path. A null child is dereferenced as soon as the walk reaches it.

So reachability is beside the point. A state nothing can enter still kills the file, which is a
stronger rule than the one the tool was about to ship, and it is why the refusal does not ask whether
anything targets the state.

Unlink rather than delete on purpose. Deleting the orphan would also have exercised object removal
and renumbering, which are separately unproven, and a crash would then have had two candidates. The
only two crashes of this signature in the log are the two from this test; the one before it is an
unrelated CEF breakpoint from a week earlier.

The refusal and the mark both come from `GraphValidator.StatesWithNoGenerator`, so they cannot
disagree about what empty means, and five checks hold the refusal to saying what it is refusing and
why. Vanilla is unaffected: all 4881 states across 314 files have a generator, so a mark only ever
means an edit produced it.
## 2026-08-04, first edit to run in the game

**The Red Rocket gas station garage door was edited with this tool, loaded by Fallout 4, and did what
the edit asked.** It sat permanently half open, with no interaction needed, which is the whole point
of that particular test: a door cannot end up half open by accident, so the signal cannot be confused
with a broken mod.

The edit was three scalar values on one existing object, the `Closed` state's sequence generator:

    pSequence           Closed  ->  Opening
    eUseTimePercentage  NOT_USING_TIME_PERCENTAGE  ->  USING_TIME_PERCENTAGE
    fTimePercent        0.0  ->  0.5

The file keeps vanilla's 30 objects, 7 states, 11 events and byte size. So what the engine accepted is
a field value edit on an existing object, written here and repacked by hkxpack.

Everything before this was proven one step short of that: repack, read the binary back, count the
objects, run the validator. All of that says the file is well formed. None of it says the engine will
load it, and the README carried "none of it has been loaded by Fallout 4" as a standing caveat since
the tool was split out.

That caveat is now narrower, and only in one direction. Structural editing is still untested against
the game: adding a state, removing one, retargeting a transition and renumbering a symbol have never
been in front of it, and neither has `symrm door`'s additive edit. The `.bak` is still worth keeping.

## 2026-08-04, the Pip-Boy's unused variables

**`iTabSync` and `iCatSync` are declared and never used, by anything.** They looked like the obvious
drivers of the Pip-Boy's tab and category switching, and the Symbols tab showing an empty "Used by"
column for both read as the tool missing a route rather than as the answer.

Searched three places, case insensitively: the behaviour file binds neither, the 1.10.163 unpacked
binary contains neither byte sequence anywhere in 65 MB, and no vanilla Papyrus script mentions
either, across all 8570 entries of `Fallout4 - Misc.ba2` decompressed and searched. The same pass
finds `PlayAnimation` in 220 scripts, so the search works.

The contrast is what settles it. `fRadLevel` and `fRadioTune`, the two the file does bind, are both
literals in `.rdata` beside the Pip-Boy's INI settings, and `PipboyManager::SetInputGraphVariables`
passes them to `SetGraphVariableFloat` by name. The by-name mechanism exists and is in use for exactly
two of the four variables. The other two never appear.

So the tab switching is event driven, which the file states outright, and the Symbols tab's wording
was right all along: an empty column means nothing in this file reads it, not that the symbol is dead.
For these two it happens to be both.

Recorded with its own caveat: a name assembled at runtime, or one written by a mod, would slip past a
byte search of the binary. Neither is plausible here and neither can be ruled out without reading the
values from a running game.

## 2026-08-04, lossless scale

**The lossless scale path is confirmed against the engine.** It could not be checked against game
data, because no vanilla animation of that class carries a scale, so it was checked against
`hkaLosslessCompressedAnimation::getFrameTransform` in the 1.10.163 unpacked binary instead.

Every point agrees: the scale word array at `+0xb8`, static values at `+0xa8`, dynamic at `+0x98`,
stride as the dynamic array's length over the frame count at `+0xd8`, and the same `(offset << 2) |
type` packing that `::getType` and `::getOffset` apply, four fields to a 64 bit word. Dynamic indexing
is `offset + frame * stride`, frame major, the same trap that nearly shipped on translations.

The one that mattered most: what a clear word means. The engine prefills the output transform before
touching any of it, with translation 0, rotation identity and scale 1,1,1,1, from a constant at
0x143828480 that reads as four ones. So returning 1,1,1 for a clear scale is the engine's answer, not
a convenient default, and a scale falling back to 0 would have collapsed whatever it drives.

13 new checks hold the reader to those rules, including the field above bit 32 that hkxpack's XML
drops, so the packing cannot drift back to a guess. The README no longer calls this unproven, but it
still says plainly that no real file has ever exercised it.

## 2026-08-04, frame browser

**The animation tab answers the question a variable driven clip asks.** Type a
`userControlledTimeFraction`, and it says which frame that is, moves the page to it and marks the row.
Previously that mapping existed only in `symrm frames`, printed for five fixed fractions, which is not
much use when you are aiming a Pip-Boy needle at a pose. It now lives in `HkxAnimationData.FrameAt` so
the window and the harness share one implementation rather than two that can drift.

**A bone filter**, because a character animation has 95 tracks and reading one bone's motion should
not mean scrolling past 94 others. Filtering also expands what it finds, so a search lands on frames
rather than on a collapsed row.

Nonsense in the fraction box is refused and says so rather than aiming at something. Out of range is
clamped, since the value comes from a graph variable and wrapping to the other end of the clip would
be worse than pinning to the nearest.

Checked against real files: fraction 1 on `Idle_TrainTrain_Song05` lands on frame 3684 of 3684 and
jumps 13 pages to get there, and 0 and 1 land on the ends of every file tested.

## 2026-08-04, scale

**Animation scale is decoded, shown, and checked against real data.** It was being decoded all along
and then printed nowhere: the tab had columns for position and rotation only, and `symrm frames`
counted scales without ever showing one. A wrong value and a right one looked identical, which is not
a decode anyone should trust.

There is a Scale column now, and `frames` prints it, on the tracks that carry one. Almost every track
in the game is a flat 1,1,1, so printing all of them would bury the ones that are not.

Checked, rather than assumed. 130 of the 13133 vanilla spline compressed animations scale something,
none of them contains a zero, and the values are the shape authored data takes: the crow's
`PerchedIdle` folds both wings to exactly 0.4599 on all three axes, left and right identical. Those
float32s are in the file at 0x714 and 0x794, so the static branch is confirmed against the raw bytes
rather than against itself.

The lossless branch is still unproven and the README now says so plainly. All 856 vanilla lossless
animations leave both scale arrays empty with every scale word clear, so only the clear case has ever
run. It returns 1,1,1 there, which is correct, but nothing in the game exercises static or dynamic
scale. `symrm scale` is the sweep that produced these numbers.

## 2026-08-04, later still

**Expanding an event says what the file does with it.** Raised here, listened for here, or written
somewhere with no established direction, each naming the class and member rather than the struct that
carries it: every clip trigger and every alarm is an `hkbEventProperty`, so that name separates
nothing.

No verdict comes with it, which was the decision on the ticket. An event listened for with nothing in
the file sending it is the ordinary case, not a fault: 2912 of the 4799 events used across the 314
vanilla behaviour files look exactly like that, because Papyrus and the engine send them by name from
outside. A check would be wrong more often than right.

The role table was enumerated rather than recalled. Those files write an event index in 43 distinct
class and member pairs and all 43 are listed, so the only thing reporting as "referenced" on vanilla
data is `BSLimbCycleModifier`. Anything outside the table reports the same way instead of being
assigned a direction. `symrm events` reprints the measurement over a directory.

Found on the way: state enter and exit notify events were invisible. They sit inline in
`hkbStateMachineEventPropertyArray` with no class attribute of their own, so the reference walk never
saw them, in 2804 places across the vanilla corpus. That hid them from the Used by column and, worse,
from renumbering, so removing an event left every notify event above it pointing one too high. Both
are fixed.

## 2026-08-04, later

**Check graph now finds a state nothing can enter.** Being referenced and being reachable are
different questions for a state: a machine always lists its own states, so the unattached check could
never see one that no transition targets. That is what the door edit produced, and the checker had
nothing to say about it.

The ticket asked for this as an error, on the grounds that a dead state is always a mistake. Vanilla
says otherwise, so it ships as a warning. Swept over all 328 behaviours: 477 hits, dominated by
`RagdollAndGetUp`, the `SharedCore` wrapper state and `PairedState`. Those are entered by the game,
not by the graph, and nothing in the file describes how. Checked and ruled out on samples:
`startStateIdSelector` is null, `startStateId` is not variable bound, and all four of Havok's implicit
transition event ids are -1. Skipping machines that have no transitions at all, which are engine
driven by definition, takes it to 123 across 56 files. States named as a `toNestedStateId` target are
exempt too, since a parent machine can enter a nested state directly.

Two independent implementations of the reachability walk, one in the validator and one throwaway in
Python, agree on the same set, so the count is the data rather than a bug in the walk.

## 2026-08-04

Two checks that need something outside the single file, which is why the validator never had them.

**A clip's animation is now checked against the folder on disk.** Getting there meant fixing the
chain first: it read the animation list from `animationNames`, which is a Skyrim field. Fallout 4
puts them in `animationBundleNameData`, so the Chain tab's animation list had been empty for every
vanilla file it had ever been pointed at, and nothing downstream could have checked anything.

Swept over the whole of `Fallout4 - Animations.ba2`: 215 project roots, 328 behaviours, 111 clips
either missing their animation on disk or playing one the character does not declare. Those are real
rather than false alarms, so both are warnings and not errors. Shared behaviours reference per
creature animations that not every creature has, and some clips point at content that never shipped
in any form. Dogmeat's behaviour plays `Animations\WalkForward_B.hkt` and there is no such file.

**Save verifies the repack before overwriting anything.** hkxpack renumbers every object, so a
repack cannot be compared by id, but the object count and the multiset of class names have to come
back identical. They are compared now, on the file hkxpack actually produced, before the original is
touched. A short file is refused and named rather than written.

`symrm anims` and `symrm repack` run both from a clone. `anims` takes a directory to sweep every
project root beneath it, which is where the numbers above come from.

## cac7b09, 2026-07-30

Door graph editing, symbol removal and a validator. One squashed commit covering the session.

**Doors are driven by events, not variables.** Every animated door, lift, periscope and switch
checked declares no graph variables at all. They are state machines, and Papyrus sends the events:
`ObjectReference.PlayAnimation` takes the name of the event to send to the object's animation graph,
and 177 vanilla base scripts call it or `PlayAnimationAndWait`. The names line up on both sides, so
`DN151_DoorSeal` sending `StartOpen` and `Open` reaches a graph that declares exactly those. The
Pip-Boy pattern of binding a variable to `userControlledTimeFraction` is for gauges and dials, and is
the wrong tool for a door.

**The SpecialCaseDoors edit.** `symrm door` adds `StartOpen` and `StartClosed`, which that behaviour
does not have. `StartOpen` goes straight to the held `Opened` pose, the way `SwitchDoorExLarge01`
does it, so a door placed open is simply open rather than animating itself while the cell loads.
`StartClosed` plays its sequence and settles. No existing transition is retargeted, because those
event ids are shared by every door built on this behaviour. 30 objects, 7 states, 10 transitions and
11 events become 33, 8, 13 and 13.

An earlier version of that edit built a `StartOpening` state for `StartOpen` to enter. Once
`StartOpen` was pointed at the existing posed state instead, that state had nothing pointing at it
and duplicated the `Open` state the graph already had, so it is no longer created. The checker did
not catch it while it existed, which is filed as issue 12: an unreachable state is still a referenced
object, because the machine lists it.

**Also in this commit.** Nodes can be added from the graph view. Variables and events can be removed,
renumbering every reference above them. `AddVariable` now writes `variableBounds`, a fourth parallel
array it had been skipping. `GraphValidator` and the Check graph button. `tools/symrm`, the harness
that produces the numbers quoted here, so they can be re-run from a clone.

**Not verified in game.** Everything above is proven against hkxpack round trips and the validator.
No file produced by this tool has been loaded by Fallout 4.

Session notes for this work, including the reasoning that did not belong in commit messages, are
recorded outside the repository in the assistant's own store rather than here.
