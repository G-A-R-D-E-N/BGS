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

        /// Whether carrying this out makes the file longer. Text, arrays and new objects are all
        /// appended rather than overwritten, so a caller comparing the result to the original byte
        /// for byte has to expect it.
        public bool Grows => Changes.Exists(c => c.Text || c.Array || c.Added);
    }

    public sealed record Change(string ClassName, int Index, string Field, string Value,
                                bool Text = false, bool Ref = false, bool Array = false,
                                bool Added = false)
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

    /// A pointer at another object. Written by moving the fixup that names it rather than by writing
    /// anything into the object at all, so the file does not change length and no byte moves.
    ///
    /// This is what rewiring a node on the canvas is. It reads as a structural edit because the
    /// graph's shape changes, and it is not one in the file: the objects are all still there, the
    /// same size, in the same places, and one entry in the pointer table names a different
    /// destination.
    private static bool IsReference(string type) =>
        type.StartsWith("pointer of", StringComparison.Ordinal) || type == "pointer";

    /// An array of pointers at other objects, which is what a node's children are.
    ///
    /// Resized by appending, the same way a longer string is. The new run of pointers goes on the
    /// end of the section and the array's own pointer is aimed at it, so nothing that was already in
    /// the file moves and no offset anybody holds goes stale.
    ///
    /// The element fixups are rewritten to name the new run, and they are put back at the same
    /// position in the table rather than on the end. That is not tidiness. The table is in the order
    /// the writer walked the objects, and moving a run of entries to the end of it makes hkxpack
    /// read every element of that array as null.
    private static bool IsPointerArray(string type) => type == "array of pointer";

    /// Where the object's own id is kept in a field bag. Not a field name any file can have.
    private const string IdKey = "#id";

    /// What hkxpack writes in a pointer field: an object id, or the word null.
    private static bool IsReferenceValue(string value) =>
        value == "null" ||
        (value.Length > 1 && value[0] == '#' && value[1..].All(char.IsAsciiDigit));

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

            if (edited.Count < originals.Count)
                return new Plan(changes,
                    $"{originals.Count - edited.Count} {className} object(s) were removed, which is " +
                    "not written in place yet");

            if (edited.Count > originals.Count)
            {
                // Only added at the end, and only with the ids that follow on. The editor appends a
                // new object to the document and numbers it one past the highest, so this holds; it
                // is checked because everything downstream resolves an id to a position and would
                // otherwise aim a pointer at the wrong object without saying so.
                for (int k = 0; k < originals.Count; k++)
                    if (originals[k][IdKey] != edited[k][IdKey])
                        return new Plan(changes,
                            $"the {className} objects were renumbered, so nothing can be matched up");

                for (int k = originals.Count; k < edited.Count; k++)
                    changes.Add(new Change(className, k, "", edited[k][IdKey], Added: true));
            }

            var layout = classes.Members(className).ToDictionary(m => m.Name, m => m.Type,
                                                                 StringComparer.Ordinal);

            // One field, considered on its own. Written once and used twice: for a field whose value
            // changed, and for every field of an object that has just been added, where there is no
            // old value to compare against and everything the editor wrote is new.
            string? Consider(int i, string field, string now)
            {
                if (!layout.TryGetValue(field, out string? type))
                    return $"{className}.{field} changed, and we have no byte layout for it";

                if (IsPointerArray(type))
                {
                    var elements = now.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (!elements.All(IsReferenceValue))
                        return $"{className}.{field} holds something that is neither an object id nor null";

                    changes.Add(new Change(className, i, field, string.Join(" ", elements), Array: true));
                    return null;
                }

                if (IsReference(type))
                {
                    if (!IsReferenceValue(now))
                        return $"{className}.{field} was set to '{now}', which is neither an object " +
                               "id nor null";

                    changes.Add(new Change(className, i, field, now, Ref: true));
                    return null;
                }

                if (!Writable.Contains(type) && !WritableText.Contains(type))
                    return $"{className}.{field} changed, and a {type} cannot be written in place " +
                           "without moving what follows it";

                if (!WritableText.Contains(type) && !Parses(now, type))
                    return $"{className}.{field} was set to '{now}', which is not a {type}";

                changes.Add(new Change(className, i, field, now, WritableText.Contains(type)));
                return null;
            }

            for (int i = 0; i < originals.Count; i++)
            {
                foreach (var (field, was) in originals[i])
                {
                    if (field == IdKey) continue;

                    if (!edited[i].TryGetValue(field, out string? now))
                        return new Plan(changes, $"{className}.{field} is no longer in the file");

                    if (string.Equals(was, now, StringComparison.Ordinal)) continue;

                    string? refusal = Consider(i, field, now);
                    if (refusal != null) return new Plan(changes, refusal);
                }

                if (edited[i].Count != originals[i].Count)
                    return new Plan(changes, $"a {className} gained or lost a field");
            }

            // The added ones. A new object starts as zeroes, so a field the editor left out is
            // whatever zero means for it, and a field it wrote is a change like any other.
            for (int i = originals.Count; i < edited.Count; i++)
                foreach (var (field, value) in edited[i])
                {
                    if (field == IdKey) continue;

                    string? refusal = Consider(i, field, value);
                    if (refusal != null) return new Plan(changes, refusal);
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

        // The same question the window asks before it reads a value out of the bytes, asked again
        // before one is written into them, and it matters more here. Every offset written below
        // comes from this build's idea of the class; if the file was written against a different
        // one, reading it back gives a wrong number and writing it puts a wrong number in somebody
        // else's field. Refused rather than attempted: the caller falls back to the rebuild, which
        // goes through the file's own class definitions rather than ours.
        var mismatched = HavokClassTypes.Shipped.SignatureProblems(objects.ClassNames());
        if (mismatched.Count > 0)
            throw new InvalidOperationException(
                "This file's classes are not the ones this build describes, so nothing was written " +
                $"into its bytes: {mismatched[0]}" +
                (mismatched.Count > 1 ? $", and {mismatched.Count - 1} more like it." : "."));
        // Added objects first, because everything after this resolves a class and a position, and a
        // field of an object that does not exist yet cannot be written.
        int adding = 0;
        foreach (var add in plan.Changes.Where(c => c.Added))
        {
            var data = image.Section("__data__")
                ?? throw new InvalidOperationException("this file has no data section");

            var layout = HavokClassTypes.Shipped[add.ClassName];
            if (layout?.Size is not int size || size <= 0)
                throw new InvalidOperationException(
                    $"No size for {add.ClassName}, so no object of it was added.");

            var names = image.Section("__classnames__")
                ?? throw new InvalidOperationException("this file has no class name section");

            // A class the file has never named gets named here rather than refused. That section can
            // be grown, and the one implementation that does it lives in NativeAppend, which knows
            // the part that is easy to get wrong: the section is padded to sixteen with 0xFF and a
            // name written after that padding is one our reader finds and the game never does.
            int nameAt = NativeAppend.NameOffset(names, add.ClassName, layout.Signature);

            // Where it will land, and therefore what id it must have. The editor numbers a new
            // object one past the highest and this appends it past the last, so the two agree. They
            // are checked rather than trusted, because everything downstream turns an id into a
            // position and would otherwise aim a pointer at the wrong object in silence.
            string expected = "#" + (NativeGraphModel.FirstId + objects.Instances.Count + adding);
            if (add.Value != expected)
                throw new InvalidOperationException(
                    $"The new {add.ClassName} is {add.Value} in the document and would be {expected} " +
                    "in the file, so nothing was written.");

            data.AddVirtual(data.AppendObject(new byte[size]), image.Sections.IndexOf(names), nameAt);
            adding++;
        }

        // Read again, so the added objects are in the list the field writes below look themselves up
        // in. The view resolved its objects when it was built and does not know about them.
        if (adding > 0) objects = new PackfileObjects(image, classes);

        var byClass = objects.Instances.GroupBy(i => i.ClassName)
                                       .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // The plan counts objects as the XML lists them and this writes them as the file stores
        // them, which only line up while both agree how many of a class there are. They often do
        // not: hkxpack gives an inline struct its own object and the file does not. Today the
        // enclosing struct is always refused first so a mismatched class never reaches here, but
        // that is a consequence of other code rather than anything this checks, and writing a right
        // value into the wrong object is exactly the silent kind of wrong.
        foreach (var group in plan.Changes.Where(c => !c.Added).GroupBy(c => c.ClassName))
        {
            int inFile = byClass.TryGetValue(group.Key, out var all) ? all.Count : 0;
            if (group.Max(c => c.Index) >= inFile)
                throw new InvalidOperationException(
                    $"The file holds {inFile} {group.Key} objects, fewer than the edit expects, so " +
                    "nothing was written rather than guessing which one was meant.");
        }

        foreach (var change in plan.Changes)
        {
            if (change.Added) continue;

            if (!byClass.TryGetValue(change.ClassName, out var instances) ||
                change.Index >= instances.Count)
                throw new InvalidOperationException(
                    $"{change} does not correspond to anything in the file, so nothing was written.");

            var instance = instances[change.Index];
            var member = (classes ?? HavokClasses.Shipped).Field(change.ClassName, change.Field)
                ?? throw new InvalidOperationException($"No layout for {change.ClassName}.{change.Field}.");

            bool written = change.Ref ? Repoint(image, objects, instance, change)
                         : change.Array ? Resize(image, objects, instance, change)
                         : member.Type switch
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

        // Both pointer tables put back into the order the writer would have written them in. Setting
        // an entry that already exists leaves it where it is, so this only matters once something is
        // added, and an array going from empty to holding something adds one. Done for every plan
        // rather than only the ones that add, because a null save has to come back byte for byte and
        // that is the check that proves this reorder is the file's own order and not our idea of it.
        FixupOrder.Reorder(image);

        return image.Rebuild();
    }

    /// A field narrower than four bytes has to be written at its own width, or writing a one byte
    /// flag would flatten the three bytes beside it, which belong to other fields.
    /// Aims a pointer field at another object, or at nothing.
    ///
    /// The object ids are hkxpack's numbering, which counts from #90 in the order the objects sit in
    /// the file, and that ordering is the one the reader already uses. So an id resolves to a
    /// position in the object list and from there to the offset the fixup has to name.
    private static bool Repoint(PackfileImage image, PackfileObjects objects,
                                PackfileObjects.Instance instance, Change change)
    {
        var data = image.Section("__data__")
            ?? throw new InvalidOperationException("this file has no data section");

        if (objects.FieldAt(instance, change.Field) is not int at)
            throw new InvalidOperationException(
                $"No offset for {change.ClassName}.{change.Field}, so nothing was written.");

        if (change.Value == "null")
        {
            data.SetGlobal(at, 0, -1);
            return true;
        }

        int index = int.Parse(change.Value[1..]) - NativeGraphModel.FirstId;
        if (index < 0 || index >= objects.Instances.Count)
            throw new InvalidOperationException(
                $"{change} names an object this file does not have, so nothing was written.");

        data.SetGlobal(at, image.Sections.IndexOf(data), objects.Instances[index].Offset);
        return true;
    }

    /// Writes an array of object pointers at a new length.
    ///
    /// The run goes on the end rather than over the old one, so nothing already in the file moves.
    /// Three things then have to agree: the array's own pointer aims at the new run, the count beside
    /// it says how long it is, and there is one fixup per element that points somewhere.
    ///
    /// The capacity word is not invented. It carries flags in its top bits, and both zero and the
    /// high bit occur across the vanilla corpus, so what was there is kept and only the length part
    /// is rewritten.
    private static bool Resize(PackfileImage image, PackfileObjects objects,
                               PackfileObjects.Instance instance, Change change)
    {
        var data = image.Section("__data__")
            ?? throw new InvalidOperationException("this file has no data section");

        if (objects.FieldAt(instance, change.Field) is not int at)
            throw new InvalidOperationException(
                $"No offset for {change.ClassName}.{change.Field}, so nothing was written.");

        var elements = change.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int section = image.Sections.IndexOf(data);

        int run = elements.Length == 0 ? -1 : data.AppendData(new byte[elements.Length * 8]);
        data.SetLocal(at, run);

        // The element fixups are replaced where the old ones sat rather than dropped and appended.
        // Their position in the table is not free: the table is in the order the writer walked the
        // objects, and moving a run of entries to the end makes hkxpack read every element of that
        // array as null. The run of bytes still goes on the end, which is what keeps the rest of the
        // file where it was; only the table entries stay put.
        var old = objects.ArrayAt(at);
        var entries = data.Globals().ToList();
        int first = entries.Count;

        if (old != null && old.Count > 0)
        {
            int from = old.At, to = old.At + old.Count * 8;
            int found = entries.FindIndex(e => e.Source >= from && e.Source < to);
            if (found >= 0) first = found;
            entries.RemoveAll(e => e.Source >= from && e.Source < to);
        }

        var replacements = new List<(int, int, int)>();
        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i] == "null") continue;

            int index = int.Parse(elements[i][1..]) - NativeGraphModel.FirstId;
            if (index < 0 || index >= objects.Instances.Count)
                throw new InvalidOperationException(
                    $"{change} names an object this file does not have, so nothing was written.");

            replacements.Add((run + i * 8, section, objects.Instances[index].Offset));
        }

        entries.InsertRange(Math.Min(first, entries.Count), replacements);
        data.SetGlobals(entries);

        BitConverter.GetBytes(elements.Length).CopyTo(data.Data, at + 8);
        uint capacity = BitConverter.ToUInt32(data.Data, at + 12);
        BitConverter.GetBytes((capacity & 0xC0000000u) | (uint)elements.Length).CopyTo(data.Data, at + 12);

        return true;
    }

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

            // Under a key no hkparam can have, because a param name never starts with a hash. The id
            // is what says where an added object will sit once it is written, and it is checked
            // rather than trusted.
            fields[IdKey] = element.Attribute("name")?.Value ?? "";

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
