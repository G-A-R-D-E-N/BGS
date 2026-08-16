using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public abstract record HavokIntermediateValue
{
    public sealed record NullValue : HavokIntermediateValue;
    public sealed record BoolValue(bool Value) : HavokIntermediateValue;
    public sealed record IntegerValue(long Value) : HavokIntermediateValue;
    public sealed record RealValue(double Value) : HavokIntermediateValue;
    public sealed record StringValue(string Value) : HavokIntermediateValue;
    public sealed record ReferenceValue(long? TargetId) : HavokIntermediateValue;
    public sealed record ArrayValue(IReadOnlyList<HavokIntermediateValue> Values) : HavokIntermediateValue;
    public sealed record StructValue(IReadOnlyDictionary<string, HavokIntermediateValue> Members) : HavokIntermediateValue;

    public static readonly NullValue Null = new();
}

public sealed class HavokIntermediateObject
{
    public HavokIntermediateObject(long id, string typeName)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (string.IsNullOrWhiteSpace(typeName)) throw new ArgumentException("type name is required", nameof(typeName));
        Id = id;
        TypeName = typeName;
    }

    public long Id { get; }
    public string TypeName { get; set; }
    public Dictionary<string, HavokIntermediateValue> Members { get; } = new(StringComparer.Ordinal);
}

public sealed class HavokIntermediateDocument
{
    private readonly Dictionary<long, HavokIntermediateObject> _objects = new();

    public long? RootId { get; set; }
    public IReadOnlyDictionary<long, HavokIntermediateObject> Objects => _objects;

    public HavokIntermediateObject Add(long id, string typeName)
    {
        if (_objects.ContainsKey(id)) throw new InvalidOperationException($"object {id} already exists");
        var value = new HavokIntermediateObject(id, typeName);
        _objects.Add(id, value);
        return value;
    }

    public HavokIntermediateObject? Get(long id) => _objects.GetValueOrDefault(id);
}

public sealed record HavokMemberDefinition(string Name, string ValueType, string? TargetType = null);

public sealed record HavokTypeDefinition(
    string Name,
    int? Size,
    IReadOnlyList<HavokMemberDefinition> Members);

public sealed class HavokTypeRegistry
{
    private readonly Dictionary<string, HavokTypeDefinition> _types = new(StringComparer.Ordinal);

    public int Count => _types.Count;
    public IEnumerable<string> Names => _types.Keys;

    public void Register(HavokTypeDefinition type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (string.IsNullOrWhiteSpace(type.Name)) throw new ArgumentException("type name is required", nameof(type));
        if (!_types.TryAdd(type.Name, type))
            throw new InvalidOperationException($"Havok type {type.Name} is already registered");
    }

    public bool TryGet(string name, out HavokTypeDefinition? type) => _types.TryGetValue(name, out type);
}

public enum HavokConversionDiagnosticLevel
{
    Info,
    Warning,
    Error,
}

public sealed record HavokConversionDiagnostic(
    HavokConversionDiagnosticLevel Level,
    long ObjectId,
    string Member,
    string Message);

public sealed class HavokConversionReport
{
    public int ExactObjects { get; internal set; }
    public int PatchedObjects { get; internal set; }
    public int DefaultedFields { get; internal set; }
    public int DroppedFields { get; internal set; }
    public int DroppedReferences { get; internal set; }
    public int EnumMappedFields { get; internal set; }
    public int UnsupportedEnumValues { get; internal set; }
    public int UnsupportedObjects { get; internal set; }

    public int ConvertedObjects => ExactObjects + PatchedObjects;
}

public sealed class HavokConversionMap
{
    public sealed class TypeRule
    {
        internal TypeRule(string sourceType, string targetType)
        {
            SourceType = sourceType;
            TargetType = targetType;
        }

