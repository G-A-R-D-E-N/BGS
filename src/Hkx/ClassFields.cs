using System;
using System.Collections.Generic;
using System.Linq;

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
//
// Each field comes back with the offset it sits at, which is the part that makes it useful rather
// than merely correct: a name on its own cannot be read, because a struct written inside an object
// is at no offset that object's class describes.
public static class ClassFields
{
    /// A run of names deeper than this is a cycle rather than a graph. Nothing in the vanilla data
    /// goes past four.
    private const int Deepest = 8;

    /// One value as the file writes it. `Owner` is the class that declares the member, which is not
    /// the class of the object when the field belongs to a struct written inside it. `Element` picks
    /// one out of a fixed length array, where `hkReal[8]` is written as eight separate fields.
    public sealed record Field(string Name, string Owner, int At, HavokClassTypes.Member Member,
                               int Element = 0)
    {
        public override string ToString() => $"{Owner}.{Name} at 0x{At:x}";
    }

    /// The fields, in the order the file writes them, or null when the walk hit something it could
    /// not resolve. Null is the useful answer: it says the list is unknown rather than short.
    public static List<Field>? Of(PackfileObjects objects, PackfileObjects.Instance instance,
                                  HavokClassTypes? types = null) =>
        Walk(objects, types ?? HavokClassTypes.Shipped, instance.ClassName, instance.Offset, 0);

    /// Just the names, which is what a comparison against hkxpack's list needs.
    public static List<string>? NamesOf(PackfileObjects objects, PackfileObjects.Instance instance,
                                        HavokClassTypes? types = null) =>
        Of(objects, instance, types)?.Select(f => f.Name).ToList();

    private static List<Field>? Walk(PackfileObjects objects, HavokClassTypes types,
                                     string className, int at, int depth)
    {
        if (depth > Deepest || !types.Knows(className)) return null;

        var fields = new List<Field>();
        foreach (var member in types.Members(className))
        {
            if (!member.Written) continue;

            int here = at + member.Offset;

            if (member.VType == "TYPE_STRUCT")
            {
                // Nothing to walk into and no way to skip it honestly: the fields of that struct
                // belong in the list and we cannot name them, so the list is unknown rather than
                // short by however many they are.
                if (member.CType == null) return null;

                var inside = Walk(objects, types, member.CType, here, depth + 1);
                if (inside == null) return null;
                fields.AddRange(inside);
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
                    fields.AddRange(inside);
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
                for (int i = 1; i <= member.ArrSize; i++)
                    fields.Add(new Field(member.Name + i, className, here, member, i - 1));
                continue;
            }

            fields.Add(new Field(member.Name, className, here, member));
        }

        return fields;
    }
}
