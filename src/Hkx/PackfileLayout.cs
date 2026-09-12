using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class PackfileLayout
{

    public sealed record Item(string Kind, int At, int Length)
    {
        public bool IsString => Kind is "string" or "element string";

        // When set, the item is placed at this alignment instead of the kind-based boundary.
        // The converter uses it to snap relative-array payloads to the 16-byte lines the
        // FO4-era serializers pad array payloads to; the verify/rewrite tools leave it unset
        // and keep the plain boundary rules.
        public int Align;

        public override string ToString() => $"{Kind} at 0x{At:x} for {Length}";
    }

    public static List<Item>? Of(PackfileImage image, HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;

        var data = image.Section("__data__");
        if (data == null) return null;

        PackfileObjects objects;
        try { objects = new PackfileObjects(image); }
        catch (InvalidOperationException) { return null; }

        if (objects.Instances.Any(i => !types.Knows(i.ClassName))) return null;

        var aims = new Dictionary<int, int>();
        foreach (var (source, destination) in data.Locals()) aims[source] = destination;

        var items = new List<Item>();
        var seen = new HashSet<int>();

        void Walk(int offset, string className, int depth)
        {

            if (depth > 8) return;

            var laid = LayoutWalker.Active(types, className, objects.PointerWidth);
            if (laid == null) return;

            foreach (var member in types.Members(className)
                                       .OrderBy(m => laid.OffsetOf(m.Name) ?? m.Offset))
            {
                if (!member.Written) continue;
                int at = offset + (laid.OffsetOf(member.Name) ?? member.Offset);

                if (member.VType is "TYPE_STRINGPTR" or "TYPE_CSTRING")
                {
                    if (aims.TryGetValue(at, out int text) && seen.Add(text))
                        items.Add(new Item("string", text, Zeroed(data.Data, text)));
                    continue;
                }

                if (member.VType == "TYPE_STRUCT")
                {
                    if (member.CType != null && types.Knows(member.CType))
                        Walk(at, member.CType, depth + 1);
                    continue;
                }

                if (member.VType is not ("TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY")) continue;
                if (!aims.ContainsKey(at)) continue;

                if (member.VSub == "TYPE_STRUCT" && member.CType != null && types.Knows(member.CType))
                {
                    int stride = LayoutWalker.Active(types, member.CType, objects.PointerWidth)?.Size ?? 0;
                    if (stride <= 0) continue;

                    var structArray = objects.ArrayAt(at, stride);
                    if (structArray == null || structArray.Count == 0) continue;

                    if (seen.Add(structArray.At))
                        items.Add(new Item("struct array", structArray.At, structArray.Count * stride));

                    for (int i = 0; i < structArray.Count; i++)
                        Walk(structArray.At + i * stride, member.CType, depth + 1);
                    continue;
                }

                // hkUlong elements are pointer-sized and variants are two pointers, so both
                // arrays stride at the active pointer width, not the shipped eight-byte width.
                int width = member.VSub == "TYPE_VARIANT" ? 2 * objects.PointerWidth
                          : member.VSub is "TYPE_POINTER" or "TYPE_STRINGPTR" or "TYPE_CSTRING" or "TYPE_ULONG"
                              ? objects.PointerWidth
                              : HavokClassTypes.Width(member.VSub);
                if (width <= 0) continue;

                var array = objects.ArrayAt(at, width);
                if (array == null || array.Count == 0) continue;

                string kind = member.VSub == "TYPE_POINTER" ? "pointer array"
                            : member.VSub is "TYPE_STRINGPTR" or "TYPE_CSTRING" ? "string array"
                            : member.VSub == "TYPE_VARIANT" ? "variant array"
                            : "value array";

                if (seen.Add(array.At)) items.Add(new Item(kind, array.At, array.Count * width));

                if (member.VSub is not ("TYPE_STRINGPTR" or "TYPE_CSTRING")) continue;

                for (int i = 0; i < array.Count; i++)
                    if (aims.TryGetValue(array.At + i * objects.PointerWidth, out int text) && seen.Add(text))
                        items.Add(new Item("element string", text, Zeroed(data.Data, text)));
            }
        }

        foreach (var instance in objects.Instances)
        {
            var laid = LayoutWalker.Active(types, instance.ClassName, objects.PointerWidth);
            if (laid == null || laid.Size <= 0) return null;

            items.Add(new Item("object", instance.Offset, laid.Size));
            Walk(instance.Offset, instance.ClassName, 0);
        }

        return items;
    }

    public static List<int> Where(IReadOnlyList<Item> items)
    {
        var at = new List<int>(items.Count);
        int cursor = 0;
        string previous = "";

        foreach (var item in items)
        {
            cursor = Align(cursor, item.Align > 0 ? item.Align : Boundary(item.Kind, previous));
            at.Add(cursor);
            cursor += item.Length;
            previous = item.Kind;
        }

        return at;
    }

    public static bool Rewrite(PackfileImage image, HavokClassTypes? types = null)
    {
        var data = image.Section("__data__");
        if (data == null) return false;

        var items = Of(image, types);
        if (items == null) return false;

        if (!Accounted(items, data.Data.Length)) return false;

        return RewriteAs(image, items);
    }

    public static bool RewriteAs(PackfileImage image, IReadOnlyList<Item> items)
    {
        var data = image.Section("__data__");
        if (data == null) return false;

        var at = Where(items);

        var byOldOffset = items.Select((item, k) => (item, To: at[k]))
                               .OrderBy(x => x.item.At).ToList();

        int? Moved(int offset)
        {
            int low = 0, high = byOldOffset.Count - 1;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                var (item, to) = byOldOffset[mid];

                if (offset < item.At) high = mid - 1;
                else if (offset >= item.At + item.Length) low = mid + 1;
                else return to + (offset - item.At);
            }
            return null;
        }

        int end = items.Count == 0 ? 0 : at[^1] + items[^1].Length;
        var written = new byte[Align(end, NativeAppend.Alignment)];

        for (int k = 0; k < items.Count; k++)
        {
            if (items[k].At + items[k].Length > data.Data.Length) return false;
            Array.Copy(data.Data, items[k].At, written, at[k], items[k].Length);
        }

        var locals = new List<(int Source, int Destination)>();
        foreach (var (source, destination) in data.Locals())
        {
            if (Moved(source) is not int from || Moved(destination) is not int to) return false;
            locals.Add((from, to));
        }

        var globals = new List<(int Source, int Section, int Destination)>();
        foreach (var (source, section, destination) in data.Globals())
        {
            if (Moved(source) is not int from) return false;

            int to = section == image.Sections.IndexOf(data)
                     ? Moved(destination) ?? -1
                     : destination;
            if (to < 0) return false;

            globals.Add((from, section, to));
        }

        var virtuals = new List<(int Source, int Section, int Destination)>();
        foreach (var (source, section, destination) in data.Virtuals())
        {
            if (Moved(source) is not int from) return false;
            virtuals.Add((from, section, destination));
        }

        data.Data = written;
        data.SetLocals(locals);
        data.SetGlobals(globals);
        data.SetVirtuals(virtuals);
        return true;
    }

    public static List<List<Item>> ByObject(IReadOnlyList<Item> items)
    {
        var runs = new List<List<Item>>();

        foreach (var item in items)
        {
            if (item.Kind == "object" || runs.Count == 0) runs.Add(new List<Item>());
            runs[^1].Add(item);
        }

        return runs;
    }

    public static bool Reaches(IReadOnlyList<Item> items, PackfileSection data, int section)
    {
        var spans = items.Select(i => (i.At, End: i.At + i.Length)).OrderBy(x => x.At).ToList();

        bool Lands(int offset)
        {
            int low = 0, high = spans.Count - 1;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (offset < spans[mid].At) high = mid - 1;
                else if (offset >= spans[mid].End) low = mid + 1;
                else return true;
            }
            return false;
        }

        foreach (var (source, destination) in data.Locals())
            if (!Lands(source) || !Lands(destination)) return false;

        foreach (var (source, which, destination) in data.Globals())
            if (!Lands(source) || (which == section && !Lands(destination))) return false;

        foreach (var (source, _, _) in data.Virtuals())
            if (!Lands(source)) return false;

        return true;
    }

    public static bool Accounted(IReadOnlyList<Item> items, int length)
    {
        int covered = 0;
        foreach (var item in items.OrderBy(i => i.At))
        {
            if (item.At - covered >= NativeAppend.Alignment) return false;
            covered = Math.Max(covered, item.At + item.Length);
        }

        return length - covered < NativeAppend.Alignment;
    }

    private static int Boundary(string kind, string previous) => kind switch
    {
        // The default relative-array placement packs payloads right after the object's struct
        // bytes in member order. It is only a placement policy — the header's stored offset is
        // authoritative and is rewritten to wherever the payload lands — and the converter
        // overrides it with 16-byte line alignment via Item.Align when it builds its blocks.
        "relarray" => 1,

        "element string" => 2,

        "string" when previous.EndsWith("array", StringComparison.Ordinal) => 1,

        _ => NativeAppend.Alignment,
    };

    private static int Zeroed(byte[] data, int at)
    {
        int end = Array.IndexOf(data, (byte)0, at);
        return end < 0 ? data.Length - at : end - at + 1;
    }

    public static int Align(int value, int to) => (value + to - 1) / to * to;
}
