using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;





















public static class FixupOrder
{









    public static void Reorder(PackfileImage image, HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;

        var data = image.Section("__data__");
        if (data == null) return;





        var objects = new PackfileObjects(image);
        if (objects.Instances.Any(i => !types.Knows(i.ClassName))) return;

        data.SetGlobals(InWalkOrder(data.Globals().ToList(), g => g.Source,
                                    Sources(objects, types, data, global: true)));

        data.SetLocals(InWalkOrder(data.Locals().ToList(), l => l.Source,
                                   Sources(objects, types, data, global: false)));
    }

    private static List<T> InWalkOrder<T>(List<T> entries, Func<T, int> sourceOf, List<int> order)
    {
        var rank = new Dictionary<int, int>();
        for (int i = 0; i < order.Count; i++) rank.TryAdd(order[i], i);


        return entries.Select((e, at) => (e, key: rank.TryGetValue(sourceOf(e), out int r) ? r : int.MaxValue, at))
                      .OrderBy(x => x.key).ThenBy(x => x.at)
                      .Select(x => x.e).ToList();
    }


    public static List<int> Sources(PackfileObjects objects, HavokClassTypes types,
                                    PackfileSection data, bool global)
    {
        var present = new HashSet<int>(global ? data.Globals().Select(g => g.Source)
                                              : data.Locals().Select(l => l.Source));
        var found = new List<int>();

        foreach (var instance in objects.Instances)
            Walk(objects, types, instance.Offset, instance.ClassName, present, found, global, 0);

        return found;
    }



    private static void Walk(PackfileObjects objects, HavokClassTypes types, int offset,
                             string className, HashSet<int> present, List<int> into,
                             bool global, int depth)
    {


        if (depth > 8) return;

        foreach (var member in types.Members(className).OrderBy(m => m.Offset))
        {
            if (!member.Written) continue;
            int at = offset + member.Offset;

            if (member.VType == "TYPE_POINTER")
            {
                if (global && present.Contains(at)) into.Add(at);
                continue;
            }

            if (member.VType is "TYPE_STRINGPTR" or "TYPE_CSTRING")
            {
                if (!global && present.Contains(at)) into.Add(at);
                continue;
            }

            if (member.VType == "TYPE_STRUCT")
            {
                if (member.CType != null && types.Knows(member.CType))
                    Walk(objects, types, at, member.CType, present, into, global, depth + 1);
                continue;
            }

            if (member.VType is not ("TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY")) continue;



            if (!global && present.Contains(at)) into.Add(at);

            var array = objects.ArrayAt(at);
            if (array == null || array.Count == 0) continue;

            if (member.VSub == "TYPE_POINTER")
            {
                if (!global) continue;
                for (int i = 0; i < array.Count; i++)
                    if (present.Contains(array.At + i * 8)) into.Add(array.At + i * 8);
            }
            else if (member.VSub is "TYPE_STRINGPTR" or "TYPE_CSTRING")
            {
                if (global) continue;
                for (int i = 0; i < array.Count; i++)
                    if (present.Contains(array.At + i * 8)) into.Add(array.At + i * 8);
            }
            else if (member.VSub == "TYPE_STRUCT" && member.CType != null && types.Knows(member.CType))
            {
                int stride = types[member.CType]?.Size ?? 0;
                if (stride <= 0) continue;

                for (int i = 0; i < array.Count; i++)
                    Walk(objects, types, array.At + i * stride, member.CType, present, into,
                         global, depth + 1);
            }
        }
    }
}
