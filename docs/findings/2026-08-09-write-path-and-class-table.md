# Write path and class table findings, 9 August 2026

Things measured against the sample corpus and against this repository's own code. Nothing here is
implemented yet. Each entry says how it was checked so it can be rechecked.

The corpus is every `.hkx` under `dist/examples`, 453 files that parse as Fallout 4 packfiles.

---

## 1. Appended strings can land on an odd offset

**Status: defect, not yet fixed.**

A string member is a pointer whose lowest bit is used as a flag rather than as address. Bit set means
the buffer belongs to the object and should be released with it. Section data begins on a sixteen
byte boundary, so the parity of an offset inside the section is the parity of the loaded address.

**Measured.** Every local fixup destination in the corpus was read and its parity counted.

| | |
|---|---|
| files | 453 |
| local fixup destinations | 37,545 |
| landing on an odd offset | 0 |

Havok's own writer never puts one on an odd offset.

**What this repository does.** `PackfileImage.AppendObject` rounds up to sixteen before appending, and
says so, having been measured the same way. `PackfileImage.AppendData` does not round up at all, and
that is the one `PackfileObjects.WriteString` uses, along with the string array writer in
`NativeSave` and the string append in `NativePaste`.

Text of an even character count becomes an odd byte count once the terminator is added, so the next
append after it starts on an odd offset. Two string edits in one save, the first with an even length,
is enough to produce one.

**Why it would not have been noticed.** The file still reads back correctly, repacks identically and
passes the validator. The consequence is at the far end, when the game releases an object and is
pointed at memory inside the packfile image.

**Fix.** Round up before appending a string. Two is enough for correctness. Sixteen matches what is
already done for objects and costs a handful of bytes.

---

## 2. Growing an array that was empty loses the do not free flag

**Status: defect, not yet fixed. Blocks the second half of #44.**

An array member is a pointer, a count, and a word holding the capacity in its low thirty bits and
flags in its top two. Bit 31 means the storage is not the array's to release, which has to be true of
anything sitting inside the file.

**Measured.** Array headers were located through the local fixup table, which is where an array with
a run in the file is pointed from, and their capacity words read.

| | |
|---|---|
| array headers found | 4,850 |
| carrying bit 31 | 4,850 |
| carrying bit 30 | 0 |

Unanimous. An earlier count of this was wrong because it looked for a non zero pointer, and on disk
that field is zero until the fixup is applied. Recorded here because the wrong method looked
plausible and gave a plausible answer.

**What this repository does.** Four places write a capacity word and they do not agree.

| where | what it writes |
|---|---|
| `NativeAnimation` | forces bit 31 on |
| `NativeSave` | preserves whatever the top two bits were |
| `NativeRemove` | preserves |
| `NativePaste` | preserves |

Preserving is right for an array that already had a run, because the flag was already there. It is
wrong for an array that was empty, whose capacity word is zero, because there is no flag to preserve
and none gets written. That is exactly the case that matters for growing a `variableBounds` array
from nothing, which is the open half of #44.

**Fix.** Force bit 31 rather than preserving it, wherever the run being pointed at lives inside the
file. One rule, one place, the way the reference walk was handled.

---

## 3. The class table records no default for any menu valued field

**Status: gap, not yet fixed.**

`HavokClassTypes.json` records a default for a member where one is known. It has them for numbers,
booleans and strings. It has none at all for the types whose values come from a fixed set.

**Measured.** Every member marked as written was counted by type.

| written members of these types | with a default recorded |
|---|---|
| 430 | 0 |

Not a scattering of misses. The extractor drops them for these types as a category.

**What it affects.** Anything offering to put a field back to normal, and any display that wants to
say whether a value is the ordinary one. It does not affect reading or writing files, because a
default is never stored in a file.

**Fix.** In the extractor, then regenerate. One change closes all 430.

---

## 4. This build's `hkbStateMachine` is not the stock shape

**Status: recorded, no action decided.**

