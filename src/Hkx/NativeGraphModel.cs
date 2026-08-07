using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// The graph model, built from the file's own bytes.
//
// The model it builds is the one the regex parser builds out of hkxpack's text, and that is the
// whole specification: an id, a class, and four dictionaries per object. Everything reading a
// behaviour goes through those, so if these come out the same then the graph, the symbols, the
// validator and the compare tab come out the same without knowing anything changed.
//
// **The target is equivalence, not correctness.** The parser drops things, and every one of those
// has to be dropped here too:
//
//   Nesting. A struct written inside a struct is recorded as an empty string and its contents are
//   gone. Real files nest three deep, so this is not a corner case, and reading the inner fields
//   properly would hand every consumer fields it has never seen.
//
//   Array text. An array of vectors is split on whitespace, which cuts `(0 0 0 1)` into four tokens
//   with brackets stuck to the ends. Nothing reads one, so nothing has noticed. Copied as is.
//
// Values come from `FieldRender`, which is already the renderer the properties panel uses and has
// been checked against hkxpack 485,793 times. This adds no second opinion about what a float looks
// like; it only decides which of the four buckets each field lands in.
public static class NativeGraphModel
{
    /// hkxpack starts numbering at #90 and counts up in the order the objects sit in the file, which
    /// is the order the virtual fixups give us. Measured across the corpus rather than assumed, and
    /// the model command checks it holds on every file it reads.
    public const int FirstId = 90;

    /// Null when the class table cannot describe every class in the file, rather than a model with
    /// holes in it. The same discipline as the load path: a reading that is partly right is worse
    /// than one that says it cannot read, because nothing downstream can tell the difference.
    public static BehaviourGraphModel? From(PackfileObjects objects, HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;
        if (types.Count == 0) return null;
        if (objects.Instances.Any(i => !types.Knows(i.ClassName))) return null;

        var index = new Dictionary<PackfileObjects.Instance, int>();
        for (int i = 0; i < objects.Instances.Count; i++) index[objects.Instances[i]] = i;

        string Reference(PackfileObjects.Instance? target, bool wasNull) =>
            wasNull || target == null || !index.TryGetValue(target, out int at)
                ? "null"
                : "#" + (FirstId + at);

        var model = new BehaviourGraphModel();
        for (int i = 0; i < objects.Instances.Count; i++)
        {
            var instance = objects.Instances[i];
            var obj = new HkObject { Id = (FirstId + i).ToString(), Class = instance.ClassName };

            Fill(objects, types, obj, instance.Offset, instance.ClassName, Reference);

            model.ById[obj.Id] = obj;
            model.Objects.Add(obj);
        }

        return model;
    }

    /// One object's fields, sorted into the four buckets the parser sorts them into.
    private static void Fill(PackfileObjects objects, HavokClassTypes types, HkObject obj,
                            int offset, string className, FieldRender.Reference reference)
    {
        foreach (var member in types.Members(className))
        {
            if (!member.Written) continue;
            int at = offset + member.Offset;

            if (member.VType == "TYPE_STRUCT")
            {
                // No entry in Scalars for the field itself. hkxpack opens a tag and puts the struct
                // inside it, so the parser sees a param with nothing in it and records nothing.
                if (member.CType != null && types.Knows(member.CType))
                {
                    obj.Structs[member.Name] = Flatten(objects, types, at, member.CType, reference);
                    Leak(objects, types, obj, at, member.CType, member.Name);
                }
                continue;
            }

            if (member.VType is "TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY")
            {
                Array(objects, types, obj, at, member, reference);
                continue;
            }

            if (member.ArrSize > 0)
            {
                // A fixed length C array is written as one field per element, named from one rather
                // than from zero. Measured: `hkbFootIkControlData.enabled` is `enabled1` through
                // `enabled8`.
                for (int e = 0; e < member.ArrSize; e++)
                    obj.Scalars[member.Name + (e + 1)] =
                        Text(objects, at, className, member, reference, e, types);
                continue;
            }

            obj.Scalars[member.Name] = Text(objects, at, className, member, reference, 0, types);
        }
    }

