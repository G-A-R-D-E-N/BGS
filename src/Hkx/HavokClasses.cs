using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace OpenCommonwealth.Services.Hkx;

// Where every field of every Havok class lives inside an object, by byte offset.
//
// Read out of Fallout 4 itself rather than guessed or taken from an SDK. The game builds these
// descriptions at startup, one function per class, and each one names the class, its parent, how big
// an instance is, and where every field sits. Dumping those gave 935 classes; the data file beside
// this one is that dump, and the sweep is written up in the F4SE workspace under
// ReverseEngineering/03-FINDINGS.md.
//
// A class only declares the fields it adds, so anything inherited has to be walked for. Fields are
// laid out parent first, which is why an inherited offset needs no adjusting.
public sealed class HavokClasses
{
    public sealed class Member
    {
        public string Name { get; init; } = "";
        public int Offset { get; init; }
        public string Type { get; init; } = "";

        public override string ToString() => $"+{Offset} {Name} {Type}";
    }

    public sealed class Layout
    {
        public string Name { get; init; } = "";
        public string? Parent { get; init; }
        public int Size { get; init; }

        /// Only what this class adds. Use Members for the whole object.
        public IReadOnlyList<Member> Declared { get; init; } = Array.Empty<Member>();
    }

    private readonly Dictionary<string, Layout> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Member>> _resolved = new(StringComparer.Ordinal);

    public static HavokClasses Shipped { get; } = LoadShipped();

    public int Count => _byName.Count;
    public IEnumerable<string> Names => _byName.Keys;

    public Layout? this[string className] =>
        _byName.TryGetValue(className, out var layout) ? layout : null;

    /// Every field of an object of this class, inherited ones included, in offset order. Empty when
    /// the class is not one we have, which a caller must tell apart from a class with no fields.
    public IReadOnlyList<Member> Members(string className)
    {
        if (_resolved.TryGetValue(className, out var cached)) return cached;

        var all = new List<Member>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? at = className; at != null; )
        {
            if (!seen.Add(at)) break;             // a cycle in the parent chain would hang here
            if (!_byName.TryGetValue(at, out var layout)) break;
            all.AddRange(layout.Declared);
            at = layout.Parent;
        }

        all.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        _resolved[className] = all;
        return all;
    }

    public Member? Field(string className, string name) =>
        Members(className).FirstOrDefault(m => m.Name == name);

    public bool Knows(string className) => _byName.ContainsKey(className);

    private static HavokClasses LoadShipped()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string? resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("HavokClassLayouts.json", StringComparison.Ordinal));

        if (resource != null)
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            return Parse(stream);
        }

        // Running from a build that did not embed the data. Falling back to the file beside the
        // source keeps the command line tools working from a checkout; shipping without it would
        // otherwise fail as an empty class list, which reads as "no fields" rather than "no data".
        string beside = Path.Combine(AppContext.BaseDirectory, "HavokClassLayouts.json");
        if (File.Exists(beside)) using (var stream = File.OpenRead(beside)) return Parse(stream);

        throw new FileNotFoundException(
            "HavokClassLayouts.json is missing. It is the class field layouts read out of Fallout 4 " +
            "and nothing that writes object bytes can work without it.");
    }

    public static HavokClasses Parse(Stream json)
    {
        using var document = JsonDocument.Parse(json);
        var classes = new HavokClasses();

        foreach (var entry in document.RootElement.GetProperty("classes").EnumerateObject())
        {
            var members = new List<Member>();
            foreach (var m in entry.Value.GetProperty("members").EnumerateArray())
            {
                members.Add(new Member
                {
                    Name = m[0].GetString() ?? "",
                    Offset = m[1].GetInt32(),
                    Type = m[2].GetString() ?? "",
                });
            }

            classes._byName[entry.Name] = new Layout
            {
                Name = entry.Name,
                Parent = entry.Value.GetProperty("parent").ValueKind == JsonValueKind.Null
                    ? null
                    : entry.Value.GetProperty("parent").GetString(),
                Size = entry.Value.GetProperty("size").GetInt32(),
                Declared = members,
            };
        }

        return classes;
    }
}