The class table gives `hkbStateMachine` a size of 328 with a two byte member named
`sCurrentStateIndexAndEntered` at offset 324, marked as never written to a file.

The name does not follow the convention every other member in the class uses, and the layout the
class table describes for every other version of this class ends earlier. The likeliest reading is
that this is an addition on the game's side rather than part of Havok's own class.

Because it is never written, nothing about reading or writing a file depends on it. The size does,
if a `hkbStateMachine` ever appears as an element of an array of structs, which so far it does not.

**Worth doing before relying on it.** The unpacked game executable is the authority on the runtime
layout and can settle this. Left open rather than guessed at.

---

## 5. Clip duration is one frame interval shorter than the frame count suggests

**Status: constraint on future work, no defect today.**

A clip's duration is the time from the first frame to the last, not the time for every frame to be
shown. For a thirty frames per second clip of 337 frames that is 336 intervals, or 11.2 seconds, not
337 intervals.

**Measured.** Every animation under `dist/examples/Dogmeat/Animations` was decoded and its stored
duration compared against both readings.

| | |
|---|---|
| animations | 76 |
| duration equals `(frames - 1) / fps` | 76 |
| duration equals `frames / fps` | 0 |

**Already honoured, which this measurement confirms rather than warns about.** `AnimationEdit`
computes `duration = (kept - 1) * frameDuration` when trimming and derives
a missing frame duration as `Duration / (NumFrames - 1)`. Annotations are rebased rather than copied.

The measurement was worth taking because the rule was being followed without anything asserting it,
and because it was assumed rather than checked. On `main`, `NativeAnimation` reads the existing
duration and writes it back unchanged, which is right only while an edit leaves the frame count
alone.

What is missing is a regression check. An error here fails nothing. It plays the clip at slightly
the wrong speed and slides every sync marker. Dogmeat's clips carry markers named `FootFront`,
`SyncLeft` and `SyncRight`, so the symptom is feet out of step with the ground rather than anything
that looks like a bug in a file.

Annotation firing is a half open window, so one landing exactly on `duration` never fires. No
annotation in the corpus sits past its clip's duration, so that is gateable as an invariant.

---

## 6. A state machine often does not start where `startStateId` says

**Status: defect in the unreachable state check, not yet fixed.**

`GraphValidator.CheckReachableStates` walks outwards from `startStateId` and reports states it cannot
arrive at. A state machine has other ways of choosing where it starts, and this build's own files use
them.

**Measured.** The four behaviour files under `dist/examples/Dogmeat/Behaviors` were read and their
state machines counted by start state mode.

| file | machines | not the default mode |
|---|---|---|
| DogmeatRoot | 9 | 5 random |
| DogmeatDefault | 33 | 3 synced to a variable, 2 random |
| DogmeatDefaultWrappingSneak | 1 | none |
| DogmeatFurniture | 2 | none |

Five of the nine machines in `DogmeatRoot` do not use `startStateId` at all. The check reads neither
the mode nor the variable index.

There are also two events on a state machine that move it to the next state id up or down, and which
take effect whether or not a transition exists between them. When either is set, every state in that
machine is reachable and the check cannot know it. Neither field is read.

**Also wrong, separately.** The suppression for states entered from a parent machine, at
`GraphValidator.cs`, has two faults. It treats a nested target as real when the id is not zero, but
whether it is real is carried by a flag on the transition, so a genuine target of state zero is
ignored and a leftover value with the flag clear is honoured. And it pools the ids across the whole
file, so a nested target of five in one machine excuses state five in an unrelated one.

Reading the flag needs the transition's `flags` field, which `StateEditor.TransitionRow` does not
currently carry.

---

## Order these are worth doing in

1. The two write path defects, 1 and 2. They are the only ones where the tool can put something bad
   in a file, and 2 is in the way of #44.
2. The class table defaults, 3. One change, 430 gaps.
3. The unreachable state check, 6. Largest reduction in noise, no risk to a file.
4. The duration rule, 5, when frame count editing is picked up.
5. The state machine layout question, 4, whenever the executable is being read for another reason.
