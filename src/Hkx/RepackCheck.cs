using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenCommonwealth.Services.Hkx;

// hkxpack renumbers every object when it packs, so a repack cannot be compared by id. Counting
// objects and class names catches a file that comes back short, which still loads and then behaves
// wrongly. It does not catch a value coming back different, which loads and behaves wrongly in a way
// nothing reports at all, so the contents are compared as well.
//
// Both are safe to compare because hkxpack preserves object order and every value verbatim.
// Measured over 8 shipped behaviour files, 21220 objects between them, including the two largest in
// the game: with ids normalised, a dump and a dump of its own repack are identical line for line,
// and the class sequence matches position for position.
public static class RepackCheck
{
    private static readonly Regex ObjectHead =
        new(@"<hkobject class=""(?<cls>[A-Za-z0-9_]+)"" name=""#(?<id>\d+)""", RegexOptions.Compiled);

    private static readonly Regex AnyId = new(@"#\d+", RegexOptions.Compiled);

    public sealed record Entry(string Id, string Class, string Body);

    public sealed class Census
    {
        public int Objects;
        public readonly Dictionary<string, int> ByClass = new(StringComparer.Ordinal);
        public readonly List<Entry> InOrder = new();
    }

    public sealed class Drift
    {
        public int Before;
        public int After;
        public readonly List<string> Lost = new();
        public readonly List<string> Gained = new();
        public readonly List<string> Changed = new();

        public bool Clean => Before == After && Lost.Count == 0 && Gained.Count == 0 && Changed.Count == 0;

        public override string ToString()
        {
            if (Clean) return $"kept all {Before} objects and every value in them";

            var parts = new List<string>();
            if (Before != After) parts.Add($"went in with {Before} objects and came back with {After}");
            if (Lost.Count > 0) parts.Add("lost " + string.Join(", ", Lost));
            if (Gained.Count > 0) parts.Add("invented " + string.Join(", ", Gained));
            if (Changed.Count > 0)
                parts.Add($"changed {Changed.Count} value{(Changed.Count == 1 ? "" : "s")}: " +
                          string.Join("; ", Changed.Take(3)) +
                          (Changed.Count > 3 ? $", and {Changed.Count - 3} more" : ""));
            return string.Join("; ", parts);
        }
    }

    public static Census Take(string xml)
    {
        var census = new Census();
        var marks = ObjectHead.Matches(xml);

        for (int i = 0; i < marks.Count; i++)
        {
            string cls = marks[i].Groups["cls"].Value;
            census.Objects++;
            census.ByClass[cls] = census.ByClass.TryGetValue(cls, out int n) ? n + 1 : 1;

            int start = marks[i].Index + marks[i].Length;
            int end = i + 1 < marks.Count ? marks[i + 1].Index : xml.Length;

            // Every id is flattened, the object's own and the ones it points at. Renumbering is the
            // one difference a repack is allowed to make, so it must not read as a change, and
            // ordering is what preserves which reference is which.
            census.InOrder.Add(new Entry(marks[i].Groups["id"].Value, cls, Normalise(xml[start..end])));
        }
        return census;
    }

    private static string Normalise(string body)
    {
        var lines = AnyId.Replace(body, "#").Split('\n');
        return string.Join("\n", lines.Select(l => l.Trim()).Where(l => l.Length > 0));
    }

    public static Drift Compare(Census before, Census after)
    {
        var drift = new Drift { Before = before.Objects, After = after.Objects };

        foreach (string cls in before.ByClass.Keys.Union(after.ByClass.Keys).OrderBy(c => c))
        {
            before.ByClass.TryGetValue(cls, out int was);
            after.ByClass.TryGetValue(cls, out int now);
            if (now < was) drift.Lost.Add($"{was - now} {cls}");
            else if (now > was) drift.Gained.Add($"{now - was} {cls}");
        }

        // Position for position only while the two agree on how many objects there are. Once the
        // counts differ, everything after the first missing object shifts and every later comparison
        // is noise on top of the real fault, which is already reported above.
        if (before.Objects != after.Objects) return drift;

        for (int i = 0; i < before.InOrder.Count; i++)
        {
            var was = before.InOrder[i];
            var now = after.InOrder[i];
            if (was.Class == now.Class && was.Body == now.Body) continue;

            drift.Changed.Add(was.Class != now.Class
                ? $"#{was.Id} was a {was.Class} and came back a {now.Class}"
                : $"#{was.Id} {was.Class}.{FirstDifference(was.Body, now.Body)}");
        }

        return drift;
    }

    /// The name of the first field whose line differs, so a report says what moved rather than only
    /// that something did.
    private static string FirstDifference(string was, string now)
    {
        var a = was.Split('\n');
        var b = now.Split('\n');

        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            string left = i < a.Length ? a[i] : "";
            string right = i < b.Length ? b[i] : "";
            if (left == right) continue;

            var name = Regex.Match(left.Length > 0 ? left : right, @"name=""([^""]+)""");
            return name.Success ? name.Groups[1].Value : "contents";
        }
        return "contents";
    }
}
