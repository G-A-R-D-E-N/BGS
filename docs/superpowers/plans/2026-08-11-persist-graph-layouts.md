# Persist Graph Layouts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist user-dragged Freeform node positions per HKX file in local settings and restore them when that file is reopened.

**Architecture:** `Settings` owns a small path-keyed codec using its atomic writer. `GraphView` accepts restored Freeform positions and signals one completed-drag change. `MainWindow` restores before `Show` and saves the current snapshot after the signal.

**Tech Stack:** C#/.NET, Avalonia, BehaviourGraphStudio UI smoke harness.

## Global Constraints

- Store positions only in the existing user settings file. Never alter HKX bytes.
- Normalize keys with `Path.GetFullPath`; apply case-insensitive key normalization only on Windows.
- Keep layouts isolated by input path and ignore saved IDs absent from the open graph.
- Use the existing atomic temporary-file write path for every settings update.
- Invalid or unavailable preferences must leave automatic placement available and never block opening a file.
- Save only completed Freeform drags. Structured Flow is derived and is never persisted.
- Preserve graph ownership movement for every node changed by a drag.
- Do not alter untracked `.freebuff/` content.

---

## File Structure

- Modify `app/Settings.cs`: path-key derivation, invariant layout encoding and safe settings API.
- Modify `app/GraphView.cs`: restore input, placement snapshot and completed-drag notification.
- Modify `app/MainWindow.cs`: active-file restore and save wiring.
- Modify `tools/uismoke/Smoke.cs`: fresh-window persistence regression.

### Task 1: Add the layout contract and make the user regression fail

**Files:**

- Modify: `app/Settings.cs:1-104`
- Modify: `app/GraphView.cs:67-90, 332-390, 754-760, 846-855, 1330-1350, 1486-1505`
- Test: `tools/uismoke/Smoke.cs:near existing GraphView drag coverage`

**Interfaces:**

- Consumes: `Settings.TrySet(string, string, out string)`, `GraphView._placed`, and `GraphView.DragForTest(string, double, double)`.
- Produces: `Settings.GetGraphLayout(string) : IReadOnlyDictionary<string, LayoutPoint>`, `Settings.TrySetGraphLayout(string, IReadOnlyDictionary<string, LayoutPoint>, out string) : bool`, `GraphView.RestoreFreeformPositions(IReadOnlyDictionary<string, LayoutPoint>) : void`, `GraphView.SnapshotFreeformPositions() : IReadOnlyDictionary<string, LayoutPoint>`, and `GraphView.LayoutChanged : event Action`.

- [ ] **Step 1: Write the failing fresh-window smoke regression**

Add this test method beside existing GraphView smoke tests and call it from smoke-harness `Main`:

```csharp
private static void GraphLayoutPersistsAcrossFreshWindow()
{
    var path = Path.Combine(Path.GetTempPath(), $"bgs-layout-{Guid.NewGuid():N}.hkx");
    File.WriteAllBytes(path, OneClipBytes());
    try
    {
        var first = new MainWindow(); first.Show(); first.Open(path); RunJobs();
        const string id = "90";
        first.Canvas.DragForTest(id, 73, -29); RunJobs();
        var moved = first.Canvas.PositionOf(id)!.Value;
        var second = new MainWindow(); second.Show(); second.Open(path); RunJobs();
        Check("graph layout survives fresh-window reload", moved, second.Canvas.PositionOf(id)!.Value);
        second.Close(); first.Close();
    }
    finally { File.Delete(path); }
}
```

Use the existing synthetic fixture's actual node ID. The second window must open the same full path and compare the exact dragged `Point`.

- [ ] **Step 2: Verify the regression fails before production changes**

Run: `/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj`

Expected: `graph layout survives fresh-window reload` fails because the second window receives automatic coordinates.

- [ ] **Step 3: Define the Settings codec**

Add this independent value type and API to `Settings`:

```csharp
public readonly record struct LayoutPoint(double X, double Y);
public static IReadOnlyDictionary<string, LayoutPoint> GetGraphLayout(string path);
public static bool TrySetGraphLayout(string path, IReadOnlyDictionary<string, LayoutPoint> positions, out string failure);
```

Build the key as `graph-layout.` plus SHA-256 hexadecimal of `Path.GetFullPath(path)`, uppercased only under `OperatingSystem.IsWindows()`. Serialize ordinal-sorted IDs as URL-escaped `id,x,y` records separated by `|`, using `R` and `CultureInfo.InvariantCulture` for coordinates. Omit empty IDs and non-finite values. The reader returns an empty dictionary for missing data and skips malformed, non-finite and duplicate records. The writer delegates to existing `TrySet`.

- [ ] **Step 4: Define the GraphView contract**

Add:

```csharp
public event Action? LayoutChanged;
public void RestoreFreeformPositions(IReadOnlyDictionary<string, LayoutPoint> positions);
public IReadOnlyDictionary<string, LayoutPoint> SnapshotFreeformPositions();
```

