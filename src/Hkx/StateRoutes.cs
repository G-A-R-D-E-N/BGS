using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;


















public sealed class StateRoutes
{






    public sealed record Route(string MachineId, string FromId, string ToId, string Event,
                               int EventId, bool Wildcard, string IntoId, bool Global = false)
    {
        public override string ToString() =>
            $"{(Wildcard ? "any" : "#" + FromId)} -{Event}-> #{ToId}" +
            (IntoId.Length > 0 ? $" then #{IntoId}" : "");
    }


    public readonly HashSet<string> StartStates = new(StringComparer.Ordinal);

    public readonly List<Route> Routes = new();


    public readonly Dictionary<string, List<Route>> Out = new(StringComparer.Ordinal);
    public readonly Dictionary<string, List<Route>> In = new(StringComparer.Ordinal);






    public readonly Dictionary<string, string> MachineOfState = new(StringComparer.Ordinal);


    public readonly Dictionary<string, List<string>> StatesOf = new(StringComparer.Ordinal);







    public IEnumerable<Route> LeavingState(string stateId)
    {
        if (Out.TryGetValue(stateId, out var own))
            foreach (var route in own.Where(r => !r.Wildcard)) yield return route;

        if (!MachineOfState.TryGetValue(stateId, out var machineId)) yield break;
        if (!Out.TryGetValue(machineId, out var wildcards)) yield break;

        foreach (var route in wildcards.Where(r => r.Wildcard))
        {



            if (route.ToId == stateId) continue;
            yield return route with { FromId = stateId };
        }
    }



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



        foreach (var route in LeavingState(id))
        {
            yield return route.ToId;
            if (route.IntoId.Length > 0) yield return route.IntoId;
        }
    }

    public static StateRoutes Of(BehaviourGraphModel model)
    {
        var routes = new StateRoutes();
        var events = SymbolEditor.EventNames(model);

        foreach (var machine in model.Objects.Where(o => o.Class == "hkbStateMachine"))
        {
            var states = StateEditor.States(model, machine.Id);
            if (states.Count == 0) continue;



            var byStateId = new Dictionary<int, string>();
            foreach (var state in states) byStateId.TryAdd(state.StateId, state.Id);

            routes.StatesOf[machine.Id] = states.Select(s => s.Id).ToList();
            foreach (var state in states) routes.MachineOfState[state.Id] = machine.Id;

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




    private static string NameOf(IReadOnlyList<string> events, int id) =>
        id < 0 ? "no event"
        : id < events.Count && events[id].Length > 0 ? events[id]
        : id.ToString();







    private static string NestedTarget(BehaviourGraphModel model, string enteredId, int nestedStateId)
    {
        if (nestedStateId == 0) return "";

        var entered = model.Get(enteredId);
        var machine = MachineUnder(model, model.Get(entered?.Ref("generator")), 0);
        if (machine == null) return "";

        return StateEditor.States(model, machine.Id)
                          .FirstOrDefault(s => s.StateId == nestedStateId)?.Id ?? "";
    }




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
