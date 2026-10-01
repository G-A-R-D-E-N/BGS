using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;
using System.Xml.Linq;
using OpenCommonwealth.Services;

namespace OpenCommonwealth.Services.Hkx;

public static partial class NativeSave
{
    public sealed record Plan(List<Change> Changes, string? Refusal, List<int>? Removed = null)
    {
        public bool Possible => Refusal == null;
        public bool Empty => Changes.Count == 0 && Gone.Count == 0;

        public List<int> Gone => Removed ?? new List<int>();

        public bool Grows => Changes.Exists(c => c.Text || c.Array || c.Added || c.Grow);
    }

    public sealed record Change(string ClassName, int Index, string Field, string Value,
                                bool Text = false, bool Ref = false, bool Array = false,
                                bool Added = false, int Element = -1, string Member = "",
                                bool Grow = false, int Id = 0)
    {

        public bool InElement => Element >= 0 && !Grow;

        public override string ToString() =>
            Grow ? $"{ClassName}[{Index}].{Field} is now {Value} element(s) long"
                 : InElement ? $"{ClassName}[{Index}].{Field}[{Element}].{Member} = {Value}"
                             : $"{ClassName}[{Index}].{Field} = {Value}";
    }

    private static readonly HashSet<string> Writable = new(StringComparer.Ordinal)
    {
        "real", "int32", "uint32", "int16", "uint16", "int8", "uint8", "bool", "enum",
    };

    private static readonly HashSet<string> WritableText = new(StringComparer.Ordinal)
    {
        "stringptr", "cstring",
    };

    internal static bool IsReference(string type) =>
        type.StartsWith("pointer of", StringComparison.Ordinal) || type == "pointer";

    internal static bool IsPointerArray(string type) => type == "array of pointer";

    internal static int WideFloats(string type) => type switch
    {
        "vector4" or "quaternion" => 4,
        "qstransform" or "matrix3" or "rotation" => 12,
        "transform" or "matrix4" => 16,
        _ => 0,
    };

    internal static bool IsWideInteger(string type) =>
        type is "uint64" or "int64" or "ulong";

    private static float[]? Bracketed(string value, int wanted)
    {
        var numbers = new List<float>();
        foreach (string token in value.Replace('(', ' ').Replace(')', ' ')
                                      .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ||
                float.IsNaN(f) || float.IsInfinity(f))
                return null;
            numbers.Add(f);
        }

