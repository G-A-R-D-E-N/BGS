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

    /// <summary>
    /// The enabled Mod Organizer 2 roots layered over the game data, lowest priority first;
    /// the last entry (the profile's overwrite folder when present) wins. Each root mirrors
    /// the Data folder layout, so "Meshes\Actors\..." inside a root overlays the same
    /// virtual path in the base tree.
    /// </summary>
    public IReadOnlyList<string> ModRoots { get; }


    /// <summary>
    /// One weapon animation type (the folder under Animations\Weapon\) and the path
    /// prefixes the engine searches, in fallback order, when a weapon subgraph plays a
    /// generic clip: e.g. 44Pistol resolves against "Animations\Weapon\44Pistol\Player",
    /// then "Animations\Weapon\44Pistol", then "Animations\Weapon\Pistol", and so on.
    /// Derived from the race AnimationSetData in the game's master plugin.
    /// </summary>
    public sealed record WeaponTypeSet(string Type, IReadOnlyList<string> Prefixes);

    private readonly Dictionary<string, Ba2> _opened = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _names = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string>? _modLooseIndex;
    private List<WeaponTypeSet>? _weaponSets;

    private GameData(string dataFolder, List<string> archivePaths, string? pluginsPath, List<string> modRoots)
    {
        DataFolder = dataFolder;
        ArchivePaths = archivePaths;
        PluginsPath = pluginsPath;
        ModRoots = modRoots;
    }

    /// <summary>
    /// Index every .ba2 directly under the data folder, in load order. When mod roots are
    /// given, their top-level .ba2 files are layered ahead of the game's own archives, highest
    /// priority first, so a modded copy wins over the base game's.
    /// </summary>
    public static GameData Discover(string dataFolder, string? pluginsPath = null,
                                    IReadOnlyList<string>? modRoots = null)
    {
        string folder = Path.GetFullPath(dataFolder);

        var roots = new List<string>();
        var modArchives = new List<string>();
        if (modRoots != null)
        {
            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in modRoots)
            {
                string full = Path.GetFullPath(root);
                if (!placed.Add(full)) continue;
                roots.Add(full);
                foreach (string ba2 in Directory.EnumerateFiles(full, "*.ba2", SearchOption.TopDirectoryOnly)
                                                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    if (placed.Add(ba2)) modArchives.Add(ba2);
            }
            modArchives.Reverse();
        }

        var baseArchives = Directory.EnumerateFiles(folder, "*.ba2")
                                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                                    .Select(Path.GetFullPath)
                                    .ToList();
        pluginsPath ??= FindPluginsFile();
        if (pluginsPath != null && File.Exists(pluginsPath))
            baseArchives = OrderByPlugins(baseArchives, pluginsPath);

        modArchives.AddRange(baseArchives);
        return new GameData(folder, modArchives, pluginsPath, roots);
    }

    /// <summary>
    /// The game data with a Mod Organizer 2 modlist layered over it: every enabled mod in
    /// mods/&lt;modsFolder&gt;/mods is a root in modlist.txt order (bottom of the file wins),
    /// and the profile's overwrite folder, when it exists, is the final root. The modlist
    /// lives at &lt;modsFolder&gt;/modlist.txt, or at
    /// &lt;modsFolder&gt;/profiles/&lt;profile&gt;/modlist.txt when a profile is named.
    /// </summary>
    public static GameData DiscoverModded(string dataFolder, string modsFolder,
                                          string? profile = null, string? pluginsPath = null)
    {
        string mods = Path.GetFullPath(modsFolder);
        var roots = new List<string>();

        string? modlist = profile != null
            ? Path.Combine(mods, "profiles", profile, "modlist.txt")
            : Path.Combine(mods, "modlist.txt");
        if (File.Exists(modlist))
        {
            foreach (string raw in File.ReadAllLines(modlist))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line[0] != '+') continue;
                string name = line[1..].Trim();
                if (name.Length == 0) continue;
                string root = Path.Combine(mods, "mods", name);
                if (Directory.Exists(root)) roots.Add(root);
            }
        }

        string overwrite = Path.Combine(mods, "overwrite");
        if (Directory.Exists(overwrite)) roots.Add(overwrite);

        return Discover(dataFolder, pluginsPath, roots);
    }

    /// <summary>
    /// True when the animation is loose under the project root, or an archive entry matches it.
    /// The declared extension does not matter (.hkt resolves to .hkx, as the game treats them
    /// as the same file).
    /// </summary>
    public bool ContainsAnimation(string projectRoot, string declared) =>
        ResolveAnimation(projectRoot, declared) != null;

    /// <summary>
    /// Where the declared animation resolves from: "loose" when a file sits under the project
    /// root, the archive's file name when it is packed, or null when it is absent everywhere.
    /// </summary>
    public string? ResolveAnimation(string projectRoot, string declared) =>
        ReadAnimation(projectRoot, declared)?.Source;

    /// <summary>A declared animation that resolves somewhere: its bytes and where they came from.</summary>
    public sealed record AnimationRead(byte[] Bytes, string Source, string? EntryName);

    /// <summary>
    /// Read the declared animation's bytes from the loose file or the matching archive entry,
    /// so callers can decode a packed clip without extracting it. Returns null when the
    /// animation is absent everywhere.
    /// </summary>
    public AnimationRead? ReadAnimation(string projectRoot, string declared)
    {
        string loose = ResolveLoose(projectRoot, declared);
        string? rel = DataRelativePath(loose);
        if (rel != null && ModLooseIndex().TryGetValue(Normalize(rel), out string? modLoose))
        {
            try { return new AnimationRead(File.ReadAllBytes(modLoose), "loose", null); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        }
        string? baseLoose = CaseInsensitivePath(loose) ?? CaseInsensitivePath(Path.ChangeExtension(loose, ".hkx"));
        if (baseLoose != null)
        {
            try { return new AnimationRead(File.ReadAllBytes(baseLoose), "loose", null); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        }

        var (archive, entry) = FindEntry(projectRoot, declared);
        if (entry == null) return null;
        try { return new AnimationRead(archive.Read(entry), Path.GetFileName(archive.Path), entry.Name); }
        catch (Exception e) when (e is IOException or InvalidDataException) { return null; }
    }

    private string? DataRelativePath(string fullPath)
    {
        if (!IsUnder(fullPath, DataFolder)) return null;
        return Path.GetRelativePath(DataFolder, fullPath);
    }

    private Dictionary<string, string> ModLooseIndex()
    {
        if (_modLooseIndex != null) return _modLooseIndex;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in ModRoots)
        {
            if (!Directory.Exists(Path.Combine(root, "Meshes"))) continue;
            try
            {
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(root, file);
                    map[Normalize(rel)] = file;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        _modLooseIndex = map;
        return _modLooseIndex;
    }

    /// <summary>
    /// The engine is case-insensitive (Windows), but this tool runs on case-sensitive
    /// filesystems, and mod manifests routinely declare paths in a different case than
    /// the loose folder that ships them. Resolves a path by matching each missing
    /// component case-insensitively against the deepest existing ancestor.
    /// </summary>
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
        if (missing.Count == 0) return null;

        while (missing.Count > 0)
        {
            string want = missing.Pop();
            bool last = missing.Count == 0;
            string? found = null;
            try
            {
                if (last)
                {
                    foreach (string candidate in Directory.EnumerateFiles(current))
                        if (string.Equals(Path.GetFileName(candidate), want, StringComparison.OrdinalIgnoreCase))
                        { found = candidate; break; }
                    if (found == null)
                        foreach (string candidate in Directory.EnumerateDirectories(current))
                            if (string.Equals(Path.GetFileName(candidate), want, StringComparison.OrdinalIgnoreCase))
                            { found = candidate; break; }
                }
                else
                {
                    foreach (string candidate in Directory.EnumerateDirectories(current))
                        if (string.Equals(Path.GetFileName(candidate), want, StringComparison.OrdinalIgnoreCase))
                        { found = candidate; break; }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
            if (found == null) return null;
            current = found;
        }
        return current;
    }

    /// <summary>
    /// The first .hkx under the character's CharacterAssets folder in the archives, given an
    /// animation entry name like "Meshes/Actors/Turret/Animations/...". Returns its bytes, or
    /// null when no packed skeleton exists there. The rig is usually the loose chain skeleton,
    /// so this is the fallback for fully packed actors.
    /// </summary>
    public byte[]? SkeletonBytes(string animationEntryName)
    {
        string flat = animationEntryName.Replace('\\', '/');
        int anims = flat.LastIndexOf("/Animations/", StringComparison.Ordinal);
        if (anims < 0) return null;
        string assets = flat[..anims] + "/CharacterAssets/";

        foreach (string archive in ArchivePaths)
        {
            foreach (var entry in Entries(archive))
            {
                if (!entry.Name.StartsWith(assets, StringComparison.OrdinalIgnoreCase)) continue;
                if (!entry.Name.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase)) continue;
                try { return Open(archive).Read(entry); }
                catch (Exception e) when (e is IOException or InvalidDataException) { return null; }
            }
        }
        return null;
    }

    private (Ba2 Archive, Ba2.Entry? Entry) FindEntry(string projectRoot, string declared)
    {
        string key = Normalize(declared);
        if (key.Length == 0) return (null!, null);

        // the declared path may traverse up out of the project root (..\PowerArmor\... borrows
        // another actor's animations), so resolve it against the root before matching archives
        string? prefixed = null;
        try
        {
            string absolute = Path.GetFullPath(Path.Combine(projectRoot,
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
            if (prefixed != null && !names.Contains(prefixed)) continue;
            if (prefixed == null && !names.Any(n => n.EndsWith("/" + key, StringComparison.Ordinal))) continue;

            foreach (var entry in Entries(archive))
            {
                string normalized = Normalize(entry.Name);
                if (prefixed != null)
                {
                    if (normalized == prefixed) return (Open(archive), entry);
                }
                else if (normalized.EndsWith("/" + key, StringComparison.Ordinal))
                {
                    return (Open(archive), entry);
                }
            }
        }
        return (null!, null);
    }

    private IEnumerable<Ba2.Entry> Entries(string archivePath)
    {
        try { return Open(archivePath).Entries; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // a texture (DX10) archive or an unreadable file simply holds no animations
            return Array.Empty<Ba2.Entry>();
        }
    }

    /// <summary>
    /// Every entry of every archive, lowest priority first, with the archive it came from.
    /// The subgraph index uses this to read the engine's AnimTextData manifests and keeps
    /// the last entry for an id, so the highest-priority copy wins.
    /// </summary>
    public IEnumerable<(string ArchivePath, Ba2.Entry Entry)> EnumerateEntries()
    {
        foreach (string archive in ArchivePaths.Reverse())
            foreach (var entry in Entries(archive))
                yield return (archive, entry);
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
        AddSubfolders(CaseInsensitivePath(looseDir));

        string? rootRel = DataRelativePath(Path.GetFullPath(projectRoot));
        if (rootRel != null)
        {
            foreach (string mod in ModRoots)
            {
                string modLooseDir = Path.Combine(
                    Path.Combine(mod, rootRel),
                    virtualFolder.Replace('/', Path.DirectorySeparatorChar));
                AddSubfolders(CaseInsensitivePath(modLooseDir));
            }
        }

        void AddSubfolders(string? dir)
        {
            if (dir == null) return;
            try
            {
                foreach (string sub in Directory.EnumerateDirectories(dir))
                    result.Add(Path.GetFileName(sub));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

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

    /// <summary>
    /// The weapon animation types the engine will actually resolve, derived from the
    /// AnimationSetData on the race records of the game's master plugin (Fallout4.esm).
    /// Empty when no master is present or it cannot be read, in which case callers fall
    /// back to inferring weapon types from the subgraph itself.
    /// </summary>
    public IReadOnlyList<WeaponTypeSet> WeaponTypeSets
    {
        get
        {
            if (_weaponSets != null) return _weaponSets;
            _weaponSets = BuildWeaponTypeSets();
            return _weaponSets;
        }
    }

    private List<WeaponTypeSet> BuildWeaponTypeSets()
    {
        var sets = new List<WeaponTypeSet>();
        string? master = FindMaster();
        if (master == null) return sets;

        List<EsPlugin.AnimSet> animSets;
        try { animSets = EsPlugin.RaceAnimationSets(master); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return sets;
        }

        var byType = new Dictionary<string, (List<string> Prefixes, HashSet<string> Seen)>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in animSets)
        {
            string behavior = Path.GetFileNameWithoutExtension(set.Behavior);
            if (!behavior.Contains("Weapon", StringComparison.OrdinalIgnoreCase)) continue;

            string? type = null;
            var prefixes = new List<string>();
            foreach (string path in set.Paths)
            {
                int at = path.IndexOf("Animations\\Weapon\\", StringComparison.OrdinalIgnoreCase);
                if (at <= 0) continue;
                string relative = path[at..];
                int typeEnd = relative.IndexOf('\\', "Animations\\Weapon\\".Length);
                string folder = typeEnd > 0
                    ? relative["Animations\\Weapon\\".Length..typeEnd]
                    : relative["Animations\\Weapon\\".Length..];
                if (folder.Length == 0) continue;

                type ??= folder;
                prefixes.Add(relative);
            }
            if (type == null) continue;

            if (!byType.TryGetValue(type, out var known))
                byType[type] = known = (new List<string>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            foreach (string prefix in prefixes)
                if (known.Seen.Add(prefix)) known.Prefixes.Add(prefix);
        }

        foreach (var pair in byType.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            sets.Add(new WeaponTypeSet(pair.Key, pair.Value.Prefixes.ToList()));
        return sets;
    }

    private string? FindMaster()
    {
        for (int i = ModRoots.Count - 1; i >= 0; i--)
        {
            string candidate = Path.Combine(ModRoots[i], "Fallout4.esm");
            if (File.Exists(candidate)) return candidate;
        }
        string master = Path.Combine(DataFolder, "Fallout4.esm");
        return File.Exists(master) ? master : null;
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
