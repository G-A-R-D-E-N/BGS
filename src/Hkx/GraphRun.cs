using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// Running the graph, rather than only drawing it.
//
// Everything else here is static. The canvas says a transition listens for `StartOpen`, and whether
// sending `StartOpen` actually gets you there could only be answered by loading the game. A behaviour
// graph is a small interpreter and this file parses all of it, so stepping it is a loop.
//
// What this is, stated plainly, because it matters: this is our reading of the format, not Havok's
// runtime. Havok never shipped the behaviour product's source, so there is no reference
// implementation to check against and nothing here can be proved correct the way the packfile writer
// was proved against the game's own writer. What can be done is to refuse to guess. Anything this
// cannot model is recorded as a stop and reported, rather than being quietly stepped through as
// though it were understood.
//
// The corpus says the job is smaller than it looks. All 6,394 transitions in the 531 vanilla
// behaviours carry an event id, so none of them fires on time alone, and only 115 of them carry a
// condition. So an event driven reading covers 98% of the vanilla data exactly, and the rest is
// named rather than assumed.
//
// What is deliberately not here yet: time, and therefore blend weights. A transition takes a
// duration, a blender mixes its children by weight, and a clip has a length. None of that is stepped.
// This answers which state you end up in, not what the character looks like part way there.
public sealed class GraphRun
{
    /// Generators that lead somewhere this file cannot see.
    ///
    /// A behaviour reference generator loads another behaviour by name, and a graph swap generator
    /// replaces the running graph outright. Following either would mean opening another file and
    /// guessing which, so the walk stops and says so. 81 states in the corpus sit on the first and 11
    /// on the second.
    private static readonly HashSet<string> Opaque = new(StringComparer.Ordinal)
    {
        "hkbBehaviorReferenceGenerator",
        "BSBehaviorGraphSwapGenerator",
    };

    public sealed record Active(string MachineId, string MachineName, string StateId, int Number, string StateName)
    {
        public override string ToString() => $"#{MachineId} '{MachineName}' is in #{StateId} '{StateName}'";
    }

    /// Somewhere the walk would have had to guess, recorded instead.
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

    private readonly BehaviourGraphModel _model;
    private readonly StateRoutes _routes;
    private readonly List<string> _events;

    /// The active state of every machine the walk reaches, keyed by machine.
    private readonly Dictionary<string, string> _in = new(StringComparer.Ordinal);
    private readonly List<Stop> _stops = new();

    public IReadOnlyList<Stop> Stops => _stops;
    public string RootId { get; private set; } = "";

    /// Which state each running machine is in, in the order the walk reached them.
    public IReadOnlyList<Active> Where() => _in
        .Select(pair => Describe(pair.Key, pair.Value))
        .Where(a => a != null)
        .Select(a => a!)
        .ToList();

    private GraphRun(BehaviourGraphModel model)
    {
        _model = model;
        _routes = StateRoutes.Of(model);
        _events = SymbolEditor.EventNames(model);
    }

