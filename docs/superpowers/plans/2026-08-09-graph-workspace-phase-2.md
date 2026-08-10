# Graph Workspace Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add machine-focused navigation, explicit State Machine Tree focus, static dependency tracing, numeric object-ID chips, and active-machine indicators without changing graph data or simulation behavior.

**Architecture:** Keep static traversal as a small model-side helper that reads existing canvas relationships and resolved StateRoutes without modifying GraphAuthor.PointsAt. Keep all view-only state in GraphView, and let MainWindow own the navigator controls and GraphRun-to-navigator presentation update.

**Tech Stack:** .NET 8, Avalonia, existing Hkx graph model, StateRoutes, GraphOwnership, GraphView, symrm, and uismoke.

## Global Constraints

- Do not modify `GraphAuthor.PointsAt` or its deliberate omission policy.
- Do not modify parser, serialization, native-write, or simulation-semantics code.
- Keep the Tree tab intact and do not turn the navigator into a full object tree.
- Focus tree changes GraphView visibility only. It must not mutate XML, routes, pinned positions, undo state, or GraphRun state.
- Static tracing remains independent of selection and must never alter GraphRun state.
- Trace only existing ownership/reference relationships and resolved StateRoutes.
- With Focus tree active, trace traversal is limited to currently visible nodes.
- Render supported serialized numeric IDs as `#` plus the existing parsed numeric ID. Do not claim literal-ID support.
- Do not redesign diagnostics, the Runtime window, or the Phase 1 toolbar.
- Preserve concurrent bone-array edits. Stage only files owned by the Phase 2 task.
- Before every commit, run:
  - `/home/ricky/.dotnet/dotnet test tools/tests/BehaviourGraph.Tests.csproj`
  - `/home/ricky/.dotnet/dotnet run --project tools/symrm/symrm.csproj -- test`
  - `/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj`
  - `/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj -- dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx`

---

### Task 1: Build the view-only dependency traversal

**Files:**
- Create: `src/Hkx/GraphTrace.cs`
- Modify: `tools/symrm/Tests.cs`

**Interfaces:**
- Consumes: `BehaviourGraphModel`, `StateRoutes`, and existing `GraphAuthor.PointsAt`.
- Produces: `GraphTrace.Of(BehaviourGraphModel, StateRoutes)` and
  `GraphTrace.Reachable(string seedId, Direction direction, IReadOnlySet<string> visible)`.
- Preserves: `GraphAuthor.PointsAt` source and behavior.

- [ ] **Step 1: Write the failing symrm regression**

Add a test graph with ownership/reference links and two resolved transition routes. Add the intended assertions:

```csharp
var trace = GraphTrace.Of(model, StateRoutes.Of(model));
var visible = model.Objects.Select(o => o.Id).ToHashSet();

Check("upstream trace follows references and routes", "1,2,3",
    string.Join(",", trace.Reachable("3", GraphTrace.Direction.Upstream, visible).OrderBy(id => id)));
Check("downstream trace follows references and routes", "3,4,5",
    string.Join(",", trace.Reachable("3", GraphTrace.Direction.Downstream, visible).OrderBy(id => id)));
Check("focused trace cannot escape its visible tree", "3,4",
    string.Join(",", trace.Reachable("3", GraphTrace.Direction.Both,
        new HashSet<string> { "3", "4" }).OrderBy(id => id)));
```

The graph must include a cycle so the expected set also proves traversal terminates.

- [ ] **Step 2: Run the failing regression**

Run:

```bash
/home/ricky/.dotnet/dotnet run --project tools/symrm/symrm.csproj -- test
```

Expected: compilation failure because `GraphTrace` does not exist.

- [ ] **Step 3: Implement the smallest traversal helper**

Create `GraphTrace.cs` with the exact public shape:

```csharp
public static class GraphTrace
{
    public enum Direction { Upstream, Downstream, Both }

    public static GraphTraceMap Of(BehaviourGraphModel model, StateRoutes routes) => ...;

    public sealed class GraphTraceMap
    {
        public IReadOnlyCollection<string> Reachable(
            string seedId, Direction direction, IReadOnlySet<string> visible) => ...;
    }
}
```

Build forward and reverse adjacency sets from existing `GraphAuthor.PointsAt(model, object)` results plus resolved `StateRoutes.Routes`. Include `IntoId` only when it is nonempty. Filter both frontier and results through `visible`. Use a `HashSet<string>` visited set so cycles cannot loop. Always include a visible seed.

- [ ] **Step 4: Run the regression green**

Run the same `symrm test` command.

Expected: the new upstream, downstream, both-direction, focus-scope, and cycle assertions pass.

- [ ] **Step 5: Mutation-test the traversal assertion**

Temporarily change the expected downstream set to omit one reachable ID. Run `symrm test` and verify the new assertion fails for that missing ID. Restore the exact expected set and rerun green.

- [ ] **Step 6: Commit the isolated traversal**

Stage only `src/Hkx/GraphTrace.cs` and `tools/symrm/Tests.cs` after resolving any concurrent-edit ownership with the user. Run all four repository gates. Commit:

```bash
git add src/Hkx/GraphTrace.cs tools/symrm/Tests.cs
git commit -m "feat(trace): resolve graph dependencies"
git push gitlab main
```

### Task 2: Add GraphView focus, trace, and identifier presentation

**Files:**
- Modify: `app/GraphView.cs`
- Modify: `tools/uismoke/Smoke.cs`

**Interfaces:**
- Consumes: `GraphTrace.GraphTraceMap` from Task 1 and the existing `GraphOwnership.Tree`.
- Produces: `SetFocusTree`, `ClearFocusTree`, `Trace`, `ClearTrace`, and read-only smoke hooks.
- Preserves: selected node when clearing a trace.

- [ ] **Step 1: Write failing UI smoke assertions**

After loading Dogmeat, add smoke checks that call the intended hooks:

```csharp
canvas.SetFocusTree(machineId);
CheckTrue($"{name}: focus hides nodes outside its machine tree",
    canvas.FocusTreeActive && canvas.DrawnCount < drawnBefore);
Check($"{name}: focus does not change XML", xmlBefore, window.LoadedXml);

canvas.Trace(GraphTrace.Direction.Both);
CheckTrue($"{name}: trace keeps the selected seed", canvas.TraceIds.Contains(machineId));
CheckTrue($"{name}: trace dims unrelated visible nodes",
    canvas.DrawnIds.Any(id => !canvas.TraceIds.Contains(id) && canvas.IsDimmed(id)));

canvas.ClearTrace();
Check($"{name}: clearing trace keeps the selection", machineId, canvas.SelectedId);
CheckTrue($"{name}: clearing trace restores normal emphasis",
    !canvas.TraceActive && !canvas.IsTraceDimmed(unrelatedId));
```

Also render a focused node and assert its formatted node-header text contains the expected `#` numeric ID.

- [ ] **Step 2: Run the failing UI smoke test**

Run:

```bash
/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj -- dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx
```

Expected: compilation failure because the focus and trace APIs do not exist.

- [ ] **Step 3: Implement focused visibility without data mutation**

Add private view-only fields for `_focusTreeRootId`, `_traceIds`, and trace-edge membership. During `Show(model)`, build ownership from the full layout first, then filter `showing` to the selected root plus `_own.Under(root)` when focus is active. Never alter the model, XML, routes, collapsed collection, or pinned positions.

Expose:

```csharp
public bool SetFocusTree(string machineId);
public void ClearFocusTree();
public bool FocusTreeActive { get; }
public string FocusTreeRootId { get; }
```

Return false without changing the view when the ID is absent or is not an `hkbStateMachine`. A machine with no owned descendants remains a valid one-node focus.

- [ ] **Step 4: Implement static trace rendering**

Create the trace map from the loaded model and existing `_routes`. Expose:

```csharp
public bool Trace(GraphTrace.Direction direction);
public void ClearTrace();
public bool TraceActive { get; }
public IReadOnlyCollection<string> TraceIds { get; }
public string HeaderTextOf(string id);
```

Use `_nodes.Keys` as the visible scope. Frame the trace result when tracing succeeds. Extend dimming and wire lighting so trace nodes and trace edges are strong, non-trace visible nodes are dim, active-state rings and validation styling remain visible, and clearing only removes trace emphasis.

- [ ] **Step 5: Render numeric object-ID chips**

Reserve the right side of the existing 22-pixel node header for a compact chip. Draw the node name in the remaining width and draw `#` plus `node.Id` right-aligned. Do not change IDs, references, parser behavior, or node ordering.

- [ ] **Step 6: Run the UI smoke test green**

Run the Dogmeat smoke command from Step 2. Confirm focus, trace direction, focused trace scope, selection preservation, XML preservation, and ID-chip assertions pass.

- [ ] **Step 7: Mutation-test visibility isolation**

Temporarily omit the visible-scope filter from `Trace`. The focused-trace smoke assertion must fail because an out-of-focus node appears in the trace. Restore the filter and rerun green.

- [ ] **Step 8: Commit GraphView behavior**

Stage only `app/GraphView.cs` and the owned `tools/uismoke/Smoke.cs` hunks after resolving concurrent ownership. Run all four repository gates. Commit:

```bash
git add app/GraphView.cs tools/uismoke/Smoke.cs
git commit -m "feat(graph): add focus and static trace"
git push gitlab main
```

### Task 3: Build the Machine Navigator and Legend swap

**Files:**
- Modify: `app/MainWindow.cs`
- Modify: `tools/uismoke/Smoke.cs`

**Interfaces:**
- Consumes: `GraphView.SetFocusTree`, `GraphView.ClearFocusTree`, `GraphView.Trace`, and the current `GraphRun.Where()` result.
- Produces: a machine-only navigator whose rows select and frame machines, plus explicit focus and trace controls.
- Preserves: Tree tab, left-pane resize/collapse behavior, and the existing Legend content.

- [ ] **Step 1: Write failing navigator smoke assertions**

Add checks after a real graph loads:

