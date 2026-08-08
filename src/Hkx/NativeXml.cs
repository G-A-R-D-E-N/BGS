using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenCommonwealth.Services.Hkx;

// The text form of a file, written from the file's own bytes instead of by hkxpack.
//
// Reading a behaviour has not needed Java for a while: the tree, the graph, the symbols and the
// properties all come out of the bytes, and that reading is checked field by field against hkxpack
// across the corpus. Editing still does, and for one reason only. An edit is made by rewriting the
// unpacked text and then working out what changed by comparing two texts, so without hkxpack there
// is no text to rewrite and every edit is refused.
//
// Producing the same text ourselves is the smaller of the two ways out. The other is to move every
// edit onto the model, which is the better shape and touches every consumer; this leaves all of them
// working and removes the dependency underneath them.
//
// Equivalence is the whole point, so this reproduces hkxpack's own spelling rather than a tidier
// one: its number formatting, its self closing tags for anything empty, its SERIALIZE_IGNORED
// comments for members that exist in the class and are not written to the file, and its four space
// indentation. Anywhere the two differ, hkxpack is right by definition, because the text it produces
// is the text every consumer here was written against.
public static class NativeXml
{
    /// The width hkxpack packs an array's elements into.
    private const int Wrap = 64;

    /// hkxpack writes this on every file it produces, whatever the packfile header says. Taken from
    /// its output rather than from the header: the header's own class version is 11 and its contents
    /// version is the same string, so the two agree today, and if they ever stop agreeing the text
    /// has to match the text.
    private const string Header =
        "<?xml version=\"1.0\" encoding=\"ASCII\" standalone=\"no\"?>";

    public static string From(byte[] hkx, HavokClassTypes? types = null) =>
        From(new PackfileObjects(PackfileImage.Read(hkx)), PackfileImage.Read(hkx), types);

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

    /// The contents version as the file itself states it, trimmed of the padding the header pads it
    /// with. Read rather than assumed, so a file written for another Havok says so in its text.
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

