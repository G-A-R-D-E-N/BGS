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
// Every node gets a band as tall as everything under it, bands never overlap, and a node sits
// centred in its own band, which puts it level with the middle of its family.
//
// Centring children on their parent is not enough on its own, and finding that out is why this is
// written the way it is. Two parents 120 apart, one with six children and one with two, gives the
// first family a 700 tall block and the second nowhere to go: pushing it clear moves it 300 away
// from the parent it belongs to, and the long wire is back. A parent's position has to account for
// how tall its family is, which means measuring the family before placing the parent.
//
// So this is two passes. Deepest first to measure what each subtree needs, then outermost first to
// hand each one a band and centre its owner in it. Nothing collides, because siblings get bands that
// do not overlap, so there is no packing step that could split a family in the first place.
//
// Two things outrank all of that, and only two. A node the user dragged never moves, because a
// position somebody chose by hand beats one worked out here. And a shared node is laid out once,
// under the parent that owns it, so the branch borrowing it gets a wire rather than a second copy or
// a fight over where it goes.
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
    /// `pinned` is the nodes the user dragged. They keep their position, and the automatic nodes are
    /// pushed clear of them rather than the other way round.
    ///
    /// The same items and the same pins always give the same answer. Nothing depends on dictionary
    /// order: children keep the order the walk placed them, and ties are broken by id.
    public static Dictionary<string, double> Place(IReadOnlyList<Item> items,
                                                   IReadOnlyDictionary<string, double> pinned,
                                                   double rowGap)
    {
        var y = new Dictionary<string, double>(StringComparer.Ordinal);
        if (items.Count == 0) return y;

        var byId = new Dictionary<string, Item>(StringComparer.Ordinal);
        foreach (var item in items) byId[item.Id] = item;

        var tree = GraphOwnership.Of(items.Select(i => (i.Id, i.OwnerId)));
        var roots = items.Where(i => i.OwnerId.Length == 0).Select(i => i.Id).ToList();

        var kids = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var item in items)
            kids[item.Id] = tree.Children(item.Id).Where(byId.ContainsKey).ToList();

        // How much room each subtree needs, deepest first so a node is measured after everything
        // under it. An explicit stack rather than recursion, because a chain of a few thousand nodes
        // is a shape this has to survive and the walk producing these is not depth limited.
        var extent = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (string id in DeepestFirst(roots, kids))
        {
            double inner = -rowGap;
            foreach (string child in kids[id]) inner += extent[child] + rowGap;
            extent[id] = Math.Max(byId[id].Height, Math.Max(inner, 0));
        }

        // Outermost first, handing each subtree a band of its own. Bands do not overlap, so two
        // families cannot collide and nothing has to be pushed apart afterwards.
        var todo = new Queue<(string Id, double Top)>();

        double nextRoot = 0;
        foreach (string root in roots)
        {
            todo.Enqueue((root, nextRoot));
            nextRoot += extent[root] + rowGap;
        }

        while (todo.Count > 0)
        {
            var (id, top) = todo.Dequeue();

            // The node sits in the middle of its own band, which is the middle of its family.
            y[id] = top + (extent[id] - byId[id].Height) / 2;

            var children = kids[id];
            if (children.Count == 0) continue;

            double inner = -rowGap;
            foreach (string child in children) inner += extent[child] + rowGap;

            // A node taller than everything under it leaves its family centred inside the band
            // rather than pinned to the top of it.
            double at = top + (extent[id] - inner) / 2;

            foreach (string child in children)
            {
                todo.Enqueue((child, at));
                at += extent[child] + rowGap;
            }
        }

        // Anything the walk never reached still needs a number rather than a missing key.
        foreach (var item in items)
            if (!y.ContainsKey(item.Id)) y[item.Id] = 0;

        // A node the user dragged sits where they left it, whatever its band said.
        foreach (var (id, at) in pinned)
            if (byId.ContainsKey(id)) y[id] = at;

        ClearPinned(items, y, pinned, tree, byId, rowGap);
        return y;
    }

    /// Nodes with everything they own listed before them.
    private static List<string> DeepestFirst(IReadOnlyList<string> roots,
                                             Dictionary<string, List<string>> kids)
    {
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<(string Id, bool Ready)>();

        for (int i = roots.Count - 1; i >= 0; i--) stack.Push((roots[i], false));

        while (stack.Count > 0)
        {
            var (id, ready) = stack.Pop();
            if (ready) { order.Add(id); continue; }
            if (!seen.Add(id)) continue;

            stack.Push((id, true));
            var children = kids[id];
            for (int i = children.Count - 1; i >= 0; i--) stack.Push((children[i], false));
        }

        return order;
    }

    /// Pushes families clear of anything the user pinned.
    ///
    /// This is the only case left where something has to move, because bands already keep families
    /// apart from each other. A pinned node is not part of any band: it is an obstacle, families go
    /// round it, and it never goes anywhere itself.
    ///
    /// The unit that moves is the family, not the node. Moving one member and not the rest is what
    /// splits a family and brings the long wires back, so a collision moves the whole group by one
    /// delta, which leaves the spacing inside it exactly as it was, and everything each member owns
    /// moves with it so the shape below arrives intact.
    private static void ClearPinned(IReadOnlyList<Item> items, Dictionary<string, double> y,
                                    IReadOnlyDictionary<string, double> pinned,
                                    GraphOwnership.Tree tree,
                                    Dictionary<string, Item> byId, double rowGap)
    {
        if (pinned.Count == 0) return;

        var columns = items.Select(i => i.Column).Distinct().OrderBy(c => c).ToList();

        // Bounded because a cycle in ownership would otherwise spin here. The ownership tests rule
        // that out, so this is a backstop rather than part of the design.
        for (int pass = 0; pass < 32; pass++)
        {
            bool moved = false;

            foreach (int column in columns)
            {
                var here = items.Where(i => i.Column == column).ToList();

                var blocks = here.Where(i => pinned.ContainsKey(i.Id))
                                 .Select(i => (Top: y[i.Id], Bottom: y[i.Id] + i.Height))
                                 .OrderBy(b => b.Top).ToList();
                if (blocks.Count == 0) continue;

                // Keyed by owner, so a family is one group. A walk root is keyed by itself, since
                // roots share an empty owner and are not a family in any useful sense.
                var groups = new Dictionary<string, (List<string> Members, double Top, double Bottom)>(
                    StringComparer.Ordinal);

                foreach (var item in here.Where(i => !pinned.ContainsKey(i.Id))
                                         .OrderBy(i => y[i.Id])
                                         .ThenBy(i => i.Id, StringComparer.Ordinal))
                {
                    string key = item.OwnerId.Length > 0 ? item.OwnerId : " root:" + item.Id;

                    if (!groups.TryGetValue(key, out var group))
                        group = (new List<string>(), double.MaxValue, double.MinValue);

                    group.Members.Add(item.Id);
                    groups[key] = (group.Members,
                                   Math.Min(group.Top, y[item.Id]),
                                   Math.Max(group.Bottom, y[item.Id] + item.Height));
                }

                foreach (var key in groups.Keys.OrderBy(k => groups[k].Top)
                                          .ThenBy(k => k, StringComparer.Ordinal).ToList())
                {
                    var group = groups[key];
                    double want = group.Top;
                    double height = group.Bottom - group.Top;

                    // Clear every pin the family would now sit across. Repeated because clearing one
                    // can land it on the next.
                    for (bool again = true; again;)
                    {
                        again = false;
                        foreach (var block in blocks)
                        {
                            if (want + height + rowGap <= block.Top || want >= block.Bottom + rowGap) continue;
                            want = block.Bottom + rowGap;
                            again = true;
                        }
                    }

                    double delta = want - group.Top;
                    if (delta <= 0.001) continue;

                    foreach (string id in group.Members) Shift(id, delta, y, pinned, tree, byId);
                    groups[key] = (group.Members, group.Top + delta, group.Bottom + delta);
                    moved = true;
                }
            }

            if (!moved) return;
        }
    }

    /// Moves a node and everything it owns by the same amount, so a family arrives at its new place
    /// in the shape it left. A pinned node under it is skipped rather than dragged.
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
