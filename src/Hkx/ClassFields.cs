using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Hkx;

// The fields an object holds, worked out from what its class is rather than from hkxpack's text.
//
// A properties panel is a list of names before it is anything else, and that list has never come
// from the file: it came from unpacking the file to XML and reading the names back out. This builds
// the same list from the class table and the file's own bytes.
//
// Half of a real list belongs to objects written *inside* the object. A state machine's block
// carries its transitions, and each transition carries two time intervals, and all of their fields
// are shown as the machine's own. Walking into them needs three things the class table has and the
// game's own class dump does not: which class the struct is, how big one is, and which members are
// written to a file at all. How many there are is not in either, and is read out of the array's own
// header in the file.
public static class ClassFields
{
    /// A run of names deeper than this is a cycle rather than a graph. Nothing in the vanilla data
    /// goes past four.
    private const int Deepest = 8;

    /// The names, in the order the file writes them, or null when the walk hit something it could
    /// not resolve. Null is the useful answer: it says the list is unknown rather than short.
    public static List<string>? Of(PackfileObjects objects, PackfileObjects.Instance instance,
                                   HavokClassTypes? types = null) =>
        Walk(objects, types ?? HavokClassTypes.Shipped, instance.ClassName, instance.Offset, 0);

    private static List<string>? Walk(PackfileObjects objects, HavokClassTypes types,
                                      string className, int at, int depth)
    {
        if (depth > Deepest || !types.Knows(className)) return null;

        var names = new List<string>();
        foreach (var member in types.Members(className))
        {
            if (!member.Written) continue;

            int here = at + member.Offset;

            if (member.VType == "TYPE_STRUCT" && member.CType != null)
            {
                var inside = Walk(objects, types, member.CType, here, depth + 1);
                if (inside == null) return null;
                names.AddRange(inside);
                continue;
            }

            if (member.VType == "TYPE_ARRAY" && member.VSub == "TYPE_STRUCT" && member.CType != null)
            {
                var elements = objects.ArrayAt(here);
                if (elements == null) return null;
                if (elements.Count == 0) continue;

                int? stride = types[member.CType]?.Size;
                if (stride == null || stride <= 0) return null;

                for (int i = 0; i < elements.Count; i++)
                {
                    var inside = Walk(objects, types, member.CType, elements.At + i * stride.Value, depth + 1);
                    if (inside == null) return null;
                    names.AddRange(inside);
                }
                continue;
            }

            // Every other kind of array is written as its own block rather than as a value, and the
            // panel has never offered one for editing.
            if (member.VType is "TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY") continue;

            // A fixed length C array is not an array as far as the file is concerned: `hkReal[8]`
            // is written as eight fields named enabled1 to enabled8.
            if (member.ArrSize > 0)
            {
                for (int i = 1; i <= member.ArrSize; i++) names.Add(member.Name + i);
                continue;
            }

            names.Add(member.Name);
        }

        return names;
    }
}
