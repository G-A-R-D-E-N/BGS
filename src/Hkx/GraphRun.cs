using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;






















public sealed class GraphRun
{






    private static readonly HashSet<string> Opaque = new(StringComparer.Ordinal)
    {
        "hkbBehaviorReferenceGenerator",
        "BSBehaviorGraphSwapGenerator",
    };

    public sealed record Active(string MachineId, string MachineName, string StateId, int Number,
                                string StateName, float Weight = 1f, bool Fading = false)
    {
        public override string ToString() =>
            $"#{MachineId} '{MachineName}' is in #{StateId} '{StateName}'" +
            (Weight < 0.999f ? $" at {Weight * 100:F0}%" : "");
    }




    private sealed record Blend(string MachineId, string FromStateId, string ToStateId,
                                float Duration, float Elapsed)
    {
        public float Fraction => Duration <= 0 ? 1f : Math.Clamp(Elapsed / Duration, 0f, 1f);
    }


    public sealed record Stop(string ObjectId, string ClassName, string Why)
    {
        public override string ToString() => $"#{ObjectId} {ClassName}: {Why}";
    }

    public sealed record Fired(string MachineId, string FromStateId, string ToStateId, string ToStateName,
                               string Event, int Priority, bool Conditional, string Condition)
    {
        public override string ToString() =>
            $"#{FromStateId} -{Event}-> #{ToStateId} '{ToStateName}'" +
            (Conditional ? $" if {Condition}" : "");
    }






    public sealed record Blocked(string MachineId, string FromStateId, string ToStateId,
                                 string ToStateName, string Event, string Condition)
    {
        public override string ToString() =>
            $"#{FromStateId} -{Event}-> #{ToStateId} '{ToStateName}' held back by {Condition}";
    }

    private readonly BehaviourGraphModel _model;
    private readonly StateRoutes _routes;
    private readonly List<string> _events;






    private readonly Dictionary<string, double> _variables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SymbolEditor.VariableType> _variableTypes =
        new(StringComparer.Ordinal);



    private readonly Dictionary<string, Expression.Parsed> _parsed = new(StringComparer.Ordinal);

    private readonly List<Blocked> _blocked = new();



    public int ConditionsWeighed { get; private set; }


    private readonly Dictionary<string, string> _in = new(StringComparer.Ordinal);
    private readonly List<Stop> _stops = new();




    private readonly Dictionary<string, Blend> _blending = new(StringComparer.Ordinal);






    private readonly Dictionary<string, float> _playing = new(StringComparer.Ordinal);




    private Dictionary<string, float>? _wasPlaying;






    private IReadOnlyDictionary<string, ClipTiming.Clip> _clips =
        new Dictionary<string, ClipTiming.Clip>(StringComparer.Ordinal);

    public IReadOnlyList<Stop> Stops => _stops;
    public string RootId { get; private set; } = "";



    public bool Blending => _blending.Count > 0;







    public IReadOnlyList<Active> Where()
    {
        var into = new List<Active>();
        foreach (var (machineId, stateId) in _in)
        {
            var settled = Describe(machineId, stateId);
            if (settled == null) continue;

            if (_blending.TryGetValue(machineId, out var blend) && blend.ToStateId == stateId)
            {
                float t = blend.Fraction;
                into.Add(settled with { Weight = t });

                var fading = Describe(machineId, blend.FromStateId);
                if (fading != null && blend.FromStateId != stateId)
                    into.Add(fading with { Weight = 1 - t, Fading = true });
            }
            else into.Add(settled);
        }
        return into;
    }

    private GraphRun(BehaviourGraphModel model)
    {
        _model = model;
        _routes = StateRoutes.Of(model);
        _events = SymbolEditor.EventNames(model);

        var names = SymbolEditor.VariableNames(model);
        var values = SymbolEditor.VariableValues(model);
        var types = SymbolEditor.VariableTypes(model);

        for (int i = 0; i < names.Count; i++)
        {
            if (names[i].Length == 0) continue;

            var type = i < types.Count ? types[i] : SymbolEditor.VariableType.Int32;
            _variableTypes[names[i]] = type;

            if (i >= values.Count || !int.TryParse(values[i], out int word)) continue;
            _variables[names[i]] = type == SymbolEditor.VariableType.Real
                                   ? BitConverter.Int32BitsToSingle(word)
                                   : word;
        }
    }


