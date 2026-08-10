using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;











public static class MeshLookup
{
    public sealed record Result(string? Path, string Reason)
    {
        public bool Found => Path != null;
    }




    public static IEnumerable<string> Places(string behaviourPath, string? projectRoot,
                                             string? skeletonPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? folder in new[]
                 {
                     Path.GetDirectoryName(behaviourPath),
                     projectRoot,
                     skeletonPath == null ? null : Path.GetDirectoryName(skeletonPath),
                 })
            if (!string.IsNullOrEmpty(folder) && seen.Add(folder))
                yield return folder;
    }

    public static Result Find(IEnumerable<string> folders, Func<string, IReadOnlyList<string>> nifsIn)
    {
        foreach (string folder in folders)
        {
            var found = nifsIn(folder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            if (found.Count == 0) continue;

            if (found.Count == 1) return new Result(found[0], "found beside the file");

            return new Result(null,
                $"{found.Count} models sit in {Path.GetFileName(folder)} " +
                $"({string.Join(", ", found.Select(Path.GetFileName).Take(3))}" +
                (found.Count > 3 ? ", ..." : "") + "), so use Mesh... to say which one.");
        }

        return new Result(null, "no model found next to this file, so use Mesh... to point at one.");
    }

    public static Result Find(string behaviourPath, string? projectRoot, string? skeletonPath) =>
        Find(Places(behaviourPath, projectRoot, skeletonPath), OnDisk);

    private static IReadOnlyList<string> OnDisk(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.nif", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
