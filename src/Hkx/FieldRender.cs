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
// It reads at an offset rather than by field name, and that is not a detail. Half the values in a
// properties panel belong to structs written inside the object, which sit at no offset the object's
// own class describes; asking for one of those by name finds a different field that happens to
// share it. `hkbStateMachine` and the `hkbEvent` written inside it both have an `id`.
//
// What it will not do is answer approximately. A field it cannot render returns null, and the caller
// decides what to do about that: the checker counts it, the window falls back to hkxpack for that
// one field. Neither of them is handed a number where a name belongs.
public static class FieldRender
{
    /// How a reference is written. The caller supplies it because the two callers spell an object
    /// differently: the window uses the id the rest of it is keyed on, the checker uses a position.
    public delegate string Reference(PackfileObjects.Instance? target, bool wasNull);

    /// How a float is spelled, for the same reason references have one: the two callers want
    /// different text for the same bits. A panel wants "0.1" because that is what somebody typed; a
    /// reading being set against hkxpack's own text wants "0.10000000149011612" because that is what
    /// is written in the file. One renderer, two spellings, chosen by whoever is asking.
    public delegate string Real(float value);

    /// The shortest text that reads back as the same float. What a person should see.
    public static readonly Real Shortest = value => value.ToString("R");

    /// The float widened to a double and written out the way Java does, which is what hkxpack puts
    /// in its XML.
    public static readonly Real LikeHkxPack = HkxNumber.Text;

    /// The value at an offset, or null when this is not a field we can read.
    ///
    /// `owner` is the class that declares the member, which an enum needs: the names of its values
    /// are declared on that class or one of its parents, and the member only carries which enum.
    ///
    /// `expected` is hkxpack's own text for the same field, when the caller has it. It is used for
    /// one thing: an enum whose value has no name can still be compared as a number if that is what
    /// hkxpack printed, and only the caller knows whether it did.
    ///
    /// `element` picks one out of a fixed length C array. `hkReal[8]` is written as eight separate
    /// fields, so each of them is this member read four bytes further along.
    public static string? Render(PackfileObjects objects, int at, string owner,
                                 HavokClassTypes.Member member, Reference reference,
                                 string? expected = null, int element = 0,
                                 HavokClassTypes? types = null, Real? real = null)
    {
        types ??= HavokClassTypes.Shipped;
        real ??= Shortest;

        if (member.VType is "TYPE_ENUM" or "TYPE_FLAGS")
        {
            long? value = Number(objects, at, member.VSub);
            if (value == null) return null;

            // The name is looked up with the signed value and printed with the unsigned one, and
            // both of those are deliberate. An enum declares `VARIABLE_TYPE_INVALID = -1`, so a
            // byte of 0xFF only finds its name signed; hkxpack prints the byte as it sits, so 0xFF
            // reads back as 255 and a comparison against -1 would call the same byte a difference.
            string? name = types.NameOf(owner, member, value.Value);
            long printed = Unsigned(value.Value, member.VSub);
            // Both, because hkxpack prints whichever it feels like: a name when it has one for the
            // exact value, and the bare number when the value is a combination of flags. Carrying
            // the number as well as the name lets a comparison meet it either way.
            if (name != null) return $"{printed}:{name}";

            // With no name of our own the number is still the whole value, when that is what the
            // other side printed. It is only unreadable when hkxpack has a name and we do not.
            return expected == null || long.TryParse(expected, out _) ? printed.ToString() : null;
        }

        // One of a fixed length array's elements. Everything below reads a single value, so the
        // offset is moved along and the rest is unchanged.
        if (member.ArrSize > 0) at += element * Width(member.VType);

        switch (member.VType)
        {
            case "TYPE_REAL": return objects.ReadFloatAt(at) is float one ? real(one) : null;
            case "TYPE_STRINGPTR":
            case "TYPE_CSTRING": return objects.ReadStringAt(at) ?? "∅";

            case "TYPE_BOOL":
            case "TYPE_CHAR":
            case "TYPE_INT8" or "TYPE_UINT8" or "TYPE_INT16" or "TYPE_UINT16"
                or "TYPE_INT32" or "TYPE_UINT32":
                // At its own width rather than as four bytes masked down. The two are the same
                // everywhere except the last bytes of a section, where the wider read runs off the
                // end and the value reads as nothing.
                return Narrow(objects.ReadNarrowAt(at, Bytes(member.VType)), member.VType);

            case "TYPE_ULONG":
            case "TYPE_INT64":
            case "TYPE_UINT64": return objects.ReadULongAt(at)?.ToString();

            case "TYPE_VECTOR4":
            case "TYPE_QUATERNION": return Floats(objects.ReadFloatsAt(at, 4), real);
            // Anything wider than four floats is written as a run of bracketed fours rather than as
            // one long bracket, which is how hkxpack writes it and how the file reads back. A
            // qstransform is three of them: `(0 0 0 1)(0 0 0 1)(1 1 1 1)`.
            case "TYPE_ROTATION":
            case "TYPE_MATRIX3": return Floats(objects.ReadFloatsAt(at, 12), real);
            case "TYPE_QSTRANSFORM": return Floats(objects.ReadFloatsAt(at, 12), real);
            case "TYPE_TRANSFORM":
            case "TYPE_MATRIX4": return Floats(objects.ReadFloatsAt(at, 16), real);

            case "TYPE_POINTER":
            {
                var target = objects.ReadRefAt(at, out bool wasNull);
                return reference(target, wasNull);
            }

            case "TYPE_ARRAY": return Array(objects, at, member, reference, real);

            // A half is two bytes of float and nothing here has ever had to read one; saying so is
            // better than printing the two bytes as though they were a number.
            default: return null;
        }
    }