```csharp
Check("Machines is the default left-pane view", "Machines", window.GraphLeftPaneView);
CheckTrue($"{name}: navigator contains only state machines",
    window.MachineNavigatorIds.All(id => model.Get(id)?.Class == "hkbStateMachine"));
CheckTrue($"{name}: navigator labels serialize numeric IDs",
    window.MachineNavigatorLabels.All(text => text.Contains("#")));

window.SelectMachineForTest(machineId);
Check($"{name}: navigator selection reaches the inspector", machineId, window.SelectedObjectId);
CheckTrue($"{name}: navigator selection does not enable focus", !window.GraphFocusTreeActive);

window.ShowLegendForTest();
Check("Legend swaps into the navigator pane", "Legend", window.GraphLeftPaneView);
window.ShowMachinesForTest();
Check("Machines returns after the legend closes", "Machines", window.GraphLeftPaneView);
```

Start a real `GraphRun` and assert at least one navigator row becomes active. Stop or clear the run and assert its active indicator disappears.

- [ ] **Step 2: Run the failing UI smoke test**

Run:

```bash
/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj -- dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx
```

Expected: compilation failure because the navigator state and test hooks do not exist.

- [ ] **Step 3: Implement a machine-only left-pane host**

Replace the left-pane direct Legend placement with one framed host containing exactly one child at a time: Machines or Legend. Populate Machines from `Model().Objects.Where(o => o.Class == "hkbStateMachine")`. Each row has display name, `#` plus the object ID, and a presentation-only active marker.

Selection must call the existing canvas/inspector selection route, apply the existing one-hop frame behavior, and leave focus and trace unchanged.

- [ ] **Step 4: Implement explicit controls**

Place `Focus tree`, `Show full graph`, `Upstream`, `Downstream`, `Both`, and `Clear trace` under the machine list. Enable focus only for a selected machine. Enable trace only for a selected visible node. Keep the controls out of the Phase 1 toolbar and the diagnostics drawer.

Wire `Focus tree` to GraphView only. Wire `Show full graph` to clear GraphView focus. Wire trace buttons to GraphView only. Clear trace must not call `SelectObjectId` or clear the inspector.

- [ ] **Step 5: Refresh active-machine indicators**

In the existing `RefreshRun` path, derive active non-fading machine IDs from `_run.Where()` and refresh navigator presentation. On file load without a runnable graph and when the run is cleared, refresh with an empty set. Do not add runtime history or alter GraphRun.

- [ ] **Step 6: Run navigator smoke green**

Run the Dogmeat smoke command from Step 2. Confirm default Machines view, Legend swap, machine-only row source, navigation selection/frame, explicit focus, explicit full-graph restore, and active indicators all pass.

- [ ] **Step 7: Mutation-test explicit focus**

Temporarily call `SetFocusTree` inside navigator selection. The smoke assertion that navigator selection does not enable focus must fail. Remove that call and rerun green.

- [ ] **Step 8: Commit navigator integration**

Stage only `app/MainWindow.cs` and the owned `tools/uismoke/Smoke.cs` hunks after resolving concurrent ownership. Run all four repository gates. Commit:

```bash
git add app/MainWindow.cs tools/uismoke/Smoke.cs
git commit -m "feat(navigator): add machine workspace"
git push gitlab main
```

### Task 4: Render review and final Phase 2 verification

**Files:**
- Modify: `tools/uismoke/Smoke.cs` only if a missing assertion is discovered.

**Interfaces:**
- Consumes: all Phase 2 public view and window interfaces.
- Produces: visual evidence at 1600 by 1000 and final automated-gate evidence.

- [ ] **Step 1: Generate the full-workspace render**

Run:

```bash
/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj -- --png --window --details --check --event dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx /tmp/bgs-phase2-1600.png
```

Inspect that Machines is bounded to the left pane, the graph dominates the center, node IDs are readable, focus controls are explicit, and the diagnostics drawer remains independent.

- [ ] **Step 2: Generate focus and trace renders**

Add test-first PNG options `--focus-tree <machine-id>` and `--trace <upstream|downstream|both> <node-id>`
to the existing uismoke renderer. They must call the same GraphView APIs used by the real navigator
buttons, never private state. Render both views with these exact commands, substituting IDs read
from the loaded Dogmeat graph:

```bash
/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj -- --png --window --focus-tree <machine-id> dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx /tmp/bgs-phase2-focus.png
/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj -- --png --window --trace both <node-id> dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx /tmp/bgs-phase2-trace.png
```

Assert in uismoke that each flag produces the corresponding Focus tree or static-trace state before
writing its image.

- [ ] **Step 3: Run all final gates**

Run all four commands from Global Constraints and record their exact counts.

- [ ] **Step 4: Verify commit scope**

Run:

```bash
git diff --check
git status --short --branch
git log --oneline --decorate -5
```

Confirm no parser, serialization, Runtime-window, diagnostics, or `GraphAuthor.PointsAt` change is included in Phase 2.

- [ ] **Step 5: Commit and push final review fixes**

If Task 4 produced an owned smoke-only change, stage it explicitly, rerun all four gates, commit with a Conventional Commit subject under 50 characters, and push `main`. Otherwise do not create an empty commit.
