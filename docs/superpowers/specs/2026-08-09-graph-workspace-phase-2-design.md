# Graph workspace Phase 2 design

## Purpose

Phase 2 keeps the Graph tab focused on editing. Navigation and simulation information move to a
reusable top-level Workspace tool window, while state-machine focus, static tracing, and object-ID
chips remain graph-view actions. No change may alter XML, parsed graph data, routes, saved data, or
simulation semantics.

## Evidence and constraints

`symrm objects dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx hkbStateMachine` reports 906
objects, all covered by the class table, including 33 state machines. The Workspace window therefore
uses a scrollable, filterable machine list and receives its active indicators from the existing
`GraphRun.Where()` path.

The existing graph edge definition remains authoritative. Static tracing uses existing
ownership/reference relationships and resolved `StateRoutes`. It must not change
`GraphAuthor.PointsAt` or broaden its deliberate reachability policy.

Supported files serialize object identifiers in numeric `#98` form. The parser stores the inner
numeric value. The UI presents `#` plus that existing value and does not claim literal-ID support.

## Scope

Included:

- Remove the permanent left graph pane.
- One reusable, resizable Workspace top-level window with Machines and Runtime tabs.
- Workspace lifecycle persistence: single instance, remembered bounds, hide-on-close, reopen and
  activate the existing instance, and continued synchronization while hidden.
- View-menu access to Workspace, Properties, Problems, Output, and a separate Legend reference
  window.
- Explicit Focus tree, Show full graph, static trace actions, and object-ID chips on canvas nodes.
- Active-machine indicators, machine filtering, and selection/frame behavior in Workspace.
- Inspector containment through constrained two-column rows and independently scrolling Properties.
- UI smoke, rendered 1600x1000 evidence, and existing regression gates.

Excluded:

- Runtime execution history, runtime tracing, Variables, Watches, Timeline, Breakpoints, Search,
  Statistics, and other future Workspace tabs.
- Parser, serialization, native-write, graph-layout, or simulation-semantics changes.
- Literal nonnumeric object identifier support.
- Replacement or expansion of the Tree tab.

## Graph workspace

The Graph tab contains only the grouped toolbar, graph canvas, Properties pane, and optional
Problems/Output drawer. There is no permanent left navigation pane and no runtime panel below the
canvas.

The View toolbar group exposes a compact menu or equivalent popup with these actions:

- Workspace: opens or activates the Workspace tool window and states whether it is open.
- Properties: shows or hides the right inspector pane.
- Problems and Output: open the bounded diagnostics drawer on the requested tab.
- Legend: opens a separate temporary Legend reference window.

The toolbar does not gain a separate permanent Workspace button. View is the single home for
workspace visibility and reference material.

## Workspace tool window

Workspace is a normal, resizable, top-level editor window. It has one instance per main window and
uses the main window as owner. It remembers its last normal size and position through `Settings`;
the first presentation is centered on the owner. Closing cancels the native close and hides the
window. Reopening shows the same window, restores it if minimized, and activates it. Hiding it does
not stop or reset simulation.

Workspace owns only its presentation. `MainWindow` remains the owner of graph selection, GraphRun,
loaded document data, and mutation operations. When the loaded graph or current run changes,
MainWindow refreshes Workspace immediately whether it is visible or hidden.

The window starts with a TabControl rather than a layout specialized to two tabs. Future tool tabs
can be added without reworking the window frame or lifecycle.

### Machines tab

Machines lists only `hkbStateMachine` objects in loaded graph order. Each row has the display name,
serialized numeric identifier, and presentation-only running marker. An unnamed machine displays
its class name. A filter field matches display name or serialized ID without changing the graph.

Selecting a row selects the same object in the canvas and Properties pane and frames its existing
graph context. It does not enter Focus tree, clear or start a trace, change graph visibility,
modify XML, or affect simulation.

### Runtime tab

Runtime hosts the existing active-machines, stopped, held-back, and event-log controls. It is the
current home for simulation information. Its tab organization deliberately leaves room for later
Variables, Watches, Timeline, and Execution History tabs without adding disabled placeholders now.

## Legend reference window

Legend is reference material, not navigation. View > Legend opens a separate temporary top-level
Legend window. It is resizable, centered on first presentation, hides on close, and reopens as the
same instance. It is independent from Workspace and does not consume graph-canvas width.

## Focus tree and static trace

Focus tree is an explicit graph action enabled only with a selected state machine. It uses existing
ownership data to filter GraphView visibility, does not mutate data or simulation, and Show full
graph explicitly restores all graph nodes.

Static trace is independent from selection. Upstream, Downstream, and Both traverse only existing
ownership/reference links and resolved `StateRoutes`; Focus tree limits traversal to visible nodes.
Tracing emphasizes its result, dims unrelated visible content, frames the result, and Clear trace
restores normal emphasis while retaining selection. It must not change GraphRun state.

## Inspector containment

Every regular field, enum field, bone-array field, and expandable-array header uses a bounded
two-column layout: a fixed, ellipsized label column and a star-sized value column. The row and its
children clip to their bounds. Long summaries and values trim rather than widening the scroll
content. Properties retains vertical scrolling only and no child may paint outside its host.

## Verification plan

Smoke coverage must prove the real UI paths:

- The Graph workspace has no permanent left pane and keeps the canvas usable at 1600x1000.
- View exposes Workspace, Properties, Problems, Output, and Legend without duplicate controls.
- Workspace opens once, activates on repeat request, hides on close, restores saved bounds, and
  keeps simulation running while hidden.
- Workspace starts with Machines and Runtime tabs. Machines filters, lists only state machines,
  shows numeric IDs and active markers, and selection frames without entering Focus tree.
- Runtime receives the existing live simulation grids and remains synchronized after it is hidden.
- Legend opens independently and never changes the graph workspace width.
- Focus, Show full graph, static tracing, trace clearing, and ID chips retain their Phase 2
  behavior and restrictions.
- Properties rows and expander headers fit within the Properties host and vertically scroll.
- Existing selection, playback clipping, diagnostics drawer, parsing, serialization, and simulation
  smoke checks remain green.

The implementation starts by adding assertions to `tools/uismoke`, observing the expected failures,
then making the smallest production changes. Before commit it renders the Graph workspace at
1600x1000 with Workspace visible and hidden, then runs all four required repository gates.
