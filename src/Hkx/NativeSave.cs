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
// Two kinds of edit go this way. A fixed width value is written over the value that was there, so
// nothing moves at all. A string is written on the end of the section and its pointer repointed,
// which also moves nothing: every offset in the file is derived from the sizes of what precedes it,
// and appending has nothing after it. The text that was there is left where it is, unreferenced.
//
// The limit is honest and checked rather than assumed: anything that changes the number of objects
// or the length of an array is still refused, because those move what follows them. When an edit is
// not expressible this way it says so and the caller falls back to the old path, which is still
// correct, just lossier.
public static class NativeSave
{
    public sealed record Plan(List<Change> Changes, string? Refusal)
    {
        public bool Possible => Refusal == null;
        public bool Empty => Changes.Count == 0;

        /// Whether carrying this out makes the file longer. Text is appended rather than overwritten,
        /// so a caller comparing the result to the original byte for byte has to expect it.
        public bool Grows => Changes.Exists(c => c.Text);
    }

    public sealed record Change(string ClassName, int Index, string Field, string Value, bool Text = false)
    {
        public override string ToString() => $"{ClassName}[{Index}].{Field} = {Value}";
    }

    /// Which fixed width scalars a value can be written into. Arrays and pointers are absent because
    /// changing one changes how much of the file follows it.
    private static readonly HashSet<string> Writable = new(StringComparer.Ordinal)
    {
        "real", "int32", "uint32", "int16", "uint16", "int8", "uint8", "bool", "enum",
    };

    /// Text fields. Not a fixed width, and written anyway: the field holds a pointer, and a pointer
    /// can be aimed at text appended to the end of the section instead of at the text it aimed at
    /// before. `cstring` and `stringptr` differ in how Havok owns the memory at runtime, which is
    /// nothing to a file on disk; both are a pointer to a run of bytes ending in zero.
    private static readonly HashSet<string> WritableText = new(StringComparer.Ordinal)
    {
        "stringptr", "cstring",
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

                    if (!Writable.Contains(type) && !WritableText.Contains(type))
                        return new Plan(changes,
                            $"{className}.{field} changed, and a {type} cannot be written in place " +
                            "without moving what follows it");

                    if (!WritableText.Contains(type) && !Parses(now, type))
                        return new Plan(changes,
                            $"{className}.{field} was set to '{now}', which is not a {type}");

                    changes.Add(new Change(className, i, field, now, WritableText.Contains(type)));
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

        // The plan counts objects as the XML lists them and this writes them as the file stores
        // them, which only line up while both agree how many of a class there are. They often do
        // not: hkxpack gives an inline struct its own object and the file does not. Today the
        // enclosing struct is always refused first so a mismatched class never reaches here, but
        // that is a consequence of other code rather than anything this checks, and writing a right
        // value into the wrong object is exactly the silent kind of wrong.
        foreach (var group in plan.Changes.GroupBy(c => c.ClassName))
        {
            int inFile = byClass.TryGetValue(group.Key, out var all) ? all.Count : 0;
            if (group.Max(c => c.Index) >= inFile)
                throw new InvalidOperationException(
                    $"The file holds {inFile} {group.Key} objects, fewer than the edit expects, so " +
                    "nothing was written rather than guessing which one was meant.");
        }

        foreach (var change in plan.Changes)
        {
            if (!byClass.TryGetValue(change.ClassName, out var instances) ||
                change.Index >= instances.Count)
                throw new InvalidOperationException(
                    $"{change} does not correspond to anything in the file, so nothing was written.");

            var instance = instances[change.Index];
            var member = (classes ?? HavokClasses.Shipped).Field(change.ClassName, change.Field)
                ?? throw new InvalidOperationException($"No layout for {change.ClassName}.{change.Field}.");

            bool written = member.Type switch
            {
                "real" => objects.WriteFloat(instance, change.Field, AsFloat(change.Value)),
                _ when WritableText.Contains(member.Type) =>
                    objects.WriteString(instance, change.Field, change.Value),
                _ => WriteNarrow(objects, instance, change.Field, member.Type, change.Value,
                                 image.Section("__data__")!),
            };

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

    /// Whether a typed value is really of the type the field holds. Checked when the edit is judged
    /// rather than when it is written, so a value that is not a number is refused with the field
    /// named instead of quietly landing as zero. Locale is fixed on purpose: the document spells
    /// numbers one way, and reading '1,5' as fifteen tenths because of a machine setting would put a
    /// number in the file that nobody typed.
    private static bool Parses(string value, string type)
    {
        string text = value.Trim();
        if (type == "bool")
            return text is "true" or "false" or "1" or "0";

        if (type == "real")
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
                   && !float.IsNaN(f) && !float.IsInfinity(f);

        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) &&
            !(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
              long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)))
            return false;

        // 8: the write masks the value down to the field's width, so a number too big for the field
        // would land as its low bytes and read as a different number entirely.
        return type switch
        {
            "int8" => n is >= -128 and <= 127,
            "uint8" or "enum" => n is >= 0 and <= 255,
            "int16" => n is >= short.MinValue and <= short.MaxValue,
            "uint16" => n is >= 0 and <= ushort.MaxValue,
            "int32" => n is >= int.MinValue and <= int.MaxValue,
            "uint32" => n is >= 0 and <= uint.MaxValue,
            _ => true,
        };
    }

    private static float AsFloat(string value) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
            ? f
            : throw new InvalidOperationException(
                $"'{value}' is not a number, so it cannot be written into a real field.");

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
