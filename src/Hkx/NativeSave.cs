using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace OpenCommonwealth.Services.Hkx;

// Saving by writing the changed values into the original file's own bytes, instead of converting the
// whole file to XML and asking hkxpack to build a new one.
//
// Why it is worth the trouble: a rebuild through XML replaces every byte of the file, so anything
// the XML cannot carry exactly is lost, which is why saving a file holding a lossless compressed
// animation had to be refused outright. Writing values in place leaves every byte we did not
// deliberately change exactly as Bethesda shipped it, so there is nothing for a round trip to lose.
//
// The limit is honest and checked rather than assumed: this can only change a value into another
// value of the same width. Every offset in a packfile is derived from the sizes of what precedes
// it, so anything that resizes an object, adds or removes one, or changes the length of a string
// would invalidate pointers all through the file. When an edit is not expressible this way it says
// so and the caller falls back to the old path, which is still correct, just lossier.
public static class NativeSave
{
    public sealed record Plan(List<Change> Changes, string? Refusal)
    {
        public bool Possible => Refusal == null;
        public bool Empty => Changes.Count == 0;
    }

    public sealed record Change(string ClassName, int Index, string Field, string Value)
    {
        public override string ToString() => $"{ClassName}[{Index}].{Field} = {Value}";
    }

    /// Which fixed width scalars a value can be written into. Everything else, strings and arrays and
    /// pointers most of all, changes the size of something and is therefore not ours to write.
    private static readonly HashSet<string> Writable = new(StringComparer.Ordinal)
    {
        "real", "int32", "uint32", "int16", "uint16", "int8", "uint8", "bool", "enum", "half",
    };

    /// Works out what changed between the file as loaded and the file as edited, and whether all of
    /// it can be written in place. Compares the two XML texts rather than the bytes, because the XML
    /// is what the editor actually changes, and a difference the comparison cannot see is a
    /// difference that would be silently dropped.
    public static Plan Compare(string originalXml, string editedXml, HavokClasses? classes = null)
    {
        classes ??= HavokClasses.Shipped;

        var before = ByClass(originalXml);
        var after = ByClass(editedXml);
        var changes = new List<Change>();

        if (before.Count != after.Count || before.Keys.Any(k => !after.ContainsKey(k)))
            return new Plan(changes, "the set of object types in the file changed");

        foreach (var (className, originals) in before)
        {
            var edited = after[className];
            if (originals.Count != edited.Count)
                return new Plan(changes,
                    $"the number of {className} objects changed from {originals.Count} to {edited.Count}");

            var layout = classes.Members(className).ToDictionary(m => m.Name, m => m.Type,
                                                                 StringComparer.Ordinal);

            for (int i = 0; i < originals.Count; i++)
            {
                foreach (var (field, was) in originals[i])
                {
                    if (!edited[i].TryGetValue(field, out string? now))
                        return new Plan(changes, $"{className}.{field} is no longer in the file");

                    if (string.Equals(was, now, StringComparison.Ordinal)) continue;

                    if (!layout.TryGetValue(field, out string? type))
                        return new Plan(changes,
                            $"{className}.{field} changed, and we have no byte layout for it");

                    if (!Writable.Contains(type))
                        return new Plan(changes,
                            $"{className}.{field} changed, and a {type} cannot be written in place " +
                            "without moving what follows it");

                    changes.Add(new Change(className, i, field, now));
                }

                if (edited[i].Count != originals[i].Count)
                    return new Plan(changes, $"a {className} gained or lost a field");
            }
        }

        return new Plan(changes, null);
    }

    /// Applies a plan to a file and returns the new bytes. Throws rather than half applying: a file
    /// with some of an edit in it is worse than a file with none of it.
    public static byte[] Apply(string hkxPath, Plan plan, HavokClasses? classes = null)
    {
        if (!plan.Possible)
            throw new InvalidOperationException("This edit cannot be written in place: " + plan.Refusal);

        var image = PackfileImage.Read(hkxPath);
        var objects = new PackfileObjects(image, classes);
        var byClass = objects.Instances.GroupBy(i => i.ClassName)
                                       .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var change in plan.Changes)
        {
            if (!byClass.TryGetValue(change.ClassName, out var instances) ||
                change.Index >= instances.Count)
                throw new InvalidOperationException(
                    $"{change} does not correspond to anything in the file, so nothing was written.");

            var instance = instances[change.Index];
            var member = (classes ?? HavokClasses.Shipped).Field(change.ClassName, change.Field)
                ?? throw new InvalidOperationException($"No layout for {change.ClassName}.{change.Field}.");

            bool written = member.Type == "real"
                ? objects.WriteFloat(instance, change.Field, AsFloat(change.Value))
                : WriteNarrow(objects, instance, change.Field, member.Type, change.Value,
                              image.Section("__data__")!);

            if (!written)
                throw new InvalidOperationException($"{change} could not be written, so nothing was.");
        }

        return image.Rebuild();
    }

    /// A field narrower than four bytes has to be written at its own width, or writing a one byte
    /// flag would flatten the three bytes beside it, which belong to other fields.
    private static bool WriteNarrow(PackfileObjects objects, PackfileObjects.Instance instance,
                                    string field, string type, string value, PackfileSection data)
    {
        int? at = objects.FieldAt(instance, field);
        if (at == null) return false;

        long number = AsLong(value, type);
        int width = type switch
        {
            "int8" or "uint8" or "bool" or "enum" => 1,
            "int16" or "uint16" or "half" => 2,
            _ => 4,
        };
        if (at + width > data.Data.Length) return false;

        for (int i = 0; i < width; i++) data.Data[at.Value + i] = (byte)(number >> (8 * i));
        return true;
    }

    private static float AsFloat(string value) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;

    /// hkxpack writes a bool as true or false and an enum by name, neither of which parses as a
    /// number. A name we cannot resolve is refused earlier rather than guessed at here.
    private static long AsLong(string value, string type)
    {
        string text = value.Trim();
        if (type == "bool")
            return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1" ? 1 : 0;

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n)) return n;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)) return n;

        throw new InvalidOperationException(
            $"'{value}' is not a number, so it cannot be written into a {type} field. " +
            "Named values are not resolved here on purpose: guessing one writes the wrong number.");
    }

    /// Objects grouped by class in document order, each as its simple named values. Nested objects
    /// appear in the document too and are counted here the same way, which is what makes the index
    /// within a class line up with the same index in the file.
    private static Dictionary<string, List<Dictionary<string, string>>> ByClass(string xml)
    {
        var byClass = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.Ordinal);
        if (xml.Length == 0) return byClass;

        foreach (var element in XDocument.Parse(xml).Descendants("hkobject"))
        {
            string? className = element.Attribute("class")?.Value;
            if (className == null) continue;

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in element.Elements("hkparam"))
            {
                string? name = p.Attribute("name")?.Value;
                if (name != null) fields[name] = (p.Value ?? "").Trim();
            }

            if (!byClass.TryGetValue(className, out var list)) byClass[className] = list = new();
            list.Add(fields);
        }

        return byClass;
    }
}