    /// The fields of a struct written inline, flattened the way the parser flattens them.
    ///
    /// Anything with its own contents becomes an empty string here: a struct inside this one, an
    /// array of structs, an array of strings. That is not a shortcut, it is what the parser records,
    /// because it only ever looks one level down.
    private static Dictionary<string, string> Flatten(PackfileObjects objects, HavokClassTypes types,
                                                      int offset, string className,
                                                      FieldRender.Reference reference)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var member in types.Members(className))
        {
            if (!member.Written) continue;
            int at = offset + member.Offset;

            if (member.VType == "TYPE_STRUCT")
            {
                fields[member.Name] = "";
                continue;
            }

            if (member.VType is "TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY")
            {
                fields[member.Name] = member.VSub is "TYPE_STRUCT" or "TYPE_STRINGPTR" or "TYPE_CSTRING"
                    ? ""
                    : string.Join(" ", Elements(objects, types, at, member, reference));
                continue;
            }

            if (member.ArrSize > 0)
            {
                for (int e = 0; e < member.ArrSize; e++)
                    fields[member.Name + (e + 1)] =
                        Text(objects, at, className, member, reference, e, types);
                continue;
            }

            fields[member.Name] = Text(objects, at, className, member, reference, 0, types);
        }

        return fields;
    }

    /// Strings held by a struct written inside this object, which land in the outer object's lists
    /// under the outer field's name.
    ///
    /// This is a leak in the parser rather than a design. Strings are written as their own tags, and
    /// the parser files a tag it meets under whichever field it last saw opened, which by then is the
    /// field the struct sits in and not the field inside the struct that actually holds them. So the
    /// four animation names inside `animationBundleNameData` come out as four strings on the object
    /// itself. Reproduced because equivalence is the point, and because something may well be reading
    /// them from where they land.
    private static void Leak(PackfileObjects objects, HavokClassTypes types, HkObject obj,
                             int offset, string className, string under)
    {
        foreach (var member in types.Members(className))
        {
            if (!member.Written) continue;
            if (member.VType is not ("TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY")) continue;
            if (member.VSub is not ("TYPE_STRINGPTR" or "TYPE_CSTRING")) continue;

            var values = objects.ReadStringArrayAt(offset + member.Offset);
            if (values == null || values.Count == 0) continue;

            if (!obj.Lists.TryGetValue(under, out var list)) obj.Lists[under] = list = new List<string>();
            foreach (string? value in values) list.Add(Trimmed(Escaped(value ?? "")));
        }
    }

    /// An array, into whichever buckets its elements belong in.
    ///
    /// An array of structs lands in two of them. hkxpack writes the elements as nested objects, so
    /// the parser first records an empty list under the field name from the opening tag and then
    /// fills the struct list from the objects inside it. Both entries exist, and the empty one is
    /// not an oversight to tidy away.
    private static void Array(PackfileObjects objects, HavokClassTypes types, HkObject obj, int at,
                              HavokClassTypes.Member member, FieldRender.Reference reference)
    {
        if (member.VSub == "TYPE_STRUCT")
        {
            obj.Lists[member.Name] = new List<string>();
            if (member.CType == null || !types.Knows(member.CType)) return;

            var array = objects.ArrayAt(at);
            if (array == null) return;

            int stride = types[member.CType]?.Size ?? 0;
            if (stride <= 0) return;

            // An empty one gets no struct list at all, only the empty plain list above. hkxpack
            // writes an empty array as a tag that closes itself, so there are no objects inside it
            // for the parser to make a list out of, and a reading that offers an empty list where
            // the other has no key is a disagreement.
            if (array.Count == 0) return;

            var elements = new List<Dictionary<string, string>>(array.Count);
            for (int e = 0; e < array.Count; e++)
            {
                int element = array.At + e * stride;
                elements.Add(Flatten(objects, types, element, member.CType, reference));
                Leak(objects, types, obj, element, member.CType, member.Name);
            }

            obj.StructLists[member.Name] = elements;
            return;
        }

        if (member.VSub is "TYPE_STRINGPTR" or "TYPE_CSTRING")
        {
            // Strings come out as their own tags rather than as text, so they stay whole instead of
            // being split on whitespace. A name with a space in it survives, which it would not if
            // this went through the text path below.
            var values = objects.ReadStringArrayAt(at);
            obj.Lists[member.Name] = values == null
                ? new List<string>()
                : values.Select(v => Trimmed(Escaped(v ?? ""))).ToList();
            return;
        }

        // Everything else is written as text inside one tag, and the parser splits that text on
        // whitespace. Joining the elements and splitting them again looks like a waste and is not:
        // it is what puts an array of vectors through the same mangling, so `(0 0 0 1)` comes back
        // as four tokens here exactly as it does there.
        string joined = string.Join(" ", Elements(objects, types, at, member, reference));
        obj.Lists[member.Name] = joined
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    /// One array's elements, each rendered as the type the array holds.
    internal static IEnumerable<string> Elements(PackfileObjects objects, HavokClassTypes types, int at,
                                                HavokClassTypes.Member member,
                                                FieldRender.Reference reference)
    {
        var array = objects.ArrayAt(at);
        if (array == null) yield break;

        if (member.VSub == "TYPE_POINTER")
        {
            var targets = objects.ReadRefArrayAt(at);
            if (targets == null) yield break;

            foreach (var target in targets) yield return reference(target, target == null);
            yield break;
        }

        // The element is read as a lone value of the array's own type, which is what lets the same
        // renderer answer for both. A member standing in for the element type, so an array of reals
        // is read as a real at each stride.
        var element = new HavokClassTypes.Member { Name = member.Name, VType = member.VSub };
        int stride = Stride(member.VSub);
        if (stride <= 0) yield break;

        for (int e = 0; e < array.Count; e++)
            yield return FieldRender.Render(objects, array.At + e * stride, "", element, reference,
                                            null, 0, types, FieldRender.LikeHkxPack) ?? "";
    }

    /// The parser takes the text between the tags and trims it, because the tags in a real file sit
    /// on their own lines with the value indented between them. A name that genuinely begins with a
    /// space therefore loses it, and three state machines in the vanilla weapon behaviour are named
    /// exactly that way. Losing it here too is what equivalence means; the file still holds the
    /// space, and the properties panel, which reads the bytes rather than this, still shows it.
    internal static string Trimmed(string value) => value.Trim();

    /// `trim` is what the parser does to a string and what writing the text back must not do. A
    /// string in this format can carry a trailing space and six vanilla values do:
    /// `NPCRobotAssaultronAttackHandSpinLP ` is one. The parser trims, so the model trims to match
    /// it; the writer has to hand back what the file holds or the text stops being the file's.
    internal static string Text(PackfileObjects objects, int at, string owner,
                               HavokClassTypes.Member member, FieldRender.Reference reference,
                               int element, HavokClassTypes types, bool trim = true)
    {
        // An enum or a flags field, spelled the way hkxpack spells it, which is not the way a panel
        // should. hkxpack writes the name when the number is exactly one the class declares and the
        // bare number otherwise, so a combination of two named flags comes out as 3072 rather than
        // as the two names joined. The panel joins them because that is the readable answer; a
        // reading being set against the file has to say 3072.
        if (member.VType is "TYPE_ENUM" or "TYPE_FLAGS")
        {
            long? value = FieldRender.Number(objects, at, member.VSub);
            if (value == null) return "";

            // Matched against the unsigned reading, which is what hkxpack matches against. Six enums
            // in the table declare a negative value, `VariableType.VARIABLE_TYPE_INVALID = -1` among
            // them, and every member carrying one of those is an unsigned width. A stored 0xFF is
            // -1 signed and 255 unsigned; hkxpack prints 255, so naming it INVALID here would be a
            // reading the file's own text disagrees with.
            long printed = FieldRender.Unsigned(value.Value, member.VSub);

            var declared = member.EType == null ? null : types.Enum(owner, member.EType);
            foreach (var (name, number) in declared ?? Empty)
                if (number == printed) return name;

            return printed.ToString();
        }

        string? rendered = FieldRender.Render(objects, at, owner, member, reference, null, element,
                                             types, FieldRender.LikeHkxPack);
        if (rendered == null) return "";

        string shown = PanelFields.Shown(rendered);
        if (member.VType is not ("TYPE_STRINGPTR" or "TYPE_CSTRING")) return shown;
        return trim ? Trimmed(Escaped(shown)) : Escaped(shown);
    }

    private static readonly IReadOnlyDictionary<string, long> Empty =
        new Dictionary<string, long>(StringComparer.Ordinal);

    /// A value in the file is XML, so an expression holding a greater than sign is written `&gt;`
    /// there and the parser, which is a regex and not an XML reader, hands that through as it is.
    /// Every consumer has therefore always seen the escape rather than the character, so this reading
    /// has to produce it too.
    ///
    /// Written here rather than borrowed from the text editor on purpose. The point of this producer
    /// is that nothing needs that file, and reaching into it for three string replacements would be a
    /// dependency on the thing being retired.
    /// Only these four. A sweep of every unpacked vanilla file turns up `&#13;` and no other
    /// numeric reference, so a general escaper would be inventing rules the file does not use.
    /// A newline is written as itself; only the carriage return beside it is escaped.
    internal static string Escaped(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r", "&#13;");

    private static int Stride(string vsub) => vsub switch
    {
        "TYPE_BOOL" or "TYPE_CHAR" or "TYPE_INT8" or "TYPE_UINT8" => 1,
        "TYPE_INT16" or "TYPE_UINT16" or "TYPE_HALF" => 2,
        "TYPE_INT64" or "TYPE_UINT64" or "TYPE_ULONG" or "TYPE_POINTER"
            or "TYPE_STRINGPTR" or "TYPE_CSTRING" => 8,
        "TYPE_VECTOR4" or "TYPE_QUATERNION" => 16,
        "TYPE_QSTRANSFORM" => 48,
        "TYPE_TRANSFORM" or "TYPE_MATRIX4" => 64,
        "TYPE_REAL" or "TYPE_INT32" or "TYPE_UINT32" or "TYPE_ENUM" or "TYPE_FLAGS" => 4,
        _ => 0,
    };
}
