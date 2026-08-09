# Reading a big graph: ownership, layout, collapse and multiple selection

Date: 2026-08-09

## The problem

A shipped behaviour draws a few thousand nodes and the canvas gives you one way to deal with that,
which is dragging nodes one at a time. Four things are missing and one thing is wrong.

**The layout is wrong, and it is not a matter of taste.** `GraphAuthor.Layout` returns a node and a
column, where the column is the node's depth from the root. `GraphView.Show` then places each node
using one running Y counter per column: the next node in a column goes below the last one put in that
column, in whatever order the walk happened to reach it. Nothing ever consults the parent's position.

So a parent sitting two thousand pixels down the canvas gets its children placed wherever that
column's counter has got to, which is usually near the top. The result is the long diagonal wires
across the whole canvas. They are not a drawing problem, they are the layout telling the truth about
where it put things.

**There is no multiple selection.** `SelectedId` is one string. Moving twelve related nodes means
twelve drags.

**There is no collapse.** A state machine with forty states draws forty states whether or not you are
looking at that part of the file.

**And a shared node has no way to say so.** Sharing is the ordinary case rather than an edge case:
across the corpus, 3,624 of 5,320 state infos share something with another state, usually a generator
two states both point at. The canvas draws that node once, under whichever parent reached it first,
with a wire from every other parent. Nothing on the node says it is borrowed.

## The idea the rest of this hangs off

Every node has exactly one **layout owner**: the parent that reached it first in the walk that places
it. Ownership decides three things and there is no second concept of it anywhere:

- where a node is placed automatically
- whether a collapse hides it
- whether a drag moves it

Other parents still draw their wire to a node they do not own. They do not get to move it or hide it.

This is not a new traversal. `GraphAuthor.Layout` already walks parent before child and already skips
a node it has placed, so the first parent to reach a node is already decided; the walk simply throws
that fact away today.

## Design

### 1. Ownership comes out of the walk that already exists

`GraphAuthor.Layout` returns `(HkObject Node, int Column, string OwnerId)` instead of
`(HkObject Node, int Column)`. `OwnerId` is empty for the root and for each detached node the walk
starts from.

`OwnerId` is the source of truth for placement, collapse and drag. Nothing else may compute
ownership by another route later. If a future feature needs a different grouping, it gets a
different name and does not touch this one.

`GraphView` keeps `_owner`, id to owner id, rebuilt on every `Show`, and derives from it:

- `OwnedBy(id)`, the direct children a node owns
- `OwnedUnder(id)`, every descendant through owned edges only
- `OwnerChain(id)`, the node's owners up to a root

`Layout` is covered by existing tests. `DetachedSubtreeStaysDrawn` asserts a drawn count of 7 and
`ReplacingLinkSaysWhatItDisplaced` reads the same list. Those tests get updated to the new shape and
gain assertions on the ownership contract itself:

- every node except a walk root has exactly one owner
- a node shared by two parents is owned by the one that appears first in the walk
- no ownership cycle: following `OwnerChain` always terminates

### 2. Layout is parent relative, and sibling groups do not get broken

Replace the per-column running Y counter in `GraphView.Show`.

- A walk root goes at the top of its column.
- The nodes a parent owns are laid out as one **sibling group**, stacked in walk order and centred
  vertically on the parent's own Y.
- Then one pass per column, top to bottom, resolves overlap. Where a sibling group overlaps the group
  above it, the whole group moves down by the overlap plus `RowGap`.

**A sibling group is never split.** Pushing one member of a group and not the others is what produces
the long wires, so a collision moves the whole family or nothing. Extra vertical whitespace is the
accepted cost and is preferred to a tighter graph.

**A pinned node wins over automatic layout.** Anything in `_placed`, meaning the user dragged it,
keeps its position, takes no part in collision resolution, and is never moved to make room. If that
leaves a hole or forces a group further down than it would otherwise go, that is the correct outcome.
Nothing the user positioned by hand may move on its own.

A node that is drawn but not owned by anything in this column, because its owner is elsewhere, is
laid out under its owner and only wired from here.

This changes the automatic position of every node in every file on first open. That is intended. The
positions being replaced are the ones producing the problem, and preserving them to avoid visible
change would defeat the change. Pinned nodes still land where the user put them.

