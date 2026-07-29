using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public enum PlanNodeKind { Clip, Blend, Select, Machine, ReferencePose, ExternalBehaviour, Sequence, Shared, Unmapped }

public sealed class PlanNode
{
    public PlanNodeKind Kind;
    public string Name = "";
    public string Class = "";
    public string AnimationName = "";
    public string Detail = "";
    public float Speed = 1f;
    public readonly List<PlanNode> Children = new();
    public PlanMachine? Machine;
    public float Weight = 1f;
}

public sealed class PlanState
{
    public int StateId;
    public string Name = "";
    public PlanNode? Generator;
}

public sealed class PlanTransition
{
    public int FromStateId = -1;
    public int ToStateId = -1;
    public bool Wildcard;
    public int EventId = -1;
    public string EventName = "";
    public string ConditionClass = "";
    public string ConditionText = "";
    public float Duration;
}

public sealed class PlanMachine
{
    public string Name = "";
    public int StartStateId;
    public readonly List<PlanState> States = new();
    public readonly List<PlanTransition> Transitions = new();
}

public sealed class PlanGap
{
    public int Count;
    public string Explanation = "";
}

public sealed class BehaviourPlan
{
    public string GraphName = "";
    public PlanMachine? Root;
    public readonly List<PlanMachine> Machines = new();
    public readonly List<string> EventNames = new();
    public readonly List<string> VariableNames = new();
    public readonly Dictionary<string, int> UnmappedGenerators = new();
    public readonly List<string> Warnings = new();
    public int MissingGenerators;
    public int RevisitedGenerators;
    public int BlendNodes;
    public readonly Dictionary<string, PlanGap> Gaps = new();

    public int BlendBranchesNotWired => GapCount("blend-branch-not-wired");
    public int WeightsRead => GapCount("blend-weight-ignored");

    public int GapCount(string key) => Gaps.TryGetValue(key, out var g) ? g.Count : 0;

    public void NoteGap(string key, string explanation, int n = 1)
    {
        if (!Gaps.TryGetValue(key, out var gap))
        {
            gap = new PlanGap { Explanation = explanation };
            Gaps[key] = gap;
        }
        gap.Count += n;
    }

    public int StateCount => Machines.Sum(m => m.States.Count);
    public int TransitionCount => Machines.Sum(m => m.Transitions.Count);
    public int StatesWithGenerator => Machines.Sum(m => m.States.Count(s => s.Generator is { Kind: not PlanNodeKind.Unmapped }));
    public int TransitionsWithEvent => Machines.Sum(m => m.Transitions.Count(t => t.EventId >= 0));
    public int TransitionsWithCondition => Machines.Sum(m => m.Transitions.Count(t => t.ConditionText.Length > 0));

    private static readonly Dictionary<string, string> PassThrough = new()
    {
        ["hkbModifierGenerator"] = "generator",
        ["BSiStateTaggingGenerator"] = "pDefaultGenerator",
        ["BSCyclicBlendTransitionGenerator"] = "pBlenderGenerator",
        ["DynamicAnimationTaggingGenerator"] = "pDefaultGenerator",
        ["BSOffsetAnimationGenerator"] = "pDefaultGenerator",
        ["BSBehaviorGraphSwapGenerator"] = "pDefaultGenerator",
        ["BSBoneSwitchGenerator"] = "pDefaultGenerator",
    };

    public static BehaviourPlan Build(BehaviourGraphModel model)
    {
        var plan = new BehaviourPlan();

        var strings = model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraphStringData");
        if (strings != null)
        {
            plan.EventNames.AddRange(strings.Strings("eventNames"));
            plan.VariableNames.AddRange(strings.Strings("variableNames"));
        }

        var graph = model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraph");
        plan.GraphName = graph?.Str("name") ?? "";

        var rootGen = graph != null
            ? model.Follow(graph, "rootGenerator")
            : model.Objects.FirstOrDefault(o => o.Class == "hkbStateMachine");

        var visited = new HashSet<string>();
        var node = plan.Resolve(model, rootGen, visited);
        plan.Root = node?.Machine ?? plan.Machines.FirstOrDefault();
        return plan;
    }

