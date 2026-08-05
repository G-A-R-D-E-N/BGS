using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// Finding the model that goes with a behaviour, without guessing.
//
// A behaviour names its skeleton and its animations but never its model, because the game decides
// that elsewhere. So there is nothing to look up and the only honest options are to find exactly one
// obvious candidate on disk or to ask. Picking one of several would put a stranger's mesh on the
// skeleton and look like a bug in the binding rather than a bad guess.
//
// Folders are searched in order and the FIRST one holding any .nif decides the outcome. It is
// tempting to keep looking when that folder holds several, but "several here, one over there" is
// still a guess about which the user meant, so several is an answer, not a reason to continue.
public static class MeshLookup
{
    public sealed record Result(string? Path, string Reason)
    {
        public bool Found => Path != null;
    }

    /// The folders worth looking in, nearest first: beside the behaviour, then the project root,
    /// then beside the skeleton. The skeleton's folder earns its place because the mesh binds to the
    /// skeleton, and vanilla keeps the two together in CharacterAssets.
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
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }
}
