using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Nif;

namespace OpenCommonwealth.Services.Hkx;

public static class MeshLookup
{
    private const int MaximumSearchEntries = 4096;
    public sealed record Result(string? Path, string Reason)
    {
        public bool Found => Path != null;
    }

    public static IEnumerable<string> Places(string behaviourPath, string? projectRoot,
                                             string? skeletonPath)
    {
        var seen = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
        foreach (string? folder in new[]
                 {
                     Path.GetDirectoryName(behaviourPath),
                     projectRoot,
                     skeletonPath == null ? null : Path.GetDirectoryName(skeletonPath),
                 })
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;

            string canonical;
            try
            {
                canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (seen.Add(canonical)) yield return canonical;
        }
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

        if (string.IsNullOrEmpty(actorRoot))
            return new Result(null, "no model search root is available, so use Mesh... to point at one.");

        var models = OnDisk(actorRoot, out bool limited);
        return limited
            ? new Result(null, "model search limit reached, so use Mesh... to point at one.")
            : Find(new[] { actorRoot }, _ => models);
    }

    private static IReadOnlyList<string> OnDisk(string folder, out bool limited)
    {
        limited = false;
        try
        {
            if (!Directory.Exists(folder)) return Array.Empty<string>();
            var entries = Directory.EnumerateFileSystemEntries(folder, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint,
            }).Take(MaximumSearchEntries + 1).ToArray();
            if (entries.Length > MaximumSearchEntries)
            {
                limited = true;
                return Array.Empty<string>();
            }
            return entries.Where(path => string.Equals(Path.GetExtension(path), ".nif",
                                   StringComparison.OrdinalIgnoreCase))
                          .Where(IsMesh).ToList();
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
