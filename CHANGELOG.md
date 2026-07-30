# Changelog

Notable changes, newest first. Read the commit messages for the detail; this is the shape of the
work rather than a list of every edit.

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