    private PlanNode? Resolve(BehaviourGraphModel model, HkObject? gen, HashSet<string> visited)
    {
        if (gen == null) return null;
        if (!visited.Add(gen.Id))
        {
            RevisitedGenerators++;
            return new PlanNode { Kind = PlanNodeKind.Shared, Class = gen.Class, Name = gen.Str("name"), Detail = "shared with another state" };
        }

        var node = new PlanNode { Class = gen.Class, Name = gen.Str("name") };

        switch (gen.Class)
        {
            case "hkbClipGenerator":
                node.Kind = PlanNodeKind.Clip;
                node.AnimationName = gen.Str("animationName");
                node.Speed = float.TryParse(gen.Str("playbackSpeed"), out var sp) ? sp : 1f;
                return node;

            case "hkbReferencePoseGenerator":
                node.Kind = PlanNodeKind.ReferencePose;
                return node;

            case "hkbBehaviorReferenceGenerator":
                node.Kind = PlanNodeKind.ExternalBehaviour;
                node.Detail = gen.Str("behaviorName");
                return node;

            case "BGSGamebryoSequenceGenerator":
                node.Kind = PlanNodeKind.Sequence;
                node.Detail = gen.Str("pSequence");
                return node;

            case "hkbStateMachine":
                node.Kind = PlanNodeKind.Machine;
                node.Machine = BuildMachine(model, gen, visited);
                return node;

            case "hkbBlenderGenerator":
            case "hkbPoseMatchingGenerator":
                node.Kind = PlanNodeKind.Blend;
                foreach (var childId in gen.Refs("children"))
                {
                    var child = model.Get(childId);
                    var inner = Resolve(model, model.Follow(child, "generator"), visited);
                    if (inner == null) continue;
                    if (child != null && float.TryParse(child.Str("weight"), out var w))
                    {
                        inner.Weight = w;
                        NoteGap("blend-weight-ignored",
                                "blend weight read from the graph but not applied to the Godot blend tree");
                    }
                    node.Children.Add(inner);
                }
                NoteBlend(node);
                return node;

            case "hkbManualSelectorGenerator":
                node.Kind = PlanNodeKind.Select;
                foreach (var childId in gen.Refs("generators"))
                {
                    var inner = Resolve(model, model.Get(childId), visited);
                    if (inner != null) node.Children.Add(inner);
                }
                NoteBlend(node);
                return node;

            case "hkbLayerGenerator":
                node.Kind = PlanNodeKind.Blend;
                foreach (var layerId in gen.Refs("layers"))
                {
                    var layer = model.Get(layerId);
                    var inner = Resolve(model, model.Follow(layer, "generator"), visited);
                    if (inner == null) continue;
                    if (layer != null && float.TryParse(layer.Str("weight"), out var lw))
                    {
                        inner.Weight = lw;
                        NoteGap("blend-weight-ignored",
                                "blend weight read from the graph but not applied to the Godot blend tree");
                    }
                    node.Children.Add(inner);
                }
                NoteBlend(node);
                return node;
        }

        if (PassThrough.TryGetValue(gen.Class, out var field))
        {
            var inner = Resolve(model, model.Follow(gen, field), visited);
            if (inner != null) return inner;
            node.Kind = PlanNodeKind.Unmapped;
            node.Detail = $"{gen.Class}.{field} was null";
            Count(gen.Class);
            return node;
        }

        node.Kind = PlanNodeKind.Unmapped;
        node.Detail = "no mapping for this generator class";
        Count(gen.Class);
        return node;
    }

    private void NoteBlend(PlanNode node)
    {
        BlendNodes++;
        if (node.Children.Count > 1)
        {
            int unwired = node.Children.Count - 1;
            NoteGap("blend-branch-not-wired",
                    "branch built but not connected to the blend tree output, so it never plays", unwired);
            if (Warnings.Count < 40)
                Warnings.Add($"blend '{node.Name}' ({node.Class}) has {node.Children.Count} branches; " +
                             $"{unwired} will not be connected to the output");
        }
    }

    private void Count(string cls)
    {
        UnmappedGenerators.TryGetValue(cls, out int n);
        UnmappedGenerators[cls] = n + 1;
        NoteGap("generator-unmapped", "generator class with no mapping, replaced by a placeholder node");
        if (n == 0) Warnings.Add($"unmapped generator class: {cls}");
    }

    private PlanMachine BuildMachine(BehaviourGraphModel model, HkObject sm, HashSet<string> visited)
    {
        var machine = new PlanMachine { Name = sm.Str("name"), StartStateId = sm.Int("startStateId", 0) };
        Machines.Add(machine);

        foreach (var stateId in sm.Refs("states"))
        {
            var info = model.Get(stateId);
            if (info == null) continue;

            var generatorRef = model.Follow(info, "generator");
            if (generatorRef == null)
            {
                MissingGenerators++;
                NoteGap("generator-reference-unresolved", "state's generator reference did not resolve to an object");
                if (MissingGenerators <= 5)
                    Warnings.Add($"state '{info.Str("name")}' in '{machine.Name}' has no resolvable generator ({info.Str("generator")})");
            }

            var state = new PlanState
            {
                StateId = info.Int("stateId"),
                Name = info.Str("name"),
                Generator = Resolve(model, generatorRef, visited),
            };
            machine.States.Add(state);

            AddTransitions(model, machine, model.Follow(info, "transitions"), state.StateId, false);
        }

        AddTransitions(model, machine, model.Follow(sm, "wildcardTransitions"), -1, true);
        return machine;
    }

    private void AddTransitions(BehaviourGraphModel model, PlanMachine machine,
                                HkObject? array, int fromStateId, bool wildcard)
    {
        if (array == null || !array.StructLists.TryGetValue("transitions", out var rows)) return;

        foreach (var row in rows)
        {
            var t = new PlanTransition
            {
                FromStateId = fromStateId,
                Wildcard = wildcard,
                ToStateId = row.TryGetValue("toStateId", out var to) && int.TryParse(to, out var toId) ? toId : -1,
                EventId = row.TryGetValue("eventId", out var ev) && int.TryParse(ev, out var evId) ? evId : -1,
            };

            if (t.EventId >= 0 && t.EventId < EventNames.Count) t.EventName = EventNames[t.EventId];

            if (row.TryGetValue("condition", out var cond) && cond.StartsWith('#'))
            {
                var c = model.Get(cond[1..]);
                if (c != null)
                {
                    t.ConditionClass = c.Class;
                    t.ConditionText = c.Str("expression");
                    if (t.ConditionText.Length == 0) t.ConditionText = c.Str("conditionString");
                    if (t.ConditionText.Length == 0) t.ConditionText = c.Class;
                }
            }

            if (row.TryGetValue("transition", out var eff) && eff.StartsWith('#'))
            {
                var e = model.Get(eff[1..]);
                if (e != null && float.TryParse(e.Str("duration"), out var d)) t.Duration = d;
            }

            machine.Transitions.Add(t);
        }
    }
}
