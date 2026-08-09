using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// Where one object names another.
//
// Three separate walks used to answer this, one per question: which objects nothing points at, which
// objects point at this one, and which places have to be cleared before this one is deleted. They
// agreed on scalars, lists and struct lists and disagreed about structs, so a payload hung off a
// named struct was reachable to one of them and invisible to the other two. Deleting it was allowed
// and the link into it was left dangling, which is the failure the struct list arm of the clearing
// walk already carries a comment about having caused once before.
//
// The walk lives here once and says only where a reference sits. What to do about one stays with the
// caller: collecting holders, filtering to node classes and clearing a field are three different
// jobs and none of them belong to the walk.
public static class HkReferences
{
    /// How a reference is held, which decides how it can be cleared. A list element is dropped; the
    /// other three are written to null in place.
    public enum Held
    {
        Scalar,
        ListElement,
        StructListMember,
        StructMember,
    }

    /// One place one object names another.
    ///
    /// `Field` is the parameter on the holder. `Index` is the element for a list or struct list and
    /// -1 otherwise. `Member` is the field inside a struct or struct list element and empty
    /// otherwise. Together they are enough to write the site back without walking again.
    public readonly record struct Site(
        string HolderId,
        string Target,
        string Field,
        int Index,
        string Member,
        Held How);

    /// Every reference held by one object.
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

        // A named nested object, which is what an event property or a bone binding is. The parser
        // files these under Structs and the nameless ones under StructLists, so the two look alike
        // in the file and are different things in the model.
        foreach (var (field, members) in obj.Structs)
            foreach (var (member, value) in members)
                if (IsRef(value))
                    yield return new Site(obj.Id, value[1..], field, -1, member, Held.StructMember);
    }

    /// Every reference in the file.
    public static IEnumerable<Site> In(BehaviourGraphModel model) =>
        model.Objects.SelectMany(In);

    /// Every object named by something, by id. The answer to "is anything pointing at this".
    public static HashSet<string> Targets(BehaviourGraphModel model)
    {
        var targets = new HashSet<string>();
        foreach (var site in In(model)) targets.Add(site.Target);
        return targets;
    }

    /// The path `HkxTextEdit.SetParamAt` wants for this site. Only meaningful for the three kinds
    /// that are cleared in place; a list element is removed by index instead.
    public static string Path(this Site site) => site.How switch
    {
        Held.StructListMember => $"{site.Field}[{site.Index}].{site.Member}",
        Held.StructMember => $"{site.Field}.{site.Member}",
        _ => site.Field,
    };

    private static bool IsRef(string value) => value.StartsWith('#');
}
