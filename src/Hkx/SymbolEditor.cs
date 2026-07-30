using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// Variables and events: their names, their declared types, their initial values, and the parallel
// arrays that have to stay the same length as each other.
//
// A graph keeps these in three places at once. hkbBehaviorGraphStringData holds the names,
// hkbBehaviorGraphData holds one info element per name, and hkbVariableValueSet holds one value per
// variable. Add a name without the other two and the engine reads a variable with no declared type.
public static class SymbolEditor
{
    // From PipboyBehavior.hkx: hkbRoleAttribute on a variableInfos element.
    private const string RoleAttributeSignature = "0xfecef669";

    public enum VariableType { Int32, Real, Bool }

    private static string TypeName(VariableType type) => type switch
    {
        VariableType.Int32 => "VARIABLE_TYPE_INT32",
        VariableType.Real => "VARIABLE_TYPE_REAL",
        VariableType.Bool => "VARIABLE_TYPE_BOOL",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public sealed class Counts
    {
        public int Names;
        public int Infos;
        public int Values;
        public int EventNames;
        public int EventInfos;

        public bool VariablesConsistent => Names == Infos && Names == Values;
        public bool EventsConsistent => EventNames == EventInfos;
        public override string ToString() =>
            $"variables names={Names} infos={Infos} values={Values}   events names={EventNames} infos={EventInfos}";
    }

    public static Counts Audit(BehaviourGraphModel model)
    {
        var strings = model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraphStringData");
        var data = model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraphData");
        var values = model.Objects.FirstOrDefault(o => o.Class == "hkbVariableValueSet");

        return new Counts
        {
            Names = strings?.Strings("variableNames").Count ?? 0,
            Infos = data != null && data.StructLists.TryGetValue("variableInfos", out var vi) ? vi.Count : 0,
            Values = values != null && values.StructLists.TryGetValue("wordVariableValues", out var wv) ? wv.Count : 0,
            EventNames = strings?.Strings("eventNames").Count ?? 0,
            EventInfos = data != null && data.StructLists.TryGetValue("eventInfos", out var ei) ? ei.Count : 0,
        };
    }

    public static List<string> VariableNames(BehaviourGraphModel model) =>
        model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraphStringData")?.Strings("variableNames")
        ?? new List<string>();

    public static List<string> EventNames(BehaviourGraphModel model) =>
        model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraphStringData")?.Strings("eventNames")
        ?? new List<string>();

    public static List<string> VariableValues(BehaviourGraphModel model)
    {
        var set = model.Objects.FirstOrDefault(o => o.Class == "hkbVariableValueSet");
        if (set == null || !set.StructLists.TryGetValue("wordVariableValues", out var rows)) return new List<string>();
        return rows.Select(r => r.TryGetValue("value", out var v) ? v : "").ToList();
    }

    // hkbVariableValueSet stores every variable as a 32 bit word, so a float is written as its bit
    // pattern reinterpreted as an int, not as "0.5".
    public static string EncodeValue(VariableType type, string text)
    {
        switch (type)
        {
            case VariableType.Bool:
                bool flag = text.Trim() is "1" or "true" or "True";
                return flag ? "1" : "0";
            case VariableType.Real:
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float real))
                    throw new ArgumentException($"'{text}' is not a number");
                return BitConverter.SingleToInt32Bits(real).ToString(CultureInfo.InvariantCulture);
            default:
                if (!int.TryParse(text, out int whole)) throw new ArgumentException($"'{text}' is not a whole number");
                return whole.ToString(CultureInfo.InvariantCulture);
        }
    }

    public static string DecodeValue(VariableType type, string stored) => type switch
    {
        VariableType.Bool => stored.Trim() == "0" ? "false" : "true",
        VariableType.Real => int.TryParse(stored, out int bits)
            ? BitConverter.Int32BitsToSingle(bits).ToString("0.0#####", CultureInfo.InvariantCulture)
            : stored,
        _ => stored,
    };

    public static List<VariableType> VariableTypes(BehaviourGraphModel model)
    {
        var data = model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraphData");
        var result = new List<VariableType>();
        if (data == null || !data.StructLists.TryGetValue("variableInfos", out var rows)) return result;

        foreach (var row in rows)
        {
            row.TryGetValue("type", out var t);
            result.Add(t switch
            {
                "VARIABLE_TYPE_REAL" => VariableType.Real,
                "VARIABLE_TYPE_BOOL" => VariableType.Bool,
                _ => VariableType.Int32,
            });
        }
        return result;
    }

    public static string SetVariableValue(string xml, int index, string encodedValue)
    {
        var ids = HkxTextEdit.IdsOfClass(xml, "hkbVariableValueSet");
        if (ids.Count == 0) throw new InvalidOperationException("this file has no hkbVariableValueSet");

        var values = VariableValues(BehaviourGraphModel.Parse(xml));
        if (index < 0 || index >= values.Count) throw new ArgumentOutOfRangeException(nameof(index));

        // Element by element replacement, because every element has the same parameter name.
        xml = HkxTextEdit.ArrayRemoveAt(xml, ids[0], "wordVariableValues", index);
        return HkxTextEdit.ArrayInsertAt(xml, ids[0], "wordVariableValues", index,
            "                <hkobject>\n" +
            $"                    <hkparam name=\"value\">{encodedValue}</hkparam>\n" +
            "                </hkobject>");
    }

    // Declares a variable in all three places at once and returns its index.
    public static string AddVariable(string xml, string name, VariableType type, out int index)
    {
        var stringIds = HkxTextEdit.IdsOfClass(xml, "hkbBehaviorGraphStringData");
        if (stringIds.Count == 0) throw new InvalidOperationException("this file has no hkbBehaviorGraphStringData");

        index = VariableNames(BehaviourGraphModel.Parse(xml)).Count;

        xml = HkxTextEdit.ArrayAppend(xml, stringIds[0], "variableNames",
                                      $"                <hkcstring>{name}</hkcstring>");

        var dataIds = HkxTextEdit.IdsOfClass(xml, "hkbBehaviorGraphData");
        if (dataIds.Count > 0)
            xml = HkxTextEdit.ArrayAppend(xml, dataIds[0], "variableInfos",
                "                <hkobject>\n" +
                "                    <hkparam name=\"role\">\n" +
                $"                        <hkobject class=\"hkbRoleAttribute\" name=\"role\" signature=\"{RoleAttributeSignature}\">\n" +
                "                            <hkparam name=\"role\">ROLE_DEFAULT</hkparam>\n" +
                "                            <hkparam name=\"flags\">FLAG_NONE</hkparam>\n" +
                "                        </hkobject>\n" +
                "                    </hkparam>\n" +
                $"                    <hkparam name=\"type\">{TypeName(type)}</hkparam>\n" +
                "                </hkobject>");

        var valueIds = HkxTextEdit.IdsOfClass(xml, "hkbVariableValueSet");
        if (valueIds.Count > 0)
            xml = HkxTextEdit.ArrayAppend(xml, valueIds[0], "wordVariableValues",
                "                <hkobject>\n" +
                "                    <hkparam name=\"value\">0</hkparam>\n" +
                "                </hkobject>");

        return xml;
    }

    public static string AddEvent(string xml, string name, out int index)
    {
        var stringIds = HkxTextEdit.IdsOfClass(xml, "hkbBehaviorGraphStringData");
        if (stringIds.Count == 0) throw new InvalidOperationException("this file has no hkbBehaviorGraphStringData");

        index = EventNames(BehaviourGraphModel.Parse(xml)).Count;

        xml = HkxTextEdit.ArrayAppend(xml, stringIds[0], "eventNames",
                                      $"                <hkcstring>{name}</hkcstring>");

        var dataIds = HkxTextEdit.IdsOfClass(xml, "hkbBehaviorGraphData");
        if (dataIds.Count > 0)
            xml = HkxTextEdit.ArrayAppend(xml, dataIds[0], "eventInfos",
                "                <hkobject>\n" +
                "                    <hkparam name=\"flags\">0</hkparam>\n" +
                "                </hkobject>");

        return xml;
    }

    // Renaming is index preserving on purpose. Transitions store an eventId, not a name, so a rename
    // must not reorder anything or every transition in the file would point somewhere else.
    public static string Rename(string xml, bool variable, int index, string newName)
    {
        var ids = HkxTextEdit.IdsOfClass(xml, "hkbBehaviorGraphStringData");
        if (ids.Count == 0) throw new InvalidOperationException("this file has no hkbBehaviorGraphStringData");

        string field = variable ? "variableNames" : "eventNames";
        var model = BehaviourGraphModel.Parse(xml);
        var names = variable ? VariableNames(model) : EventNames(model);
        if (index < 0 || index >= names.Count) throw new ArgumentOutOfRangeException(nameof(index));

        xml = HkxTextEdit.ArrayRemoveAt(xml, ids[0], field, index);
        return HkxTextEdit.ArrayInsertAt(xml, ids[0], field, index,
                                         $"                <hkcstring>{newName}</hkcstring>");
    }
}