### 3. Collapse

A chevron in the node header, left of the name. Click toggles that node.

A node is hidden when **any node on its owner chain is collapsed**. That single rule gives the
behaviour asked for without a special case:

- collapsing a state hides what that state owns
- a shared node owned by another branch stays visible, because the collapsed node is not on its owner
  chain
- the wire from the collapsed branch to that shared node is hidden with the branch, because the wire
  belongs to the hidden end

Collapsing a state can never blank out part of another state elsewhere on the canvas.

A collapsed node draws a badge reading `+14 hidden`. **That count is owned descendants actually
hidden by this collapse.** It excludes nodes owned elsewhere, and it excludes nodes already hidden by
a collapse further up. It is a count of what this chevron is responsible for, so expanding it brings
back exactly that many nodes.

**Ctrl+click the chevron** collapses or expands every owned descendant rather than one level. If any
owned descendant is expanded, ctrl+click collapses all of them; otherwise it expands all of them.

Collapse state is held in a set of ids and survives a rebuild, the same way `_placed` does, so an edit
does not silently unfold the graph. It is cleared when a different file is opened.

### 4. Multiple selection and group drag

`SelectedId` becomes a set. The existing single id stays as a property returning the first of the set
so callers that read it keep working, and `Selected` keeps firing with one id for the panel.

- **Left-drag from empty canvas** draws a marquee and selects every node whose bounds intersect it.
  Intersecting rather than fully contained, so a partly visible node can be caught without zooming
  out to fit it.
- **Ctrl+click** adds or removes one node from the selection.
- **Click a node** with no modifier selects only that node, as now.
- **Dragging a node that is not in the selection** selects only that node first, then drags it. It
  does not carry the previous selection along.
- **Clicking the chevron** collapses or expands and does not change the selection.
- **Middle-drag** pans, as now.

Left-drag on empty canvas currently pans. The marquee takes that gesture and pan stays on the middle
button only. This is a deliberate change to a common gesture.

Dragging any selected node moves the whole selection. On top of that, a dragged node brings the
descendants it **owns**, so moving a parent moves its family. A shared descendant owned by another
branch stays where it is.

**The movement set is deduplicated before the delta is applied.** A node that is both explicitly
selected and reached through its parent's ownership must move once. Build the set, then apply the
delta once per member.

Every moved node is written to `_placed`, so a group drag pins the whole group.

### 5. Saying that a node is shared

A shared node draws a second inset outline in its accent colour, one pixel inside its border, so it
reads as doubled. No icon and no header space, so it survives zooming out and does not compete with
the wildcard rows.

**Selection stays visually stronger than sharing.** The shared outline is drawn under the selection
treatment and at a lower opacity, so a selected shared node reads as selected first.

Hovering it shows `Shared by 3 parents`, listing them by name, or by id where a parent has no name.

### 6. A standalone animation fills the clip list

`BuildClipList` builds from `hkbClipGenerator` objects. An animation file holds none, so the list is
empty and the Playback panel looks broken, which costs every user a question before they can use the
tab.

When the loaded file holds no clip generator but does hold an animation, the list gets one row: the
file's animation name, with its duration and frame count in the second column. The row is selected,
so Playback behaves as if a clip had been picked.

## What this does not do

No automatic re-layout button, no saved layouts, no per-column manual sizing, and no change to how
routes are drawn. Collapse and selection do not change the file. Nothing here writes an hkx.

## Testing

Ownership, layout and the collapse count are logic and get unit tests in `tools/symrm/Tests.cs`
against a graph built in memory, the same as every other case there:

- ownership contract, as listed in section 1
- a child's Y is inside its parent's sibling group's span, and the group is contiguous
- a collision moves a whole sibling group, never part of one
- a pinned node is not moved by collision resolution
- collapsing a node hides its owned descendants and not a node owned elsewhere
- the hidden count equals owned descendants hidden by that collapse
- the movement set for a drag with overlapping selection and ownership contains no duplicates

Marquee, ctrl+click, chevron hit testing and the hover text are window behaviour and get covered by
`tools/uismoke/Smoke.cs`, which already cycles tabs and drives the canvas.
