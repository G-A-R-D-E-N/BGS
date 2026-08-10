using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

/// Static reachability over the relationships the canvas already understands. This is deliberately
/// read-only: it does not author links, change StateRoutes, or broaden GraphAuthor.PointsAt.
public static class GraphTrace
{
    public enum Direction
    {
        Upstream,
        Downstream,
        Both,
    }

    public static GraphTraceMap Of(BehaviourGraphModel model, StateRoutes routes)
    {
        var forward = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var reverse = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        void Add(string from, string to)
        {
            if (from.Length == 0 || to.Length == 0 || from == to) return;
            if (!forward.TryGetValue(from, out var leaving))
                forward[from] = leaving = new HashSet<string>(StringComparer.Ordinal);
            leaving.Add(to);

            if (!reverse.TryGetValue(to, out var arriving))
                reverse[to] = arriving = new HashSet<string>(StringComparer.Ordinal);
            arriving.Add(from);
        }

        foreach (var obj in model.Objects)
            foreach (string target in GraphAuthor.PointsAt(model, obj))
                if (model.Get(target) != null) Add(obj.Id, target);

        foreach (var route in routes.Routes)
        {
            Add(route.FromId, route.ToId);
            if (route.IntoId.Length > 0) Add(route.ToId, route.IntoId);
        }

        return new GraphTraceMap(forward, reverse);
    }

    public sealed class GraphTraceMap
    {
        private readonly IReadOnlyDictionary<string, HashSet<string>> _forward;
        private readonly IReadOnlyDictionary<string, HashSet<string>> _reverse;

        internal GraphTraceMap(IReadOnlyDictionary<string, HashSet<string>> forward,
                               IReadOnlyDictionary<string, HashSet<string>> reverse)
        {
            _forward = forward;
            _reverse = reverse;
        }

        public IReadOnlyCollection<string> Reachable(string seedId, Direction direction,
                                                      IReadOnlySet<string> visible)
        {
            if (!visible.Contains(seedId)) return Array.Empty<string>();

            var found = new HashSet<string>(StringComparer.Ordinal) { seedId };
            if (direction is Direction.Downstream or Direction.Both)
                Walk(seedId, _forward, visible, found);
            if (direction is Direction.Upstream or Direction.Both)
                Walk(seedId, _reverse, visible, found);
            return found;
        }

        private static void Walk(string seedId, IReadOnlyDictionary<string, HashSet<string>> edges,
                                 IReadOnlySet<string> visible, HashSet<string> found)
        {
            var pending = new Queue<string>();
            pending.Enqueue(seedId);

            while (pending.Count > 0)
            {
                string from = pending.Dequeue();
                if (!edges.TryGetValue(from, out var next)) continue;
                foreach (string to in next)
                    if (visible.Contains(to) && found.Add(to)) pending.Enqueue(to);
            }
        }
    }
}
