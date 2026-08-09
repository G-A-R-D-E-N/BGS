# Graph ownership, layout, collapse and multiple selection: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the canvas one layout owner per node, place children beside the parent that owns them, and add collapse, marquee and group drag on top of that one rule.

**Architecture:** The walk in `GraphAuthor.Layout` already decides which parent reaches a node first; it starts returning that as `OwnerId`. Two new pure classes in `src/Hkx` hold everything derived from it: `GraphOwnership` answers who owns what, what a collapse hides and what a drag moves, and `GraphLayout` turns owners plus node heights into a Y per node. `GraphView` stops computing positions and consumes both, so the logic is testable without Avalonia and the view keeps only drawing and input.

**Tech Stack:** .NET 8, C#, Avalonia for the canvas, the existing console test runner in `tools/symrm/Tests.cs`, `tools/uismoke/Smoke.cs` for window behaviour.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-09-graph-selection-collapse-and-layout-design.md`. Read it before Task 1.
- `OwnerId` from `GraphAuthor.Layout` is the only source of ownership. No task may compute ownership another way.
- Commit messages: plain professional English, no em dashes or en dashes. Standing rule on this repo.
- Comments explain why, not what. Match the density and tone of the file being edited.
- Run `dotnet run --project tools/symrm/symrm.csproj -- test` before every commit. It must end `0 failed`.
- Branch is `feat/graph-selection-and-layout`, cut from `main` at `df076f8`. Do not merge to main.
- A sibling group is never split by collision resolution. Extra whitespace is preferred to a split.
- A node in `_placed`, meaning the user dragged it, is never moved by automatic layout.

---

### Task 1: `GraphAuthor.Layout` reports who owns each node

