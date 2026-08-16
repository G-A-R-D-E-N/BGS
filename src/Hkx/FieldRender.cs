using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class FieldRender
{

    public delegate string Reference(PackfileObjects.Instance? target, bool wasNull);

    public delegate string Real(float value);

    public static readonly Real Shortest = value => value.ToString("R");

    public static readonly Real ReferenceText = HkxNumber.Text;

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

            string? name = types.NameOf(owner, member, value.Value);
            long printed = Unsigned(value.Value, member.VSub);

            if (name != null) return $"{printed}:{name}";

            return expected == null || long.TryParse(expected, out _) ? printed.ToString() : null;
        }

        if (member.ArrSize > 0) at += element * Width(member.VType, objects.PointerWidth);

        switch (member.VType)
        {
            case "TYPE_REAL": return objects.ReadFloatAt(at) is float one ? real(one) : null;
            case "TYPE_STRINGPTR":
            case "TYPE_CSTRING": return objects.ReadStringAt(at) ?? "∅";

            case "TYPE_BOOL":
            case "TYPE_CHAR":
            case "TYPE_INT8" or "TYPE_UINT8" or "TYPE_INT16" or "TYPE_UINT16"
                or "TYPE_INT32" or "TYPE_UINT32":

                return Narrow(objects.ReadNarrowAt(at, Bytes(member.VType)), member.VType);

            case "TYPE_ULONG": return objects.ReadUnsignedAt(at, objects.PointerWidth)?.ToString();
            case "TYPE_UINT64": return objects.ReadULongAt(at)?.ToString();
            case "TYPE_INT64": return objects.ReadLongAt(at)?.ToString();

            case "TYPE_VECTOR4":
            case "TYPE_QUATERNION": return Floats(objects.ReadFloatsAt(at, 4), real);

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

            case "TYPE_ARRAY": return Array(objects, at, member, reference, real, types);

            default: return null;
        }
    }

    private static string? Array(PackfileObjects objects, int at, HavokClassTypes.Member member,
                                 Reference reference, Real real, HavokClassTypes types)
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

            case "TYPE_STRUCT":
            {
                if (member.CType != null && types[member.CType]?.Size is int stride && stride > 0)
                {
                    var array = objects.ArrayAt(at, stride);
                    return array == null ? null : List(array.Count, "structs");
                }

                var declared = objects.ArrayAt(at);
                return declared == null ? null : List(declared.Count, "structs");
            }

            case "TYPE_VECTOR4":
            case "TYPE_QUATERNION": return Grouped(objects, at, 16, 4, real);
            case "TYPE_QSTRANSFORM": return Grouped(objects, at, 48, 12, real);
            case "TYPE_TRANSFORM":
            case "TYPE_MATRIX4": return Grouped(objects, at, 64, 16, real);

            case "TYPE_BOOL":
            case "TYPE_CHAR":
            case "TYPE_UINT8":
                return Listed(objects.ReadValueArrayAt(at, 1, (b, o) => b[o]));
            case "TYPE_INT8":
                return Listed(objects.ReadValueArrayAt(at, 1, (b, o) => (sbyte)b[o]));
            case "TYPE_INT16":
                return Listed(objects.ReadValueArrayAt(at, 2, BitConverter.ToInt16));
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
            case "TYPE_INT64":
                return Listed(objects.ReadValueArrayAt(at, 8, BitConverter.ToInt64));
            case "TYPE_UINT64":
                return Listed(objects.ReadValueArrayAt(at, 8, BitConverter.ToUInt64));
            case "TYPE_ULONG":
                // hkUlong elements are pointer-sized, so they stride at the active pointer
                // width rather than the shipped eight-byte width.
                return Listed(objects.ReadValueArrayAt(
                    at, objects.PointerWidth, (b, o) => objects.ReadUnsignedAt(o, objects.PointerWidth) ?? 0));
            case "TYPE_VARIANT":
            {
                var targets = objects.ReadVariantArrayAt(at);
                return targets == null ? null : List(targets.Count, targets.Select(t => reference(t, t == null)));
            }

            default: return null;
        }
    }

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

    public static long Unsigned(long value, string width) => width switch
    {
        "TYPE_INT8" or "TYPE_UINT8" or "TYPE_CHAR" => value & 0xFF,
        "TYPE_INT16" or "TYPE_UINT16" => value & 0xFFFF,
        "TYPE_INT32" => value & 0xFFFFFFFFL,
        _ => value,
    };

    private static int Bytes(string vtype) => vtype switch
    {
        "TYPE_BOOL" or "TYPE_CHAR" or "TYPE_INT8" or "TYPE_UINT8" => 1,
        "TYPE_INT16" or "TYPE_UINT16" => 2,
        _ => 4,
    };

    private static int Width(string vtype, int pointer) => vtype switch
    {
        "TYPE_BOOL" or "TYPE_CHAR" or "TYPE_INT8" or "TYPE_UINT8" => 1,
        "TYPE_INT16" or "TYPE_UINT16" or "TYPE_HALF" => 2,
        "TYPE_INT64" or "TYPE_UINT64" => 8,
        "TYPE_ULONG" or "TYPE_POINTER" or "TYPE_STRINGPTR" or "TYPE_CSTRING" => pointer,
        "TYPE_VECTOR4" or "TYPE_QUATERNION" => 16,
        "TYPE_QSTRANSFORM" => 48,
        "TYPE_TRANSFORM" or "TYPE_MATRIX4" => 64,
        _ => 4,
    };

    public static string Plain(string rendered)
    {
        int colon = rendered.IndexOf(':');
        return colon > 0 && long.TryParse(rendered[..colon], out _) ? rendered[(colon + 1)..] : rendered;
    }

    private static string? Grouped(PackfileObjects objects, int at, int stride, int floats, Real real)
    {
        var all = objects.ReadValueArrayAt(at, stride,
                                           (b, o) => Enumerable.Range(0, floats)
                                                               .Select(i => BitConverter.ToSingle(b, o + i * 4))
                                                               .ToArray());
        return all == null ? null : List(all.Count, all.Select(e => Floats(e, real)!));
    }

    private static string? Narrow(int? value, string vtype)
    {
        if (value is not int raw) return null;
        return vtype switch
        {
            "TYPE_BOOL" => ((raw & 0xFF) != 0).ToString().ToLowerInvariant(),
            "TYPE_INT8" => ((sbyte)(byte)(raw & 0xFF)).ToString(),
            "TYPE_UINT8" or "TYPE_CHAR" => (raw & 0xFF).ToString(),
            "TYPE_INT16" => ((short)(ushort)(raw & 0xFFFF)).ToString(),
            "TYPE_UINT16" => (raw & 0xFFFF).ToString(),
            _ => raw.ToString(),
        };
    }

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

    public static string List(int count, string what) => count == 0 ? "[0: ]" : $"[{count}: {what}]";
}