    private static string? Array(PackfileObjects objects, int at, HavokClassTypes.Member member,
                                 Reference reference, Real real)
    {
        switch (member.VSub)
        {
            case "TYPE_POINTER":
            {
                var targets = objects.ReadRefArrayAt(at);
                return targets == null
                    ? null
                    : List(targets.Count, targets.Select(t => reference(t, t == null)));
            }

            case "TYPE_STRINGPTR":
            case "TYPE_CSTRING":
            {
                var values = objects.ReadStringArrayAt(at);
                return values == null ? null : List(values.Count, values.Select(v => v ?? "∅"));
            }

            // Only the count. An array of structs is written as a run of nested objects, and the
            // fields inside them are listed in their own right rather than as this one value.
            case "TYPE_STRUCT":
            {
                var array = objects.ArrayAt(at);
                return array == null ? null : List(array.Count, "structs");
            }

            case "TYPE_VECTOR4":
            case "TYPE_QUATERNION": return Grouped(objects, at, 16, 4, real);
            case "TYPE_QSTRANSFORM": return Grouped(objects, at, 48, 12, real);
            case "TYPE_TRANSFORM":
            case "TYPE_MATRIX4": return Grouped(objects, at, 64, 16, real);

            // Unsigned for the signed widths too, the same way a lone int8 or int16 is read:
            // hkxpack prints the bytes as they sit, so a parent index of 0xFFFF is 65535 there
            // rather than -1, and a reading that spells it differently in an array than on its own
            // agrees with neither.
            case "TYPE_BOOL":
            case "TYPE_CHAR":
            case "TYPE_INT8":
            case "TYPE_UINT8":
                return Listed(objects.ReadValueArrayAt(at, 1, (b, o) => b[o]));
            case "TYPE_INT16":
            case "TYPE_UINT16":
                return Listed(objects.ReadValueArrayAt(at, 2, BitConverter.ToUInt16));
            case "TYPE_REAL":
            {
                var reals = objects.ReadValueArrayAt(at, 4, BitConverter.ToSingle);
                return reals == null ? null : List(reals.Count, reals.Select(v => real(v)));
            }
            case "TYPE_INT32":
                return Listed(objects.ReadValueArrayAt(at, 4, BitConverter.ToInt32));
            case "TYPE_UINT32":
                return Listed(objects.ReadValueArrayAt(at, 4, BitConverter.ToUInt32));
            case "TYPE_UINT64":
            case "TYPE_INT64":
                return Listed(objects.ReadValueArrayAt(at, 8, BitConverter.ToUInt64));

            default: return null;
        }
    }

    /// An enum's number, at whatever width the field is. Signed where the type is: an enum of int8
    /// holding 0xFF is -1, and looking up 255 would miss the entry.
    public static long? Number(PackfileObjects objects, int at, string width)
    {
        int? whole = objects.ReadIntAt(at);
        if (whole == null) return null;

        return width switch
        {
            "TYPE_INT8" => (sbyte)whole.Value,
            "TYPE_UINT8" or "TYPE_CHAR" => (byte)whole.Value,
            "TYPE_INT16" => (short)whole.Value,
            "TYPE_UINT16" => (ushort)whole.Value,
            "TYPE_UINT32" => (uint)whole.Value,
            _ => whole.Value,
        };
    }

