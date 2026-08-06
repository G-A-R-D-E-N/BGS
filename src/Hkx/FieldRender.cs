using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// A field, out of the bytes, written the way hkxpack writes it.
//
// Kept here rather than in the checking tool on purpose. The tool's job is to set our reading of a
// file beside hkxpack's reading of the same file, and that says nothing about the window unless the
// window is reading through the same code. One renderer, two callers.
//
// What it will not do is answer approximately. A field it cannot render returns null, and the caller
// decides what to do about that: the checker counts it, the window falls back to hkxpack for that
// one field. Neither of them is handed a number where a name belongs.
public static class FieldRender
{
    /// How a reference is written. The caller supplies it because the two callers spell an object
    /// differently: the window uses the id the rest of it is keyed on, the checker uses a position.
    public delegate string Reference(PackfileObjects.Instance? target, bool wasNull);

    /// The value, or null when this is not a field we can read.
    ///
    /// `expected` is hkxpack's own text for the same field, when the caller has it. It is used for
    /// one thing: an enum whose value has no name can still be compared as a number if that is what
    /// hkxpack printed, and only the caller knows whether it did.
    public static string? Render(PackfileObjects objects, PackfileObjects.Instance instance,
                                 HavokClasses.Member member, Reference reference,
                                 string? expected = null)
    {
        if (member.Type.StartsWith("enum of", StringComparison.Ordinal) ||
            member.Type.StartsWith("flags of", StringComparison.Ordinal))
        {
            long? value = Number(objects, instance, member);
            if (value == null) return null;

            string? name = HavokEnums.Shipped.Name(HavokEnums.Key(member), value.Value);
            // Both, because hkxpack prints whichever it feels like: a name when it has one for the
            // exact value, and the bare number when the value is a combination of flags. Carrying
            // the number as well as the name lets a comparison meet it either way.
            if (name != null) return $"{value}:{name}";

            // With no name of our own the number is still the whole value, when that is what the
            // other side printed. It is only unreadable when hkxpack has a name and we do not.
            return expected == null || long.TryParse(expected, out _) ? value.ToString() : null;
        }

        switch (member.Type)
        {
            case "real": return objects.ReadFloat(instance, member.Name)?.ToString("R");
            case "stringptr":
            case "cstring": return objects.ReadString(instance, member.Name) ?? "∅";

            case "bool" or "int8" or "uint8" or "int16" or "uint16" or "int32" or "uint32":
                return Narrow(objects.ReadInt(instance, member.Name), member.Type);
            case "ulong":
            case "uint64": return objects.ReadULong(instance, member.Name)?.ToString();

            case "vector4":
            case "quaternion": return Floats(objects.ReadFloats(instance, member.Name, 4));
            case "qstransform": return Floats(objects.ReadFloats(instance, member.Name, 12));

            case "pointer":
            case "pointer of struct":
            {
                var target = objects.ReadRef(instance, member.Name, out bool wasNull);
                return reference(target, wasNull);
            }

            case "array of pointer":
            {
                var targets = objects.ReadRefArray(instance, member.Name);
                return targets == null
                    ? null
                    : List(targets.Count, targets.Select(t => reference(t, t == null)));
            }

            case "array of stringptr":
            {
                var values = objects.ReadStringArray(instance, member.Name);
                return values == null ? null : List(values.Count, values.Select(v => v ?? "∅"));
            }

            // Only the count. The class dump does not name the class of a struct written inline, so
            // there is nothing to read the elements with, and a count is not a reading of them.
            case "array of struct":
            {
                var array = objects.ReadArray(instance, member.Name);
                return array == null ? null : List(array.Count, "structs");
            }

            case "array of vector4":
            case "array of quaternion": return Grouped(objects, instance, member.Name, 16, 4);
            case "array of matrix4": return Grouped(objects, instance, member.Name, 64, 16);
            case "array of qstransform": return Grouped(objects, instance, member.Name, 48, 12);

            case "array of uint8":
                return Listed(objects.ReadValueArray(instance, member.Name, 1, (b, at) => b[at]));
            case "array of int8":
                return Listed(objects.ReadValueArray(instance, member.Name, 1, (b, at) => (sbyte)b[at]));
            case "array of real":
                return Listed(objects.ReadValueArray(instance, member.Name, 4, BitConverter.ToSingle));
            case "array of int16":
                return Listed(objects.ReadValueArray(instance, member.Name, 2, BitConverter.ToInt16));
            case "array of uint16":
                return Listed(objects.ReadValueArray(instance, member.Name, 2, BitConverter.ToUInt16));
            case "array of int32":
                return Listed(objects.ReadValueArray(instance, member.Name, 4, BitConverter.ToInt32));
            case "array of uint32":
                return Listed(objects.ReadValueArray(instance, member.Name, 4, BitConverter.ToUInt32));

            default: return null;
        }
    }

