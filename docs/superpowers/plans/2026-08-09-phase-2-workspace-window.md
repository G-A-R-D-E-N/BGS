# Phase 2 Workspace Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Graph tab's permanent left pane and separate Runtime window with one reusable Workspace tool window, while containing the Properties inspector.

**Architecture:** `MainWindow` remains the owner of document, graph, selection, and `GraphRun` state. A reusable `WorkspaceWindow` receives refreshed presentation controls for a tabbed Machines and Runtime surface. A separate reusable `LegendWindow` owns only reference material. Inspector rows use bounded grids so their desired width cannot widen a scroll viewer.

**Tech Stack:** .NET 8, Avalonia, existing `HkGrid`, `Inspector`, `Settings`, `GraphView`, `GraphRun`, and `tools/uismoke`.

## Global Constraints

- Remove the Graph tab's permanent left pane. Keep only toolbar, canvas, Properties, and optional diagnostics drawer.
- View owns Workspace, Properties, Problems, Output, Legend, Focus tree, Show full graph, and static trace affordances.
- Workspace and Legend are single-instance, resizable top-level windows. They hide on close, reactivate on reopen, and do not affect simulation.
- Workspace starts with Machines and Runtime tabs, but its frame is not specialized to those two tabs.
- Machines shows only state machines, supports text filtering, displays `#` numeric IDs, and frames/selects without enabling focus.
- Preserve Focus tree, static trace, ID chips, Tree tab, `GraphAuthor.PointsAt`, parser, serialization, native-write, and simulation semantics.
- All Properties contents must fit their pane horizontally, trim or clip overflow, and scroll only vertically.
- Before commit run all four required repository gates.

---

### Task 1: Add failing Workspace and containment smoke coverage

**Files:**
- Modify: `tools/uismoke/Smoke.cs`

**Interfaces:**
- Requires future `MainWindow.OpenWorkspaceForTest`, `CloseWorkspaceForTest`, `WorkspaceWindowForTest`, `WorkspaceVisible`, `WorkspaceWindowInstances`, `OpenLegendForTest`, and `LegendWindowVisible`.
- Requires future `WorkspaceWindow.TabHeaders`, `Machines`, `Runtime`, `MachineFilterText`, and `SavedBounds`.
- Requires future `Inspector.ContentsFitWidth` and `Inspector.ScrollsVerticallyOnly`.

- [ ] **Step 1: Write failing Workspace lifecycle assertions**

Add a Dogmeat smoke block that opens Workspace, closes it, and reopens it:

```csharp
CheckTrue($"{name}: graph workspace has no permanent left pane", !window.GraphLeftPanePresent);
window.OpenWorkspaceForTest();
CheckTrue($"{name}: Workspace opens as a top-level tool window", window.WorkspaceVisible);
Check("Workspace starts with Machines and Runtime tabs", "Machines, Runtime",
      string.Join(", ", window.WorkspaceWindowForTest!.TabHeaders));
int instances = window.WorkspaceWindowInstances;
window.CloseWorkspaceForTest();
CheckTrue($"{name}: hiding Workspace keeps the run alive", !window.WorkspaceVisible && window.RunReady);
window.OpenWorkspaceForTest();
Check("Workspace reopens without duplication", instances, window.WorkspaceWindowInstances);
```

- [ ] **Step 2: Write failing Machines and inspector assertions**

Add assertions that Workspace's Machines tab lists only state machines, its filter narrows results,
selection reaches the canvas without Focus tree, active markers refresh, and every Properties
editor and expander header fits within the Inspector bounds.

```csharp
var workspace = window.WorkspaceWindowForTest!;
workspace.FilterMachinesForTest("DefaultRoot");
Check("machine filter narrows its real list", 1, workspace.Machines.RowCount);
workspace.SelectMachineForTest(navigatorMachine);
Check($"{name}: Workspace selection reaches the canvas", navigatorMachine, window.Canvas.SelectedId);
CheckTrue($"{name}: Workspace selection leaves focus off", !window.GraphFocusTreeActive);
CheckTrue($"{name}: Properties contains every row", window.GraphProperties.ContentsFitWidth);
CheckTrue($"{name}: Properties scrolls vertically only", window.GraphProperties.ScrollsVerticallyOnly);
```

- [ ] **Step 3: Run the Dogmeat UI smoke test and observe expected compilation failures**

Run:

```bash
/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj -- dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx
```

Expected: compile failures naming the missing Workspace and Inspector APIs, proving the checks do
not pass against the current left-pane design.

### Task 2: Implement reusable tool-window presentation

**Files:**
- Create: `app/WorkspaceWindow.cs`
- Create: `app/LegendWindow.cs`
- Modify: `app/MainWindow.cs`
- Modify: `tools/uismoke/Smoke.cs`

**Interfaces:**
- `WorkspaceWindow(HkGrid machines, Control runtime, TextBox machineFilter)` hosts a `TabControl`.
- `WorkspaceWindow.Present(Window owner)`, `WorkspaceWindow.RememberBounds()`, and hide-on-close
  provide a one-instance lifecycle.
- `LegendWindow(Control legend)` uses the same hide-on-close lifecycle.
- `MainWindow` rebuilds both in-window machine lists from the current model and active run.

