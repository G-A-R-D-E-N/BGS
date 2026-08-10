# Structured Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in Structured Flow mode that renders the existing behaviour graph as a hierarchy-first state-machine overview while preserving Freeform.

**Architecture:** A new pure layout helper in `src/Hkx` derives structural ranks, machine ancestry, and stable sibling order from the existing ownership result. `GraphView` selects that helper only in Structured Flow, renders machine containers behind its existing nodes, and chooses render detail from the current zoom without altering the model. `MainWindow` owns the mode selector and `uismoke` validates both modes and creates 1600x1000 render evidence.

**Tech Stack:** C# 12, .NET 8, Avalonia, existing `GraphAuthor`, `GraphOwnership`, `StateRoutes`, `symrm`, and `uismoke`.

## Global Constraints

- Keep Freeform available and preserve its current placement behavior.
- Do not change parsing, serialization, native writes, `GraphAuthor.PointsAt`, `StateRoutes`, or simulation semantics.
- Use only existing ownership and resolved route data. Never infer labels such as Movement or Combat from names.
- Render actual names and serialized numeric identifiers as `Name #ID`.
- Structured Flow is hierarchy-first. Container boundaries and owner links have stronger normal emphasis than transition routes.
- Far, medium, and close detail bands change only presentation. They do not change model data, ownership, selection, trace traversal, focus, XML, or simulation.
- Static trace and Focus tree remain view-only and work in both modes.
- Do not add ELK, Graphviz, or another layout-engine dependency.
- Write a failing test first for every production behavior and mutation-test assertions used for layout invariants.
- Before every commit run: `dotnet test tools/tests/BehaviourGraph.Tests.csproj`, `dotnet run --project tools/symrm/symrm.csproj -- test`, `dotnet run --project tools/uismoke/uismoke.csproj`, and `dotnet run --project tools/uismoke/uismoke.csproj -- dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx`.
- Stage explicit paths only, use Conventional Commit messages, and push `main` to `gitlab`.

---

### Task 1: Derive a deterministic structured hierarchy

**Files:**
- Create: `src/Hkx/StructuredFlowLayout.cs`
- Modify: `tools/symrm/Tests.cs`

**Interfaces:**
- Consumes: `IReadOnlyList<(HkObject Node, int Column, string OwnerId)>` from `GraphAuthor.Layout`.
- Produces: `StructuredFlowLayout.Plan Of(IReadOnlyList<(HkObject Node, int Column, string OwnerId)> placed)`.
- Produces: `Plan.Items: IReadOnlyDictionary<string, StructuredFlowLayout.Item>` where `Item` contains `Id`, `OwnerId`, `MachineId`, `ParentMachineId`, `Depth`, `SiblingOrder`, `Kind`, and `StructuralAncestorIds`.
- Produces: `Plan.Machines: IReadOnlyList<StructuredFlowLayout.Machine>` where each machine has its real ID, parent machine ID, hierarchy depth, and member IDs in ownership order.

- [ ] **Step 1: Write the failing hierarchy tests**

Add a small in-memory behavior graph to `tools/symrm/Tests.cs` with a root state machine, two direct state infos, a nested state machine reached by one state, and a helper generator below the other. Assert the structural facts rather than renderer implementation:

```csharp
var plan = StructuredFlowLayout.Of(GraphAuthor.Layout(model, 1000));

Check("flow: root machine has no parent", "", plan.Item(rootMachine).ParentMachineId);
Check("flow: nested machine stays in its owner machine", rootMachine,
      plan.Item(nestedMachine).ParentMachineId);
Check("flow: state inherits owning machine", rootMachine, plan.Item(firstState).MachineId);
Check("flow: helper inherits nearest state machine", rootMachine, plan.Item(helper).MachineId);
Check("flow: roots rank above descendants", true,
      plan.Item(rootMachine).Depth < plan.Item(firstState).Depth);
Check("flow: sibling order is deterministic", true,
      plan.Item(firstState).SiblingOrder < plan.Item(secondState).SiblingOrder);
```

- [ ] **Step 2: Run the focused test and verify it fails for the missing API**

