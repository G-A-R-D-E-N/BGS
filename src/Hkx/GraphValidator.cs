using System;
using System.Collections.Generic;
using System.IO;
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

    // The chain is optional because most of these checks only need the one file. Pass it and the
    // clip animations are checked against the folder on disk as well, which is the breakage that
    // cloning a behaviour folder under a new name causes and that nothing else here can see.
    public static List<Finding> Check(string xml, ProjectChain? chain = null)
    {
        var model = BehaviourGraphModel.Parse(xml);
        var found = new List<Finding>();

        CheckSymbolArrays(model, found);
        CheckDanglingReferences(model, found);
        CheckSymbolIndices(xml, model, found);
        CheckStateMachines(model, found);
        CheckReachableStates(model, found);
        CheckBlenders(model, found);
        CheckClips(model, found);
        CheckClipAnimations(model, chain, found);
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

    /// A state holding nothing. Deleting a generator clears the link that pointed at it rather than
    /// refusing, which is right, but it leaves the state behind looking ordinary. The views and Save
    /// ask this rather than each deciding for themselves what empty means, so they cannot drift apart
    /// from what Check graph reports.
    ///
    /// The game never ships this shape: across all 531 vanilla behaviour files, all 5329 states have a
    /// generator. It only appears after an edit.
    public static bool HasNoGenerator(StateEditor.StateRow state) =>
        string.IsNullOrEmpty(state.GeneratorRef) || state.GeneratorRef == "null";

    /// One empty state, named well enough to go and fix it without running anything else.
    public readonly record struct EmptyState(string Id, string Name, string Machine)
    {
        public override string ToString() =>
            $"'{(Name.Length > 0 ? Name : "#" + Id)}'" +
            (Machine.Length > 0 ? $" in {Machine}" : "");
    }

    public static List<EmptyState> EmptyStates(BehaviourGraphModel model)
    {
        var found = new List<EmptyState>();
        foreach (var machine in model.Objects.Where(o => o.Class == "hkbStateMachine"))
            foreach (var state in StateEditor.States(model, machine.Id).Where(HasNoGenerator))
                found.Add(new EmptyState(state.Id, state.Name ?? "", machine.Str("name")));
        return found;
    }

    /// Why Save must not write this file, or null when there is nothing to refuse.
    ///
    /// Fallout 4 crashes while loading a graph that contains one, before any state is entered, so
    /// reachability does not save it. BShkbUtils::GraphTraverser::Next pops every child a node
    /// reports and reads its vtable pointer without a null check, at Fallout4.exe+0x1705DDF, under
    /// LoadBehaviorHelper on the background clone thread. Measured 2026-08-04 on the Red Rocket
    /// garage door with one generator link cleared and nothing else changed.
    /// Named rather than counted, and with both ways out spelled out, because the refusal is the
    /// whole message: someone who hits this is stopped, and being stopped without being told which
    /// state or what to do about it is worse than not checking at all.
    public static string? RefuseToSave(string xml)
    {
        if (xml.Length == 0) return null;
        var empty = EmptyStates(BehaviourGraphModel.Parse(xml));
        if (empty.Count == 0) return null;

        const int Show = 4;
        string named = string.Join(", ", empty.Take(Show));
        if (empty.Count > Show) named += $", and {empty.Count - Show} more";

        return $"Not saved, and the original is untouched. {empty.Count} " +
               $"state{(empty.Count == 1 ? " has" : "s have")} nothing to play: {named}. " +
               "Fallout 4 crashes on load while it walks the graph, whether or not anything can " +
               "enter the state. To fix: give each one a generator, by dragging a clip or another " +
               "generator onto its generator slot in the graph, or delete the state itself if " +
               "nothing needs it. Check graph lists them all.";
    }

    /// Object ids of every state in the file that has no generator, for marking them on screen.
    public static HashSet<string> StatesWithNoGenerator(BehaviourGraphModel model) =>
        EmptyStates(model).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

    private static void CheckStateMachines(BehaviourGraphModel model, List<Finding> found)
    {
        foreach (var machine in model.Objects.Where(o => o.Class == "hkbStateMachine"))
        {
            var states = StateEditor.States(model, machine.Id);
            string name = machine.Str("name");

            foreach (var group in states.GroupBy(s => s.StateId).Where(g => g.Count() > 1))
                Add(found, Level.Error, $"#{machine.Id} {name}",
                    $"stateId {group.Key} is used by {group.Count()} states, so transitions to it are ambiguous");

            foreach (var state in states.Where(HasNoGenerator))
                Add(found, Level.Error, $"#{state.Id} state '{state.Name}'",
                    "has nothing to play, and Fallout 4 crashes while loading a graph that contains " +
                    "one; give it a generator or delete the state");

            var ids = states.Select(s => s.StateId).ToHashSet();
            foreach (var t in StateEditor.Transitions(model, machine.Id).Where(t => !ids.Contains(t.ToStateId)))
                Add(found, Level.Error, $"#{machine.Id} {name}",
                    $"a {(t.Wildcard ? "wildcard " : "")}transition targets stateId {t.ToStateId}, which no state in this machine has");

            int start = machine.Int("startStateId");
            if (states.Count > 0 && start >= 0 && !ids.Contains(start))
                Add(found, Level.Error, $"#{machine.Id} {name}", $"startStateId is {start}, which no state in this machine has");
        }
    }

    // Being referenced and being reachable are different questions for a state. A state info is
    // always referenced, because the machine lists it, so the unattached check can never see a state
    // that no transition can enter. Retargeting a transition is the normal way to change what an
    // event does, and it silently orphans whatever the transition used to point at.
    private static void CheckReachableStates(BehaviourGraphModel model, List<Finding> found)
    {
        // A transition in one machine can enter a nested machine's state directly, so a state named
        // anywhere as a nested target is enterable even with nothing pointing at it in its own
        // machine.
        var nestedTargets = model.Objects
            .Where(o => o.Class == "hkbStateMachineTransitionInfoArray")
            .SelectMany(o => o.StructLists.TryGetValue("transitions", out var rows) ? rows : new())
            .Select(r => r.TryGetValue("toNestedStateId", out var v) && int.TryParse(v, out int n) ? n : 0)
            .Where(n => n != 0)
            .ToHashSet();

        foreach (var machine in model.Objects.Where(o => o.Class == "hkbStateMachine"))
        {
            var states = StateEditor.States(model, machine.Id);
            int start = machine.Int("startStateId");

            // A machine whose start state does not exist is already an error above, and treating it
            // as unreachable here would report every state in the machine on top of that.
            if (states.Count == 0 || !states.Any(s => s.StateId == start)) continue;

            var transitions = StateEditor.Transitions(model, machine.Id);

            // A machine with no transitions at all is not transition driven: the engine picks the
            // state. Saying nothing transitions to a state there is true and useless, and it is how
            // vanilla writes ragdoll and death machines.
            if (transitions.Count == 0) continue;

            var reachable = new HashSet<int> { start };

            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (var t in transitions)
                {
                    if (t.ToStateId < 0 || reachable.Contains(t.ToStateId)) continue;
                    // A wildcard fires from any state, so its target is live once anything is.
                    if (!t.Wildcard && !reachable.Contains(t.FromStateId)) continue;
                    reachable.Add(t.ToStateId);
                    grew = true;
                }
            }

            // A warning, not an error. The ticket assumed an unreachable state is always a mistake;
            // vanilla disagrees 123 times across 56 files, and every one checked is a state the game
            // enters from outside the graph rather than through a transition: ragdoll, death
            // variants, paired animations, the SharedCore wrapper.
            foreach (var s in states.Where(s => !reachable.Contains(s.StateId) && !nestedTargets.Contains(s.StateId)))
                Add(found, Level.Warning, $"#{s.Id} state '{s.Name}'",
                    $"cannot be entered from inside this file: nothing in #{machine.Id} '{machine.Str("name")}' transitions to stateId {s.StateId}, and it is not the start state");
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
        int declaredVariables = SymbolEditor.VariableNames(model).Count;

        foreach (var clip in model.Objects.Where(o => o.Class == "hkbClipGenerator"))
        {
            if (string.IsNullOrWhiteSpace(clip.Str("animationName")))
                Add(found, Level.Error, $"#{clip.Id} clip '{clip.Str("name")}'", "has no animationName");

            // An unbound MODE_USER_CONTROLLED clip sits on frame zero, which in a door, lift or
            // periscope graph is the point: it is the rest pose the state machine sits in until an
            // event moves it on. Those graphs declare no variables at all, so only say something
            // when the graph does have variables and this clip could plausibly have meant to use
            // one. Without that condition this fires on fifteen vanilla files and means nothing.
            if (clip.Str("mode") != "MODE_USER_CONTROLLED" || declaredVariables == 0) continue;

            var set = model.Follow(clip, "variableBindingSet");
            bool driven = set != null && set.StructLists.TryGetValue("bindings", out var rows)
                          && rows.Any(r => r.TryGetValue("memberPath", out var p) && p == "userControlledTimeFraction");
            if (!driven)
                Add(found, Level.Warning, $"#{clip.Id} clip '{clip.Str("name")}'",
                    "is MODE_USER_CONTROLLED with nothing bound to userControlledTimeFraction, so it holds frame zero");
        }
    }

    private static void CheckClipAnimations(BehaviourGraphModel model, ProjectChain? chain, List<Finding> found)
    {
        if (chain == null || chain.Root.Length == 0) return;

        var declared = chain.Animations.Select(ProjectChain.AnimationKey).ToHashSet();

        foreach (var clip in model.Objects.Where(o => o.Class == "hkbClipGenerator"))
        {
            string anim = clip.Str("animationName");
            if (string.IsNullOrWhiteSpace(anim)) continue;

            string where = $"#{clip.Id} clip '{clip.Str("name")}'";

            // A warning rather than an error because Bethesda ships plenty of these: shared
            // behaviours reference per creature animations that not every creature has, and some
            // clips point at content that was cut. A file full of them after a folder was renamed
            // is still the loudest signal there is.
            if (!File.Exists(ProjectChain.ResolvePath(chain.Root, anim)))
                Add(found, Level.Warning, where, $"plays '{anim}', which is not on disk under {chain.Root}");
            else if (declared.Count > 0 && !declared.Contains(ProjectChain.AnimationKey(anim)))
                Add(found, Level.Warning, where,
                    $"plays '{anim}', which the character file does not list, so the engine may not load it");
        }
    }

    private static void CheckUnattached(BehaviourGraphModel model, List<Finding> found)
    {
        foreach (var obj in GraphAuthor.Unattached(model))
            Add(found, Level.Warning, $"#{obj.Id} {obj.Class}",
                $"'{obj.Str("name")}' has nothing pointing at it, so the engine will never reach it");
    }
}
