using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Archive;

/// <summary>
/// The engine's AnimTextData subgraph index, built from the files the game itself ships.
///
/// The game keys its animation data by a 64-bit subgraph identifier (a hash Bethesda computes
/// at runtime; the algorithm is not public). The vanilla game ships the ground truth in
/// Meshes\AnimTextData\AnimationFileData\&lt;id&gt;.txt inside its .ba2 archives: a small text
/// manifest that names the subgraph's behavior graph(s) and every animation file bound to it.
/// Mods extend the same tree, and the union manifest (PersistantSubgraphInfoAndOffsetData.txt)
/// lists which per-subgraph offset files exist.
///
/// This index reads those manifests so a crash-log hash like 10448007347639226270 can be
/// resolved to the subgraph it names without reimplementing the hash.
/// </summary>
public sealed class SubgraphIndex
{
    /// <summary>One subgraph manifest: the behavior graph(s) and animation files keyed by id.</summary>
    public sealed record Subgraph(
        ulong Id,
        IReadOnlyList<string> BehaviorPaths,
        IReadOnlyList<string> AnimationPaths,
        string Source,
        string EntryName)
    {
        public string PrimaryBehavior => BehaviorPaths.Count > 0 ? BehaviorPaths[0] : "";
    }

    /// <summary>A per-subgraph offset-data file with no named manifest: the id exists in the
    /// AnimationOffsets tree, and its first embedded path is a (sometimes inaccurate) hint.</summary>
    public sealed record OffsetData(ulong Id, string Source, string EntryName, int Bytes, string? FirstPathHint);

    private readonly Dictionary<ulong, Subgraph> _byId;
    private readonly Dictionary<ulong, OffsetData> _offsets;

    private SubgraphIndex(Dictionary<ulong, Subgraph> byId, Dictionary<ulong, OffsetData> offsets)
    {
        _byId = byId;
        _offsets = offsets;
    }

    public IReadOnlyDictionary<ulong, Subgraph> Subgraphs => _byId;

    /// <summary>Resolve a subgraph id to its named manifest, or null when the game data has none.</summary>
    public Subgraph? Find(ulong id) => _byId.TryGetValue(id, out var s) ? s : null;

    /// <summary>The offset-data file for an id, or null when the game data has none.</summary>
    public OffsetData? FindOffsetData(ulong id) => _offsets.TryGetValue(id, out var o) ? o : null;

    /// <summary>
    /// Build the index from a game data tree. Loose AnimTextData files win over archive
    /// entries with the same id, and within archives the later-loaded archive wins, matching
    /// how the engine resolves the tree.
    /// </summary>
    public static SubgraphIndex Discover(GameData data)
    {
        var byId = new Dictionary<ulong, Subgraph>();
        var offsets = new Dictionary<ulong, OffsetData>();

        // Archives first, then loose files override: the engine and mod managers resolve
        // Meshes\AnimTextData with loose files winning over archive entries.
        foreach (var (archive, entry) in data.EnumerateEntries())
        {
            string name = entry.Name.Replace('\\', '/');
            string lower = name.ToLowerInvariant();

            if (lower.StartsWith("meshes/animtextdata/animationfiledata/", StringComparison.Ordinal) &&
                lower.EndsWith(".txt", StringComparison.Ordinal))
            {
                var sub = ParseManifestFile(Path.GetFileName(name), ReadEntry(archive, entry), archive, entry.Name);
                if (sub != null) byId[sub.Id] = sub;
            }
            else if (lower.StartsWith("meshes/animtextdata/animationoffsets/", StringComparison.Ordinal) &&
                     lower.EndsWith(".txt", StringComparison.Ordinal))
            {
                string stem = Path.GetFileNameWithoutExtension(name);
                if (!ulong.TryParse(stem, out ulong id)) continue;
                byte[] bytes = ReadEntry(archive, entry);
                offsets[id] = new OffsetData(id, Path.GetFileName(archive), entry.Name, bytes.Length,
                                             FirstPathHint(bytes));
            }
        }

        string looseRoot = Path.Combine(data.DataFolder, "Meshes", "AnimTextData");
        if (Directory.Exists(looseRoot))
        {
            string looseFileData = Path.Combine(looseRoot, "AnimationFileData");
            if (Directory.Exists(looseFileData))
            foreach (string file in Directory.EnumerateFiles(
                         looseFileData, "*.txt",
                         SearchOption.TopDirectoryOnly))
            {
                var sub = ParseManifestFile(Path.GetFileName(file), ReadText(file), "loose", file);
                if (sub != null) byId[sub.Id] = sub;
            }
            string looseOffsets = Path.Combine(looseRoot, "AnimationOffsets");
            if (Directory.Exists(looseOffsets))
            foreach (string file in Directory.EnumerateFiles(
                         looseOffsets, "*.txt",
                         SearchOption.TopDirectoryOnly))
            {
                string stem = Path.GetFileNameWithoutExtension(file);
                if (ulong.TryParse(stem, out ulong id))
                    offsets[id] = new OffsetData(id, "loose", file, (int)new FileInfo(file).Length, FirstPathHint(ReadText(file)));
            }
        }

        return new SubgraphIndex(byId, offsets);
    }