**Files:**
- Modify: `src/Hkx/GraphAuthor.cs:212-259`
- Test: `tools/symrm/Tests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `GraphAuthor.Layout(BehaviourGraphModel model, int max)` returning
  `List<(HkObject Node, int Column, string OwnerId)>`. `OwnerId` is `""` for a walk root and
  otherwise the id of the node that first reached it.

- [ ] **Step 1: Write the failing test**

Add to the `Cases` array in `tools/symrm/Tests.cs`, keeping it in file order with the others:

```csharp
("EveryDrawnNodeHasOneOwner", EveryDrawnNodeHasOneOwner),
```

And the case itself, next to the other graph cases:

```csharp
// Ownership is the rule the whole canvas hangs off: where a node is placed, whether a collapse
// hides it, and whether a drag moves it. It is not a new idea, it is a fact the walk already knew
// and threw away, so this pins it down before anything is built on it.
private static void EveryDrawnNodeHasOneOwner()
{
    Console.WriteLine("\nevery drawn node has one owner");

    var model = BehaviourGraphModel.Parse(BlenderGraph(0, 0, 1, 1));
    var placed = GraphAuthor.Layout(model, 1000);

    Check("the walk placed the whole graph", 7, placed.Count);

    var owner = placed.ToDictionary(p => p.Node.Id, p => p.OwnerId);
    Check("the root owns nothing above it", "", owner["91"]);
    Check("the blender is owned by the graph that names it", "91", owner["110"]);
    Check("and a blender child by the blender", "110", owner["111"]);

    // Every node bar a walk root has exactly one owner, and following owners always ends.
    foreach (var (node, _, ownerId) in placed)
    {
        if (ownerId.Length == 0) continue;
        CheckTrue($"#{node.Id}'s owner is itself drawn", owner.ContainsKey(ownerId));

        var seen = new HashSet<string>();
        string at = node.Id;
        while (owner.TryGetValue(at, out string? up) && up.Length > 0)
        {
            CheckTrue($"#{node.Id}'s owner chain does not loop", seen.Add(up));
            at = up;
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project tools/symrm/symrm.csproj -- test 2>&1 | grep -A3 "every drawn node"`
Expected: a compile error, because `Layout` returns a two element tuple and the test destructures three.

- [ ] **Step 3: Write minimal implementation**

In `src/Hkx/GraphAuthor.cs`, change both methods. `Layout`:

```csharp
    /// Every node the canvas will draw, with its depth from the root and the node that reached it
    /// first.
    ///
    /// That last value is what makes the canvas work rather than a detail of the walk. A node can be
    /// pointed at by several parents, and the picture has to put it in one place, hide it under one
    /// collapse and move it with one drag. The walk already decides which parent gets there first,
    /// because it skips a node it has placed; this stops throwing that answer away.
    public static List<(HkObject Node, int Column, string OwnerId)> Layout(BehaviourGraphModel model, int max)
    {
        var placed = new Dictionary<string, int>();
        var order = new List<(HkObject, int, string)>();

        var root = model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraph")
                   ?? model.Objects.FirstOrDefault(o => o.Class == "hkbStateMachine")
                   ?? model.Objects.FirstOrDefault();
        if (root == null) return order;

        int deepest = Walk(model, root, 0, placed, order, max);

        foreach (var detached in Unattached(model))
        {
            if (order.Count >= max) break;
            if (placed.ContainsKey(detached.Id)) continue;
            deepest = Math.Max(deepest, Walk(model, detached, deepest + 1, placed, order, max));
        }

        return order;
    }
```

And `Walk`, which now records the parent it dequeued rather than only the depth:

```csharp
    private static int Walk(BehaviourGraphModel model, HkObject from, int column,
                            Dictionary<string, int> placed, List<(HkObject, int, string)> order, int max)
    {
        var queue = new Queue<(HkObject Node, int Column)>();
        queue.Enqueue((from, column));
        placed[from.Id] = column;

        // A walk root is owned by nothing. There is one per detached subtree as well as the real
        // root, and each is the top of its own family.
        order.Add((from, column, ""));
        int deepest = column;

        while (queue.Count > 0 && order.Count < max)
        {
            var (current, depth) = queue.Dequeue();
            foreach (string target in Targets(model, current))
            {
                if (placed.ContainsKey(target)) continue;
                var next = model.Get(target);
                if (next == null) continue;

                placed[target] = depth + 1;
                deepest = Math.Max(deepest, depth + 1);
                order.Add((next, depth + 1, current.Id));
                queue.Enqueue((next, depth + 1));
                if (order.Count >= max) break;
            }
        }
        return deepest;
    }
```

- [ ] **Step 4: Fix the existing callers**

Three places read the old shape. In `app/GraphView.cs:284`, change the loop header to
`foreach (var (obj, column, _) in GraphAuthor.Layout(model, MaxNodes))` for now; Task 4 replaces this
block entirely.

In `tools/symrm/Tests.cs`, `DetachedSubtreeStaysDrawn` and `ReplacingLinkSaysWhatItDisplaced` call
`GraphAuthor.Layout(...).Count`, which still compiles. Search for any other use:

Run: `grep -rn "GraphAuthor.Layout" --include=*.cs .`
Fix every hit that destructures the tuple.

- [ ] **Step 5: Run the suite**

Run: `dotnet run --project tools/symrm/symrm.csproj -- test 2>&1 | tail -3`
Expected: `0 failed`, and the total rises by the new checks.

- [ ] **Step 6: Commit**

```bash
git add src/Hkx/GraphAuthor.cs app/GraphView.cs tools/symrm/Tests.cs
git commit -m "Say which parent reached a node first

The walk already decides it, because it skips a node it has placed, and
then throws the answer away. The canvas needs it: a node pointed at by
several parents has to be placed once, hidden by one collapse and moved by
one drag."
```

---

### Task 2: `GraphOwnership`, everything derived from the owner

**Files:**
- Create: `src/Hkx/GraphOwnership.cs`
- Test: `tools/symrm/Tests.cs`

**Interfaces:**
- Consumes: `GraphAuthor.Layout`'s `OwnerId`.
- Produces:
  - `GraphOwnership.Of(IEnumerable<(string Id, string OwnerId)>)` returning a `Tree`
  - `Tree.Owner` : `IReadOnlyDictionary<string, string>`
  - `Tree.Children(string id)` : `IReadOnlyList<string>`
  - `Tree.Under(string id)` : `List<string>`, every owned descendant
  - `Tree.Chain(string id)` : `List<string>`, owners upward, nearest first
  - `Tree.Hidden(ISet<string> collapsed, string id)` : `bool`
  - `Tree.HiddenBy(ISet<string> collapsed, string id)` : `int`
  - `Tree.Moving(IEnumerable<string> selected)` : `HashSet<string>`

- [ ] **Step 1: Write the failing test**

Add to `Cases`:

```csharp
("OwnershipAnswersWhatMovesAndWhatHides", OwnershipAnswersWhatMovesAndWhatHides),
```

And the case:

```csharp
// The three questions the canvas asks about ownership, on a shape built to make the shared case
// the interesting one. A owns B and C; B owns D; C points at D too but does not own it.
private static void OwnershipAnswersWhatMovesAndWhatHides()
{
    Console.WriteLine("\nownership answers what moves and what hides");

    var tree = GraphOwnership.Of(new[]
    {
        ("A", ""), ("B", "A"), ("C", "A"), ("D", "B"), ("E", "C"), ("F", "E"),
    });

    Check("A owns two", 2, tree.Children("A").Count);
    Check("and everything under it", 5, tree.Under("A").Count);
    Check("D is owned by B and nobody else", "B", tree.Owner["D"]);

    var chain = tree.Chain("F");
    Check("F's chain is nearest first", "E", chain[0]);
    Check("then up to the root", "A", chain[^1]);

    var collapsed = new HashSet<string> { "B" };
    CheckTrue("collapsing B hides what B owns", tree.Hidden(collapsed, "D"));
    CheckTrue("and leaves the other branch alone", !tree.Hidden(collapsed, "E"));
    CheckTrue("and does not hide B itself", !tree.Hidden(collapsed, "B"));

    Check("B's badge counts only what it hides", 1, tree.HiddenBy(collapsed, "B"));

    // A collapse further up already hides these, so the inner one is not responsible for them and
    // must not claim them, or expanding it would promise nodes it cannot bring back.
    var both = new HashSet<string> { "A", "E" };
    Check("an inner collapse claims nothing already hidden", 0, tree.HiddenBy(both, "E"));
    Check("and the outer one claims all of it", 5, tree.HiddenBy(both, "A"));

    // The dedupe that matters: E is selected in its own right and is also under A.
    var moving = tree.Moving(new[] { "A", "E" });
    Check("everything moves once", 6, moving.Count);
    CheckTrue("including the one reached twice", moving.Contains("E"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project tools/symrm/symrm.csproj -- test 2>&1 | grep -i "GraphOwnership"`
Expected: a compile error, `GraphOwnership` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Hkx/GraphOwnership.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// Who owns what on the canvas, and the three answers that come out of it.
//
// A node can be pointed at by several parents. Sharing is the ordinary case rather than a corner:
// 3,624 of the corpus's 5,320 state infos share something with another state, usually a generator
// two states both point at. The picture has to put such a node in one place, hide it under one
// collapse and move it with one drag, so exactly one of those parents owns it, and that is the one
// the walk in GraphAuthor.Layout reached it from first.
//
// Everything here is derived from that one field. There is deliberately no second notion of
// ownership: if a later feature wants a different grouping it gets a different name, because two
// rules that disagree about who owns a node is a canvas where dragging and collapsing disagree.
public static class GraphOwnership
{
    public sealed class Tree
    {
        public IReadOnlyDictionary<string, string> Owner => _owner;

        private readonly Dictionary<string, string> _owner = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _children = new(StringComparer.Ordinal);
        private static readonly IReadOnlyList<string> None = Array.Empty<string>();

        internal Tree(IEnumerable<(string Id, string OwnerId)> placed)
        {
            foreach (var (id, ownerId) in placed)
            {
                _owner[id] = ownerId;
                if (ownerId.Length == 0) continue;

                if (!_children.TryGetValue(ownerId, out var kids))
                    _children[ownerId] = kids = new List<string>();
                kids.Add(id);
            }
        }

        /// The nodes this one owns, in the order the walk placed them, which is the order they are
        /// stacked in so that a family reads top to bottom the way the file lists it.
        public IReadOnlyList<string> Children(string id) =>
            _children.TryGetValue(id, out var kids) ? kids : None;

        /// Every node under this one through owned links only. A node owned by another branch is not
        /// under this one however many wires run to it from here.
        public List<string> Under(string id)
        {
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal) { id };
            var stack = new Stack<string>(Children(id));

            while (stack.Count > 0)
            {
                string at = stack.Pop();
                if (!seen.Add(at)) continue;
                found.Add(at);
                foreach (string child in Children(at)) stack.Push(child);
            }

            return found;
        }

        /// The owners above this node, nearest first.
        public List<string> Chain(string id)
        {
            var chain = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal) { id };

            string at = id;
            while (_owner.TryGetValue(at, out string? up) && up.Length > 0 && seen.Add(up))
            {
                chain.Add(up);
                at = up;
            }

            return chain;
        }

        /// Whether a collapse anywhere above this node hides it. A collapsed node is not hidden by
        /// its own collapse; it is the thing you click to get the rest back.
        public bool Hidden(ISet<string> collapsed, string id) =>
            collapsed.Count > 0 && Chain(id).Any(collapsed.Contains);

        /// How many nodes this collapse is responsible for hiding, which is what its badge says.
        ///
        /// Owned descendants only, so a node owned by another branch never inflates it, and only
        /// those not already hidden by a collapse further up, so expanding this one brings back
        /// exactly the number it promised.
        public int HiddenBy(ISet<string> collapsed, string id)
        {
            if (!collapsed.Contains(id)) return 0;
            if (Chain(id).Any(collapsed.Contains)) return 0;

            var without = new HashSet<string>(collapsed, StringComparer.Ordinal);
            without.Remove(id);

            return Under(id).Count(n => !Hidden(without, n));
        }

        /// Everything a drag moves: the nodes picked, plus what each of them owns.
        ///
        /// A set rather than a list because the two overlap all the time. Select a parent and one of
        /// its own children and the child is reached twice, and applying the drag delta twice to it
        /// would send it off at double speed.
        public HashSet<string> Moving(IEnumerable<string> selected)
        {
            var moving = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in selected)
            {
                moving.Add(id);
                foreach (string under in Under(id)) moving.Add(under);
            }
            return moving;
        }
    }

    public static Tree Of(IEnumerable<(string Id, string OwnerId)> placed) => new(placed);

    public static Tree Of(IEnumerable<(HkObject Node, int Column, string OwnerId)> placed) =>
        new(placed.Select(p => (p.Node.Id, p.OwnerId)));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project tools/symrm/symrm.csproj -- test 2>&1 | grep -A14 "ownership answers"`
Expected: every line `ok`.

- [ ] **Step 5: Commit**

```bash
git add src/Hkx/GraphOwnership.cs tools/symrm/Tests.cs
git commit -m "Answer what a collapse hides and what a drag moves

All three answers come off the one owner field rather than each working it
out its own way. The hidden count is owned descendants this collapse is
responsible for, so it excludes anything already hidden further up and
anything owned by another branch, and expanding brings back the number the
badge promised. The movement set is a set because a selected parent and a
selected child of it reach the same node twice."```

---

### Task 3: `GraphLayout`, children placed beside the parent that owns them

**Files:**
- Create: `src/Hkx/GraphLayout.cs`
- Test: `tools/symrm/Tests.cs`

**Interfaces:**
- Consumes: `GraphOwnership.Tree`.
- Produces:
  - `GraphLayout.Item(string Id, int Column, string OwnerId, double Height)`
  - `GraphLayout.Place(IReadOnlyList<Item> items, IReadOnlyDictionary<string, double> pinned, double rowGap)`
    returning `Dictionary<string, double>`, a Y per id. X is the caller's business.

- [ ] **Step 1: Write the failing test**

Add to `Cases`:

```csharp
("ChildrenSitBesideTheParentThatOwnsThem", ChildrenSitBesideTheParentThatOwnsThem),
("APinnedNodeIsNeverMovedToMakeRoom", APinnedNodeIsNeverMovedToMakeRoom),
```

And both cases:

```csharp
// The defect this replaces: nodes were placed by depth into columns and stacked with one running Y
// counter per column, so nothing ever consulted the parent's position and a parent low on the
// canvas got its children put near the top. The long diagonal wires were that.
private static void ChildrenSitBesideTheParentThatOwnsThem()
{
    Console.WriteLine("\nchildren sit beside the parent that owns them");

    // Two parents in column 1, each owning two children in column 2. The second parent is far down,
    // which is the case the old layout got wrong.
    var items = new List<GraphLayout.Item>
    {
        new("root", 0, "",     100),
        new("P1",   1, "root", 100),
        new("P2",   1, "root", 100),
        new("A",    2, "P1",   100),
        new("B",    2, "P1",   100),
        new("C",    2, "P2",   100),
        new("D",    2, "P2",   100),
    };

    var y = GraphLayout.Place(items, new Dictionary<string, double>(), 20);

    double Centre(string id) => y[id] + 50;

    // Each family straddles its own parent rather than starting at the top of the column.
    CheckTrue($"P1's children straddle P1 ({y["A"]:F0}, {y["B"]:F0} against {y["P1"]:F0})",
        Centre("A") < Centre("P1") + 1 && Centre("B") > Centre("P1") - 1);
    CheckTrue($"P2's children straddle P2 ({y["C"]:F0}, {y["D"]:F0} against {y["P2"]:F0})",
        Centre("C") < Centre("P2") + 1 && Centre("D") > Centre("P2") - 1);

    // The whole point: the far family is far down too, not stacked under the near one at the top.
    CheckTrue($"the second family is nowhere near the first ({y["C"]:F0} against {y["B"]:F0})",
        y["C"] > y["B"]);

    // Siblings stay adjacent. A group split by collision resolution is what produced the long wires.
    CheckTrue($"A and B are adjacent ({y["B"] - y["A"]:F0})", Math.Abs(y["B"] - y["A"] - 120) < 1);
    CheckTrue($"C and D are adjacent ({y["D"] - y["C"]:F0})", Math.Abs(y["D"] - y["C"] - 120) < 1);

    // And nothing overlaps in a column.
    foreach (var column in items.GroupBy(i => i.Column))
    {
        var sorted = column.OrderBy(i => y[i.Id]).ToList();
        for (int i = 1; i < sorted.Count; i++)
            CheckTrue($"{sorted[i - 1].Id} and {sorted[i].Id} do not overlap",
                y[sorted[i].Id] >= y[sorted[i - 1].Id] + sorted[i - 1].Height - 0.001);
    }
}

// A position the user chose by hand outranks anything the layout would rather do. It blocks, so a
// family can be pushed past it, and it never moves itself.
private static void APinnedNodeIsNeverMovedToMakeRoom()
{
    Console.WriteLine("\na pinned node is never moved to make room");

    var items = new List<GraphLayout.Item>
    {
        new("root", 0, "",     100),
        new("P",    1, "root", 100),
        new("A",    2, "P",    100),
        new("Held", 2, "",     100),
    };

    var pinned = new Dictionary<string, double> { ["Held"] = 0 };
    var y = GraphLayout.Place(items, pinned, 20);

    Check("the pinned node is exactly where it was put", 0d, y["Held"]);
    CheckTrue($"and the family went around it ({y["A"]:F0})", y["A"] >= 120 - 0.001);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project tools/symrm/symrm.csproj -- test 2>&1 | grep -i "GraphLayout"`
Expected: a compile error, `GraphLayout` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Hkx/GraphLayout.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// Where each node sits down the canvas.
//
// What this replaces put nodes into a column by depth and stacked them with one running counter per
// column, in whatever order the walk reached them. Nothing consulted the parent, so a parent two
// thousand pixels down got its children placed near the top of the next column, and the long
// diagonal wires across the whole canvas were that layout telling the truth about where it had put
// things.
//
// The rule here is that a family is a unit. The nodes a parent owns are stacked together and centred
// on the parent, and when two families collide the whole family moves. Splitting a family to pack a
// column tighter is exactly what produces the wires, so it is not done: extra whitespace is the
// price and it is the right price.
//
// The X is the caller's business. This only answers the question the old code got wrong.
public static class GraphLayout
{
    /// A node to place. `Height` matters because nodes are not the same height: one is as tall as
    /// its slot count, so stacking by a fixed row height overlaps every node shorter than the
    /// tallest.
    public sealed record Item(string Id, int Column, string OwnerId, double Height);

    /// A Y for every item, families kept together, pinned nodes left exactly where they are.
    ///
    /// `pinned` is the nodes the user dragged. They take no part in being moved and they do block, so
    /// a family can be pushed past one but never through it and never over the top of it.
    public static Dictionary<string, double> Place(IReadOnlyList<Item> items,
                                                   IReadOnlyDictionary<string, double> pinned,
                                                   double rowGap)
    {
        var y = new Dictionary<string, double>(StringComparer.Ordinal);
        if (items.Count == 0) return y;

        var byId = items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var tree = GraphOwnership.Of(items.Select(i => (i.Id, i.OwnerId)));

        // Walk roots first, stacked down their own column, then every family under each of them.
        // Done breadth first so a parent always has a position before its children are placed
        // against it.
        double nextRoot = 0;
        var order = new Queue<string>();

        foreach (var item in items.Where(i => i.OwnerId.Length == 0))
        {
            y[item.Id] = pinned.TryGetValue(item.Id, out double held) ? held : nextRoot;
            nextRoot = y[item.Id] + item.Height + rowGap;
            order.Enqueue(item.Id);
        }

        while (order.Count > 0)
        {
            string parent = order.Dequeue();
            var children = tree.Children(parent).Where(byId.ContainsKey).ToList();
            if (children.Count == 0) continue;

            // The family as one block, centred on the parent, so the wires leaving the parent fan
            // out from beside it rather than running the height of the canvas.
            double total = children.Sum(c => byId[c].Height) + rowGap * (children.Count - 1);
            double top = y[parent] + byId[parent].Height / 2 - total / 2;

            foreach (string child in children)
            {
                y[child] = pinned.TryGetValue(child, out double held) ? held : top;
                top += byId[child].Height + rowGap;
                order.Enqueue(child);
            }
        }

        Resolve(items, y, pinned, tree, byId, rowGap);
        return y;
    }

    /// Pushes families apart until nothing in a column overlaps.
    ///
    /// A family moves whole, and moving one carries everything under it so the shape below stays
    /// where it was relative to its own parent. Repeated because moving one family down can put it
    /// into the next, and bounded because a cycle in ownership would otherwise spin here; the
    /// ownership tests rule that out, and the bound is a backstop rather than a design.
    private static void Resolve(IReadOnlyList<Item> items, Dictionary<string, double> y,
                                IReadOnlyDictionary<string, double> pinned,
                                GraphOwnership.Tree tree,
                                Dictionary<string, Item> byId, double rowGap)
    {
        var columns = items.GroupBy(i => i.Column).OrderBy(g => g.Key).ToList();

        for (int pass = 0; pass < 64; pass++)
        {
            bool moved = false;

            foreach (var column in columns)
            {
                var sorted = column.OrderBy(i => y[i.Id]).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();

                for (int i = 1; i < sorted.Count; i++)
                {
                    var above = sorted[i - 1];
                    var below = sorted[i];

                    double wants = y[above.Id] + above.Height + rowGap;
                    double overlap = wants - y[below.Id];
                    if (overlap <= 0.001) continue;

                    // A node the user positioned is not ours to move. The one above it has already
                    // been placed, so the only thing left is to leave the collision alone rather
                    // than drag a pinned node out of the way.
                    if (pinned.ContainsKey(below.Id)) continue;

                    Shift(below.Id, overlap, y, pinned, tree, byId);
                    moved = true;
                }
            }

            if (!moved) return;
        }
    }

    /// Moves a node and everything it owns by the same amount, so a family arrives at its new place
    /// in the shape it left.
    private static void Shift(string id, double by, Dictionary<string, double> y,
                              IReadOnlyDictionary<string, double> pinned,
                              GraphOwnership.Tree tree, Dictionary<string, Item> byId)
    {
        if (!pinned.ContainsKey(id) && y.ContainsKey(id)) y[id] += by;

        foreach (string under in tree.Under(id))
            if (!pinned.ContainsKey(under) && y.ContainsKey(under) && byId.ContainsKey(under))
                y[under] += by;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project tools/symrm/symrm.csproj -- test 2>&1 | grep -A16 "children sit beside"`
Expected: every line `ok`.

- [ ] **Step 5: Commit**

```bash
git add src/Hkx/GraphLayout.cs tools/symrm/Tests.cs
git commit -m "Put a node's children beside the node that owns them

The layout placed nodes by depth into columns and stacked each column with
one running counter, so nothing consulted the parent and a parent low on the
canvas got its children put near the top. The long diagonal wires were that.

A family is now a unit: stacked together, centred on the parent, and moved
whole when two families collide. Splitting a family to pack a column tighter
is what produced the wires, so the whitespace is accepted instead. Anything
the user dragged blocks and never moves."
```

---

### Task 4: The canvas uses the new layout

**Files:**
- Modify: `app/GraphView.cs:255-326`
- Test: `tools/uismoke/Smoke.cs`

**Interfaces:**
- Consumes: `GraphLayout.Place`, `GraphOwnership.Of`.
- Produces: `GraphView` fields `_own` (`GraphOwnership.Tree`) and `Node.OwnerId`, used by Tasks 5 to 7.

- [ ] **Step 1: Add the ownership field and the node's owner**

In `app/GraphView.cs`, add to the `Node` class beside `Class`:

```csharp
        public string OwnerId = "";
```

And beside `_model`:

```csharp
    /// Who owns what, rebuilt with the nodes. Every question about collapsing, dragging and
    /// placement goes through this rather than being worked out again per feature.
    private GraphOwnership.Tree _own = GraphOwnership.Of(Array.Empty<(string, string)>());
```

- [ ] **Step 2: Replace the placement block**

Replace the body of `Show` from `var nextY = new Dictionary<int, double>();` through the end of the
`foreach` with this. The heights have to be known before anything is placed, so the loop is split in
two: measure, place, then build.

```csharp
        // Heights first, because a node is as tall as its slot count and the layout cannot centre a
        // family on a parent whose height it does not know yet.
        var placed = GraphAuthor.Layout(model, MaxNodes);
        _own = GraphOwnership.Of(placed);

        var measured = new List<GraphLayout.Item>();
        var slotsOf = new Dictionary<string, List<GraphLinks.Slot>>(StringComparer.Ordinal);
        var wildcardsOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (obj, column, ownerId) in placed)
        {
            var slots = GraphLinks.OutSlots(model, obj);
            var wildcards = wildcardsInto.GetValueOrDefault(obj.Id) ?? new List<string>();
            double height = HeaderHeight + Math.Max(1, slots.Count) * RowHeight
                            + Math.Min(wildcards.Count, WildcardRows) * RowHeight + 8;

            slotsOf[obj.Id] = slots;
            wildcardsOf[obj.Id] = wildcards;
            measured.Add(new GraphLayout.Item(obj.Id, column, ownerId, height));
        }

        // A node the user has dragged stays where they put it, and blocks, so a family is pushed
        // past it rather than over it.
        var pinned = _placed.Where(p => slotsOf.ContainsKey(p.Key))
                            .ToDictionary(p => p.Key, p => p.Value.Y, StringComparer.Ordinal);

        var y = GraphLayout.Place(measured, pinned, RowGap);

        foreach (var (obj, column, ownerId) in placed)
        {
            var slots = slotsOf[obj.Id];
            var wildcards = wildcardsOf[obj.Id];
            double height = measured.First(m => m.Id == obj.Id).Height;
            double x = _placed.TryGetValue(obj.Id, out var kept) ? kept.X : column * ColumnGap;

            _order.Add(obj.Id);
            _nodes[obj.Id] = new Node
            {
                Id = obj.Id,
                Class = obj.Class,
                OwnerId = ownerId,
                Name = obj.Str("name"),
                Animation = obj.Str("animationName"),
                Slots = slots,
                Accent = Ux.ForClass(obj.Class),
                Empty = empty.Contains(obj.Id),
                Start = _routes.StartStates.Contains(obj.Id),
                Wildcards = wildcards,
                Problem = _problems.TryGetValue(obj.Id, out var level) ? level : null,
                Bounds = new Rect(x, y[obj.Id], NodeWidth, height),
            };
        }
```

- [ ] **Step 3: Build and run the window smoke test**

Run: `dotnet build app/BehaviourStudio.csproj --nologo 2>&1 | tail -3`
Expected: `0 Error(s)`.

Run: `dotnet test tools/uismoke/uismoke.csproj --nologo 2>&1 | tail -5`
Expected: passing. The smoke test already opens a behaviour and counts drawn nodes, so a layout that
throws or drops nodes fails here.

- [ ] **Step 4: Look at it**

This task exists entirely to change how the canvas reads, and "it did not throw" is not "it reads
better". The smoke runner already draws the canvas to a PNG with no display attached, which is the
honest way to check it:

```bash
dotnet run --project tools/uismoke/uismoke.csproj -- --png \
  dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx /tmp/graph-after.png 0.35
```

Do the same on `main` first and keep it as `/tmp/graph-before.png`, then compare the two. Expected:
families sitting beside their parents, and none of the long diagonals that run the height of the
canvas in the before picture. If the two look the same, `Show` is still using the old counter.

- [ ] **Step 5: Commit**

```bash
git add app/GraphView.cs
git commit -m "Draw the graph where the layout puts it

Measuring is now separate from placing, because a family is centred on its
parent and that cannot be worked out until the parent's height is known."
```

---

### Task 5: Collapse

**Files:**
- Modify: `app/GraphView.cs`
- Test: `tools/uismoke/Smoke.cs`

**Interfaces:**
- Consumes: `_own.Hidden`, `_own.HiddenBy`, `_own.Under`.
- Produces: `GraphView.Collapsed` (`IReadOnlyCollection<string>`), `GraphView.ToggleCollapse(string id, bool deep)`.

- [ ] **Step 1: Hold the collapsed set**

Add beside `_placed` in `app/GraphView.cs`:

```csharp
    /// Which nodes are folded shut, kept across rebuilds so an edit does not silently unfold the
    /// graph, and cleared when a different file is opened.
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Collapsed => _collapsed;
```

In `Clear()` beside `_placed.Clear()`, add `_collapsed.Clear();`.

- [ ] **Step 2: Skip hidden nodes when drawing**

In `Render`, at the top of the loop that draws nodes, and again in the loop that draws wires, skip
anything hidden. Add this helper beside `NodeAt`:

```csharp
    /// A node is hidden when a collapse anywhere above it is shut. A node owned by another branch is
    /// not below this one, so collapsing a state can never blank out part of a state elsewhere on
    /// the canvas.
    private bool IsHidden(string id) => _own.Hidden(_collapsed, id);
```

Guard the node loop with `if (IsHidden(node.Id)) continue;` and the wire loops with
`if (IsHidden(route.FromId) || IsHidden(route.ToId)) continue;`. A wire whose far end is a shared
node owned elsewhere disappears with the branch it leaves, because the near end is hidden.

- [ ] **Step 3: Draw the chevron and the badge**

In `DrawNode`, after the title is drawn, add:

```csharp
        // The chevron, and what it is holding. The count is what this collapse is responsible for
        // rather than everything reachable, so expanding brings back exactly what it says.
        if (_own.Children(node.Id).Count > 0)
        {
            bool shut = _collapsed.Contains(node.Id);
            var chevron = ChevronRect(node);
            Draw(ctx, shut ? "▸" : "▾", chevron.X, chevron.Y, 10 * scale,
                 Ux.MutedBrush, chevron.Width);

            if (shut)
            {
                int hidden = _own.HiddenBy(_collapsed, node.Id);
                var badge = new Rect(r.Right - 62 * scale, r.Bottom - 14 * scale, 58 * scale, 12 * scale);
                ctx.DrawRectangle(new SolidColorBrush(node.Accent, 0.30), null, badge, 3, 3);
                Draw(ctx, $"+{hidden} hidden", badge.X + 3 * scale, badge.Y, 8 * scale,
                     Ux.MutedBrush, badge.Width - 4 * scale);
            }
        }
```

And the hit rectangle, beside `NodeAt`:

```csharp
    /// Where the chevron sits, in screen space. Left of the title, inside the header.
    private Rect ChevronRect(Node node)
    {
        var r = new Rect(ToScreen(node.Bounds.TopLeft),
                         new Size(node.Bounds.Width * _zoom, node.Bounds.Height * _zoom));
        return new Rect(r.X + 2 * _zoom, r.Y + 3 * _zoom, 12 * _zoom, 12 * _zoom);
    }
```

The title currently starts at `r.X + 6 * scale`. Move it to `r.X + 18 * scale` for nodes with
children so the chevron is not drawn over it.

- [ ] **Step 4: Toggle on click**

Add the public method:

```csharp
    /// Folds a node shut or open. Deep does every node it owns rather than one level: if any of them
    /// is open they all shut, otherwise they all open, so one gesture always has an obvious result.
    public void ToggleCollapse(string id, bool deep)
    {
        if (!_nodes.ContainsKey(id)) return;

        if (!deep)
        {
            if (!_collapsed.Add(id)) _collapsed.Remove(id);
        }
        else
        {
            var family = _own.Under(id).Where(_nodes.ContainsKey).Append(id).ToList();
            bool anyOpen = family.Any(n => _own.Children(n).Count > 0 && !_collapsed.Contains(n));

            foreach (string node in family)
            {
                if (_own.Children(node).Count == 0) continue;
                if (anyOpen) _collapsed.Add(node); else _collapsed.Remove(node);
            }
        }

        InvalidateVisual();
    }
```

And in `OnPointerPressed`, before the `NodeAt` branch that selects and starts a drag:

```csharp
        // The chevron is checked before selection so folding a node does not also select it, which
        // would throw away a selection the user built up to drag.
        foreach (var candidate in _nodes.Values)
        {
            if (IsHidden(candidate.Id) || _own.Children(candidate.Id).Count == 0) continue;
            if (!ChevronRect(candidate).Contains(screen)) continue;

            ToggleCollapse(candidate.Id, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            return;
        }
```

- [ ] **Step 5: Test it in the window smoke runner**

`tools/uismoke/Smoke.cs` is a console runner with its own `Check` and `CheckTrue`, not xunit. Add
this next to the other canvas checks, inside the block that has already selected the Graph tab:

```csharp
        // Collapsing a state must never blank out part of a state somewhere else, which is the
        // failure the ownership rule exists to prevent.
        {
            var canvas = Find<GraphView>(window).First();
            string parent = canvas.DrawnIds.First(id => canvas.OwnedCount(id) > 0);
            int owned = canvas.OwnedCount(parent);

            canvas.ToggleCollapse(parent, false);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Check("folding a node hides exactly what it owns", owned, canvas.HiddenCount);
            CheckTrue("and the node itself stays", !canvas.IsHiddenForTest(parent));

            canvas.ToggleCollapse(parent, false);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Check("unfolding brings all of it back", 0, canvas.HiddenCount);
        }
```

That needs three read only members on `GraphView`. Two of them are what the badge already computes,
so they cost nothing:

```csharp
    public int OwnedCount(string id) => _own.Under(id).Count(_nodes.ContainsKey);
    public int HiddenCount => _nodes.Keys.Count(IsHidden);
    public bool IsHiddenForTest(string id) => IsHidden(id);
```

- [ ] **Step 6: Run both suites**

Run: `dotnet run --project tools/symrm/symrm.csproj -- test 2>&1 | tail -2`
Run: `dotnet test tools/uismoke/uismoke.csproj --nologo 2>&1 | tail -5`
Expected: both pass.

- [ ] **Step 7: Commit**

```bash
git add app/GraphView.cs tools/uismoke/Smoke.cs
git commit -m "Fold a node shut

Hiding is by owner chain, so collapsing a state hides what that state owns
and nothing else. A shared node owned by another branch stays visible and
the wire from the folded branch to it goes with the branch.

Ctrl on the chevron does every node it owns rather than one level. The badge
counts what this collapse is responsible for, so expanding brings back the
number it promised."
```

---

### Task 6: Marquee, ctrl click and group drag

**Files:**
- Modify: `app/GraphView.cs`
- Test: `tools/uismoke/Smoke.cs`

**Interfaces:**
- Consumes: `_own.Moving`.
- Produces: `GraphView.SelectedIds` (`IReadOnlyCollection<string>`). `SelectedId` stays and returns
  the first of the set, so `MainWindow` keeps compiling unchanged.

- [ ] **Step 1: Turn the selection into a set**

Replace the `SelectedId` property in `app/GraphView.cs`:

```csharp
    private readonly List<string> _selected = new();

    /// The whole selection. Several nodes can be picked with a marquee or with ctrl, and a drag moves
    /// all of them.
    public IReadOnlyCollection<string> SelectedIds => _selected;

    /// The one the properties panel is looking at, which is the first of the selection. Kept so every
    /// caller that only ever wanted one node goes on working.
    public string SelectedId => _selected.Count > 0 ? _selected[0] : "";
```

Replace every `SelectedId = x;` with `Select(x);` and every `SelectedId = "";` with `_selected.Clear();`,
adding:

```csharp
    private void Select(string id)
    {
        _selected.Clear();
        if (id.Length > 0) _selected.Add(id);
    }
```

In `DrawNode`, `bool selected = node.Id == SelectedId;` becomes `bool selected = _selected.Contains(node.Id);`.

- [ ] **Step 2: Marquee on left drag from empty canvas**

Add the fields:

```csharp
    private Point? _marqueeFrom;
    private Rect _marquee;
```

In `OnPointerPressed`, where an empty click currently sets `_panning = true`, start a marquee instead:

```csharp
        else
        {
            // Left drag on empty canvas draws a marquee. Panning stays on the middle button, which
            // already worked and is where it lives in every other tool of this kind.
            _selected.Clear();
            _marqueeFrom = world;
            _marquee = new Rect(world, world);
            Selected?.Invoke("");
        }
```

In `OnPointerMoved`, before the `_panning` branch:

```csharp
        if (_marqueeFrom is { } from)
        {
            var to = ToWorld(screen);
            _marquee = new Rect(from, to);
            InvalidateVisual();
            return;
        }
```

In `OnPointerReleased`, at the top:

```csharp
        if (_marqueeFrom != null)
        {
            _marqueeFrom = null;

            // Intersecting rather than fully inside, so a node hanging off the edge of the view can
            // be caught without zooming out far enough to fit all of it in the box.
            foreach (var node in _nodes.Values)
                if (!IsHidden(node.Id) && node.Bounds.Intersects(_marquee))
                    _selected.Add(node.Id);

            Selected?.Invoke(SelectedId);
            _marquee = default;
            InvalidateVisual();
            return;
        }
```

And draw it, at the end of `Render`:

```csharp
        if (_marquee.Width > 0 && _marquee.Height > 0)
        {
            var box = new Rect(ToScreen(_marquee.TopLeft),
                               new Size(_marquee.Width * _zoom, _marquee.Height * _zoom));
            ctx.DrawRectangle(new SolidColorBrush(Ux.RouteColour, 0.10),
                              new Pen(new SolidColorBrush(Ux.RouteColour, 0.6), 1), box);
        }
```

- [ ] **Step 3: Ctrl click, and drag the whole selection**

In `OnPointerPressed`, replace the node branch:

```csharp
        var node = NodeAt(world);
        if (node != null)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (!_selected.Remove(node.Id)) _selected.Add(node.Id);
            }
            // Dragging something outside the selection takes just that node, rather than carrying a
            // selection the user has visibly moved on from.
            else if (!_selected.Contains(node.Id)) Select(node.Id);

            Selected?.Invoke(SelectedId);
            if (e.ClickCount >= 2) Activated?.Invoke(node.Id);
            else _dragNode = node;
        }
```

In `OnPointerMoved`, replace the drag branch:

```csharp
        if (_dragNode != null)
        {
            // What moves is everything selected plus everything each of them owns, as a set. A
            // selected parent and a selected child of it reach the same node twice, and applying the
            // delta twice would send it off at double speed.
            var picked = _selected.Contains(_dragNode.Id) ? _selected : new List<string> { _dragNode.Id };

            foreach (string id in _own.Moving(picked))
            {
                if (!_nodes.TryGetValue(id, out var moving)) continue;
                moving.Bounds = moving.Bounds.WithX(moving.Bounds.X + delta.X / _zoom)
                                             .WithY(moving.Bounds.Y + delta.Y / _zoom);
                _placed[id] = moving.Bounds.TopLeft;
            }

            InvalidateVisual();
            return;
        }
```

- [ ] **Step 4: Test the dedupe**

Add to `tools/uismoke/Smoke.cs`, in the same block:

```csharp
        // The failure this guards: a node both explicitly selected and reached through its parent
        // moves twice and drifts away from its family at double speed.
        {
            var canvas = Find<GraphView>(window).First();
            string parent = canvas.DrawnIds.First(id => canvas.OwnedCount(id) > 0);
            string child = canvas.OwnedIds(parent).First();

            var wasParent = canvas.PositionOf(parent)!.Value;
            var wasChild = canvas.PositionOf(child)!.Value;

            canvas.SelectForTest(new[] { parent, child });
            canvas.DragForTest(parent, new Avalonia.Vector(40, 25));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var nowParent = canvas.PositionOf(parent)!.Value;
            var nowChild = canvas.PositionOf(child)!.Value;

            Check("the node dragged moved by the drag", 40d, Math.Round(nowParent.X - wasParent.X));
            Check("and the one reached twice moved once, not twice", 40d,
                  Math.Round(nowChild.X - wasChild.X));
            Check("in both directions", 25d, Math.Round(nowChild.Y - wasChild.Y));
        }
```

`PositionOf` already exists on `GraphView`. Add the two seams the drag needs, next to it:

```csharp
    /// Selection and drag driven directly, for the window checks. Injected clicks are not reliable
    /// enough on this platform to build a regression on, and the thing worth testing is what the
    /// movement set does rather than whether a synthetic click landed on a node.
    public void SelectForTest(IEnumerable<string> ids)
    {
        _selected.Clear();
        foreach (string id in ids) if (_nodes.ContainsKey(id)) _selected.Add(id);
        InvalidateVisual();
    }

    public void DragForTest(string id, Vector by)
    {
        if (!_nodes.ContainsKey(id)) return;
        var picked = _selected.Contains(id) ? _selected : new List<string> { id };

        foreach (string moving in _own.Moving(picked))
        {
            if (!_nodes.TryGetValue(moving, out var node)) continue;
            node.Bounds = node.Bounds.WithX(node.Bounds.X + by.X).WithY(node.Bounds.Y + by.Y);
            _placed[moving] = node.Bounds.TopLeft;
        }

        InvalidateVisual();
    }

    public IReadOnlyList<string> OwnedIds(string id) => _own.Under(id).Where(_nodes.ContainsKey).ToList();
```

- [ ] **Step 5: Run both suites and drive it by hand**

Run: `dotnet run --project tools/symrm/symrm.csproj -- test 2>&1 | tail -2`
Run: `dotnet test tools/uismoke/uismoke.csproj --nologo 2>&1 | tail -5`
Then launch the app as in Task 4 and check by hand: drag a box over several nodes, ctrl click one to
drop it, drag one of the rest, and confirm the group moves together and middle drag still pans.

- [ ] **Step 6: Commit**

```bash
git add app/GraphView.cs tools/uismoke/Smoke.cs
git commit -m "Select several nodes and move them together

Marquee on left drag from empty canvas, ctrl click to add or drop one, and a
drag moves everything selected plus everything each of them owns. Panning
moves to the middle button only, which already worked.

The movement set is deduplicated: a selected parent and a selected child of
it reach the same node twice, and moving it twice sends it off at double
speed."
```

---

### Task 7: A shared node says so

**Files:**
- Modify: `app/GraphView.cs`
- Test: `tools/uismoke/Smoke.cs`

**Interfaces:**
- Consumes: `_routes`, `_model`, `Node.OwnerId`.
- Produces: `GraphView.SharedBy(string id)` returning `IReadOnlyList<string>`, the parents that point
  at a node other than its owner.

- [ ] **Step 1: Work out who else points at a node**

Add to `GraphView`, filled in `Show` right after `_own` is built:

```csharp
    /// Which other nodes point at a node without owning it. Ownership decides where a node is drawn,
    /// and a node with entries here is drawn in a place that is only one of its homes, so it says so
    /// rather than letting the picture imply it belongs to one branch.
    private readonly Dictionary<string, List<string>> _sharedBy = new(StringComparer.Ordinal);

    public IReadOnlyList<string> SharedBy(string id) =>
        _sharedBy.TryGetValue(id, out var by) ? by : Array.Empty<string>();
```

In `Show`, after `_own = GraphOwnership.Of(placed);`:

```csharp
        _sharedBy.Clear();
        foreach (var (obj, _, _) in placed)
            foreach (var slot in GraphLinks.OutSlots(model, obj))
                foreach (string target in slot.Targets)
                {
                    if (!_own.Owner.TryGetValue(target, out string? owner)) continue;
                    if (owner == obj.Id || obj.Id == target) continue;

                    if (!_sharedBy.TryGetValue(target, out var by))
                        _sharedBy[target] = by = new List<string>();
                    if (!by.Contains(obj.Id)) by.Add(obj.Id);
                }
```

- [ ] **Step 2: Draw the doubled border**

In `DrawNode`, immediately after the main `ctx.DrawRectangle(body, edge, r, 4, 4);`:

```csharp
        // A second outline one pixel in, so a shared node reads as doubled at a glance. Under the
        // selection treatment and dimmer than it on purpose: which node you picked matters more in
        // the moment than which nodes are borrowed, and a shared node that is also selected has to
        // read as selected first.
        if (_sharedBy.ContainsKey(node.Id))
            ctx.DrawRectangle(null, new Pen(new SolidColorBrush(borderColour, 0.45), 1),
                              r.Deflate(2.5), 3, 3);
```

- [ ] **Step 3: Say it on hover**

Add to `OnPointerMoved`, before the drag branch:

```csharp
        // Hovering a shared node names the parents that point at it. Which ones, not just how many:
        // "shared" on its own leaves you hunting the canvas for the other end.
        var over = NodeAt(ToWorld(screen));
        string tip = "";
        if (over != null && _sharedBy.TryGetValue(over.Id, out var parents))
        {
            var names = parents.Select(p => _nodes.TryGetValue(p, out var n) && n.Name.Length > 0
                                            ? n.Name : "#" + p);
            tip = $"Shared by {parents.Count + 1} parents: {string.Join(", ", names)}";
        }

        if (!Equals(ToolTip.GetTip(this), tip)) ToolTip.SetTip(this, tip.Length > 0 ? tip : null);
```

The count is `parents.Count + 1` because the owner is a parent too and is not in the list.

- [ ] **Step 4: Test it**

Add to `tools/uismoke/Smoke.cs`, in the same block:

```csharp
        // Sharing is the ordinary case in a shipped behaviour, so a real file has to produce some.
        // A check that passes because nothing was shared would be testing nothing.
        {
            var canvas = Find<GraphView>(window).First();
            int shared = canvas.DrawnIds.Count(id => canvas.SharedBy(id).Count > 0);
            CheckTrue($"the example behaviour shares nodes, so the case is covered ({shared})",
                      shared > 0);

            string one = canvas.DrawnIds.First(id => canvas.SharedBy(id).Count > 0);
            CheckTrue("and the owner is not listed among the borrowers",
                      !canvas.SharedBy(one).Contains(canvas.OwnerOf(one)));
        }
```

With one more seam on `GraphView`:

```csharp
    public string OwnerOf(string id) => _own.Owner.TryGetValue(id, out string? owner) ? owner : "";
```

- [ ] **Step 5: Run and commit**

Run: `dotnet test tools/uismoke/uismoke.csproj --nologo 2>&1 | tail -5`

```bash
git add app/GraphView.cs tools/uismoke/Smoke.cs
git commit -m "Say when a node is drawn in only one of its homes

Ownership decides where a shared node goes, and without a mark the picture
implies it belongs to that branch alone. A second inset outline says it is
doubled, drawn under the selection treatment and dimmer, because which node
you picked matters more in the moment than which are borrowed. Hovering
names the other parents rather than only counting them."
```

---

### Task 8: A standalone animation fills the clip list

**Files:**
- Modify: `app/MainWindow.cs:2316-2328`, and the load path at `app/MainWindow.cs:2784`
- Test: `tools/uismoke/Smoke.cs`

**Interfaces:**
- Consumes: `_animation` (the decoded `HkxAnimationData` the Animation tab already holds).
- Produces: nothing new.

- [ ] **Step 1: Add the row**

In `app/MainWindow.cs`, replace `BuildClipList`:

```csharp
    private void BuildClipList(BehaviourGraphModel model)
    {
        _clips.Clear();
        foreach (var clip in model.Objects.Where(o => o.Class == "hkbClipGenerator"))
        {
            string animation = clip.Str("animationName");
            _clips.Add(null, clip.Str("name"), animation.Length > 0 ? animation : "nothing")
                  .Colour(0, Ux.TitleBrush)
                  .Colour(1, animation.Length > 0 ? Ux.CodeBrush : Ux.MutedBrush)
                  .Tag(clip.Id);
        }
    }

    /// The one row a file holding an animation and no behaviour gets.
    ///
    /// The list is built from clip generators, and an animation file has none, so it came up empty
    /// and the Playback panel looked broken. It was not: there is genuinely nothing to pick, because
    /// the animation is already the thing being played. Saying that costs a row and saves everyone
    /// working out the distinction before they can use the tab.
    private void ShowLoneAnimation()
    {
        _clips.Clear();
        if (_animation is not { NumFrames: > 0 }) return;

        // Deliberately untagged. The selection handler reads the tag as an object id and returns
        // when it is not a string, so a row naming no object needs no guard added to it.
        _clips.Add(null, Path.GetFileNameWithoutExtension(_hkxPath),
                   $"{_animation.Duration:F2}s, {_animation.NumFrames} frames")
              .Colour(0, Ux.TitleBrush)
              .Colour(1, Ux.CodeBrush);

        _clips.SelectFirst();
    }
```

- [ ] **Step 2: Call it on the animation path**

At `app/MainWindow.cs:2784`, where `isAnimation` is decided, add after the summary is set:

```csharp
        if (isAnimation) ShowLoneAnimation();
```

`HkGrid` has no `SelectFirst`. Add it beside `SelectByTag`, which is the method it is a simpler
cousin of. The grid is a `TreeView` behind the scenes, so selecting means setting `SelectedItem`
rather than an index:

```csharp
    /// Picks the first row, for a list with one thing in it worth looking at. `SelectByTag` cannot
    /// do this job because the row it is for names no object and therefore carries no tag.
    public bool SelectFirst()
    {
        if (_tree.Items.Count == 0) return false;
        _tree.SelectedItem = _tree.Items[0] as TreeViewItem;
        return true;
    }
```

- [ ] **Step 3: Test it**

Add to `tools/uismoke/Smoke.cs`. The grid counts rows with `RowCount`, so open a second window on the
animation rather than reusing the behaviour one:

```csharp
        // An empty panel reads as broken even when it is correct, and every user pays for that once.
        {
            var animWindow = new MainWindow();
            animWindow.Show();
            animWindow.Open("dist/examples/Dogmeat/Animations/IdleOutroDogmeatWalkForward.hkx");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var animTabs = Find<TabControl>(animWindow).First();
            animTabs.SelectedIndex = animTabs.Items.OfType<TabItem>().ToList()
                                             .FindIndex(t => t.Header?.ToString() == "Playback");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var clips = Find<HkGrid>(animWindow).Last();
            Check("a standalone animation puts itself in the clip list", 1, clips.RowCount);
        }
```

`Find<HkGrid>(animWindow).Last()` is the clip grid because it is the last one built on the Playback
tab. If that turns out to be fragile, give the grid a `Name` in `BuildPlaybackTab` and find it by
name instead.

- [ ] **Step 4: Run and look at it**

Run: `dotnet test tools/uismoke/uismoke.csproj --nologo 2>&1 | tail -5`

Then draw the window to a PNG the same way Task 4 does, on the animation rather than the behaviour,
and confirm the panel shows one row reading `IdleOutroDogmeatWalkForward` and `11.20s, 337 frames`.

- [ ] **Step 5: Commit**

```bash
git add app/MainWindow.cs app/HkGrid.cs tools/uismoke/Smoke.cs
git commit -m "Put the animation in the clip list when there is no behaviour

The list is built from clip generators and an animation file holds none, so
Playback opened with an empty panel that reads as broken. It was correct and
that did not help anyone: the row says what is loaded and selects it, so the
tab is usable without first understanding why a standalone animation has no
clip references."
```

---

## Self-review

**Spec coverage.** Section 1 ownership is Tasks 1 and 2. Section 2 layout is Tasks 3 and 4. Section 3
collapse is Task 5. Section 4 selection and drag is Task 6. Section 5 the shared mark is Task 7.
Section 6 the animation row is Task 8. The spec's testing list maps onto the tests in Tasks 1, 2, 3,
5, 6 and 7, with the one gap noted below.

**One gap, deliberately left.** The spec asks for a test that a collision moves a whole sibling group
and never part of one. Task 3 tests adjacency after placement, which catches a split indirectly. A
direct test needs a fixture where two families are forced to collide; add it in Task 3 if the
adjacency check turns out to pass while the picture still splits families.

**Names used across tasks:** `GraphAuthor.Layout` -> `(Node, Column, OwnerId)`, `GraphOwnership.Of`,
`Tree.Children`, `Tree.Under`, `Tree.Chain`, `Tree.Hidden`, `Tree.HiddenBy`, `Tree.Moving`,
`GraphLayout.Item`, `GraphLayout.Place`, `GraphView._own`, `GraphView._collapsed`,
`GraphView.ToggleCollapse`, `GraphView.SelectedIds`, `GraphView.SharedBy`. Checked consistent across
all eight tasks.

**Risk worth stating.** Task 4 changes the shape of `Show`, which is the busiest method on the
canvas, and Task 6 changes the meaning of `SelectedId` under every caller in `MainWindow`. Both are
the tasks to review most carefully. `SelectedId` keeps its type and its single node meaning, so the
compiler will not flag a caller that should have become multiple selection aware; grep for it and
decide each one on purpose.
