using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;






































public static class GraphLayout
{



    public sealed record Item(string Id, int Column, string OwnerId, double Height);








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








        var shape = new Dictionary<string, Dictionary<int, (double Top, double Bottom)>>(StringComparer.Ordinal);
        var own = new Dictionary<string, double>(StringComparer.Ordinal);
        var under = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (string id in DeepestFirst(roots, kids))
        {
            var children = kids[id];
            int column = byId[id].Column;
            double height = byId[id].Height;

            if (children.Count == 0)
            {
                own[id] = 0;
                shape[id] = new Dictionary<int, (double, double)> { [column] = (0, height) };
                continue;
            }

            var stacked = new Dictionary<int, (double Top, double Bottom)>();

            foreach (string child in children)
            {



                double offset = 0;
                foreach (var (col, span) in shape[child])
                    if (stacked.TryGetValue(col, out var taken))
                        offset = Math.Max(offset, taken.Bottom + rowGap - span.Top);

                under[child] = offset;

                foreach (var (col, span) in shape[child])
                {
                    double top = span.Top + offset, bottom = span.Bottom + offset;
                    stacked[col] = stacked.TryGetValue(col, out var had)
                        ? (Math.Min(had.Top, top), Math.Max(had.Bottom, bottom))
                        : (top, bottom);
                }
            }


            string first = children[0], last = children[^1];
            double firstCentre = under[first] + own[first] + byId[first].Height / 2;
            double lastCentre = under[last] + own[last] + byId[last].Height / 2;

            own[id] = (firstCentre + lastCentre) / 2 - height / 2;
            stacked[column] = (own[id], own[id] + height);



            double lift = stacked.Values.Min(s => s.Top);
            if (Math.Abs(lift) > 0.001)
            {
                own[id] -= lift;
                foreach (string child in children) under[child] -= lift;
                foreach (var col in stacked.Keys.ToList())
                    stacked[col] = (stacked[col].Top - lift, stacked[col].Bottom - lift);
            }

            shape[id] = stacked;
        }



        var todo = new Queue<(string Id, double Top)>();
        var laid = new Dictionary<int, (double Top, double Bottom)>();

        foreach (string root in roots)
        {
            double offset = 0;
            foreach (var (col, span) in shape[root])
                if (laid.TryGetValue(col, out var taken))
                    offset = Math.Max(offset, taken.Bottom + rowGap - span.Top);

            foreach (var (col, span) in shape[root])
            {
                double top = span.Top + offset, bottom = span.Bottom + offset;
                laid[col] = laid.TryGetValue(col, out var had)
                    ? (Math.Min(had.Top, top), Math.Max(had.Bottom, bottom))
                    : (top, bottom);
            }

            todo.Enqueue((root, offset));
        }

        while (todo.Count > 0)
        {
            var (id, top) = todo.Dequeue();
            y[id] = top + own[id];

            foreach (string child in kids[id]) todo.Enqueue((child, top + under[child]));
        }


        foreach (var item in items)
            if (!y.ContainsKey(item.Id)) y[item.Id] = 0;


        foreach (var (id, at) in pinned)
            if (byId.ContainsKey(id)) y[id] = at;

        ClearPinned(items, y, pinned, tree, byId, rowGap);
        return y;
    }


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











    private static void ClearPinned(IReadOnlyList<Item> items, Dictionary<string, double> y,
                                    IReadOnlyDictionary<string, double> pinned,
                                    GraphOwnership.Tree tree,
                                    Dictionary<string, Item> byId, double rowGap)
    {
        if (pinned.Count == 0) return;

        var columns = items.Select(i => i.Column).Distinct().OrderBy(c => c).ToList();



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
