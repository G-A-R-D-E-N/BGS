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
// ownership: two rules that disagree about who owns a node give a canvas where dragging and
// collapsing disagree, and that is not a bug anyone would enjoy finding. If a later feature wants a
// different grouping it gets a different name.
//
// This holds no state of its own beyond the map. Whether a node is collapsed is the canvas's
// business and is passed in, so the same tree can answer for any collapsed set and nothing here has
// to be kept in step with the view.
public static class GraphOwnership
{
    public sealed class Tree
    {
        /// Each node's owner, empty for a walk root.
        public IReadOnlyDictionary<string, string> Owner => _owner;

        private readonly Dictionary<string, string> _owner = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _children = new(StringComparer.Ordinal);
        private static readonly IReadOnlyList<string> None = Array.Empty<string>();

        internal Tree(IEnumerable<(string Id, string OwnerId)> placed)
        {
            foreach (var (id, ownerId) in placed)
            {
                // A node placed twice keeps the first answer, the same rule the walk itself uses.
                if (!_owner.TryAdd(id, ownerId)) continue;
                if (ownerId.Length == 0) continue;

                if (!_children.TryGetValue(ownerId, out var kids))
                    _children[ownerId] = kids = new List<string>();
                kids.Add(id);
            }
        }

        /// The nodes this one owns, in the order the walk placed them, which is the order they are
        /// stacked in so a family reads down the canvas the way the file lists it.
        public IReadOnlyList<string> Children(string id) =>
            _children.TryGetValue(id, out var kids) ? kids : None;

        /// Every node under this one through owned links only. A node owned by another branch is not
        /// under this one however many wires run to it from here.
        public List<string> Under(string id)
        {
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal) { id };
            var stack = new Stack<string>(Children(id).Reverse());

            while (stack.Count > 0)
            {
                string at = stack.Pop();
                if (!seen.Add(at)) continue;
                found.Add(at);

                var kids = Children(at);
                for (int i = kids.Count - 1; i >= 0; i--) stack.Push(kids[i]);
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

        /// Whether a collapse anywhere above this node hides it.
        ///
        /// A collapsed node is not hidden by its own collapse; it is the thing you click to get the
        /// rest back. And because the chain follows owners only, a node owned by another branch is
        /// never reached from here, so collapsing one state cannot blank out part of a state
        /// somewhere else on the canvas.
        public bool Hidden(ISet<string> collapsed, string id) =>
            collapsed.Count > 0 && Chain(id).Any(collapsed.Contains);

        /// How many nodes this collapse is responsible for hiding, which is what its badge says.
        ///
        /// Owned descendants only, so a node owned by another branch never inflates it, and only
        /// those not already hidden by a collapse further up, so expanding this one brings back
        /// exactly the number it promised rather than a number that counted somebody else's work.
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
        /// would send it off at double speed and out of its family.
        public HashSet<string> Moving(IEnumerable<string> selected)
        {
            var moving = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in selected)
            {
                if (!_owner.ContainsKey(id)) continue;
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