    /// The same bits read without a sign, which is how hkxpack writes them out.
    public static long Unsigned(long value, string width) => width switch
    {
        "TYPE_INT8" or "TYPE_UINT8" or "TYPE_CHAR" => value & 0xFF,
        "TYPE_INT16" or "TYPE_UINT16" => value & 0xFFFF,
        "TYPE_INT32" => value & 0xFFFFFFFFL,
        _ => value,
    };

    /// How much room one value of a type takes, for stepping through a fixed length array of them.
    /// How many bytes a narrow field occupies, which is how many should be read for it.
    private static int Bytes(string vtype) => vtype switch
    {
        "TYPE_BOOL" or "TYPE_CHAR" or "TYPE_INT8" or "TYPE_UINT8" => 1,
        "TYPE_INT16" or "TYPE_UINT16" => 2,
        _ => 4,
    };

    private static int Width(string vtype) => vtype switch
    {
        "TYPE_BOOL" or "TYPE_CHAR" or "TYPE_INT8" or "TYPE_UINT8" => 1,
        "TYPE_INT16" or "TYPE_UINT16" or "TYPE_HALF" => 2,
        "TYPE_INT64" or "TYPE_UINT64" or "TYPE_ULONG" or "TYPE_POINTER"
            or "TYPE_STRINGPTR" or "TYPE_CSTRING" => 8,
        "TYPE_VECTOR4" or "TYPE_QUATERNION" => 16,
        "TYPE_QSTRANSFORM" => 48,
        "TYPE_TRANSFORM" or "TYPE_MATRIX4" => 64,
        _ => 4,
    };

    /// The name on its own, without the number in front. What a person reads.
    public static string Plain(string rendered)
    {
        int colon = rendered.IndexOf(':');
        return colon > 0 && long.TryParse(rendered[..colon], out _) ? rendered[(colon + 1)..] : rendered;
    }

    /// An array whose elements are several floats each: a vector, a transform, a matrix. Read as one
    /// long run and cut into elements, because that is how they sit in the file.
    private static string? Grouped(PackfileObjects objects, int at, int stride, int floats, Real real)
    {
        var array = objects.ArrayAt(at);
        if (array == null) return null;

        var all = objects.ReadValueArrayAt(at, stride,
                                           (b, o) => Enumerable.Range(0, floats)
                                                               .Select(i => BitConverter.ToSingle(b, o + i * 4))
                                                               .ToArray());
        return all == null ? null : List(array.Count, all.Select(e => Floats(e, real)!));
    }

    /// A field narrower than four bytes still reads as four, so the extra has to be masked off or a
    /// one byte flag reports whatever its neighbours happen to hold.
    private static string? Narrow(int? value, string vtype)
    {
        if (value is not int raw) return null;
        return vtype switch
        {
            "TYPE_BOOL" => ((raw & 0xFF) != 0).ToString().ToLowerInvariant(),
            // Masked rather than sign extended: hkxpack prints the bytes as they sit, so an
            // animationBindingIndex of 0xFFFF is 65535 there and not -1.
            "TYPE_INT8" or "TYPE_UINT8" or "TYPE_CHAR" => (raw & 0xFF).ToString(),
            "TYPE_INT16" or "TYPE_UINT16" => (raw & 0xFFFF).ToString(),
            _ => raw.ToString(),
        };
    }

    /// Four floats to a bracket. One vector is one bracket; a transform is four of them run
    /// together with nothing between, which is what puts the closing and opening bracket of two
    /// neighbours in the same whitespace separated token.
    public static string? Floats(float[]? values, Real? real = null)
    {
        real ??= Shortest;
        if (values == null) return null;

        var text = new System.Text.StringBuilder();
        for (int at = 0; at < values.Length; at += 4)
            text.Append('(')
                .Append(string.Join(" ", values.Skip(at).Take(4).Select(v => real(v))))
                .Append(')');

        return text.Length == 0 ? "()" : text.ToString();
    }

    private static string? Listed<T>(IReadOnlyList<T>? values) =>
        values == null ? null : List(values.Count, values.Select(v => v?.ToString() ?? ""));

    public static string List(int count, IEnumerable<string> tokens) =>
        $"[{count}: {string.Join("|", tokens)}]";

    /// An empty array has nothing in it to describe, however unreadable its elements would be, so it
    /// reads the same either way rather than as a count with a word after it.
    public static string List(int count, string what) => count == 0 ? "[0: ]" : $"[{count}: {what}]";
}
