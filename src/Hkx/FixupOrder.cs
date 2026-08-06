using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// What order the two pointer tables are in.
//
// Position in these tables is not free, which is not obvious and cost a day to find out. Moving an
// array's element entries to the end of the global table makes hkxpack read every element of that
// array as null, while our own reader, which looks entries up by source, reads it perfectly.
// Sorting the table by source, on the theory that something binary searches it, makes hkxpack
// misread more than a hundred fields instead.
//
// The order is the order the writer walked the objects:
//
//   objects in the order they sit in the file, and inside an object its members in offset order,
//   stepping into an array or an inline struct at the point the member holding it is reached rather
//   than after the object is finished.
//
// That is why the table runs backwards in places. An array's elements live elsewhere in the section,
// so reaching the array field emits entries with much larger offsets, and then the walk carries on
// with the fields after it. On Dogmeat 22 of the 1,151 steps go backwards and every one is an array.
//
// Measured, not assumed: the rule reproduces the exact order of both tables in all 533 vanilla
// behaviour files, 46,599 entries, with no file out of order and no entry unaccounted for.
public static class FixupOrder
{
    /// The two tables rewritten into the order the walk implies.
    ///
    /// Used after an edit adds entries. Appending was fine while every entry a change touched
    /// already existed, because setting one leaves it where it is; it stops being fine the moment
    /// something is added, and an array going from empty to holding something adds one.
    ///
    /// An entry the walk does not reach keeps its position relative to the others rather than being
    /// dropped. Nothing should produce one, and quietly losing a pointer would be far worse than
    /// leaving it in the wrong place.
    public static void Reorder(PackfileImage image, HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;

        var data = image.Section("__data__");
        if (data == null) return;

        // Read again from the edited image rather than reusing the view the caller had. That view
        // resolved its pointers when it was built, so after an array has been repointed it still
        // answers with the run the array used to hold, and the walk would predict a run of sources
        // the file no longer has.
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

        // Anything the walk did not reach sorts after everything it did, keeping the order it had.
        return entries.Select((e, at) => (e, key: rank.TryGetValue(sourceOf(e), out int r) ? r : int.MaxValue, at))
                      .OrderBy(x => x.key).ThenBy(x => x.at)
                      .Select(x => x.e).ToList();
    }

    /// Every source the walk touches, in the order it touches them.
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

    /// Only sources the file actually has an entry for. A null pointer has no entry, so predicting
    /// one would put the whole sequence out of step from that point on.
    private static void Walk(PackfileObjects objects, HavokClassTypes types, int offset,
                             string className, HashSet<int> present, List<int> into,
                             bool global, int depth)
    {
        // A class that somehow holds itself would otherwise walk forever. Nothing in the corpus
        // nests more than three deep.
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

            // The array's own pointer at its run, which is a local. It comes before the run's
            // contents, because the walk reaches the field before it reaches what the field holds.
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
