using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// A line saying what one element of an array of structs is, so the panel can collapse the element
// behind it.
//
// The panel used to show an array of structs as one flat run of boxes, because that is how the file
// writes it: five transitions of sixteen fields each is eighty boxes, with `enterEventId` and
// `exitEventId` appearing ten times between them. Reading a state machine off that is not practical,
// and people were reading the unpacked XML instead, where hkxpack writes a comment naming the event
// and the target above each element.
//
// This is that comment, built from the file rather than copied from hkxpack's. A transition is the
// case worth doing: it is the one whose meaning is entirely in two numbers that resolve to names
// held somewhere else in the file.
//
// Anything else gets nothing, and the panel shows the element's index alone. That is deliberate. An
// element with no summary reads as an element with no summary; an invented one reads as a fact.
public static class ElementSummary
{
    /// A line per element of an array of structs the object holds, keyed the way `ClassFields`
    /// groups them: `transitions[0]`, `transitions[1]`. Missing keys are the ordinary case.
    public static Dictionary<string, string> For(BehaviourGraphModel model, string objectId)
    {
        var lines = new Dictionary<string, string>(StringComparer.Ordinal);

        var array = model.Get(objectId);
        if (array == null || array.Class != "hkbStateMachineTransitionInfoArray") return lines;

        string machineId = MachineOwning(model, objectId);
        if (machineId.Length == 0) return lines;

        var events = SymbolEditor.EventNames(model);
        var states = StateEditor.States(model, machineId)
                                .GroupBy(s => s.StateId)
                                .ToDictionary(g => g.Key, g => g.First().Name, EqualityComparer<int>.Default);

        array.StructLists.TryGetValue("transitions", out var elements);

        foreach (var row in StateEditor.Transitions(model, machineId))
        {
            if (row.ArrayId != objectId) continue;
            string flags = elements != null && row.Index < elements.Count
                           && elements[row.Index].TryGetValue("flags", out var f) ? f : "";
            lines[$"transitions[{row.Index}]"] = Line(row, events, states, flags);
        }

        return lines;
    }

    /// `164  dynIdleLoop  ->  100 EAP_dynIdleLoop_A`, which is the two numbers that matter and the
    /// names they resolve to. A number whose name is not in the file is shown as the number alone
    /// rather than as a blank, because an event id with no declared name is a real thing to find and
    /// hiding it would be the wrong kind of tidy.
    private static string Line(StateEditor.TransitionRow row,
                               IReadOnlyList<string> events, IReadOnlyDictionary<int, string> states,
                               string flags)
    {
        // A wildcard says which kind it is, because the two are different rules. Local fires from
        // any state of the machine that declares it; global fires from anywhere at all, including
        // while a machine nested deeper is the one running. Across the vanilla data 2,034 are local
        // and 594 global, so neither is the rare case that could be left unsaid.
        string from = row.Wildcard
            ? Kind(flags) switch
            {
                Wildcard.Global => "from anywhere",
                Wildcard.Local => "from any state here",
                _ => "any state",
            }
            : "";
        string on = row.EventId >= 0 && row.EventId < events.Count
            ? $"{row.EventId} {events[row.EventId]}"
            : row.EventId < 0 ? "no event" : $"{row.EventId}";

        string to = states.TryGetValue(row.ToStateId, out var name) && name.Length > 0
            ? $"{row.ToStateId} {name}"
            : $"state {row.ToStateId}";

        // A nested target is a state inside the state being entered, and this tool does not follow
        // one. Saying so beats printing the outer target as if it were the whole answer.
        if (row.ToNestedStateId != 0) to += $", then nested {row.ToNestedStateId}";

        return (from.Length > 0 ? from + "  " : "") + on + "  ->  " + to;
    }

    public enum Wildcard { None, Local, Global }

    /// Which kind of wildcard a flags value declares.
    ///
    /// The value has to be decoded a bit at a time rather than read as text. hkxpack prints a name
    /// when the value is exactly one declared flag and the bare number when it is a combination, and
    /// a wildcard almost always carries more than one bit, so matching on the string finds nothing
    /// on the cases that matter.
    public static Wildcard Kind(string flags)
    {
        var declared = HavokClassTypes.Shipped.Enum("hkbStateMachineTransitionInfo", "TransitionFlags");
        if (declared == null) return Wildcard.None;

        long bits;
        if (!long.TryParse(flags.Trim(), out bits))
        {
            bits = 0;
            foreach (string part in flags.Split('|', StringSplitOptions.RemoveEmptyEntries))
                foreach (var (name, value) in declared)
                    if (name == part.Trim()) bits |= value;
        }

        long global = declared.FirstOrDefault(v => v.Key == "FLAG_IS_GLOBAL_WILDCARD").Value;
        long local = declared.FirstOrDefault(v => v.Key == "FLAG_IS_LOCAL_WILDCARD").Value;

        if (global != 0 && (bits & global) == global) return Wildcard.Global;
        if (local != 0 && (bits & local) == local) return Wildcard.Local;
        return Wildcard.None;
    }

    /// The state machine an array of transitions belongs to, by finding the one that points at it.
    /// A transition array carries no way back to its machine, and the numbers in it mean nothing
    /// without one: a `toStateId` is an index into that machine's states and no other's.
    public static string MachineOwning(BehaviourGraphModel model, string arrayId)
    {
        foreach (var obj in model.Objects)
        {
            if (obj.Class != "hkbStateMachine") continue;
            if (obj.Ref("wildcardTransitions") == arrayId) return obj.Id;

            foreach (string stateId in obj.Refs("states"))
                if (model.Get(stateId)?.Ref("transitions") == arrayId) return obj.Id;
        }
        return "";
    }
}
