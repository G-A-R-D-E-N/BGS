using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// Checks a graph before it is repacked, because hkxpack validates shape and signatures but not
// meaning: it will happily write a file whose transitions point at states that do not exist, or
// whose event ids run past the end of the event list. Those load without an error and then behave
// wrongly, which is the worst kind of failure to chase from inside the game.
public static class GraphValidator
{
    public enum Level { Error, Warning }

    public sealed class Finding
    {
        public Level Level;
        public string Where = "";
        public string What = "";
        public override string ToString() => $"{(Level == Level.Error ? "error" : "warning")}  {Where}  {What}";
    }

    public static List<Finding> Check(string xml)
    {
        var model = BehaviourGraphModel.Parse(xml);
        var found = new List<Finding>();

        CheckSymbolArrays(model, found);
        CheckDanglingReferences(model, found);
        CheckSymbolIndices(xml, model, found);
        CheckStateMachines(model, found);
        CheckBlenders(model, found);
        CheckClips(model, found);
        CheckUnattached(model, found);

        return found;
    }

    private static void Add(List<Finding> found, Level level, string where, string what) =>
        found.Add(new Finding { Level = level, Where = where, What = what });

    private static void CheckSymbolArrays(BehaviourGraphModel model, List<Finding> found)
    {
        var counts = SymbolEditor.Audit(model);
        if (!counts.VariablesConsistent)
            Add(found, Level.Error, "hkbBehaviorGraphData",
                $"the variable arrays disagree: {counts}");
        if (!counts.EventsConsistent)
            Add(found, Level.Error, "hkbBehaviorGraphData",
                $"eventNames has {counts.EventNames} entries but eventInfos has {counts.EventInfos}");
    }

    private static void CheckDanglingReferences(BehaviourGraphModel model, List<Finding> found)
    {
        foreach (var obj in model.Objects)
        {
            foreach (var (field, value) in obj.Scalars)
                if (value.StartsWith('#') && model.Get(value[1..]) == null)
                    Add(found, Level.Error, $"#{obj.Id} {obj.Class}.{field}", $"points at {value}, which is not in this file");

            foreach (var (field, list) in obj.Lists)
                foreach (string token in list)
                    if (token.StartsWith('#') && model.Get(token[1..]) == null)
                        Add(found, Level.Error, $"#{obj.Id} {obj.Class}.{field}", $"contains {token}, which is not in this file");

            foreach (var (field, rows) in obj.StructLists)
                foreach (var row in rows)
                    foreach (var (member, value) in row)
                        if (value.StartsWith('#') && model.Get(value[1..]) == null)
                            Add(found, Level.Error, $"#{obj.Id} {obj.Class}.{field}.{member}",
                                $"points at {value}, which is not in this file");
        }
    }

    private static void CheckSymbolIndices(string xml, BehaviourGraphModel model, List<Finding> found)
    {
        foreach (string unknown in SymbolIndexFixup.UnknownIndexFields(xml))
            Add(found, Level.Warning, unknown,
                "looks like an event or variable index but is not in the known table, so removing a symbol will refuse");

        int variables = SymbolEditor.VariableNames(model).Count;
        int events = SymbolEditor.EventNames(model).Count;

        foreach (string user in SymbolIndexFixup.ReferencesAtOrAbove(xml, events: false, variables))
            Add(found, Level.Error, user, $"but this graph declares only {variables} variables");
        foreach (string user in SymbolIndexFixup.ReferencesAtOrAbove(xml, events: true, events))
            Add(found, Level.Error, user, $"but this graph declares only {events} events");
    }

    private static void CheckStateMachines(BehaviourGraphModel model, List<Finding> found)
    {
        foreach (var machine in model.Objects.Where(o => o.Class == "hkbStateMachine"))
        {
            var states = StateEditor.States(model, machine.Id);
            string name = machine.Str("name");

            foreach (var group in states.GroupBy(s => s.StateId).Where(g => g.Count() > 1))
                Add(found, Level.Error, $"#{machine.Id} {name}",
                    $"stateId {group.Key} is used by {group.Count()} states, so transitions to it are ambiguous");

            foreach (var state in states.Where(s => string.IsNullOrEmpty(s.GeneratorRef) || s.GeneratorRef == "null"))
                Add(found, Level.Error, $"#{state.Id} state '{state.Name}'", "has no generator, so entering it plays nothing");

            var ids = states.Select(s => s.StateId).ToHashSet();
            foreach (var t in StateEditor.Transitions(model, machine.Id).Where(t => !ids.Contains(t.ToStateId)))
                Add(found, Level.Error, $"#{machine.Id} {name}",
                    $"a {(t.Wildcard ? "wildcard " : "")}transition targets stateId {t.ToStateId}, which no state in this machine has");

            int start = machine.Int("startStateId");
            if (states.Count > 0 && start >= 0 && !ids.Contains(start))
                Add(found, Level.Error, $"#{machine.Id} {name}", $"startStateId is {start}, which no state in this machine has");
        }
    }

    private static void CheckBlenders(BehaviourGraphModel model, List<Finding> found)
    {
        foreach (var blender in model.Objects.Where(o => o.Class == "hkbBlenderGenerator"))
            foreach (string childId in blender.Refs("children"))
            {
                var child = model.Get(childId);
                if (child != null && child.Class != "hkbBlenderGeneratorChild")
                    Add(found, Level.Error, $"#{blender.Id} {blender.Str("name")}",
                        $"child #{childId} is a {child.Class}; a blender's children must be hkbBlenderGeneratorChild wrappers");
            }
    }

    private static void CheckClips(BehaviourGraphModel model, List<Finding> found)
    {
        foreach (var clip in model.Objects.Where(o => o.Class == "hkbClipGenerator"))
        {
            if (string.IsNullOrWhiteSpace(clip.Str("animationName")))
                Add(found, Level.Error, $"#{clip.Id} clip '{clip.Str("name")}'", "has no animationName");

            if (clip.Str("mode") == "MODE_USER_CONTROLLED")
            {
                var set = model.Follow(clip, "variableBindingSet");
                bool driven = set != null && set.StructLists.TryGetValue("bindings", out var rows)
                              && rows.Any(r => r.TryGetValue("memberPath", out var p) && p == "userControlledTimeFraction");
                // Not an error. Vanilla's animated doors, lifts and periscopes are all
                // MODE_USER_CONTROLLED with no binding, so the engine clearly drives the fraction
                // some other way. Worth surfacing only because an unbound clip is also what a
                // half-finished edit looks like.
                if (!driven)
                    Add(found, Level.Warning, $"#{clip.Id} clip '{clip.Str("name")}'",
                        "is MODE_USER_CONTROLLED with no variable bound to userControlledTimeFraction");
            }
        }
    }

    private static void CheckUnattached(BehaviourGraphModel model, List<Finding> found)
    {
        foreach (var obj in GraphAuthor.Unattached(model))
            Add(found, Level.Warning, $"#{obj.Id} {obj.Class}",
                $"'{obj.Str("name")}' has nothing pointing at it, so the engine will never reach it");
    }
}
