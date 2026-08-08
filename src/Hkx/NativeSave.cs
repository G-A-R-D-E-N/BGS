using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
// A third kind goes a different way. Taking an object out moves everything after it, so it cannot be
// written in place at all; it is carried out last, after every value has been written at the offset
// it had, by laying the data section out again without it. That is still not a rebuild through XML,
// so it still loses nothing.
//
// The limit is honest and checked rather than assumed. What is still refused is a file whose objects
// have been renumbered, or a class that has gained or lost a field, or a value of a type nothing here
// can spell. When an edit is not expressible this way it says so and the caller falls back to the old
// path, which is still correct, just lossier.
public static class NativeSave
{
    public sealed record Plan(List<Change> Changes, string? Refusal, List<int>? Removed = null)
    {
        public bool Possible => Refusal == null;
        public bool Empty => Changes.Count == 0 && Gone.Count == 0;

        /// Objects the edit takes out of the file, by id. Deleting one moves every object after it,
        /// so this is carried out last, after every value has been written at the offsets it had.
        public List<int> Gone => Removed ?? new List<int>();

        /// Whether carrying this out makes the file longer. Text, arrays and new objects are all
        /// appended rather than overwritten, so a caller comparing the result to the original byte
        /// for byte has to expect it.
        public bool Grows => Changes.Exists(c => c.Text || c.Array || c.Added || c.Grow);
    }

    public sealed record Change(string ClassName, int Index, string Field, string Value,
                                bool Text = false, bool Ref = false, bool Array = false,
                                bool Added = false, int Element = -1, string Member = "",
                                bool Grow = false)
    {
        /// Whether this writes into one element of an array of structs rather than into a field of
        /// the object itself.
        public bool InElement => Element >= 0 && !Grow;

        public override string ToString() =>
            Grow ? $"{ClassName}[{Index}].{Field} is now {Value} element(s) long"
                 : InElement ? $"{ClassName}[{Index}].{Field}[{Element}].{Member} = {Value}"
                             : $"{ClassName}[{Index}].{Field} = {Value}";
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

    /// An array of strings, which is how a behaviour lists its event and variable names.
    ///
    /// This was the last refusal standing between a person and adding an event without Java. The
    /// array has to grow, and a run cannot grow where it sits, so it goes on the end like every
    /// other run that changes size and the file is compacted the next time it is laid out.
    private static bool IsTextArray(string type) =>
        type == "array of stringptr" || type == "array of cstring";

    /// What holds an array of names together while it is one value.
    ///
    /// A zero byte, because a name in this format ends at the first one and so cannot contain one.
    /// A newline was the obvious choice and it is wrong: `WeaponBehavior` declares two events whose
    /// names carry a literal carriage return and newline, `SyncRight\r\nFootRight` and
    /// `SyncLeft\r\nFootLeft`. Both hkxpack and this reader agree they are one name each, and
    /// splitting on newlines turned them into four and wrote an array two elements too long in ten
    /// of the vanilla behaviours.
    private const char TextSeparator = '\0';

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

        // What the edit took out. Ids are positions in the file, and the editor deletes an object by
        // removing its block rather than by renumbering what is left, so the ids that survive are
        // still the ids they were and a straight set difference says what went.
        var deleted = Deleted(originalXml, editedXml);

        var before = ByClass(originalXml, deleted.Text);
        var after = ByClass(editedXml);
        var changes = new List<Change>();

        // A class the file no longer holds any of. That used to be refused outright, and now it is
        // only a refusal when it happened without a deletion to explain it, which would mean the two
        // documents are not the same file.
        if (before.Keys.Any(k => !after.ContainsKey(k)) || after.Keys.Any(k => !before.ContainsKey(k)))
            return new Plan(changes, "the set of object types in the file changed");

        foreach (var (className, originals) in before)
        {
            var edited = after[className];

            if (edited.Count < originals.Count)
                return new Plan(changes,
                    $"{originals.Count - edited.Count} {className} object(s) went missing without " +
                    "being deleted, so nothing can be matched up");

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
                // A member inside an array of structs, as `variableBounds[2].min.value`. A length
                // change is a different question, handled before this is reached, so getting here
                // means the array is the same length and one number inside it moved. The exception
                // is an object being added, which has no old length to have changed from.
                if (field.EndsWith(CountKey, StringComparison.Ordinal))
                    return $"a new {className} was given {now} element(s) in " +
                           $"{field[..^CountKey.Length]}, which is not written in place yet";

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

                    if (!layout.TryGetValue(arrayField, out string? arrayType) ||
                        arrayType != "array of struct")
                        return $"{className}.{arrayField} is not an array of structs";

                    string? why = StructElementWritable(classes, className, arrayField, member, now);
                    if (why != null) return why;

                    changes.Add(new Change(className, i, arrayField, now, Element: element,
                                           Member: member));
                    return null;
                }

                if (!layout.TryGetValue(field, out string? type))
                    return $"{className}.{field} changed, and we have no byte layout for it";

                // An array of strings that changed. Written by appending, the same way a longer
                // string is: a new run of pointers on the end of the section, one appended string
                // per element, and the array aimed at the run. Nothing already in the file moves.
                if (IsTextArray(type))
                {
                    changes.Add(new Change(className, i, field, now, Text: true, Array: true));
                    return null;
                }

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
                // Arrays of structs that changed length, taken first and taken whole. An element
                // added at the end has no old value to compare against, and the elements below it
                // land in a run that is being rewritten anyway, so the array is considered as one
                // thing rather than key by key. Everything belonging to one of these is then left
                // out of the ordinary comparison below, which would otherwise read a key the
                // shorter side does not have as a field appearing or disappearing.
                var resized = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (field, was) in originals[i])
                {
                    if (!field.EndsWith(CountKey, StringComparison.Ordinal)) continue;
                    if (edited[i].TryGetValue(field, out string? now) &&
                        string.Equals(was, now, StringComparison.Ordinal)) continue;

                    resized.Add(field[..^CountKey.Length]);
                }

