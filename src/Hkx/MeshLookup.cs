using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Nif;

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

            if (found.Count == 1)
                return new Result(found[0], $"found under {Path.GetFileName(folder)}");

            return new Result(null,
                $"{found.Count} models sit in {Path.GetFileName(folder)} " +
                $"({string.Join(", ", found.Select(Path.GetFileName).Take(3))}" +
                (found.Count > 3 ? ", ..." : "") + "), so use Mesh... to say which one.");
        }

        return new Result(null, "no model found next to this file, so use Mesh... to point at one.");
    }

    public static Result Find(string behaviourPath, string? projectRoot, string? skeletonPath)
    {
        string? actorRoot = !string.IsNullOrEmpty(projectRoot)
            ? projectRoot
            : skeletonPath == null ? null : Path.GetDirectoryName(skeletonPath);
        actorRoot ??= Path.GetDirectoryName(behaviourPath);

        return string.IsNullOrEmpty(actorRoot)
            ? new Result(null, "no model search root is available, so use Mesh... to point at one.")
            : Find(new[] { actorRoot }, OnDisk);
    }

    private static IReadOnlyList<string> OnDisk(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.nif", SearchOption.AllDirectories)
                           .Where(IsMesh)
                           .ToList()
                : Array.Empty<string>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsMesh(string path)
    {
        try
        {
            return NifGeometry.Shapes(NifFile.Read(path)).Count > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