Run:

```bash
/home/ricky/.dotnet/dotnet run --project tools/symrm/symrm.csproj -- test
```

Expected: compilation failure because `StructuredFlowLayout` does not exist.

- [ ] **Step 3: Implement the pure layout model**

Create `src/Hkx/StructuredFlowLayout.cs`. Build the ownership tree from the `OwnerId` values emitted by `GraphAuthor.Layout`. Walk ownership roots in source order. For every item, determine:

```csharp
public enum NodeKind { Root, Machine, State, Helper }

public sealed record Item(
    string Id, string OwnerId, string MachineId, string ParentMachineId,
    int Depth, int SiblingOrder, NodeKind Kind,
    IReadOnlyList<string> StructuralAncestorIds);
```

Classify `hkbStateMachine` as `Machine`, `hkbStateMachineStateInfo` as `State`, the graph root as `Root`, and every other node as `Helper`. A machine's `ParentMachineId` is its nearest machine ancestor, excluding itself. A state and helper inherit their nearest machine ancestor. Do not parse names and do not inspect references beyond the existing placed ownership result.

- [ ] **Step 4: Run the focused test and verify it passes**

Run:

```bash
/home/ricky/.dotnet/dotnet run --project tools/symrm/symrm.csproj -- test
```

Expected: the new hierarchy assertions pass.

- [ ] **Step 5: Mutation-test hierarchy ancestry**

Temporarily change the nested machine parent calculation to return the direct owner ID. Re-run `symrm test`.

Expected: the nested-machine parent assertion fails. Restore the correct implementation and confirm `symrm test` passes.

- [ ] **Step 6: Commit the pure layout helper**

Run all four repository gates. Then:

```bash
git add src/Hkx/StructuredFlowLayout.cs tools/symrm/Tests.cs
git commit -m "feat(layout): derive structured flow hierarchy"
git push gitlab main
```

### Task 2: Add Structured Flow placement, containers, and detail bands

**Files:**
- Modify: `app/GraphView.cs`
- Modify: `tools/uismoke/Smoke.cs`

**Interfaces:**
- Consumes: `StructuredFlowLayout.Plan` from Task 1 and the existing node measurements in `GraphView.Show`.
- Produces: `public enum GraphLayoutMode { Freeform, StructuredFlow }`.
- Produces: `public GraphLayoutMode LayoutMode { get; }` and `public void SetLayoutMode(GraphLayoutMode mode)`.
- Produces: `public enum StructuredFlowDetail { Far, Medium, Close }` and `public StructuredFlowDetail DetailLevel { get; }`.
- Produces: `public IReadOnlyCollection<string> StructuredMachineIds`, `public Rect? StructuredContainerBounds(string machineId)`, and `public bool IsDrawnAtCurrentDetail(string id)` for smoke assertions.

- [ ] **Step 1: Write failing GraphView smoke assertions**

In the Dogmeat graph smoke section, add assertions that explicitly set Structured Flow, frame the canvas, and validate its public evidence:

```csharp
canvas.SetLayoutMode(GraphLayoutMode.StructuredFlow);
Check("Structured Flow mode is selected", GraphLayoutMode.StructuredFlow, canvas.LayoutMode);
CheckTrue("Structured Flow creates machine containers",
          canvas.StructuredMachineIds.Count > 0);
CheckTrue("Structured Flow places a machine above its state",
          canvas.PositionOf(machineId)!.Value.Y < canvas.PositionOf(stateId)!.Value.Y);
CheckTrue("Structured Flow bounds its root machine",
          canvas.StructuredContainerBounds(machineId) is { } box && box.Contains(canvas.PositionOf(stateId)!.Value));
```

Add detail-band assertions after setting test zoom levels through a test-only public method:

```csharp
canvas.SetZoomForTest(0.35);
Check("far detail hides helper tiles", StructuredFlowDetail.Far, canvas.DetailLevel);
CheckTrue("far detail retains machine tiles", canvas.IsDrawnAtCurrentDetail(machineId));
CheckTrue("far detail suppresses helper tiles", !canvas.IsDrawnAtCurrentDetail(helperId));
canvas.SetZoomForTest(1.20);
Check("close detail reveals helpers", StructuredFlowDetail.Close, canvas.DetailLevel);
CheckTrue("close detail draws helper tiles", canvas.IsDrawnAtCurrentDetail(helperId));
```

