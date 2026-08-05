using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenCommonwealth.Services.Hkx;

// Two behaviour files, read as one change set. RepackCheck already compares a document with a
// version of itself position for position, which works because a repack cannot reorder anything.
// Two different edits of the same vanilla file can, so this walks the two object sequences with a
// lookahead instead and reports what one has that the other does not.
//
// Ids are meaningless across files, since hkxpack renumbers on every pack, so matching is on class
// and normalised contents. RepackCheck.Take already flattens every id to "#" for exactly that
// reason, and its output is what this consumes.
public static class BehaviourDiff
{
    public enum Kind
    {
        Added,
        Removed,
        Changed,
    }

    public readonly record struct Line(Kind Kind, string Class, string Where, string Was, string Now)
    {
        public override string ToString() => Kind switch
        {
            Kind.Added => $"added {Class} {Where}",
            Kind.Removed => $"removed {Class} {Where}",
            _ => $"{Class}.{Where}: {Was} -> {Now}",
        };
    }

    public sealed class Result
    {
        public readonly List<Line> Lines = new();
        public int Added => Lines.Count(l => l.Kind == Kind.Added);
        public int Removed => Lines.Count(l => l.Kind == Kind.Removed);
        public int Changed => Lines.Count(l => l.Kind == Kind.Changed);
        public bool Identical => Lines.Count == 0;

        public override string ToString() => Identical
            ? "the two files hold the same objects with the same values"
            : $"{Added} added, {Removed} removed, {Changed} value{(Changed == 1 ? "" : "s")} changed";
    }

    // How far ahead of a mismatch to look for the two sides lining up again. A behaviour is a few
    // thousand objects and a mod's edit is a handful of them, so a short window resynchronises on
    // the first shared object after the change. A full alignment would be quadratic on files this
    // size for no useful gain.
    private const int Window = 400;

    public static Result Compare(RepackCheck.Census left, RepackCheck.Census right)
    {
        var result = new Result();
        var a = left.InOrder;
        var b = right.InOrder;

        int i = 0, j = 0;
        while (i < a.Count && j < b.Count)
        {
            if (Same(a[i], b[j])) { i++; j++; continue; }

            var (skipA, skipB) = NextSync(a, b, i, j);
            if (skipA < 0)
            {
                // No resync inside the window, so the tails are treated as wholly different rather
                // than pretending to a match that was not found.
                break;
            }

            Emit(result, a.GetRange(i, skipA), b.GetRange(j, skipB));
            i += skipA;
            j += skipB;
        }

        Emit(result, a.GetRange(i, a.Count - i), b.GetRange(j, b.Count - j));
        return result;
    }

    private static bool Same(RepackCheck.Entry x, RepackCheck.Entry y) =>
        x.Class == y.Class && x.Body == y.Body;

    // The nearest point at which the two sequences agree again, preferring the smallest total skip so
    // a one object edit reads as one object rather than as a block.
    private static (int SkipA, int SkipB) NextSync(
        IReadOnlyList<RepackCheck.Entry> a, IReadOnlyList<RepackCheck.Entry> b, int i, int j)
    {
        int reachA = Math.Min(Window, a.Count - i);
        int reachB = Math.Min(Window, b.Count - j);

        for (int total = 1; total < reachA + reachB; total++)
            for (int da = 0; da <= Math.Min(total, reachA - 1); da++)
            {
                int db = total - da;
                if (db >= reachB) continue;
                if (Same(a[i + da], b[j + db])) return (da, db);
            }

        return (-1, -1);
    }

    // A removed object and an added one of the same class, in the same place, is one object whose
    // values moved. Reporting that as a delete and an insert hides the only thing anybody wanted to
    // know, which is which field is different.
    private static void Emit(Result result, List<RepackCheck.Entry> gone, List<RepackCheck.Entry> came)
    {
        var pairedRight = new bool[came.Count];
        var pairedLeft = new bool[gone.Count];

        for (int g = 0; g < gone.Count; g++)
            for (int c = 0; c < came.Count; c++)
            {
                if (pairedRight[c] || came[c].Class != gone[g].Class) continue;

                pairedLeft[g] = pairedRight[c] = true;
                foreach (var (field, before, after) in FieldDifferences(gone[g].Body, came[c].Body))
                    result.Lines.Add(new Line(Kind.Changed, gone[g].Class, field, before, after));
                break;
            }

        for (int g = 0; g < gone.Count; g++)
            if (!pairedLeft[g])
                result.Lines.Add(new Line(Kind.Removed, gone[g].Class, Name(gone[g].Body), Summarise(gone[g].Body), ""));
        for (int c = 0; c < came.Count; c++)
            if (!pairedRight[c])
                result.Lines.Add(new Line(Kind.Added, came[c].Class, Name(came[c].Body), "", Summarise(came[c].Body)));
    }

    private static readonly Regex NamedParam =
        new(@"<hkparam name=""(?<name>[^""]+)""(?:\s*/>|>(?<value>[^<]*)</hkparam>)", RegexOptions.Compiled);

    private static IEnumerable<(string Field, string Was, string Now)> FieldDifferences(string was, string now)
    {
        var before = Fields(was);
        var after = Fields(now);

        foreach (string key in before.Keys.Union(after.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            before.TryGetValue(key, out string? x);
            after.TryGetValue(key, out string? y);
            if ((x ?? "") == (y ?? "")) continue;
            yield return (key, x ?? "(absent)", y ?? "(absent)");
        }
    }

    private static Dictionary<string, string> Fields(string body)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in NamedParam.Matches(body))
        {
            // A repeated name is an array element's own field, not the object's. First wins, which is
            // the object's own, and the array as a whole still shows up through the body compare.
            string name = m.Groups["name"].Value;
            if (!fields.ContainsKey(name)) fields[name] = m.Groups["value"].Value.Trim();
        }
        return fields;
    }

    /// The object's own name if it has one, so a report says which state was removed rather than only
    /// that an hkbStateInfo was.
    private static string Name(string body)
    {
        var m = Regex.Match(body, @"<hkparam name=""name"">([^<]*)</hkparam>");
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    private static string Summarise(string body)
    {
        var fields = Fields(body);
        return string.Join(", ", fields.Where(f => f.Value.Length > 0 && f.Value.Length < 40)
                                       .Take(4).Select(f => $"{f.Key}={f.Value}"));
    }
}
