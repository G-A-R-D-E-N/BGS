using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Archive;

/// <summary>
/// The game's data tree as the engine sees it: a folder holding loose files plus every .ba2
/// archive under it, consulted in load order. Loose files win over archives, and within the
/// archives the later-loaded archive wins; for existence checks any hit is enough.
///
/// A project chain declares animation paths relative to its root, which is normally a folder
/// somewhere inside the data tree (for Fallout 4's vanilla character: Data\Meshes\Actors\
/// Character). The same path inside an archive appears with that data-relative prefix, so an
/// animation that is not loose can still exist inside e.g. "Fallout4 - Animations.ba2".
/// </summary>
public sealed class GameData : IDisposable
{
    public string DataFolder { get; }
    public IReadOnlyList<string> ArchivePaths { get; }
    public string? PluginsPath { get; }


    private readonly Dictionary<string, Ba2> _opened = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _names = new(StringComparer.OrdinalIgnoreCase);

    private GameData(string dataFolder, List<string> archivePaths, string? pluginsPath)
    {
        DataFolder = dataFolder;
        ArchivePaths = archivePaths;
        PluginsPath = pluginsPath;
    }

    /// <summary>Index every .ba2 directly under the data folder, in load order.</summary>
    public static GameData Discover(string dataFolder, string? pluginsPath = null)
    {
        string folder = Path.GetFullPath(dataFolder);
        var archives = Directory.EnumerateFiles(folder, "*.ba2")
                                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                                .Select(Path.GetFullPath)
                                .ToList();

        pluginsPath ??= FindPluginsFile();
        if (pluginsPath != null && File.Exists(pluginsPath))
            archives = OrderByPlugins(archives, pluginsPath);

        return new GameData(folder, archives, pluginsPath);
    }

