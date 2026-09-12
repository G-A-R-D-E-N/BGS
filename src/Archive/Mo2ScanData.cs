using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Archive;

/// <summary>
/// Resolves the active Mod Organizer 2 instance into the same GameData view BGS already uses.
/// This is deliberately a thin adapter: the actual loose/archive precedence stays in GameData.
/// </summary>
internal sealed class Mo2ScanData : IDisposable
{
    internal sealed record Layout(
        string InstancePath,
        string? Profile,
        string ModlistPath,
        string GameDataFolder,
        string OverwriteFolder,
        IReadOnlyList<string> ModRootsHighToLow);

    public Layout Info { get; }
    public GameData Data { get; }
    public IReadOnlyList<string> ModRootsHighToLow => Info.ModRootsHighToLow;

    private readonly List<string> _archiveWarnings = new();

    private Mo2ScanData(Layout info, GameData data)
    {
        Info = info;
        Data = data;
    }

    public static bool TryOpen(string instancePath, out Mo2ScanData? result, out string error)
    {
        result = null;
        error = "";
        try
        {
            string instance = Path.GetFullPath(instancePath);
            if (!Directory.Exists(instance))
            {
                error = $"MO2 instance does not exist: {instance}";
                return false;
            }

            string modsDir = Path.Combine(instance, "mods");
            if (!Directory.Exists(modsDir))
            {
                error = $"no mods/ directory under {instance}";
                return false;
            }

            if (!TryProfile(instance, out string? profile, out string modlist, out error))
                return false;

            string dataFolder = ResolveGameDataFolder(instance);
            if (dataFolder.Length == 0 || !Directory.Exists(dataFolder))
            {
                error = "could not resolve Fallout 4 Data from ModOrganizer.ini gamePath or Stock Folder/Data";
                return false;
            }

            string overwrite = ResolveOverwriteFolder(instance);
            var rootsHighToLow = new List<string>();
            if (Directory.Exists(overwrite)) rootsHighToLow.Add(Path.GetFullPath(overwrite));

            foreach (string name in EnabledMods(modlist))
            {
                string root = Path.Combine(modsDir, name);
                if (Directory.Exists(root)) rootsHighToLow.Add(Path.GetFullPath(root));
            }

            string? plugins = profile != null
                ? Path.Combine(instance, "profiles", profile, "plugins.txt")
                : Path.Combine(instance, "plugins.txt");
            if (!File.Exists(plugins)) plugins = null;

            // GameData expects mod roots lowest -> highest; overwrite is highest.
            var rootsLowToHigh = rootsHighToLow.AsEnumerable().Reverse().ToList();
            var data = GameData.Discover(dataFolder, plugins, rootsLowToHigh);
            result = new Mo2ScanData(
                new Layout(instance, profile, modlist, dataFolder, overwrite, rootsHighToLow),
                data);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                                      ArgumentException or NotSupportedException)
        {
            error = First(e.Message);
            return false;
        }
    }

    public IReadOnlyList<string> ModHkxPaths(string folderName, Action<string>? onArchive = null)
    {
        string needle = "/" + folderName.Trim('/', '\\') + "/";
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Highest priority first so the displayed spelling comes from the winning side.
        foreach (string root in ModRootsHighToLow)
        {
            foreach (string file in EnumerateFilesSafe(root, "*.hkx", recursive: true))
            {
                string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (!HasFolder(rel, needle)) continue;
                string key = GameData.Normalize(rel);
                if (!found.ContainsKey(key)) found[key] = rel;
            }

            foreach (string archivePath in EnumerateFilesSafe(root, "*.ba2", recursive: false)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                onArchive?.Invoke(Path.GetFileName(archivePath));
                if (!Data.TryArchiveEntries(archivePath, out var entries, out string? warning))
                {
                    if (warning != null) _archiveWarnings.Add(warning);
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (!entry.Name.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase)) continue;
                    string rel = entry.Name.Replace('\\', '/');
                    if (!HasFolder(rel, needle)) continue;
                    string key = GameData.Normalize(rel);
                    if (!found.ContainsKey(key)) found[key] = rel;
                }
            }
        }

