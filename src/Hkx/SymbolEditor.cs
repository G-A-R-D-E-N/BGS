using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class SymbolEditor
{
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
        public int Bounds;
        public int EventNames;
        public int EventInfos;

        public bool BoundsAreParallel => Bounds == Names;

        public bool VariablesConsistent =>
            Names == Infos && Names == Values && Bounds <= Names;
        public bool EventsConsistent => EventNames == EventInfos;
        public override string ToString() =>
            $"variables names={Names} infos={Infos} values={Values} bounds={Bounds}   " +
            $"events names={EventNames} infos={EventInfos}";
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
            Bounds = data != null && data.StructLists.TryGetValue("variableBounds", out var vb) ? vb.Count : 0,
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

        xml = HkxTextEdit.ArrayRemoveAt(xml, ids[0], "wordVariableValues", index);
        return HkxTextEdit.ArrayInsertAt(xml, ids[0], "wordVariableValues", index,
            "                <hkobject>\n" +
            $"                    <hkparam name=\"value\">{encodedValue}</hkparam>\n" +
            "                </hkobject>");
    }

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
                $"                        <hkobject class=\"hkbRoleAttribute\" name=\"role\" signature=\"{HkxSignatures.Of("hkbRoleAttribute")}\">\n" +
                "                            <hkparam name=\"role\">ROLE_DEFAULT</hkparam>\n" +
                "                            <hkparam name=\"flags\">FLAG_NONE</hkparam>\n" +
                "                        </hkobject>\n" +
                "                    </hkparam>\n" +
                $"                    <hkparam name=\"type\">{TypeName(type)}</hkparam>\n" +
                "                </hkobject>");

        if (dataIds.Count > 0 && Audit(BehaviourGraphModel.Parse(xml)).Bounds == index)
            xml = HkxTextEdit.ArrayAppend(xml, dataIds[0], "variableBounds", BoundsElement());

        var valueIds = HkxTextEdit.IdsOfClass(xml, "hkbVariableValueSet");
        if (valueIds.Count > 0)
            xml = HkxTextEdit.ArrayAppend(xml, valueIds[0], "wordVariableValues",
                "                <hkobject>\n" +
                "                    <hkparam name=\"value\">0</hkparam>\n" +
                "                </hkobject>");

        return xml;
    }

    public static List<(string Min, string Max)> VariableBounds(BehaviourGraphModel model)
    {
        var data = model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraphData");
        var bounds = new List<(string, string)>();
        if (data == null || !data.StructLists.TryGetValue("variableBounds", out var rows)) return bounds;

        foreach (var row in rows)
            bounds.Add((row.TryGetValue("min", out var lo) ? lo : "",
                        row.TryGetValue("max", out var hi) ? hi : ""));

        return bounds;
    }

    public static string SetVariableBounds(string xml, int index, string encodedMin, string encodedMax)
    {
        var dataIds = HkxTextEdit.IdsOfClass(xml, "hkbBehaviorGraphData");
        if (dataIds.Count == 0)
            throw new InvalidOperationException("this file has no hkbBehaviorGraphData");

        int variables = VariableNames(BehaviourGraphModel.Parse(xml)).Count;
        if (index < 0 || index >= variables)
            throw new ArgumentOutOfRangeException(nameof(index),
                $"this file declares {variables} variable(s), so there is no variable {index} to bound");

        int have = Audit(BehaviourGraphModel.Parse(xml)).Bounds;
        for (int i = have; i <= index; i++)
            xml = HkxTextEdit.ArrayAppend(xml, dataIds[0], "variableBounds", BoundsElement());

        xml = HkxTextEdit.ArrayRemoveAt(xml, dataIds[0], "variableBounds", index);
        return HkxTextEdit.ArrayInsertAt(xml, dataIds[0], "variableBounds", index,
                                         BoundsElement(encodedMin, encodedMax));
    }

    private static string BoundsElement() => BoundsElement("0", "0");

    private static string BoundsElement(string min, string max) =>
        "                <hkobject>\n" +
        BoundsMember("min", min) +
        BoundsMember("max", max) +
        "                </hkobject>";

    private static string BoundsMember(string name, string value) =>
        $"                    <hkparam name=\"{name}\">\n" +
        $"                        <hkobject class=\"hkbVariableValue\" name=\"{name}\" signature=\"{HkxSignatures.Of("hkbVariableValue")}\">\n" +
        $"                            <hkparam name=\"value\">{value}</hkparam>\n" +
        "                        </hkobject>\n" +
        "                    </hkparam>\n";

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

    public static string RemoveVariable(string xml, int index, bool force, out List<string> blockers) =>
        Remove(xml, variable: true, index, force, out blockers);

    public static string RemoveEvent(string xml, int index, bool force, out List<string> blockers) =>
        Remove(xml, variable: false, index, force, out blockers);

    private static string Remove(string xml, bool variable, int index, bool force, out List<string> blockers)
    {
        var model = BehaviourGraphModel.Parse(xml);
        var names = variable ? VariableNames(model) : EventNames(model);
        if (index < 0 || index >= names.Count) throw new ArgumentOutOfRangeException(nameof(index));

        blockers = SymbolIndexFixup.ReferencesTo(xml, events: !variable, index);
        if (blockers.Count > 0 && !force) return xml;

        var stringIds = HkxTextEdit.IdsOfClass(xml, "hkbBehaviorGraphStringData");
        if (stringIds.Count == 0) throw new InvalidOperationException("this file has no hkbBehaviorGraphStringData");
        xml = HkxTextEdit.ArrayRemoveAt(xml, stringIds[0], variable ? "variableNames" : "eventNames", index);

        var dataIds = HkxTextEdit.IdsOfClass(xml, "hkbBehaviorGraphData");
        if (dataIds.Count > 0)
        {
            var before = Audit(BehaviourGraphModel.Parse(xml));
            xml = HkxTextEdit.ArrayRemoveAt(xml, dataIds[0], variable ? "variableInfos" : "eventInfos", index);

            if (variable && index < before.Bounds)
                xml = HkxTextEdit.ArrayRemoveAt(xml, dataIds[0], "variableBounds", index);
        }

        if (variable)
        {
            var valueIds = HkxTextEdit.IdsOfClass(xml, "hkbVariableValueSet");
            if (valueIds.Count > 0)
                xml = HkxTextEdit.ArrayRemoveAt(xml, valueIds[0], "wordVariableValues", index);
        }

        return SymbolIndexFixup.ShiftDown(xml, events: !variable, index, out _);
    }

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