    public IReadOnlyList<string> Variables => _variables.Keys.ToList();




    public double? ValueOf(string name) =>
        _variables.TryGetValue(name, out double value) ? value : null;

    public SymbolEditor.VariableType TypeOf(string name) =>
        _variableTypes.TryGetValue(name, out var type) ? type : SymbolEditor.VariableType.Int32;




    public void Set(string name, double value)
    {
        if (!_variableTypes.ContainsKey(name))
            throw new ArgumentException(
                $"this graph declares no variable called '{name}', so setting it would change " +
                "nothing. A variable nothing reads and a variable that does not exist are different " +
                "answers.");

        _variables[name] = value;




        _blocked.Clear();
    }


    public IReadOnlyList<Blocked> HeldBack => _blocked;




    public IReadOnlyList<(StateRoutes.Route Route, string Condition, Expression.Verdict Verdict)> Conditions()
    {
        var found = new List<(StateRoutes.Route, string, Expression.Verdict)>();

        foreach (var route in _routes.Routes)
        {
            string condition = Detail(route).Condition;
            if (condition.Length > 0) found.Add((route, condition, Test(condition)));
        }

        return found;
    }







    public Expression.Verdict Test(string condition)
    {
        if (condition.Length == 0) return Expression.Verdict.True;

        if (!_parsed.TryGetValue(condition, out var parsed))
            _parsed[condition] = parsed = Expression.Parse(condition);

        return Expression.Evaluate(parsed, name => ValueOf(name));
    }






    public static GraphRun Start(BehaviourGraphModel model)
    {
        var run = new GraphRun(model);

        var graph = model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraph");
        string root = graph?.Ref("rootGenerator") ?? "";

        if (root.Length == 0)
        {



            var pointedAt = model.Objects
                .SelectMany(o => GraphAuthor.PointsAt(model, o))
                .ToHashSet(StringComparer.Ordinal);

            var loose = model.Objects.FirstOrDefault(o => o.Class == "hkbStateMachine" && !pointedAt.Contains(o.Id));
            root = loose?.Id ?? model.Objects.FirstOrDefault(o => o.Class == "hkbStateMachine")?.Id ?? "";

            if (root.Length > 0)
                run._stops.Add(new Stop(root, model.Get(root)?.Class ?? "",
                    "this file has no hkbBehaviorGraph, so the run was started at a state machine " +
                    "nothing points at rather than at the graph's own root generator"));
        }

        run.RootId = root;
        if (root.Length > 0) run.Enter(root, 0, new HashSet<string>(StringComparer.Ordinal));
        return run;
    }







    private void Enter(string generatorId, int depth, HashSet<string> onPath)
    {
        if (generatorId.Length == 0 || depth > 32) return;



        if (!onPath.Add(generatorId)) return;

        var node = _model.Get(generatorId);
        if (node == null) { onPath.Remove(generatorId); return; }

        if (Opaque.Contains(node.Class))
        {
            string names = node.Str("behaviorName");
            _stops.Add(new Stop(node.Id, node.Class, names.Length > 0
                ? $"loads '{names}', which is another file, so the run stops here"
                : "swaps the running graph for another one, so the run stops here"));
            onPath.Remove(generatorId);
            return;
        }

        if (node.Class == "hkbStateMachine")
        {
            var states = StateEditor.States(_model, node.Id);
            int start = node.Int("startStateId");
            var into = states.FirstOrDefault(s => s.StateId == start);



            if (_resume != null && _resume.TryGetValue(node.Id, out var was) &&
                states.Any(s => s.Id == was))
                into = states.First(s => s.Id == was);

            if (into == null)
            {


                _stops.Add(new Stop(node.Id, node.Class,
                    $"its start state {start} is not one of its {states.Count} state(s), so it cannot be entered"));
                onPath.Remove(generatorId);
                return;
            }



            if ((node.Ref("startStateIdSelector") ?? "").Length > 0)
                _stops.Add(new Stop(node.Id, node.Class,
                    "its start state is chosen at runtime by a selector, so the state named here is " +
                    "the one the file declares rather than the one the game will pick"));

            Switch(node.Id, into.Id, depth, onPath);
            onPath.Remove(generatorId);
            return;
        }




        if (node.Class == "hkbClipGenerator")
        {
            _playing[node.Id] = _wasPlaying != null && _wasPlaying.TryGetValue(node.Id, out float was) ? was : 0;
            onPath.Remove(generatorId);
            return;
        }

        foreach (string next in Below(node)) Enter(next, depth + 1, onPath);
        onPath.Remove(generatorId);
    }


