using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using OpenCommonwealth.Services;

namespace OpenCommonwealth.Services.Hkx;

public static partial class NativeSave
{
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