        return numbers.Count == wanted ? numbers.ToArray() : null;
    }

    internal static bool IsTextArray(string type) =>
        type == "array of stringptr" || type == "array of cstring";

    internal static int ValueElement(string type) => type switch
    {
        "array of real" or "array of int32" or "array of uint32" => 4,
        "array of int16" or "array of uint16" => 2,
        "array of int8" or "array of uint8" or "array of bool" or "array of char" => 1,
        "array of int64" or "array of uint64" or "array of ulong" => 8,
        "array of qstransform" => WideFloats("qstransform") * sizeof(float),
        _ => 0,
    };

    internal static byte[]? Numbers(string value, string type, int width)
    {
        if (type != "array of qstransform")
            return NumberCodecs.ArrayBytes(value, type[("array of ").Length..], width);
        var groups = System.Text.RegularExpressions.Regex.Matches(value, @"\(([^()]*)\)");
        if (groups.Count % 3 != 0 ||
            !string.IsNullOrWhiteSpace(System.Text.RegularExpressions.Regex.Replace(value, @"\([^()]*\)", "")))
            return null;
        var values = new List<string>();
        foreach (System.Text.RegularExpressions.Match group in groups)
        {
            var tokens = group.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 4) return null;
            values.AddRange(tokens);
        }
        return NumberCodecs.ArrayBytes(string.Join(" ", values), "real", sizeof(float));
    }

    private const char TextSeparator = '\0';

    private const string IdKey = "#id";
    private const uint EmbeddedArrayStorage = 0x80000000u;

    private static void WriteArrayCount(PackfileSection data, int at, int count)
    {
        BitConverter.GetBytes(count).CopyTo(data.Data, at + 8);
        uint capacity = BitConverter.ToUInt32(data.Data, at + 12);
        uint flags = capacity & 0xC0000000u;
        if (count > 0) flags |= EmbeddedArrayStorage;
        BitConverter.GetBytes(flags | (uint)count).CopyTo(data.Data, at + 12);
    }

    private static bool IsReferenceValue(string value) =>
        value == "null" ||
        (value.Length > 1 && value[0] == '#' && value[1..].All(char.IsAsciiDigit));

    public static Plan Compare(string originalXml, string editedXml, HavokClasses? classes = null)
    {
        classes ??= HavokClasses.Shipped;

        var deleted = Deleted(originalXml, editedXml);

        var before = ByClass(originalXml, classes);
        var after = ByClass(editedXml, classes);
        var changes = new List<Change>();

        foreach (var (className, originals) in before)
        {
            if (!after.TryGetValue(className, out var edited))
            {
                if (originals.All(o => deleted.Text.Contains(o[IdKey]))) continue;
                return new Plan(changes, "the set of object types in the file changed");
            }

            var survivors = originals.Where(o => !deleted.Text.Contains(o[IdKey])).ToList();
            if (survivors.Count == 0 && originals.Count > 0)
                return new Plan(changes,
                    $"the {className} objects were renumbered, so nothing can be matched up");
            if (edited.Count < survivors.Count)
                return new Plan(changes,
                    $"{survivors.Count - edited.Count} {className} object(s) went missing without " +
                    "being deleted, so nothing can be matched up");

            for (int k = 0; k < survivors.Count; k++)
                if (edited[k][IdKey] != survivors[k][IdKey])
                    return new Plan(changes,
                        $"the {className} objects were renumbered, so nothing can be matched up");

            for (int k = survivors.Count; k < edited.Count; k++)
                changes.Add(new Change(className, k, "", edited[k][IdKey], Added: true,
                                       Id: IdOf(edited[k])));

            var layout = classes.Members(className).ToDictionary(m => m.Name, m => m.Type,
                                                                 StringComparer.Ordinal);

            for (int i = 0; i < survivors.Count; i++)
            {
                var original = survivors[i];
                int id = IdOf(original);

                var resized = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (field, was) in original)
                {
                    if (!field.EndsWith(CountKey, StringComparison.Ordinal)) continue;
                    if (edited[i].TryGetValue(field, out string? now) &&
                        string.Equals(was, now, StringComparison.Ordinal)) continue;

                    resized.Add(field[..^CountKey.Length]);
                }

                foreach (var field in edited[i].Keys)
                {
                    if (!field.EndsWith(CountKey, StringComparison.Ordinal)) continue;
                    if (!original.ContainsKey(field)) resized.Add(field[..^CountKey.Length]);
                }

                foreach (string arrayField in resized.OrderBy(f => f, StringComparer.Ordinal))
                {
                    string? refusal = Resized(classes, changes, className, layout, id, i,
                                              original, edited[i], arrayField);
                    if (refusal != null) return new Plan(changes, refusal);
                }

                foreach (var (field, was) in original)
                {
                    if (field == IdKey || Belongs(field, resized)) continue;

                    if (!edited[i].TryGetValue(field, out string? now))
                        return new Plan(changes, $"{className}.{field} is no longer in the file");

                    if (string.Equals(was, now, StringComparison.Ordinal)) continue;

                    if (edited[i].ContainsKey(field + CountKey) && layout.TryGetValue(field, out string? valueType) && ValueElement(valueType) is int width and > 0 &&
                        Numbers(now, valueType, width) is { } bytes && bytes.Length / width != Length(edited[i], field))
                        return new Plan(changes, $"{className}.{field} does not match its declared element count");

                    string? refusal = Consider(classes, layout, changes, className, i, id, field, now);
                    if (refusal != null) return new Plan(changes, refusal);
                }

                if (Counted(edited[i], resized) != Counted(original, resized))
                    return new Plan(changes, $"a {className} gained or lost a field");
            }

            for (int k = survivors.Count; k < edited.Count; k++)
                foreach (var (field, value) in edited[k])
                {
                    if (field == IdKey) continue;

                    string? refusal = Consider(classes, layout, changes, className, k,
                                                IdOf(edited[k]), field, value, added: true);
                    if (refusal != null) return new Plan(changes, refusal);
                }
        }

        foreach (var className in after.Keys.Where(k => !before.ContainsKey(k)).ToList())
        {
            if ((classes.Members(className).Count == 0 && classes[className] == null) ||
                HavokClassTypes.Shipped[className]?.Signature == null)
                return new Plan(changes, "the set of object types in the file changed");

            var layout = classes.Members(className).ToDictionary(m => m.Name, m => m.Type,
                                                                 StringComparer.Ordinal);
            var edited = after[className];
            for (int k = 0; k < edited.Count; k++)
            {
                changes.Add(new Change(className, k, "", edited[k][IdKey], Added: true,
                                       Id: IdOf(edited[k])));
                foreach (var (field, value) in edited[k])
                {
                    if (field == IdKey) continue;
                    string? refusal = Consider(classes, layout, changes, className, k,
                                               IdOf(edited[k]), field, value, added: true);
                    if (refusal != null) return new Plan(changes, refusal);
                }
            }
        }

        return new Plan(changes, null, deleted.Ids);
    }

    private static string? Consider(HavokClasses classes, Dictionary<string, string> layout,
                                    List<Change> changes, string className, int index, int id,
                                    string field, string now, bool added = false)
    {
        if (field.EndsWith(CountKey, StringComparison.Ordinal))
        {
            if (added)
            {
                string arrayField = field[..^CountKey.Length];
                if (layout.TryGetValue(arrayField, out string? arrayType) &&
                    arrayType == "struct" && now == "1")
                    return null;
                if (arrayType != null && ValueElement(arrayType) > 0) return null;

                if (arrayType == "array of struct" &&
                    int.TryParse(now, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) &&
                    count >= 0 && ElementClass(className, arrayField) is string elementClass &&
                    HavokClassTypes.Shipped[elementClass]?.Size is > 0)
                {
                    changes.Add(new Change(className, index, arrayField, now, Grow: true, Id: id));
                    return null;
                }
            }
            return $"a new {className} was given {now} element(s) in " +
                   $"{field[..^CountKey.Length]}, which is not written in place yet";
        }

        int dot = field.IndexOf('.');
        if (added && dot > 0 &&
            layout.TryGetValue(field[..dot], out string? structType) && structType == "struct")
        {
            string structField = field[..dot];
            string member = field[(dot + 1)..];
            string? why = StructElementWritable(classes, className, structField, member, now);
            if (why != null) return why;
            changes.Add(new Change(className, index, structField, now,
                                   Element: 0, Member: member, Id: id));
            return null;
        }

        int bracket = field.IndexOf('[');
        if (bracket > 0)
        {
            int close = field.IndexOf(']', bracket);
            if (close < 0 || close + 2 > field.Length)
                return $"{className}.{field} is not a name this understands";

            string arrayField = field[..bracket];
            if (!int.TryParse(field[(bracket + 1)..close], out int element))
                return $"{className}.{field} does not name an element";

            string member = field[(close + 2)..];

            if (!layout.TryGetValue(arrayField, out string? arrayType))
                return $"{className}.{arrayField} has no byte layout";

            if (arrayType == "struct")
            {
                if (element != 0)
                    return $"{className}.{arrayField} is one struct, so it has no element {element}";
            }
            else if (arrayType != "array of struct")
            {
                return $"{className}.{arrayField} is not an array of structs";
            }

            string? why = StructElementWritable(classes, className, arrayField, member, now);
            if (why != null) return why;

            changes.Add(new Change(className, index, arrayField, now, Element: element,
                                   Member: member, Id: id));
            return null;
        }

        if (!layout.TryGetValue(field, out string? type))
            return $"{className}.{field} changed, and we have no byte layout for it";

        if (IsTextArray(type))
        {
            changes.Add(new Change(className, index, field, now, Text: true, Array: true, Id: id));
            return null;
        }

        if (ValueElement(type) is int width and > 0)
        {
            if (Numbers(now, type, width) == null)
                return $"{className}.{field} was set to something that is not a list of " +
                       $"{type[("array of ").Length..]}";

            changes.Add(new Change(className, index, field, now, Array: true, Id: id));
            return null;
        }

        if (IsPointerArray(type))
        {
            var elements = now.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (!elements.All(IsReferenceValue))
                return $"{className}.{field} holds something that is neither an object id nor null";

            changes.Add(new Change(className, index, field, string.Join(" ", elements), Array: true, Id: id));
            return null;
        }

        if (IsReference(type))
        {
            if (!IsReferenceValue(now))
                return $"{className}.{field} was set to '{now}', which is neither an object " +
                       "id nor null";

            changes.Add(new Change(className, index, field, now, Ref: true, Id: id));
            return null;
        }

        if (WideFloats(type) is int floats and > 0)
        {
            if (Bracketed(now, floats) == null)
                return $"{className}.{field} was set to '{now}', which is not {floats} " +
                       "number(s) in brackets";

            changes.Add(new Change(className, index, field, now, Id: id));
            return null;
        }

        if (IsWideInteger(type))
        {
            if (!NumberCodecs.Parses(now.Trim(), type))
                return $"{className}.{field} was set to '{now}', which is not a {type}";

            changes.Add(new Change(className, index, field, now, Id: id));
            return null;
        }

        string storage = NumberCodecs.Underlying(type);
        if (storage != type && !Parses(now, storage))
        {
            var member = HavokClassTypes.Shipped.Members(className)
                .FirstOrDefault(m => m.Name == field);
            var map = member?.EType == null ? null
                : HavokClassTypes.Shipped.Enum(className, member.EType);
            if (map != null && map.TryGetValue(now.Trim(), out long numeric))
                now = numeric.ToString(CultureInfo.InvariantCulture);
        }

        if (!Writable.Contains(storage) && !WritableText.Contains(type))
            return $"{className}.{field} changed, and a {type} cannot be written in place " +
                   "without moving what follows it";

        if (!WritableText.Contains(type) && !Parses(now, storage))
            return $"{className}.{field} was set to '{now}', which is not a {type}";

        changes.Add(new Change(className, index, field, now, WritableText.Contains(type), Id: id));
        return null;
    }

    private static (List<int> Ids, HashSet<string> Text) Deleted(string originalXml, string editedXml)
    {
        var ids = new List<int>();
        var text = new HashSet<string>(StringComparer.Ordinal);
        if (originalXml.Length == 0 || editedXml.Length == 0) return (ids, text);

        var kept = new HashSet<string>(Ids(editedXml), StringComparer.Ordinal);
        foreach (string id in Ids(originalXml))
        {
            if (kept.Contains(id)) continue;
            text.Add(id);
            ids.Add(int.Parse(id[1..], CultureInfo.InvariantCulture));
        }

        return (ids, text);
    }

    private static int IdOf(Dictionary<string, string> fields) =>
        fields.TryGetValue(IdKey, out string? id) && id.Length > 1 && id[0] == '#' &&
        int.TryParse(id[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            ? n
            : -1;

    private static IEnumerable<string> Ids(string xml) =>
        XDocument.Parse(xml).Descendants("hkobject")
                 .Select(e => e.Attribute("name")?.Value ?? "")
                 .Where(id => id.Length > 1 && id[0] == '#' && id[1..].All(char.IsAsciiDigit));

    private static string ArrayOf(string field)
    {
        if (field.EndsWith(CountKey, StringComparison.Ordinal))
            return field[..^CountKey.Length];

        int bracket = field.IndexOf('[');
        return bracket > 0 ? field[..bracket] : "";
    }

    private static bool Belongs(string field, HashSet<string> arrays) =>
        arrays.Contains(field) || arrays.Contains(ArrayOf(field));

    private static int Counted(Dictionary<string, string> fields, HashSet<string> skip) =>
        skip.Count == 0 ? fields.Count : fields.Count(f => !Belongs(f.Key, skip));

    private static string? Resized(HavokClasses classes, List<Change> changes, string className,
                                   Dictionary<string, string> layout, int id, int index,
                                   Dictionary<string, string> before, Dictionary<string, string> after,
                                   string arrayField)
    {
        layout.TryGetValue(arrayField, out string? arrayType);

        if (arrayType != null && ValueElement(arrayType) is int width and > 0)
        {
            string value = after.GetValueOrDefault(arrayField, "");
            byte[]? bytes = Numbers(value, arrayType, width);
            if (bytes == null)
                return $"{className}.{arrayField} is not a list of {arrayType[("array of ").Length..]}";
            if (bytes.Length / width != Length(after, arrayField))
                return $"{className}.{arrayField} does not match its declared element count";
            changes.Add(new Change(className, index, arrayField, value, Array: true, Id: id));
            return null;
        }

        if (arrayType != null && IsTextArray(arrayType))
        {
            changes.Add(new Change(className, index, arrayField,
                                   after.GetValueOrDefault(arrayField, ""), Text: true, Array: true,
                                   Id: id));
            return null;
        }

        if (arrayType != "array of struct")
            return $"{className}.{arrayField} changed length, and it is not an array of structs";

        string? elementClass = ElementClass(className, arrayField);
        if (elementClass == null)
            return $"{className}.{arrayField} does not say what class its elements are";

        if (HavokClassTypes.Shipped[elementClass]?.Size is not int stride || stride <= 0)
            return $"{className}.{arrayField} holds {elementClass}, whose size this build does not know";

        int had = Length(before, arrayField), now = Length(after, arrayField);
        if (now < 0) return $"{className}.{arrayField} has no length in the edited file";

        var fill = new List<Change>();
        string prefix = arrayField + "[";

        foreach (var (field, value) in after)
        {
            if (!field.StartsWith(prefix, StringComparison.Ordinal)) continue;

            int close = field.IndexOf(']');
            if (close < 0 || close + 2 > field.Length)
                return $"{className}.{field} is not a name this understands";

            if (!int.TryParse(field[prefix.Length..close], out int element))
                return $"{className}.{field} does not name an element";

            string member = field[(close + 2)..];

            bool carried = element < had && element < now;
            if (carried && before.TryGetValue(field, out string? was) &&
                string.Equals(was, value, StringComparison.Ordinal))
                continue;

            string? why = StructElementWritable(classes, className, arrayField, member, value);
            if (why != null)
            {

                if (!carried && MeansNothing(value)) continue;
                return why;
            }

            fill.Add(new Change(className, index, arrayField, value, Element: element, Member: member,
                                 Id: id));
        }

        changes.Add(new Change(className, index, arrayField, now.ToString(CultureInfo.InvariantCulture),
                               Element: had, Grow: true, Id: id));
        changes.AddRange(fill);
        return null;
    }

    private static int Length(Dictionary<string, string> fields, string arrayField) =>
        !fields.TryGetValue(arrayField + CountKey, out string? text) ? 0
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : -1;

    private static bool MeansNothing(string value)
    {
        string text = value.Trim();
        if (text.Length == 0 || text == "null" || text == "false") return true;

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) &&
               n == 0;
    }

}
