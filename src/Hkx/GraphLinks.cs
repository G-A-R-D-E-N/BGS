using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// What can be wired to what, so the graph view can offer a port per link a node is allowed to have
// rather than only per link it already has. Without this a node with an empty generator field has
// nothing to drag from and the connection can never be made.
//
// Two sources, on purpose. The table below covers the fields a class can hold even when they are
// empty, which cannot be discovered from the file. Anything else currently holding a reference is
// picked up from the object itself, so an unusual class still shows its real links.
public static class GraphLinks
{
    public sealed class Slot
    {
        public string Field = "";
        public bool Array;
        public List<string> Targets = new();
        public override string ToString() => Array ? $"{Field}[]" : Field;
    }

    private sealed class Shape
    {
        public string[] Scalars = Array.Empty<string>();
        public string[] Arrays = Array.Empty<string>();
    }

    private static readonly Dictionary<string, Shape> Shapes = new(StringComparer.Ordinal)
    {
        ["hkbBehaviorGraph"] = new() { Scalars = new[] { "rootGenerator" } },
        ["hkbStateMachine"] = new() { Scalars = new[] { "wildcardTransitions" }, Arrays = new[] { "states" } },
        ["hkbStateMachineStateInfo"] = new() { Scalars = new[] { "generator", "transitions" } },
        ["hkbBlenderGenerator"] = new() { Arrays = new[] { "children" } },
        ["hkbBlenderGeneratorChild"] = new() { Scalars = new[] { "generator" } },
        ["hkbManualSelectorGenerator"] = new() { Arrays = new[] { "generators" } },
        ["hkbModifierGenerator"] = new() { Scalars = new[] { "generator", "modifier" } },
        ["hkbModifierList"] = new() { Arrays = new[] { "modifiers" } },
        ["hkbLayerGenerator"] = new() { Arrays = new[] { "layers" } },
        ["hkbLayer"] = new() { Scalars = new[] { "generator" } },
        ["hkbClipGenerator"] = new() { Scalars = new[] { "triggers" } },
        ["hkbPoseMatchingGenerator"] = new() { Arrays = new[] { "children" } },
        ["BSBoneSwitchGenerator"] = new() { Scalars = new[] { "pDefaultGenerator" }, Arrays = new[] { "BoneSwitchGeneratorBoneData" } },
        ["hkbBehaviorReferenceGenerator"] = new(),
        ["BGSGamebryoSequenceGenerator"] = new(),
    };

    // Port types, so the canvas can refuse a connection that would not work rather than letting it
    // be made and finding out later. A generator slot only takes something that produces a pose; a
    // triggers slot only takes a clip trigger array. Numbers are arbitrary, only equality matters.
    public const int KindGenerator = 1;
    public const int KindModifier = 2;
    public const int KindTransitionArray = 3;
    public const int KindStateInfo = 4;
    public const int KindTriggerArray = 5;
    public const int KindBlenderChild = 6;
    public const int KindBoneWeights = 7;
    public const int KindStates = 8;
    public const int KindChildren = 9;
    public const int KindOther = 99;

    // What a field will accept.
    public static int Accepts(string field) => field switch
    {
        "rootGenerator" or "generator" or "generators" or "pDefaultGenerator" or "pBlenderGenerator"
            => KindGenerator,
        "modifier" or "modifiers" => KindModifier,
        "transitions" or "wildcardTransitions" => KindTransitionArray,
        "states" => KindStates,
        "children" or "layers" => KindChildren,
        "triggers" => KindTriggerArray,
        "boneWeights" => KindBoneWeights,
        _ => KindOther,
    };

    // What an object can be used as.
    public static int FamilyOf(string className) => className switch
    {
        "hkbStateMachineStateInfo" => KindStateInfo,
        "hkbStateMachineTransitionInfoArray" => KindTransitionArray,
        "hkbClipTriggerArray" => KindTriggerArray,
        "hkbBlenderGeneratorChild" => KindBlenderChild,
        "hkbBoneWeightArray" => KindBoneWeights,
        "hkbLayer" => KindChildren,
        _ when className.EndsWith("Modifier", StringComparison.Ordinal) => KindModifier,
        _ when className.EndsWith("Generator", StringComparison.Ordinal) => KindGenerator,
        "hkbStateMachine" => KindGenerator,
        "hkbModifierList" => KindModifier,
        _ => KindOther,
    };

    // Pairs that are allowed even though the two numbers differ, because the connection builds
    // something rather than writing a bare reference. A states array takes a state info directly or
    // a generator that gets wrapped in one; a blender's children take either the wrapper or the
    // generator to wrap.
    public static IEnumerable<(int From, int To)> ValidPairs => new[]
    {
        (KindStates, KindStateInfo),
        (KindStates, KindGenerator),
        (KindChildren, KindBlenderChild),
        (KindChildren, KindGenerator),
        (KindModifier, KindModifier),
        (KindGenerator, KindGenerator),
    };