    /// Whether a field is one the window shows as a single box. An array of anything is not: hkxpack
    /// writes it over several lines and the window has never offered it for editing.
    public static bool IsOneValue(HavokClasses.Member member) =>
        !member.Type.StartsWith("array of", StringComparison.Ordinal) &&
        member.Type != "struct";

    /// An enum's number, at whatever width the field is. Signed where the type is: `enum of int8`
    /// holding 0xFF is -1, and looking up 255 would miss the entry.
    public static long? Number(PackfileObjects objects, PackfileObjects.Instance instance,
                               HavokClasses.Member member)
    {
        int? whole = objects.ReadInt(instance, member.Name);
        if (whole == null) return null;

        return member.Type switch
        {
            "enum of int8" => (sbyte)whole.Value,
            "enum of uint8" => (byte)whole.Value,
            "enum of int16" or "flags of int16" => (short)whole.Value,
            "enum of uint16" or "flags of uint16" => (ushort)whole.Value,
            "enum of uint32" or "flags of uint32" => (uint)whole.Value,
            _ => whole.Value,
        };
    }

    /// The name on its own, without the number in front. What a person reads.
    public static string Plain(string rendered)
    {
        int colon = rendered.IndexOf(':');
        return colon > 0 && long.TryParse(rendered[..colon], out _) ? rendered[(colon + 1)..] : rendered;
    }

    private static string? Grouped(PackfileObjects objects, PackfileObjects.Instance instance,
                                   string field, int stride, int floats)
    {
        var array = objects.ReadArray(instance, field);
        if (array == null) return null;

        var all = objects.ReadValueArray(instance, field, stride,
                                         (b, at) => Enumerable.Range(0, floats)
                                                              .Select(i => BitConverter.ToSingle(b, at + i * 4))
                                                              .ToArray());
        return all == null ? null : List(array.Count, all.Select(e => Floats(e)!));
    }

    /// A field narrower than four bytes still reads as four, so the extra has to be masked off or a
    /// one byte flag reports whatever its neighbours happen to hold.
    private static string? Narrow(int? value, string type)
    {
        if (value is not int raw) return null;
        return type switch
        {
            "bool" => ((raw & 0xFF) != 0).ToString().ToLowerInvariant(),
            // Masked rather than sign extended, both of them. hkxpack prints the bytes as they
            // sit, so an animationBindingIndex of 0xFFFF is 65535 there and not -1, and matching it
            // is what 53,956 compared values were checked against.
            "int8" or "uint8" => (raw & 0xFF).ToString(),
            "int16" or "uint16" => (raw & 0xFFFF).ToString(),
            _ => raw.ToString(),
        };
    }

    public static string? Floats(float[]? values) =>
        values == null ? null : "(" + string.Join(" ", values.Select(v => v.ToString("R"))) + ")";

    private static string? Listed<T>(IReadOnlyList<T>? values) =>
        values == null ? null : List(values.Count, values.Select(v => v?.ToString() ?? ""));

    public static string List(int count, IEnumerable<string> tokens) =>
        $"[{count}: {string.Join("|", tokens)}]";

    /// An empty array has nothing in it to describe, however unreadable its elements would be, so it
    /// reads the same either way rather than as a count with a word after it.
    public static string List(int count, string what) => count == 0 ? "[0: ]" : $"[{count}: {what}]";
}
