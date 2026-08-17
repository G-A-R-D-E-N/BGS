using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OpenCommonwealth.Services.Hkx;

namespace OpenCommonwealth.Services.Archive;

public sealed class SubgraphIndex
{
    public sealed record Subgraph(
        ulong Id,
        IReadOnlyList<string> BehaviorPaths,
        IReadOnlyList<string> AnimationPaths,
        string Source,
        string EntryName)
    {
        public string PrimaryBehavior => BehaviorPaths.Count > 0 ? BehaviorPaths[0] : "";
    }

    public sealed record OffsetData(ulong Id, string Source, string EntryName, int Bytes, string? FirstPathHint);

    private readonly Dictionary<ulong, Subgraph> _byId;
    private readonly Dictionary<ulong, OffsetData> _offsets;

    private SubgraphIndex(Dictionary<ulong, Subgraph> byId, Dictionary<ulong, OffsetData> offsets)
    {
        _byId = byId;
        _offsets = offsets;
    }

    public IReadOnlyDictionary<ulong, Subgraph> Subgraphs => _byId;

    public Subgraph? Find(ulong id) => _byId.TryGetValue(id, out var s) ? s : null;

    public OffsetData? FindOffsetData(ulong id) => _offsets.TryGetValue(id, out var o) ? o : null;

    public static SubgraphIndex Discover(GameData data)
    {
        var byId = new Dictionary<ulong, Subgraph>();
        var offsets = new Dictionary<ulong, OffsetData>();

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

    public static (bool WeaponSubgraph, List<GraphValidator.Finding> Gaps) WeaponGapFindings(
        GameData data, IEnumerable<string> behaviorPaths)
    {
        bool weaponSubgraph = false;
        var gaps = new List<GraphValidator.Finding>();
        foreach (string behavior in behaviorPaths)
        {
            string norm = behavior.Replace('/', '\\');
            int at = norm.LastIndexOf("\\Behaviors\\", StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;

            string root = Path.Combine(data.DataFolder, "Meshes", norm[..at]);
            var read = data.ReadAnimation(root, norm[(at + 1)..]);
            if (read == null) continue;

            string xml;
            try { xml = NativeXml.From(read.Bytes); }
            catch { continue; }
            if (xml.Length == 0) continue;

            if (xml.IndexOf(@"Animations\Weapon\", StringComparison.OrdinalIgnoreCase) >= 0)
                weaponSubgraph = true;

            var chain = new ProjectChain { Root = root, Data = data };
            List<GraphValidator.Finding> findings;
            try { findings = GraphValidator.Check(xml, chain); }
            catch { continue; }
            gaps.AddRange(findings.Where(f => f.Where == "weapon subgraph"));
        }
        return (weaponSubgraph, gaps);
    }

    public static ulong? ExtractSubgraphHash(string input)
    {
        string text = input;
        if (File.Exists(input))
        {
            try { text = File.ReadAllText(input); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        }

        var patterns = new[]
        {
            new Regex(@"AnimationOffsets[\\/](\d+)\.txt"),
            new Regex(@"(\d{9,20})"),
        };
        foreach (var re in patterns)
        {
            var m = re.Match(text);
            if (m.Success && ulong.TryParse(m.Groups[1].Value, out ulong id)) return id;
        }
        return null;
    }

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
