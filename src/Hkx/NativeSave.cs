using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;
using System.Xml.Linq;

namespace OpenCommonwealth.Services.Hkx;























public static class NativeSave
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
                                bool Grow = false)
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








    private static bool IsReference(string type) =>
        type.StartsWith("pointer of", StringComparison.Ordinal) || type == "pointer";











    private static bool IsPointerArray(string type) => type == "array of pointer";













    private static int WideFloats(string type) => type switch
    {
        "vector4" or "quaternion" => 4,
        "qstransform" or "matrix3" or "rotation" => 12,
        "transform" or "matrix4" => 16,
        _ => 0,
    };


    private static bool IsWideInteger(string type) =>
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

    private static bool IsTextArray(string type) =>
        type == "array of stringptr" || type == "array of cstring";








    private static int ValueElement(string type) => type switch
    {
        "array of real" or "array of int32" or "array of uint32" => 4,
        "array of int16" or "array of uint16" => 2,
        "array of int8" or "array of uint8" or "array of bool" or "array of char" => 1,
        "array of int64" or "array of uint64" or "array of ulong" => 8,
        _ => 0,
    };



    private static byte[]? Numbers(string value, string type, int width)
    {
        var tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var run = new byte[tokens.Length * width];

        for (int i = 0; i < tokens.Length; i++)
        {
            if (type == "array of real")
            {
                if (!float.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture,
                                    out float f) || float.IsNaN(f) || float.IsInfinity(f))
                    return null;
                BitConverter.GetBytes(f).CopyTo(run, i * 4);
                continue;
            }

            string token = tokens[i];
            if (type == "array of bool")
            {
                if (token is "true" or "1") { run[i] = 1; continue; }
                if (token is "false" or "0") { run[i] = 0; continue; }
                return null;
            }

            if (!long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n))
                return null;

            var bytes = BitConverter.GetBytes(n);
            for (int b = 0; b < width; b++) run[i * width + b] = bytes[b];
        }

        return run;
    }






    private const char TextSeparator = '\0';


    private const string IdKey = "#id";


    private static bool IsReferenceValue(string value) =>
        value == "null" ||
        (value.Length > 1 && value[0] == '#' && value[1..].All(char.IsAsciiDigit));





    public static Plan Compare(string originalXml, string editedXml, HavokClasses? classes = null)
    {
        classes ??= HavokClasses.Shipped;




        var deleted = Deleted(originalXml, editedXml);

        var before = ByClass(originalXml, deleted.Text);
        var after = ByClass(editedXml);
        var changes = new List<Change>();




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




                for (int k = 0; k < originals.Count; k++)
                    if (originals[k][IdKey] != edited[k][IdKey])
                        return new Plan(changes,
                            $"the {className} objects were renumbered, so nothing can be matched up");

                for (int k = originals.Count; k < edited.Count; k++)
                    changes.Add(new Change(className, k, "", edited[k][IdKey], Added: true));
            }

            var layout = classes.Members(className).ToDictionary(m => m.Name, m => m.Type,
                                                                 StringComparer.Ordinal);




            string? Consider(int i, string field, string now)
            {




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

                    changes.Add(new Change(className, i, arrayField, now, Element: element,
                                           Member: member));
                    return null;
                }

                if (!layout.TryGetValue(field, out string? type))
                    return $"{className}.{field} changed, and we have no byte layout for it";




                if (IsTextArray(type))
                {
                    changes.Add(new Change(className, i, field, now, Text: true, Array: true));
                    return null;
                }



                if (ValueElement(type) is int width and > 0)
                {
                    if (Numbers(now, type, width) == null)
                        return $"{className}.{field} was set to something that is not a list of " +
                               $"{type[("array of ").Length..]}";

                    changes.Add(new Change(className, i, field, now, Array: true));
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

                if (WideFloats(type) is int floats and > 0)
                {
                    if (Bracketed(now, floats) == null)
                        return $"{className}.{field} was set to '{now}', which is not {floats} " +
                               "number(s) in brackets";

                    changes.Add(new Change(className, i, field, now));
                    return null;
                }

                if (IsWideInteger(type))
                {
                    if (!ulong.TryParse(now.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        return $"{className}.{field} was set to '{now}', which is not a {type}";

                    changes.Add(new Change(className, i, field, now));
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






                var resized = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (field, was) in originals[i])
                {
                    if (!field.EndsWith(CountKey, StringComparison.Ordinal)) continue;
                    if (edited[i].TryGetValue(field, out string? now) &&
                        string.Equals(was, now, StringComparison.Ordinal)) continue;

                    resized.Add(field[..^CountKey.Length]);
                }



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
                                   Dictionary<string, string> layout, int index,
                                   Dictionary<string, string> before, Dictionary<string, string> after,
                                   string arrayField)
    {
        layout.TryGetValue(arrayField, out string? arrayType);




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

            fill.Add(new Change(className, index, arrayField, value, Element: element, Member: member));
        }

        changes.Add(new Change(className, index, arrayField, now.ToString(CultureInfo.InvariantCulture),
                               Element: had, Grow: true));
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



    public static byte[] Apply(string hkxPath, Plan plan, HavokClasses? classes = null)
        => Apply(File.ReadAllBytes(hkxPath), plan, classes);

    public static byte[] Apply(byte[] source, Plan plan, HavokClasses? classes = null)
    {
        if (!plan.Possible)
            throw new InvalidOperationException("This edit cannot be written in place: " + plan.Refusal);

        var image = PackfileImage.Read(source);
        var objects = new PackfileObjects(image, classes);







        var mismatched = HavokClassTypes.Shipped.SignatureProblems(objects.ClassNames());
        if (mismatched.Count > 0)
            throw new InvalidOperationException(
                "This file's classes are not the ones this build describes, so nothing was written " +
                $"into its bytes: {mismatched[0]}" +
                (mismatched.Count > 1 ? $", and {mismatched.Count - 1} more like it." : "."));


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





            int nameAt = NativeAppend.NameOffset(names, add.ClassName, layout.Signature);





            string expected = "#" + (NativeGraphModel.FirstId + objects.Instances.Count + adding);
            if (add.Value != expected)
                throw new InvalidOperationException(
                    $"The new {add.ClassName} is {add.Value} in the document and would be {expected} " +
                    "in the file, so nothing was written.");

            data.AddVirtual(data.AppendObject(new byte[size]), image.Sections.IndexOf(names), nameAt);
            adding++;
        }



        if (adding > 0) objects = new PackfileObjects(image, classes);

        var byClass = objects.Instances.GroupBy(i => i.ClassName)
                                       .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);







        foreach (var group in plan.Changes.Where(c => !c.Added).GroupBy(c => c.ClassName))
        {
            int inFile = byClass.TryGetValue(group.Key, out var all) ? all.Count : 0;
            if (group.Max(c => c.Index) >= inFile)
                throw new InvalidOperationException(
                    $"The file holds {inFile} {group.Key} objects, fewer than the edit expects, so " +
                    "nothing was written rather than guessing which one was meant.");
        }




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
                         : change.Array && ValueElement(member.Type) > 0
                             ? ResizeValues(image, objects, instance, change, member.Type)
                         : change.Array ? Resize(image, objects, instance, change)
                         : WideFloats(member.Type) > 0
                             ? WriteWide(objects, instance, change, WideFloats(member.Type), image)
                         : IsWideInteger(member.Type)
                             ? WriteWideInteger(objects, instance, change, image)
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






        FixupOrder.Reorder(image);




        if (plan.Gone.Count > 0) NativeRemove.Delete(image, plan.Gone);

        return image.Rebuild();
    }








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






    private static bool WriteWide(PackfileObjects objects, PackfileObjects.Instance instance,
                                  Change change, int floats, PackfileImage image)
    {
        var data = image.Section("__data__");
        if (data == null) return false;

        if (objects.FieldAt(instance, change.Field) is not int at) return false;
        if (Bracketed(change.Value, floats) is not float[] values) return false;
        if (at < 0 || at + floats * 4 > data.Data.Length) return false;

        for (int i = 0; i < floats; i++)
            BitConverter.GetBytes(values[i]).CopyTo(data.Data, at + i * 4);

        return true;
    }


    private static bool WriteWideInteger(PackfileObjects objects, PackfileObjects.Instance instance,
                                         Change change, PackfileImage image)
    {
        var data = image.Section("__data__");
        if (data == null) return false;

        if (objects.FieldAt(instance, change.Field) is not int at) return false;
        if (!ulong.TryParse(change.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out ulong value))
            return false;
        if (at < 0 || at + 8 > data.Data.Length) return false;

        BitConverter.GetBytes(value).CopyTo(data.Data, at);
        return true;
    }






    private static bool ResizeValues(PackfileImage image, PackfileObjects objects,
                                     PackfileObjects.Instance instance, Change change, string type)
    {
        var data = image.Section("__data__");
        if (data == null) return false;

        if (objects.FieldAt(instance, change.Field) is not int at) return false;

        int width = ValueElement(type);
        if (width <= 0) return false;
        if (Numbers(change.Value, type, width) is not byte[] run) return false;

        int count = run.Length / width;

        if (count == 0)
        {
            data.SetLocal(at, -1);
            BitConverter.GetBytes(0).CopyTo(data.Data, at + 8);
            uint none = BitConverter.ToUInt32(data.Data, at + 12);
            BitConverter.GetBytes(none & 0xC0000000u).CopyTo(data.Data, at + 12);
            return true;
        }

        data.AlignData(NativeAppend.Alignment);
        data.SetLocal(at, data.AppendData(run));

        BitConverter.GetBytes(count).CopyTo(data.Data, at + 8);
        uint capacity = BitConverter.ToUInt32(data.Data, at + 12);
        BitConverter.GetBytes((capacity & 0xC0000000u) | (uint)count).CopyTo(data.Data, at + 12);
        return true;
    }















    private static bool ResizeText(PackfileImage image, PackfileObjects objects,
                                   PackfileObjects.Instance instance, Change change)
    {
        var data = image.Section("__data__");
        if (data == null) return false;

        if (objects.FieldAt(instance, change.Field) is not int at) return false;

        var names = change.Value.Length == 0
                    ? new List<string>()
                    : change.Value.Split(TextSeparator).ToList();






        var old = objects.ArrayAt(at);
        if (old != null && old.Count > 0)
        {
            var keep = data.Locals()
                           .Where(l => l.Source < old.At || l.Source >= old.At + old.Count * 8)
                           .ToList();
            data.SetLocals(keep);
        }



        if (names.Count == 0)
        {
            data.SetLocal(at, -1);
            BitConverter.GetBytes(0).CopyTo(data.Data, at + 8);
            uint was = BitConverter.ToUInt32(data.Data, at + 12);
            BitConverter.GetBytes(was & 0xC0000000u).CopyTo(data.Data, at + 12);
            return true;
        }



        var wrote = new List<int>(names.Count);
        foreach (string name in names)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            var withEnd = new byte[bytes.Length + 1];
            bytes.CopyTo(withEnd, 0);
            wrote.Add(data.AppendAligned(withEnd, PackfileSection.StringAlignment));
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







    private static bool WriteInElement(PackfileImage image, PackfileObjects objects,
                                       PackfileObjects.Instance instance, Change change)
    {
        var data = image.Section("__data__");
        if (data == null) return false;

        if (objects.FieldAt(instance, change.Field) is not int header) return false;

        string? elementClass = ElementClass(change.ClassName, change.Field);
        if (elementClass == null) return false;

        var found = StructMember(elementClass, change.Member);
        if (found == null) return false;



        bool inline = HavokClasses.Shipped.Field(change.ClassName, change.Field)?.Type == "struct";
        int start;
        if (inline)
        {
            if (change.Element != 0) return false;
            start = header;
        }
        else
        {
            var array = objects.ArrayAt(header);
            if (array == null || change.Element < 0 || change.Element >= array.Count) return false;

            int stride = HavokClassTypes.Shipped[elementClass]?.Size ?? 0;
            if (stride <= 0) return false;

            start = array.At + change.Element * stride;
        }

        int where = start + found.Value.Offset;

        if (found.Value.VType == "TYPE_POINTER")
            return RepointAt(image, objects, data, where, change.Value);

        int wide = WideFloats(Spelled(found.Value.VType));
        if (wide > 0)
        {
            if (Bracketed(change.Value, wide) is not float[] numbers) return false;
            if (where < 0 || where + wide * 4 > data.Data.Length) return false;

            for (int i = 0; i < wide; i++)
                BitConverter.GetBytes(numbers[i]).CopyTo(data.Data, where + i * 4);
            return true;
        }

        if (IsWideInteger(Spelled(found.Value.VType)))
        {
            if (!ulong.TryParse(change.Value.Trim(), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out ulong big)) return false;
            if (where < 0 || where + 8 > data.Data.Length) return false;

            BitConverter.GetBytes(big).CopyTo(data.Data, where);
            return true;
        }

        string type = Narrow(found.Value.VType);
        if (type.Length == 0) return false;

        int at = where;
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


    private static string? ElementClass(string className, string arrayField) =>
        HavokClassTypes.Shipped.Members(className).FirstOrDefault(m => m.Name == arrayField)?.CType;



    private static string? StructElementWritable(HavokClasses classes, string className,
                                                 string arrayField, string member, string value)
    {
        string? elementClass = ElementClass(className, arrayField);
        if (elementClass == null)
            return $"{className}.{arrayField} does not say what class its elements are";

        var found = StructMember(elementClass, member);
        if (found == null)
            return $"{elementClass}.{member} is not a member this build can place";





        if (found.Value.VType == "TYPE_POINTER")
            return IsReferenceValue(value)
                ? null
                : $"{elementClass}.{member} was set to '{value}', which is neither an object id nor null";




        int wide = WideFloats(Spelled(found.Value.VType));
        if (wide > 0)
            return Bracketed(value, wide) != null
                ? null
                : $"{elementClass}.{member} was set to '{value}', which is not {wide} number(s) in brackets";

        if (IsWideInteger(Spelled(found.Value.VType)))
            return ulong.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                ? null
                : $"{elementClass}.{member} was set to '{value}', which is not a whole number";

        string type = Narrow(found.Value.VType);
        if (type.Length == 0)
            return $"{elementClass}.{member} is a {found.Value.VType}, which is not written in " +
                   "place yet";

        if (!Parses(value, type))
            return $"{elementClass}.{member} was set to '{value}', which is not a {type}";

        return null;
    }



    private static string Spelled(string vtype) => vtype switch
    {
        "TYPE_VECTOR4" => "vector4",
        "TYPE_QUATERNION" => "quaternion",
        "TYPE_QSTRANSFORM" => "qstransform",
        "TYPE_MATRIX3" => "matrix3",
        "TYPE_ROTATION" => "rotation",
        "TYPE_TRANSFORM" => "transform",
        "TYPE_MATRIX4" => "matrix4",
        "TYPE_UINT64" => "uint64",
        "TYPE_INT64" => "int64",
        "TYPE_ULONG" => "ulong",
        _ => "",
    };



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


    private const string CountKey = "#count";





    private static void Flatten(Dictionary<string, string> fields, string path, XElement element)
    {
        foreach (var p in element.Elements("hkparam"))
        {
            string? name = p.Attribute("name")?.Value;
            if (name == null) continue;

            var inner = p.Elements("hkobject").ToList();
            if (inner.Count == 1) { Flatten(fields, $"{path}.{name}", inner[0]); continue; }



            fields[$"{path}.{name}"] = (p.Value ?? "").Trim();
        }
    }












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




            if (skipping != null && skipping.Contains(id)) continue;

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);




            fields[IdKey] = element.Attribute("name")?.Value ?? "";

            foreach (var p in element.Elements("hkparam"))
            {
                string? name = p.Attribute("name")?.Value;
                if (name == null) continue;







                var elements = p.Elements("hkobject").ToList();
                if (elements.Count > 0)
                {
                    fields[name + CountKey] = elements.Count.ToString();
                    for (int e = 0; e < elements.Count; e++)
                        Flatten(fields, $"{name}[{e}]", elements[e]);
                    continue;
                }













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