    /// <summary>
    /// True when the animation is loose under the project root, or an archive entry matches it.
    /// The declared extension does not matter (.hkt resolves to .hkx, as the game treats them
    /// as the same file).
    /// </summary>
    public bool ContainsAnimation(string projectRoot, string declared)
    {
        string loose = ResolveLoose(projectRoot, declared);
        if (File.Exists(loose)) return true;

        string key = Normalize(declared);
        if (key.Length == 0) return false;

        // the declared path may traverse up out of the project root (..\PowerArmor\... borrows
        // another actor's animations), so resolve it against the root before matching archives
        string? prefixed = null;
        string? absolute = null;
        try
        {
            absolute = Path.GetFullPath(Path.Combine(projectRoot,
                declared.Replace('\\', Path.DirectorySeparatorChar)
                        .Replace('/', Path.DirectorySeparatorChar)));
            if (IsUnder(absolute, DataFolder))
            {
                string rel = Path.GetRelativePath(DataFolder, absolute)
                               .Replace('\\', '/').ToLowerInvariant();
                prefixed = Normalize(rel);
            }
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            // paths on different roots or malformed input: fall back to suffix matching
        }

        foreach (string archive in ArchivePaths)
        {
            var names = Names(archive);
            if (prefixed != null && names.Contains(prefixed)) return true;

            if (prefixed == null)
            {
                foreach (string name in names)
                    if (name.EndsWith("/" + key, StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The subfolders that exist under a virtual folder, e.g. the weapon types under
    /// "Animations/Weapon", from both loose files under the project root and archive entries.
    /// </summary>
    public List<string> Subfolders(string projectRoot, string virtualFolder)
    {
        var result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        string looseDir = Path.Combine(projectRoot,
            virtualFolder.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            if (Directory.Exists(looseDir))
                foreach (string dir in Directory.EnumerateDirectories(looseDir))
                    result.Add(Path.GetFileName(dir));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        string norm = "/" + virtualFolder.Replace('\\', '/').Trim('/').ToLowerInvariant() + "/";
        foreach (string archive in ArchivePaths)
        {
            foreach (string name in Names(archive))
            {
                int at = name.IndexOf(norm, StringComparison.Ordinal);
                if (at < 0) continue;
                int start = at + norm.Length;
                int end = name.IndexOf('/', start);
                if (end > start) result.Add(name[start..end]);
            }
        }
        return result.ToList();
    }

    /// <summary>Like <see cref="ProjectChain.ResolvePath"/>: the loose file, with .hkt resolving to .hkx.</summary>
    public static string ResolveLoose(string projectRoot, string declared)
    {
        string cleaned = declared.Replace('\\', Path.DirectorySeparatorChar)
                                 .Replace('/', Path.DirectorySeparatorChar);
        string full = Path.GetFullPath(Path.Combine(projectRoot, cleaned));
        if (File.Exists(full)) return full;
        string swapped = Path.ChangeExtension(full, ".hkx");
        return File.Exists(swapped) ? swapped : full;
    }

    /// <summary>
    /// Lower-cased, '/'-separated, without the final extension, with '.' and '..' segments
    /// collapsed; the archive index key.
    /// </summary>
    public static string Normalize(string name)
    {
        string flat = name.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        int dot = flat.LastIndexOf('.');
        int slash = flat.LastIndexOf('/');
        if (dot > slash) flat = flat[..dot];

        var parts = flat.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>();
        foreach (string part in parts)
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(part);
        }
        return string.Join('/', stack);
    }

    private HashSet<string> Names(string archivePath)
    {
        if (_names.TryGetValue(archivePath, out var names)) return names;

        names = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var archive = Open(archivePath);
            foreach (var entry in archive.Entries) names.Add(Normalize(entry.Name));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // a texture (DX10) archive or an unreadable file simply holds no animations
        }
        _names[archivePath] = names;
        return names;
    }

    private Ba2 Open(string archivePath)
    {
        if (_opened.TryGetValue(archivePath, out var archive)) return archive;
        archive = Ba2.Open(archivePath);
        _opened[archivePath] = archive;
        return archive;
    }

    private static bool IsUnder(string path, string root)
    {
        if (path.Length < root.Length) return false;
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) return true;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
        return path[root.Length] == Path.DirectorySeparatorChar ||
               path[root.Length] == Path.AltDirectorySeparatorChar;
    }

    private static List<string> OrderByPlugins(List<string> archives, string pluginsPath)
    {
        var plugins = File.ReadAllLines(pluginsPath)
                          .Select(line => line.Trim())
                          .Where(line => line.Length > 0 && !line.StartsWith('#'))
                          .Select(line => line.StartsWith('*') ? line[1..] : line)
                          .Select(Path.GetFileNameWithoutExtension)
                          .Where(baseName => !string.IsNullOrEmpty(baseName))
                          .Cast<string>()
                          .ToList();

        var ordered = new List<string>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string pluginBase in plugins)
        {
            foreach (string archive in archives)
            {
                if (!IsArchiveOfPlugin(archive, pluginBase)) continue;
                if (placed.Add(archive)) ordered.Add(archive);
            }
        }
        foreach (string archive in archives)
            if (placed.Add(archive)) ordered.Add(archive);

        return ordered;
    }

    private static bool IsArchiveOfPlugin(string archivePath, string pluginBase)
    {
        string name = Path.GetFileNameWithoutExtension(archivePath);
        int dash = name.IndexOf(" - ", StringComparison.Ordinal);
        string core = dash >= 0 ? name[..dash] : name;
        return string.Equals(core, pluginBase, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindPluginsFile()
    {
        string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "Fallout4", "plugins.txt");
        if (File.Exists(local)) return local;

        // Steam Play (Proton) keeps the Windows AppData in the prefix
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string proton = Path.Combine(home, ".steam", "steam", "steamapps", "compatdata", "377160",
            "pfx", "drive_c", "users", "steamuser", "AppData", "Local", "Fallout4", "plugins.txt");
        return File.Exists(proton) ? proton : null;
    }

    public void Dispose()
    {
        foreach (var archive in _opened.Values) archive.Dispose();
        _opened.Clear();
    }
}
