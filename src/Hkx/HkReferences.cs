using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class HkReferences
{

    public enum Held
    {
        Scalar,
        ListElement,
        StructListMember,
        StructMember,
    }

    public readonly record struct Site(
        string HolderId,
        string Target,
        string Field,
        int Index,
        string Member,
        Held How);

    public static IEnumerable<Site> In(HkObject obj)
    {
        foreach (var (field, value) in obj.Scalars)
            if (IsRef(value))
                yield return new Site(obj.Id, value[1..], field, -1, "", Held.Scalar);

        foreach (var (field, list) in obj.Lists)
            for (int i = 0; i < list.Count; i++)
                if (IsRef(list[i]))
                    yield return new Site(obj.Id, list[i][1..], field, i, "", Held.ListElement);

        foreach (var (field, rows) in obj.StructLists)
            for (int row = 0; row < rows.Count; row++)
                foreach (var (member, value) in rows[row])
                    if (IsRef(value))
                        yield return new Site(obj.Id, value[1..], field, row, member,
                                              Held.StructListMember);

        foreach (var (field, members) in obj.Structs)
            foreach (var (member, value) in members)
                if (IsRef(value))
                    yield return new Site(obj.Id, value[1..], field, -1, member, Held.StructMember);
    }

    public static IEnumerable<Site> In(BehaviourGraphModel model) =>
        model.Objects.SelectMany(In);

    public static HashSet<string> Targets(BehaviourGraphModel model)
    {
        var targets = new HashSet<string>();
        foreach (var site in In(model)) targets.Add(site.Target);
        return targets;
    }

    public static string Path(this Site site) => site.How switch
    {
        Held.StructListMember => $"{site.Field}[{site.Index}].{site.Member}",
        Held.StructMember => $"{site.Field}.{site.Member}",
        _ => site.Field,
    };

    private static bool IsRef(string value) => value.StartsWith('#');
}