`RestoreFreeformPositions` clears `_placed` and copies finite values as `Point` before `Show`. `SnapshotFreeformPositions` returns a new dictionary. Keep `Move` responsible for all ownership-related `_placed` updates and make it report whether anything moved. `DragForTest` sends one event after a successful Freeform move. Pointer movement marks an active changed drag; `OnPointerReleased` sends one event only for that changed Freeform drag. A click, no movement, and Structured Flow send nothing.

- [ ] **Step 5: Verify compilation succeeds but behavior remains red**

Run: `/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj`

Expected: the project compiles, but the fresh-window test still fails until `MainWindow` is wired.

- [ ] **Step 6: Commit the contract layer**

Run: `git add app/Settings.cs app/GraphView.cs tools/uismoke/Smoke.cs && git commit -m "feat(layout): add graph layout contract" -m "Add path-keyed settings serialization and graph drag notifications.\n\nRefs: #55"`

### Task 2: Wire persisted state to the active document

**Files:**

- Modify: `app/MainWindow.cs:constructor setup, 3370-3385, 3624-3655`
- Modify: `tools/uismoke/Smoke.cs:GraphLayoutPersistsAcrossFreshWindow`
- Test: `tools/uismoke/Smoke.cs:GraphLayoutPersistsAcrossFreshWindow`

**Interfaces:**

- Consumes: the `Settings` and `GraphView` methods from Task 1.
- Produces: per-file restore before rendering, post-drag safe write, and concise save-failure status.

- [ ] **Step 1: Preserve the end-to-end test unchanged as the red test**

Confirm it opens shown first and second windows for the same file, does a completed `DragForTest`, and compares exact coordinates. Do not weaken the assertion or share a `GraphView` between windows.

- [ ] **Step 2: Confirm the exact current failure**

Run: `/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj`

Expected: only the new fresh-window persistence assertion is unresolved.

- [ ] **Step 3: Wire MainWindow restore and save**

Subscribe once during construction:

```csharp
_graph.LayoutChanged += SaveCurrentGraphLayout;
```

Immediately before `_graph.Show(model)` in `PrepareEditing`, restore the active path:

```csharp
_graph.RestoreFreeformPositions(Settings.GetGraphLayout(_hkxPath));
_graph.Show(model);
```

Add `SaveCurrentGraphLayout` beside existing setting helpers. It returns for empty `_hkxPath` or a non-Freeform `GraphLayoutMode`. Otherwise call `Settings.TrySetGraphLayout(_hkxPath, _graph.SnapshotFreeformPositions(), out var failure)`. On failure use existing concise status or warning behavior with `Could not save graph layout: {failure}`. Never throw, fail loading, alter HKX bytes, or save Structured Flow state.

- [ ] **Step 4: Verify the original symptom is fixed**

Run: `/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj`

Expected: exit code 0 and the second window's node position exactly matches the dragged location.

- [ ] **Step 5: Run the required repository gates**

Run: `/home/ricky/.dotnet/dotnet build app/app.csproj --no-restore`

Run: `/home/ricky/.dotnet/dotnet test --no-restore`

Run: `/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj`

Expected: every command exits 0. Read complete output and separate any baseline failure from branch-introduced failure.

- [ ] **Step 6: Review and commit only the persisted-layout scope**

Run: `git diff --check main...HEAD`

Run: `git diff -- app/MainWindow.cs app/GraphView.cs app/Settings.cs tools/uismoke/Smoke.cs`

Run: `git status --short`

Confirm the diff contains only local settings persistence and regression coverage. Do not stage `.freebuff/`.

Run: `git add app/MainWindow.cs app/GraphView.cs app/Settings.cs tools/uismoke/Smoke.cs && git commit -m "fix(layout): persist freeform positions" -m "Restore path-keyed graph positions before rendering and save them after a completed Freeform drag.\n\nFixes: #55"`

### Task 3: Preserve and review the approved scope

**Files:**

- Modify: `docs/superpowers/specs/2026-08-11-persist-graph-layouts-design.md`
- Create: `docs/superpowers/plans/2026-08-11-persist-graph-layouts.md`

**Interfaces:**

- Consumes: the approved persistence decision.
- Produces: versioned design and execution records for issue #55.

- [ ] **Step 1: Check design boundaries against the plan**

Run: `rg -n "settings|full path|drag|Structured Flow|HKX|sidecar" docs/superpowers/specs/2026-08-11-persist-graph-layouts-design.md docs/superpowers/plans/2026-08-11-persist-graph-layouts.md`

Expected: both documents cover settings storage, full-path keying, completed drags, no HKX changes, no sidecar, and no Structured Flow persistence.

- [ ] **Step 2: Commit the reviewed plan record**

Run: `git add docs/superpowers/specs/2026-08-11-persist-graph-layouts-design.md docs/superpowers/plans/2026-08-11-persist-graph-layouts.md && git commit -m "docs(layout): plan graph persistence" -m "Record the approved local Freeform layout persistence design and execution plan.\n\nRefs: #55"`