- [ ] **Step 1: Implement the smallest Workspace and Legend windows**

Create `WorkspaceWindow` with a `TabControl`, 480x680 initial size, 360x360 minimum size,
`CenterOwner` first presentation, saved normal bounds in `Settings`, and an `Opened`/`PositionChanged`/
`Resized` lifecycle that records the last normal position and size. Cancel `Closing`, remember
bounds, and call `Hide()`. `Present` calls `Show(owner)` only while hidden, restores a minimized
window, then calls `Activate()` and `Focus()`.

Create `LegendWindow` with the same top-level lifecycle and a vertically scrolling legend control.

- [ ] **Step 2: Move Graph-tab presentation out of the left pane**

Remove the left grid column, splitters, `Machines` and `Legend` hosts, and all left-pane resize
state from `BuildGraphTab`. Keep the canvas minimum width and right Properties splitter. Move
Focus tree, Show full graph, Upstream, Downstream, Both, and Clear trace into the existing View
popup so they remain explicit graph actions.

Move the existing runtime presentation controls into Workspace's Runtime tab. Create a dedicated
workspace machine grid and filter field, wire selection through `SelectObjectId` and
`GraphView.FocusOn`, and rebuild it whenever model or active-machine state changes.

- [ ] **Step 3: Implement View menu state and tool-window controls**

Use a single View menu/popup in the View toolbar group with entries for Workspace, Properties,
Problems, Output, Legend, Focus tree, Show full graph, Upstream, Downstream, Both, and Clear
trace. Workspace and Legend entries state `Open` or `Closed`; Problems and Output select their
drawer tab before opening it. No extra permanent Workspace toolbar button is added.

- [ ] **Step 4: Run the Dogmeat smoke test green**

Run the Task 1 command. Confirm the Workspace has exactly one instance, hides without stopping the
run, reopens and activates, filters the 33 real Dogmeat machines, and selection stays separate
from Focus tree.

- [ ] **Step 5: Mutation-test the reuse assertion**

Temporarily create a new `WorkspaceWindow` on every open. Run the Dogmeat smoke test and verify
the no-duplication assertion fails. Restore the one-instance lifecycle and rerun green.

### Task 3: Contain Properties rows

**Files:**
- Modify: `app/Inspector.cs`
- Modify: `app/MainWindow.cs`
- Modify: `tools/uismoke/Smoke.cs`

**Interfaces:**
- `Inspector.TwoColumnRow(Control label, Control value)` produces a clipped 128px-plus-star grid.
- `Inspector.ContentsFitWidth` and `Inspector.ScrollsVerticallyOnly` expose actual containment
  state for headless smoke checks.

- [ ] **Step 1: Run the failing containment assertion**

Keep the Task 1 inspector assertions in place and run the Dogmeat smoke command. Expected: it
fails because the Inspector containment properties do not exist.

- [ ] **Step 2: Implement bounded rows and headers**

Add `Inspector.TwoColumnRow` using a 128px fixed first column and `GridUnitType.Star` second
column, `ClipToBounds = true`, ellipsized labels, and stretch value controls with `MinWidth = 0`.
Use it from regular fields, enum fields, and bone-array rows. Replace `ElementBlock`'s horizontal
header stack with a clipped two-column grid so a long summary cannot set the content's desired
width. Keep the Inspector ScrollViewer horizontal scrolling disabled and vertical scrolling auto.

- [ ] **Step 3: Run the containment checks green**

Run the Dogmeat smoke command. Confirm the selected object renders all field types within the
Properties host and the scroll viewer can reach final rows.

- [ ] **Step 4: Mutation-test width containment**

Temporarily remove `ClipToBounds` from `Inspector.TwoColumnRow`. Run the smoke test and verify its
containment assertion fails. Restore clipping and rerun green.

### Task 4: Render, verify, and commit

**Files:**
- Modify only files proven necessary by Tasks 1-3.

- [ ] **Step 1: Render both requested 1600x1000 states**

Run:

```bash
/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj -- --png dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx /tmp/bgs-workspace-hidden.png 0.75 990 --window --fit
/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj -- --png dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx /tmp/bgs-workspace-open.png 0.75 990 --window --workspace-window --fit
```

Inspect both PNGs. The hidden state must show graph, Properties, and collapsed diagnostics with no
left pane. The open-state capture must show Workspace's tabs and a contained Properties view.

- [ ] **Step 2: Run all repository gates**

```bash
/home/ricky/.dotnet/dotnet test tools/tests/BehaviourGraph.Tests.csproj
/home/ricky/.dotnet/dotnet run --project tools/symrm/symrm.csproj -- test
/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj
/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj -- dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx
```

- [ ] **Step 3: Commit the reviewed correction**

```bash
git add app/Inspector.cs app/LegendWindow.cs app/MainWindow.cs app/WorkspaceWindow.cs \
    tools/uismoke/Smoke.cs docs/superpowers/specs/2026-08-09-graph-workspace-phase-2-design.md \
    docs/superpowers/plans/2026-08-09-phase-2-workspace-window.md
git commit -m "feat(workspace): move tools out of graph"
```