    private void Switch(string machineId, string stateId, int depth, HashSet<string> onPath)
    {
        _in[machineId] = stateId;
        string generator = _model.Get(stateId)?.Ref("generator") ?? "";
        if (generator.Length > 0) Enter(generator, depth + 1, onPath);
    }













    private static readonly Dictionary<string, HashSet<string>> Carries = BuildCarriers();

    private static Dictionary<string, HashSet<string>> BuildCarriers()
    {
        var carriers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var types = HavokClassTypes.Shipped;

        foreach (string className in types.Names)
        {
            foreach (var member in types.Members(className))
            {
                if (member.CType != "hkbGenerator") continue;
                if (member.VType is not ("TYPE_POINTER" or "TYPE_ARRAY")) continue;





                if (className is "hkbGeneratorTransitionEffect" or "hkbSetBehaviorCommand") continue;

                if (!carriers.TryGetValue(className, out var fields))
                    carriers[className] = fields = new HashSet<string>(StringComparer.Ordinal);
                fields.Add(member.Name);
            }
        }



        carriers["BSIStateManagerModifierBSiStateData"] = new HashSet<string>(StringComparer.Ordinal) { "pStateMachine" };
        return carriers;
    }






    private static readonly HashSet<string> Wrappers = Carries.Keys
        .Where(c => !c.EndsWith("Generator", StringComparison.Ordinal) && c != "hkbBehaviorGraph" &&
                    c != "hkbStateMachineStateInfo")
        .ToHashSet(StringComparer.Ordinal);






    private IEnumerable<string> Below(HkObject node)
    {




        Carries.TryGetValue(node.Class, out var fields);

        foreach (var slot in GraphLinks.OutSlots(_model, node))
        {
            bool declared = fields != null && fields.Contains(slot.Field);

            foreach (string target in slot.Targets)
            {
                var to = _model.Get(target);
                if (to == null) continue;
                if (declared || Wrappers.Contains(to.Class)) yield return target;
            }
        }
    }

    private Active? Describe(string machineId, string stateId)
    {
        var machine = _model.Get(machineId);
        var state = _model.Get(stateId);
        if (machine == null || state == null) return null;
        return new Active(machineId, machine.Str("name"), stateId, state.Int("stateId"), state.Str("name"));
    }







    public IReadOnlyList<Fired> Send(string name)
    {
        int id = _events.IndexOf(name);
        if (id < 0)
            throw new ArgumentException(
                $"this graph declares no event called '{name}', so nothing could be sent. " +
                "An event nothing listens for and an event that does not exist are different answers.");
        return Send(id);
    }



    public bool Declares(string name) => _events.Contains(name);

    public IReadOnlyList<string> Events => _events;

