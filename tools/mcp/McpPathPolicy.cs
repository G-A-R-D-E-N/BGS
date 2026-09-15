using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BehaviourStudio.Mcp;

public sealed class McpPathPolicy
{
    private readonly string[] _roots;
    private readonly StringComparer _comparer;

    private McpPathPolicy(IEnumerable<string> roots)
    {
        _comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _roots = roots.Distinct(_comparer).ToArray();
    }

    public static bool TryCreate(IEnumerable<string> roots, out McpPathPolicy? policy, out string error)
    {
        policy = null;
        error = "";
        var canonical = new List<string>();
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        foreach (string raw in roots)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "--root requires a directory";
                return false;
            }

            string root;
            try { root = Path.GetFullPath(raw); }
            catch (Exception) when (raw.Length > 0)
            {
                error = "--root contains an invalid path";
                return false;
            }

            if (!Directory.Exists(root))
            {
                error = $"allowed root does not exist: {raw}";
                return false;
            }
            if (HasReparsePoint(root))
            {
                error = "allowed roots cannot be symbolic links or reparse points";
                return false;
            }

            try
            {
                using var entries = Directory.EnumerateFileSystemEntries(root).GetEnumerator();
                _ = entries.MoveNext();
            }
            catch (Exception)
            {
                error = $"allowed root is not readable: {raw}";
                return false;
            }

            if (!canonical.Contains(root, comparer)) canonical.Add(root);
        }

        if (canonical.Count == 0)
        {
            error = "at least one --root is required";
            return false;
        }

        policy = new McpPathPolicy(canonical);
        return true;
    }

    public bool TryAuthorize(string raw, out string canonical, out string code, out string message)
    {
        canonical = "";
        code = "ok";
        message = "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            code = "invalid_argument";
            message = "path is required";
            return false;
        }

        try { canonical = Path.GetFullPath(raw); }
        catch (Exception)
        {
            code = "path_not_allowed";
            message = "path is not allowed";
            return false;
        }

        if (!File.Exists(canonical) && !Directory.Exists(canonical))
        {
            code = "path_not_found";
            message = "path was not found";
            return false;
        }
        if (Directory.Exists(canonical))
        {
            code = "path_not_file";
            message = "inspection path must be a file";
            return false;
        }

        string? root = null;
        foreach (string candidate in _roots)
            if (IsWithin(candidate, canonical))
            {
                root = candidate;
                break;
            }
        if (root == null)
        {
            code = "path_not_allowed";
            message = "path is outside the allowed roots";
            return false;
        }
        if (HasLinkComponent(root, canonical))
        {
            code = "path_link_not_allowed";
            message = "path contains a symbolic link or reparse point";
            return false;
        }

        return true;
    }

    private bool IsWithin(string root, string target) =>
        _comparer.Equals(root, target) ||
        target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                          Path.DirectorySeparatorChar, StringComparisonFrom(_comparer));

    private static StringComparison StringComparisonFrom(StringComparer comparer) =>
        comparer == StringComparer.OrdinalIgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool HasLinkComponent(string root, string target)
    {
        if (HasReparsePoint(root)) return true;
        string relative = Path.GetRelativePath(root, target);
        string current = root;
        foreach (string part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar,
                                               StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (HasReparsePoint(current)) return true;
        }
        return false;
    }

    private static bool HasReparsePoint(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception) { return true; }
    }
}
