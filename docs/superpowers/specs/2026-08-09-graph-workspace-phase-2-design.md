# Graph workspace Phase 2 design

## Purpose

Phase 2 makes a large behaviour graph easier to navigate without changing the graph, its saved
data, or simulation semantics. It adds a state-machine-only navigator, an explicit tree focus
mode, a static dependency trace, object-ID chips on nodes, and active-machine indicators.

The graph canvas remains the primary workspace. The existing Tree tab remains the complete object
tree. This phase does not redesign diagnostics, the Runtime window, or the toolbar.

## Evidence and constraints

`symrm nesting dist/examples` measured 453 readable files, 45 state machines, 167 states, and 181
resolved transitions. The existing `StateRoutes` model resolves the transition relationships used
by the graph. `GraphView` already owns selection, dimming, and framing behavior.

Every measured file serializes object names in the numeric form `#98`. The current text and graph
parsers accept only that numeric format and store the inner value, such as `98`. Phase 2 displays
that serialized form by adding the `#` only for presentation. It neither renumbers objects nor
claims support for literal identifiers such as `State1`.

The existing graph edge definition remains authoritative. Static tracing uses the same existing
ownership/reference relationships and resolved `StateRoutes`; it must not modify
`GraphAuthor.PointsAt` or expand its deliberate reachability policy.

## Scope

Included:

- Machine Navigator in the left pane.
- Explicit Focus tree and Show full graph actions.
- Static upstream, downstream, and both-direction dependency tracing.
- Serialized numeric object-ID chips on canvas nodes.
- Active-machine indicators in the navigator while simulation is running.
- Headless UI smoke coverage and existing regression gates.

Excluded:

- Runtime execution history and runtime tracing.
- Diagnostics, Runtime-window, or toolbar redesign.
- Parser, serialization, native write, or simulation-semantics changes.
- Literal nonnumeric object identifier support.
- Any replacement or expansion of the Tree tab.

## Machine Navigator

The left pane defaults to **Machines** when a graph is loaded. It contains only
`hkbStateMachine` objects, ordered as their graph objects are loaded. Each row shows the
machine's display name and its serialized numeric object ID, for example `Locomotion   #221`.
An unnamed machine uses its class name as its display name. The navigator does not list arbitrary
generators, state infos, symbols, or full object hierarchies.

Selecting a navigator row performs the same selection path as a canvas selection, selects the
machine in the inspector, and frames that machine and its immediate graph context. It does not
enter focus mode, hide nodes, alter a trace, modify XML, or start or stop simulation.

While a `GraphRun` exists, a machine row receives an active indicator when it has an active,
non-fading runtime machine. The indicator is presentation only and is refreshed from the existing
run update path. It is removed when the run stops or a different file is loaded.

## Legend swap

The existing **Legend** control remains in the View group. It swaps the left pane between the
Machine Navigator and the Legend. Closing or collapsing the Legend restores the Machine Navigator
when the left pane is open. The left pane remains resizable and collapsible under the Phase 1
constraints.

## Focus tree

`Focus tree` is enabled only with a selected state machine. It is an explicit view action, never a
side effect of selecting a machine.

Focus computes the selected machine's ownership tree using the existing graph ownership result.
The visible set is the machine plus all descendants owned by that machine. Nodes owned by another
branch remain hidden even when they are referenced by a visible node. This makes the result a
readable ownership tree rather than a misleading duplicate graph.

`Show full graph` clears the focus filter and restores normal GraphView visibility. Focus state is
held only by the view. It does not mutate XML, the parsed model, routes, collapsed-state data,
pinned positions, the undo stack, or the simulation.

If a selected machine is not on the canvas or has no ownership tree, focus is refused with a clear
status message and the visible graph stays unchanged. A machine with no owned descendants is still
a valid focus result: the canvas shows that single machine and frames it.

## Static dependency trace

Static tracing is independent of selection. Selection chooses the seed. A trace begins only when
the user presses one of its explicit actions:

- **Upstream**: every visible graph node that can reach the seed.
- **Downstream**: every visible graph node reachable from the seed.
- **Both**: the union of upstream and downstream nodes and the seed.
- **Clear trace**: removes static trace emphasis while retaining the selected node and its
  inspector contents.

Traversal uses a cycle-safe breadth-first walk over existing reference/ownership relationships and
the existing resolved `StateRoutes` transition relationships. It does not create edges, rewrite
routes, broaden `GraphAuthor.PointsAt`, or alter GraphRun state.

When Focus tree is active, the traversal graph is restricted to the currently visible ownership
tree. A trace does not reveal nodes outside focus mode and does not silently cancel focus. In the
full graph view, traversal uses all canvas-visible graph nodes.

When there is no selected canvas node, trace actions are disabled. If a selected node is no longer
visible, the trace is cleared during the normal visibility rebuild. A seed with no path in the
chosen direction remains selected and frames itself as a one-node result.

## Trace visual states

The visual layers remain distinct:

1. Selection uses the existing selected-node treatment.
2. Static trace uses a dedicated strong path treatment for trace nodes and trace edges.
3. Non-traced visible graph content is dimmed but remains visible for context.
4. Active simulation state keeps its existing live-state ring and is not interpreted as trace
   history.
5. Validation marks retain their existing error and warning treatment.

Running a static trace frames the trace result. Clearing the trace restores normal emphasis, keeps
the selected node, and does not reframe the canvas.

Runtime execution tracing is intentionally absent from this phase. A later phase may add event and
transition history to the Runtime window, with a separate representation from static tracing.

## Node ID chips

Each rendered node gains a compact, right-aligned header chip. The primary node name remains on
the left. In current supported files the chip displays `#` plus the parsed numeric object ID, for
example `#98`.

The chip is a label only. It does not identify a node by draw order, does not change object IDs,
and does not imply support for alternate identifier syntax. The name truncates before the chip
rather than overlapping it; the chip remains readable at normal graph zoom.

## Proposed left-pane layout

```text
┌──────────── Machines ────────────┐
│ Filter machines...                │
│                                    │
│ ▾ Root                 #92  ●      │
│   Combat               #143        │
│   Locomotion           #221  ●     │
│   Idle                 #356        │
│                                    │
│ [Focus tree] [Show full graph]    │
│                                    │
│ Trace selection                   │
│ [Upstream] [Downstream] [Both]    │
│ [Clear trace]                     │
└────────────────────────────────────┘
```

`●` denotes a live non-fading machine. It appears only while simulation is running.

## Verification plan

Smoke coverage must prove the real UI paths, not only helper state:

- Machines is the default left-pane view after a real graph loads.
- Legend swaps into the same pane and returns to Machines when dismissed.
- Only state machines appear in the navigator, with their expected `#` numeric IDs.
- Selecting a navigator row selects the matching graph object and frames it without entering focus.
- Active-machine indicators match the current GraphRun state and disappear when the run stops.
- Focus tree changes only the visible canvas set, leaves loaded XML and run state unchanged, and
  Show full graph restores the full canvas set.
- Upstream, downstream, and both traces follow known graph-reference and `StateRoutes` paths.
- Static trace dims unrelated visible nodes, frames the trace result, and Clear trace preserves the
  selected node while restoring normal emphasis.
- A trace in Focus tree never includes hidden nodes.
- Node ID chips render `#` numeric identifiers without obscuring node names.
- Existing selection, inspector, layout, playback clipping, and Phase 1 pane smoke checks remain
  green.

The implementation must begin by adding these assertions in `tools/uismoke`, observing their
expected failures, and then adding the smallest production changes to make them pass. The four
repository gates run before the implementation commit.
