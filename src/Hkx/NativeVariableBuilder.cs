using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class NativeVariableBuilder
{
    public sealed record Entry(string Name, SymbolEditor.VariableType Type, string InitialValue = "0");
    public sealed record Created(Entry Entry, int Index);
    public sealed record Result(byte[] Bytes, IReadOnlyList<Created> Created,
                                IReadOnlyList<GraphValidator.Finding> Findings);

    public static Result Build(byte[] source, IEnumerable<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entries);

        var requested = entries.ToList();
        if (requested.Count == 0)
            throw new ArgumentException("at least one variable is required", nameof(entries));
        if (requested.Any(entry => string.IsNullOrWhiteSpace(entry.Name)))
            throw new ArgumentException("every variable needs a name", nameof(entries));

        var duplicate = requested.GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            throw new ArgumentException($"variable name '{duplicate.Key}' appears more than once", nameof(entries));

        var objects = new PackfileObjects(PackfileImage.Read(source), HavokClasses.Shipped);
        var model = NativeGraphModel.From(objects)
            ?? throw new InvalidOperationException("the source file cannot be represented by the native graph model");

        var strings = model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraphStringData")
            ?? throw new InvalidOperationException("this graph has no hkbBehaviorGraphStringData");
        var data = model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraphData")
            ?? throw new InvalidOperationException("this graph has no hkbBehaviorGraphData");
        var values = model.Objects.FirstOrDefault(o => o.Class == "hkbVariableValueSet")
            ?? throw new InvalidOperationException("this graph has no hkbVariableValueSet");

        var audit = SymbolEditor.Audit(model);
        if (!audit.VariablesConsistent)
            throw new InvalidOperationException(
                "the source variable arrays are not aligned: " + audit);

        var names = SymbolEditor.VariableNames(model).ToList();
        var existing = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in requested)
            if (!existing.Add(entry.Name))
                throw new ArgumentException($"variable '{entry.Name}' already exists", nameof(entries));

        int stringsId = ParseId(strings.Id);
        int dataId = ParseId(data.Id);
        int valuesId = ParseId(values.Id);
        bool parallelBounds = audit.Bounds == audit.Names;

        var plan = new NativeAuthoringPlan(source);
        var created = new List<Created>(requested.Count);
        foreach (var entry in requested)
        {
            int index = names.Count;
            names.Add(entry.Name);
            plan.SetTextArray(stringsId, "variableNames", names);

            plan.ResizeStructArray(dataId, "variableInfos", index + 1);
            SetStructEnum(plan, dataId, "variableInfos", index, "role.role", "ROLE_DEFAULT");
            SetStructEnum(plan, dataId, "variableInfos", index, "role.flags", "FLAG_NONE");
            SetStructEnum(plan, dataId, "variableInfos", index, "type", TypeName(entry.Type));

            plan.ResizeStructArray(valuesId, "wordVariableValues", index + 1);
            plan.SetStructMember(valuesId, "wordVariableValues", index, "value",
                                 SymbolEditor.EncodeValue(entry.Type, entry.InitialValue));

            if (parallelBounds)
            {
                plan.ResizeStructArray(dataId, "variableBounds", index + 1);
                plan.SetStructMember(dataId, "variableBounds", index, "min.value", "0");
                plan.SetStructMember(dataId, "variableBounds", index, "max.value", "0");
            }

            created.Add(new Created(entry, index));
        }

        var result = plan.Apply();
        return new Result(result.Bytes, created, result.Findings);
    }

    private static int ParseId(string id) =>
        int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new InvalidOperationException($"'{id}' is not a native object id");

    private static string TypeName(SymbolEditor.VariableType type) => type switch
    {
        SymbolEditor.VariableType.Int32 => "VARIABLE_TYPE_INT32",
        SymbolEditor.VariableType.Real => "VARIABLE_TYPE_REAL",
        SymbolEditor.VariableType.Bool => "VARIABLE_TYPE_BOOL",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static void SetStructEnum(NativeAuthoringPlan plan, int objectId, string field,
                                      int element, string memberPath, string enumName)
    {
        var types = HavokClassTypes.Shipped;
        string ownerClass = plan.ClassOf(objectId);
        var fieldMember = types.Members(ownerClass).FirstOrDefault(member => member.Name == field)
            ?? throw new InvalidOperationException($"{ownerClass}.{field} is not in the class metadata");
        string currentClass = fieldMember.CType
            ?? throw new InvalidOperationException($"{ownerClass}.{field} has no structured element class");

        string[] path = memberPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (path.Length == 0) throw new ArgumentException("member path is required", nameof(memberPath));

        HavokClassTypes.Member? final = null;
        string enumOwner = currentClass;
        for (int i = 0; i < path.Length; i++)
        {
            final = types.Members(currentClass).FirstOrDefault(member => member.Name == path[i])
                ?? throw new InvalidOperationException($"{currentClass}.{path[i]} is not in the class metadata");
            enumOwner = currentClass;

            if (i == path.Length - 1) break;
            if (final.VType != "TYPE_STRUCT" || final.CType == null)
                throw new InvalidOperationException($"{currentClass}.{path[i]} is not an inline struct");
            currentClass = final.CType;
        }

        if (final == null || final.VType is not ("TYPE_ENUM" or "TYPE_FLAGS") || final.EType == null)
            throw new InvalidOperationException($"{enumOwner}.{path[^1]} is not an enum or flags field");

        var enumValues = types.Enum(enumOwner, final.EType)
            ?? throw new InvalidOperationException($"{enumOwner}.{path[^1]} has no enum table");
        if (!enumValues.TryGetValue(enumName, out long value))
            throw new InvalidOperationException($"{enumName} is not a value of {enumOwner}.{path[^1]}");

        plan.SetStructMember(objectId, field, element, memberPath,
                             value.ToString(CultureInfo.InvariantCulture));
    }
}
