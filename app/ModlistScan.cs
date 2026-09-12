using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Archive;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.App;

/// <summary>
/// Headless load-order scanners for behaviour/animation defects.
/// </summary>
public static class ModlistScan
{
    public static int Extract(string[] args)
    {
        int i = Array.IndexOf(args, "--extract");
        if (i < 0 || i + 3 >= args.Length)
        {
            Console.Error.WriteLine("usage: --extract <archive.ba2> <substring> <outDir>");
            return 2;
        }

        string archive = args[i + 1];
        string substring = args[i + 2];
        string outDir = args[i + 3];
        if (!File.Exists(archive))
        {
            Console.Error.WriteLine($"no such archive: {archive}");
            return 2;
        }

        try
        {
            Directory.CreateDirectory(outDir);
            int n = Ba2.ExtractMatching(archive, substring, outDir, "", Console.WriteLine, keepFolders: true);
            Console.WriteLine($"extracted {n} entry(ies) matching '{substring}' to {outDir}");
            return n > 0 ? 0 : 1;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine("extract failed: " + First(e.Message));
            return 2;
        }
    }

    public static int ScanModlist(string[] args)
    {
        bool json = args.Contains("--json");
        string? instance = After(args, "--scan-modlist");
        if (instance == null)
            return Fail(json, "scan-modlist", "", "usage", "--scan-modlist needs an MO2 instance directory");

        if (!Mo2ScanData.TryOpen(instance, out var scan, out string error))
            return Fail(json, "scan-modlist", instance, "config", error);

        using (scan!)
        {
            PrintLayout(scan!, json);
            Progress("indexing mod archives for characters...");
            int archivesSeen = 0;
            var characters = scan!.ModHkxPaths("characters",
                _ => { if (++archivesSeen % 25 == 0) Progress($"  processed {archivesSeen} archive(s)..."); });
            Progress($"processed {archivesSeen} archive(s); found {characters.Count} character file(s) to check");
            var findings = new List<ScanFinding>();
            CollectArchiveWarnings(scan, json, findings);

            string temp = TempDirectory("bgs-modlist");
            int parsed = 0, broken = 0, missing = 0, unreadable = 0, roundTripLosses = 0, serial = 0;
            int processed = 0, total = characters.Count;
            try
            {
                foreach (string dataRel in characters)
                {
                    if (StepDue(++processed, total)) Progress($"  scanned {processed}/{total} character(s)...");
                    var read = scan.ReadDataRelative(dataRel);
                    if (read == null)
                    {
                        unreadable++;
                        if (!json) Console.WriteLine($"UNREADABLE CHARACTER  {dataRel}  (winning copy could not be read)");
                        findings.Add(new ScanFinding("unreadable-character", dataRel, null, new[] { "winning copy could not be read" }));
                        continue;
                    }

                    string source = scan.DescribeSource(dataRel, read);
                    roundTripLosses += AddRoundTripFinding(read.Bytes, dataRel, source, json, findings);

                    int at = FolderIndex(dataRel, "characters");
                    if (at < 0) continue;
                    string projectRel = dataRel[..at];

                    string tmp = Path.Combine(temp, $"{serial++}_{Ba2.FlatFileName(dataRel)}.hkx");
                    try { File.WriteAllBytes(tmp, read.Bytes); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        unreadable++;
                        if (!json) Console.WriteLine($"UNREADABLE CHARACTER  {dataRel}: {First(e.Message)}");
                        findings.Add(new ScanFinding("unreadable-character", dataRel, null, new[] { First(e.Message) }));
                        continue;
                    }

                    List<string> declared;
                    try
                    {
                        string xml = HkxTextEdit.TextOf(tmp);
                        if (xml.Length == 0)
                        {
                            unreadable++;
                            if (!json) Console.WriteLine($"UNREADABLE CHARACTER  {dataRel}: unsupported HKX class layout");
                            findings.Add(new ScanFinding("unreadable-character", dataRel, null, new[] { "unsupported HKX class layout" }));
                            continue;
                        }

                        var model = BehaviourGraphModel.Parse(xml);
                        var csd = model.Objects.FirstOrDefault(o => o.Class == "hkbCharacterStringData");
                        if (csd == null) continue;
                        declared = ProjectChain.DeclaredAnimations(csd);
                    }
                    catch (Exception e)
                    {
                        unreadable++;
                        if (!json) Console.WriteLine($"UNREADABLE CHARACTER  {dataRel}: {First(e.Message)}");
                        findings.Add(new ScanFinding("unreadable-character", dataRel, null, new[] { First(e.Message) }));
                        continue;
                    }
                    finally
                    {
                        try { File.Delete(tmp); } catch { }
                    }

                    if (declared.Count == 0) continue;
                    parsed++;

                    var gone = declared
                        .Where(a => !scan.ExistsDataRelative(CombineData(projectRel, a)))
                        .ToList();

                    if (gone.Count == 0) continue;
                    broken++;
                    missing += gone.Count;

                    if (!json)
                    {
                        Console.WriteLine(
                            $"MISSING ANIMATIONS  {dataRel}  [{source}]  " +
                            $"({gone.Count} of {declared.Count})");
                        foreach (string animation in gone.Take(40)) Console.WriteLine("    " + animation);
                        if (gone.Count > 40) Console.WriteLine($"    ... and {gone.Count - 40} more");
                    }
                    findings.Add(new ScanFinding("missing-animations", dataRel, source, gone));
                }
            }
            finally
            {
                TryDeleteDirectory(temp);
            }

            int exit = missing == 0 && unreadable == 0 && roundTripLosses == 0 ? 0 : 1;
            var summary = new Dictionary<string, int>
            {
                ["checked"] = parsed,
                ["broken"] = broken,
                ["missing"] = missing,
                ["unreadable"] = unreadable,
                ["roundTripLosses"] = roundTripLosses,
            };
            if (json)
                Console.WriteLine(new ScanReport("scan-modlist", ScanContextFor(scan, instance), summary, findings, exit).ToJson());
            else
                Console.WriteLine(
                    $"done  {parsed} winning character file(s) checked, {broken} with missing animations, " +
                    $"{missing} missing reference(s), {unreadable} unreadable, " +
                    $"{roundTripLosses} round-trip issue(s)");
            return exit;
        }
    }

