using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenCommonwealth.Services.Hkx;

// hkxpack renumbers every object when it packs, so a repack cannot be compared by id. What has to
// survive is the object count and the multiset of class names. A file that comes back short still
// loads, and then behaves wrongly, which is the failure this exists to refuse.
public static class RepackCheck
{
    private static readonly Regex ObjectHead =
        new(@"<hkobject class=""(?<cls>[A-Za-z0-9_]+)"" name=""#\d+""", RegexOptions.Compiled);

    public sealed class Census
    {
        public int Objects;
        public readonly Dictionary<string, int> ByClass = new(StringComparer.Ordinal);
    }

    public sealed class Drift
    {
        public int Before;
        public int After;
        public readonly List<string> Lost = new();
        public readonly List<string> Gained = new();

        public bool Clean => Before == After && Lost.Count == 0 && Gained.Count == 0;

        public override string ToString()
        {
            if (Clean) return $"kept all {Before} objects";

            var parts = new List<string>();
            if (Before != After) parts.Add($"went in with {Before} objects and came back with {After}");
            if (Lost.Count > 0) parts.Add("lost " + string.Join(", ", Lost));
            if (Gained.Count > 0) parts.Add("invented " + string.Join(", ", Gained));
            return string.Join("; ", parts);
        }
    }

    public static Census Take(string xml)
    {
        var census = new Census();
        foreach (Match m in ObjectHead.Matches(xml))
        {
            string cls = m.Groups["cls"].Value;
            census.Objects++;
            census.ByClass[cls] = census.ByClass.TryGetValue(cls, out int n) ? n + 1 : 1;
        }
        return census;
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

        return drift;
    }
}
