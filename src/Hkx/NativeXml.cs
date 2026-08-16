using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenCommonwealth.Services.Hkx;

public static class NativeXml
{

    private const int Wrap = 64;

    private const string Header =
        "<?xml version=\"1.0\" encoding=\"ASCII\" standalone=\"no\"?>";

    public static string From(byte[] hkx, HavokClassTypes? types = null)
    {
        var image = PackfileImage.Read(hkx);

        // Thread the supplied schema into the object reader too, or a custom schema would only
        // affect signatures/enum names while member offsets silently fell back to the shipped
        // table (which matters at four bytes, where offsets are re-derived per schema).
        return From(new PackfileObjects(image, types: types), image, types);
    }

    public static string From(PackfileObjects objects, PackfileImage image,
                              HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;

        var index = new Dictionary<PackfileObjects.Instance, int>();
        for (int i = 0; i < objects.Instances.Count; i++) index[objects.Instances[i]] = i;

        string Reference(PackfileObjects.Instance? target, bool wasNull) =>
            wasNull || target == null || !index.TryGetValue(target, out int at)
                ? "null"
                : "#" + (NativeGraphModel.FirstId + at);

        var text = new StringBuilder();
        text.Append(Header).Append('\n');
        text.Append($"<hkpackfile classversion=\"{image.FileVersion}\" contentsversion=\"")
            .Append(ContentsVersion(image)).Append("\">\n");
        text.Append("    <hksection name=\"__data__\">\n");

        for (int i = 0; i < objects.Instances.Count; i++)
        {
            var instance = objects.Instances[i];
            string signature = types[instance.ClassName] is { } layout
                ? $"0x{layout.Signature:x}"
                : "";

            text.Append($"        <hkobject class=\"{instance.ClassName}\" ")
                .Append($"name=\"#{NativeGraphModel.FirstId + i}\" signature=\"{signature}\">\n");

            Members(text, objects, types, instance.ClassName, instance.Offset, Reference, 3);

            text.Append("        </hkobject>\n");
        }

        text.Append("    </hksection>\n");
        text.Append("</hkpackfile>\n");
        return text.ToString();
    }

    private static string ContentsVersion(PackfileImage image)
    {
        int end = System.Array.IndexOf(image.ContentsVersion, (byte)0);
        if (end < 0) end = image.ContentsVersion.Length;
        return Encoding.ASCII.GetString(image.ContentsVersion, 0, end);
    }

    private static void Members(StringBuilder text, PackfileObjects objects, HavokClassTypes types,
                                string className, int offset, FieldRender.Reference reference,
                                int depth)
    {
        string pad = new(' ', depth * 4);

        var layout = LayoutWalker.Active(types, className, objects.PointerWidth);
        if (layout == null) return;

        foreach (var member in types.Members(className))
        {

            if (!member.Written)
            {
                if (member.ArrSize > 0)
                    for (int e = 0; e < member.ArrSize; e++)
                        text.Append($"{pad}<!-- {member.Name}{e + 1} SERIALIZE_IGNORED -->\n");
                else
                    text.Append($"{pad}<!-- {member.Name} SERIALIZE_IGNORED -->\n");
                continue;
            }

            int at = offset + (layout.OffsetOf(member.Name) ?? member.Offset);

            if (member.VType == "TYPE_STRUCT")
            {
                if (member.CType == null || !types.Knows(member.CType))
                {
                    text.Append($"{pad}<hkparam name=\"{member.Name}\"/>\n");
                    continue;
                }

                string inner = types[member.CType] is { } held
                    ? $" class=\"{member.CType}\" name=\"{member.Name}\" signature=\"0x{held.Signature:x}\""
                    : $" name=\"{member.Name}\"";

                text.Append($"{pad}<hkparam name=\"{member.Name}\">\n");
                text.Append($"{pad}    <hkobject{inner}>\n");
                Members(text, objects, types, member.CType, at, reference, depth + 2);
                text.Append($"{pad}    </hkobject>\n");
                text.Append($"{pad}</hkparam>\n");
                continue;
            }

            if (member.VType is "TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY")
            {
                Array(text, objects, types, member, offset, at, reference, depth);
                continue;
            }

            if (member.ArrSize > 0)
            {

                for (int e = 0; e < member.ArrSize; e++)
                    Scalar(text, pad, member.Name + (e + 1),
                           NativeGraphModel.Text(objects, at, className, member, reference, e, types, trim: false));
                continue;
            }

            Scalar(text, pad, member.Name,
                   NativeGraphModel.Text(objects, at, className, member, reference, 0, types, trim: false));
        }
    }

