using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace OpenCommonwealth.Services.Hkx;











public sealed class HavokClasses
{
    public sealed class Member
    {
        public string Name { get; init; } = "";
        public int Offset { get; init; }
        public string Type { get; init; } = "";




        public string Owner { get; init; } = "";

        public override string ToString() => $"+{Offset} {Name} {Type}";
    }

    public sealed class Layout
    {
        public string Name { get; init; } = "";
        public string? Parent { get; init; }
        public int Size { get; init; }


        public IReadOnlyList<Member> Declared { get; init; } = Array.Empty<Member>();
    }

    private readonly Dictionary<string, Layout> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Member>> _resolved = new(StringComparer.Ordinal);

    public static HavokClasses Shipped { get; } = LoadShipped();

    public int Count => _byName.Count;
    public IEnumerable<string> Names => _byName.Keys;

    public Layout? this[string className] =>
        _byName.TryGetValue(className, out var layout) ? layout : null;



    public IReadOnlyList<Member> Members(string className)
    {
        if (_resolved.TryGetValue(className, out var cached)) return cached;

        var all = new List<Member>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? at = className; at != null; )
        {
            if (!seen.Add(at)) break;
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
                    Owner = entry.Name,
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
