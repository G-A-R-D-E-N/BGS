using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// Which event moves which state to which state, as objects the canvas can draw between.
//
// The file does not hold it in that shape. A transition lives inside an array of structs, its target
// is a `stateId` that means something only inside the machine that owns the array, and the event is
// an index into a list of names held somewhere else entirely. None of those are object references,
// so the graph view, which draws references, has never had anything to draw.
//
// Measured over the 531 vanilla behaviours before this was built, because the objection to building
// it was that a transition cannot be drawn honestly:
//
//   6,394 transitions, of which 2,398 are wildcards and 230 carry a nested state id
//   0 whose toStateId is not a state of its own machine
//   all 230 nested ids name a real state of the machine under the state being entered
//   median 2 transitions per machine, 90th percentile 8, busiest 168
//
// So every route resolves, and a nested one resolves to two hops rather than to a guess. `symrm
// nesting` reprints those numbers on demand.
public sealed class StateRoutes
{
    /// One transition, as objects rather than as numbers.
    ///
    /// `FromId` is the state the transition leaves, or the machine itself when it is a wildcard,
    /// since a wildcard fires from any state and drawing it from one of them would be a lie. `IntoId`
    /// is the state inside the entered state that the transition also selects, empty for the usual
    /// case.
    public sealed record Route(string MachineId, string FromId, string ToId, string Event,
                               int EventId, bool Wildcard, string IntoId)
    {
        public override string ToString() =>
            $"{(Wildcard ? "any" : "#" + FromId)} -{Event}-> #{ToId}" +
            (IntoId.Length > 0 ? $" then #{IntoId}" : "");
    }

    /// State info objects that are their machine's starting state.
    public readonly HashSet<string> StartStates = new(StringComparer.Ordinal);

    public readonly List<Route> Routes = new();

    /// Routes leaving an object, and routes arriving at it. A wildcard is keyed from its machine.
    public readonly Dictionary<string, List<Route>> Out = new(StringComparer.Ordinal);
    public readonly Dictionary<string, List<Route>> In = new(StringComparer.Ordinal);

    /// Everything a node is joined to by a transition, either way round, which is what the canvas
    /// needs to keep lit when one node is picked out.
    public IEnumerable<string> Touching(string id)
    {
        if (Out.TryGetValue(id, out var leaving))
            foreach (var route in leaving)
            {
                yield return route.ToId;
                if (route.IntoId.Length > 0) yield return route.IntoId;
            }

        if (In.TryGetValue(id, out var arriving))
            foreach (var route in arriving) yield return route.FromId;
    }

    public static StateRoutes Of(BehaviourGraphModel model)
    {
        var routes = new StateRoutes();
        var events = SymbolEditor.EventNames(model);

        foreach (var machine in model.Objects.Where(o => o.Class == "hkbStateMachine"))
        {
            var states = StateEditor.States(model, machine.Id);
            if (states.Count == 0) continue;

            // A stateId is an index into this machine's states and no other's, so the lookup is
            // rebuilt per machine rather than shared.
            var byStateId = new Dictionary<int, string>();
            foreach (var state in states) byStateId.TryAdd(state.StateId, state.Id);

            int start = machine.Int("startStateId");
            if (byStateId.TryGetValue(start, out var startId)) routes.StartStates.Add(startId);

            foreach (var row in StateEditor.Transitions(model, machine.Id))
            {
                if (!byStateId.TryGetValue(row.ToStateId, out var toId)) continue;

                string fromId = row.Wildcard ? machine.Id
                    : byStateId.TryGetValue(row.FromStateId, out var from) ? from : machine.Id;

                routes.Add(new Route(machine.Id, fromId, toId, NameOf(events, row.EventId),
                                     row.EventId, row.Wildcard,
                                     NestedTarget(model, toId, row.ToNestedStateId)));
            }
        }

        return routes;
    }

    private void Add(Route route)
    {
        Routes.Add(route);
        if (!Out.TryGetValue(route.FromId, out var leaving)) Out[route.FromId] = leaving = new List<Route>();
        leaving.Add(route);
        if (!In.TryGetValue(route.ToId, out var arriving)) In[route.ToId] = arriving = new List<Route>();
        arriving.Add(route);
    }

    /// An event id with no name in the file is shown as the number. A transition with no event fires
    /// on time or on a condition rather than on anything sent, which is a different thing from one
    /// whose event we failed to look up, so the two do not share a label.
    private static string NameOf(IReadOnlyList<string> events, int id) =>
        id < 0 ? "no event"
        : id < events.Count && events[id].Length > 0 ? events[id]
        : id.ToString();

    /// The state a nested id names, which sits in the machine under the state being entered.
    ///
    /// The machine is usually not that state's generator directly: a modifier generator or a bone
    /// switch holds it. Looking only at the generator finds 81 of the 230 nested transitions in the
    /// vanilla data and makes the other 149 look like something we cannot explain, which is what a
    /// first pass at this reported.
    private static string NestedTarget(BehaviourGraphModel model, string enteredId, int nestedStateId)
    {
        if (nestedStateId == 0) return "";

        var entered = model.Get(enteredId);
        var machine = MachineUnder(model, model.Get(entered?.Ref("generator")), 0);
        if (machine == null) return "";

        return StateEditor.States(model, machine.Id)
                          .FirstOrDefault(s => s.StateId == nestedStateId)?.Id ?? "";
    }

    /// The state machine a generator leads to, through whatever wraps it. A behaviour reference
    /// generator loads another file and leads nowhere this file can see, which is a genuine stop and
    /// comes back as null like anything else we cannot follow.
    public static HkObject? MachineUnder(BehaviourGraphModel model, HkObject? generator, int depth)
    {
        if (generator == null || depth > 6) return null;
        if (generator.Class == "hkbStateMachine") return generator;

        foreach (string field in new[] { "generator", "pDefaultGenerator", "pBlenderGenerator" })
        {
            var found = MachineUnder(model, model.Get(generator.Ref(field)), depth + 1);
            if (found != null) return found;
        }
        return null;
    }
}
