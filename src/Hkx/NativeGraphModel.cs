using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class NativeGraphModel
{

    public const int FirstId = 90;

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

    private static void Fill(PackfileObjects objects, HavokClassTypes types, HkObject obj,
                            int offset, string className, FieldRender.Reference reference)
    {
        var layout = LayoutWalker.Active(types, className, objects.PointerWidth);
        if (layout == null) return;

        foreach (var member in types.Members(className))
        {
            if (!member.Written) continue;
            int at = offset + (layout.OffsetOf(member.Name) ?? member.Offset);

            if (member.VType == "TYPE_STRUCT")
            {

                if (member.CType != null && types.Knows(member.CType))
                {
                    obj.Structs[member.Name] = Flatten(objects, types, at, member.CType, reference);
                    Leak(objects, types, obj, at, member.CType, member.Name);
                }
                continue;
            }

            if (member.VType is "TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY")
            {
                Array(objects, types, obj, offset, at, member, reference);
                continue;
            }

            if (member.ArrSize > 0)
            {

                for (int e = 0; e < member.ArrSize; e++)
                    obj.Scalars[member.Name + (e + 1)] =
                        Text(objects, at, className, member, reference, e, types);
                continue;
            }

            obj.Scalars[member.Name] = Text(objects, at, className, member, reference, 0, types);
        }
    }

    private static Dictionary<string, string> Flatten(PackfileObjects objects, HavokClassTypes types,
                                                      int offset, string className,
                                                      FieldRender.Reference reference)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        var layout = LayoutWalker.Active(types, className, objects.PointerWidth);
        if (layout == null) return fields;

        foreach (var member in types.Members(className))
        {
            if (!member.Written) continue;
            int at = offset + (layout.OffsetOf(member.Name) ?? member.Offset);

            if (member.VType == "TYPE_STRUCT")
            {
                fields[member.Name] = "";
                continue;
            }

            if (member.VType is "TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY")
            {
                fields[member.Name] = member.VSub is "TYPE_STRUCT" or "TYPE_STRINGPTR" or "TYPE_CSTRING"
                    ? ""
                    : string.Join(" ", Elements(objects, types, offset, at, member, reference));
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

    private static void Leak(PackfileObjects objects, HavokClassTypes types, HkObject obj,
                             int offset, string className, string under)
    {
        var layout = LayoutWalker.Active(types, className, objects.PointerWidth);
        if (layout == null) return;

        foreach (var member in types.Members(className))
        {
            if (!member.Written) continue;
            if (member.VType is not ("TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY")) continue;
            if (member.VSub is not ("TYPE_STRINGPTR" or "TYPE_CSTRING")) continue;

            int at = offset + (layout.OffsetOf(member.Name) ?? member.Offset);
            var values = member.VType == "TYPE_RELARRAY"
                ? objects.ReadRelStringArrayAt(offset, at)
                : objects.ReadStringArrayAt(at);
            if (values == null || values.Count == 0) continue;

            if (!obj.Lists.TryGetValue(under, out var list)) obj.Lists[under] = list = new List<string>();
            foreach (string? value in values) list.Add(Trimmed(Escaped(value ?? "")));
        }
    }

    private static void Array(PackfileObjects objects, HavokClassTypes types, HkObject obj,
                              int structStart, int at, HavokClassTypes.Member member,
                              FieldRender.Reference reference)
    {
        if (member.VSub == "TYPE_STRUCT")
        {
            obj.Lists[member.Name] = new List<string>();
            if (member.CType == null || !types.Knows(member.CType)) return;

            int stride = LayoutWalker.Active(types, member.CType, objects.PointerWidth)?.Size ?? 0;
            if (stride <= 0) return;

            PackfileObjects.IArraySpan? array = member.VType == "TYPE_RELARRAY"
                ? objects.RelArrayAt(structStart, at, stride)
                : objects.ArrayAt(at, stride);
            if (array == null || array.Count == 0) return;

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

            var values = member.VType == "TYPE_RELARRAY"
                ? objects.ReadRelStringArrayAt(structStart, at)
                : objects.ReadStringArrayAt(at);
            obj.Lists[member.Name] = values == null
                ? new List<string>()
                : values.Select(v => Trimmed(Escaped(v ?? ""))).ToList();
            return;
        }

        string joined = string.Join(" ", Elements(objects, types, structStart, at, member, reference));
        obj.Lists[member.Name] = joined
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    internal static IEnumerable<string> Elements(PackfileObjects objects, HavokClassTypes types,
                                                 int structStart, int at,
                                                 HavokClassTypes.Member member,
                                                 FieldRender.Reference reference)
    {
        if (member.VType == "TYPE_RELARRAY")
        {
            // Relative arrays are read through their own uint16(size+1)+uint16(relative-offset)
            // header; the payload sits at structStart + storedOffset, never behind a fixup.
            if (member.VSub == "TYPE_POINTER")
            {
                var targets = objects.ReadRelRefArrayAt(structStart, at);
                if (targets == null) yield break;

                foreach (var target in targets) yield return reference(target, target == null);
                yield break;
            }

            if (member.VSub == "TYPE_VARIANT")
            {
                var targets = objects.ReadRelVariantArrayAt(structStart, at);
                if (targets == null) yield break;

                foreach (var target in targets) yield return reference(target, target == null);
                yield break;
            }

            int relStride = RelElementWidth(types, objects.PointerWidth, member);
            if (relStride <= 0) yield break;

            var rel = objects.RelArrayAt(structStart, at, relStride);
            if (rel == null) yield break;

            var relElement = new HavokClassTypes.Member { Name = member.Name, VType = member.VSub };
            for (int e = 0; e < rel.Count; e++)
                yield return FieldRender.Render(objects, rel.At + e * relStride, "", relElement, reference,
                                                null, 0, types, FieldRender.ReferenceText) ?? "";
            yield break;
        }

        if (member.VSub == "TYPE_POINTER")
        {
            var targets = objects.ReadRefArrayAt(at);
            if (targets == null) yield break;

            foreach (var target in targets) yield return reference(target, target == null);
            yield break;
        }

        if (member.VSub == "TYPE_VARIANT")
        {
            var targets = objects.ReadVariantArrayAt(at);
            if (targets == null) yield break;

            foreach (var target in targets) yield return reference(target, target == null);
            yield break;
        }

        int stride = ElementWidth(member.VSub, objects.PointerWidth);
        if (stride <= 0) yield break;

        var array = objects.ArrayAt(at, stride);
        if (array == null) yield break;

        var element = new HavokClassTypes.Member { Name = member.Name, VType = member.VSub };
        for (int e = 0; e < array.Count; e++)
            yield return FieldRender.Render(objects, array.At + e * stride, "", element, reference,
                                            null, 0, types, FieldRender.ReferenceText) ?? "";
    }

    internal static string Trimmed(string value) => value.Trim();

    internal static string Text(PackfileObjects objects, int at, string owner,
                               HavokClassTypes.Member member, FieldRender.Reference reference,
                               int element, HavokClassTypes types, bool trim = true)
    {

        if (member.VType is "TYPE_ENUM" or "TYPE_FLAGS")
        {
            long? value = FieldRender.Number(objects, at, member.VSub);
            if (value == null) return "";

            long printed = FieldRender.Unsigned(value.Value, member.VSub);

            var declared = member.EType == null ? null : types.Enum(owner, member.EType);
            foreach (var (name, number) in declared ?? Empty)
                if (number == printed) return name;

            return printed.ToString();
        }

        string? rendered = FieldRender.Render(objects, at, owner, member, reference, null, element,
                                             types, FieldRender.ReferenceText);
        if (rendered == null) return "";

        string shown = PanelFields.Shown(rendered);
        if (member.VType is not ("TYPE_STRINGPTR" or "TYPE_CSTRING")) return shown;
        return trim ? Trimmed(Escaped(shown)) : Escaped(shown);
    }

    private static readonly IReadOnlyDictionary<string, long> Empty =
        new Dictionary<string, long>(StringComparer.Ordinal);

    internal static string Escaped(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r", "&#13;");

    // The element width of a relative array's payload at a pointer width, mirroring the
    // converter's RelElementWidth: pointers, strings and hkUlong are pointer-sized, variants
    // are two pointers, and structs use their laid-out size.
    internal static int RelElementWidth(HavokClassTypes types, int pointer,
                                        HavokClassTypes.Member member)
    {
        if (member.VSub is "TYPE_POINTER" or "TYPE_STRINGPTR" or "TYPE_CSTRING") return pointer;
        if (member.VSub == "TYPE_VARIANT") return 2 * pointer;
        if (member.VSub == "TYPE_ULONG") return pointer;
        if (member.VSub == "TYPE_STRUCT")
            return member.CType != null && types.Knows(member.CType)
                ? LayoutWalker.Active(types, member.CType, pointer)?.Size ?? 0
                : 0;
        return HavokClassTypes.Width(member.VSub);
    }

    internal static int ElementWidth(string vsub, int pointer) => vsub switch
    {
        "TYPE_BOOL" or "TYPE_CHAR" or "TYPE_INT8" or "TYPE_UINT8" => 1,
        "TYPE_INT16" or "TYPE_UINT16" or "TYPE_HALF" => 2,
        "TYPE_INT64" or "TYPE_UINT64" => 8,
        "TYPE_ULONG" or "TYPE_POINTER" or "TYPE_STRINGPTR" or "TYPE_CSTRING" => pointer,
        "TYPE_VARIANT" => 2 * pointer,
        "TYPE_VECTOR4" or "TYPE_QUATERNION" => 16,
        "TYPE_QSTRANSFORM" => 48,
        "TYPE_TRANSFORM" or "TYPE_MATRIX4" => 64,
        "TYPE_REAL" or "TYPE_INT32" or "TYPE_UINT32" or "TYPE_ENUM" or "TYPE_FLAGS" => 4,
        _ => 0,
    };
}