    public IReadOnlyList<Fired> Send(int eventId)
    {
        var fired = new List<Fired>();
        var moved = new Dictionary<string, string>(StringComparer.Ordinal);
        _blocked.Clear();



        foreach (var (machineId, stateId) in _in.ToList())
        {
            var candidates = _routes.LeavingState(stateId)
                .Where(r => r.EventId == eventId && r.MachineId == machineId)
                .Select(r => (Route: r, Detail: Detail(r)))
                .OrderByDescending(x => x.Detail.Priority)
                .ThenBy(x => x.Detail.Order)
                .ToList();









            foreach (var held in candidates)
            {
                if (held.Detail.Condition.Length > 0) ConditionsWeighed++;
                if (Test(held.Detail.Condition) != Expression.Verdict.False) continue;
                var to = _model.Get(held.Route.ToId);
                _blocked.Add(new Blocked(machineId, stateId, held.Route.ToId, to?.Str("name") ?? "",
                                         held.Route.Event, held.Detail.Condition));
            }

            var pick = candidates.FirstOrDefault(x => Test(x.Detail.Condition) != Expression.Verdict.False);

            if (pick.Route == null) continue;

            var target = _model.Get(pick.Route.ToId);
            fired.Add(new Fired(machineId, stateId, pick.Route.ToId, target?.Str("name") ?? "",
                pick.Route.Event, pick.Detail.Priority, pick.Detail.Condition.Length > 0, pick.Detail.Condition));

            moved[machineId] = pick.Route.ToId;





            if (pick.Detail.Duration > 0 && pick.Route.ToId != stateId)
                _blending[machineId] = new Blend(machineId, stateId, pick.Route.ToId, pick.Detail.Duration, 0);
            else
                _blending.Remove(machineId);



            if (pick.Route.IntoId.Length > 0)
            {
                string inner = _routes.MachineOfState.TryGetValue(pick.Route.IntoId, out var m) ? m : "";
                if (inner.Length > 0) moved[inner] = pick.Route.IntoId;
            }
        }

        if (moved.Count > 0) Rebuild(moved);
        return fired;
    }












    public IReadOnlyList<Fired> Advance(float seconds)
    {
        var fired = new List<Fired>();
        if (seconds <= 0) return fired;

        foreach (string machineId in _blending.Keys.ToList())
        {
            var blend = _blending[machineId] with { Elapsed = _blending[machineId].Elapsed + seconds };
            if (blend.Elapsed >= blend.Duration) _blending.Remove(machineId);
            else _blending[machineId] = blend;
        }

        if (_clips.Count == 0 || _playing.Count == 0) return fired;





        var raised = new List<string>();

        foreach (string clipId in _playing.Keys.ToList())
        {
            if (!_clips.TryGetValue(clipId, out var clip) || !clip.Known) continue;

            float from = _playing[clipId];
            float to = from + seconds;

            foreach (var trigger in clip.Triggers)
            {



                if (trigger.At > from && trigger.At <= to && !raised.Contains(trigger.Event))
                    raised.Add(trigger.Event);
            }





            _playing[clipId] = clip.Looping && to >= clip.Seconds
                ? to % clip.Seconds
                : Math.Min(to, clip.Seconds);
        }

        foreach (string name in raised) fired.AddRange(Send(name));
        return fired;
    }


    public float? PlayingAt(string clipId) => _playing.TryGetValue(clipId, out float at) ? at : null;


