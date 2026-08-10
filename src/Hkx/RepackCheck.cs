using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenCommonwealth.Services.Hkx;










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