        public string SourceType { get; }
        public string TargetType { get; }
        internal Dictionary<string, string> RenamedMembers { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> DroppedMembers { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, HavokIntermediateValue> Defaults { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, IReadOnlyDictionary<long, long>> EnumMappings { get; } = new(StringComparer.Ordinal);
        internal Action<HavokConversionContext, HavokIntermediateObject, HavokIntermediateObject>? Special { get; private set; }

        public TypeRule Rename(string sourceMember, string targetMember)
        {
            RenamedMembers[sourceMember] = targetMember;
            return this;
        }

        public TypeRule Drop(params string[] members)
        {
            foreach (string member in members) DroppedMembers.Add(member);
            return this;
        }

        public TypeRule Default(string targetMember, HavokIntermediateValue value)
        {
            Defaults[targetMember] = value;
            return this;
        }

        public TypeRule MapEnum(string sourceMember, params (long SourceValue, long TargetValue)[] values)
        {
            if (string.IsNullOrWhiteSpace(sourceMember))
                throw new ArgumentException("source member is required", nameof(sourceMember));
            if (values == null || values.Length == 0)
                throw new ArgumentException("at least one enum mapping is required", nameof(values));

            var mapping = new Dictionary<long, long>();
            foreach (var (sourceValue, targetValue) in values)
                if (!mapping.TryAdd(sourceValue, targetValue))
                    throw new ArgumentException($"enum value {sourceValue} is mapped more than once", nameof(values));

            EnumMappings[sourceMember] = mapping;
            return this;
        }

        public TypeRule ConvertWith(Action<HavokConversionContext, HavokIntermediateObject, HavokIntermediateObject> converter)
        {
            Special = converter ?? throw new ArgumentNullException(nameof(converter));
            return this;
        }
    }

    private readonly Dictionary<string, TypeRule> _rules = new(StringComparer.Ordinal);
    private readonly HashSet<string> _identityTypes = new(StringComparer.Ordinal);

    public TypeRule Map(string sourceType, string targetType)
    {
        if (string.IsNullOrWhiteSpace(sourceType)) throw new ArgumentException("source type is required", nameof(sourceType));
        if (string.IsNullOrWhiteSpace(targetType)) throw new ArgumentException("target type is required", nameof(targetType));
        var rule = new TypeRule(sourceType, targetType);
        _rules[sourceType] = rule;
        return rule;
    }

    public HavokConversionMap AllowIdentity(params string[] typeNames)
    {
        foreach (string typeName in typeNames)
            if (!string.IsNullOrWhiteSpace(typeName)) _identityTypes.Add(typeName);
        return this;
    }

    internal TypeRule? Resolve(string sourceType)
    {
        if (_rules.TryGetValue(sourceType, out var rule)) return rule;
        return _identityTypes.Contains(sourceType) ? new TypeRule(sourceType, sourceType) : null;
    }
}

public sealed class HavokConversionContext
{
    internal HavokConversionContext(HavokIntermediateDocument source, HavokIntermediateDocument target,
                                    HavokConversionReport report, List<HavokConversionDiagnostic> diagnostics)
    {
        Source = source;
        Target = target;
        Report = report;
        Diagnostics = diagnostics;
    }

    public HavokIntermediateDocument Source { get; }
    public HavokIntermediateDocument Target { get; }
    public HavokConversionReport Report { get; }
    public IList<HavokConversionDiagnostic> Diagnostics { get; }
}

public sealed record HavokConversionResult(
    HavokIntermediateDocument Document,
    HavokConversionReport Report,
    IReadOnlyList<HavokConversionDiagnostic> Diagnostics);

public static class HavokSemanticConverter
{
    public static HavokConversionResult Convert(HavokIntermediateDocument source, HavokConversionMap map) =>
        Convert(source, map, null);

    /// <param name="targetTypes">
    /// When supplied, the converted document is validated against this target schema: every
    /// mapped target type and every written target member must be declared, otherwise an error
    /// diagnostic is raised. When null, no schema validation is performed (the value-only path).
    /// </param>
    public static HavokConversionResult Convert(
        HavokIntermediateDocument source, HavokConversionMap map, HavokTypeRegistry? targetTypes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);

        var target = new HavokIntermediateDocument();
        var report = new HavokConversionReport();
        var diagnostics = new List<HavokConversionDiagnostic>();
        var rules = new Dictionary<long, HavokConversionMap.TypeRule>();

        foreach (var sourceObject in source.Objects.Values.OrderBy(o => o.Id))
        {
            var rule = map.Resolve(sourceObject.TypeName);
            if (rule == null)
            {
                report.UnsupportedObjects++;
                diagnostics.Add(new HavokConversionDiagnostic(
                    HavokConversionDiagnosticLevel.Error,
                    sourceObject.Id,
                    "",
                    $"no conversion rule exists for {sourceObject.TypeName}"));
                continue;
            }

            target.Add(sourceObject.Id, rule.TargetType);
            rules[sourceObject.Id] = rule;
        }

        var context = new HavokConversionContext(source, target, report, diagnostics);
        foreach (var sourceObject in source.Objects.Values.OrderBy(o => o.Id))
        {
            if (!rules.TryGetValue(sourceObject.Id, out var rule)) continue;
            var targetObject = target.Get(sourceObject.Id)!;
            bool patched = sourceObject.TypeName != rule.TargetType;
            int droppedReferencesBefore = report.DroppedReferences;

            foreach (var (sourceMember, sourceValue) in sourceObject.Members)
            {
                if (rule.DroppedMembers.Contains(sourceMember))
                {
                    report.DroppedFields++;
                    patched = true;
                    diagnostics.Add(new HavokConversionDiagnostic(
                        HavokConversionDiagnosticLevel.Warning,
                        sourceObject.Id,
                        sourceMember,
                        "field was explicitly dropped by the conversion rule"));
                    continue;
                }

                string targetMember = rule.RenamedMembers.GetValueOrDefault(sourceMember, sourceMember);
                if (!string.Equals(sourceMember, targetMember, StringComparison.Ordinal))
                    patched = true;

                var convertedValue = CloneValue(
                    sourceValue, sourceObject.Id, sourceMember, target, report, diagnostics);
                if (rule.EnumMappings.TryGetValue(sourceMember, out var enumMapping))
                {
                    convertedValue = ConvertEnum(
                        sourceValue, enumMapping, sourceObject.Id, sourceMember, report, diagnostics, out bool enumPatched);
                    patched |= enumPatched;
                }
                targetObject.Members[targetMember] = convertedValue;
            }

            foreach (var (member, value) in rule.Defaults)
            {
                if (targetObject.Members.ContainsKey(member)) continue;
                targetObject.Members[member] = CloneValue(value, sourceObject.Id, member, target, report, diagnostics);
                report.DefaultedFields++;
                patched = true;
            }

            if (report.DroppedReferences != droppedReferencesBefore)
                patched = true;

            if (rule.Special != null)
            {
                string beforeType = targetObject.TypeName;
                var before = targetObject.Members.ToDictionary(
                    pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                rule.Special.Invoke(context, sourceObject, targetObject);
                if (!string.Equals(beforeType, targetObject.TypeName, StringComparison.Ordinal) ||
                    !MembersEqual(before, targetObject.Members))
                    patched = true;
            }

            if (patched) report.PatchedObjects++;
            else report.ExactObjects++;
        }

        // Validate the finished document as a final pass rather than while copying fields. This
        // runs after any ConvertWith callback, so a special converter cannot change the object
        // type or introduce undeclared/mistyped members without being caught.
        if (targetTypes != null)
            ValidateAgainstSchema(target, targetTypes, diagnostics);

        if (source.RootId is long root)
        {
            if (target.Get(root) != null) target.RootId = root;
            else diagnostics.Add(new HavokConversionDiagnostic(
                HavokConversionDiagnosticLevel.Error,
                root,
                "",
                "the source root object is unsupported, so the converted document has no root"));
        }

        return new HavokConversionResult(target, report, diagnostics);
    }

    private static void ValidateAgainstSchema(HavokIntermediateDocument target, HavokTypeRegistry registry,
                                              List<HavokConversionDiagnostic> diagnostics)
    {
        foreach (var obj in target.Objects.Values)
        {
            if (!registry.TryGet(obj.TypeName, out var definition) || definition == null)
            {
                diagnostics.Add(new HavokConversionDiagnostic(
                    HavokConversionDiagnosticLevel.Error, obj.Id, "",
                    $"target type {obj.TypeName} is not declared in the target schema"));
                continue;
            }

            var declared = definition.Members.ToDictionary(m => m.Name, m => m, StringComparer.Ordinal);
            foreach (var (name, value) in obj.Members)
            {
                if (!declared.TryGetValue(name, out var member))
                {
                    diagnostics.Add(new HavokConversionDiagnostic(
                        HavokConversionDiagnosticLevel.Error, obj.Id, name,
                        $"target type {obj.TypeName} does not declare member {name}"));
                    continue;
                }
                ValidateValue(obj.Id, name, value, member, target, registry, diagnostics);
            }
        }
    }

    private static void ValidateValue(long objId, string path, HavokIntermediateValue value,
                                      HavokMemberDefinition member, HavokIntermediateDocument target,
                                      HavokTypeRegistry registry, List<HavokConversionDiagnostic> diagnostics)
    {
        // A null value stands in for "unset" and is accepted for any member.
        if (value is HavokIntermediateValue.NullValue) return;

        if (!KindMatches(member.ValueType, value))
        {
            diagnostics.Add(new HavokConversionDiagnostic(
                HavokConversionDiagnosticLevel.Error, objId, path,
                $"member {path} holds a {KindName(value)} but target type declares {member.ValueType}"));
            return;
        }

        // A reference must point at an object of the declared target class.
        if (value is HavokIntermediateValue.ReferenceValue reference && reference.TargetId is long id &&
            member.TargetType != null && target.Get(id) is { } referenced &&
            !string.Equals(referenced.TypeName, member.TargetType, StringComparison.Ordinal))
            diagnostics.Add(new HavokConversionDiagnostic(
                HavokConversionDiagnosticLevel.Error, objId, path,
                $"member {path} references a {referenced.TypeName} but target type declares {member.TargetType}"));

        // Recurse into a struct member whose element type is itself declared.
        if (value is HavokIntermediateValue.StructValue structure && member.TargetType != null &&
            registry.TryGet(member.TargetType, out var structDef) && structDef != null)
        {
            var declared = structDef.Members.ToDictionary(m => m.Name, m => m, StringComparer.Ordinal);
            foreach (var (name, sub) in structure.Members)
            {
                if (!declared.TryGetValue(name, out var subMember))
                    diagnostics.Add(new HavokConversionDiagnostic(
                        HavokConversionDiagnosticLevel.Error, objId, path + "." + name,
                        $"struct type {member.TargetType} does not declare member {name}"));
                else
                    ValidateValue(objId, path + "." + name, sub, subMember, target, registry, diagnostics);
            }
        }

        // Recurse into array elements when the element type is a declared struct.
        if (value is HavokIntermediateValue.ArrayValue array && member.TargetType != null &&
            registry.TryGet(member.TargetType, out _))
            for (int i = 0; i < array.Values.Count; i++)
                if (array.Values[i] is HavokIntermediateValue.StructValue)
                    ValidateValue(objId, $"{path}[{i}]", array.Values[i],
                        new HavokMemberDefinition(member.Name, "TYPE_STRUCT", member.TargetType),
                        target, registry, diagnostics);
    }

    private static bool KindMatches(string valueType, HavokIntermediateValue value) => valueType switch
    {
        "TYPE_BOOL" => value is HavokIntermediateValue.BoolValue,
        "TYPE_INT8" or "TYPE_UINT8" or "TYPE_INT16" or "TYPE_UINT16" or "TYPE_INT32" or "TYPE_UINT32"
            or "TYPE_INT64" or "TYPE_UINT64" or "TYPE_ULONG" or "TYPE_CHAR" or "TYPE_ENUM" or "TYPE_FLAGS"
            => value is HavokIntermediateValue.IntegerValue,
        "TYPE_REAL" or "TYPE_HALF" => value is HavokIntermediateValue.RealValue,
        "TYPE_STRING" or "TYPE_STRINGPTR" or "TYPE_CSTRING" => value is HavokIntermediateValue.StringValue,
        "TYPE_POINTER" => value is HavokIntermediateValue.ReferenceValue,
        "TYPE_STRUCT" => value is HavokIntermediateValue.StructValue,
        "TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY" => value is HavokIntermediateValue.ArrayValue,
        "TYPE_VARIANT" => value is HavokIntermediateValue.ReferenceValue or HavokIntermediateValue.StructValue,
        // An unrecognised declared type cannot be judged, so do not raise a false mismatch.
        _ => true,
    };

    private static string KindName(HavokIntermediateValue value) => value switch
    {
        HavokIntermediateValue.BoolValue => "boolean",
        HavokIntermediateValue.IntegerValue => "integer",
        HavokIntermediateValue.RealValue => "real",
        HavokIntermediateValue.StringValue => "string",
        HavokIntermediateValue.ReferenceValue => "reference",
        HavokIntermediateValue.ArrayValue => "array",
        HavokIntermediateValue.StructValue => "struct",
        _ => "value",
    };

    private static HavokIntermediateValue ConvertEnum(
        HavokIntermediateValue sourceValue,
        IReadOnlyDictionary<long, long> mapping,
        long objectId,
        string member,
        HavokConversionReport report,
        List<HavokConversionDiagnostic> diagnostics,
        out bool patched)
    {
        patched = false;
        if (sourceValue is not HavokIntermediateValue.IntegerValue integer)
        {
            report.UnsupportedEnumValues++;
            patched = true;
            diagnostics.Add(new HavokConversionDiagnostic(
                HavokConversionDiagnosticLevel.Error,
                objectId,
                member,
                "enum mapping expected an integer intermediate value"));
            return HavokIntermediateValue.Null;
        }

        if (!mapping.TryGetValue(integer.Value, out long targetValue))
        {
            report.UnsupportedEnumValues++;
            patched = true;
            diagnostics.Add(new HavokConversionDiagnostic(
                HavokConversionDiagnosticLevel.Error,
                objectId,
                member,
                $"enum value {integer.Value} has no target mapping"));
            return HavokIntermediateValue.Null;
        }

        report.EnumMappedFields++;
        patched = targetValue != integer.Value;
        return new HavokIntermediateValue.IntegerValue(targetValue);
    }

    private static bool MembersEqual(
        IReadOnlyDictionary<string, HavokIntermediateValue> left,
        IReadOnlyDictionary<string, HavokIntermediateValue> right)
    {
        if (left.Count != right.Count) return false;
        foreach (var (name, value) in left)
        {
            if (!right.TryGetValue(name, out var other) || !ValuesEqual(value, other))
                return false;
        }
        return true;
    }

    private static bool ValuesEqual(HavokIntermediateValue left, HavokIntermediateValue right)
    {
        if (ReferenceEquals(left, right)) return true;
        return (left, right) switch
        {
            (HavokIntermediateValue.NullValue, HavokIntermediateValue.NullValue) => true,
            (HavokIntermediateValue.BoolValue a, HavokIntermediateValue.BoolValue b) => a.Value == b.Value,
            (HavokIntermediateValue.IntegerValue a, HavokIntermediateValue.IntegerValue b) => a.Value == b.Value,
            (HavokIntermediateValue.RealValue a, HavokIntermediateValue.RealValue b) => a.Value.Equals(b.Value),
            (HavokIntermediateValue.StringValue a, HavokIntermediateValue.StringValue b) =>
                string.Equals(a.Value, b.Value, StringComparison.Ordinal),
            (HavokIntermediateValue.ReferenceValue a, HavokIntermediateValue.ReferenceValue b) => a.TargetId == b.TargetId,
            (HavokIntermediateValue.ArrayValue a, HavokIntermediateValue.ArrayValue b) =>
                a.Values.Count == b.Values.Count && a.Values.Zip(b.Values, ValuesEqual).All(equal => equal),
            (HavokIntermediateValue.StructValue a, HavokIntermediateValue.StructValue b) =>
                MembersEqual(a.Members, b.Members),
            _ => false,
        };
    }

    private static HavokIntermediateValue CloneValue(
        HavokIntermediateValue value,
        long ownerId,
        string member,
        HavokIntermediateDocument target,
        HavokConversionReport report,
        List<HavokConversionDiagnostic> diagnostics)
    {
        switch (value)
        {
            case HavokIntermediateValue.ReferenceValue reference:
                if (reference.TargetId is not long targetId) return new HavokIntermediateValue.ReferenceValue(null);
                if (target.Get(targetId) != null) return new HavokIntermediateValue.ReferenceValue(targetId);

                report.DroppedReferences++;
                diagnostics.Add(new HavokConversionDiagnostic(
                    HavokConversionDiagnosticLevel.Error,
                    ownerId,
                    member,
                    $"reference to unsupported object {targetId} was replaced with null"));
                return new HavokIntermediateValue.ReferenceValue(null);

            case HavokIntermediateValue.ArrayValue array:
                return new HavokIntermediateValue.ArrayValue(
                    array.Values.Select(item => CloneValue(item, ownerId, member, target, report, diagnostics)).ToList());

            case HavokIntermediateValue.StructValue structure:
                return new HavokIntermediateValue.StructValue(
                    structure.Members.ToDictionary(
                        pair => pair.Key,
                        pair => CloneValue(pair.Value, ownerId, member + "." + pair.Key, target, report, diagnostics),
                        StringComparer.Ordinal));

            case HavokIntermediateValue.StringValue text:
                return new HavokIntermediateValue.StringValue(text.Value);
            case HavokIntermediateValue.BoolValue flag:
                return new HavokIntermediateValue.BoolValue(flag.Value);
            case HavokIntermediateValue.IntegerValue integer:
                return new HavokIntermediateValue.IntegerValue(integer.Value);
            case HavokIntermediateValue.RealValue real:
                return new HavokIntermediateValue.RealValue(real.Value);
            default:
                return HavokIntermediateValue.Null;
        }
    }
}