using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class GraphValidator
{
    private const int MaxPerWeaponFindings = 6;

    public enum Level { Error, Warning }

    public sealed class Finding
    {
        public Level Level;
        public string Where = "";
        public string What = "";
        public string ObjectId = "";
        public bool BlocksSave;

        public override string ToString() => $"{(Level == Level.Error ? "error" : "warning")}  {Where}  {What}";
    }

    public static List<Finding> Check(string xml, ProjectChain? chain = null)
    {
        var model = BehaviourGraphModel.Parse(xml);
        var found = Check(model, chain);

        CheckSymbolIndices(xml, model, found);

        return found;
    }

    public static List<Finding> Check(BehaviourGraphModel model, ProjectChain? chain = null,
                                      PackfileObjects? objects = null)
    {
        var found = new List<Finding>();

        if (objects != null) CheckSymbolIndices(objects, model, found);

        CheckSymbolArrays(model, found);
        CheckDanglingReferences(model, found);
        CheckStateMachines(model, found);
        CheckReachableStates(model, found);
        CheckBlenders(model, found);
        CheckClips(model, found);
        CheckClipAnimations(model, chain, found);
        if (chain != null) CheckWeaponSubgraphClips(model, chain, found);
        CheckUnattached(model, found);

        return found;
    }

    private static void Add(List<Finding> found, Level level, string where, string what,
                            bool blocksSave = false) =>
        found.Add(new Finding { Level = level, Where = where, What = what, ObjectId = LeadingId(where),
                                BlocksSave = blocksSave });

    private static string LeadingId(string where)
    {
        if (where.Length < 2 || where[0] != '#') return "";
        int i = 1;
        while (i < where.Length && char.IsAsciiDigit(where[i])) i++;
        return i > 1 ? where[1..i] : "";
    }

    public static Dictionary<string, Level> ByObject(IEnumerable<Finding> findings)
    {
        var worst = new Dictionary<string, Level>(StringComparer.Ordinal);
        foreach (var f in findings.Where(f => f.ObjectId.Length > 0))
            if (!worst.TryGetValue(f.ObjectId, out var had) || (had == Level.Warning && f.Level == Level.Error))
                worst[f.ObjectId] = f.Level;
        return worst;
    }

    private static void CheckSymbolArrays(BehaviourGraphModel model, List<Finding> found)
    {
        var counts = SymbolEditor.Audit(model);
        if (!counts.VariablesConsistent)
            Add(found, Level.Error, "hkbBehaviorGraphData",
                $"the variable arrays disagree: {counts}", blocksSave: true);
        if (!counts.EventsConsistent)
            Add(found, Level.Error, "hkbBehaviorGraphData",
                $"eventNames has {counts.EventNames} entries but eventInfos has {counts.EventInfos}",
                blocksSave: true);
    }

    private static void CheckDanglingReferences(BehaviourGraphModel model, List<Finding> found)
    {
        foreach (var obj in model.Objects)
            foreach (var site in HkReferences.In(obj))
            {
                if (model.Get(site.Target) != null) continue;

                string where = site.Member.Length > 0
                    ? $"#{obj.Id} {obj.Class}.{site.Field}.{site.Member}"
                    : $"#{obj.Id} {obj.Class}.{site.Field}";

                Add(found, Level.Error, where,
                    site.How == HkReferences.Held.ListElement
                        ? $"contains #{site.Target}, which is not in this file"
                        : $"points at #{site.Target}, which is not in this file",
                    blocksSave: true);
            }
    }

    private static void CheckSymbolIndices(PackfileObjects objects, BehaviourGraphModel model,
                                           List<Finding> found)
    {
        foreach (string unknown in SymbolIndexFixup.UnknownIndexFields(objects))
            Add(found, Level.Warning, unknown,
                "looks like an event or variable index but is not in the known table, so removing a symbol will refuse");

        int variables = SymbolEditor.VariableNames(model).Count;
        int events = SymbolEditor.EventNames(model).Count;

        foreach (string user in SymbolIndexFixup.ReferencesAtOrAbove(objects, events: false, variables))
            Add(found, Level.Error, user, $"but this graph declares only {variables} variables");
        foreach (string user in SymbolIndexFixup.ReferencesAtOrAbove(objects, events: true, events))
            Add(found, Level.Error, user, $"but this graph declares only {events} events");
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

    public static bool HasNoGenerator(StateEditor.StateRow state) =>
        string.IsNullOrEmpty(state.GeneratorRef) || state.GeneratorRef == "null";

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

    private static readonly string[] LossyOnRepack =
    {
        "hkaLosslessCompressedAnimation",
    };

    public static string? NativeRebuildWouldLose(string xml)
    {
        foreach (string cls in LossyOnRepack)
            if (xml.Contains($"class=\"{cls}\"", StringComparison.Ordinal))
                return $"Not saved, and the original is untouched. This edit needs the file rebuilt, " +
                       $"and this file holds a {cls}, which a native rebuild cannot write back without " +
                       "changing it: the packed words it stores are cut short on the way through, so " +
                       "the animation that came out would not be the one that went in. Changing " +
                       "values on their own is written straight into the file and does not hit this.";
        return null;
    }

    public static string? RefuseToSave(string xml) => RefuseToSave(xml, includeRepackLosses: true);

    public static string? RefuseToSave(string xml, bool includeRepackLosses)
    {
        if (xml.Length == 0) return null;

        if (includeRepackLosses && NativeRebuildWouldLose(xml) is { } lossy) return lossy;

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

    public static string? SaveRefusal(string xml, string sourceXml, bool includeRepackLosses = false)
    {
        if (RefuseToSave(xml, includeRepackLosses) is { } refused) return refused;

        var sourceErrors = Check(sourceXml).Where(f => f.BlocksSave)
                                           .Select(FindingKey).ToHashSet(StringComparer.Ordinal);
        var blocking = Check(xml).FirstOrDefault(f => f.BlocksSave &&
                                                      !sourceErrors.Contains(FindingKey(f)));
        return blocking == null ? null
            : $"Not saved, and the original is untouched. {blocking.Where}: {blocking.What}";
    }

    private static string FindingKey(Finding finding) => finding.Where + "\n" + finding.What;

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
                    $"stateId {group.Key} is used by {group.Count()} states, so transitions to it are ambiguous",
                    blocksSave: true);

            foreach (var state in states.Where(HasNoGenerator))
                Add(found, Level.Error, $"#{state.Id} state '{state.Name}'",
                    "has nothing to play, and Fallout 4 crashes while loading a graph that contains " +
                    "one; give it a generator or delete the state");

            var ids = states.Select(s => s.StateId).ToHashSet();
            foreach (var t in StateEditor.Transitions(model, machine.Id).Where(t => !ids.Contains(t.ToStateId)))
                Add(found, Level.Error, $"#{machine.Id} {name}",
                    $"a {(t.Wildcard ? "wildcard " : "")}transition targets stateId {t.ToStateId}, which no state in this machine has",
                    blocksSave: true);

            int start = machine.Int("startStateId");
            if (states.Count > 0 && start >= 0 && !ids.Contains(start))
                Add(found, Level.Error, $"#{machine.Id} {name}",
                    $"startStateId is {start}, which no state in this machine has", blocksSave: true);
        }
    }

    private static void CheckReachableStates(BehaviourGraphModel model, List<Finding> found)
    {
        var machines = model.Objects.Where(o => o.Class == "hkbStateMachine").ToList();
        var statesByMachine = machines.ToDictionary(m => m.Id, m => StateEditor.States(model, m.Id));
        var transitionsByMachine = machines.ToDictionary(m => m.Id,
            m => StateEditor.Transitions(model, m.Id));
        var enabledByMachine = statesByMachine.ToDictionary(p => p.Key,
            p => p.Value.Where(s => s.Enabled).Select(s => s.StateId).ToHashSet());
        var reachableByMachine = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var checkedMachines = new HashSet<string>(StringComparer.Ordinal);

        foreach (var machine in machines)
        {
            var states = statesByMachine[machine.Id];
            var reachable = new HashSet<int>();
            reachableByMachine[machine.Id] = reachable;
            if (states.Count == 0) continue;

            string startMode = machine.Str("startStateMode");
            bool randomStart = startMode is "START_STATE_MODE_RANDOM" or "2";
            bool defaultStart = startMode.Length == 0 ||
                                startMode is "START_STATE_MODE_DEFAULT" or "0";

            bool externalEntry = (!defaultStart && !randomStart) ||
                                 machine.Ref("startStateIdSelector") != null ||
                                 machine.Int("syncVariableIndex") >= 0 ||
                                 machine.Int("transitionToNextHigherStateEventId") >= 0 ||
                                 machine.Int("transitionToNextLowerStateEventId") >= 0;
            if (externalEntry)
            {
                reachable.UnionWith(states.Where(s => s.Enabled).Select(s => s.StateId));
                continue;
            }

            int start = machine.Int("startStateId");

            if (!randomStart && !states.Any(s => s.StateId == start))
            {
                reachable.UnionWith(states.Where(s => s.Enabled).Select(s => s.StateId));
                continue;
            }

            var transitions = transitionsByMachine[machine.Id];

            if (transitions.Count == 0)
            {
                reachable.UnionWith(states.Where(s => s.Enabled).Select(s => s.StateId));
                continue;
            }

            if (randomStart) reachable.UnionWith(states.Where(s => s.Enabled).Select(s => s.StateId));
            else if (enabledByMachine[machine.Id].Contains(start)) reachable.Add(start);
            checkedMachines.Add(machine.Id);
        }

        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (var machine in machines)
            {
                if (ExpandReachable(reachableByMachine[machine.Id],
                                    transitionsByMachine[machine.Id],
                                    enabledByMachine[machine.Id]))
                    grew = true;
            }

            foreach (var outer in machines)
            {
                var outerReachable = reachableByMachine[outer.Id];
                if (outerReachable.Count == 0) continue;

                var outerStates = statesByMachine[outer.Id];
                foreach (var transition in transitionsByMachine[outer.Id]
                             .Where(t => t.HasFlag(0x2000)))
                {
                    if (!transition.Wildcard && !outerReachable.Contains(transition.FromStateId))
                        continue;

                    var entered = outerStates.FirstOrDefault(s => s.StateId == transition.ToStateId);
                    if (entered == null || !entered.Enabled) continue;
                    var generator = model.Get(entered?.GeneratorRef.TrimStart('#'));
                    var inner = StateRoutes.MachineUnder(model, generator, 0);
                    if (inner == null || !reachableByMachine.TryGetValue(inner.Id, out var innerReachable))
                        continue;

                    if (enabledByMachine[inner.Id].Contains(transition.ToNestedStateId) &&
                        innerReachable.Add(transition.ToNestedStateId))
                        grew = true;
                }
            }
        }

        foreach (var machine in machines.Where(m => checkedMachines.Contains(m.Id)))
        {
            var states = statesByMachine[machine.Id];
            var reachable = reachableByMachine[machine.Id];

            foreach (var s in states.Where(s => s.Enabled && !reachable.Contains(s.StateId)))
                Add(found, Level.Warning, $"#{s.Id} state '{s.Name}'",
                    $"cannot be entered from inside this file: nothing in #{machine.Id} '{machine.Str("name")}' transitions to stateId {s.StateId}, and it is not the start state");
        }
    }

    private static bool ExpandReachable(HashSet<int> reachable,
                                        IReadOnlyList<StateEditor.TransitionRow> transitions,
                                        IReadOnlySet<int> enabled)
    {
        bool changed = false;
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (var transition in transitions)
            {
                if (transition.ToStateId < 0 || !enabled.Contains(transition.ToStateId) ||
                    reachable.Contains(transition.ToStateId))
                    continue;

                if (!transition.Wildcard && !reachable.Contains(transition.FromStateId)) continue;
                if (transition.Wildcard && reachable.Count == 0) continue;
                reachable.Add(transition.ToStateId);
                changed = grew = true;
            }
        }
        return changed;
    }

    private static void CheckBlenders(BehaviourGraphModel model, List<Finding> found)
    {
        foreach (var blender in model.Objects.Where(o => o.Class == "hkbBlenderGenerator"))
            foreach (string childId in blender.Refs("children"))
            {
                var child = model.Get(childId);
                if (child == null) continue;
                if (child.Class != "hkbBlenderGeneratorChild")
                    Add(found, Level.Error, $"#{blender.Id} {blender.Str("name")}",
                        $"child #{childId} is a {child.Class}; a blender's children must be hkbBlenderGeneratorChild wrappers",
                        blocksSave: true);
                else if (child.Ref("generator") == null)
                    Add(found, Level.Error, $"#{blender.Id} {blender.Str("name")}",
                        $"child #{childId} has no generator, so it plays nothing", blocksSave: true);
            }
    }

    private static void CheckClips(BehaviourGraphModel model, List<Finding> found)
    {
        int declaredVariables = SymbolEditor.VariableNames(model).Count;

        foreach (var clip in model.Objects.Where(o => o.Class == "hkbClipGenerator"))
        {
            if (string.IsNullOrWhiteSpace(clip.Str("animationName")))
                Add(found, Level.Error, $"#{clip.Id} clip '{clip.Str("name")}'", "has no animationName",
                    blocksSave: true);

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

            if (!ProjectChain.AnimationExists(chain.Root, anim, chain.Data))
            {
                string whereTo = chain.Data != null
                    ? $"under {chain.Root} nor inside any .ba2 under {chain.Data.DataFolder}"
                    : $"under {chain.Root}";
                Add(found, chain.Data != null ? Level.Error : Level.Warning, where,
                    $"plays '{anim}', which is not on disk {whereTo}");
            }
            else if (declared.Count > 0 && !declared.Contains(ProjectChain.AnimationKey(anim)))
                Add(found, Level.Warning, where,
                    $"plays '{anim}', which the character file does not list, so the engine may not load it");
        }
    }

    /// <summary>
    /// The per-weapon half of animation checking. A behavior that plays clips under
    /// Animations\Weapon\&lt;Type&gt;\ is a weapon subgraph: the engine resolves its generic
    /// Animations\&lt;clip&gt; references per weapon through the animation-set fallback chains
    /// on the race record (AnimationSetData), whose paths are Animations\Weapon\&lt;Type&gt;\...
    /// GameData derives that map from the game's master plugin, so each missing clip is
    /// resolved to the exact engine search: the failing chain prefix, and whether the generic
    /// Animations\&lt;clip&gt; fallback exists. Without the master (no game data folder), the
    /// older bounded heuristic is kept: the weapon types the subgraph names itself, and the
    /// generic clips that have a per-weapon copy somewhere.
    /// </summary>
    private static void CheckWeaponSubgraphClips(BehaviourGraphModel model, ProjectChain chain,
                                                 List<Finding> found)
    {
        if (chain.Data == null || chain.Root.Length == 0) return;

        var clips = model.Objects.Where(o => o.Class == "hkbClipGenerator").ToList();
        var referencedTypes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in clips)
        {
            string anim = clip.Str("animationName");
            string[] parts = anim.Replace('\\', '/').Split('/');
            for (int i = 0; i + 1 < parts.Length; i++)
                if (parts[i].Equals("Weapon", StringComparison.OrdinalIgnoreCase) && parts[i + 1].Length > 0)
                    referencedTypes.Add(parts[i + 1]);
        }
        if (referencedTypes.Count == 0) return;

        // the generic clips this subgraph plays from the Animations root, one level deep
        var generic = clips
            .Select(clip => clip.Str("animationName").Replace('\\', '/'))
            .Where(a => a.Split('/').Length == 2 && a.StartsWith("Animations/", StringComparison.OrdinalIgnoreCase))
            .Select(a => a[(a.LastIndexOf('/') + 1)..])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (generic.Count == 0) return;

        var sets = chain.Data.WeaponTypeSets;
        if (sets.Count > 0)
        {
            CheckWeaponSubgraphAgainstMap(model, chain, generic, sets, found);
            return;
        }

        var weaponFolders = chain.Data.Subfolders(chain.Root, "Animations/Weapon");
        if (weaponFolders.Count == 0) return;

        // a clip is per-weapon when a copy exists under some weapon folder in the data
        var perWeapon = generic.Where(leaf =>
            weaponFolders.Any(type => ProjectChain.AnimationExists(
                chain.Root, $"Animations\\Weapon\\{type}\\{leaf}", chain.Data))).ToList();
        if (perWeapon.Count == 0) return;

        // without the master the weapon types are the folders the subgraph names itself, and
        // every per-weapon clip missing for a type is reported with the failing search path and
        // whether the engine's generic fallback still plays it.
        var messages = new List<string>();
        foreach (string type in referencedTypes)
        {
            foreach (string leaf in perWeapon)
            {
                if (ProjectChain.AnimationExists(chain.Root, $"Animations\\Weapon\\{type}\\{leaf}", chain.Data))
                    continue;

                bool genericExists = ProjectChain.AnimationExists(chain.Root, "Animations\\" + leaf, chain.Data);
                messages.Add(genericExists
                    ? $"per-weapon coverage: '{type}' lacks {leaf} under Animations\\Weapon\\{type}; " +
                      $"the generic Animations\\{leaf} copy exists, so the engine falls back and the clip " +
                      "still plays (extract a per-weapon copy to override it)"
                    : $"per-weapon coverage: '{type}' lacks {leaf} under Animations\\Weapon\\{type}, and no " +
                      $"generic Animations\\{leaf} copy exists either — playing this clip for this weapon " +
                      $"type is a crash (extract the animation under Animations\\Weapon\\{type})");
            }
        }
        ReportWeaponGaps(found, messages);
    }

    /// <summary>
    /// The precise form of the per-weapon check: each weapon type carries the fallback chain
    /// of animation paths the engine searches, from the race AnimationSetData. A generic clip
    /// is covered for a type when it exists under any prefix of that type's chain, or as the
    /// generic Animations\&lt;clip&gt; fallback the engine falls back to after the chain. Every
    /// genuinely missing clip is reported with the exact engine search that failed: the chain
    /// prefix where the copy should be, and the fact that no generic fallback exists (the crash
    /// condition). Vanilla resolves every clip this way, so a clean install reports nothing;
    /// only truly missing copies are named.
    /// </summary>
    private static void CheckWeaponSubgraphAgainstMap(BehaviourGraphModel model, ProjectChain chain,
                                                      List<string> generic,
                                                      IReadOnlyList<OpenCommonwealth.Services.Archive.GameData.WeaponTypeSet> sets,
                                                      List<Finding> found)
    {
        // the engine searches the type's fallback chain first and falls back to the generic
        // Animations\<clip> file, so a clip is covered for the type when either holds it;
        // only a clip absent from both is a real gap, and the warning names the exact search.
        var messages = new List<string>();
        foreach (var set in sets)
        {
            if (set.Prefixes.Count == 0) continue;

            foreach (string leaf in generic)
            {
                bool inChain = set.Prefixes.Any(prefix =>
                    ProjectChain.AnimationExists(chain.Root, prefix + "\\" + leaf, chain.Data));
                if (inChain) continue;

                // the engine's final fallback is the generic copy; when it exists the clip plays
                if (ProjectChain.AnimationExists(chain.Root, "Animations\\" + leaf, chain.Data)) continue;

                messages.Add($"per-weapon coverage: '{set.Type}' cannot resolve {leaf}: the engine searched " +
                             $"its {set.Prefixes.Count}-prefix chain starting at {set.Prefixes[0]}\\{leaf} and " +
                             $"found no copy, and no generic Animations\\{leaf} fallback exists either — " +
                             "playing this clip for this weapon type is a crash");
            }
        }
        ReportWeaponGaps(found, messages);
    }

    /// <summary>Emit the per-clip findings, bounded so one subgraph cannot flood the list.</summary>
    private static void ReportWeaponGaps(List<Finding> found, List<string> messages)
    {
        if (messages.Count == 0) return;

        foreach (string message in messages.Take(MaxPerWeaponFindings))
            Add(found, Level.Warning, "weapon subgraph", message);
        if (messages.Count > MaxPerWeaponFindings)
            Add(found, Level.Warning, "weapon subgraph",
                $"per-weapon coverage: {messages.Count - MaxPerWeaponFindings} more missing clip(s) for " +
                "other weapon types (extract the named animations under Animations\\Weapon)");
    }

    private static void CheckUnattached(BehaviourGraphModel model, List<Finding> found)
    {
        foreach (var obj in GraphAuthor.Unattached(model))
            Add(found, Level.Warning, $"#{obj.Id} {obj.Class}",
                $"'{obj.Str("name")}' has nothing pointing at it, so the engine will never reach it");
    }
}