                // An array the file left empty has no count key at all on the original side, so a
                // first element only shows up as keys the edited side has and the original does not.
                foreach (var field in edited[i].Keys)
                {
                    if (!field.EndsWith(CountKey, StringComparison.Ordinal)) continue;
                    if (!originals[i].ContainsKey(field)) resized.Add(field[..^CountKey.Length]);
                }

                foreach (string arrayField in resized.OrderBy(f => f, StringComparer.Ordinal))
                {
                    string? refusal = Resized(classes, changes, className, layout, i,
                                              originals[i], edited[i], arrayField);
                    if (refusal != null) return new Plan(changes, refusal);
                }

                foreach (var (field, was) in originals[i])
                {
                    if (field == IdKey || Belongs(field, resized)) continue;

                    if (!edited[i].TryGetValue(field, out string? now))
                        return new Plan(changes, $"{className}.{field} is no longer in the file");

                    if (string.Equals(was, now, StringComparison.Ordinal)) continue;

                    string? refusal = Consider(i, field, now);
                    if (refusal != null) return new Plan(changes, refusal);
                }

                if (Counted(edited[i], resized) != Counted(originals[i], resized))
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

        return new Plan(changes, null, deleted.Ids);
    }

    /// The objects in the first document that are not in the second, by id.
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

    private static IEnumerable<string> Ids(string xml) =>
        XDocument.Parse(xml).Descendants("hkobject")
                 .Select(e => e.Attribute("name")?.Value ?? "")
                 .Where(id => id.Length > 1 && id[0] == '#' && id[1..].All(char.IsAsciiDigit));

    /// Which array a flattened key belongs to, or an empty string when it belongs to none.
    /// `variableBounds[2].min.value` and `variableBounds#count` both belong to `variableBounds`.
    private static string ArrayOf(string field)
    {
        if (field.EndsWith(CountKey, StringComparison.Ordinal))
            return field[..^CountKey.Length];

        int bracket = field.IndexOf('[');
        return bracket > 0 ? field[..bracket] : "";
    }

    /// Whether a flattened key describes part of one of these arrays. An array with nothing in it is
    /// written as a plain empty param and has no keys of its own at all, so its own name counts as
    /// well as the element and length keys a full one has.
    private static bool Belongs(string field, HashSet<string> arrays) =>
        arrays.Contains(field) || arrays.Contains(ArrayOf(field));

    private static int Counted(Dictionary<string, string> fields, HashSet<string> skip) =>
        skip.Count == 0 ? fields.Count : fields.Count(f => !Belongs(f.Key, skip));

    /// An array of structs at a new length, planned as one thing.
    ///
    /// The run of elements is rewritten wholesale rather than patched, so what this has to produce
    /// is every element the edited document holds. The elements the file already had are carried
    /// over as bytes when the run is written, which is what keeps anything inside them this cannot
    /// spell, so only the members that moved are listed for those. A new element starts as zeroes,
    /// so a member it leaves at zero needs nothing written and one it does not has to be a member
    /// that can be written at all, or the whole resize is refused rather than losing it quietly.
    private static string? Resized(HavokClasses classes, List<Change> changes, string className,
                                   Dictionary<string, string> layout, int index,
                                   Dictionary<string, string> before, Dictionary<string, string> after,
                                   string arrayField)
    {
        layout.TryGetValue(arrayField, out string? arrayType);

        // An array of strings at a new length, which is what declaring an event or a variable is.
        // Nothing element by element to work out: the whole run is rewritten from the names the
        // edited document holds, so the change carries all of them and the writer does the rest.
        if (arrayType != null && IsTextArray(arrayType))
        {
            changes.Add(new Change(className, index, arrayField,
                                   after.GetValueOrDefault(arrayField, ""), Text: true, Array: true));
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

            // Unchanged and already in the file, so the bytes carried over say it. Listing it would
            // mean being able to write it, which for a string or a pointer this cannot do, and
            // refusing there would refuse resizes that are perfectly safe.
            bool carried = element < had && element < now;
            if (carried && before.TryGetValue(field, out string? was) &&
                string.Equals(was, value, StringComparison.Ordinal))
                continue;

            string? why = StructElementWritable(classes, className, arrayField, member, value);
            if (why != null)
            {
                // A member of a brand new element that this cannot write is only a problem when it
                // was asked to hold something. Zero is what an appended element already is.
                if (!carried && MeansNothing(value)) continue;
                return why;
            }

            fill.Add(new Change(className, index, arrayField, value, Element: element, Member: member));
        }

        changes.Add(new Change(className, index, arrayField, now.ToString(CultureInfo.InvariantCulture),
                               Element: had, Grow: true));
        changes.AddRange(fill);
        return null;
    }

    /// How long a flattened object says one of its arrays is. Absent means the file left it empty,
    /// which is a length of zero and not a missing answer.
    private static int Length(Dictionary<string, string> fields, string arrayField) =>
        !fields.TryGetValue(arrayField + CountKey, out string? text) ? 0
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : -1;

    /// Whether a value is what a run of zero bytes already reads as, so writing it would change
    /// nothing. A null pointer, an empty string and a zero number all are.
    private static bool MeansNothing(string value)
    {
        string text = value.Trim();
        if (text.Length == 0 || text == "null" || text == "false") return true;

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) &&
               n == 0;
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

        // The struct array runs, before anything is written into one. Each is a fresh run appended
        // to the section, so the writes that follow have to find the array where it now is rather
        // than where it was, which means reading the objects again once the last one is done.
        bool grew = false;
        foreach (var change in plan.Changes.Where(c => c.Grow))
        {
            var instance = byClass[change.ClassName][change.Index];
            Regrow(image, objects, instance, change);
            grew = true;
        }

        if (grew)
        {
            objects = new PackfileObjects(image, classes);
            byClass = objects.Instances.GroupBy(i => i.ClassName)
                             .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        }

        foreach (var change in plan.Changes)
        {
            if (change.Added || change.Grow) continue;

            if (!byClass.TryGetValue(change.ClassName, out var instances) ||
                change.Index >= instances.Count)
                throw new InvalidOperationException(
                    $"{change} does not correspond to anything in the file, so nothing was written.");

            var instance = instances[change.Index];

            if (change.InElement)
            {
                if (!WriteInElement(image, objects, instance, change))
                    throw new InvalidOperationException($"{change} could not be written, so nothing was.");
                continue;
            }

            var member = (classes ?? HavokClasses.Shipped).Field(change.ClassName, change.Field)
                ?? throw new InvalidOperationException($"No layout for {change.ClassName}.{change.Field}.");

            bool written = change.Ref ? Repoint(image, objects, instance, change)
                         : change.Array && change.Text ? ResizeText(image, objects, instance, change)
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

        // Last, because everything above names an object by its position among its class and writes
        // at the offset it has today. Taking one out moves every object after it and renumbers every
        // id above the hole, so doing it any earlier would send the rest of the plan somewhere else.
        if (plan.Gone.Count > 0) NativeRemove.Delete(image, plan.Gone);

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

        return RepointAt(image, objects, data, at, change.Value);
    }

    /// Aims one pointer, wherever it sits. The offset is worked out by the caller, which is the only
    /// difference between a field of an object and a member of an element inside one: both are eight
    /// bytes named by a fixup, and neither moves anything when it is changed.
    private static bool RepointAt(PackfileImage image, PackfileObjects objects,
                                  PackfileSection data, int at, string value)
    {
        if (value == "null")
        {
            data.SetGlobal(at, 0, -1);
            return true;
        }

        int index = int.Parse(value[1..], CultureInfo.InvariantCulture) - NativeGraphModel.FirstId;
        if (index < 0 || index >= objects.Instances.Count)
            throw new InvalidOperationException(
                $"#{value[1..]} is not an object this file has, so nothing was written.");

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

    /// Writes an array of strings at whatever length the edit left it.
    ///
    /// The same three moves the other resizes make, and one more. A run of eight byte pointers goes
    /// on the end of the section, the array's own pointer is aimed at it and the count beside it is
    /// rewritten. The extra move is that each element is itself a pointer at text, so every name is
    /// appended too and gets its own local fixup.
    ///
    /// Every name is written out again rather than only the new ones. The run has moved, so the old
    /// element fixups name bytes nothing points at any more, and carrying a string over would mean
    /// working out which of the old runs it sat in. Appending them is a few hundred bytes on a file
    /// that gets compacted the next time it is laid out, and it cannot get an element wrong.
    ///
    /// An empty name is still a pointer, at a single zero byte. It is not the same as no pointer:
    /// hkxpack writes an empty element and the file has to have one, or the array comes back short.
    private static bool ResizeText(PackfileImage image, PackfileObjects objects,
                                   PackfileObjects.Instance instance, Change change)
    {
        var data = image.Section("__data__");
        if (data == null) return false;

        if (objects.FieldAt(instance, change.Field) is not int at) return false;

        var names = change.Value.Length == 0
                    ? new List<string>()
                    : change.Value.Split(TextSeparator).ToList();

        // The run this array points at today, so its element fixups can go with it. Each element is
        // a pointer with a fixup of its own, and leaving those behind aims them at a run nothing
        // refers to any more. Our own reader looks entries up by source and would never notice; the
        // game fixes up every entry in the table on load, so it would be writing into space that has
        // been reused by whatever the file is compacted into next.
        var old = objects.ArrayAt(at);
        if (old != null && old.Count > 0)
        {
            var keep = data.Locals()
                           .Where(l => l.Source < old.At || l.Source >= old.At + old.Count * 8)
                           .ToList();
            data.SetLocals(keep);
        }

        // A run of zero elements has no run at all, the same as an empty array of pointers: a fixup
        // aiming at offset zero would point the array at the start of the section.
        if (names.Count == 0)
        {
            data.SetLocal(at, -1);
            BitConverter.GetBytes(0).CopyTo(data.Data, at + 8);
            uint was = BitConverter.ToUInt32(data.Data, at + 12);
            BitConverter.GetBytes(was & 0xC0000000u).CopyTo(data.Data, at + 12);
            return true;
        }

        // The text first, so the run can be pointed straight at it. Aligned the way every run is,
        // since the run of pointers is a run like any other.
        var wrote = new List<int>(names.Count);
        foreach (string name in names)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            var withEnd = new byte[bytes.Length + 1];
            bytes.CopyTo(withEnd, 0);
            wrote.Add(data.AppendData(withEnd));
        }

        data.AlignData(NativeAppend.Alignment);
        int run = data.AppendData(new byte[names.Count * 8]);

        data.SetLocal(at, run);
        for (int e = 0; e < names.Count; e++) data.SetLocal(run + e * 8, wrote[e]);

        BitConverter.GetBytes(names.Count).CopyTo(data.Data, at + 8);
        uint capacity = BitConverter.ToUInt32(data.Data, at + 12);
        BitConverter.GetBytes((capacity & 0xC0000000u) | (uint)names.Count).CopyTo(data.Data, at + 12);
        return true;
    }

    /// Writes an array of structs at a new length.
    ///
    /// The same three moves `Resize` makes for an array of pointers. A run of the new length goes on
    /// the end of the section, the array's own pointer is aimed at it and the count beside it is
    /// rewritten, so nothing already in the file moves and no offset anybody holds goes stale. The
    /// old run is left where it is, unreferenced, which is what an unreferenced run already looks
    /// like in this format.
    ///
    /// What an array of pointers does not have to do is carry anything over. An element here is a
    /// struct with fields of its own, and some of them are things this cannot spell: a string, a
    /// pointer, an array. So the elements the file already had are copied across as bytes rather
    /// than rebuilt from the document, and any fixup naming a byte inside them is moved with them.
    /// A member the caller changed is then written over the copy.
    ///
    /// The fixups belonging to elements the resize drops are dropped with them. Left behind they
    /// would aim at a run nothing refers to any more, which is a pointer the game would still
    /// follow and fix up on load.
    private static void Regrow(PackfileImage image, PackfileObjects objects,
                               PackfileObjects.Instance instance, Change change)
    {
        var data = image.Section("__data__")
            ?? throw new InvalidOperationException("this file has no data section");

        if (objects.FieldAt(instance, change.Field) is not int at)
            throw new InvalidOperationException(
                $"No offset for {change.ClassName}.{change.Field}, so nothing was written.");

        string elementClass = ElementClass(change.ClassName, change.Field)
            ?? throw new InvalidOperationException(
                $"{change.ClassName}.{change.Field} does not say what class its elements are.");

        int stride = HavokClassTypes.Shipped[elementClass]?.Size ?? 0;
        if (stride <= 0)
            throw new InvalidOperationException(
                $"No size for {elementClass}, so {change.Field} was not resized.");

        int count = int.Parse(change.Value, CultureInfo.InvariantCulture);
        var old = objects.ArrayAt(at);
        int wasAt = old?.At ?? 0, had = old?.Count ?? 0;
        int carried = Math.Min(had, count);

        int run = -1;
        if (count > 0)
        {
            // Sixteen, because a struct holding a vector is read with instructions that require the
            // alignment. Every element after the first is on the same boundary already, since a
            // class that needs it is padded to it.
            data.AlignData(16);
            run = data.AppendData(new byte[count * stride]);
            if (carried > 0) Array.Copy(data.Data, wasAt, data.Data, run, carried * stride);
        }

        data.SetLocal(at, run);
        Move(data.Locals().ToList(), data.SetLocals, l => l.Source, (l, s) => (s, l.Destination));
        Move(data.Globals().ToList(), data.SetGlobals, g => g.Source, (g, s) => (s, g.Section, g.Destination));

        BitConverter.GetBytes(count).CopyTo(data.Data, at + 8);
        uint capacity = BitConverter.ToUInt32(data.Data, at + 12);
        BitConverter.GetBytes((capacity & 0xC0000000u) | (uint)count).CopyTo(data.Data, at + 12);

        // Both tables get the same treatment: an entry inside a carried element follows it to the
        // new run, an entry inside a dropped one goes, and everything else is left alone.
        void Move<T>(List<T> entries, Action<IEnumerable<T>> write, Func<T, int> sourceOf,
                     Func<T, int, T> moved)
        {
            if (had == 0) return;

            var kept = new List<T>();
            foreach (var entry in entries)
            {
                int source = sourceOf(entry);
                if (source < wasAt || source >= wasAt + had * stride) { kept.Add(entry); continue; }
                if (source >= wasAt + carried * stride) continue;

                kept.Add(moved(entry, source - wasAt + run));
            }
            write(kept);
        }
    }

    /// Writes one number inside one element of an array of structs.
    ///
    /// The elements sit in their own run somewhere else in the section, not inside the object, so
    /// this is the array's own pointer followed to that run and then a stride into it. Nothing moves
    /// and nothing changes length: it is the same write as any other fixed width value, aimed
    /// somewhere the object's own class does not describe.
    private static bool WriteInElement(PackfileImage image, PackfileObjects objects,
                                       PackfileObjects.Instance instance, Change change)
    {
        var data = image.Section("__data__");
        if (data == null) return false;

        if (objects.FieldAt(instance, change.Field) is not int header) return false;

        var array = objects.ArrayAt(header);
        if (array == null || change.Element < 0 || change.Element >= array.Count) return false;

        string? elementClass = ElementClass(change.ClassName, change.Field);
        if (elementClass == null) return false;

        int stride = HavokClassTypes.Shipped[elementClass]?.Size ?? 0;
        if (stride <= 0) return false;

        var found = StructMember(elementClass, change.Member);
        if (found == null) return false;

        if (found.Value.VType == "TYPE_POINTER")
            return RepointAt(image, objects, data,
                             array.At + change.Element * stride + found.Value.Offset, change.Value);

        string type = Narrow(found.Value.VType);
        if (type.Length == 0) return false;

        int at = array.At + change.Element * stride + found.Value.Offset;
        int width = type switch
        {
            "int8" or "uint8" or "bool" or "enum" => 1,
            "int16" or "uint16" => 2,
            _ => 4,
        };
        if (at < 0 || at + width > data.Data.Length) return false;

        if (type == "real")
        {
            BitConverter.GetBytes(AsFloat(change.Value)).CopyTo(data.Data, at);
            return true;
        }

        long number = AsLong(change.Value, type);
        for (int i = 0; i < width; i++) data.Data[at + i] = (byte)(number >> (8 * i));
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

    /// Where a member inside a struct array element sits, and whether it is one that can be written.
    ///
    /// The class table is what knows the element's class and the offsets inside it, since the layout
    /// table used elsewhere here describes objects rather than the structs written inside them. The
    /// path can go more than one level deep: `min.value` on a `hkbVariableBounds` is a
    /// `hkbVariableValue` and then the number in it.
    private static (int Offset, string VType, string Owner)? StructMember(string elementClass,
                                                                          string path)
    {
        var types = HavokClassTypes.Shipped;
        int offset = 0;
        string owner = elementClass;

        foreach (string step in path.Split('.'))
        {
            if (!types.Knows(owner)) return null;

            var member = types.Members(owner).FirstOrDefault(m => m.Name == step);
            if (member == null) return null;

            offset += member.Offset;

            if (member.VType == "TYPE_STRUCT")
            {
                if (member.CType == null) return null;
                owner = member.CType;
                continue;
            }

            return (offset, member.VType, owner);
        }

        return null;
    }

    /// The class of an array's elements, which the layout table does not carry.
    private static string? ElementClass(string className, string arrayField) =>
        HavokClassTypes.Shipped.Members(className).FirstOrDefault(m => m.Name == arrayField)?.CType;

    /// Whether one member inside a struct array element can be written where it sits, and whether
    /// the value given for it is really of that type.
    private static string? StructElementWritable(HavokClasses classes, string className,
                                                 string arrayField, string member, string value)
    {
        string? elementClass = ElementClass(className, arrayField);
        if (elementClass == null)
            return $"{className}.{arrayField} does not say what class its elements are";

        var found = StructMember(elementClass, member);
        if (found == null)
            return $"{elementClass}.{member} is not a member this build can place";

        // A pointer inside an element is a fixup, not a value, so it is written by moving the entry
        // that names it rather than by putting bytes anywhere. This is where a transition keeps the
        // effect it plays, and leaving it out meant a blending transition effect could not be
        // detached, which meant it could not be deleted.
        if (found.Value.VType == "TYPE_POINTER")
            return IsReferenceValue(value)
                ? null
                : $"{elementClass}.{member} was set to '{value}', which is neither an object id nor null";

        string type = Narrow(found.Value.VType);
        if (type.Length == 0)
            return $"{elementClass}.{member} is a {found.Value.VType}, which is not written in " +
                   "place yet";

        if (!Parses(value, type))
            return $"{elementClass}.{member} was set to '{value}', which is not a {type}";

        return null;
    }

    /// The class table's spelling of a type, in the words the writers here use. Empty for anything
    /// that is not a fixed width number, which is everything that would move what follows it.
    private static string Narrow(string vtype) => vtype switch
    {
        "TYPE_REAL" => "real",
        "TYPE_INT32" => "int32",
        "TYPE_UINT32" => "uint32",
        "TYPE_INT16" => "int16",
        "TYPE_UINT16" => "uint16",
        "TYPE_INT8" or "TYPE_CHAR" => "int8",
        "TYPE_UINT8" => "uint8",
        "TYPE_BOOL" => "bool",
        "TYPE_ENUM" or "TYPE_FLAGS" => "enum",
        _ => "",
    };

    /// How many elements a struct array holds, under a key no hkparam can have.
    private const string CountKey = "#count";

    /// One element of a struct array, as a key per member, walking into members that are themselves
    /// written as an object. `hkbVariableBounds` holds its min and its max that way, each an
    /// `hkbVariableValue` with a single number inside it, so nothing here is reachable without the
    /// walk.
    private static void Flatten(Dictionary<string, string> fields, string path, XElement element)
    {
        foreach (var p in element.Elements("hkparam"))
        {
            string? name = p.Attribute("name")?.Value;
            if (name == null) continue;

            var inner = p.Elements("hkobject").ToList();
            if (inner.Count == 1) { Flatten(fields, $"{path}.{name}", inner[0]); continue; }

            // An array inside an array element. Left as its joined text, which means a change in one
            // is refused rather than written, and refusing is the honest answer until it is done.
            fields[$"{path}.{name}"] = (p.Value ?? "").Trim();
        }
    }

    /// Objects grouped by class in document order, each as its simple named values.
    ///
    /// Only the file's own objects. hkxpack writes a struct held inside another object as an
    /// `hkobject` too, so walking every one of them counts things the file has no object for: a
    /// behaviour with no `hkbVariableValue` object in it appears to hold hundreds, because every
    /// bound carries two inline. The file's objects are the ones with an id, `name="#90"`, and an
    /// inline struct is named after the field it sits in or not named at all.
    ///
    /// This never showed before because a change inside an inline struct was refused before anything
    /// tried to write it. Now that those changes are written, counting them would send a value at an
    /// object that does not exist.
    private static Dictionary<string, List<Dictionary<string, string>>> ByClass(
        string xml, ISet<string>? skipping = null)
    {
        var byClass = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.Ordinal);
        if (xml.Length == 0) return byClass;

        foreach (var element in XDocument.Parse(xml).Descendants("hkobject"))
        {
            string? className = element.Attribute("class")?.Value;
            if (className == null) continue;

            string? id = element.Attribute("name")?.Value;
            if (id == null || id.Length < 2 || id[0] != '#' || !id[1..].All(char.IsAsciiDigit))
                continue;

            // Objects the edit deleted are left out of the before picture, so what remains lines up
            // with the after picture object for object. Without this a deletion reads as every
            // object of that class after the hole having changed into its neighbour.
            if (skipping != null && skipping.Contains(id)) continue;

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);

            // Under a key no hkparam can have, because a param name never starts with a hash. The id
            // is what says where an added object will sit once it is written, and it is checked
            // rather than trusted.
            fields[IdKey] = element.Attribute("name")?.Value ?? "";

            foreach (var p in element.Elements("hkparam"))
            {
                string? name = p.Attribute("name")?.Value;
                if (name == null) continue;

                // An array of structs is not one value. Kept whole it is a blob of every element's
                // text joined together, so a single number changing inside it reads as the whole
                // field changing and there is nothing left to say which element or which member.
                // Split into a key per member instead, `variableBounds[2].min.value`, and the
                // comparison below finds the one number that moved without knowing anything about
                // arrays.
                var elements = p.Elements("hkobject").ToList();
                if (elements.Count > 0)
                {
                    fields[name + CountKey] = elements.Count.ToString();
                    for (int e = 0; e < elements.Count; e++)
                        Flatten(fields, $"{name}[{e}]", elements[e]);
                    continue;
                }

                // An array of strings, which is how a file lists its events and its variables. Kept
                // as its joined text it cannot be told from one long string, and the count is what
                // says whether a name was added rather than renamed. The elements are their own tags
                // so they are read as tags: splitting the text would guess wrongly about a name with
                // a space in it, and six values in the corpus carry one.
                // Only when there are real elements. Testing for a numelements attribute instead
                // caught every array written as inline text, which is every array of pointers, and
                // quietly reduced `states` to an empty list of names.
                //
                // An array of strings that is empty needs nothing here: it reads as the empty value
                // it already was, and growing it from nothing is a value change rather than a length
                // change, which the comparison already routes to the same writer.
                var strings = p.Elements("hkcstring").ToList();
                if (strings.Count > 0)
                {
                    fields[name + CountKey] = strings.Count.ToString();
                    fields[name] = string.Join(TextSeparator, strings.Select(t => t.Value));
                    continue;
                }

                fields[name] = (p.Value ?? "").Trim();
            }

            if (!byClass.TryGetValue(className, out var list)) byClass[className] = list = new();
            list.Add(fields);
        }

        return byClass;
    }
}
