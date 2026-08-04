# Changelog

Notable changes, newest first. Read the commit messages for the detail; this is the shape of the
work rather than a list of every edit.

## 2026-08-04, first edit to run in the game

**The Red Rocket gas station door was edited with this tool, loaded by Fallout 4, and the animation
played correctly.**

Everything before today was proven one step short of that: repack with hkxpack, read the binary back,
count the objects, run the validator. All of that says the file is well formed. None of it says the
engine will accept it, and the README has carried "none of it has been loaded by Fallout 4" as a
standing caveat since the tool was split out.

That caveat is now narrower. One edit, one door, one file, confirmed in game. It does not generalise
to every edit the tool can make, and the `.bak` is still worth keeping, but the gap between "hkxpack
accepts it" and "the game accepts it" has been crossed at least once, which is the first evidence that
the write path produces something the engine can actually load.

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
