using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;




public static class StateEditor
{
    public sealed class StateRow
    {
        public string Id = "";
        public int StateId;
        public string Name = "";
        public string GeneratorRef = "";
        public string TransitionsRef = "";
        public bool Enabled = true;
    }

    public sealed class TransitionRow
    {
        public string ArrayId = "";
        public int Index;
        public int FromStateId = -1;
        public int ToStateId = -1;
        public int ToNestedStateId;
        public int EventId = -1;
        public int Priority;
        public int Flags;
        public bool Wildcard;

        public bool HasFlag(int flag) => (Flags & flag) == flag;
    }

    public static List<StateRow> States(BehaviourGraphModel model, string machineId)
    {
        var rows = new List<StateRow>();
        var machine = model.Get(machineId);
        if (machine == null) return rows;

        foreach (string id in machine.Refs("states"))
        {
            var info = model.Get(id);
            if (info == null) continue;
            rows.Add(new StateRow
            {
                Id = info.Id,
                StateId = info.Int("stateId"),
                Name = info.Str("name"),
                GeneratorRef = info.Str("generator"),
                TransitionsRef = info.Str("transitions"),
                Enabled = !string.Equals(info.Str("enable"), "false", StringComparison.OrdinalIgnoreCase),
            });
        }
        return rows;
    }

    public static List<TransitionRow> Transitions(BehaviourGraphModel model, string machineId)
    {
        var rows = new List<TransitionRow>();
        var machine = model.Get(machineId);
        if (machine == null) return rows;

        foreach (var (arrayRef, wildcard, from) in Arrays(model, machine))
        {
            var array = model.Get(arrayRef);
            if (array == null || !array.StructLists.TryGetValue("transitions", out var elements)) continue;
            for (int i = 0; i < elements.Count; i++)
            {
                elements[i].TryGetValue("toStateId", out var to);
                elements[i].TryGetValue("eventId", out var ev);
                elements[i].TryGetValue("toNestedStateId", out var nested);
                elements[i].TryGetValue("priority", out var priority);
                elements[i].TryGetValue("flags", out var flags);
                rows.Add(new TransitionRow
                {
                    ArrayId = array.Id,
                    Index = i,
                    FromStateId = from,
                    ToStateId = int.TryParse(to, out int t) ? t : -1,
                    ToNestedStateId = int.TryParse(nested, out int n) ? n : 0,
                    EventId = int.TryParse(ev, out int e) ? e : -1,
                    Priority = int.TryParse(priority, out int p) ? p : 0,
                    Flags = TransitionFlags(flags),
                    Wildcard = wildcard,
                });
            }
        }
        return rows;
    }

    private static int TransitionFlags(string? text)
    {
        text = text?.Trim() ?? "";
        if (int.TryParse(text, out int number)) return number;

        var declared = HavokClassTypes.Shipped
            .Enum("hkbStateMachineTransitionInfo", "TransitionFlags");
        if (declared == null) return 0;

        int bits = 0;
        foreach (string part in text.Split('|', StringSplitOptions.RemoveEmptyEntries))
            if (declared.TryGetValue(part.Trim(), out long value)) bits |= (int)value;
        return bits;
    }

    private static IEnumerable<(string Ref, bool Wildcard, int FromStateId)> Arrays(
        BehaviourGraphModel model, HkObject machine)
    {
        string wild = machine.Ref("wildcardTransitions") ?? "";
        if (wild.Length > 0) yield return (wild, true, -1);

        foreach (string id in machine.Refs("states"))
        {
            var info = model.Get(id);
            string own = info?.Ref("transitions") ?? "";
            if (own.Length > 0) yield return (own, false, info?.Int("stateId") ?? -1);
        }
    }

    private static int NextStateId(BehaviourGraphModel model, string machineId)
    {
        var used = States(model, machineId).Select(s => s.StateId).ToList();
        return used.Count == 0 ? 0 : used.Max() + 1;
    }



    public static string AddState(string xml, string machineId, string name, string generatorRef,
                                  out string newObjectId, out int newStateId)
    {
        var model = BehaviourGraphModel.Parse(xml);
        if (model.Get(machineId) is null) throw new ArgumentException($"#{machineId} is not in this file");
        if (generatorRef.StartsWith('#') && model.Get(generatorRef[1..]) is null)
            throw new ArgumentException($"generator {generatorRef} is not in this file");

        newStateId = NextStateId(model, machineId);

        string inner =
            "            <hkparam name=\"variableBindingSet\">null</hkparam>\n" +
            "            <hkparam name=\"listeners\" numelements=\"0\">\n</hkparam>\n" +
            "            <hkparam name=\"enterNotifyEvents\">null</hkparam>\n" +
            "            <hkparam name=\"exitNotifyEvents\">null</hkparam>\n" +
            "            <hkparam name=\"transitions\">null</hkparam>\n" +
            $"            <hkparam name=\"generator\">{generatorRef}</hkparam>\n" +
            $"            <hkparam name=\"name\">{name}</hkparam>\n" +
            $"            <hkparam name=\"stateId\">{newStateId}</hkparam>\n" +
            "            <hkparam name=\"probability\">1.0</hkparam>\n" +
            "            <hkparam name=\"enable\">true</hkparam>";

        xml = HkxTextEdit.AddObject(xml, "hkbStateMachineStateInfo",
                                    HkxSignatures.Of("hkbStateMachineStateInfo"), inner, out newObjectId);
        return HkxTextEdit.ArrayAppend(xml, machineId, "states", $"                #{newObjectId}");
    }