    public static int ScanClips(string[] args)
    {
        bool json = args.Contains("--json");
        string? instance = After(args, "--scan-clips");
        if (instance == null)
            return Fail(json, "scan-clips", "", "usage", "--scan-clips needs an MO2 instance directory");

        if (!Mo2ScanData.TryOpen(instance, out var scan, out string error))
            return Fail(json, "scan-clips", instance, "config", error);

        using (scan!)
        {
            PrintLayout(scan!, json);
            Progress("indexing mod archives for behaviours...");
            int archivesSeen = 0;
            var behaviours = scan!.ModHkxPaths("behaviors",
                _ => { if (++archivesSeen % 25 == 0) Progress($"  processed {archivesSeen} archive(s)..."); });
            Progress($"processed {archivesSeen} archive(s); found {behaviours.Count} behaviour file(s) to check");
            var findings = new List<ScanFinding>();
            CollectArchiveWarnings(scan, json, findings);

            string? Resolve(string projectRel, string animation)
            {
                string flat = animation.Replace('\\', '/');
                string direct = CombineData(projectRel, flat);
                if (scan.ExistsDataRelative(direct)) return direct;

                var parts = flat.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 ||
                    !parts[0].Equals("Animations", StringComparison.OrdinalIgnoreCase))
                    return null;

                string leaf = parts[1];

                // Prefer the engine-derived weapon search prefixes already maintained by GameData.
                foreach (var set in scan.Data.WeaponTypeSets)
                    foreach (string prefix in set.Prefixes)
                    {
                        string candidate = CombineData(projectRel, prefix + "\\" + leaf);
                        if (scan.ExistsDataRelative(candidate)) return candidate;
                    }

                // If Fallout4.esm is unavailable, preserve the static fallback using every
                // weapon folder visible in the merged loose/archive data view.
                foreach (string type in scan.WeaponSubfolders(projectRel))
                {
                    string candidate = CombineData(projectRel, $"Animations/Weapon/{type}/{leaf}");
                    if (scan.ExistsDataRelative(candidate)) return candidate;
                }

                return null;
            }

            string temp = TempDirectory("bgs-clips");
            int checkedBehaviours = 0, broken = 0, missingClips = 0, unreadable = 0, roundTripLosses = 0, serial = 0;
            int processed = 0, total = behaviours.Count;
            var checkedAnimations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string dataRel in behaviours)
                {
                    if (StepDue(++processed, total)) Progress($"  scanned {processed}/{total} behaviour(s)...");
                    var read = scan.ReadDataRelative(dataRel);
                    if (read == null)
                    {
                        unreadable++;
                        if (!json) Console.WriteLine($"UNREADABLE BEHAVIOUR  {dataRel}  (winning copy could not be read)");
                        findings.Add(new ScanFinding("unreadable-behaviour", dataRel, null, new[] { "winning copy could not be read" }));
                        continue;
                    }

                    string source = scan.DescribeSource(dataRel, read);
                    roundTripLosses += AddRoundTripFinding(read.Bytes, dataRel, source, json, findings);

                    int at = FolderIndex(dataRel, "behaviors");
                    if (at < 0) continue;
                    string projectRel = dataRel[..at];

                    string tmp = Path.Combine(temp, $"{serial++}_{Ba2.FlatFileName(dataRel)}.hkx");
                    try { File.WriteAllBytes(tmp, read.Bytes); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        unreadable++;
                        if (!json) Console.WriteLine($"UNREADABLE BEHAVIOUR  {dataRel}: {First(e.Message)}");
                        findings.Add(new ScanFinding("unreadable-behaviour", dataRel, null, new[] { First(e.Message) }));
                        continue;
                    }

                    List<(string Clip, string Anim)> clips;
                    try
                    {
                        string xml = HkxTextEdit.TextOf(tmp);
                        if (xml.Length == 0)
                        {
                            unreadable++;
                            if (!json) Console.WriteLine($"UNREADABLE BEHAVIOUR  {dataRel}: unsupported HKX class layout");
                            findings.Add(new ScanFinding("unreadable-behaviour", dataRel, null, new[] { "unsupported HKX class layout" }));
                            continue;
                        }

                        var model = BehaviourGraphModel.Parse(xml);
                        clips = model.Objects
                            .Where(o => o.Class == "hkbClipGenerator")
                            .Select(o => (o.Str("name"), o.Str("animationName")))
                            .Where(c => !string.IsNullOrWhiteSpace(c.Item2))
                            .ToList();
                    }
                    catch (Exception e)
                    {
                        unreadable++;
                        if (!json) Console.WriteLine($"UNREADABLE BEHAVIOUR  {dataRel}: {First(e.Message)}");
                        findings.Add(new ScanFinding("unreadable-behaviour", dataRel, null, new[] { First(e.Message) }));
                        continue;
                    }
                    finally
                    {
                        try { File.Delete(tmp); } catch { }
                    }

                    if (clips.Count == 0) continue;
                    checkedBehaviours++;

                    var resolved = clips
                        .Select(c => (c.Clip, c.Anim, Path: Resolve(projectRel, c.Anim)))
                        .ToList();

                    foreach (string animationPath in resolved
                                 .Where(c => c.Path != null)
                                 .Select(c => c.Path!)
                                 .Where(checkedAnimations.Add))
                    {
                        var animationRead = scan.ReadDataRelative(animationPath);
                        if (animationRead == null) continue;
                        string animationSource = scan.DescribeSource(animationPath, animationRead);
                        roundTripLosses += AddRoundTripFinding(
                            animationRead.Bytes, animationPath, animationSource, json, findings);
                    }

                    var gone = resolved.Where(c => c.Path == null).ToList();
                    if (gone.Count == 0) continue;

                    broken++;
                    missingClips += gone.Count;
                    var details = gone.Select(c => $"clip '{c.Clip}' plays '{c.Anim}' - resolves nowhere").ToList();
                    if (!json)
                    {
                        Console.WriteLine(
                            $"MISSING CLIP ANIMATIONS  {dataRel}  [{source}]  " +
                            $"({gone.Count} clip(s))");
                        foreach (string line in details.Take(20)) Console.WriteLine("    " + line);
                        if (gone.Count > 20) Console.WriteLine($"    ... and {gone.Count - 20} more");
                    }
                    findings.Add(new ScanFinding("missing-clip-animations", dataRel, source, details));
                }
            }
            finally
            {
                TryDeleteDirectory(temp);
            }

            int exit = missingClips == 0 && unreadable == 0 && roundTripLosses == 0 ? 0 : 1;
            var summary = new Dictionary<string, int>
            {
                ["checked"] = checkedBehaviours,
                ["broken"] = broken,
                ["missingClips"] = missingClips,
                ["unreadable"] = unreadable,
                ["roundTripLosses"] = roundTripLosses,
            };
            if (json)
                Console.WriteLine(new ScanReport("scan-clips", ScanContextFor(scan, instance), summary, findings, exit).ToJson());
            else
                Console.WriteLine(
                    $"done  {checkedBehaviours} winning behaviour file(s) checked, " +
                    $"{broken} with unresolved clip animations, {missingClips} clip(s), {unreadable} unreadable, " +
                    $"{roundTripLosses} round-trip issue(s)");
            return exit;
        }
    }

    public static int Run(string[] args)
    {
        bool json = args.Contains("--json");
        string? root = After(args, "--scan-archives");
        if (root == null || !Directory.Exists(root))
            return Fail(json, "scan-archives", root ?? "", "usage", "--scan-archives needs an existing directory");

        bool errorsOnly = args.Contains("--errors-only");
        string temp = TempDirectory("bgs-scan");
        int archives = 0, scanned = 0, corrupt = 0, roundTripLosses = 0, serial = 0;
        var findings = new List<ScanFinding>();

        try
        {
            var ba2s = Mo2ScanData.EnumerateFilesSafe(root, "*.ba2", recursive: true)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Info(json, $"scanning {ba2s.Count} .ba2 archive(s) under {root}");

            int ba2ix = 0;
            foreach (string ba2path in ba2s)
            {
                if (StepDue(++ba2ix, ba2s.Count)) Progress($"  opened {ba2ix}/{ba2s.Count} archive(s)...");
                Ba2 archive;
                try { archive = Ba2.Open(ba2path); }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    if (!errorsOnly && !json) Console.WriteLine($"skip     {Rel(root, ba2path)}: {First(e.Message)}");
                    findings.Add(new ScanFinding("skip", Rel(root, ba2path), null, new[] { First(e.Message) }));
                    continue;
                }

                using (archive)
                {
                    var entries = archive.Entries.Where(IsBehaviourEntry).ToList();
                    if (entries.Count == 0) continue;
                    archives++;

                    foreach (var entry in entries)
                    {
                        scanned++;
                        string archiveName = Rel(root, ba2path);
                        string tmp = Path.Combine(temp, $"{serial++}_{Ba2.FlatFileName(entry.Name)}");
                        byte[] bytes;
                        try
                        {
                            bytes = archive.Read(entry);
                            File.WriteAllBytes(tmp, bytes);
                        }
                        catch (Exception e)
                        {
                            corrupt++;
                            if (!json) Console.WriteLine($"UNREADABLE  {archiveName} :: {entry.Name}: {First(e.Message)}");
                            findings.Add(new ScanFinding("unreadable", entry.Name, archiveName, new[] { First(e.Message) }));
                            continue;
                        }

                        roundTripLosses += AddRoundTripFinding(bytes, entry.Name, archiveName, json, findings);

                        var errors = CheckBehaviour(tmp);
                        try { File.Delete(tmp); } catch { }
                        if (errors.Count == 0) continue;

                        corrupt++;
                        if (!json)
                        {
                            Console.WriteLine($"CORRUPT  {archiveName} :: {entry.Name}");
                            foreach (string finding in errors) Console.WriteLine("    " + finding);
                        }
                        findings.Add(new ScanFinding("corrupt", entry.Name, archiveName, errors));
                    }
                }
            }

            var loose = Mo2ScanData.EnumerateFilesSafe(root, "*.hkx", recursive: true)
                .Where(p => IsBehaviourPath(p.Replace('\\', '/')))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Info(json, $"scanning {loose.Count} loose behaviour/character .hkx");

            int looseIx = 0;
            foreach (string path in loose)
            {
                if (StepDue(++looseIx, loose.Count)) Progress($"  checked {looseIx}/{loose.Count} loose file(s)...");
                scanned++;
                string relative = Rel(root, path);
                roundTripLosses += AddRoundTripFinding(path, relative, "loose", json, findings);

                var errors = CheckBehaviour(path);
                if (errors.Count == 0) continue;
                corrupt++;
                if (!json)
                {
                    Console.WriteLine($"CORRUPT  {relative}  (loose)");
                    foreach (string finding in errors) Console.WriteLine("    " + finding);
                }
                findings.Add(new ScanFinding("corrupt", relative, "loose", errors));
            }
        }
        finally
        {
            TryDeleteDirectory(temp);
        }

        int exit = corrupt == 0 && roundTripLosses == 0 ? 0 : 1;
        var summary = new Dictionary<string, int>
        {
            ["archives"] = archives,
            ["scanned"] = scanned,
            ["corrupt"] = corrupt,
            ["roundTripLosses"] = roundTripLosses,
        };
        if (json)
            Console.WriteLine(new ScanReport("scan-archives", new ScanContext(root, null, null, 0), summary, findings, exit).ToJson());
        else
            Console.WriteLine(
                $"done  {scanned} behaviour/character hkx scanned across {archives} archive(s) + loose, " +
                $"{corrupt} with errors, {roundTripLosses} round-trip issue(s)");
        return exit;
    }

    private static void PrintLayout(Mo2ScanData scan, bool json)
    {
        string profile = scan.Info.Profile ?? "(root modlist)";
        Info(json,
            $"profile {profile}; {scan.ModRootsHighToLow.Count} active mod root(s); " +
            $"Data {scan.Info.GameDataFolder}");
        if (Directory.Exists(scan.Info.OverwriteFolder))
            Info(json, $"overwrite {scan.Info.OverwriteFolder}");
    }

    private static void CollectArchiveWarnings(Mo2ScanData scan, bool json, List<ScanFinding> findings)
    {
        foreach (string warning in scan.DrainArchiveWarnings())
        {
            if (!json) Console.WriteLine("ARCHIVE WARNING  " + warning);
            int sep = warning.IndexOf(": ", StringComparison.Ordinal);
            string where = sep > 0 ? warning[..sep] : warning;
            string message = sep > 0 ? warning[(sep + 2)..] : warning;
            findings.Add(new ScanFinding("archive-warning", where, null, new[] { message }));
        }
    }

    private static int AddRoundTripFinding(byte[] bytes, string path, string? source, bool json,
                                           List<ScanFinding> findings)
    {
        var report = RoundTripReport.ForBytes(bytes);
        if (!report.HasLosses) return 0;

        var details = report.Losses.Select(DescribeRoundTripLoss).ToList();
        if (!json)
        {
            Console.WriteLine($"ROUND-TRIP BLOCKED  {path}" + (source == null ? "" : $"  [{source}]"));
            foreach (string detail in details) Console.WriteLine("    " + detail);
        }
        findings.Add(new ScanFinding("roundtrip-loss", path, source, details));
        return report.Count;
    }

    private static int AddRoundTripFinding(string filePath, string displayPath, string? source, bool json,
                                           List<ScanFinding> findings)
    {
        try
        {
            return AddRoundTripFinding(InputFilePolicy.ReadHkx(filePath), displayPath, source, json, findings);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            // The ordinary scanner path owns malformed/unreadable reporting. Do not turn that
            // same failure into a second round-trip finding.
            return 0;
        }
    }

    private static string DescribeRoundTripLoss(RoundTripLoss loss)
    {
        string where = loss.ObjectId is long id
            ? $"object {id}" + (loss.Member.Length > 0 ? $".{loss.Member}" : "") + ": "
            : loss.Member.Length > 0 ? loss.Member + ": " : "";
        return $"{loss.Kind}: {where}{loss.Message}";
    }

    private static ScanContext ScanContextFor(Mo2ScanData scan, string target) =>
        new(target, scan.Info.Profile, scan.Info.GameDataFolder, scan.ModRootsHighToLow.Count);

    private static void Info(bool json, string line)
    {
        if (json) Console.Error.WriteLine(line);
        else Console.WriteLine(line);
    }

    private static void Progress(string line) => Console.Error.WriteLine(line);

    private static bool StepDue(int done, int total) =>
        total > 0 && (done == total || done % Math.Max(1, total / 20) == 0);

    private static int Fail(bool json, string mode, string target, string kind, string message)
    {
        if (json)
        {
            var report = new ScanReport(mode, new ScanContext(target, null, null, 0),
                new Dictionary<string, int>(), new[] { new ScanFinding(kind, target, null, new[] { message }) }, 2);
            Console.WriteLine(report.ToJson());
        }
        else
        {
            Console.Error.WriteLine(message);
        }
        return 2;
    }

    private static string CombineData(string projectRel, string declared)
    {
        string left = projectRel.Replace('\\', '/').Trim('/');
        string right = declared.Replace('\\', '/').TrimStart('/');
        return left.Length == 0 ? right : left + "/" + right;
    }

    private static int FolderIndex(string dataRelative, string folder)
    {
        string flat = dataRelative.Replace('\\', '/');
        string needle = "/" + folder.Trim('/', '\\') + "/";
        int at = flat.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (at >= 0) return at;

        string prefix = folder.Trim('/', '\\') + "/";
        return flat.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? 0 : -1;
    }

    private static bool IsBehaviourEntry(Ba2.Entry entry) =>
        entry.Name.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase) &&
        IsBehaviourPath(entry.Name);

    private static bool IsBehaviourPath(string name)
    {
        string flat = "/" + name.Replace('\\', '/').Trim('/') + "/";
        return flat.Contains("/behaviors/", StringComparison.OrdinalIgnoreCase) ||
               flat.Contains("/characters/", StringComparison.OrdinalIgnoreCase) ||
               flat.Contains("/characterassets/", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> CheckBehaviour(string path)
    {
        var errors = new List<string>();
        try
        {
            if (!HkxBinaryReader.IsFo4Hkx(path)) return errors;
            string xml = HkxTextEdit.TextOf(path);
            if (xml.Length == 0) return errors;
            foreach (var finding in GraphValidator.Check(xml))
                if (finding.Level == GraphValidator.Level.Error)
                    errors.Add(finding.ToString());
        }
        catch (Exception e)
        {
            errors.Add("parse threw: " + First(e.Message));
        }
        return errors;
    }

    private static string TempDirectory(string prefix)
    {
        string path = Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static string? After(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string Rel(string root, string path)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return path; }
    }

    private static string First(string message) =>
        message.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? message.Trim();
}