- [ ] **Step 2: Run Dogmeat smoke and verify it fails for the missing mode API**

Run:

```bash
/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj -- dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx
```

Expected: compilation failure because `GraphLayoutMode`, `SetLayoutMode`, and Structured Flow evidence APIs do not exist.

- [ ] **Step 3: Implement mode-specific placement**

Keep the existing Freeform branch byte-for-byte equivalent in behavior. In Structured Flow, use `StructuredFlowLayout.Plan` to calculate a top-to-bottom hierarchy:

```csharp
double y = item.Depth * StructuredRowGap;
double x = item.SiblingOrder * StructuredColumnGap;
```

Replace the simplistic coordinate formula with a subtree-width pass that centers a parent over its visible structural children. Structural children are machines and state infos. Helpers inherit the selected state branch and receive stable close-detail positions beneath that state. Preserve existing drag pins only for the active layout mode, so a Freeform drag never corrupts Structured Flow coordinates.

- [ ] **Step 4: Implement machine-container rendering and subordinate routes**

Before drawing ownership links and nodes, draw a clipped, translucent container for each visible state machine. Derive its rectangle from the union of the machine header, its visible direct states, and its nested machine containers. Draw nested containers after their parent background so parent boundaries remain visible.

Use the actual `HeaderTextOf(machineId)` for the container title. Draw container boundaries and ownership links with higher opacity and width than unselected transition routes. Reuse the current route and wildcard drawing policy. Do not add routes or alter `StateRoutes`.

- [ ] **Step 5: Implement progressive disclosure**

Add a single detail calculation based on `_zoom`:

```csharp
private StructuredFlowDetail CurrentDetail() => _zoom < 0.50 ? StructuredFlowDetail.Far
    : _zoom < 1.05 ? StructuredFlowDetail.Medium
    : StructuredFlowDetail.Close;
```

In Structured Flow:

- Far draws machines and their containers only.
- Medium draws machines and direct state infos.
- Close draws all existing nodes.
- A selected, traced, or Focus-tree ancestor branch may draw its owned helpers at Medium, but this affects only rendering. `_nodes`, `DrawnIds`, selection, and trace traversal remain complete.

Ensure every draw path uses the same `IsDrawnAtCurrentDetail` predicate, including node painting, ownership links, routes, hit tests, and container bounds. Keep the canvas clipped.

- [ ] **Step 6: Run smoke and verify it passes**

Run the Dogmeat smoke command from Step 2. Expected: the new Structured Flow, containment, and detail assertions pass while existing graph tests remain green.

- [ ] **Step 7: Mutation-test the detail predicate**

Temporarily return `Close` for all zoom levels. Re-run Dogmeat smoke.

Expected: the far-detail helper suppression assertion fails. Restore the predicate and re-run the Dogmeat smoke successfully.

- [ ] **Step 8: Commit rendering behavior**

Run all four repository gates. Then:

```bash
git add app/GraphView.cs tools/uismoke/Smoke.cs
git commit -m "feat(graph): add structured flow view"
git push gitlab main
```

### Task 3: Expose the view selector without changing workspace architecture

**Files:**
- Modify: `app/MainWindow.cs`
- Modify: `tools/uismoke/Smoke.cs`

**Interfaces:**
- Consumes: `GraphView.SetLayoutMode(GraphLayoutMode mode)`.
- Produces: `public GraphLayoutMode GraphLayoutModeForTest { get; }` and `public void SetGraphLayoutModeForTest(GraphLayoutMode mode)`.
- Produces: View-menu actions labelled `Freeform` and `Structured Flow` with the active mode visibly indicated.

- [ ] **Step 1: Write failing control-surface smoke assertions**

Add a Graph workspace smoke assertion that invokes the public test helper and validates both choices:

```csharp
window.SetGraphLayoutModeForTest(GraphLayoutMode.StructuredFlow);
Check("Graph view exposes Structured Flow", GraphLayoutMode.StructuredFlow,
      window.GraphLayoutModeForTest);
window.SetGraphLayoutModeForTest(GraphLayoutMode.Freeform);
Check("Graph view restores Freeform", GraphLayoutMode.Freeform,
      window.GraphLayoutModeForTest);
```

Verify a reload preserves the selected mode, while `Show full graph` clears only Focus tree and leaves the layout mode unchanged.

- [ ] **Step 2: Run default UI smoke and verify it fails for the missing helper**

Run:

```bash
/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj
```

Expected: compilation failure because the MainWindow mode helper does not exist.

- [ ] **Step 3: Implement the View-menu selector**

Add Freeform and Structured Flow actions to the existing `View ▾` menu. Update their headers before opening the menu so the active mode reads as checked or marked active. Do not add a permanent toolbar button and do not touch Workspace, Legend, diagnostics, Runtime, or the Tree tab.

Make mode changes call `_graph.SetLayoutMode(mode)`, frame the active layout, and refresh the current graph only through the existing `GraphView.Show` path. Provide the small test-only MainWindow accessors needed by `uismoke`.

- [ ] **Step 4: Run smoke and verify it passes**

Run the default UI smoke command from Step 2. Expected: both mode-toggle assertions and existing workspace tests pass.

- [ ] **Step 5: Commit the selector**

Run all four repository gates. Then:

```bash
git add app/MainWindow.cs tools/uismoke/Smoke.cs
git commit -m "feat(graph): expose structured flow mode"
git push gitlab main
```

### Task 4: Render the side-by-side Dogmeat comparison and complete verification

**Files:**
- Modify: `tools/uismoke/Smoke.cs` only if the existing `--png` path needs a `--structured-flow` switch.
- Create, uncommitted: `/tmp/bgs-dogmeat-freeform-1600x1000.png`
- Create, uncommitted: `/tmp/bgs-dogmeat-structured-flow-1600x1000.png`

**Interfaces:**
- Consumes: Graph layout mode test helper from Task 3 and the existing `uismoke --png` renderer.
- Produces: two 1600x1000 local evidence images for the same Dogmeat file.

- [ ] **Step 1: Write a failing PNG-mode smoke argument assertion**

If `tools/uismoke --png` cannot select Structured Flow, add a command-level test or a clear return-code assertion for:

```bash
/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj -- --png   dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx /tmp/bgs-dogmeat-structured-flow-1600x1000.png   --whole --structured-flow
```

Expected before implementation: the argument is rejected or produces the Freeform image, proving the selector plumbing is missing.

- [ ] **Step 2: Implement the smallest PNG selector needed**

Parse `--structured-flow` only in the existing PNG path. Set the mode through the same public MainWindow method used by smoke before rendering. Keep Freeform as the default and do not introduce a new export format.

- [ ] **Step 3: Render and inspect both 1600x1000 layouts**

Run:

```bash
/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj -- --png \
  dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx /tmp/bgs-dogmeat-freeform-1600x1000.png --whole
/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj -- --png \
  dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx /tmp/bgs-dogmeat-structured-flow-1600x1000.png \
  --whole --structured-flow
```

Inspect both images. Confirm Freeform remains a raw dependency view and Structured Flow makes machine containers, hierarchy, and state groups readable while keeping transitions secondary. Do not claim this is manual GUI verification.

- [ ] **Step 4: Run final gates and inspect repository state**

Run all four required gates. Then:

```bash
git status --short --branch
git log --oneline --decorate -5
git ls-remote gitlab refs/heads/main
```

Expected: all gates pass, `main` matches GitLab, and the two render files are outside the repository.

- [ ] **Step 5: Commit PNG selector only if it changed**

If Task 4 changed `tools/uismoke/Smoke.cs`, stage it explicitly and commit:

```bash
git add tools/uismoke/Smoke.cs
git commit -m "test(graph): render structured flow evidence"
git push gitlab main
```

