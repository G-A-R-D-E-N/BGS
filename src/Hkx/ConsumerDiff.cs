using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;















public static class ConsumerDiff
{
    public sealed record Difference(string Consumer, string What)
    {
        public override string ToString() => $"{Consumer}: {What}";
    }

    public sealed record Result(int Compared, IReadOnlyList<Difference> Differences)
    {
        public bool Clean => Differences.Count == 0;

        public override string ToString() =>
            $"{Compared} output(s) compared, {Differences.Count} differing";
    }

    public static Result Compare(BehaviourGraphModel a, BehaviourGraphModel b)
    {
        var differences = new List<Difference>();
        int compared = 0;

        void Same(string consumer, string left, string right)
        {
            compared++;
            if (!string.Equals(left, right, StringComparison.Ordinal))
                differences.Add(new Difference(consumer, First(left, right)));
        }

        Same("symbol audit", SymbolEditor.Audit(a).ToString(), SymbolEditor.Audit(b).ToString());
        Same("variable names", Lines(SymbolEditor.VariableNames(a)), Lines(SymbolEditor.VariableNames(b)));
        Same("variable values", Lines(SymbolEditor.VariableValues(a)), Lines(SymbolEditor.VariableValues(b)));
        Same("variable types", Lines(SymbolEditor.VariableTypes(a).Select(t => t.ToString())),
                               Lines(SymbolEditor.VariableTypes(b).Select(t => t.ToString())));
        Same("event names", Lines(SymbolEditor.EventNames(a)), Lines(SymbolEditor.EventNames(b)));
        Same("binding variable names", Lines(BindingEditor.VariableNames(a)),
                                       Lines(BindingEditor.VariableNames(b)));

        Same("empty states", Lines(GraphValidator.EmptyStates(a).Select(e => $"{e.Id} {e.Name} {e.Machine}")),
                             Lines(GraphValidator.EmptyStates(b).Select(e => $"{e.Id} {e.Name} {e.Machine}")));
        Same("states with no generator", Lines(GraphValidator.StatesWithNoGenerator(a).OrderBy(s => s, StringComparer.Ordinal)),
                                         Lines(GraphValidator.StatesWithNoGenerator(b).OrderBy(s => s, StringComparer.Ordinal)));



        Same("checker findings", Lines(GraphValidator.Check(a).Select(f => f.ToString())),
                                Lines(GraphValidator.Check(b).Select(f => f.ToString())));

        Same("the wiring", Wiring(a), Wiring(b));
        Same("the bindings", Bindings(a), Bindings(b));
        Same("state machine rows", Machines(a), Machines(b));
        Same("what points at what", Referrers(a), Referrers(b));

        return new Result(compared, differences);
    }






    private static string Wiring(BehaviourGraphModel model) =>
        Lines(model.Objects.SelectMany(o => GraphLinks.OutSlots(model, o)
                                                      .Select(s => $"#{o.Id} {o.Class} {s} -> " +
                                                                   string.Join(",", s.Targets))));

    private static string Bindings(BehaviourGraphModel model) =>
        Lines(model.Objects.SelectMany(o => BindingEditor.BindingsOf(model, o)
                                                         .Select(b => $"#{o.Id} {b.SetId} {b.Index} " +
                                                                      $"{b.MemberPath} {b.VariableIndex} {b.BindingType}")));

    private static string Machines(BehaviourGraphModel model)
    {
        var rows = new List<string>();
        foreach (var machine in model.Objects.Where(o => o.Class == "hkbStateMachine"))
        {
            foreach (var state in StateEditor.States(model, machine.Id))
                rows.Add($"#{machine.Id} state {state.Id} {state.StateId} {state.Name} " +
                         $"{state.GeneratorRef} {state.TransitionsRef}");

            foreach (var move in StateEditor.Transitions(model, machine.Id))
                rows.Add($"#{machine.Id} move {move.ArrayId} {move.Index} {move.FromStateId} " +
                         $"{move.ToStateId} {move.ToNestedStateId} {move.EventId} {move.Wildcard}");
        }
        return Lines(rows);
    }

    private static string Referrers(BehaviourGraphModel model) =>
        Lines(model.Objects.Select(o => $"#{o.Id} <- " +
                                        string.Join(",", GeneratorEditor.ReferencesTo(model, o.Id))));

    private static string Lines(IEnumerable<string> values) => string.Join("\n", values);



    private static string First(string left, string right)
    {
        var mine = left.Split('\n');
        var theirs = right.Split('\n');

        for (int i = 0; i < Math.Max(mine.Length, theirs.Length); i++)
        {
            string a = i < mine.Length ? mine[i] : "(nothing)";
            string b = i < theirs.Length ? theirs[i] : "(nothing)";
            if (!string.Equals(a, b, StringComparison.Ordinal))
                return $"line {i + 1} of {mine.Length} is \"{a}\" against \"{b}\"";
        }

        return $"{mine.Length} line(s) against {theirs.Length}";
    }
}
