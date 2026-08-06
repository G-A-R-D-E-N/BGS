using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// Two readings of one file, set beside each other field by field.
//
// This exists ahead of the thing it is meant to check. The graph model is built by regex over the
// text hkxpack produces, and it is going to be built from the file's own bytes instead; the only
// way to know the second one is the first one is to compare every field of every object and find
// nothing. So the comparison is written first, and proved to fail on faults put there on purpose,
// because a comparison that cannot fail cannot pass either.
//
// Strict on purpose, in three ways that each cost something:
//
// The key sets are compared, not just the values. A field one side does not have at all is the
// failure that would otherwise pass silently, because nothing asks for a key that is not there.
//
// Values are compared as they are, with no trimming. Six vanilla values carry meaningful leading or
// trailing spaces, and a comparison that tidies them up is agreeing with itself rather than with
// the file.
//
// Objects are compared by position rather than by id. The ids are hkxpack's own numbering and are
// part of what is being checked, so matching on them first would assume the answer.
public static class ModelDiff
{
    /// Where the two disagree, named well enough to find it in the file. `Where` is the object and
    /// field, `What` is what each side said.
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
            (Strided == 0 ? "" : $", {Strided} where hkxpack strides a padded struct wrongly");
    }

    /// Whether a field is an array of a struct hkxpack measures the wrong size for, given the class
    /// that owns it and the field's name.
    ///
    /// This is the one excuse the comparison accepts, and it is narrow on purpose. A struct holding
    /// a vector is aligned to sixteen and the compiler pads it; hkxpack has no size in its data and
    /// rounds the last member up to eight, so every element after the first in an array of one of
    /// those is read from the wrong place. By hkxpack, not by us, because our size comes from the
    /// game's own class registration.
    ///
    /// It excuses nothing else. Differences under a field it does not name still count, and the
    /// count of what it did excuse is reported rather than folded away, because an excuse nobody
    /// sees is indistinguishable from a reading that agrees.
    public delegate bool Strided(string owningClass, string field);

    /// `a` is the reading being trusted, `b` the one being checked. Stops collecting examples after
    /// `cap` of them but keeps counting, so a producer that is wrong about everything reports how
    /// wrong without filling memory with it.
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

        // Counted, not shown and not totalled. Deciding this as the difference is found rather than
        // by picking it back out of the list afterwards is what makes it independent of the cap: an
        // earlier attempt filtered the shown examples, which meant a file with more differences than
        // the cap lost its examples and kept its count, and reported a wall of nothing.
        var excusedBy = new Dictionary<string, int>(StringComparer.Ordinal);

        void Excuse(string where, string what)
        {
            excused++;

            // Which field, counted. An excuse nobody can see the shape of is one nobody can check,
            // and the whole claim rests on it covering only the handful of classes it should.
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

            // The count is never excused. Both sides read it out of the array header rather than by
            // walking the elements, so a stride that is wrong cannot change it, and a count that
            // disagrees is something else entirely.
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

    /// The keys both sides have, reporting the ones only one side has on the way through. Ordered so
    /// the same two models always report the same thing in the same order, which is what makes a run
    /// worth comparing against the last one.
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
