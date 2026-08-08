using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// Creating a node and hanging it off an existing one.
//
// Each parent class holds its children differently, and the wrong shape gives a file hkxpack accepts
// and the engine cannot read. A state machine does not hold generators, it holds state infos that
// hold generators; a blender holds weighted child wrappers. Attaching is therefore per class rather
// than one generic "add to children".
public static class GraphAuthor
{
    public static IEnumerable<string> Kinds => GeneratorEditor.Kinds.Keys;

    // Parents that can take a generator, with the wording used in the status line.
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

    // Creates the node and attaches it in one step when a usable parent is given. An unattached node
    // is still a real object in the file, it just has nothing pointing at it yet.
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

    // Objects nothing points at. A file can legitimately contain a few, but after an edit session
    // these are usually nodes the user meant to hook up and did not.
    public static List<HkObject> Unattached(BehaviourGraphModel model)
    {
        var referenced = new HashSet<string>();
        foreach (var obj in model.Objects)
        {
            foreach (var value in obj.Scalars.Values)
                if (value.StartsWith('#')) referenced.Add(value[1..]);
            foreach (var list in obj.Lists.Values)
                foreach (string token in list)
                    if (token.StartsWith('#')) referenced.Add(token[1..]);
            foreach (var rows in obj.StructLists.Values)
                foreach (var row in rows)
                    foreach (var value in row.Values)
                        if (value.StartsWith('#')) referenced.Add(value[1..]);
            // Named nested objects, such as an event's payload, hold references too. Missing these
            // reported every hkbStringEventPayload in a vanilla graph as unreachable.
            foreach (var members in obj.Structs.Values)
                foreach (var value in members.Values)
                    if (value.StartsWith('#')) referenced.Add(value[1..]);
        }

        return model.Objects.Where(o => !referenced.Contains(o.Id) && IsNode(o.Class)).ToList();
    }

    // Deleting these takes the file with it: the graph header, the symbol tables and the container
    // everything hangs off. Nothing else is protected, so a node that shipped with the game is as
    // deletable as one just made.
    private static readonly HashSet<string> Structural = new(StringComparer.Ordinal)
    {
        "hkRootLevelContainer", "hkbBehaviorGraph", "hkbBehaviorGraphData",
        "hkbBehaviorGraphStringData", "hkbVariableValueSet", "hkbProjectData",
        "hkbProjectStringData", "hkbCharacterData", "hkbCharacterStringData",
    };

    public static bool CanDelete(string className) => !Structural.Contains(className);

    // Removes a node and breaks every link into it first, which is what a blueprint editor does.
    // Refusing while references exist made vanilla nodes undeletable in practice, since almost
    // everything in a shipped graph is referenced by something.
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

            // A blender child exists only to hold one generator. Once that is gone it is litter the
            // validator would report as unreachable, so it goes with it.
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

    // Clears every reference to target held by this one object, whichever shape it is in.
    private static string Detach(string xml, HkObject holder, string targetId)
    {
        string token = "#" + targetId;

        foreach (var (field, value) in holder.Scalars.Where(p => p.Value == token).ToList())
            xml = HkxTextEdit.SetParam(xml, holder.Id, field, "null");

        foreach (var (field, list) in holder.Lists)
        {
            // Back to front, because removing an element renumbers the ones after it.
            var indices = list.Select((v, i) => (v, i)).Where(p => p.v == token)
                              .Select(p => p.i).OrderByDescending(i => i).ToList();
            foreach (int index in indices)
                xml = HkxTextEdit.ArrayRemoveAt(xml, holder.Id, field, index);
        }

        // A pointer inside an element of an array of structs, which is where a transition keeps the
        // effect it plays. These were found by the search for holders and then never cleared, so
        // deleting a blending transition effect took the object out of the document and left every
        // transition still naming it. Nothing said so: the save went out through hkxpack, which was
        // handed a file naming an object that was not in it.
        //
        // Cleared to null rather than by dropping the element. A transition with no effect is a
        // transition that snaps, which is a thing the format allows and vanilla files do; dropping
        // the element would silently delete a route between two states instead.
        foreach (var (field, rows) in holder.StructLists)
            for (int row = 0; row < rows.Count; row++)
                foreach (var (member, value) in rows[row].Where(p => p.Value == token).ToList())
                    xml = HkxTextEdit.SetParamAt(xml, holder.Id, $"{field}[{row}].{member}", "null");

        return xml;
    }

    // Which objects the canvas should draw, and in which column.
    //
    // Walking outwards from the root alone is not enough. Retargeting a link, which is the ordinary
    // way to change what a node points at, detaches whatever it used to point at along with
    // everything under it. Those objects are still in the file and still referenced by their own
    // parents, so they are neither reachable from the root nor unattached, and drawing only the two
    // makes an entire subtree vanish the moment a link is dragged.
    //
    // So every detached subtree gets walked as well, from its own head.
    public static List<(HkObject Node, int Column)> Layout(BehaviourGraphModel model, int max)
    {
        var placed = new Dictionary<string, int>();
        var order = new List<(HkObject, int)>();

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

    private static int Walk(BehaviourGraphModel model, HkObject from, int column,
                            Dictionary<string, int> placed, List<(HkObject, int)> order, int max)
    {
        var queue = new Queue<(HkObject Node, int Column)>();
        queue.Enqueue((from, column));
        placed[from.Id] = column;
        order.Add((from, column));
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
                order.Add((next, depth + 1));
                queue.Enqueue((next, depth + 1));
                if (order.Count >= max) break;
            }
        }
        return deepest;
    }

    // Everything the object points at, including references buried in array elements such as a
    // transition's blend effect, which the port list does not carry.
    private static IEnumerable<string> Targets(BehaviourGraphModel model, HkObject obj)
    {
        foreach (var slot in GraphLinks.OutSlots(model, obj))
            foreach (string target in slot.Targets)
                yield return target;

        foreach (var rows in obj.StructLists.Values)
            foreach (var row in rows)
                foreach (string value in row.Values)
                    if (value.StartsWith('#')) yield return value[1..];
    }

    // Only things that produce or shape a pose count. Vanilla ships plenty of unreferenced
    // hkbStringEventPayload leftovers, and reporting those buries the one node the user forgot to
    // hook up under sixteen that never mattered.
    public static bool IsNode(string className) =>
        className.EndsWith("Generator", StringComparison.Ordinal)
        || className.EndsWith("Modifier", StringComparison.Ordinal)
        || className == "hkbStateMachine"
        || className == "hkbStateMachineStateInfo"
        || className == "hkbModifierList"
        || className == "hkbLayer";
}
