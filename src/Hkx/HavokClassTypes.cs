using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace OpenCommonwealth.Services.Hkx;

public sealed class HavokClassTypes
{
    public sealed class Member
    {
        public string Name { get; init; } = "";
        public int Offset { get; init; }

        public string VType { get; init; } = "";

        public string VSub { get; init; } = "";

        public string? CType { get; init; }

        public string? EType { get; init; }

        public int ArrSize { get; init; }

        public bool Written { get; init; } = true;

        public string? Default { get; init; }

        public override string ToString() => $"+{Offset} {Name} {VType}" + (Written ? "" : " (not written)");
    }

    public sealed class Layout
    {
        public string Name { get; init; } = "";
        public string? Parent { get; init; }

        public uint Signature { get; init; }

        public int? Size { get; init; }

        public int? Align { get; init; }

        public IReadOnlyList<Member> Declared { get; init; } = Array.Empty<Member>();
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> Enums { get; init; } =
            new Dictionary<string, IReadOnlyDictionary<string, long>>();
    }

    private readonly Dictionary<string, Layout> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Member>> _resolved = new(StringComparer.Ordinal);

    public static HavokClassTypes Shipped { get; } = LoadShipped();

    public int Count => _byName.Count;
    public IEnumerable<string> Names => _byName.Keys;
    public bool Knows(string className) => _byName.ContainsKey(className);

    public Layout? this[string className] =>
        _byName.TryGetValue(className, out var layout) ? layout : null;

    public IReadOnlyList<Member> Members(string className)
    {
        if (_resolved.TryGetValue(className, out var cached)) return cached;

        var chain = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? at = className; at != null && _byName.ContainsKey(at); at = _byName[at].Parent)
        {
            if (!seen.Add(at)) break;
            chain.Add(at);
        }

        chain.Reverse();
        var all = chain.SelectMany(c => _byName[c].Declared).ToList();
        _resolved[className] = all;
        return all;
    }

    public IReadOnlyDictionary<string, long>? Enum(string className, string enumName)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? at = className; at != null && _byName.ContainsKey(at); at = _byName[at].Parent)
        {
            if (!seen.Add(at)) break;
            if (_byName[at].Enums.TryGetValue(enumName, out var values)) return values;
        }
        return null;
    }

    public string? NameOf(string className, Member member, long value)
    {
        if (member.EType == null) return null;

        var values = Enum(className, member.EType);
        if (values == null) return null;

        foreach (var (name, declared) in values)
            if (declared == value) return name;

        if (member.VType != "TYPE_FLAGS") return null;

        var parts = new List<string>();
        for (int bit = 0; bit < 64; bit++)
        {
            long one = 1L << bit;
            if ((value & one) == 0) continue;

            string? part = values.FirstOrDefault(v => v.Value == one).Key;
            if (part == null) return null;
            parts.Add(part);
        }

        return parts.Count > 0 ? string.Join("|", parts) : null;
    }

    public IReadOnlyList<string> SignatureProblems(IEnumerable<(uint Signature, string Name)> declared)
    {
        var problems = new List<string>();

        if (_byName.Count == 0) return problems;

        foreach (var (signature, name) in declared.Distinct())
        {
            if (!_byName.TryGetValue(name, out var layout))
                problems.Add($"{name} is a class this build has no definition for");
            else if (layout.Signature != signature)
                problems.Add($"{name} is signed 0x{signature:x8} in the file " +
                             $"and 0x{layout.Signature:x8} here");
        }
        return problems;
    }

    public bool HasTrailingPadding(string className)
    {
        if (this[className]?.Size is not int size) return false;

        int end = 0;
        foreach (var m in Members(className))
        {
            int width = m.CType != null && m.VType == "TYPE_STRUCT" ? this[m.CType]?.Size ?? 4 : Width(m.VType);
            end = Math.Max(end, m.Offset + width * Math.Max(1, m.ArrSize));
        }

        return size != (end + 7) / 8 * 8 && size == (end + 15) / 16 * 16;
    }

    public static int Width(string vtype) => vtype switch
    {
        "TYPE_BOOL" or "TYPE_CHAR" or "TYPE_INT8" or "TYPE_UINT8" => 1,
        "TYPE_INT16" or "TYPE_UINT16" or "TYPE_HALF" => 2,
        "TYPE_INT64" or "TYPE_UINT64" or "TYPE_ULONG" or "TYPE_POINTER"
            or "TYPE_STRINGPTR" or "TYPE_CSTRING" or "TYPE_RELARRAY" => 8,
        "TYPE_VECTOR4" or "TYPE_QUATERNION" or "TYPE_ARRAY" or "TYPE_SIMPLEARRAY"
            or "TYPE_VARIANT" => 16,
        "TYPE_QSTRANSFORM" or "TYPE_MATRIX3" or "TYPE_ROTATION" => 48,
        "TYPE_TRANSFORM" or "TYPE_MATRIX4" => 64,
        _ => 4,
    };

    public static HavokClassTypes Parse(Stream json)
    {
        using var document = JsonDocument.Parse(json);
        var types = new HavokClassTypes();

        foreach (var entry in document.RootElement.GetProperty("classes").EnumerateObject())
        {
            var members = new List<Member>();
            foreach (var m in entry.Value.GetProperty("members").EnumerateArray())
            {
                members.Add(new Member
                {
                    Name = Text(m, "name") ?? "",
                    Offset = m.GetProperty("offset").GetInt32(),
                    VType = Text(m, "vtype") ?? "",
                    VSub = Text(m, "vsub") ?? "",
                    CType = Text(m, "ctype"),
                    EType = Text(m, "etype"),
                    ArrSize = m.TryGetProperty("arrsize", out var a) ? a.GetInt32() : 0,
                    Written = !m.TryGetProperty("written", out var w) || w.GetBoolean(),
                    Default = Text(m, "default"),
                });
            }

            var enums = new Dictionary<string, IReadOnlyDictionary<string, long>>(StringComparer.Ordinal);
            if (entry.Value.TryGetProperty("enums", out var declared))
                foreach (var e in declared.EnumerateObject())
                    enums[e.Name] = e.Value.EnumerateObject()
                                           .ToDictionary(v => v.Name, v => v.Value.GetInt64(),
                                                         StringComparer.Ordinal);

            types._byName[entry.Name] = new Layout
            {
                Name = entry.Name,
                Parent = Text(entry.Value, "parent"),
                Signature = uint.Parse(Text(entry.Value, "signature")!.Replace("0x", ""),
                                       System.Globalization.NumberStyles.HexNumber),
                Size = entry.Value.TryGetProperty("size", out var s) && s.ValueKind != JsonValueKind.Null
                    ? s.GetInt32()
                    : null,
                Align = entry.Value.TryGetProperty("align", out var al) && al.ValueKind != JsonValueKind.Null
                    ? al.GetInt32()
                    : null,
                Declared = members,
                Enums = enums,
            };
        }

        return types;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static HavokClassTypes LoadShipped()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string? resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("HavokClassTypes.json", StringComparison.Ordinal));

        if (resource != null)
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            return Parse(stream);
        }

        string beside = Path.Combine(AppContext.BaseDirectory, "HavokClassTypes.json");
        if (File.Exists(beside)) using (var stream = File.OpenRead(beside)) return Parse(stream);

        return new HavokClassTypes();
    }
}
