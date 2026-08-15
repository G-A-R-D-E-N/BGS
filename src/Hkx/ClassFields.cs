using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class ClassFields
{

    private const int Deepest = 8;

    public sealed record Field(string Name, string Owner, int At, HavokClassTypes.Member Member,
                               int Element = 0, string Path = "", string Group = "")
    {
        public override string ToString() => $"{Owner}.{Name} at 0x{At:x}";
    }

    public static List<Field>? Of(PackfileObjects objects, PackfileObjects.Instance instance,
                                  HavokClassTypes? types = null) =>
        Walk(objects, types ?? HavokClassTypes.Shipped, instance.ClassName, instance.Offset, 0, "", "");

    public static List<string>? NamesOf(PackfileObjects objects, PackfileObjects.Instance instance,
                                        HavokClassTypes? types = null) =>
        Of(objects, instance, types)?.Select(f => f.Name).ToList();

    private static List<Field>? Walk(PackfileObjects objects, HavokClassTypes types,
                                     string className, int at, int depth, string under, string group)
    {
        if (depth > Deepest || !types.Knows(className)) return null;

        var fields = new List<Field>();
        foreach (var member in types.Members(className))
        {
            if (!member.Written) continue;

            int here = at + member.Offset;
            string path = under.Length == 0 ? member.Name : under + "." + member.Name;

            if (member.VType == "TYPE_STRUCT")
            {

                if (member.CType == null) return null;

                var inside = Walk(objects, types, member.CType, here, depth + 1, path, group);
                if (inside == null) return null;
                fields.AddRange(inside);
                continue;
            }

            if (member.VType == "TYPE_ARRAY" && member.VSub == "TYPE_STRUCT" && member.CType != null)
            {
                int? stride = types[member.CType]?.Size;
                if (stride == null || stride <= 0) return null;

                var elements = objects.ArrayAt(here, stride.Value);
                if (elements == null) return null;
                if (elements.Count == 0) continue;

                for (int i = 0; i < elements.Count; i++)
                {
                    string element = $"{path}[{i}]";
                    var inside = Walk(objects, types, member.CType,
                                      elements.At + i * stride.Value, depth + 1, element, element);
                    if (inside == null) return null;
                    fields.AddRange(inside);
                }
                continue;
            }

            if (member.VType is "TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY") continue;

            if (member.ArrSize > 0)
            {
                for (int i = 1; i <= member.ArrSize; i++)
                {
                    string numbered = member.Name + i;
                    fields.Add(new Field(numbered, className, here, member, i - 1,
                                         under.Length == 0 ? numbered : under + "." + numbered, group));
                }
                continue;
            }

            fields.Add(new Field(member.Name, className, here, member, 0, path, group));
        }

        return fields;
    }
}
