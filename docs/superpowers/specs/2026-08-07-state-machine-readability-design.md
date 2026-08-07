# Reading a state machine: transitions, colours, and grouped fields

Date: 2026-08-07

## The problem

A state machine's most important fact is which event moves which state to which state. The tool
holds all of it and shows none of it.

On the canvas, a state machine draws as `hkbStateMachine -> states[] -> hkbStateMachineStateInfo`,
plus a dead end `hkbStateMachineTransitionInfoArray` box. The event, the target state and the
wildcard flag are inside that box as inline struct array elements, so they are never drawn. There is
no way to read a route off the picture.

In the properties panel, that same box is 80 text boxes in one flat column, because a struct array is
flattened element by element into a single run of fields. Five transitions of sixteen fields each
means `enterEventId`, `exitEventId`, `enterTime`, `exitTime` appearing four times before the
`eventId` that matters. People are reading the unpacked XML instead, where each element is a
collapsible block with a comment naming the event and the target.

On the canvas again, `Ux.ForClass` tests `cls.Contains("StateMachine")` first, which matches
`hkbStateMachine`, `hkbStateMachineStateInfo` and `hkbStateMachineTransitionInfoArray` alike. The
`Transition` rule below it never fires for that class. All three draw in the same accent colour.

## The bug underneath the panel problem

`HkxTextEdit.SetParam` replaces the first `<hkparam>` matching a name within an object block. A
transition array's block contains `eventId` once per element. The panel builds its boxes from
`ClassFields`, which flattens the array and keeps only the field name.

So editing the `eventId` of transition 4 writes transition 0's `eventId`, silently. The same applies
to every duplicate name in every struct array: `variableBounds`, `transitions`, `eventInfos`.
`_editedFields` is name keyed too, so one edit marks every same named field as edited.

This is not a display problem that happens to look bad. The element index is computed during the
walk and thrown away, and the write path has no way to say which element it meant.

## Design

### 1. Carry the element path

`ClassFields.Field` gains a path. `transitions[3].eventId`, and for a struct nested inside an
element, `transitions[3].triggerInterval.enterEventId`. The walk already knows the index; it stops
discarding it.

The flat list's order and length do not change. That matters: `PanelFields.For` refuses to use the
native reading at all unless its field count matches hkxpack's list, and `symrm panel` and
`symrm crosscheck` compare field for field across the corpus. The path is additional information on
each entry, not a restructuring of the list.

`PanelFields.Field` surfaces the same path.

### 2. Element addressed writes

A path aware write in `HkxTextEdit` that resolves `transitions[3].eventId` to the right occurrence
inside the object block, rather than the first one with that name. `MainWindow.Apply` and the
`_editedFields` keying go through the path.

This is the fix for the corruption above and has to land with the grouping, not after it: grouping
the panel makes the later elements easy to reach, which makes the bug easier to hit.

### 3. Group the panel

Each struct array element renders as one collapsible block instead of a run of loose boxes.
Collapsed by default, so five transitions is five lines rather than eighty boxes.

Each block is headed by a summary of what it is. For a transition that is the same thing the XML
comment says:

    164  dynIdleLoop  ->  100 EAP_dynIdleLoop_A

The event name comes from `SymbolEditor.EventNames`, the target state name from
`StateEditor.States`. A field with no summary available shows the element index alone rather than an
invented description.

### 4. Transition edges on the canvas

A second edge layer, drawn distinctly from the ownership wires: state info to target state info,
labelled with the event name. Wildcard transitions originate at the state machine node, since they
fire from any state.

Labels are gated by zoom and by selection. A weapon behaviour holds thousands of transitions and
drawing every label always would be unreadable; selecting a state lights its incoming and outgoing
routes while the rest dims, which is what the existing highlight already does for structure.

`toNestedStateId` targets are drawn as a marked stop rather than a guessed edge, following the rule
in #37: what we cannot model honestly, we draw as a stop.

### 5. Colours and the start state

`Ux.ForClass` matches by exact class name for the state machine family, so `hkbStateMachine`,
`hkbStateMachineStateInfo` and `hkbStateMachineTransitionInfoArray` each get their own colour.

The state info whose `stateId` equals the machine's `startStateId` gets a badge. `GraphValidator`
already reads `startStateId`, so this is a marker rather than new reading.

## Out of scope

Flag decoding. The panel shows `flags 9728` where the XML says `FLAG_IS_LOCAL_WILDCARD`.
`HavokClassTypes.NameOf` decodes flags but returns the raw number when any set bit has no declared
name, which is the honest answer and means the class table is missing bits for that enum. Working
out which bits are missing is #36 and needs its own investigation. Nothing here invents a name.

Removing hkxpack. Reading no longer needs it: `NativeXml.From` builds the editable text from the
file's own bytes and hkxpack is only a fallback when that text does not line up. Java is still
required to pack a structural change back out, which is #34 step 4 and #32. The path aware write
here stays inside the existing text pipeline rather than pre empting that work.

## Verification

- A test that writes a later struct array element's field and reads back that only that element
  changed. This fails before the change.
- Field count and value equality against hkxpack across the corpus, unchanged: `symrm panel` and
  `symrm crosscheck`, against the Dogmeat reference numbers of 11,882 and 7,587.
- The window checks in `tools/uismoke`, including a run under `tools/no-java.sh`, since the panel is
  reachable without a Java runtime and that path is the one nobody exercises by accident.