        foreach (var member in types.Members(className))
        {
            // A member the class declares and the file does not store. hkxpack says so rather than
            // leaving it out, and something downstream may well be counting the lines.
            //
            // One line per element when it is a fixed length C array, the same way a written one is
            // one field per element. `hkbBehaviorGraph.pad` is `hkInt8[4]` and hkxpack writes four
            // comments for it, not one, which is the whole of a 1,509 line disagreement on a single
            // behaviour: three lines an object, on every object carrying a pad.
            if (!member.Written)
            {
                if (member.ArrSize > 0)
                    for (int e = 0; e < member.ArrSize; e++)
                        text.Append($"{pad}<!-- {member.Name}{e + 1} SERIALIZE_IGNORED -->\n");
                else
                    text.Append($"{pad}<!-- {member.Name} SERIALIZE_IGNORED -->\n");
                continue;
            }

            int at = offset + member.Offset;

            if (member.VType == "TYPE_STRUCT")
            {
                if (member.CType == null || !types.Knows(member.CType))
                {
                    text.Append($"{pad}<hkparam name=\"{member.Name}\"/>\n");
                    continue;
                }

                // A struct written inline names its own class and signature. An element of an array
                // of structs does not, and is a bare tag. Both measured against hkxpack's output:
                // hkbStateMachine's eventToSendWhenStateOrTransitionChanges is
                // `<hkobject class="hkbEvent" name="..." signature="0x3e0fd810">` while the elements
                // of namedVariants are plain `<hkobject>`.
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
                Array(text, objects, types, member, at, reference, depth);
                continue;
            }

            if (member.ArrSize > 0)
            {
                // A fixed length C array is not an array to this format: `hkReal[8]` is eight fields
                // named from one rather than from zero.
                for (int e = 0; e < member.ArrSize; e++)
                    Scalar(text, pad, member.Name + (e + 1),
                           NativeGraphModel.Text(objects, at, className, member, reference, e, types, trim: false));
                continue;
            }

            Scalar(text, pad, member.Name,
                   NativeGraphModel.Text(objects, at, className, member, reference, 0, types, trim: false));
        }
    }

    /// An empty value is a tag that closes itself rather than a pair with nothing between them, which
    /// is how hkxpack writes an unset string and how a reader tells one from an empty one.
    private static void Scalar(StringBuilder text, string pad, string name, string value)
    {
        // Not escaped again here. The reader already escapes on the way out, so doing it twice turns
        // an expression's `&gt;` into `&amp;gt;`, which is what three of Dogmeat's expression strings
        // caught.
        if (value.Length == 0) text.Append($"{pad}<hkparam name=\"{name}\"/>\n");
        else text.Append($"{pad}<hkparam name=\"{name}\">{value}</hkparam>\n");
    }

    private static void Array(StringBuilder text, PackfileObjects objects, HavokClassTypes types,
                              HavokClassTypes.Member member, int at,
                              FieldRender.Reference reference, int depth)
    {
        string pad = new(' ', depth * 4);
        var array = objects.ArrayAt(at);
        int count = array?.Count ?? 0;

        // An empty array of pointers is written as a pair of tags; every other empty array closes
        // itself. Derived from hkxpack's own output rather than reasoned about: across one behaviour,
        // the empty `listeners` and `variantVariableValues`, both arrays of pointers, are paired,
        // while the empty `attributeDefaults`, `variableBounds`, `quadVariableValues` and
        // `attributeNames`, which are reals, structs, vectors and strings, all close themselves.
        bool pointers = member.VSub == "TYPE_POINTER";

        if (count == 0 && !pointers)
        {
            text.Append($"{pad}<hkparam name=\"{member.Name}\" numelements=\"0\"/>\n");
            return;
        }

        // Numbers, vectors and anything else that is not a pointer go on one line, between the tags,
        // the way `boneIndices numelements="24">0 1 2 ...</hkparam>` is written. Only pointers get a
        // line each, and those sit on the margin.
        if (!pointers && member.VSub is not ("TYPE_STRUCT" or "TYPE_STRINGPTR" or "TYPE_CSTRING"))
        {
            var values = NativeGraphModel.Elements(objects, types, at, member, reference).ToList();
            string all = string.Join(" ", values);

            // Everything that is not a pointer is packed into lines of at most sixty four characters.
            // Content that fits in one such line stays between the tags; content that does not goes
            // onto its own lines, on the margin, with the closing tag against the last of them.
            //
            // Sixty four is measured, not chosen: across 12,833 arrays in the unpacked corpus, the
            // longest inline content and the longest wrapped line are both exactly 64.
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
            int stride = types[member.CType]?.Size ?? 0;
            if (stride > 0)
                for (int e = 0; e < count; e++)
                {
                    text.Append($"{pad}    <hkobject>\n");
                    Members(text, objects, types, member.CType, array!.At + e * stride, reference,
                            depth + 2);
                    text.Append($"{pad}    </hkobject>\n");
                }
        }
        else if (member.VSub is "TYPE_STRINGPTR" or "TYPE_CSTRING")
        {
            var values = objects.ReadStringArrayAt(at);
            foreach (string? value in values ?? new List<string?>())
                text.Append($"{pad}    <hkcstring>{NativeGraphModel.Escaped(value ?? "")}</hkcstring>\n");
        }
        else
        {
            // Pointers get a line each, hard against the left margin, with the closing tag on the
            // margin with them. One per line however short they are: measured across 3,235 pointer
            // arrays in the corpus, never two on a line.
            foreach (string token in NativeGraphModel.Elements(objects, types, at, member, reference))
                text.Append(token).Append('\n');

            text.Append("</hkparam>\n");
            return;
        }

        text.Append($"{pad}</hkparam>\n");
    }
}