        return found.Values.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public GameData.AnimationRead? ReadDataRelative(string dataRelative) =>
        Data.ReadAnimation(Data.DataFolder, dataRelative);

    public bool ExistsDataRelative(string dataRelative) =>
        Data.ContainsAnimation(Data.DataFolder, dataRelative);

    public List<string> WeaponSubfolders(string projectRelative)
    {
        string projectRoot = Path.Combine(
            Data.DataFolder,
            projectRelative.Replace('/', Path.DirectorySeparatorChar)
                           .Replace('\\', Path.DirectorySeparatorChar));
        return Data.Subfolders(projectRoot, "Animations/Weapon");
    }

    public string DescribeSource(string dataRelative, GameData.AnimationRead read)
    {
        if (!read.Source.Equals("loose", StringComparison.OrdinalIgnoreCase))
            return read.Source;

        foreach (string root in ModRootsHighToLow)
        {
            string? hit = FindLoose(root, dataRelative);
            if (hit != null)
                return "loose:" + Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar,
                                                                Path.AltDirectorySeparatorChar));
        }

        return "loose:Data";
    }

    public IReadOnlyList<string> DrainArchiveWarnings()
    {
        var copy = _archiveWarnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _archiveWarnings.Clear();
        return copy;
    }

    private static bool TryProfile(string instance, out string? profile, out string modlist, out string error)
    {
        error = "";
        profile = null;
        modlist = "";

        string? selected = ReadIniValue(instance, "selected_profile");
        if (!string.IsNullOrWhiteSpace(selected))
        {
            string selectedList = Path.Combine(instance, "profiles", selected, "modlist.txt");
            if (!File.Exists(selectedList))
            {
                error = $"selected MO2 profile '{selected}' has no modlist.txt";
                return false;
            }
            profile = selected;
            modlist = selectedList;
            return true;
        }

        string rootList = Path.Combine(instance, "modlist.txt");
        if (File.Exists(rootList))
        {
            modlist = rootList;
            return true;
        }

        string defaultList = Path.Combine(instance, "profiles", "Default", "modlist.txt");
        if (File.Exists(defaultList))
        {
            profile = "Default";
            modlist = defaultList;
            return true;
        }

        string profiles = Path.Combine(instance, "profiles");
        if (!Directory.Exists(profiles))
        {
            error = "could not find an MO2 modlist.txt";
            return false;
        }

        var candidates = EnumerateDirectoriesSafe(profiles)
            .Where(d => File.Exists(Path.Combine(d, "modlist.txt")))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 1)
        {
            profile = Path.GetFileName(candidates[0]);
            modlist = Path.Combine(candidates[0], "modlist.txt");
            return true;
        }

        error = candidates.Count == 0
            ? "could not find an MO2 modlist.txt"
            : "ModOrganizer.ini does not identify the active profile and multiple profiles contain modlist.txt";
        return false;
    }

    private static List<string> EnabledMods(string modlist)
    {
        var mods = new List<string>();
        foreach (string raw in File.ReadAllLines(modlist))
        {
            string line = raw.Trim();
            if (!line.StartsWith('+')) continue;
            string name = line[1..].Trim();
            if (name.Length == 0 || name.EndsWith("_separator", StringComparison.OrdinalIgnoreCase)) continue;
            mods.Add(name);
        }
        return mods; // MO2 modlist.txt: highest priority first
    }

    private static string ResolveGameDataFolder(string instance)
    {
        string gamePath = NativePath(ReadIniValue(instance, "gamePath") ?? "");
        if (!string.IsNullOrWhiteSpace(gamePath))
        {
            if (Directory.Exists(gamePath) &&
                Path.GetFileName(gamePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    .Equals("Data", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(gamePath);

            string data = Path.Combine(gamePath, "Data");
            if (Directory.Exists(data)) return Path.GetFullPath(data);
        }

        string stock = Path.Combine(instance, "Stock Folder", "Data");
        return Directory.Exists(stock) ? Path.GetFullPath(stock) : "";
    }

    private static string ResolveOverwriteFolder(string instance)
    {
        string baseDir = NativePath(ReadIniValue(instance, "base_directory") ?? "");
        string overwriteDir = NativePath(ReadIniValue(instance, "overwrite_directory") ?? "");

        if (string.IsNullOrWhiteSpace(baseDir)) baseDir = instance;
        baseDir = baseDir.Replace("%BASE_DIR%", instance, StringComparison.OrdinalIgnoreCase);
        if (!Path.IsPathRooted(baseDir)) baseDir = Path.Combine(instance, baseDir);
        baseDir = Path.GetFullPath(baseDir);

        string overwrite = string.IsNullOrWhiteSpace(overwriteDir)
            ? Path.Combine(baseDir, "overwrite")
            : overwriteDir.Replace("%BASE_DIR%", baseDir, StringComparison.OrdinalIgnoreCase);

        overwrite = overwrite.Replace('/', Path.DirectorySeparatorChar)
                             .Replace('\\', Path.DirectorySeparatorChar);
        if (!Path.IsPathRooted(overwrite)) overwrite = Path.Combine(baseDir, overwrite);
        return Path.GetFullPath(overwrite);
    }

    private static string? ReadIniValue(string instance, string key)
    {
        string ini = Path.Combine(instance, "ModOrganizer.ini");
        if (!File.Exists(ini)) return null;

        string prefix = key + "=";
        foreach (string raw in File.ReadAllLines(ini))
        {
            string line = raw.Trim();
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string value = line[prefix.Length..].Trim();
            const string byteArray = "@ByteArray(";
            if (value.StartsWith(byteArray, StringComparison.OrdinalIgnoreCase) && value.EndsWith(')'))
                value = value[byteArray.Length..^1];
            return value.Replace("\\\\", "\\");
        }
        return null;
    }

    private static string NativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || OperatingSystem.IsWindows()) return path;

        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            string rest = path[2..].Replace('\\', '/').TrimStart('/');
            char drive = char.ToLowerInvariant(path[0]);
            if (drive == 'z') return "/" + rest;
            string prefix = Environment.GetEnvironmentVariable("WINEPREFIX")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wine");
            return Path.Combine(prefix, "drive_" + drive, rest);
        }
        return path.Replace('\\', '/');
    }

    private static bool HasFolder(string relative, string needle)
    {
        string flat = "/" + relative.Replace('\\', '/').Trim('/') + "/";
        return flat.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindLoose(string root, string dataRelative)
    {
        string full;
        try
        {
            full = Path.Combine(root,
                dataRelative.Replace('/', Path.DirectorySeparatorChar)
                            .Replace('\\', Path.DirectorySeparatorChar));
        }
        catch { return null; }

        string? direct = CaseInsensitivePath(full);
        if (direct != null && File.Exists(direct)) return direct;

        string swapped = Path.ChangeExtension(full, ".hkx");
        direct = CaseInsensitivePath(swapped);
        return direct != null && File.Exists(direct) ? direct : null;
    }

    private static string? CaseInsensitivePath(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) return path;

        var missing = new Stack<string>();
        string current = path;
        while (!File.Exists(current) && !Directory.Exists(current))
        {
            string? parent = Path.GetDirectoryName(current);
            if (parent == null || string.Equals(parent, current, StringComparison.Ordinal)) return null;
            missing.Push(Path.GetFileName(current));
            current = parent;
        }

        while (missing.Count > 0)
        {
            string want = missing.Pop();
            string? found = null;
            try
            {
                foreach (string candidate in Directory.EnumerateFileSystemEntries(current))
                    if (string.Equals(Path.GetFileName(candidate), want, StringComparison.OrdinalIgnoreCase))
                    {
                        found = candidate;
                        break;
                    }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
            if (found == null) return null;
            current = found;
        }
        return current;
    }

    internal static IEnumerable<string> EnumerateFilesSafe(string root, string pattern, bool recursive)
    {
        if (!Directory.Exists(root)) yield break;

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            string[] files;
            try { files = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }

            foreach (string file in files) yield return file;
            if (!recursive) continue;

            string[] children;
            try { children = Directory.GetDirectories(dir); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            for (int i = children.Length - 1; i >= 0; i--) pending.Push(children[i]);
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string root)
    {
        try { return Directory.GetDirectories(root); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static string First(string message) =>
        message.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? message.Trim();

    public void Dispose() => Data.Dispose();
}
