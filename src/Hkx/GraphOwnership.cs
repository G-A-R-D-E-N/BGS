using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

















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

                if (!_owner.TryAdd(id, ownerId)) continue;
                if (ownerId.Length == 0) continue;

                if (!_children.TryGetValue(ownerId, out var kids))
                    _children[ownerId] = kids = new List<string>();
                kids.Add(id);
            }
        }



        public IReadOnlyList<string> Children(string id) =>
            _children.TryGetValue(id, out var kids) ? kids : None;



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







        public bool Hidden(ISet<string> collapsed, string id) =>
            collapsed.Count > 0 && Chain(id).Any(collapsed.Contains);






        public int HiddenBy(ISet<string> collapsed, string id)
        {
            if (!collapsed.Contains(id)) return 0;
            if (Chain(id).Any(collapsed.Contains)) return 0;

            var without = new HashSet<string>(collapsed, StringComparer.Ordinal);
            without.Remove(id);

            return Under(id).Count(n => !Hidden(without, n));
        }






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