    public IReadOnlyList<(ClipTiming.Clip Clip, float At)> Playing() =>
        _playing.Where(p => _clips.ContainsKey(p.Key))
                .Select(p => (_clips[p.Key], p.Value))
                .OrderBy(p => p.Item1.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();








    public void Time(IReadOnlyDictionary<string, ClipTiming.Clip> clips)
    {
        _clips = clips;

        foreach (var clip in clips.Values)
        {
            if (clip.Known || clip.Triggers.Count > 0) continue;
            _stops.Add(new Stop(clip.ClipId, "hkbClipGenerator",
                $"'{clip.Name}' has no length, so nothing it raises at a point in itself can be " +
                $"timed: {clip.Why}"));
        }
    }



    public void Settle() => _blending.Clear();












    private void Rebuild(Dictionary<string, string> moved)
    {
        var keep = new Dictionary<string, string>(_in, StringComparer.Ordinal);
        foreach (var (machineId, stateId) in moved) keep[machineId] = stateId;

        _in.Clear();
        _resume = keep;
        _wasPlaying = new Dictionary<string, float>(_playing, StringComparer.Ordinal);
        _playing.Clear();
        if (RootId.Length > 0) Enter(RootId, 0, new HashSet<string>(StringComparer.Ordinal));
        _resume = null;
        _wasPlaying = null;
    }


    private Dictionary<string, string>? _resume;





    private (int Priority, int Order, string Condition, float Duration) Detail(StateRoutes.Route route)
    {
        var machine = _model.Get(route.MachineId);
        if (machine == null) return (0, 0, "", 0);

        string arrayId = route.Wildcard
            ? machine.Ref("wildcardTransitions") ?? ""
            : _model.Get(route.FromId)?.Ref("transitions") ?? "";

        var array = _model.Get(arrayId);
        if (array == null || !array.StructLists.TryGetValue("transitions", out var rows)) return (0, 0, "", 0);

        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].TryGetValue("eventId", out var ev);
            rows[i].TryGetValue("toStateId", out var to);
            if (ev != route.EventId.ToString()) continue;

            var target = _model.Get(route.ToId);
            if (target != null && to != target.Int("stateId").ToString()) continue;

            rows[i].TryGetValue("priority", out var pr);
            rows[i].TryGetValue("condition", out var cond);

            string condition = "";
            if (!string.IsNullOrEmpty(cond) && cond != "null")
            {
                var held = _model.Get(cond.TrimStart('#'));
                condition = held?.Str("expression") ?? held?.Class ?? cond;
            }

            rows[i].TryGetValue("transition", out var effect);
            float duration = TransitionDuration(effect);

            return (int.TryParse(pr, out int p) ? p : 0, i, condition, duration);
        }

        return (0, 0, "", 0);
    }






    private float TransitionDuration(string? effectRef)
    {
        if (string.IsNullOrEmpty(effectRef) || effectRef == "null") return 0;

        var effect = _model.Get(effectRef.TrimStart('#'));
        if (effect == null) return 0;

        string field = effect.Class switch
        {
            "hkbBlendingTransitionEffect" => "duration",
            "hkbGeneratorTransitionEffect" => "blendInDuration",
            _ => "",
        };
        if (field.Length == 0) return 0;

        return float.TryParse(effect.Str(field), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float d) && d > 0 ? d : 0;
    }


    public sealed record Reach(
        IReadOnlyDictionary<string, SortedSet<string>> EventsInto,
        IReadOnlyCollection<string> Reachable,
        IReadOnlyCollection<string> Unreachable,
        IReadOnlyCollection<StateRoutes.Route> Dead,
        int Conditional);
















    public Reach Reachable()
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var into = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        int conditional = 0;











        var pending = new Queue<string>();
        var descended = new HashSet<string>(StringComparer.Ordinal);

        void Reach(string stateId)
        {
            if (stateId.Length == 0 || !reached.Add(stateId)) return;
            pending.Enqueue(stateId);
        }

        foreach (var active in Where()) Reach(active.StateId);

        while (pending.Count > 0)
        {
            string stateId = pending.Dequeue();



            if (descended.Add(stateId))
            {
                var below = new GraphRun(_model);
                below.Switch(_routes.MachineOfState.TryGetValue(stateId, out var owner) ? owner : stateId,
                             stateId, 0, new HashSet<string>(StringComparer.Ordinal));
                foreach (var active in below.Where()) Reach(active.StateId);
            }

            foreach (var route in _routes.LeavingState(stateId))
            {
                if (!into.TryGetValue(route.ToId, out var events))
                    into[route.ToId] = events = new SortedSet<string>(StringComparer.Ordinal);
                events.Add(route.Event);

                Reach(route.ToId);
                Reach(route.IntoId);
            }
        }

        foreach (var route in _routes.Routes)
            if (route.Event.Length > 0 && Detail(route).Condition.Length > 0) conditional++;

        var everyState = _routes.MachineOfState.Keys.ToHashSet(StringComparer.Ordinal);
        var dead = _routes.Routes.Where(r => !r.Wildcard && !reached.Contains(r.FromId)).ToList();

        return new Reach(into, reached.Where(everyState.Contains).ToList(),
                         everyState.Except(reached).ToList(), dead, conditional);
    }
}