    public static string RemoveState(string xml, string machineId, string stateObjectId, out int strippedTransitions)
    {
        var model = BehaviourGraphModel.Parse(xml);
        var machine = model.Get(machineId) ?? throw new ArgumentException($"#{machineId} is not in this file");
        var state = model.Get(stateObjectId) ?? throw new ArgumentException($"#{stateObjectId} is not in this file");

        int stateId = state.Int("stateId");
        var refs = machine.Refs("states");
        int position = refs.IndexOf(stateObjectId);
        if (position < 0) throw new ArgumentException($"#{stateObjectId} is not a state of #{machineId}");

        strippedTransitions = 0;
        foreach (var t in Transitions(model, machineId).Where(t => t.ToStateId == stateId)
                                                      .OrderByDescending(t => t.Index))
        {
            xml = HkxTextEdit.ArrayRemoveAt(xml, t.ArrayId, "transitions", t.Index);
            strippedTransitions++;
        }

        return HkxTextEdit.ArrayRemoveAt(xml, machineId, "states", position);
    }



    public static string AddTransition(string xml, string machineId, string fromStateObjectId,
                                       int toStateId, int eventId, string effectRef)
    {
        var model = BehaviourGraphModel.Parse(xml);
        var machine = model.Get(machineId) ?? throw new ArgumentException($"#{machineId} is not in this file");

        if (!States(model, machineId).Any(s => s.StateId == toStateId))
            throw new ArgumentException($"#{machineId} has no state with stateId {toStateId}");

        bool wildcard = string.IsNullOrEmpty(fromStateObjectId);
        string owner = wildcard ? machineId : fromStateObjectId;
        string field = wildcard ? "wildcardTransitions" : "transitions";

        var holder = model.Get(owner) ?? throw new ArgumentException($"#{owner} is not in this file");
        string arrayRef = holder.Ref(field) ?? "";

        string element =
            "                <hkobject>\n" +
            "                    <hkparam name=\"triggerInterval\">\n" +
            Interval("triggerInterval") +
            "                    </hkparam>\n" +
            "                    <hkparam name=\"initiateInterval\">\n" +
            Interval("initiateInterval") +
            "                    </hkparam>\n" +
            $"                    <hkparam name=\"transition\">{effectRef}</hkparam>\n" +
            "                    <hkparam name=\"condition\">null</hkparam>\n" +
            $"                    <hkparam name=\"eventId\">{eventId}</hkparam>\n" +
            $"                    <hkparam name=\"toStateId\">{toStateId}</hkparam>\n" +
            "                    <hkparam name=\"fromNestedStateId\">0</hkparam>\n" +
            "                    <hkparam name=\"toNestedStateId\">0</hkparam>\n" +
            "                    <hkparam name=\"priority\">0</hkparam>\n" +
            "                    <hkparam name=\"flags\">0</hkparam>\n" +
            "                </hkobject>";

        if (arrayRef.Length > 0)
            return HkxTextEdit.ArrayAppend(xml, arrayRef, "transitions", element);

        string inner = $"            <hkparam name=\"transitions\" numelements=\"1\">\n{element}\n            </hkparam>";
        xml = HkxTextEdit.AddObject(xml, "hkbStateMachineTransitionInfoArray",
                                    HkxSignatures.Of("hkbStateMachineTransitionInfoArray"), inner, out string arrayId);
        return HkxTextEdit.SetParam(xml, owner, field, "#" + arrayId);
    }

    private static string Interval(string name) =>
        $"                        <hkobject class=\"hkbStateMachineTimeInterval\" name=\"{name}\" signature=\"{HkxSignatures.Of("hkbStateMachineTimeInterval")}\">\n" +
        "                            <hkparam name=\"enterEventId\">-1</hkparam>\n" +
        "                            <hkparam name=\"exitEventId\">-1</hkparam>\n" +
        "                            <hkparam name=\"enterTime\">0.0</hkparam>\n" +
        "                            <hkparam name=\"exitTime\">0.0</hkparam>\n" +
        "                        </hkobject>\n";

    public static string RemoveTransition(string xml, string arrayId, int index) =>
        HkxTextEdit.ArrayRemoveAt(xml, arrayId, "transitions", index);
}
