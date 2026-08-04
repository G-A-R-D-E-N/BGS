# Changelog

Notable changes, newest first. Read the commit messages for the detail; this is the shape of the
work rather than a list of every edit.

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
