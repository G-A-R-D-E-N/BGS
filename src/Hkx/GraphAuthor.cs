using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;







public static class GraphAuthor
{
    public static IEnumerable<string> Kinds => GeneratorEditor.Kinds.Keys;


    public static string AttachmentFor(string parentClass) => parentClass switch
    {
        "hkbBehaviorGraph" => "root generator",
        "hkbStateMachine" => "a new state",
        "hkbStateMachineStateInfo" => "the state's generator",
        "hkbModifierGenerator" => "the wrapped generator",
        "hkbBlenderGenerator" => "a weighted child",
        "hkbManualSelectorGenerator" => "one of the selectable generators",
        _ => "",
    };

    public static string Attach(string xml, string parentId, string childId, string childName, out string how)
    {
        var model = BehaviourGraphModel.Parse(xml);
        var parent = model.Get(parentId) ?? throw new ArgumentException($"#{parentId} is not in this file");
        if (model.Get(childId) is null) throw new ArgumentException($"#{childId} is not in this file");

        string child = "#" + childId;
        how = AttachmentFor(parent.Class);
        if (how.Length == 0)
            throw new ArgumentException(
                $"a {parent.Class} has no generator slot, so pick a state machine, blender, selector, " +
                "modifier or state as the parent");

        switch (parent.Class)
        {
            case "hkbBehaviorGraph":
                return HkxTextEdit.SetParam(xml, parentId, "rootGenerator", child);
            case "hkbStateMachineStateInfo":
            case "hkbModifierGenerator":
                return HkxTextEdit.SetParam(xml, parentId, "generator", child);
            case "hkbManualSelectorGenerator":
                return GeneratorEditor.AttachToSelector(xml, parentId, child);
            case "hkbBlenderGenerator":
                return GeneratorEditor.AddBlenderChild(xml, parentId, child, 1.0f, out _);
            default:
                xml = StateEditor.AddState(xml, parentId, childName, child, out _, out int stateId);
                how = $"a new state, stateId {stateId}";
                return xml;
        }
    }



    public static string AddNode(string xml, string kind, string name, string animation,
                                 string parentId, out string newId, out string note)
    {
        xml = GeneratorEditor.Add(xml, kind, name, animation, "", out newId);

        if (string.IsNullOrEmpty(parentId))
        {
            note = $"created {name}, not attached to anything yet";
            return xml;
        }

        try
        {
            xml = Attach(xml, parentId, newId, name, out string how);
            note = $"created {name} and attached it as {how} of #{parentId}";
            return xml;
        }
        catch (ArgumentException ex)
        {
            note = $"created {name} but left it unattached: {ex.Message}";
            return xml;
        }
    }



    public static List<HkObject> Unattached(BehaviourGraphModel model)
    {



        var referenced = HkReferences.Targets(model);



        return model.Objects.Where(o => !referenced.Contains(o.Id) && IsNode(o.Class)).ToList();
    }




    private static readonly HashSet<string> Structural = new(StringComparer.Ordinal)
    {
        "hkRootLevelContainer", "hkbBehaviorGraph", "hkbBehaviorGraphData",
        "hkbBehaviorGraphStringData", "hkbVariableValueSet", "hkbProjectData",
        "hkbProjectStringData", "hkbCharacterData", "hkbCharacterStringData",
    };

    public static bool CanDelete(string className) => !Structural.Contains(className);




