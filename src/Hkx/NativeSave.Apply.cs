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
    private static PackfileObjects.Instance Resolve(PackfileObjects objects, Change change)
    {
        if (change.Id <= 0)
            throw new InvalidOperationException(
                $"{change} was planned without a stable object id, so nothing was written.");

        int at = change.Id - NativeGraphModel.FirstId;
        if (at < 0 || at >= objects.Instances.Count ||
            objects.Instances[at].ClassName != change.ClassName)
            throw new InvalidOperationException(
                $"{change} does not correspond to anything in the file, so nothing was written.");

        return objects.Instances[at];
    }

    public static byte[] Apply(string hkxPath, Plan plan, HavokClasses? classes = null)
        => Apply(InputFilePolicy.ReadHkx(hkxPath), plan, classes);

    public static byte[] Apply(byte[] source, Plan plan, HavokClasses? classes = null)
    {
        if (!plan.Possible)
            throw new InvalidOperationException("This edit cannot be written in place: " + plan.Refusal);

        var image = PackfileImage.Read(source);

        NativeLayout.RequireWritable(image);

        var objects = new PackfileObjects(image, classes);

        var mismatched = HavokClassTypes.Shipped.SignatureProblems(objects.ClassNames());
        if (mismatched.Count > 0)
            throw new InvalidOperationException(
                "This file's classes are not the ones this build describes, so nothing was written " +
                $"into its bytes: {mismatched[0]}" +
                (mismatched.Count > 1 ? $", and {mismatched.Count - 1} more like it." : "."));

        RequireUnaliasedArrays(image, objects, plan);

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

        bool grew = false;
        foreach (var change in plan.Changes.Where(c => c.Grow))
        {
            var instance = Resolve(objects, change);
            Regrow(image, objects, instance, change);
            grew = true;
        }

        if (grew)
        {
            objects = new PackfileObjects(image, classes);
        }

        var targets = new List<(Change Change, PackfileObjects.Instance Instance)>();
        foreach (var change in plan.Changes.Where(c => !c.Added))
            targets.Add((change, Resolve(objects, change)));

        foreach (var (change, instance) in targets)
        {
            if (change.Grow) continue;

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
                             ? WriteWideInteger(objects, instance, change, member.Type, image)
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

        WriteArrayCount(data, at, elements.Length);

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
                                         Change change, string type, PackfileImage image)
    {
        var data = image.Section("__data__");
        if (data == null) return false;

        if (objects.FieldAt(instance, change.Field) is not int at) return false;
        if (at < 0 || at + 8 > data.Data.Length) return false;

        try { NumberCodecs.WriteScalar(data.Data, at, type, change.Value); }
        catch (InvalidOperationException) { return false; }
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
            WriteArrayCount(data, at, 0);
            return true;
        }

        data.AlignData(NativeAppend.Alignment);
        data.SetLocal(at, data.AppendData(run));

        WriteArrayCount(data, at, count);
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
            WriteArrayCount(data, at, 0);
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

        WriteArrayCount(data, at, names.Count);
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

        WriteArrayCount(data, at, count);

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

        if (found.Value.VType is "TYPE_STRINGPTR" or "TYPE_CSTRING")
            return objects.WriteStringAt(where, change.Value);

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
            if (where < 0 || where + 8 > data.Data.Length) return false;

            try { NumberCodecs.WriteScalar(data.Data, where, Spelled(found.Value.VType), change.Value); }
            catch (InvalidOperationException) { return false; }
            return true;
        }

        string type = Narrow(found.Value.VType);
        if (type == "enum")
            type = EnumStorage(found.Value.VSub);
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

        string codecType = NumberCodecs.Underlying(type);
        int width = codecType switch
        {
            "int8" or "uint8" or "bool" or "enum" => 1,
            "int16" or "uint16" or "half" => 2,
            _ => 4,
        };
        if (at.Value + width > data.Data.Length) return false;

        try { NumberCodecs.WriteScalar(data.Data, at.Value, type, value); }
        catch (InvalidOperationException) { return false; }
        return true;
    }

    private static bool Parses(string value, string type) => NumberCodecs.Parses(value, type);

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

    private static (int Offset, string VType, string VSub, string Owner)? StructMember(string elementClass,
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

            return (offset, member.VType, member.VSub, owner);
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

        if (found.Value.VType is "TYPE_STRINGPTR" or "TYPE_CSTRING")
            return value.Contains('\0') ? $"{elementClass}.{member} contains a null terminator" : null;

        int wide = WideFloats(Spelled(found.Value.VType));
        if (wide > 0)
            return Bracketed(value, wide) != null
                ? null
                : $"{elementClass}.{member} was set to '{value}', which is not {wide} number(s) in brackets";

        if (IsWideInteger(Spelled(found.Value.VType)))
            return NumberCodecs.Parses(value.Trim(), Spelled(found.Value.VType))
                ? null
                : $"{elementClass}.{member} was set to '{value}', which is not a whole number";

        string type = Narrow(found.Value.VType);
        if (type == "enum")
            type = EnumStorage(found.Value.VSub);
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

    private static string EnumStorage(string vsub) => vsub switch
    {
        "TYPE_INT8" => "int8",
        "TYPE_UINT8" => "uint8",
        "TYPE_INT16" => "int16",
        "TYPE_UINT16" => "uint16",
        "TYPE_INT32" => "int32",
        "TYPE_UINT32" => "uint32",
        _ => "enum",
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
        string xml, HavokClasses classes)
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
                if (ValueElement(classes.Field(className, name)?.Type ?? "") > 0 && p.Attribute("numelements") is { } count)
                    fields[name + CountKey] = count.Value;
            }

            if (!byClass.TryGetValue(className, out var list)) byClass[className] = list = new();
            list.Add(fields);
        }

        return byClass;
    }
}