    /// <summary>
    /// Parse an AnimationFileData manifest: a header line, a count line, the id, a path count,
    /// then the paths. Paths under a ...\Behaviors\ folder are behavior graphs; the rest are
    /// animation files. Returns null when the file is not a readable numeric manifest.
    /// </summary>
    internal static Subgraph? ParseManifestFile(string entryName, byte[] bytes, string source, string entryPath)
    {
        string text;
        try { text = System.Text.Encoding.UTF8.GetString(bytes); }
        catch { return null; }
        return ParseManifestText(entryName, text, source, entryPath);
    }

    internal static Subgraph? ParseManifestText(string entryName, string text, string source, string entryPath)
    {
        var lines = text.Split('\n')
                        .Select(l => l.TrimEnd('\r'))
                        .Where(l => l.Length > 0)
                        .ToArray();
        if (lines.Length < 5) return null;
        if (!ulong.TryParse(lines[2].Trim(), out ulong id)) return null;

        var behaviors = new List<string>();
        var animations = new List<string>();
        foreach (string path in lines.Skip(4))
        {
            string norm = path.Replace('/', '\\');
            if (norm.IndexOf("\\Behaviors\\", StringComparison.OrdinalIgnoreCase) >= 0)
                behaviors.Add(path);
            else
                animations.Add(path);
        }
        if (behaviors.Count == 0 && animations.Count > 0)
        {
            // A manifest with only animation paths still names a subgraph via its first entry.
            behaviors.Add(animations[0]);
            animations.RemoveAt(0);
        }
        if (behaviors.Count == 0) return null;

        return new Subgraph(id, behaviors, animations, source, entryPath);
    }

    private static byte[] ReadText(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Array.Empty<byte>(); }
    }

    private static byte[] ReadEntry(string archivePath, Ba2.Entry entry)
    {
        try { return Ba2.Open(archivePath).Read(entry); }
        catch (Exception e) when (e is IOException or InvalidDataException) { return Array.Empty<byte>(); }
    }

    /// <summary>
    /// The first embedded path of an AnimationOffsets file: "V4\n" followed by a one-byte
    /// length and the behavior path (including its trailing NUL). This is a hint only — the
    /// engine binds one offset file across several subgraph instances, so the authoritative
    /// subgraph name comes from the AnimationFileData manifest.
    /// </summary>
    internal static string? FirstPathHint(byte[] bytes)
    {
        if (bytes.Length < 8 || bytes[0] != (byte)'V' || bytes[1] != (byte)'4') return null;
        int at = Array.IndexOf(bytes, (byte)'\n');
        if (at < 0 || at + 1 >= bytes.Length) return null;
        at++;
        int len = bytes[at];
        at++;
        if (at + len > bytes.Length || len == 0) return null;
        string path = System.Text.Encoding.UTF8.GetString(bytes, at, len).TrimEnd('\0');
        return path.Length > 0 ? path : null;
    }
}
