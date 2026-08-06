using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace OpenCommonwealth.Services.Hkx;

// What a class is made of, beyond where its fields sit.
//
// `HavokClasses` says a field is at offset 144 and holds a string. That is enough to read a value
// and not enough to know the file has a value there at all: it does not say which of a class's
// fields the engine ever writes out, and it does not say what class a struct written inline is an
// instance of. Both are needed to know what a file contains without asking hkxpack.
//
// This is that missing half, and it is not ours: it comes from the class database hkxpack ships
// inside its own jar, 908 classes with the type of every member, the class of every inline struct,
// which members are never serialised, and every enum's values. Read out of the jar as a zip, so
// nothing here runs Java.
//
// The two halves check each other rather than being taken on trust. Where both know a field they
// agree on its offset, 3,894 times with no disagreement, and every class name in every packfile on
// hand carries a signature that matches the one declared here, 27,759 of them.
public sealed class HavokClassTypes
{
    public sealed class Member
    {
        public string Name { get; init; } = "";
        public int Offset { get; init; }

        /// Havok's own name for the shape of the field: TYPE_REAL, TYPE_STRUCT, TYPE_ARRAY.
        public string VType { get; init; } = "";

        /// What an array holds, when the field is one.
        public string VSub { get; init; } = "";

        /// The class an inline struct, or an array's elements, are instances of. This is the fact
        /// nothing else has: a class dump read out of the game records the word "struct" and stops.
        public string? CType { get; init; }

        /// Which of the owning class's enums gives this field's value names.
        public string? EType { get; init; }

        /// A fixed length C array, `hkReal[8]`. Written out as eight fields named enabled1 to
        /// enabled8 rather than as one array, which is why the count has to be known.
        public int ArrSize { get; init; }

        /// Whether the engine writes the field out at all. A class holds running state and padding
        /// beside the fields that are really in the file; offering those for editing would put
        /// values in a file that vanilla does not have.
        public bool Written { get; init; } = true;

        public string? Default { get; init; }

        public override string ToString() => $"+{Offset} {Name} {VType}" + (Written ? "" : " (not written)");
    }

    public sealed class Layout
    {
        public string Name { get; init; } = "";
        public string? Parent { get; init; }

        /// The four bytes every packfile stores in front of the class's name. A file whose signature
        /// does not match this one was written against a different definition of the class, and
        /// reading it with this one would be quiet nonsense rather than an error.
        public uint Signature { get; init; }

        /// How big an instance is, which decides the stride of an array of them. hkxpack's data does
        /// not carry it; the dump read out of the game does, so the two are merged.
        public int? Size { get; init; }

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

    /// Every member of an object of this class, inherited ones first and each class's own in the
    /// order it declares them.
    ///
    /// Declaration order, not offset order, and the difference matters: this is the order the file
    /// is written in, so it is the order a field list has to come back in. Sorting by offset would
    /// be tidier and would not match anything.
    public IReadOnlyList<Member> Members(string className)
    {
        if (_resolved.TryGetValue(className, out var cached)) return cached;

        var chain = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? at = className; at != null && _byName.ContainsKey(at); at = _byName[at].Parent)
        {
            if (!seen.Add(at)) break;              // a cycle in the parent chain would hang here
            chain.Add(at);
        }

        chain.Reverse();
        var all = chain.SelectMany(c => _byName[c].Declared).ToList();
        _resolved[className] = all;
        return all;
    }

    /// The values of one of a class's enums, looked for up the parent chain because a class can use
    /// an enum its parent declares.
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

    /// The name for a member's value, or null when the member has no enum or the value is not one of
    /// its declared ones. A value outside the declaration is left unnamed rather than guessed at.
    public string? NameOf(string className, Member member, long value)
    {
        if (member.EType == null) return null;

        var values = Enum(className, member.EType);
        if (values == null) return null;

        foreach (var (name, declared) in values)
            if (declared == value) return name;

        // Flags combine. A combination is only as good as its parts: a bit with no name of its own
        // makes the whole answer a partial reading dressed up as a complete one.
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

    /// Classes a file names whose definition is not the one we hold, or is not one we hold at all.
    /// Empty is the answer on every file to hand; anything else means the file was written against
    /// a different Havok than this data describes and nothing read out of it can be trusted.
    public IReadOnlyList<string> SignatureProblems(IEnumerable<(uint Signature, string Name)> declared)
    {
        var problems = new List<string>();

        // No table, no opinion. A build without the data reads files the way it did before this
        // existed, and reporting every class in every file as unknown would turn a missing data
        // file into a tool that refuses to open anything.
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

    /// Whether an instance of this class is bigger than the end of its last member, by more than
    /// rounding up to eight would explain.
    ///
    /// This is the shape of a real disagreement with hkxpack rather than a curiosity. A struct
    /// holding a vector or a transform is aligned to sixteen, so the compiler pads it out and the
    /// game's own class registration records the padded size. hkxpack has no size in its data and
    /// works one out by rounding the end of the last member up to eight, which is right until a
    /// class is sixteen aligned. Every element after the first in an array of one of those is then
    /// read from the wrong place — by hkxpack, not by us, because we take the size from the game.
    public bool PaddedBeyondHkxPack(string className)
    {
        if (this[className]?.Size is not int size) return false;

        int end = 0;
        foreach (var m in Members(className))
        {
            int width = m.CType != null && m.VType == "TYPE_STRUCT" ? this[m.CType]?.Size ?? 4 : Width(m.VType);
            end = Math.Max(end, m.Offset + width * Math.Max(1, m.ArrSize));
        }

        // Both halves are needed. Rounding to eight has to be wrong, *and* rounding to sixteen has
        // to be right: that pair is what says the class is sixteen aligned and hkxpack stopped at
        // eight. Without the second half this catches every small class as well — `hkbVariableInfo`
        // is six bytes, which is neither, and hkxpack strides it perfectly well — and a check that
        // excuses a disagreement in one of those is worse than no check at all.
        return size != (end + 7) / 8 * 8 && size == (end + 15) / 16 * 16;
    }

    private static int Width(string vtype) => vtype switch
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

        // Empty rather than fatal, so a build without the data still opens files the way it did
        // before this existed. Anything that needs it asks whether it knows a class first.
        return new HavokClassTypes();
    }
}