    private static void Scalar(StringBuilder text, string pad, string name, string value)
    {

        if (value.Length == 0) text.Append($"{pad}<hkparam name=\"{name}\"/>\n");
        else text.Append($"{pad}<hkparam name=\"{name}\">{value}</hkparam>\n");
    }

    private static void Array(StringBuilder text, PackfileObjects objects, HavokClassTypes types,
                              HavokClassTypes.Member member, int structStart, int at,
                              FieldRender.Reference reference, int depth)
    {
        string pad = new(' ', depth * 4);
        bool rel = member.VType == "TYPE_RELARRAY";
        int width = member.VSub == "TYPE_STRUCT"
            ? member.CType != null
                ? LayoutWalker.Active(types, member.CType, objects.PointerWidth)?.Size ?? 0
                : 0
            : rel
                ? NativeGraphModel.RelElementWidth(types, objects.PointerWidth, member)
                : NativeGraphModel.ElementWidth(member.VSub, objects.PointerWidth);
        var declared = rel ? null : objects.ArrayAt(at);
        PackfileObjects.IArraySpan? array = width > 0
            ? rel ? objects.RelArrayAt(structStart, at, width) : objects.ArrayAt(at, width)
            : declared;
        int count = width > 0 ? array?.Count ?? 0 : declared?.Count ?? 0;

        bool pointers = member.VSub == "TYPE_POINTER";

        if (count == 0 && !pointers)
        {
            text.Append($"{pad}<hkparam name=\"{member.Name}\" numelements=\"0\"/>\n");
            return;
        }

        if (!pointers && member.VSub is not ("TYPE_STRUCT" or "TYPE_STRINGPTR" or "TYPE_CSTRING"))
        {
            var values = NativeGraphModel.Elements(objects, types, structStart, at, member, reference).ToList();
            string all = string.Join(" ", values);

            if (all.Length <= Wrap)
            {
                text.Append($"{pad}<hkparam name=\"{member.Name}\" numelements=\"{count}\">")
                    .Append(all).Append("</hkparam>\n");
                return;
            }

            text.Append($"{pad}<hkparam name=\"{member.Name}\" numelements=\"{count}\">\n");

            var line = new StringBuilder();
            foreach (string token in values)
            {
                if (line.Length > 0 && line.Length + 1 + token.Length > Wrap)
                {
                    text.Append(line).Append('\n');
                    line.Clear();
                }
                if (line.Length > 0) line.Append(' ');
                line.Append(token);
            }

            text.Append(line).Append("</hkparam>\n");
            return;
        }

        text.Append($"{pad}<hkparam name=\"{member.Name}\" numelements=\"{count}\">\n");

        if (member.VSub == "TYPE_STRUCT" && member.CType != null && types.Knows(member.CType))
        {
            int stride = LayoutWalker.Active(types, member.CType, objects.PointerWidth)?.Size ?? 0;
            if (stride > 0 && array != null)
                for (int e = 0; e < count; e++)
                {
                    text.Append($"{pad}    <hkobject>\n");
                    Members(text, objects, types, member.CType, array.At + e * stride, reference,
                            depth + 2);
                    text.Append($"{pad}    </hkobject>\n");
                }
        }
        else if (member.VSub is "TYPE_STRINGPTR" or "TYPE_CSTRING")
        {
            var values = rel
                ? objects.ReadRelStringArrayAt(structStart, at)
                : objects.ReadStringArrayAt(at);
            foreach (string? value in values ?? new List<string?>())
                text.Append($"{pad}    <hkcstring>{NativeGraphModel.Escaped(value ?? "")}</hkcstring>\n");
        }
        else
        {

            foreach (string token in NativeGraphModel.Elements(objects, types, structStart, at, member, reference))
                text.Append(token).Append('\n');

            text.Append("</hkparam>\n");
            return;
        }

        text.Append($"{pad}</hkparam>\n");
    }
}