    /// Puts the graph in the state it starts in.
    ///
    /// The root is the behaviour graph's own generator rather than the first state machine in the
    /// file. Those are usually the same object and when they are not, picking the first machine
    /// starts the run somewhere the game never starts it.
    public static GraphRun Start(BehaviourGraphModel model)
    {
        var run = new GraphRun(model);

        var graph = model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraph");
        string root = graph?.Ref("rootGenerator") ?? "";

        if (root.Length == 0)
        {
            // No behaviour graph object, which happens in files that are a fragment rather than a
            // whole character. Falling back to a machine nothing points at is a guess, so it is
            // recorded as one.
            var pointedAt = model.Objects
                .SelectMany(o => GraphLinks.OutSlots(model, o).SelectMany(s => s.Targets))
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

    /// Walks down from a generator, switching on every machine it passes through.
    ///
    /// More than one machine can be running at once and that is not an edge case: a blender runs all
    /// of its children and a layer generator runs all of its layers, so a character is normally
    /// several machines deep in several places at the same time. Anything that only tracked one
    /// active state would be describing a graph the game does not have.
    private void Enter(string generatorId, int depth, HashSet<string> onPath)
    {
        if (generatorId.Length == 0 || depth > 32) return;

        // Guards a generator that reaches itself. The corpus has none, but a file being edited can
        // have one for as long as it takes to wire the second half of a change.
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

            // A machine that was already running stays where it was rather than being restarted,
            // because a rebuild is working out what is running and not re-entering everything.
            if (_resume != null && _resume.TryGetValue(node.Id, out var was) &&
                states.Any(s => s.Id == was))
                into = states.First(s => s.Id == was);

            if (into == null)
            {
                // A start state that is not in the machine is already an error the validator reports.
                // Saying so again here would be noise; what matters to a run is that it cannot begin.
                _stops.Add(new Stop(node.Id, node.Class,
                    $"its start state {start} is not one of its {states.Count} state(s), so it cannot be entered"));
                onPath.Remove(generatorId);
                return;
            }

            // A start state selector picks the start state at runtime from something outside the
            // graph, so which state this machine really begins in is not knowable from the file.
            if ((node.Ref("startStateIdSelector") ?? "").Length > 0)
                _stops.Add(new Stop(node.Id, node.Class,
                    "its start state is chosen at runtime by a selector, so the state named here is " +
                    "the one the file declares rather than the one the game will pick"));

            Switch(node.Id, into.Id, depth, onPath);
            onPath.Remove(generatorId);
            return;
        }

        foreach (string next in Below(node)) Enter(next, depth + 1, onPath);
        onPath.Remove(generatorId);
    }

    /// Puts a machine into a state and walks down into whatever that state generates.
    private void Switch(string machineId, string stateId, int depth, HashSet<string> onPath)
    {
        _in[machineId] = stateId;
        string generator = _model.Get(stateId)?.Ref("generator") ?? "";
        if (generator.Length > 0) Enter(generator, depth + 1, onPath);
    }

    // Which field of which class holds something that runs, taken from the class table rather than
    // from a list written out by hand.
    //
    // The first attempt matched on the target's class name ending in Generator, which is wrong and
    // wrong quietly. A layer generator holds `hkbLayer` and a blender holds `hkbBlenderGeneratorChild`
    // and neither is a generator, so the walk stopped at the first one and reported Dogmeat as having
    // exactly one machine running out of its thirty. It looked plausible: one machine, one active
    // state, no errors.
    //
    // The table already knows. A field that carries something runnable is declared as a pointer or an
    // array of pointers to `hkbGenerator`, so the set below is read off the game's own class layouts
    // and covers classes nobody here has thought about.
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

                // Two of these are not part of the running tree. A transition effect's generator is
                // what plays *during* a transition rather than what the graph is in, and a set
                // behaviour command is an instruction rather than a node. Walking either would report
                // states as running that are not.
                if (className is "hkbGeneratorTransitionEffect" or "hkbSetBehaviorCommand") continue;

                if (!carriers.TryGetValue(className, out var fields))
                    carriers[className] = fields = new HashSet<string>(StringComparer.Ordinal);
                fields.Add(member.Name);
            }
        }

        // A state machine held by a state manager's data is reached through a field declared as
        // pointing at the machine rather than at a generator, so the table's own rule misses it.
        carriers["BSIStateManagerModifierBSiStateData"] = new HashSet<string>(StringComparer.Ordinal) { "pStateMachine" };
        return carriers;
    }

    /// Classes that exist only to hold something runnable, rather than to run anything themselves.
    ///
    /// A blender's `children` array is declared as pointing at `hkbBlenderGeneratorChild`, not at
    /// `hkbGenerator`, so the rule above does not reach it. The wrapper does hold a generator, which
    /// is exactly what makes it a wrapper, so anything pointing at one is followed.
    private static readonly HashSet<string> Wrappers = Carries.Keys
        .Where(c => !c.EndsWith("Generator", StringComparison.Ordinal) && c != "hkbBehaviorGraph" &&
                    c != "hkbStateMachineStateInfo")
        .ToHashSet(StringComparer.Ordinal);

    /// What a node runs, as opposed to everything it points at.
    ///
    /// Everything else a node points at is a modifier, a binding, an event or a payload, and running
    /// those is not what this does. A state's own generator is followed by `Switch` rather than here,
    /// because a machine's states are entered one at a time and not all together.
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

    /// Sends an event and returns every transition it fired.
    ///
    /// Every running machine gets the event, which is what the engine does: an event is raised on the
    /// graph and not on one machine, so two machines listening for the same event both move. Within a
    /// machine the highest priority transition wins, and declaration order breaks a tie, which is the
    /// order the file lists them in.
    public IReadOnlyList<Fired> Send(string name)
    {
        int id = _events.IndexOf(name);
        if (id < 0)
            throw new ArgumentException(
                $"this graph declares no event called '{name}', so nothing could be sent. " +
                "An event nothing listens for and an event that does not exist are different answers.");
        return Send(id);
    }

    /// Whether the graph declares an event at all, so a caller can tell the two answers apart before
    /// asking for one.
    public bool Declares(string name) => _events.Contains(name);

    public IReadOnlyList<string> Events => _events;

    public IReadOnlyList<Fired> Send(int eventId)
    {
        var fired = new List<Fired>();
        var moved = new Dictionary<string, string>(StringComparer.Ordinal);

        // Snapshotted first. A machine that moves must not then be re-examined with its new state in
        // the same send, or one event walks a chain of transitions in a single step.
        foreach (var (machineId, stateId) in _in.ToList())
        {
            var pick = _routes.LeavingState(stateId)
                .Where(r => r.EventId == eventId && r.MachineId == machineId)
                .Select(r => (Route: r, Detail: Detail(r)))
                .OrderByDescending(x => x.Detail.Priority)
                .ThenBy(x => x.Detail.Order)
                .FirstOrDefault();

            if (pick.Route == null) continue;

            var target = _model.Get(pick.Route.ToId);
            fired.Add(new Fired(machineId, stateId, pick.Route.ToId, target?.Str("name") ?? "",
                pick.Route.Event, pick.Detail.Priority, pick.Detail.Condition.Length > 0, pick.Detail.Condition));

            moved[machineId] = pick.Route.ToId;

            // A transition can name a state inside the machine the entered state holds, which puts
            // that inner machine somewhere other than its own start state.
            if (pick.Route.IntoId.Length > 0)
            {
                string inner = _routes.MachineOfState.TryGetValue(pick.Route.IntoId, out var m) ? m : "";
                if (inner.Length > 0) moved[inner] = pick.Route.IntoId;
            }
        }

        if (moved.Count > 0) Rebuild(moved);
        return fired;
    }

    /// Works out which machines are running now, after some of them have moved.
    ///
    /// A machine runs because something above it is in a state that holds it, so leaving that state
    /// stops it. Applying a transition in place and leaving the rest of the map alone does not model
    /// that: the machines under the state just left stay in the active set for the rest of the run,
    /// still answering events, and the set only ever grows. On a door that is one stale machine and
    /// on a character it is most of them.
    ///
    /// So the configuration is derived from the root every time rather than edited. A machine still
    /// reachable keeps where it was, and a machine reached for the first time starts at its own start
    /// state, which is what entering a machine means.
    private void Rebuild(Dictionary<string, string> moved)
    {
        var keep = new Dictionary<string, string>(_in, StringComparer.Ordinal);
        foreach (var (machineId, stateId) in moved) keep[machineId] = stateId;

        _in.Clear();
        _resume = keep;
        if (RootId.Length > 0) Enter(RootId, 0, new HashSet<string>(StringComparer.Ordinal));
        _resume = null;
    }

    /// Where each machine was before the current rebuild, so one that is still running stays put.
    private Dictionary<string, string>? _resume;

    /// The priority, declaration order and condition of a transition, read back off its own array.
    ///
    /// StateRoutes deliberately does not carry these: it exists to draw lines and a line has no
    /// priority. Rather than widen it for one caller, the row is read again here.
    private (int Priority, int Order, string Condition) Detail(StateRoutes.Route route)
    {
        var machine = _model.Get(route.MachineId);
        if (machine == null) return (0, 0, "");

        string arrayId = route.Wildcard
            ? machine.Ref("wildcardTransitions") ?? ""
            : _model.Get(route.FromId)?.Ref("transitions") ?? "";

        var array = _model.Get(arrayId);
        if (array == null || !array.StructLists.TryGetValue("transitions", out var rows)) return (0, 0, "");

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

            return (int.TryParse(pr, out int p) ? p : 0, i, condition);
        }

        return (0, 0, "");
    }

    /// Everywhere the graph can get to, and what it takes to get there.
    public sealed record Reach(
        IReadOnlyDictionary<string, SortedSet<string>> EventsInto,
        IReadOnlyCollection<string> Reachable,
        IReadOnlyCollection<string> Unreachable,
        IReadOnlyCollection<StateRoutes.Route> Dead,
        int Conditional);

    /// Which states can be reached from the start, by any sequence of events.
    ///
    /// Run as a fixpoint over states rather than a search over whole configurations. A graph with
    /// twenty machines has more configurations than there is any point enumerating, and the question
    /// being asked does not need them: a state is reachable if something that can fire leads to it
    /// from a state that is itself reachable.
    ///
    /// This is more permissive than the validator's own reachability check in two ways, both of them
    /// real. It crosses machine boundaries, so a state entered as a nested target from another
    /// machine counts, and it follows a reached state into the machines its generator holds. The
    /// validator works one machine at a time and cannot see either.
    ///
    /// A transition with a condition counts as able to fire, and how many did is reported, because
    /// nothing here evaluates an expression and calling such a transition dead would be a stronger
    /// claim than the file supports.
    public Reach Reachable()
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var into = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        int conditional = 0;

        // Descending is tracked separately from reaching, and that separation is the whole of what
        // makes this agree with actually stepping.
        //
        // The first version descended a state only at the moment it was first added, which reads as
        // the same thing and is not. A state can arrive by a route that does no descent, as a
        // transition's nested target does, and then never be descended from at all, because every
        // later route into it finds it already present and moves on. The machines that state holds
        // are then invisible to the analysis while a real run walks straight into them. It was 15 of
        // the 531 files, and the only reason it is not still there is that stepping was measured
        // against it rather than assumed to agree.
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

            // Reaching a state also starts whatever it holds, so the machines under it have a start
            // state that is live too.
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
