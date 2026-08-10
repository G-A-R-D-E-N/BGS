using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;



public static class StructuredFlowLayout
{
    public enum NodeKind
    {
        Root,
        Machine,
        State,
        Helper,
    }

    public sealed record Item(string Id, string OwnerId, string MachineId, string ParentMachineId,
                              int Depth, int SiblingOrder, NodeKind Kind,
                              IReadOnlyList<string> StructuralAncestorIds);

    public sealed record Machine(string Id, string ParentMachineId, int Depth,
                                 IReadOnlyList<string> MemberIds);

    public sealed class Plan
    {
        private readonly IReadOnlyDictionary<string, Item> _items;
        private readonly IReadOnlyList<Machine> _machines;

        internal Plan(IReadOnlyDictionary<string, Item> items, IReadOnlyList<Machine> machines)
        {
            _items = items;
            _machines = machines;
        }

        public IReadOnlyDictionary<string, Item> Items => _items;
        public IReadOnlyList<Machine> Machines => _machines;

        public Item Item(string id) => _items[id];
    }

    public static Plan Of(IReadOnlyList<(HkObject Node, int Column, string OwnerId)> placed)
    {
        var byId = placed.ToDictionary(p => p.Node.Id, p => p, StringComparer.Ordinal);
        var items = new Dictionary<string, Item>(StringComparer.Ordinal);
        foreach (var (node, _, ownerId) in placed)
        {
            var ancestors = Ancestors(node.Id, byId);
            var kind = KindOf(node, ownerId.Length == 0);
            string machineId = NearestMachine(node.Id, ancestors, byId, includeSelf: true);
            string parentMachineId = kind == NodeKind.Machine
                ? NearestMachine(node.Id, ancestors, byId, includeSelf: false)
                : "";
            int depth = ancestors.Count;
            int siblingOrder = SiblingOrder(node.Id, ownerId, placed);

            items[node.Id] = new Item(node.Id, ownerId, machineId, parentMachineId, depth,
                                      siblingOrder, kind, ancestors);
        }

        var machines = placed
            .Where(p => p.Node.Class == "hkbStateMachine")
            .Select(p =>
            {
                var item = items[p.Node.Id];
                var members = placed.Where(q => items[q.Node.Id].MachineId == p.Node.Id)
                                    .Select(q => q.Node.Id).ToList();
                return new Machine(p.Node.Id, item.ParentMachineId, item.Depth, members);
            })
            .ToList();

        return new Plan(items, machines);
    }

    private static NodeKind KindOf(HkObject node, bool root) =>
        root ? NodeKind.Root
        : node.Class == "hkbStateMachine" ? NodeKind.Machine
        : node.Class == "hkbStateMachineStateInfo" ? NodeKind.State
        : NodeKind.Helper;

    private static List<string> Ancestors(string id,
        IReadOnlyDictionary<string, (HkObject Node, int Column, string OwnerId)> byId)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { id };
        string at = id;
        while (byId.TryGetValue(at, out var item) && item.OwnerId.Length > 0 && seen.Add(item.OwnerId))
        {
            result.Add(item.OwnerId);
            at = item.OwnerId;
        }
        result.Reverse();
        return result;
    }

    private static string NearestMachine(string id, IReadOnlyList<string> ancestors,
        IReadOnlyDictionary<string, (HkObject Node, int Column, string OwnerId)> byId, bool includeSelf)
    {
        if (includeSelf && byId.TryGetValue(id, out var self) && self.Node.Class == "hkbStateMachine")
            return id;

        for (int i = ancestors.Count - 1; i >= 0; i--)
            if (byId.TryGetValue(ancestors[i], out var ancestor)
                && ancestor.Node.Class == "hkbStateMachine")
                return ancestor.Node.Id;
        return "";
    }

    private static int SiblingOrder(string id, string ownerId,
                                    IReadOnlyList<(HkObject Node, int Column, string OwnerId)> placed)
    {
        int order = 0;
        foreach (var (node, _, owner) in placed)
        {
            if (owner != ownerId) continue;
            if (node.Id == id) return order;
            order++;
        }
        return order;
    }
}
