# Graph Workspace Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Graph tab a resizable workspace whose canvas remains dominant while preserving every existing graph command and behavior.

**Architecture:** `MainWindow.BuildGraphTab()` will compose the existing graph controls into a `Grid`: optional legend pane on the left, graph canvas in the center, optional properties pane on the right, and an expandable drawer below. `GraphView` and all model, validation, runtime, and write paths remain unchanged. Small public test hooks on `MainWindow` will drive the same pane state transitions as the buttons.

**Tech Stack:** C#, .NET 8, Avalonia, existing `tools/uismoke` headless UI harness.

## Global Constraints

- Keep the work on `main`; do not create a long-lived branch or worktree.
- Do not change `GraphView`, HKX parsing, serialization, validation, graph layout, or simulation logic.
- Do not add a machine navigator, diagnostic grouping, toolbar redesign, or placeholder tabs.
- Run the four project gates before committing.
- Stage explicit paths and use a Conventional Commit message.

---

### Task 1: Define graph-workspace behavior in smoke coverage

**Files:**
- Modify: `tools/uismoke/Smoke.cs`
- Test: `tools/uismoke/Smoke.cs`

**Interfaces:**
- Consumes: `MainWindow` after opening a real behaviour file.
- Produces: expected workspace test calls: `GraphLeftPaneOpen`, `GraphRightPaneOpen`, `GraphDrawerOpen`, `GraphCenterWidth`, `SetGraphLeftPaneOpen(bool)`, `SetGraphRightPaneOpen(bool)`, `SetGraphDrawerOpen(bool)`, `ResizeGraphLeftPaneForTest(double)`, `ResizeGraphRightPaneForTest(double)`, and `ResizeGraphDrawerForTest(double)`.

- [x] **Step 1: Write failing smoke checks**

```csharp
CheckTrue("graph workspace starts with the legend pane collapsed", !window.GraphLeftPaneOpen);
CheckTrue("graph workspace starts with properties visible", window.GraphRightPaneOpen);
CheckTrue("graph workspace starts with details collapsed", !window.GraphDrawerOpen);
CheckTrue("graph workspace leaves most width for the canvas", window.GraphCenterWidth >= 900);
```

- [x] **Step 2: Run the smoke harness and verify it fails because the API does not exist**

Run: `dotnet run --project tools/uismoke/uismoke.csproj -- dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx`

Expected: compilation errors naming the missing workspace properties and methods.

### Task 2: Build the Graph workspace shell

**Files:**
- Modify: `app/MainWindow.cs:20-160,330-410,1245-1320`
- Modify: `tools/uismoke/Smoke.cs`

**Interfaces:**
- Consumes: existing `_legend`, `_graph`, `_graphProps`, `_problems`, `_running`, `_runStops`, `_runHeldBack`, `_problemBar`, `BuildRunControls()`, and `BuildPasteControls()`.
- Produces: a `Grid` workspace with a 260px optional left pane, a 320px optional right pane, a center `GraphView` constrained to at least 720px when the window permits, and a 220px bottom drawer collapsed by default.

- [x] **Step 1: Add the failing state-transition checks**

```csharp
window.SetGraphLeftPaneOpen(true);
window.ResizeGraphLeftPaneForTest(300);
CheckTrue("graph workspace can open and resize the legend pane", window.GraphLeftPaneOpen && window.GraphLeftPaneWidth == 300);

window.SetGraphDrawerOpen(true);
window.ResizeGraphDrawerForTest(250);
CheckTrue("graph workspace can open and resize details", window.GraphDrawerOpen && window.GraphDrawerHeight == 250);
```

- [x] **Step 2: Implement the minimal workspace grid**

```csharp
var center = new Grid();
center.ColumnDefinitions.Add(_graphLeftColumn);
center.ColumnDefinitions.Add(_graphLeftSplitterColumn);
center.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = 720 });
center.ColumnDefinitions.Add(_graphRightSplitterColumn);
center.ColumnDefinitions.Add(_graphRightColumn);
```

Use `GridSplitter` controls, real toggle buttons, and the existing controls. Collapsing a pane must set its dimension and splitter to zero without disposing its contents.

- [x] **Step 3: Run focused smoke coverage and verify it passes**

Run: `dotnet run --project tools/uismoke/uismoke.csproj -- dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx`

Expected: all smoke checks pass and existing selected-node inspector checks remain green.

- [x] **Step 4: Render a real workspace image**

Run: `dotnet run --project tools/uismoke/uismoke.csproj -- --png dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx /tmp/bgs-phase1.png --window --fit`

Expected: the graph is present and wider than either side pane at 1600x1000.

### Task 3: Verify, document, and commit Phase 1

**Files:**
- Modify: `app/MainWindow.cs`
- Modify: `tools/uismoke/Smoke.cs`
- Create: `docs/superpowers/plans/2026-08-09-graph-workspace-shell.md`

- [x] **Step 1: Run all project gates**

```bash
dotnet test tools/tests/BehaviourGraph.Tests.csproj
dotnet run --project tools/symrm/symrm.csproj -- test
dotnet run --project tools/uismoke/uismoke.csproj
dotnet run --project tools/uismoke/uismoke.csproj -- dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx
```

- [x] **Step 2: Inspect the diff and mutation-test the canvas-dominance and drawer-visibility assertions**

Temporarily reverse the `GraphCenterMinWidth >= 720` condition and the collapsed-drawer visibility condition in the smoke check. Confirm each focused smoke run fails, restore each condition, and rerun it successfully.

- [x] **Step 3: Commit the verified Phase 1 shell**

```bash
git add app/MainWindow.cs tools/uismoke/Smoke.cs docs/superpowers/plans/2026-08-09-graph-workspace-shell.md
git commit -m "feat(workspace): prioritize graph canvas"
```