    public static List<Slot> OutSlots(BehaviourGraphModel model, HkObject obj)
    {
        var slots = new List<Slot>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (Shapes.TryGetValue(obj.Class, out var shape))
        {
            foreach (string field in shape.Scalars)
            {
                seen.Add(field);
                var slot = new Slot { Field = field };
                string? target = obj.Ref(field);
                if (target != null) slot.Targets.Add(target);
                slots.Add(slot);
            }
            foreach (string field in shape.Arrays)
            {
                seen.Add(field);
                slots.Add(new Slot { Field = field, Array = true, Targets = obj.Refs(field) });
            }
        }

        // Anything already wired that the table does not know about, so nothing is hidden.
        foreach (var (field, value) in obj.Scalars)
            if (value.StartsWith('#') && seen.Add(field) && field != "variableBindingSet")
                slots.Add(new Slot { Field = field, Targets = { value[1..] } });

        foreach (var (field, list) in obj.Lists)
        {
            if (!seen.Add(field)) continue;
            var refs = list.Where(x => x.StartsWith('#')).Select(x => x[1..]).ToList();
            if (refs.Count > 0) slots.Add(new Slot { Field = field, Array = true, Targets = refs });
        }

        return slots;
    }

    public static string Connect(string xml, string fromId, string field, string toId, out string note)
    {
        var model = BehaviourGraphModel.Parse(xml);
        var from = model.Get(fromId) ?? throw new ArgumentException($"#{fromId} is not in this file");
        var to = model.Get(toId) ?? throw new ArgumentException($"#{toId} is not in this file");

        var slot = OutSlots(model, from).FirstOrDefault(s => s.Field == field)
                   ?? throw new ArgumentException($"a {from.Class} has no {field} to connect");

        if (!slot.Array)
        {
            // A single link can only hold one thing, so connecting to a full one drops what was
            // there. Say so, because the thing dropped keeps its own subtree and simply stops being
            // reachable, which is easy to mistake for it having been deleted.
            string had = slot.Targets.FirstOrDefault() ?? "";
            note = had.Length > 0 && had != toId
                ? $"#{fromId}.{field} now points at #{toId}, replacing #{had}, which is now detached"
                : $"#{fromId}.{field} now points at #{toId}";
            return HkxTextEdit.SetParam(xml, fromId, field, "#" + toId);
        }

        // A blender holds weighted wrappers, and a state machine holds state infos, so dropping a
        // generator on either has to build the thing that actually goes in the array.
        if (from.Class == "hkbBlenderGenerator" && to.Class != "hkbBlenderGeneratorChild")
        {
            xml = GeneratorEditor.AddBlenderChild(xml, fromId, "#" + toId, 1.0f, out string wrapper);
            note = $"#{toId} added to #{fromId} as child #{wrapper}, weight 1";
            return xml;
        }

        if (from.Class == "hkbStateMachine" && field == "states" && to.Class != "hkbStateMachineStateInfo")
        {
            string name = to.Str("name");
            if (name.Length == 0) name = to.Class;
            xml = StateEditor.AddState(xml, fromId, name, "#" + toId, out _, out int stateId);
            note = $"#{toId} wrapped in a new state {stateId} on #{fromId}";
            return xml;
        }

        if (slot.Targets.Contains(toId)) { note = $"#{fromId}.{field} already contains #{toId}"; return xml; }

        note = $"#{toId} added to #{fromId}.{field}";
        return HkxTextEdit.ArrayAppend(xml, fromId, field, $"                #{toId}");
    }

    public static string Disconnect(string xml, string fromId, string field, string toId, out string note)
    {
        var model = BehaviourGraphModel.Parse(xml);
        var from = model.Get(fromId) ?? throw new ArgumentException($"#{fromId} is not in this file");

        var slot = OutSlots(model, from).FirstOrDefault(s => s.Field == field)
                   ?? throw new ArgumentException($"a {from.Class} has no {field}");

        if (!slot.Array)
        {
            note = $"#{fromId}.{field} cleared";
            return HkxTextEdit.SetParam(xml, fromId, field, "null");
        }

        int index = slot.Targets.IndexOf(toId);
        if (index < 0) { note = $"#{fromId}.{field} does not contain #{toId}"; return xml; }

        xml = HkxTextEdit.ArrayRemoveAt(xml, fromId, field, index);

        // The wrapper only existed to hold that child, so leaving it behind would be litter the
        // validator then reports as unreachable.
        var removed = model.Get(toId);
        if (removed != null && removed.Class == "hkbBlenderGeneratorChild")
        {
            xml = GeneratorEditor.Remove(xml, toId, force: false, out var blockers);
            if (blockers.Count == 0)
            {
                note = $"#{toId} removed from #{fromId}.{field}, and the empty wrapper deleted";
                return xml;
            }
        }

        note = $"#{toId} removed from #{fromId}.{field}";
        return xml;
    }
}
