using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace OpenCommonwealth.Services.Hkx;

// What the numbers in an enum or a flags field are called.
//
// The bytes hold 0 where hkxpack writes MODE_SINGLE_PLAY, so reading such a field without this
// gives a number nobody recognises. The class dump read out of Fallout 4 kept the fields and their
// types but not the value names, so these come from somewhere else: every enum field of every
// object in a set of vanilla files was read out of the bytes and set beside what hkxpack calls the
// same field, and the pairs that resulted are the table. `symrm names` rebuilds it.
//
// That makes it a record of what vanilla actually contains rather than a specification. A value no
// vanilla file uses has no name here, and is reported as unnamed rather than guessed at, because a
// plausible name invented for an unseen value is exactly the kind of wrong that goes unnoticed.
public sealed class HavokEnums
{
    /// Keyed by the class that declares the field and the field's name, because two classes can
    /// both declare `flags` and mean different things by it.
    private readonly Dictionary<string, Dictionary<long, string>> _names = new(StringComparer.Ordinal);

    /// Fields whose names are single bits that combine, written `A|B` rather than as one name.
    private readonly HashSet<string> _combining = new(StringComparer.Ordinal);

    public static HavokEnums Shipped { get; } = LoadShipped();

    public int Count => _names.Count;

    public static string Key(HavokClasses.Member member) => member.Owner + "." + member.Name;

    /// The name for a value, or null when this field has no table or the value is not in it. Null
    /// is the useful answer: it says the reading is incomplete rather than wrong.
    public string? Name(string key, long value)
    {
        if (!_names.TryGetValue(key, out var names)) return null;
        if (names.TryGetValue(value, out string? exact)) return exact;
        if (!_combining.Contains(key)) return null;

        // A combination is only as good as its parts: every bit set has to be one we have a name
        // for, or the answer would be a partial reading dressed up as a complete one.
        var parts = new List<string>();
        for (int bit = 0; bit < 64; bit++)
        {
            long one = 1L << bit;
            if ((value & one) == 0) continue;
            if (!names.TryGetValue(one, out string? part)) return null;
            parts.Add(part);
        }

        return parts.Count > 0 ? string.Join("|", parts) : null;
    }

    public static HavokEnums Parse(Stream json)
    {
        using var document = JsonDocument.Parse(json);
        var enums = new HavokEnums();

        foreach (var field in document.RootElement.GetProperty("fields").EnumerateObject())
        {
            var names = new Dictionary<long, string>();
            foreach (var value in field.Value.GetProperty("values").EnumerateObject())
                names[long.Parse(value.Name)] = value.Value.GetString() ?? "";

            enums._names[field.Name] = names;
            if (field.Value.TryGetProperty("combining", out var combining) &&
                combining.ValueKind == JsonValueKind.True)
                enums._combining.Add(field.Name);
        }

        return enums;
    }

    /// Written the way `symrm names` produces it, so a rebuilt table can be dropped straight in.
    public static string Write(IEnumerable<(string Key, bool Combining, IEnumerable<(long Value, string Name)> Names)> fields)
    {
        var root = new Dictionary<string, object>
        {
            ["note"] = "Value names for enum and flags fields, read off vanilla files by setting our " +
                       "reading of the bytes beside hkxpack's reading of the same field. Rebuild with " +
                       "`symrm names`. A value no vanilla file uses is absent, not invented.",
            ["fields"] = fields.OrderBy(f => f.Key, StringComparer.Ordinal).ToDictionary(
                f => f.Key,
                f => (object)new Dictionary<string, object>
                {
                    ["combining"] = f.Combining,
                    ["values"] = f.Names.OrderBy(n => n.Value)
                                        .ToDictionary(n => n.Value.ToString(), n => n.Name),
                }),
        };

        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
    }

    private static HavokEnums LoadShipped()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string? resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("HavokEnumNames.json", StringComparison.Ordinal));

        if (resource != null)
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            return Parse(stream);
        }

        string beside = Path.Combine(AppContext.BaseDirectory, "HavokEnumNames.json");
        if (File.Exists(beside)) using (var stream = File.OpenRead(beside)) return Parse(stream);

        // Not fatal, unlike a missing class layout. Without names every enum reads as a number,
        // which is incomplete rather than wrong, and the coverage report says so.
        return new HavokEnums();
    }
}
