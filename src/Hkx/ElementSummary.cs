using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class ElementSummary
{

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

    private static string Line(StateEditor.TransitionRow row,
                               IReadOnlyList<string> events, IReadOnlyDictionary<int, string> states,
                               string flags)
    {

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

        if (row.ToNestedStateId != 0) to += $", then nested {row.ToNestedStateId}";

        return (from.Length > 0 ? from + "  " : "") + on + "  ->  " + to;
    }

    public enum Wildcard { None, Local, Global }

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
