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