    public static string DeleteNode(string xml, string id, out string note)
    {
        var model = BehaviourGraphModel.Parse(xml);
        var target = model.Get(id) ?? throw new ArgumentException($"#{id} is not in this file");

        if (!CanDelete(target.Class))
            throw new InvalidOperationException(
                $"#{id} is a {target.Class}, which the file is built around; deleting it would leave " +
                "a graph the engine cannot load");

        string name = target.Str("name");
        var cleared = new List<string>();
        var alsoGone = new List<string>();

        foreach (string holderId in GeneratorEditor.ReferencesTo(model, id).ToList())
        {
            var holder = BehaviourGraphModel.Parse(xml).Get(holderId);
            if (holder == null) continue;

            xml = Detach(xml, holder, id);
            cleared.Add($"#{holderId} {holder.Class}");



            if (holder.Class == "hkbBlenderGeneratorChild")
            {
                xml = DeleteNode(xml, holderId, out _);
                alsoGone.Add("#" + holderId);
            }
        }

        var (start, length) = HkxTextEdit.ObjectBlock(xml, id);
        if (start >= 0) xml = xml.Remove(start, length);

        string extra = alsoGone.Count > 0 ? $", and its wrapper {string.Join(", ", alsoGone)}" : "";
        note = cleared.Count == 0
            ? $"deleted #{id} {target.Class} '{name}', which nothing referenced"
            : $"deleted #{id} {target.Class} '{name}'{extra}, and cleared {cleared.Count} link" +
              $"{(cleared.Count == 1 ? "" : "s")} into it from {string.Join(", ", cleared.Take(3))}";
        return xml;
    }
















    private static string Detach(string xml, HkObject holder, string targetId)
    {
        var sites = HkReferences.In(holder).Where(s => s.Target == targetId).ToList();




        foreach (var site in sites.Where(s => s.How == HkReferences.Held.Scalar))
            xml = HkxTextEdit.SetParam(xml, holder.Id, site.Field, "null");

        foreach (var site in sites.Where(s => s.How is HkReferences.Held.StructListMember
                                                   or HkReferences.Held.StructMember))
            xml = HkxTextEdit.SetParamAt(xml, holder.Id, site.Path(), "null");


        foreach (var site in sites.Where(s => s.How == HkReferences.Held.ListElement)
                                  .OrderByDescending(s => s.Index))
            xml = HkxTextEdit.ArrayRemoveAt(xml, holder.Id, site.Field, site.Index);

        return xml;
    }

















    public static List<(HkObject Node, int Column, string OwnerId)> Layout(BehaviourGraphModel model, int max) =>
        Layout(model, max, out _);

    public static List<(HkObject Node, int Column, string OwnerId)> Layout(BehaviourGraphModel model, int max,
                                                                           out bool truncated)
    {
        truncated = false;
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

        // The cap is the only reason a drawable node is missing: the root walk covers
        // everything reachable and the detached pass covers everything else.
        truncated = order.Count >= max &&
                    model.Objects.Any(o => IsNode(o.Class) && !placed.ContainsKey(o.Id));
        return order;
    }

    private static int Walk(BehaviourGraphModel model, HkObject from, int column,
                            Dictionary<string, int> placed, List<(HkObject, int, string)> order, int max)
    {
        var queue = new Queue<(HkObject Node, int Column)>();
        queue.Enqueue((from, column));
        placed[from.Id] = column;



        order.Add((from, column, ""));
        int deepest = column;

        while (queue.Count > 0 && order.Count < max)
        {
            var (current, depth) = queue.Dequeue();
            foreach (string target in PointsAt(model, current))
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









    public static IEnumerable<string> PointsAt(BehaviourGraphModel model, HkObject obj)
    {
        foreach (var slot in GraphLinks.OutSlots(model, obj))
            foreach (string target in slot.Targets)
                yield return target;

        foreach (var rows in obj.StructLists.Values)
            foreach (var row in rows)
                foreach (string value in row.Values)
                    if (value.StartsWith('#')) yield return value[1..];

        foreach (var fields in obj.Structs.Values)
            foreach (string value in fields.Values)
                if (value.StartsWith('#')) yield return value[1..];
    }




    public static bool IsNode(string className) =>
        className.EndsWith("Generator", StringComparison.Ordinal)
        || className.EndsWith("Modifier", StringComparison.Ordinal)
        || className == "hkbStateMachine"
        || className == "hkbStateMachineStateInfo"
        || className == "hkbModifierList"
        || className == "hkbLayer";
}
