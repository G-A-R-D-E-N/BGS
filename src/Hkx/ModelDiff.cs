using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class ModelDiff
{

    public sealed record Difference(string Where, string What)
    {
        public override string ToString() => $"{Where}: {What}";
    }

    public sealed record Result(int Objects, int Compared, int Total, int Strided,
                                IReadOnlyList<Difference> Shown,
                                IReadOnlyDictionary<string, int> StridedBy)
    {
        public bool Clean => Total == 0;

        public override string ToString() =>
            $"{Objects} object(s), {Compared} field(s) compared, {Total} disagreement(s)" +
            (Strided == 0 ? "" : $", {Strided} where a reference formatter strides padded structs differently");
    }

    public delegate bool Strided(string owningClass, string field);

    public static Result Compare(BehaviourGraphModel a, BehaviourGraphModel b, int cap = 40,
                                 Strided? strided = null)
    {
        var shown = new List<Difference>();
        int total = 0, compared = 0, excused = 0;

        void Differ(string where, string what)
        {
            total++;
            if (shown.Count < cap) shown.Add(new Difference(where, what));
        }

        var excusedBy = new Dictionary<string, int>(StringComparer.Ordinal);

        void Excuse(string where, string what)
        {
            excused++;

            int bracket = where.IndexOf('[');
            int space = where.IndexOf(' ');
            string field = bracket > 0 && space > 0 ? where[(space + 1)..bracket] : where;
            excusedBy[field] = excusedBy.GetValueOrDefault(field) + 1;
        }

        if (a.Objects.Count != b.Objects.Count)
            Differ("the file", $"{a.Objects.Count} object(s) against {b.Objects.Count}");

        int count = Math.Min(a.Objects.Count, b.Objects.Count);
        for (int i = 0; i < count; i++)
        {
            var (left, right) = (a.Objects[i], b.Objects[i]);
            string where = $"#{left.Id} {left.Class}";

            compared += 2;
            if (left.Id != right.Id) Differ($"object {i}", $"id #{left.Id} against #{right.Id}");
            if (left.Class != right.Class) Differ($"object {i}", $"class {left.Class} against {right.Class}");

            CompareScalars(where, left.Scalars, right.Scalars, Differ, ref compared);
            CompareLists(where, left.Lists, right.Lists, Differ, ref compared);
            CompareStructs(where, left.Structs, right.Structs, Differ, ref compared);
            CompareStructLists(where, left.Class, left.StructLists, right.StructLists,
                               Differ, Excuse, strided, ref compared);
        }

        return new Result(count, compared, total, excused, shown, excusedBy);
    }

    private static void CompareScalars(string where, Dictionary<string, string> left,
                                       Dictionary<string, string> right,
                                       Action<string, string> differ, ref int compared)
    {
        foreach (string key in Keys(where, "scalar", left.Keys, right.Keys, differ))
        {
            compared++;
            if (!string.Equals(left[key], right[key], StringComparison.Ordinal))
                differ($"{where}.{key}", $"\"{left[key]}\" against \"{right[key]}\"");
        }
    }

    private static void CompareLists(string where, Dictionary<string, List<string>> left,
                                     Dictionary<string, List<string>> right,
                                     Action<string, string> differ, ref int compared)
    {
        foreach (string key in Keys(where, "array", left.Keys, right.Keys, differ))
        {
            var (l, r) = (left[key], right[key]);
            compared++;
            if (l.Count != r.Count)
            {
                differ($"{where}.{key}", $"{l.Count} element(s) against {r.Count}");
                continue;
            }

            for (int i = 0; i < l.Count; i++)
            {
                compared++;
                if (!string.Equals(l[i], r[i], StringComparison.Ordinal))
                    differ($"{where}.{key}[{i}]", $"\"{l[i]}\" against \"{r[i]}\"");
            }
        }
    }

    private static void CompareStructs(string where, Dictionary<string, Dictionary<string, string>> left,
                                       Dictionary<string, Dictionary<string, string>> right,
                                       Action<string, string> differ, ref int compared)
    {
        foreach (string key in Keys(where, "struct", left.Keys, right.Keys, differ))
            CompareScalars($"{where}.{key}", left[key], right[key], differ, ref compared);
    }

    private static void CompareStructLists(string where, string owningClass,
                                           Dictionary<string, List<Dictionary<string, string>>> left,
                                           Dictionary<string, List<Dictionary<string, string>>> right,
                                           Action<string, string> differ, Action<string, string> excuse,
                                           Strided? strided, ref int compared)
    {
        foreach (string key in Keys(where, "struct array", left.Keys, right.Keys, differ))
        {
            var (l, r) = (left[key], right[key]);
            compared++;

            if (l.Count != r.Count)
            {
                differ($"{where}.{key}", $"{l.Count} element(s) against {r.Count}");
                continue;
            }

            var collect = strided?.Invoke(owningClass, key) == true ? excuse : differ;
            for (int i = 0; i < l.Count; i++)
                CompareScalars($"{where}.{key}[{i}]", l[i], r[i], collect, ref compared);
        }
    }

    private static IEnumerable<string> Keys(string where, string what,
                                            IEnumerable<string> left, IEnumerable<string> right,
                                            Action<string, string> differ)
    {
        var mine = new HashSet<string>(left, StringComparer.Ordinal);
        var theirs = new HashSet<string>(right, StringComparer.Ordinal);

        foreach (string key in mine.Except(theirs).OrderBy(k => k, StringComparer.Ordinal))
            differ($"{where}.{key}", $"a {what} the second reading does not have");

        foreach (string key in theirs.Except(mine).OrderBy(k => k, StringComparer.Ordinal))
            differ($"{where}.{key}", $"a {what} the first reading does not have");

        return mine.Intersect(theirs).OrderBy(k => k, StringComparer.Ordinal).ToList();
    }
}
