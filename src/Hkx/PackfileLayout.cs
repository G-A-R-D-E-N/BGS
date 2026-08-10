using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;



























public static class PackfileLayout
{


    public sealed record Item(string Kind, int At, int Length)
    {
        public bool IsString => Kind is "string" or "element string";

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

            foreach (var member in types.Members(className).OrderBy(m => m.Offset))
            {
                if (!member.Written) continue;
                int at = offset + member.Offset;

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

                var array = objects.ArrayAt(at);
                if (array == null || array.Count == 0) continue;
                if (!aims.ContainsKey(at)) continue;

                if (member.VSub == "TYPE_STRUCT" && member.CType != null && types.Knows(member.CType))
                {
                    int stride = types[member.CType]?.Size ?? 0;
                    if (stride <= 0) continue;

                    if (seen.Add(array.At))
                        items.Add(new Item("struct array", array.At, array.Count * stride));

                    for (int i = 0; i < array.Count; i++)
                        Walk(array.At + i * stride, member.CType, depth + 1);
                    continue;
                }

                int width = HavokClassTypes.Width(member.VSub);
                if (width <= 0) continue;

                string kind = member.VSub == "TYPE_POINTER" ? "pointer array"
                            : member.VSub is "TYPE_STRINGPTR" or "TYPE_CSTRING" ? "string array"
                            : "value array";

                if (seen.Add(array.At)) items.Add(new Item(kind, array.At, array.Count * width));

                if (member.VSub is not ("TYPE_STRINGPTR" or "TYPE_CSTRING")) continue;

                for (int i = 0; i < array.Count; i++)
                    if (aims.TryGetValue(array.At + i * 8, out int text) && seen.Add(text))
                        items.Add(new Item("element string", text, Zeroed(data.Data, text)));
            }
        }

        foreach (var instance in objects.Instances)
        {
            if (types[instance.ClassName]?.Size is not int size || size <= 0) return null;

            items.Add(new Item("object", instance.Offset, size));
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
            cursor = Align(cursor, Boundary(item.Kind, previous));
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
