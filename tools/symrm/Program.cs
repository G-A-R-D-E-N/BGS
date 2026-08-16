using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.Tools;

public static class Program
{
    private static string _root = "";

    public static int Main(string[] argv)
    {
        _root = RepoRoot();

        if (argv.Length == 0) { Usage(); return 1; }

        switch (argv[0])
        {
            case "corpus": return Corpus(argv);
            case "check": return Check(argv);
            case "states": return States(argv);
            case "events": return Events(argv);
            case "frames": return Frames(argv);
            case "scale": return Scale(argv);
            case "skeleton": return Skeleton(argv);
            case "rig": return Rig(argv);
            case "extract": return Extract(argv);
            case "ba2": return Ba2Browse(argv);
            case "motion": return Motion(argv);
            case "pose": return Pose(argv);
            case "channels": return Channels(argv);
            case "packfile": return Packfile(argv);
            case "layout": return Layout(argv);
            case "relayout": return Relayout(argv);
            case "ground": return Ground(argv);
            case "offsets": return Offsets(argv);
            case "convert": return Convert(argv);
            case "compare": return Compare(argv);
            case "delete": return DeleteObject(argv);
            case "paste": return Paste(argv);
            case "template": return Template(argv);
            case "conditions": return Conditions(argv);
            case "savedelete": return SaveDelete(argv);
            case "classcheck": return ClassCheck(argv);
            case "chain": return Chain(argv);
            case "notes": return Notes(argv);
            case "saveevent": return SaveEvent(argv);
            case "savewide": return SaveWide(argv);
            case "savenumbers": return SaveNumbers(argv);
            case "walk": return Walk(argv);
            case "signatures": return Signatures(argv);
            case "paths": return Paths(argv);
            case "elements": return Elements(argv);
            case "nesting": return Nesting(argv);
            case "objects": return Objects(argv);
            case "capacity": return Capacity(argv);
            case "qstransform": return QsTransform(argv);
            case "splinestats": return SplineStats(argv);
            case "spline": return Spline(argv);
            case "savespline": return SaveSpline(argv);
            case "editframe": return EditFrame(argv);
            case "trim": return Trim(argv);
            case "retime": return Retime(argv);
            case "run": return Run(argv);
            case "weights": return Weights(argv);
            case "cliptime": return ClipTime(argv);
            case "cliptrim": return ClipTrim(argv);
            case "mesh": return Mesh(argv);
            case "meshpng": return DrawMesh(argv);
            case "lifecycle": return Lifecycle(argv);
            case "test": return Tests.Run();
            case "defaults": return Defaults(argv);
            default: Usage(); return 1;
        }
    }

    private static void Usage() => Console.WriteLine("""
        symrm, the native verification harness for Behaviour Graph Studio.

          lifecycle <hkxDir | file.hkx>
              Native open, edit, save, reload, validate, and render gate for supported files.

          test
              Regression checks that use native code only.

          Other commands inspect or validate native HKX data. Run a command with no arguments for
          its required input. The retired Java packer commands are intentionally unavailable.
        """);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Hkx")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static int Defaults(string[] argv)
    {
        if (argv.Length < 3)
        {
            Console.Error.WriteLine(
                "defaults <Fallout4.exe.unpacked.exe> <Fallout4_163_functions.txt>");
            return 1;
        }

        var types = HavokClassTypes.Shipped;
        var (read, refused) = GameDefaults.Of(argv[1], argv[2], types);

        string? only = argv.Skip(3).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        if (only != null)
        {
            foreach (var one in read.Where(r => r.ClassName.Contains(only, StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"{one.ClassName}: {one.Members} member(s), {one.ObjectSize} bytes, version {one.Version}");
                foreach (var member in types[one.ClassName]!.Declared)
                {
                    one.Defaults.TryGetValue(member.Name, out string? game);
                    if (game == null && member.Default == null) continue;
                    string mark = game == null ? "zero by omission" : game;
                    Console.WriteLine($"    {member.Name,-40} {mark,-42} table {member.Default ?? "-"}");
                }
            }
            return 0;
        }

        int agreed = 0, differed = 0, gained = 0, lost = 0, zeroed = 0;
        var disagreements = new List<string>();
        var lostOnes = new List<string>();
        var gainedOnes = new List<string>();

        foreach (var found in read)
        {
            foreach (var member in types[found.ClassName]!.Declared)
            {
                found.Defaults.TryGetValue(member.Name, out string? game);
                string? table = member.Default;

                if (table == null && game == null) continue;
                if (table == null)
                {
                    gained++;
                    if (found.ClassName.StartsWith("hkb", StringComparison.Ordinal) ||
                        found.ClassName.StartsWith("BS", StringComparison.Ordinal))
                        gainedOnes.Add($"{found.ClassName}.{member.Name} = {game}");
                    continue;
                }
                if (game == null)
                {

                    if (IsZero(table)) { zeroed++; continue; }
                    lost++;
                    lostOnes.Add($"{found.ClassName}.{member.Name} = {table}");
                    continue;
                }

                if (SameDefault(table, game)) agreed++;
                else
                {
                    differed++;
                    disagreements.Add($"{found.ClassName}.{member.Name}: table {table}, game {game}");
                }
            }
        }

        Console.WriteLine($"{read.Count} class(es) read, {refused.Count} refused");
        Console.WriteLine($"  the table already had  : {agreed + differed + lost}");
        Console.WriteLine($"    agreeing             : {agreed}");
        Console.WriteLine($"    disagreeing          : {differed}");
        Console.WriteLine($"    zero, which the game stores by leaving out: {zeroed}");
        Console.WriteLine($"    the game does not set and is not zero      : {lost}");
        Console.WriteLine($"  the game adds          : {gained}");

        foreach (var why in refused.Take(12)) Console.WriteLine("  refused: " + why);
        if (refused.Count > 12) Console.WriteLine($"  ... and {refused.Count - 12} more");

        foreach (string line in disagreements.Take(25)) Console.WriteLine("  differs: " + line);
        if (disagreements.Count > 25) Console.WriteLine($"  ... and {disagreements.Count - 25} more");

        foreach (string line in gainedOnes.Take(30)) Console.WriteLine("  the game says: " + line);
        if (gainedOnes.Count > 30) Console.WriteLine($"  ... and {gainedOnes.Count - 30} more on behaviour classes");

        foreach (string line in lostOnes.Take(10)) Console.WriteLine("  only in the table: " + line);
        if (lostOnes.Count > 10) Console.WriteLine($"  ... and {lostOnes.Count - 10} more");

        if (argv.Contains("--write"))
        {
            if (differed != 0 || lost != 0)
            {
                Console.Error.WriteLine(
                    "Refusing to write: the two sources disagree somewhere, so the reading is not " +
                    "trustworthy enough to fold in. Fix the disagreement first.");
                return 1;
            }
            return WriteDefaults(read, types);
        }

        return differed == 0 ? 0 : 1;
    }

    private static int WriteDefaults(List<GameDefaults.Found> read, HavokClassTypes types)
    {
        string path = Path.Combine("src", "Hkx", "HavokClassTypes.json");
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"{path} is not here, so run this from the top of the repository.");
            return 1;
        }

        var doc = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var classes = doc["classes"]!.AsObject();
        var byName = read.ToDictionary(r => r.ClassName, StringComparer.Ordinal);

        int added = 0, touched = 0;
        foreach (var (className, node) in classes)
        {
            if (!byName.TryGetValue(className, out var found)) continue;

            bool any = false;
            foreach (var member in node!["members"]!.AsArray())
            {
                string name = member!["name"]!.GetValue<string>();
                if (member["default"] != null && member["default"]!.GetValueKind() !=
                    System.Text.Json.JsonValueKind.Null) continue;
                if (!found.Defaults.TryGetValue(name, out string? game)) continue;

                member["default"] = game;
                added++;
                any = true;
            }
            if (any) touched++;
        }

        var text = new System.Text.StringBuilder();
        text.Append("{\n\"note\":");
        text.Append(JsonSerializer.Serialize(TableNote));
        text.Append(",\n\"havokVersion\":");
        text.Append(JsonSerializer.Serialize(doc["havokVersion"]!.GetValue<string>()));
        text.Append(",\n\"classes\":{\n");
        text.Append(string.Join(",\n", classes.Select(c =>
            JsonSerializer.Serialize(c.Key) + ":" + c.Value!.ToJsonString())));
        text.Append("\n}\n}\n");

        File.WriteAllText(path, text.ToString());

        Console.WriteLine($"wrote {added} default(s) onto {touched} class(es) in {path}");
        Console.WriteLine("  rerun without --write: everything should agree and nothing should be added");
        return 0;
    }

    private static readonly string TableNote =
        "What a Havok class is made of. The member types, which members are ever written to a " +
        "file, the class of every inline struct and every enum's values come from the class " +
        "published class-description metadata (MIT, see THIRD_PARTY_NOTICES.md). " +
        "The instance sizes come from HavokClassLayouts.json, which was read out of Fallout 4 " +
        "itself. The table is checked in and maintained with the native source. Its defaults come " +
        "from the game's own class registrations.";

    private static bool IsZero(string value)
    {
        string t = value.Trim();
        if (t == "false") return true;
        if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return f == 0;
        return t.Trim('(', ')', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(x => float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out float e) && e == 0);
    }

    private static bool SameDefault(string table, string game)
    {
        if (string.Equals(table.Trim(), game.Trim(), StringComparison.OrdinalIgnoreCase)) return true;

        if (float.TryParse(table, NumberStyles.Float, CultureInfo.InvariantCulture, out float a) &&
            float.TryParse(game, NumberStyles.Float, CultureInfo.InvariantCulture, out float b))
        {

            float scale = Math.Max(Math.Abs(a), Math.Abs(b));
            return Math.Abs(a - b) <= (scale > 1 ? scale * 1e-6f : 1e-6f);
        }

        return false;
    }

    private static string WorkDirectory(string prefix, string file)
    {
        string work = Path.Combine(Path.GetTempPath(), prefix + Path.GetFileNameWithoutExtension(file));
        string holding = Path.GetDirectoryName(Path.GetFullPath(file)) ?? "";

        return Path.GetFullPath(work).TrimEnd(Path.DirectorySeparatorChar) ==
               holding.TrimEnd(Path.DirectorySeparatorChar)
            ? work + "-work"
            : work;
    }

    private static int Corpus(string[] argv)
    {
        if (argv.Length < 3) { Usage(); return 1; }

        string filter = argv.Length > 3 ? argv[3] : "behavior";
        int written = OpenCommonwealth.Services.Archive.Ba2.ExtractMatching(argv[1], filter, argv[2], ".hkx", Console.WriteLine);
        Console.WriteLine($"wrote {written} file(s) matching \"{filter}\" to {argv[2]}");
        return 0;
    }

    private static int States(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        var files = Directory.EnumerateFiles(Path.GetFullPath(argv[1]), "*.xml", SearchOption.AllDirectories)
                             .OrderBy(f => f).ToList();

        int states = 0, noGenerator = 0, dangling = 0;
        var classes = new Dictionary<string, int>();

        foreach (string file in files)
        {
            var model = BehaviourGraphModel.Parse(HkxTextEdit.ReadXml(file));
            foreach (var machine in model.Objects.Where(o => o.Class == "hkbStateMachine"))
                foreach (var state in StateEditor.States(model, machine.Id))
                {
                    states++;
                    if (GraphValidator.HasNoGenerator(state)) { noGenerator++; continue; }

                    var target = model.Get(state.GeneratorRef.TrimStart('#'));
                    if (target == null) { dangling++; continue; }
                    classes[target.Class] = classes.GetValueOrDefault(target.Class) + 1;
                }
        }

        Console.WriteLine($"{states} states across {files.Count} files");
        Console.WriteLine($"  {noGenerator} with no generator");
        Console.WriteLine($"  {dangling} pointing at an object not in the file");
        Console.WriteLine($"  {classes.Count} generator classes");
        foreach (var kv in classes.OrderByDescending(k => k.Value))
            Console.WriteLine($"  {kv.Value,6}  {kv.Key}");

        return noGenerator + dangling == 0 ? 0 : 1;
    }

    private static int Frames(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        if (Directory.Exists(target))
        {
            var files = Directory.EnumerateFiles(target, "*.hkx", SearchOption.AllDirectories)
                                 .Where(f => f.Contains($"{Path.DirectorySeparatorChar}Animations{Path.DirectorySeparatorChar}",
                                                        StringComparison.OrdinalIgnoreCase))
                                 .OrderBy(f => f).ToList();

            int read = 0, empty = 0, unsupported = 0, threw = 0;
            var classes = new Dictionary<string, int>();
            var reasons = new Dictionary<string, int>();

            foreach (string file in files)
            {
                try
                {
                    var a = new HkxBinaryReader().ReadAnimation(file);
                    bool any = a.Tracks.Any(t => t.Rotations.Count > 0 || t.Translations.Count > 0 || t.Scales.Count > 0);
                    if (a.NumFrames <= 0 || !any) { empty++; Console.WriteLine($"  empty  {Short(file, target)}  {a.GetSummary()}"); }
                    else read++;
                }
                catch (NotSupportedException ex)
                {
                    unsupported++;
                    string cls = ex.Message.Split('.')[0].Replace("unsupported animation class: ", "");
                    classes[cls] = classes.GetValueOrDefault(cls) + 1;
                }
                catch (Exception ex)
                {
                    threw++;
                    string why = ex.Message.Split('\n')[0];
                    reasons[why] = reasons.GetValueOrDefault(why) + 1;
                }
            }

            Console.WriteLine($"\n{files.Count} animations: {read} decoded, {unsupported} in a class this reader " +
                              $"does not decode, {empty} decoded to nothing, {threw} threw");
            foreach (var kv in classes.OrderByDescending(k => k.Value)) Console.WriteLine($"  {kv.Value,5}  {kv.Key}");
            foreach (var kv in reasons.OrderByDescending(k => k.Value)) Console.WriteLine($"  {kv.Value,5}  {kv.Key}");
            return threw == 0 && empty == 0 ? 0 : 1;
        }

        if (argv.Length > 2 && argv[2] == "--digest")
        {
            var a = new HkxBinaryReader().ReadAnimation(target);
            double sum = 0;
            foreach (var tr in a.Tracks)
            {
                foreach (var v in tr.Translations) sum += v.X + v.Y + v.Z;
                foreach (var q in tr.Rotations) sum += q.X + q.Y + q.Z + q.W;
                foreach (var v in tr.Scales) sum += v.X + v.Y + v.Z;
            }
            Console.WriteLine($"{a.NumTracks} {a.NumFrames} {a.Duration:F4} {sum:F3}");
            return 0;
        }

        HkxAnimationData anim;
        try
        {
            anim = new HkxBinaryReader().ReadAnimation(target);
        }
        catch (NotSupportedException ex)
        {
            Console.WriteLine($"{Path.GetFileName(target)}");
            Console.WriteLine("  " + ex.Message);
            return 1;
        }

        Console.WriteLine($"{Path.GetFileName(target)}");
        Console.WriteLine("  " + anim.GetSummary());
        Console.WriteLine($"  blocks {anim.NumBlocks}, up to {anim.MaxFramesPerBlock} frames each, " +
                          $"block duration {anim.BlockDuration:F3}s, blend hint {anim.BlendHint}");
        if (anim.OriginalSkeletonName.Length > 0) Console.WriteLine($"  skeleton named in the file: {anim.OriginalSkeletonName}");

        foreach (var note in anim.Annotations) Console.WriteLine($"  annotation {note.Time:F3}s  {note.Text}");

        var skeleton = SiblingSkeleton(target);
        Console.WriteLine(skeleton == null
            ? "  no skeleton found beside it, so tracks are numbered rather than named"
            : $"  naming tracks from {skeleton.BoneNames.Count} bones in the sibling skeleton");

        int tracks = Math.Min(anim.Tracks.Count, argv.Length > 2 && int.TryParse(argv[2], out int n) ? n : 8);
        for (int t = 0; t < tracks; t++)
        {
            var track = anim.Tracks[t];
            string bone = TrackName(anim, skeleton, t);
            bool scaled = HkxTrackData.IsScaled(track);
            Console.WriteLine($"\n  {bone}: {track.Translations.Count} translations, " +
                              $"{track.Rotations.Count} rotations, {track.Scales.Count} scales" +
                              (track.Scales.Count == 0 ? "" : scaled ? ", scale is not the identity" : ", scale is 1,1,1 throughout"));

            int frames = Math.Min(4, Math.Max(Math.Max(track.Rotations.Count, track.Translations.Count),
                                              track.Scales.Count));
            for (int f = 0; f < frames; f++)
            {
                string pos = f < track.Translations.Count
                    ? $"pos {track.Translations[f].X,8:F3} {track.Translations[f].Y,8:F3} {track.Translations[f].Z,8:F3}" : "";
                string rot = f < track.Rotations.Count
                    ? $"  rot {track.Rotations[f].X,7:F4} {track.Rotations[f].Y,7:F4} {track.Rotations[f].Z,7:F4} {track.Rotations[f].W,7:F4}" : "";

                string scl = scaled && f < track.Scales.Count
                    ? $"  scale {track.Scales[f].X,7:F4} {track.Scales[f].Y,7:F4} {track.Scales[f].Z,7:F4}" : "";
                Console.WriteLine($"    frame {f,4}  t={f * anim.FrameDuration,7:F3}s  {pos}{rot}{scl}");
            }
        }

        Console.WriteLine();
        foreach (float fraction in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            Console.WriteLine($"  userControlledTimeFraction {fraction:F2} -> frame {anim.FrameAt(fraction)} " +
                              $"of {Math.Max(anim.NumFrames - 1, 0)}");

        return 0;
    }

    private static HkxSkeleton? SiblingSkeleton(string animationPath)
    {
        string? assets = SiblingSkeletonFolder(animationPath);
        if (assets == null) return null;

        foreach (string file in Directory.EnumerateFiles(assets, "*.hkx").OrderBy(f => f))
        {
            try { return new HkxBinaryReader().ReadSkeleton(file); }
            catch { }
        }
        return null;
    }

    private static string? SiblingSkeletonFolder(string animationPath)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(animationPath)) ?? "");
        while (dir != null)
        {
            if (dir.Name.Equals("Animations", StringComparison.OrdinalIgnoreCase))
            {
                var characterRoot = dir.Parent;
                if (characterRoot == null) return null;

                string assets = Path.Combine(characterRoot.FullName, "CharacterAssets");
                return Directory.Exists(assets) ? assets : null;
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static string TrackName(HkxAnimationData anim, HkxSkeleton? skeleton, int track)
    {
        if (skeleton != null && track < anim.TrackToBoneIndices.Count)
        {
            int bone = anim.TrackToBoneIndices[track];
            if (bone >= 0 && bone < skeleton.BoneNames.Count) return skeleton.BoneNames[bone];
        }

        string annotation = track < anim.BoneNames.Count ? anim.BoneNames[track] : "";
        return annotation.Length > 0 ? annotation : $"track {track}";
    }

    private static int Events(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        var files = Directory.Exists(argv[1])
            ? Directory.GetFiles(argv[1], "*.xml").OrderBy(f => f).ToList()
            : new List<string> { argv[1] };
        bool one = files.Count == 1;

        int declared = 0, withSites = 0, listenedOnly = 0, unnamedSites = 0;
        var perSite = new Dictionary<string, int>(StringComparer.Ordinal);
        var roles = new Dictionary<EventUsage.Role, int>();

        foreach (string file in files)
        {
            string xml = HkxTextEdit.ReadXml(file);
            var names = SymbolEditor.EventNames(BehaviourGraphModel.Parse(xml));
            var usage = EventUsage.ByEvent(xml);
            declared += names.Count;

            if (one) Console.WriteLine($"{Path.GetFileName(file)}: {names.Count} events declared");

            for (int i = 0; i < names.Count; i++)
            {
                if (!usage.TryGetValue(i, out var lines)) continue;
                withSites++;
                if (lines.All(l => l.Role != EventUsage.Role.Raised)) listenedOnly++;

                foreach (var line in lines)
                {
                    perSite[line.Site] = perSite.GetValueOrDefault(line.Site) + line.Count;
                    roles[line.Role] = roles.GetValueOrDefault(line.Role) + line.Count;
                    if (line.Role == EventUsage.Role.Referenced) unnamedSites += line.Count;
                }

                if (!one) continue;
                Console.WriteLine($"  #{i} {names[i]}: {EventUsage.Summarise(lines)}");
                foreach (var line in lines)
                    Console.WriteLine($"      {EventUsage.Describe(line.Role),-18} {line.Site}" +
                                      (line.Count > 1 ? $" x{line.Count}" : "") +
                                      (line.Note.Length > 0 ? $"  ({line.Note})" : ""));
            }
        }

        Console.WriteLine($"\n{files.Count} file(s), {declared} events declared, {withSites} used somewhere in their own file");
        Console.WriteLine($"{listenedOnly} of those are listened for with nothing in the file sending them, " +
                          "which is the ordinary case and not reported as a finding");
        foreach (var kv in roles.OrderByDescending(k => k.Value))
            Console.WriteLine($"  {kv.Value,7}  {EventUsage.Describe(kv.Key)}");

        Console.WriteLine($"\n{perSite.Count} class member pairs seen:");
        foreach (var kv in perSite.OrderByDescending(k => k.Value))
            Console.WriteLine($"  {kv.Value,7}  {EventUsage.Describe(EventUsage.RoleOf(kv.Key)),-18} {kv.Key}");

        if (unnamedSites > 0)
            Console.WriteLine($"\n{unnamedSites} references have no role in the table; they show as referenced, not guessed at");
        return 0;
    }

    private static int Scale(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }
        string target = Path.GetFullPath(argv[1]);

        var files = Directory.Exists(target)
            ? Directory.EnumerateFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .Where(f => f.Contains($"{Path.DirectorySeparatorChar}Animations{Path.DirectorySeparatorChar}",
                                              StringComparison.OrdinalIgnoreCase))
                       .OrderBy(f => f).ToList()
            : new List<string> { target };

        int read = 0, withScale = 0, degenerate = 0;
        var byClass = new Dictionary<string, int>();
        var scaledByClass = new Dictionary<string, int>();

        foreach (string file in files)
        {
            HkxAnimationData a;
            try { a = new HkxBinaryReader().ReadAnimation(file); }
            catch { continue; }
            read++;
            byClass[a.AnimationClass] = byClass.GetValueOrDefault(a.AnimationClass) + 1;

            float lo = float.MaxValue, hi = float.MinValue;
            int oddTracks = 0, zeroFrames = 0;
            for (int t = 0; t < a.Tracks.Count; t++)
            {
                bool odd = false;
                foreach (var s in a.Tracks[t].Scales)
                {
                    foreach (float v in new[] { s.X, s.Y, s.Z })
                    {
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;

                        if (v == 0f) zeroFrames++;
                        if (Math.Abs(v - 1f) > 0.0001f) odd = true;
                    }
                }
                if (odd) oddTracks++;
            }

            if (oddTracks == 0) continue;
            withScale++;
            scaledByClass[a.AnimationClass] = scaledByClass.GetValueOrDefault(a.AnimationClass) + 1;
            if (zeroFrames > 0) degenerate++;

            Console.WriteLine($"  {Short(file, Directory.Exists(target) ? target : Path.GetDirectoryName(target)!),-64} " +
                              $"{a.AnimationClass,-34} {oddTracks}/{a.Tracks.Count} tracks  " +
                              $"range {lo:F4}..{hi:F4}" + (zeroFrames > 0 ? $"  ZERO x{zeroFrames}" : ""));
        }

        Console.WriteLine($"\n{read} animations read, {withScale} carry a scale that is not the identity, " +
                          $"{degenerate} of those contain a zero");
        foreach (var kv in byClass.OrderByDescending(k => k.Value))
            Console.WriteLine($"  {kv.Value,6} {kv.Key}, {scaledByClass.GetValueOrDefault(kv.Key)} of them scaled");
        return 0;
    }

    private static string Short(string file, string root) =>
        file.StartsWith(root, StringComparison.Ordinal) ? file[(root.Length + 1)..] : file;

    private static int Extract(string[] argv)
    {
        if (argv.Length < 4) { Usage(); return 1; }

        bool tree = Array.IndexOf(argv, "--tree") >= 0;
        string extension = argv.Length > 4 && argv[4] != "--tree" ? argv[4] : ".hkx";
        int written = OpenCommonwealth.Services.Archive.Ba2.ExtractMatching(Path.GetFullPath(argv[1]), argv[2], Path.GetFullPath(argv[3]),
                                          extension, Console.WriteLine, tree);
        Console.WriteLine($"wrote {written} files to {Path.GetFullPath(argv[3])}");
        return written > 0 ? 0 : 1;
    }

    private static List<(char Side, int At, string Line)> Diff(string[] left, string[] right)
    {
        const int Reach = 400;
        var edits = new List<(char, int, string)>();

        int a = 0, b = 0;
        while (a < left.Length && b < right.Length)
        {
            if (left[a] == right[b]) { a++; b++; continue; }

            int found = -1, skipLeft = 0, skipRight = 0;
            for (int d = 1; d <= Reach && found < 0; d++)
            {
                if (a + d < left.Length && left[a + d] == right[b]) { found = d; skipLeft = d; }
                else if (b + d < right.Length && left[a] == right[b + d]) { found = d; skipRight = d; }
            }

            if (found < 0)
            {
                edits.Add(('-', a++, left[a - 1]));
                edits.Add(('+', b++, right[b - 1]));
                continue;
            }

            for (int i = 0; i < skipLeft; i++) edits.Add(('-', a + i, left[a + i]));
            for (int i = 0; i < skipRight; i++) edits.Add(('+', b + i, right[b + i]));
            a += skipLeft;
            b += skipRight;
        }

        while (a < left.Length) edits.Add(('-', a, left[a++]));
        while (b < right.Length) edits.Add(('+', b, right[b++]));

        return edits;
    }

    private static string FirstBound(string xml, string which)
    {
        int start = xml.IndexOf("name=\"variableBounds\"", StringComparison.Ordinal);
        if (start < 0) return "absent";

        var m = System.Text.RegularExpressions.Regex.Match(
            xml[start..], $"name=\"{which}\".*?name=\"value\">(-?\\d+)<",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : "absent";
    }

    private static int Motion(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);

        if (Directory.Exists(target))
        {
            var files = Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories);
            int carrying = 0, still = 0, failed = 0;
            float furthest = 0;
            string furthestName = "";

            foreach (string file in files)
            {
                try
                {
                    var read = RootMotion.Read(file);
                    if (!read.Any) { still++; continue; }

                    carrying++;
                    if (read.Travel.Length() > furthest)
                    {
                        furthest = read.Travel.Length();
                        furthestName = Path.GetFileName(file);
                    }
                }
                catch { failed++; }
            }

            Console.WriteLine($"{files.Length} files: {carrying} carry root motion, {still} stay on " +
                              $"the spot, {failed} could not be read");
            if (carrying > 0)
                Console.WriteLine($"furthest travelled: {furthestName} at {furthest:F1} units");
            return failed == files.Length ? 1 : 0;
        }

        var motion = RootMotion.Read(target);
        Console.WriteLine($"{Path.GetFileName(target)}: {motion}");
        if (!motion.Any) return 0;

        Console.WriteLine($"up {motion.Up.X:F0} {motion.Up.Y:F0} {motion.Up.Z:F0}, " +
                          $"forward {motion.Forward.X:F0} {motion.Forward.Y:F0} {motion.Forward.Z:F0}");

        for (int i = 0; i < motion.Samples.Count; i += Math.Max(1, motion.Samples.Count / 8))
            Console.WriteLine($"  sample {i,3}  {motion.Samples[i]}");

        Console.WriteLine($"  sample {motion.Samples.Count - 1,3}  {motion.Samples[^1]}");

        var half = RootMotion.At(motion, 0.5f);
        Console.WriteLine($"halfway through: {half}");
        return 0;
    }

    private static int Ba2Browse(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        using var archive = OpenCommonwealth.Services.Archive.Ba2.Open(Path.GetFullPath(argv[1]));
        Console.WriteLine($"{Path.GetFileName(argv[1])}: version {archive.Version}, " +
                          $"{archive.Entries.Count} entries");

        string query = argv.Length > 2 ? argv[2] : "";
        string extension = argv.Length > 3 ? argv[3] : "";

        var found = archive.Matching(query, extension).ToList();
        Console.WriteLine($"{found.Count} match \"{query}\"{(extension.Length > 0 ? " ending " + extension : "")}");

        foreach (var entry in found.Take(20))
            Console.WriteLine($"  {entry.Name}  {entry.Unpacked} bytes" +
                              (entry.Packed != 0 ? $", stored as {entry.Packed}" : ", stored plain"));

        if (found.Count > 20) Console.WriteLine($"  and {found.Count - 20} more");
        if (found.Count == 0) return 1;

        var first = found[0];
        byte[] bytes = archive.Read(first);
        Console.WriteLine($"\nread {first.FileName}: {bytes.Length} bytes" +
                          (bytes.Length == first.Unpacked ? ", the length the index promised"
                           : $", but the index said {first.Unpacked}"));

        if (bytes.Length != first.Unpacked) return 1;
        if (!first.Name.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase)) return 0;

        try
        {
            var image = PackfileImage.Read(bytes);
            var objects = new PackfileObjects(image);
            Console.WriteLine($"and it reads as a packfile: {objects.Instances.Count} objects, " +
                              $"{objects.ClassNames().Count()} classes named");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("but it does not read as a packfile: " + e.Message);
            return 1;
        }
    }

    private static bool NullSaveIsByteIdentical(string file, string originalXml)
    {
        var plan = NativeSave.Compare(originalXml, originalXml);
        if (!plan.Possible)
        {
            Console.WriteLine($"  null save: REFUSED, {plan.Refusal}");
            return false;
        }
        if (!plan.Empty)
        {
            Console.WriteLine($"  null save: FAILED, an unchanged file planned {plan.Changes.Count} change(s)");
            return false;
        }

        byte[] before = InputFilePolicy.ReadHkx(file);
        byte[] after = NativeSave.Apply(file, plan);

        if (before.Length != after.Length)
        {
            Console.WriteLine($"  null save: FAILED, {before.Length} bytes in, {after.Length} out");
            return false;
        }

        int firstDiff = -1, differing = 0;
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i] == after[i]) continue;
            if (firstDiff < 0) firstDiff = i;
            differing++;
        }

        if (differing > 0)
        {
            Console.WriteLine($"  null save: FAILED, {differing} byte(s) differ, first at 0x{firstDiff:x}");
            return false;
        }

        Console.WriteLine($"  null save: identical, all {before.Length} bytes");
        return true;
    }

    private static bool OnlyAppended(byte[] before, byte[] after, NativeSave.Plan plan)
    {
        var was = PackfileImage.Read(before);
        var now = PackfileImage.Read(after);

        int allowedPointerMoves = plan.Changes.Count(c => c.Ref);
        if (plan.Changes.Any(c => c.Array))
        {
            var original = new PackfileObjects(was);
            var byClass = original.Instances.GroupBy(o => o.ClassName)
                                            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            foreach (var change in plan.Changes.Where(c => c.Array))
            {
                int had = byClass.TryGetValue(change.ClassName, out var all) && change.Index < all.Count
                    ? original.ReadArray(all[change.Index], change.Field)?.Count ?? 0
                    : 0;

                int now_ = change.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                allowedPointerMoves += had + now_;
            }
        }

        if (was.Sections.Count != now.Sections.Count)
        {
            Console.WriteLine("  append check: FAILED, the section count changed");
            return false;
        }

        for (int i = 0; i < was.Sections.Count; i++)
        {
            var (a, b) = (was.Sections[i], now.Sections[i]);

            if (b.Data.Length < a.Data.Length)
            {
                Console.WriteLine($"  append check: FAILED, section {a.Tag} shrank");
                return false;
            }

            int touched = Enumerable.Range(0, a.Data.Length).Count(k => a.Data[k] != b.Data[k]);
            int allowed = 4 * plan.Changes.Count(c => !c.Text && !c.Ref);
            if (touched > allowed)
            {
                Console.WriteLine($"  append check: FAILED, {touched} byte(s) of {a.Tag} changed, " +
                                  $"more than the {allowed} the planned values can account for");
                return false;
            }

            var wasBySource = a.Globals().ToDictionary(g => g.Source, g => (g.Section, g.Destination));
            var nowBySource = b.Globals().ToDictionary(g => g.Source, g => (g.Section, g.Destination));

            var repointedSources = wasBySource.Keys.Union(nowBySource.Keys)
                .Where(k => !wasBySource.TryGetValue(k, out var x) ||
                            !nowBySource.TryGetValue(k, out var y) || x != y)
                .ToList();

            if (repointedSources.Count > allowedPointerMoves)
            {
                Console.WriteLine($"  append check: FAILED, {repointedSources.Count} pointer(s) in " +
                                  $"{a.Tag} changed, more than the {allowedPointerMoves} the plan " +
                                  "accounts for");
                return false;
            }

            int added = a.Tag == "__data__" ? plan.Changes.Count(c => c.Added) : 0;
            var wasVirtual = a.Virtuals().ToList();
            var nowVirtual = b.Virtuals().ToList();

            if (nowVirtual.Count != wasVirtual.Count + added ||
                !nowVirtual.Take(wasVirtual.Count).SequenceEqual(wasVirtual))
            {
                Console.WriteLine($"  append check: FAILED, section {a.Tag} has {nowVirtual.Count} " +
                                  $"object(s) where it had {wasVirtual.Count} and {added} were added");
                return false;
            }

            var (locals, wasLocals) = (b.Locals().ToList(), a.Locals().ToList());

            var wasLocalsBySource = wasLocals.ToDictionary(l => l.Source, l => l.Destination);
            var nowLocalsBySource = locals.ToDictionary(l => l.Source, l => l.Destination);

            int movedLocals = wasLocalsBySource.Keys.Union(nowLocalsBySource.Keys)
                .Count(k => !wasLocalsBySource.TryGetValue(k, out int x) ||
                            !nowLocalsBySource.TryGetValue(k, out int y) || x != y);

            int expected = plan.Changes.Count(c => c.Text || c.Array);
            if (a.Tag == "__data__" && movedLocals > expected)
            {
                Console.WriteLine($"  append check: FAILED, {movedLocals} pointer(s) moved for " +
                                  $"{expected} change(s) that move one");
                return false;
            }
        }

        Console.WriteLine($"  append check: everything before the added text is untouched, " +
                          $"{plan.Changes.Count(c => c.Text)} pointer(s) repointed");
        return true;
    }

    private static bool GrowingAnArrayOfStringsWorks(string file, string originalXml)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            originalXml, "<hkparam name=\"(?<field>eventNames|variableNames)\" numelements=\"[1-9][0-9]*\">.{3,}?</hkparam>",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!match.Success)
        {
            Console.WriteLine("  resize guard: no array of strings here, skipped");
            return true;
        }

        string field = match.Groups["field"].Value;
        string longer = match.Value.Replace("</hkparam>",
                                            "<hkcstring>an_added_name</hkcstring></hkparam>");
        string edited = ReplaceFirst(originalXml, match.Value, longer);
        var plan = NativeSave.Compare(originalXml, edited);

        if (!plan.Possible)
        {
            Console.WriteLine($"  resize guard: FAILED, growing an array was refused, {plan.Refusal}");
            return false;
        }

        byte[] after;
        try { after = NativeSave.Apply(file, plan); }
        catch (Exception e)
        {
            Console.WriteLine($"  resize guard: FAILED, applying it threw, {e.Message.Split('\n')[0]}");
            return false;
        }

        var objects = new PackfileObjects(PackfileImage.Read(after));
        var holder = objects.Instances.FirstOrDefault(i => i.ClassName == "hkbBehaviorGraphStringData");
        var names = holder == null ? null : objects.ReadStringArray(holder, field);

        int had = System.Text.RegularExpressions.Regex.Matches(match.Value, "<hkcstring").Count;
        if (names == null || names.Count != had + 1)
        {
            Console.WriteLine($"  resize guard: FAILED, {field} came back " +
                              $"{(names == null ? "unreadable" : names.Count + " long")}, expected {had + 1}");
            return false;
        }

        if (names[^1] != "an_added_name")
        {
            Console.WriteLine($"  resize guard: FAILED, the last name is '{names[^1]}'");
            return false;
        }

        Console.WriteLine($"  resize guard: {field} grew from {had} to {names.Count} and reads back");
        return true;
    }

    private static List<(string Was, string Now)> Invent(string xml)
    {
        var edits = new List<(string, string)>();

        void Try(string pattern, string replacement)
        {
            var match = System.Text.RegularExpressions.Regex.Match(xml, pattern);
            if (match.Success && !edits.Any(e => e.Item1 == match.Value))
                edits.Add((match.Value, replacement));
        }

        Try("<hkparam name=\"playbackSpeed\">1\\.0</hkparam>",
            "<hkparam name=\"playbackSpeed\">1.25</hkparam>");
        Try("<hkparam name=\"startTime\">0\\.0</hkparam>",
            "<hkparam name=\"startTime\">0.5</hkparam>");
        Try("<hkparam name=\"userPartitionMask\">0</hkparam>",
            "<hkparam name=\"userPartitionMask\">3</hkparam>");
        Try("<hkparam name=\"ignoreStartTime\">false</hkparam>",
            "<hkparam name=\"ignoreStartTime\">true</hkparam>");

        Try("<hkparam name=\"duration\">[0-9.]+</hkparam>",
            "<hkparam name=\"duration\">3.5</hkparam>");

        Try("<hkparam name=\"animationName\">[^<]{3,}</hkparam>",
            "<hkparam name=\"animationName\">Animations\\Renamed_By_Symrm_Longer.hkx</hkparam>");

        var generators = System.Text.RegularExpressions.Regex
            .Matches(xml, "<hkparam name=\"generator\">#(?<id>[0-9]+)</hkparam>")
            .Select(m => m.Groups["id"].Value).Distinct().ToList();

        if (generators.Count >= 2 && generators[0] != generators[1])
            Try($"<hkparam name=\"generator\">#{generators[0]}</hkparam>",
                $"<hkparam name=\"generator\">#{generators[1]}</hkparam>");

        Try("<hkparam name=\"variableBindingSet\">#[0-9]+</hkparam>",
            "<hkparam name=\"variableBindingSet\">null</hkparam>");

        var array = System.Text.RegularExpressions.Regex.Match(
            xml, "<hkparam name=\"(?<field>states|children|generators|modifiers|layers)\" " +
                 "numelements=\"(?<n>[1-9][0-9]*)\">(?<body>[^<]*)</hkparam>");

        if (array.Success)
        {
            var ids = array.Groups["body"].Value
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (ids.Length > 0 && ids.All(t => t.StartsWith('#')))
            {
                string field = array.Groups["field"].Value;
                string body = array.Groups["body"].Value.TrimEnd();
                edits.Add((array.Value,
                           $"<hkparam name=\"{field}\" numelements=\"{ids.Length + 1}\">" +
                           $"{body}\n{ids[0]}\n</hkparam>"));
            }
        }

        return edits;
    }

    private static string ReplaceFirst(string text, string was, string now)
    {
        int at = text.IndexOf(was, StringComparison.Ordinal);
        return at < 0 ? text : text[..at] + now + text[(at + was.Length)..];
    }

    private static int Signatures(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        int read = 0, refused = 0, checkedNames = 0;
        var problems = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (string file in files)
        {
            PackfileObjects objects;
            try { objects = new PackfileObjects(PackfileImage.Read(file)); }
            catch (Exception) { refused++; continue; }

            read++;
            var names = objects.ClassNames().ToList();
            checkedNames += names.Count;
            foreach (string problem in HavokClassTypes.Shipped.SignatureProblems(names))
                problems[problem] = problems.GetValueOrDefault(problem) + 1;
        }

        Console.WriteLine($"{read} packfile(s) read, {refused} refused, " +
                          $"{checkedNames} class name(s) checked, {problems.Count} kind(s) of problem");
        foreach (var (problem, count) in problems.OrderByDescending(p => p.Value).Take(20))
            Console.WriteLine($"   {problem} ({count} file(s))");

        return problems.Count == 0 && read > 0 ? 0 : 1;
    }

    private static int Nesting(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        long transitions = 0, nestedTo = 0, nestedFrom = 0, wildcards = 0, danglingTarget = 0;
        long nestedFlag = 0, nonzeroNestedWithoutFlag = 0, zeroNestedWithFlag = 0;
        long machines = 0, statesTotal = 0, nestedMachines = 0;
        long nestedResolves = 0, nestedUnresolved = 0, nestedNotAMachine = 0;
        long naiveWildcardLines = 0, fromMachineLines = 0;
        var flagCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var declaredFlags = HavokClassTypes.Shipped
            .Enum("hkbStateMachineTransitionInfo", "TransitionFlags")
            ?.OrderBy(v => v.Value).ToList()
            ?? new List<KeyValuePair<string, long>>();
        var nestedHolds = new Dictionary<string, int>(StringComparer.Ordinal);
        var perMachine = new List<int>();
        int filesRead = 0, filesFailed = 0;

        foreach (string file in files)
        {
            BehaviourGraphModel model;
            try
            {
                model = BehaviourGraphModel.Parse(NativeXml.From(InputFilePolicy.ReadHkx(file)));
            }
            catch
            {
                filesFailed++;
                continue;
            }
            filesRead++;

            var machineIds = model.Objects.Where(o => o.Class == "hkbStateMachine")
                                          .Select(o => o.Id).ToList();
            var nestedInside = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in machineIds)
                foreach (string stateId in model.Get(id)!.Refs("states"))
                {
                    string? generator = model.Get(stateId)?.Ref("generator");
                    if (generator != null && model.Get(generator)?.Class == "hkbStateMachine")
                        nestedInside.Add(generator);
                }

            machines += machineIds.Count;
            nestedMachines += nestedInside.Count;

            foreach (string id in machineIds)
            {
                var states = StateEditor.States(model, id);
                statesTotal += states.Count;
                var known = states.Select(s => s.StateId).ToHashSet();

                var rows = StateEditor.Transitions(model, id);
                perMachine.Add(rows.Count);

                int wildcardsHere = rows.Count(r => r.Wildcard);
                naiveWildcardLines += (long)wildcardsHere * states.Count;
                fromMachineLines += wildcardsHere;

                foreach (var row in rows)
                {
                    transitions++;
                    if (row.Wildcard) wildcards++;
                    if (!known.Contains(row.ToStateId)) danglingTarget++;

                    bool nestedIsValid = row.HasFlag(0x2000);
                    if (nestedIsValid) nestedFlag++;
                    if (row.ToNestedStateId != 0 && !nestedIsValid) nonzeroNestedWithoutFlag++;
                    if (row.ToNestedStateId == 0 && nestedIsValid) zeroNestedWithFlag++;
                    if (row.ToNestedStateId == 0) continue;

                    nestedTo++;

                    string? entered = states.FirstOrDefault(s => s.StateId == row.ToStateId)?.GeneratorRef;
                    var inner = model.Get(entered?.TrimStart('#'));

                    var machine = StateRoutes.MachineUnder(model, inner, 0);
                    if (machine == null)
                    {
                        nestedNotAMachine++;
                        string held = inner?.Class ?? "nothing";
                        nestedHolds[held] = nestedHolds.GetValueOrDefault(held) + 1;
                        continue;
                    }

                    var innerStates = StateEditor.States(model, machine.Id).Select(s => s.StateId).ToHashSet();
                    if (innerStates.Contains(row.ToNestedStateId)) nestedResolves++;
                    else nestedUnresolved++;
                }

                foreach (var array in model.Objects)
                {
                    if (array.Class != "hkbStateMachineTransitionInfoArray") continue;
                    string owner = ElementSummary.MachineOwning(model, array.Id);
                    if (owner != id) continue;
                    if (!array.StructLists.TryGetValue("transitions", out var elements)) continue;

                    bool wild = model.Get(id)?.Ref("wildcardTransitions") == array.Id;

                    foreach (var element in elements)
                    {
                        if (element.TryGetValue("fromNestedStateId", out var from) &&
                            int.TryParse(from, out int value) && value != 0) nestedFrom++;

                        element.TryGetValue("flags", out var flags);
                        long bits = FlagBits(flags ?? "", declaredFlags);
                        string side = wild ? "wildcard  " : "direct    ";

                        if (bits == 0)
                        {
                            flagCounts[side + "(none)"] = flagCounts.GetValueOrDefault(side + "(none)") + 1;
                            continue;
                        }

                        foreach (var (flagName, flagValue) in declaredFlags)
                        {
                            if ((bits & flagValue) != flagValue) continue;
                            string key = side + flagName;
                            flagCounts[key] = flagCounts.GetValueOrDefault(key) + 1;
                        }
                    }
                }
            }
        }

        long drawable = 0, drawableNested = 0, startStates = 0;
        long waysOut = 0, rewriteWrong = 0, notAState = 0, selfDirect = 0;
        foreach (string file in files)
        {
            try
            {
                var model = BehaviourGraphModel.Parse(NativeXml.From(InputFilePolicy.ReadHkx(file)));
                var routes = StateRoutes.Of(model);
                drawable += routes.Routes.Count;
                drawableNested += routes.Routes.Count(r => r.IntoId.Length > 0);
                startStates += routes.StartStates.Count;

                foreach (string stateId in routes.MachineOfState.Keys)
                {
                    var leaving = routes.LeavingState(stateId).ToList();
                    waysOut += leaving.Count;

                    if (leaving.Any(r => r.FromId != stateId)) rewriteWrong++;
                    if (leaving.Any(r => !routes.MachineOfState.ContainsKey(r.ToId))) notAState++;

                    selfDirect += leaving.Count(r => r.ToId == stateId && !r.Wildcard);
                    if (leaving.Any(r => r.ToId == stateId && r.Wildcard)) rewriteWrong++;
                }
            }
            catch
            {
            }
        }

        perMachine.Sort();
        int median = perMachine.Count == 0 ? 0 : perMachine[perMachine.Count / 2];
        int busiest = perMachine.Count == 0 ? 0 : perMachine[^1];
        int p90 = perMachine.Count == 0 ? 0 : perMachine[(int)(perMachine.Count * 0.9)];

        Console.WriteLine($"\n{filesRead} file(s) read, {filesFailed} that would not parse");
        Console.WriteLine($"  {machines,7} state machine(s), {nestedMachines,7} of them sitting in " +
                          $"another machine's state ({Percent(nestedMachines, machines)})");
        Console.WriteLine($"  {statesTotal,7} state(s)");
        Console.WriteLine($"  {transitions,7} transition(s)");
        Console.WriteLine($"  {wildcards,7} of them wildcard, fired from any state ({Percent(wildcards, transitions)})");
        Console.WriteLine($"  {nestedTo,7} with a toNestedStateId, which one arrow cannot say ({Percent(nestedTo, transitions)})");
        Console.WriteLine($"  {nestedFlag,7} carrying FLAG_TO_NESTED_STATE_ID_IS_VALID (0x2000)");
        Console.WriteLine($"  {nonzeroNestedWithoutFlag,7} with a nonzero toNestedStateId without that flag");
        Console.WriteLine($"  {zeroNestedWithFlag,7} with toNestedStateId zero and that flag");
        Console.WriteLine($"          {nestedResolves,7} of those name a real state of the machine inside the state entered");
        Console.WriteLine($"          {nestedUnresolved,7} name a state that machine does not have");
        Console.WriteLine($"          {nestedNotAMachine,7} where the state entered leads to no machine at all");
        foreach (var (held, count) in nestedHolds.OrderByDescending(p => p.Value))
            Console.WriteLine($"                  {count,5}  {held}");
        Console.WriteLine($"  {nestedFrom,7} with a fromNestedStateId ({Percent(nestedFrom, transitions)})");
        Console.WriteLine($"  {danglingTarget,7} whose toStateId is not a state of the machine ({Percent(danglingTarget, transitions)})");
        Console.WriteLine($"  transitions per machine: median {median}, 90th percentile {p90}, busiest {busiest}");
        Console.WriteLine($"\n  flags on transitions, as the file declares them:");
        foreach (var (key, count) in flagCounts.OrderBy(p => p.Key, StringComparer.Ordinal))
            Console.WriteLine($"  {count,7}  {key}");

        Console.WriteLine($"\n  wildcards, drawn from each state they could fire from:");
        Console.WriteLine($"  {naiveWildcardLines,7} line(s), which is the drawing nobody wants");
        Console.WriteLine($"  {fromMachineLines,7} line(s) drawn from the machine instead, " +
                          $"{(naiveWildcardLines == 0 ? "n/a" : $"{(double)naiveWildcardLines / fromMachineLines:0.0} times fewer")}");
        Console.WriteLine($"\n  what the canvas draws from the same files:");
        Console.WriteLine($"  {drawable,7} route(s), {drawableNested,7} of them with a second hop into a nested state");
        Console.WriteLine($"  {startStates,7} start state(s) to badge, one per machine that has its own");
        Console.WriteLine($"  {waysOut,7} way(s) out across every state, being its own transitions " +
                          $"plus its machine's wildcards");
        Console.WriteLine($"  {selfDirect,7} of those are a state's own transition back to itself, " +
                          $"which is real");
        Console.WriteLine($"  {notAState,7} state(s) with a route landing somewhere that is not a state");
        Console.WriteLine($"  {rewriteWrong,7} state(s) where rewriting a wildcard to leave that " +
                          $"state went wrong");

        if (drawable != transitions)
            Console.WriteLine($"  MISMATCH: {transitions - drawable} transition(s) would not be drawn");

        return drawable == transitions && rewriteWrong == 0 && notAState == 0 ? 0 : 1;
    }

    private static HkObject? MachineUnder(BehaviourGraphModel model, HkObject? generator, int depth)
    {
        if (generator == null || depth > 6) return null;
        if (generator.Class == "hkbStateMachine") return generator;

        foreach (string field in new[] { "generator", "pDefaultGenerator", "pBlenderGenerator" })
        {
            var next = model.Get(generator.Ref(field));
            var found = MachineUnder(model, next, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static string Percent(long part, long whole) =>
        whole == 0 ? "n/a" : $"{100.0 * part / whole:0.00}%";

    private static int Elements(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        if (Directory.Exists(target))
        {
            int worst = 0;
            foreach (string each in Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                                             .OrderBy(f => f, StringComparer.Ordinal))
                worst = Math.Max(worst, Elements(new[] { argv[0], each }.Concat(argv.Skip(2)).ToArray()));
            return worst;
        }

        string xml = NativeXml.From(InputFilePolicy.ReadHkx(target));
        var model = BehaviourGraphModel.Parse(xml);

        int arrays = 0, summarised = 0, unnamed = 0;
        foreach (var obj in model.Objects)
        {
            if (obj.Class != "hkbStateMachineTransitionInfoArray") continue;
            arrays++;

            var lines = ElementSummary.For(model, obj.Id);
            if (lines.Count == 0)
            {

                unnamed++;
                Console.WriteLine($"  #{obj.Id}  no state machine points at this array");
                continue;
            }

            summarised += lines.Count;
            string machine = ElementSummary.MachineOwning(model, obj.Id);
            Console.WriteLine($"  #{obj.Id}  on #{machine} {model.Get(machine)?.Str("name")}");

            foreach (var key in lines.Keys.OrderBy(ElementNumber))
                Console.WriteLine($"      {key,-16} {lines[key]}");
        }

        Console.WriteLine($"{Path.GetFileName(target),-34} {arrays,4} transition array(s), " +
                          $"{summarised,5} element(s) summarised, {unnamed,3} array(s) with no owner");
        return 0;
    }

    private static long FlagBits(string text, List<KeyValuePair<string, long>> declared)
    {
        text = text.Trim();
        if (text.Length == 0) return 0;
        if (long.TryParse(text, out long number)) return number;

        long bits = 0;
        foreach (string part in text.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            string name = part.Trim();
            foreach (var (declaredName, value) in declared)
                if (declaredName == name) bits |= value;
        }
        return bits;
    }

    private static int ElementNumber(string group)
    {
        int bracket = group.IndexOf('[');
        return bracket >= 0 && int.TryParse(group[(bracket + 1)..].TrimEnd(']'), out int n) ? n : 0;
    }

    private static int Paths(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        if (Directory.Exists(target))
        {
            int cleanFiles = 0, badFiles = 0;
            foreach (string each in Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                                             .OrderBy(f => f, StringComparer.Ordinal))
            {
                var carried = new[] { argv[0], each }.Concat(argv.Skip(2)).ToArray();
                if (Paths(carried) == 0) cleanFiles++; else badFiles++;
            }

            Console.WriteLine($"\n{cleanFiles} file(s) where every path lands where it should, {badFiles} not");
            return badFiles == 0 ? 0 : 1;
        }

        string file = target;
        var objects = new PackfileObjects(PackfileImage.Read(file));
        string xml = NativeXml.From(InputFilePolicy.ReadHkx(file));
        var ids = HkxTextEdit.ObjectIds(xml);

        if (ids.Count != objects.Instances.Count)
        {
            Console.WriteLine($"{Path.GetFileName(file)}: the text has {ids.Count} objects and the " +
                              $"bytes have {objects.Instances.Count}, so nothing can be lined up");
            return 1;
        }

        int checkedFields = 0, elementFields = 0, wrong = 0, unaddressable = 0, byNameWrong = 0;

        for (int i = 0; i < ids.Count; i++)
        {
            var fields = ClassFields.Of(objects, objects.Instances[i]);
            if (fields == null) continue;

            var before = HkxTextEdit.ReadParams(xml, ids[i]);
            if (before.Count != fields.Count)
            {

                unaddressable += fields.Count;
                continue;
            }

            for (int f = 0; f < fields.Count; f++)
            {

                const string Sentinel = "-987654321";
                string after;
                try
                {
                    after = HkxTextEdit.SetParamAt(xml, ids[i], fields[f].Path, Sentinel);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"  {Path.GetFileName(file)} #{ids[i]} " +
                                      $"{objects.Instances[i].ClassName}.{fields[f].Path}: {e.Message}");
                    wrong++;
                    continue;
                }

                checkedFields++;
                if (fields[f].Group.Length > 0) elementFields++;

                if (before.FindIndex(p => p.Name == fields[f].Name) != f) byNameWrong++;

                var now = HkxTextEdit.ReadParams(after, ids[i]);
                var moved = Enumerable.Range(0, Math.Min(before.Count, now.Count))
                                      .Where(n => before[n].Value != now[n].Value).ToList();

                if (moved.Count == 1 && moved[0] == f) continue;

                wrong++;
                Console.WriteLine($"  {Path.GetFileName(file)} #{ids[i]} " +
                                  $"{objects.Instances[i].ClassName}.{fields[f].Path} is field {f}, " +
                                  (moved.Count == 0
                                       ? "and writing it moved nothing"
                                       : $"but writing it moved {string.Join(", ", moved)}"));
            }
        }

        Console.WriteLine($"{Path.GetFileName(file),-34} {checkedFields,6} fields, " +
                          $"{elementFields,6} inside an element, {byNameWrong,6} of which a name " +
                          $"alone would have missed, {wrong,4} landed wrong" +
                          (unaddressable > 0 ? $", {unaddressable} not lined up to check" : ""));
        return wrong == 0 ? 0 : 1;
    }

    private static int References(string xml, int id) =>
        System.Text.RegularExpressions.Regex.Matches(xml, $@"#{id}\b").Count
        - System.Text.RegularExpressions.Regex.Matches(xml, $@"name=""#{id}""").Count;

    private static int Nulls(string xml) =>
        System.Xml.Linq.XDocument.Parse(xml).Descendants("hkparam")
            .Where(p => p.Attribute("numelements") != null && !p.Elements().Any())
            .Sum(p => (p.Value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                                     .Count(t => t == "null"));

    private static Dictionary<int, string> Numbered(string xml)
    {
        var found = new Dictionary<int, string>();
        foreach (var element in System.Xml.Linq.XDocument.Parse(xml).Descendants("hkobject"))
        {
            string? name = element.Attribute("name")?.Value;
            string? cls = element.Attribute("class")?.Value;
            if (name == null || cls == null || !name.StartsWith('#')) continue;
            if (int.TryParse(name[1..], out int id)) found[id] = cls;
        }
        return found;
    }

    private static int Walk(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToList()
            : new List<string> { target };

        int clean = 0, bad = 0, entries = 0, virtuals = 0;

        foreach (string file in files)
        {
            PackfileImage image;
            try { image = PackfileImage.Read(file); }
            catch (Exception e)
            {
                Console.WriteLine($"  {Path.GetFileName(file)}: skipped, {e.Message.Split('\n')[0]}");
                continue;
            }

            var data = image.Section("__data__");
            if (data == null) continue;

            var objects = new PackfileObjects(image);
            var types = HavokClassTypes.Shipped;
            if (objects.Instances.Any(i => !types.Knows(i.ClassName))) continue;

            bool ok = true;

            var offsets = data.Virtuals().Select(v => v.Source).ToList();
            virtuals += offsets.Count;
            for (int at = 1; at < offsets.Count; at++)
                if (offsets[at] <= offsets[at - 1])
                {
                    ok = false;
                    Console.WriteLine($"{Path.GetFileName(file)}: the virtual table is not in file " +
                                      $"order, entry {at} at 0x{offsets[at]:x} follows " +
                                      $"0x{offsets[at - 1]:x}");
                    break;
                }

            foreach (bool global in new[] { true, false })
            {
                var actual = global ? data.Globals().Select(g => g.Source).ToList()
                                    : data.Locals().Select(l => l.Source).ToList();
                var predicted = FixupOrder.Sources(objects, types, data, global);
                entries += actual.Count;

                if (predicted.SequenceEqual(actual)) continue;

                ok = false;
                int at = predicted.Zip(actual).TakeWhile(p => p.First == p.Second).Count();
                Console.WriteLine($"{Path.GetFileName(file)}: the {(global ? "global" : "local")} " +
                                  $"table is not in that order, {predicted.Count} predicted against " +
                                  $"{actual.Count}, first differing at {at}");
            }

            if (ok) clean++; else bad++;
        }

        Console.WriteLine($"\n{clean} file(s) with both tables in the predicted order, {bad} not, " +
                          $"{entries} entr(ies) checked, {virtuals} object(s) whose virtual entries " +
                          "run in file order");
        return bad == 0 ? 0 : 1;
    }

    private static bool Same(SymbolIndexFixup.Usage a, SymbolIndexFixup.Usage b) =>
        a.Index == b.Index && a.Owner == b.Owner && a.Member == b.Member &&
        a.ObjectId == b.ObjectId && a.OwnerClass == b.OwnerClass;

    private static string Spell(SymbolIndexFixup.Usage u) =>
        $"#{u.ObjectId} {u.OwnerClass} {u.Owner}.{u.Member}={u.Index}";

    private static List<string> Roles(Dictionary<int, List<EventUsage.Line>> byEvent)
    {
        var lines = new List<string>();
        foreach (var (index, sites) in byEvent.OrderBy(e => e.Key))
            foreach (var site in sites)
                lines.Add($"event {index} {site.Role} {site.Site} x{site.Count} " +
                          string.Join(",", site.ObjectIds));
        return lines;
    }

    private static bool MisStrided(string owningClass, string field)
    {
        var types = HavokClassTypes.Shipped;
        foreach (var member in types.Members(owningClass))
            if (member.Name == field)
                return member.CType != null && types.HasTrailingPadding(member.CType);

        return false;
    }

    private static BehaviourGraphModel? SecondReading(string xml, string hkxPath) =>
        NativeGraphModel.From(new PackfileObjects(PackfileImage.Read(hkxPath)));

    private static string Canonical(System.Xml.Linq.XElement p, Func<string, string> reference)
    {
        string raw = p.Value ?? "";
        string text = raw.Trim();

        if (p.Attribute("numelements") == null)
            return raw.StartsWith('#') ? reference(raw) : raw;

        int count = int.Parse(p.Attribute("numelements")!.Value);
        if (p.Elements("hkobject").Any()) return List(count, "structs");

        var strings = p.Elements("hkcstring").ToList();
        if (strings.Count > 0)
            return List(count, strings.Select(s => (s.Value ?? "").Trim()));

        if (text.Contains('('))
        {
            var groups = System.Text.RegularExpressions.Regex.Matches(text, @"\([^)]*\)")
                             .Select(m => m.Value).ToList();

            if (count > 0 && groups.Count > count && groups.Count % count == 0)
            {
                int per = groups.Count / count;
                groups = Enumerable.Range(0, count)
                    .Select(i => "(" + string.Join(" ", groups.Skip(i * per).Take(per)
                                                              .Select(g => g.Trim('(', ')'))) + ")")
                    .ToList();
            }

            return List(count, groups);
        }

        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                         .Select(t => t.StartsWith('#') ? reference(t) : t);
        return List(count, tokens);
    }

    private static string List(int count, IEnumerable<string> tokens) =>
        $"[{count}: {string.Join("|", tokens)}]";

    private static string List(int count, string what) => count == 0 ? "[0: ]" : $"[{count}: {what}]";

    private static string? Rendered(PackfileObjects objects, PackfileObjects.Instance instance,
                                    HavokClasses.Member member,
                                    Dictionary<PackfileObjects.Instance, int> indexOf,
                                    string expected)
    {
        string Reference(PackfileObjects.Instance? target, bool wasNull) =>
            wasNull ? "null"
            : target != null && indexOf.TryGetValue(target, out int at) ? "@" + at
            : "a pointer landing where no object begins";

        var described = HavokClassTypes.Shipped.Members(instance.ClassName)
                                       .FirstOrDefault(m => m.Name == member.Name);
        if (described == null) return null;

        return FieldRender.Render(objects, instance.Offset + described.Offset, instance.ClassName,
                                  described, Reference, expected);
    }

    private static List<float> Numbers(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"-?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?")
            .Select(m => float.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

    private static bool Same(string ours, string theirs)
    {
        if (string.Equals(ours, theirs, StringComparison.Ordinal)) return true;
        if (ours == "∅") return theirs.Length == 0 || theirs == "null";

        if (float.TryParse(ours, out float a) && float.TryParse(theirs, out float b))
            return Math.Abs(a - b) <= 1e-6f * Math.Max(1f, Math.Abs(b));

        int colon = ours.IndexOf(':');
        if (colon > 0 && long.TryParse(ours[..colon], out long number))
            return long.TryParse(theirs, out long theirNumber)
                ? number == theirNumber
                : ours[(colon + 1)..] == theirs;

        if (ours.StartsWith('[') && theirs.StartsWith('['))
        {

            var mine = ours[(ours.IndexOf(':') + 1)..^1]
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var yours = theirs[(theirs.IndexOf(':') + 1)..^1]
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ours[..ours.IndexOf(':')] != theirs[..theirs.IndexOf(':')]) return false;
            return mine.Length == yours.Length && mine.Zip(yours).All(p => Same(p.First, p.Second));
        }

        if (ours.StartsWith('(') && theirs.Contains('.'))
        {
            var mine = Numbers(ours);
            var yours = Numbers(theirs);
            return mine.Count > 0 && mine.Count == yours.Count &&
                   mine.Zip(yours).All(p => Math.Abs(p.First - p.Second) <=
                                            1e-6f * Math.Max(1f, Math.Abs(p.Second)));
        }

        if (ours is "true" or "false")
            return theirs.Equals(ours, StringComparison.OrdinalIgnoreCase) ||
                   theirs == (ours == "true" ? "1" : "0");

        return false;
    }

    private static string BoundAt(string xml, int index)
    {

        var array = System.Xml.Linq.XDocument.Parse(xml).Descendants("hkparam")
            .FirstOrDefault(p => p.Attribute("name")?.Value == "variableBounds");
        if (array == null) return "absent";

        var elements = array.Elements("hkobject").ToList();
        if (index >= elements.Count) return $"only {elements.Count} bound(s)";

        string Side(string which) =>
            elements[index].Elements("hkparam").FirstOrDefault(p => p.Attribute("name")?.Value == which)
                ?.Elements("hkobject").FirstOrDefault()
                ?.Elements("hkparam").FirstOrDefault(p => p.Attribute("name")?.Value == "value")
                ?.Value.Trim() ?? "absent";

        return $"{Side("min")} to {Side("max")}";
    }

    private static int ClipTrim(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        int read = 0, noAnimation = 0, unreadable = 0;
        int withAnnotations = 0, withMotion = 0, withFloatTracks = 0;
        int motionMatchesFrames = 0, motionDiffers = 0, motionUnreadable = 0, motionDurationDiffers = 0;
        var motionSampleCounts = new SortedDictionary<int, int>();
        long annotationTracks = 0, annotations = 0;
        int annotationsPastEnd = 0, annotationsAtZero = 0;
        var byClass = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var frameCounts = new List<int>();
        var durations = new List<float>();
        var examples = new List<string>();
        var motionExamples = new List<string>();

        foreach (string file in files)
        {
            PackfileObjects objects;
            try { objects = new PackfileObjects(PackfileImage.Read(InputFilePolicy.ReadHkx(file)), HavokClasses.Shipped); }
            catch (Exception) { unreadable++; continue; }

            var animation = objects.Instances.FirstOrDefault(
                i => i.ClassName.StartsWith("hka", StringComparison.Ordinal) &&
                     i.ClassName.EndsWith("Animation", StringComparison.Ordinal));
            if (animation == null) { noAnimation++; continue; }

            read++;
            byClass[animation.ClassName] = byClass.GetValueOrDefault(animation.ClassName) + 1;

            float duration = objects.ReadFloat(animation, "duration") ?? 0;
            durations.Add(duration);

            int floats = objects.ReadInt(animation, "numberOfFloatTracks") ?? 0;
            if (floats > 0) withFloatTracks++;

            int frames = objects.ReadInt(animation, "numFrames") ?? 0;
            if (frames > 0) frameCounts.Add(frames);

            var motion = objects.ReadRef(animation, "extractedMotion", out bool motionNull);
            if (!motionNull && motion != null)
            {
                withMotion++;

                var samples = objects.ReadArray(motion, "referenceFrameSamples");
                float motionDuration = objects.ReadFloat(motion, "duration") ?? 0;

                if (samples == null) motionUnreadable++;
                else if (samples.Count == frames) motionMatchesFrames++;
                else
                {
                    motionDiffers++;
                    motionSampleCounts.TryGetValue(samples.Count, out int had);
                    motionSampleCounts[samples.Count] = had + 1;
                    if (motionExamples.Count < 8)
                        motionExamples.Add($"{Short(file, target)}: {samples.Count} motion sample(s) " +
                                           $"against {frames} frame(s)");
                }

                if (samples != null && Math.Abs(motionDuration - duration) > 0.001f)
                {
                    motionDurationDiffers++;
                    if (motionExamples.Count < 8)
                        motionExamples.Add($"{Short(file, target)}: motion says {motionDuration:F3}s, " +
                                           $"clip says {duration:F3}s");
                }
            }

            var tracks = objects.ReadArray(animation, "annotationTracks");
            if (tracks == null || tracks.Count == 0) continue;

            annotationTracks += tracks.Count;
            int here = 0;
            int trackStride = HavokClassTypes.Shipped["hkaAnnotationTrack"]?.Size ?? 24;
            int noteStride = HavokClassTypes.Shipped["hkaAnnotationTrackAnnotation"]?.Size ?? 16;

            for (int t = 0; t < tracks.Count; t++)
            {
                var notes = objects.ArrayAt(tracks.At + t * trackStride + 8);
                if (notes == null || notes.Count == 0) continue;

                for (int n = 0; n < notes.Count; n++)
                {
                    float when = objects.ReadFloatAt(notes.At + n * noteStride) ?? 0;
                    here++;
                    if (Math.Abs(when) < 0.0001f) annotationsAtZero++;
                    if (when > duration + 0.0001f) annotationsPastEnd++;
                }
            }

            if (here == 0) continue;
            annotations += here;
            withAnnotations++;
            if (examples.Count < 8)
                examples.Add($"{Short(file, target)}: {here} annotation(s) across {tracks.Count} track(s), " +
                             $"clip is {duration:F3}s");
        }

        frameCounts.Sort();
        durations.Sort();

        Console.WriteLine($"\n{files.Length} file(s) looked at: {read} hold an animation, " +
                          $"{noAnimation} hold none, {unreadable} could not be read");
        Console.WriteLine("by class: " + string.Join(", ", byClass.Select(c => $"{c.Key} x{c.Value}")));
        Console.WriteLine($"carrying annotations: {withAnnotations} clip(s), {annotations} annotation(s) " +
                          $"across {annotationTracks} track(s)");
        Console.WriteLine($"  of those annotations, {annotationsAtZero} sit at time zero and " +
                          $"{annotationsPastEnd} sit past the clip's own duration");
        Console.WriteLine($"carrying extracted motion, which a cut would desync: {withMotion} clip(s)");
        Console.WriteLine($"  of those, {motionMatchesFrames} sample the root once per animation frame, " +
                          $"{motionDiffers} do not, {motionUnreadable} could not be read");
        Console.WriteLine($"  and {motionDurationDiffers} give the motion a duration different from the clip's");
        if (motionSampleCounts.Count > 0)
            Console.WriteLine("  the ones that do not match, by sample count: " +
                              string.Join(", ", motionSampleCounts.Select(m => $"{m.Key} sample(s) x{m.Value}")));
        Console.WriteLine($"driving float tracks, which the writer already refuses: {withFloatTracks} clip(s)");

        if (frameCounts.Count > 0)
            Console.WriteLine($"frames, over the {frameCounts.Count} clip(s) that name a count: " +
                              $"least {frameCounts[0]}, median {frameCounts[frameCounts.Count / 2]}, " +
                              $"most {frameCounts[^1]}");
        if (durations.Count > 0)
            Console.WriteLine($"duration: shortest {durations[0]:F3}s, median " +
                              $"{durations[durations.Count / 2]:F3}s, longest {durations[^1]:F3}s");

        foreach (string line in examples) Console.WriteLine($"  {line}");
        foreach (string line in motionExamples) Console.WriteLine($"  {line}");

        return 0;
    }

    private static int ClipTime(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)

                       .Where(f => f.Contains("behavior", StringComparison.OrdinalIgnoreCase))
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        int clips = 0, named = 0, withTriggers = 0, enforced = 0;
        int triggers = 0, toEnd = 0, absolute = 0, annotations = 0;
        int resolved = 0, durationRead = 0, perCharacter = 0;
        int variantsAgree = 0, variantsDisagree = 0, singleCandidate = 0, rootless = 0;
        int cropped = 0, offSpeed = 0, parked = 0, endAwayFromEnd = 0;
        int stepped = 0, movedOnAClip = 0, timedTransitions = 0, outOfRange = 0, impossible = 0;
        int atEnd = 0, endMisplaced = 0, exactlyAtEnd = 0, untimedTriggers = 0;
        var stepComplaints = new List<string>();
        var modes = new SortedDictionary<int, int>();
        int listened = 0, listenedToEnd = 0;
        var unresolvedExamples = new List<string>();
        var missesBy = new Dictionary<string, int>(StringComparer.Ordinal);
        var perCharacterExamples = new List<string>();
        var listenedExamples = new List<string>();

        var rootOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lengths = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var reader = new HkxBinaryReader();

        foreach (string file in files)
        {
            PackfileObjects objects;
            BehaviourGraphModel? model;
            try
            {
                objects = new PackfileObjects(PackfileImage.Read(InputFilePolicy.ReadHkx(file)));
                model = NativeGraphModel.From(objects);
            }
            catch (Exception) { continue; }
            if (model == null) continue;

            if (!rootOf.TryGetValue(file, out string? root))
            {
                try { root = ProjectChain.Resolve(file).Root; }
                catch (Exception) { root = ""; }
                rootOf[file] = root ?? "";
            }

            var events = SymbolEditor.EventNames(model);
            var heard = StateRoutes.Of(model).Routes.Select(r => r.Event)
                                   .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var clip in objects.OfClass("hkbClipGenerator"))
            {
                clips++;
                string animation = objects.ReadString(clip, "animationName") ?? "";
                if (animation.Length > 0) named++;
                if ((objects.ReadFloat(clip, "enforcedDuration") ?? 0) > 0) enforced++;
                if ((objects.ReadFloat(clip, "cropStartAmountLocalTime") ?? 0) != 0 ||
                    (objects.ReadFloat(clip, "cropEndAmountLocalTime") ?? 0) != 0) cropped++;
                float speed = objects.ReadFloat(clip, "playbackSpeed") ?? 1;
                if (speed != 1) offSpeed++;
                if (speed == 0) parked++;
                int modeValue = (objects.ReadIntAt(clip.Offset + 190) ?? 0) & 0xff;
                modes.TryGetValue(modeValue, out int seenMode);
                modes[modeValue] = seenMode + 1;

                ResolveLength(file, root ?? "", animation);

                var array = objects.ReadRef(clip, "triggers", out _);
                if (array == null) continue;
                var elements = objects.ReadArray(array, "triggers");
                if (elements == null || elements.Count == 0) continue;
                withTriggers++;

                int stride = HavokClassTypes.Shipped["hkbClipTrigger"]?.Size ?? 32;
                for (int i = 0; i < elements.Count; i++)
                {
                    int at = elements.At + i * stride;
                    triggers++;

                    bool end = (objects.ReadIntAt(at + 24) & 0xff) != 0;
                    bool annotation = (objects.ReadIntAt(at + 26) & 0xff) != 0;
                    if (annotation) annotations++;
                    if (end) toEnd++; else absolute++;
                    if (end && Math.Abs(objects.ReadFloatAt(at) ?? 0) > 0.0001f) endAwayFromEnd++;

                    int id = objects.ReadIntAt(at + 8) ?? -1;
                    string name = id >= 0 && id < events.Count ? events[id] : "";
                    if (name.Length == 0 || !heard.Contains(name)) continue;

                    listened++;
                    if (end) listenedToEnd++;
                    if (listenedExamples.Count < 12)
                        listenedExamples.Add($"{Path.GetFileName(file)}: '{objects.ReadString(clip, "name")}' " +
                                             $"raises '{name}' {(end ? "at the end of its clip" : "part way through")}, " +
                                             "and a transition listens for it");
                }

            }

            var run = GraphRun.Start(model);
            if (run.RootId.Length == 0) continue;

            var timed = ClipTiming.All(objects, events, name =>
            {
                if (string.IsNullOrEmpty(root)) return 0;
                string path = ProjectChain.ResolvePath(root, name);
                return File.Exists(path) ? Length(reader, lengths, path) : 0;
            });

            foreach (var clip in timed.Values)
            {

                if (!clip.Known) untimedTriggers += clip.Triggers.Count;

                foreach (var trigger in clip.Triggers)
                {
                    if (trigger.At < 0 || trigger.At > clip.Seconds)
                    {
                        outOfRange++;
                        if (stepComplaints.Count < 12)
                            stepComplaints.Add($"{Path.GetFileName(file)}: '{clip.Name}' raises " +
                                               $"'{trigger.Event}' at {trigger.At:F3}s of a {clip.Seconds:F3}s clip");
                        continue;
                    }

                    if (!trigger.RelativeToEnd) continue;
                    atEnd++;
                    if (Math.Abs(trigger.LocalTime) > 0.0001f) continue;

                    exactlyAtEnd++;
                    if (Math.Abs(trigger.At - clip.Seconds) > 0.0001f)
                    {
                        endMisplaced++;
                        if (stepComplaints.Count < 12)
                            stepComplaints.Add($"{Path.GetFileName(file)}: '{clip.Name}' ends at " +
                                               $"{trigger.At:F3}s of a {clip.Seconds:F3}s clip");
                    }
                }
            }

            run.Time(timed);
            stepped++;

            var allowed = run.Reachable();
            int firedHere = 0;

            for (int step = 0; step < 100; step++)
                firedHere += run.Advance(0.1f).Count;

            foreach (string name in run.Events)
            {
                run.Send(name);
                for (int step = 0; step < 100; step++) firedHere += run.Advance(0.1f).Count;
            }

            if (firedHere > 0) { movedOnAClip++; timedTransitions += firedHere; }

            foreach (var active in run.Where())
                if (allowed.Unreachable.Contains(active.StateId))
                {
                    impossible++;
                    if (stepComplaints.Count < 12)
                        stepComplaints.Add($"{Path.GetFileName(file)}: the clock reached #{active.StateId} " +
                                           $"'{active.StateName}', which the analysis calls unreachable");
                    break;
                }
        }

        void ResolveLength(string file, string root, string animation)
        {
            if (animation.Length == 0) return;

            if (string.IsNullOrEmpty(root)) { rootless++; return; }

            string path = ProjectChain.ResolvePath(root, animation);
            if (File.Exists(path))
            {
                resolved++;
                if (Length(reader, lengths, path) > 0) durationRead++;
                return;
            }

            var candidates = Variants(root, animation);
            if (candidates.Count == 0)
            {
                missesBy.TryGetValue(file, out int had);
                missesBy[file] = had + 1;
                if (unresolvedExamples.Count < 8)
                    unresolvedExamples.Add($"{Path.GetFileName(file)}: {animation}");
                return;
            }

            perCharacter++;
            var spans = candidates.Select(c => Length(reader, lengths, c)).Where(s => s > 0).ToList();
            if (spans.Count > 1 && spans.Max() - spans.Min() > 0.001f)
            {
                variantsDisagree++;
                if (perCharacterExamples.Count < 8)
                    perCharacterExamples.Add($"{Path.GetFileName(file)}: {animation} has " +
                        $"{candidates.Count} copies lasting {spans.Min():F3}s to {spans.Max():F3}s");
            }
            else if (candidates.Count > 1) variantsAgree++;
            else singleCandidate++;
        }

        Console.WriteLine($"\n{files.Length} behaviour file(s)");
        Console.WriteLine($"clips: {clips}, {named} name an animation, {withTriggers} carry a trigger array");
        Console.WriteLine($"triggers: {triggers}, {toEnd} relative to the end of the clip, {absolute} at an " +
                          $"absolute time, {annotations} marked as annotations");
        Console.WriteLine($"length already in the behaviour: {enforced} clip(s) set enforcedDuration");
        Console.WriteLine($"what the shipped data never varies, which is where a corpus gate is blind: " +
                          $"{cropped} clip(s) crop, {offSpeed} play at a speed other than 1, {parked} at zero " +
                          $"speed, {endAwayFromEnd} end trigger(s) sit away from the end, " +
                          $"{annotations} trigger(s) are annotations");
        Console.WriteLine($"playback modes: {string.Join(", ", modes.Select(m => $"{ModeName(m.Key)} x{m.Value}"))}");
        Console.WriteLine($"animation found on disk: {resolved} of {named}, length read for {durationRead}");
        Console.WriteLine($"named a path that is not a file but has copies below it: {perCharacter}, " +
                          $"of which {singleCandidate} have one copy, {variantsAgree} have several of the " +
                          $"same length, and {variantsDisagree} have several of different lengths");
        Console.WriteLine($"in a behaviour with no project of its own, so no root to resolve against: {rootless}");
        Console.WriteLine($"nothing on disk to read at all: {named - resolved - perCharacter - rootless}");
        Console.WriteLine($"raised events a transition in the same file listens for: {listened}, " +
                          $"{listenedToEnd} of them at the end of a clip");
        Console.WriteLine($"\nstepped: {stepped} file(s) run on the clock, {movedOnAClip} had a state " +
                          $"leave because a clip ended, {timedTransitions} transition(s) fired that way");
        Console.WriteLine($"triggers resolving outside their own clip: {outOfRange}");
        Console.WriteLine($"triggers offered by a clip with no length, which must be none: {untimedTriggers}");
        Console.WriteLine($"end relative triggers: {atEnd}, of which {exactlyAtEnd} carry no offset and " +
                          $"so must land on the clip's own length; {endMisplaced} do not");
        Console.WriteLine($"steps landing somewhere the reachability analysis calls impossible: {impossible}");
        foreach (string line in stepComplaints) Console.WriteLine($"  {line}");
        foreach (string line in listenedExamples) Console.WriteLine($"  {line}");
        if (perCharacterExamples.Count > 0)
        {
            Console.WriteLine("  copies of different lengths, first few:");
            foreach (string line in perCharacterExamples) Console.WriteLine($"    {line}");
        }
        if (missesBy.Count > 0)
        {
            Console.WriteLine($"  those misses come from {missesBy.Count} behaviour file(s), worst first:");
            foreach (var pair in missesBy.OrderByDescending(p => p.Value).Take(12))
                Console.WriteLine($"    {pair.Value,5}  {Path.GetFileName(pair.Key)}");
        }
        if (unresolvedExamples.Count > 0)
        {
            Console.WriteLine("  animations not found anywhere, first few:");
            foreach (string line in unresolvedExamples) Console.WriteLine($"    {line}");
        }

        return outOfRange + impossible + endMisplaced + untimedTriggers == 0 ? 0 : 1;
    }

    private static string ModeName(int mode) =>
        HavokClassTypes.Shipped.Enum("hkbClipGenerator", "PlaybackMode")
                       ?.FirstOrDefault(v => v.Value == mode).Key ?? $"mode {mode}";

    private static List<string> Variants(string root, string animation)
    {
        string cleaned = animation.Replace('\\', Path.DirectorySeparatorChar)
                                  .Replace('/', Path.DirectorySeparatorChar);
        string leaf = Path.GetFileNameWithoutExtension(cleaned);
        string under = Path.GetDirectoryName(Path.Combine(root, cleaned)) ?? root;
        if (leaf.Length == 0 || !Directory.Exists(under)) return new List<string>();

        try
        {
            return Directory.GetFiles(under, leaf + ".hkx", SearchOption.AllDirectories)
                            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        }
        catch (Exception) { return new List<string>(); }
    }

    private static float Length(HkxBinaryReader reader, Dictionary<string, float> cache, string path)
    {
        if (cache.TryGetValue(path, out float seconds)) return seconds;
        seconds = reader.TryReadAnimation(path, out var data) ? data.Duration : 0;
        cache[path] = seconds;
        return seconds;
    }

    private static int Weights(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        if (!Directory.Exists(target)) return WeightsOne(target);

        var files = Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories).OrderBy(f => f).ToArray();

        int blenders = 0, mix = 0, parametric = 0, driven = 0;
        int drivenChildren = 0;
        int badShares = 0;
        int timed = 0, instant = 0, badBlend = 0;
        var complaints = new List<string>();

        foreach (string file in files)
        {
            BehaviourGraphModel? model;
            try { model = NativeGraphModel.From(new PackfileObjects(PackfileImage.Read(InputFilePolicy.ReadHkx(file)))); }
            catch (Exception) { continue; }
            if (model == null) continue;

            foreach (var blend in BlendWeights.All(model))
            {
                blenders++;
                switch (blend.Mode)
                {
                    case BlendWeights.Mode.Mix: mix++; break;
                    case BlendWeights.Mode.Parametric: parametric++; break;
                    default: driven++; break;
                }
                drivenChildren += blend.Children.Count(c => c.WeightDriven);

                if (blend.Mode == BlendWeights.Mode.Mix)
                {
                    float sum = blend.Children.Where(c => !c.WeightDriven).Sum(c => c.Contribution);
                    bool ok = Math.Abs(sum - 1) < 1e-3f || sum < 1e-6f;
                    if (!ok)
                    {
                        badShares++;
                        if (complaints.Count < 20)
                            complaints.Add($"{Path.GetFileName(file)}: blender #{blend.BlenderId} shares sum to {sum:F3}");
                    }
                }
            }

            var run = GraphRun.Start(model);
            foreach (var route in StateRoutes.Of(model).Routes)
            {
                float d = TransitionSeconds(model, route);
                if (d <= 0) { instant++; continue; }
                timed++;
            }
        }

        if (!BlendRamps(out string why))
        {
            badBlend++;
            complaints.Add("the transition blend ramp is wrong: " + why);
        }

        Console.WriteLine($"\n{files.Length} file(s)");
        Console.WriteLine($"{blenders} blender(s): {mix} mix all children, {parametric} parametric on a " +
                          $"value in the file, {driven} parametric driven by a variable");
        Console.WriteLine($"{drivenChildren} child weight(s) driven by a variable rather than fixed");
        Console.WriteLine($"resolved mixes that sum wrong: {badShares}");
        Console.WriteLine($"transitions: {timed} blend over time, {instant} are instant");
        Console.WriteLine($"blend ramp: {(badBlend == 0 ? "starts at nothing, reaches all, stays in range" : "WRONG")}");
        foreach (string line in complaints) Console.WriteLine($"  {line}");

        return badShares + badBlend == 0 ? 0 : 1;
    }

    private static float TransitionSeconds(BehaviourGraphModel model, StateRoutes.Route route)
    {

        var machine = model.Get(route.MachineId);
        string arrayId = route.Wildcard
            ? machine?.Ref("wildcardTransitions") ?? ""
            : model.Get(route.FromId)?.Ref("transitions") ?? "";
        var array = model.Get(arrayId);
        if (array == null || !array.StructLists.TryGetValue("transitions", out var rows)) return 0;

        foreach (var row in rows)
        {
            if (!row.TryGetValue("eventId", out var ev) || ev != route.EventId.ToString()) continue;
            if (!row.TryGetValue("transition", out var effectRef) || effectRef is null or "null") continue;
            var effect = model.Get(effectRef.TrimStart('#'));
            if (effect?.Class != "hkbBlendingTransitionEffect") continue;
            if (float.TryParse(effect.Str("duration"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float d))
                return d;
        }
        return 0;
    }

    private static bool BlendRamps(out string why)
    {
        why = "";
        var model = BehaviourGraphModel.Parse(Tests.TwoStateBlendGraph());
        var run = GraphRun.Start(model);

        string startState = run.Where().First().StateId;
        run.Send("Go");

        var atStart = run.Where();
        var incoming = atStart.FirstOrDefault(a => !a.Fading);
        var outgoing = atStart.FirstOrDefault(a => a.Fading);
        if (incoming == null || outgoing == null) { why = "a transition with a duration did not blend two states"; return false; }
        if (incoming.Weight > 0.01f) { why = $"the new state started at {incoming.Weight:F2} rather than nothing"; return false; }

        run.Advance(0.25f);
        var half = run.Where();
        float mid = half.First(a => !a.Fading).Weight;
        if (mid < 0.4f || mid > 0.6f) { why = $"halfway the new state was {mid:F2} rather than about half"; return false; }

        run.Advance(0.5f);
        var done = run.Where();
        if (done.Count != 1) { why = "the blend did not finish after its duration"; return false; }
        if (done[0].Weight < 0.999f) { why = $"the settled state held {done[0].Weight:F2} rather than all of it"; return false; }

        return true;
    }

    private static int WeightsOne(string file)
    {
        BehaviourGraphModel? model;
        try { model = NativeGraphModel.From(new PackfileObjects(PackfileImage.Read(InputFilePolicy.ReadHkx(file)))); }
        catch (Exception e) { Console.WriteLine($"could not read {file}: {e.Message}"); return 1; }
        if (model == null) { Console.WriteLine("nothing to read"); return 1; }

        var blends = BlendWeights.All(model).ToList();
        Console.WriteLine($"{Path.GetFileName(file)}: {blends.Count} blender(s)");
        foreach (var blend in blends)
        {
            Console.WriteLine($"  {blend}");
            foreach (var child in blend.Children)
                Console.WriteLine($"      {child.GeneratorClass} {child}");
        }
        return 0;
    }

    private static int Run(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        if (!Directory.Exists(target)) return RunOne(target, argv.Length > 2 ? argv[2..] : Array.Empty<string>());

        var files = Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories).OrderBy(f => f).ToArray();

        int read = 0, unreadable = 0, noRoot = 0, narrower = 0;
        long machines = 0, running = 0, states = 0, reachable = 0, unreachable = 0;
        long dead = 0, conditional = 0, stops = 0, noGraphObject = 0;
        long neverEntered = 0, widerBy = 0, steppedInto = 0, blockedByCondition = 0, conditionsWeighed = 0;
        int walkedOff = 0;
        var stopKinds = new SortedDictionary<string, int>();
        var complaints = new List<string>();

        foreach (string file in files)
        {
            BehaviourGraphModel? model;
            try { model = NativeGraphModel.From(new PackfileObjects(PackfileImage.Read(InputFilePolicy.ReadHkx(file)))); }
            catch (Exception) { unreadable++; continue; }
            if (model == null) { unreadable++; continue; }

            read++;
            var run = GraphRun.Start(model);
            if (run.RootId.Length == 0) { noRoot++; continue; }

            var here = run.Where();
            var reach = run.Reachable();

            machines += model.Objects.Count(o => o.Class == "hkbStateMachine");
            running += here.Count;
            states += reach.Reachable.Count + reach.Unreachable.Count;
            reachable += reach.Reachable.Count;
            unreachable += reach.Unreachable.Count;
            dead += reach.Dead.Count;
            conditional += reach.Conditional;
            stops += run.Stops.Count;

            foreach (var stop in run.Stops)
            {
                stopKinds[stop.ClassName] = stopKinds.GetValueOrDefault(stop.ClassName) + 1;
                if (stop.Why.StartsWith("this file has no hkbBehaviorGraph", StringComparison.Ordinal))
                    noGraphObject++;
            }

            var validatorSays = ValidatorReaches(model);
            var entered = reach.Reachable
                .Select(id => model.Get(id))
                .Where(o => o != null)
                .Select(o => MachineOf(model, o!.Id))
                .Where(m => m.Length > 0)
                .ToHashSet(StringComparer.Ordinal);

            var stepped = StepEverywhere(model, out int heldBack, out int weighed);
            blockedByCondition += heldBack;
            conditionsWeighed += weighed;
            var impossible = stepped.Except(reach.Reachable).ToList();
            steppedInto += stepped.Count;
            if (impossible.Count > 0)
            {
                walkedOff++;
                if (complaints.Count < 20)
                    complaints.Add($"{Path.GetFileName(file)}: stepping entered {impossible.Count} state(s) " +
                                   $"the analysis calls unreachable, first #{impossible[0]}");
            }

            var comparable = validatorSays.Where(id => entered.Contains(MachineOf(model, id))).ToList();
            var missed = comparable.Except(reach.Reachable).ToList();

            neverEntered += validatorSays.Count - comparable.Count;
            widerBy += reach.Reachable.Except(validatorSays).Count();

            if (missed.Count > 0)
            {
                narrower++;
                if (complaints.Count < 20)
                    complaints.Add($"{Path.GetFileName(file)}: inside machines the run entered, it missed " +
                                   $"{missed.Count} state(s) the validator reaches, first #{missed[0]}");
            }
        }

        Console.WriteLine($"\n{files.Length} file(s): {read} read, {unreadable} could not be read, " +
                          $"{noRoot} that are not behaviour graphs at all");
        Console.WriteLine($"the {noRoot} are project and character files, which carry hkbProjectData " +
                          $"rather than a graph, so there is nothing in them to run");
        Console.WriteLine($"{machines} state machine(s), of which {running} are running at the start");
        Console.WriteLine($"{states} state(s): {reachable} reachable by some event, {unreachable} not");
        Console.WriteLine($"{dead} transition(s) leave a state nothing can reach");
        Console.WriteLine($"{conditional} transition(s) carry a condition, which is now read rather " +
                          $"than assumed to pass: the sweep weighed {conditionsWeighed} of them and held " +
                          $"back {blockedByCondition} because the starting values say they cannot fire");
        Console.WriteLine($"{stops} stop(s) where the run refuses to guess:");
        foreach (var kind in stopKinds) Console.WriteLine($"    {kind.Key,-40} {kind.Value}");
        if (noGraphObject > 0) Console.WriteLine($"    of those, {noGraphObject} are files with no hkbBehaviorGraph");

        Console.WriteLine($"\nagainst the validator's own reachability, inside machines the run entered: " +
                          $"{read - narrower} of {read} file(s) reach at least as much, {narrower} reach less");
        Console.WriteLine($"{neverEntered} state(s) the validator reaches sit in machines nothing enters, " +
                          $"which is what a whole graph run can say and a per machine rule cannot");
        Console.WriteLine($"{widerBy} state(s) the run reaches that the validator does not, by crossing " +
                          $"machine boundaries and by following a reached state into what it holds");
        Console.WriteLine($"\nstepping against the analysis: {steppedInto} state(s) entered by actually " +
                          $"sending events, {walkedOff} file(s) stepped somewhere the analysis calls unreachable");
        foreach (string line in complaints) Console.WriteLine($"  {line}");

        return narrower + walkedOff == 0 ? 0 : 1;
    }

    private static HashSet<string> StepEverywhere(BehaviourGraphModel model, out int heldBack,
                                                  out int weighed)
    {
        var landed = new HashSet<string>(StringComparer.Ordinal);
        heldBack = 0;
        weighed = 0;

        var run = GraphRun.Start(model);
        if (run.RootId.Length == 0) return landed;

        foreach (var active in run.Where()) landed.Add(active.StateId);

        for (int sweep = 0; sweep < 8; sweep++)
        {
            int before = landed.Count;
            foreach (string name in run.Events)
            {
                foreach (var fired in run.Send(name)) landed.Add(fired.ToStateId);
                heldBack += run.HeldBack.Count;
                weighed = run.ConditionsWeighed;
                foreach (var active in run.Where()) landed.Add(active.StateId);
            }
            if (landed.Count == before) break;
        }

        return landed;
    }

    private static readonly Dictionary<BehaviourGraphModel, Dictionary<string, string>> _machineOf = new();

    private static string MachineOf(BehaviourGraphModel model, string stateId)
    {
        if (!_machineOf.TryGetValue(model, out var map))
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var machine in model.Objects.Where(o => o.Class == "hkbStateMachine"))
                foreach (string id in machine.Refs("states"))
                    map.TryAdd(id, machine.Id);
            _machineOf[model] = map;
        }
        return map.TryGetValue(stateId, out var found) ? found : "";
    }

    private static HashSet<string> ValidatorReaches(BehaviourGraphModel model)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);

        foreach (var machine in model.Objects.Where(o => o.Class == "hkbStateMachine"))
        {
            var states = StateEditor.States(model, machine.Id);
            int start = machine.Int("startStateId");
            if (states.Count == 0 || !states.Any(s => s.StateId == start)) continue;

            var transitions = StateEditor.Transitions(model, machine.Id);
            if (transitions.Count == 0) continue;

            var live = new HashSet<int> { start };
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (var t in transitions)
                {
                    if (t.ToStateId < 0 || live.Contains(t.ToStateId)) continue;
                    if (!t.Wildcard && !live.Contains(t.FromStateId)) continue;
                    live.Add(t.ToStateId);
                    grew = true;
                }
            }

            foreach (var s in states.Where(s => live.Contains(s.StateId))) reached.Add(s.Id);
        }

        return reached;
    }

    private static int RunOne(string file, string[] events)
    {
        PackfileObjects objects;
        BehaviourGraphModel? model;
        try
        {
            objects = new PackfileObjects(PackfileImage.Read(InputFilePolicy.ReadHkx(file)));
            model = NativeGraphModel.From(objects);
        }
        catch (Exception e) { Console.WriteLine($"could not read {file}: {e.Message}"); return 1; }
        if (model == null) { Console.WriteLine("nothing to run in this file"); return 1; }

        var run = GraphRun.Start(model);
        if (run.RootId.Length == 0) { Console.WriteLine("this file has no generator to start from"); return 1; }

        var timed = ClipTiming.All(objects, SymbolEditor.EventNames(model), ClipTiming.FromDisk(file));
        run.Time(timed);

        Console.WriteLine($"{Path.GetFileName(file)}: started at #{run.RootId}");
        foreach (var active in run.Where()) Console.WriteLine($"  {active}");
        foreach (var stop in run.Stops) Console.WriteLine($"  stop: {stop}");

        int known = timed.Values.Count(c => c.Known);
        Console.WriteLine($"  clips: {timed.Count}, {known} with a length, " +
                          $"{timed.Values.Sum(c => c.Triggers.Count)} trigger(s) timed against it");
        foreach (var (clip, at) in run.Playing().Take(8))
            Console.WriteLine($"    playing '{clip.Name}' at {at:F2}s of " +
                              (clip.Known ? $"{clip.Seconds:F2}s" : "an unknown length"));

        foreach (string name in events)
        {

            if (float.TryParse(name, out float seconds) && seconds > 0)
            {
                var byTime = run.Advance(seconds);
                Console.WriteLine($"\nwait {seconds}s: " +
                                  (byTime.Count == 0 ? "nothing moved on its own"
                                                     : $"{byTime.Count} transition(s) fired by a clip"));
                foreach (var f in byTime) Console.WriteLine($"  {f}");
                foreach (var active in run.Where()) Console.WriteLine($"    now {active}");
                continue;
            }

            if (!run.Declares(name))
            {
                Console.WriteLine($"\nsend {name}: this graph declares no event of that name. " +
                                  $"It has {run.Events.Count}: {string.Join(", ", run.Events)}");
                continue;
            }

            var fired = run.Send(name);
            Console.WriteLine($"\nsend {name}: {(fired.Count == 0 ? "nothing moved" : $"{fired.Count} transition(s)")}");
            foreach (var f in fired) Console.WriteLine($"  {f}");
            foreach (var active in run.Where()) Console.WriteLine($"    now {active}");
        }

        if (events.Length == 0)
        {
            var reach = run.Reachable();
            Console.WriteLine($"\n{reach.Reachable.Count} state(s) reachable, {reach.Unreachable.Count} not, " +
                              $"{reach.Dead.Count} transition(s) from a state nothing reaches");
            foreach (string id in reach.Unreachable.Take(10))
                Console.WriteLine($"  unreachable: #{id} '{model.Get(id)?.Str("name")}'");
        }

        return 0;
    }

    private static int EditFrame(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories).OrderBy(f => f).ToArray()
            : new[] { target };

        int everyNth = argv.Length > 2 && int.TryParse(argv[2], out int n) && n > 0 ? n : 1;
        if (everyNth > 1) Console.WriteLine($"every {everyNth}th file");

        var edit = new System.Numerics.Vector3(11.5f, -22.25f, 33.75f);
        const float keptLimit = 0.1f;
        const float elsewhereLimit = 0.1f;

        string work = Path.Combine(Path.GetTempPath(), "symrm-editframe");
        Directory.CreateDirectory(work);

        var reader = new HkxBinaryReader();
        int done = 0, kept = 0, lost = 0, disturbed = 0, refused = 0, skipped = 0;
        float worstKept = 0, worstElsewhere = 0;
        string worstKeptFile = "", worstElsewhereFile = "";
        var failures = new List<string>();

        for (int i = 0; i < files.Length; i++)
        {
            if (i % everyNth != 0) continue;
            string file = files[i];

            HkxAnimationData before;
            try
            {
                if (!reader.TryReadAnimation(file, out before)) { skipped++; continue; }
                if (before.AnimationClass != "hkaSplineCompressedAnimation") { skipped++; continue; }
                if (before.NumFrames < 3 || before.Tracks.Count == 0) { skipped++; continue; }
                if (before.Tracks[0].Translations.Count < before.NumFrames) { skipped++; continue; }
            }
            catch (Exception) { skipped++; continue; }

            done++;
            int track = 0, frame = before.NumFrames / 2;

            var wasTranslations = before.Tracks[track].Translations.ToList();
            before.Tracks[track].Translations[frame] = edit;

            NativeAnimation.Result written;
            try { written = NativeAnimation.Recompress(file, before); }
            catch (InvalidOperationException e) { refused++; failures.Add($"{Path.GetFileName(file)}: refused, {e.Message}"); continue; }
            catch (Exception e) { lost++; failures.Add($"{Path.GetFileName(file)}: threw, {e.Message}"); continue; }

            string outPath = Path.Combine(work, "edited.hkx");
            File.WriteAllBytes(outPath, written.Bytes);

            HkxAnimationData after;
            try { after = reader.ReadAnimation(outPath); }
            catch (Exception e) { lost++; failures.Add($"{Path.GetFileName(file)}: could not be read back, {e.Message}"); continue; }

            if (after.Tracks.Count <= track || after.Tracks[track].Translations.Count <= frame)
            {
                lost++; failures.Add($"{Path.GetFileName(file)}: the edited track came back short"); continue;
            }

            float keptDrift = (after.Tracks[track].Translations[frame] - edit).Length();
            if (keptDrift > worstKept) { worstKept = keptDrift; worstKeptFile = Path.GetFileName(file); }

            float elsewhere = 0;
            for (int fr = 0; fr < before.NumFrames && fr < after.Tracks[track].Translations.Count; fr++)
            {
                if (fr == frame) continue;
                elsewhere = MathF.Max(elsewhere, (after.Tracks[track].Translations[fr] - wasTranslations[fr]).Length());
            }
            if (elsewhere > worstElsewhere) { worstElsewhere = elsewhere; worstElsewhereFile = Path.GetFileName(file); }

            bool keptOk = keptDrift <= keptLimit;
            bool elsewhereOk = elsewhere <= elsewhereLimit;
            if (keptOk && elsewhereOk) kept++;
            else
            {
                if (!keptOk) { lost++; failures.Add($"{Path.GetFileName(file)}: the edit drifted {keptDrift:F3}"); }
                if (!elsewhereOk) { disturbed++; failures.Add($"{Path.GetFileName(file)}: a frame it did not touch moved {elsewhere:F3}"); }
            }

            try { File.Delete(outPath); } catch (Exception) { }
        }

        Console.WriteLine($"\n{done} spline clip(s) edited and saved: {kept} kept the change and left the " +
                          $"rest alone, {lost} lost the change, {disturbed} disturbed another frame, " +
                          $"{refused} refused, {skipped} not editable spline clips");
        Console.WriteLine($"limits: the edit within {keptLimit} unit(s), no other frame moved more than {elsewhereLimit}");
        Console.WriteLine($"worst on the edited frame   {worstKept:F5}   {worstKeptFile}");
        Console.WriteLine($"worst on a frame not edited {worstElsewhere:F5}   {worstElsewhereFile}");
        foreach (string line in failures.Take(20)) Console.WriteLine($"  {line}");
        if (failures.Count > 20) Console.WriteLine($"  and {failures.Count - 20} more");

        return lost + disturbed + refused == 0 ? 0 : 1;
    }

    private static int Trim(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        int everyNth = argv.Length > 2 && int.TryParse(argv[2], out int n) && n > 0 ? n : 1;
        if (everyNth > 1) Console.WriteLine($"every {everyNth}th file");

        const float positionLimit = 0.05f;
        const float rotationLimit = 0.01f;

        string work = Path.Combine(Path.GetTempPath(), "symrm-trim");
        Directory.CreateDirectory(work);

        var reader = new HkxBinaryReader();
        int done = 0, skipped = 0, refused = 0, threw = 0, unreadable = 0;

        var held = new Dictionary<string, int>();
        var broke = new Dictionary<string, int>();
        var firstBreak = new Dictionary<string, string>();
        var checks = new[]
        {
            "frame count", "clip duration", "frame duration", "kept frames pose for pose",
            "annotations kept", "annotations keep their text", "annotations inside the clip",
            "motion carried over",
            "motion sample count", "motion duration", "motion starts at the origin",
        };
        foreach (string check in checks) { held[check] = 0; broke[check] = 0; }

        void Check(string name, bool ok, string file, string detail)
        {
            if (ok) { held[name]++; return; }
            broke[name]++;
            if (!firstBreak.ContainsKey(name)) firstBreak[name] = $"{Path.GetFileName(file)}: {detail}";
        }

        var refusals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        float worstPos = 0, worstRot = 0;
        string worstPosFile = "", worstRotFile = "";
        long before = 0, after = 0;

        int motionSeen = 0, motionAtOrigin = 0;

        for (int i = 0; i < files.Length; i++)
        {
            if (i % everyNth != 0) continue;
            string file = files[i];

            HkxAnimationData was;
            RootMotion.Motion motion;
            try
            {
                if (!reader.TryReadAnimation(file, out was)) { skipped++; continue; }
                if (was.AnimationClass != NativeAnimation.SplineClass) { skipped++; continue; }
                if (was.NumFrames < 4 || was.Tracks.Count == 0) { skipped++; continue; }
                if (was.Tracks[0].Translations.Count < was.NumFrames) { skipped++; continue; }
                motion = RootMotion.Read(file);
            }
            catch (Exception) { skipped++; continue; }

            if (motion.Any)
            {
                motionSeen++;
                var start = motion.Samples[0];
                if (start.Position.Length() < 1e-3f && MathF.Abs(start.TurnRadians) < 1e-3f)
                    motionAtOrigin++;
            }

            int first = was.NumFrames / 4;
            int last = was.NumFrames - 1 - was.NumFrames / 4;
            if (last - first + 1 < 2) { skipped++; continue; }

            done++;

            AnimationEdit.Trimmed cut;
            try { cut = AnimationEdit.Trim(was, motion, first, last); }
            catch (InvalidOperationException e)
            {
                refused++;
                refusals[Head(e.Message)] = refusals.GetValueOrDefault(Head(e.Message)) + 1;
                continue;
            }

            NativeAnimation.Result written;
            try
            {
                written = NativeAnimation.Recompress(file, cut.Animation, null, true,
                                                     NativeAnimation.Timeline.Of(cut));
            }
            catch (InvalidOperationException e)
            {
                refused++;
                refusals[Head(e.Message)] = refusals.GetValueOrDefault(Head(e.Message)) + 1;
                continue;
            }
            catch (Exception e) { threw++; refusals[Head(e.Message)] = refusals.GetValueOrDefault(Head(e.Message)) + 1; continue; }

            string saved = Path.Combine(work, "trimmed.hkx");
            File.WriteAllBytes(saved, written.Bytes);

            HkxAnimationData now;
            RootMotion.Motion nowMotion;
            try { now = reader.ReadAnimation(saved); nowMotion = RootMotion.Read(saved); }
            catch (Exception) { unreadable++; continue; }

            before += new FileInfo(file).Length;
            after += written.Bytes.Length;

            int kept = last - first + 1;
            Check("frame count", now.NumFrames == kept, file, $"{now.NumFrames} frame(s) against {kept}");
            Check("clip duration", MathF.Abs(now.Duration - cut.Animation.Duration) <= 1e-3f, file,
                  $"{now.Duration:F4}s against {cut.Animation.Duration:F4}s");
            Check("frame duration", MathF.Abs(now.FrameDuration - was.FrameDuration) <= 1e-5f, file,
                  $"{now.FrameDuration:F6} against {was.FrameDuration:F6}");

            float pos = 0, rot = 0;
            int comparable = Math.Min(kept, now.NumFrames);
            for (int t = 0; t < was.Tracks.Count && t < now.Tracks.Count; t++)
                for (int f = 0; f < comparable; f++)
                {
                    var a = was.Tracks[t];
                    var b = now.Tracks[t];
                    if (first + f < a.Translations.Count && f < b.Translations.Count)
                        pos = MathF.Max(pos, (a.Translations[first + f] - b.Translations[f]).Length());
                    if (first + f < a.Rotations.Count && f < b.Rotations.Count)
                        rot = MathF.Max(rot, SplineQuat.AngleBetween(a.Rotations[first + f], b.Rotations[f]));
                }

            if (pos > worstPos) { worstPos = pos; worstPosFile = Path.GetFileName(file); }
            if (rot > worstRot) { worstRot = rot; worstRotFile = Path.GetFileName(file); }

            Check("kept frames pose for pose", pos <= positionLimit && rot <= rotationLimit, file,
                  $"drifted {pos:F4} unit(s), {rot:F5} radian(s)");

            Check("annotations kept", now.Annotations.Count == cut.Animation.Annotations.Count, file,
                  $"{now.Annotations.Count} annotation(s) against the {cut.Animation.Annotations.Count} the cut kept");

            var texts = cut.Animation.Annotations.Select(a => a.Text).OrderBy(t => t, StringComparer.Ordinal);
            var back = now.Annotations.Select(a => a.Text).OrderBy(t => t, StringComparer.Ordinal);
            Check("annotations keep their text", texts.SequenceEqual(back, StringComparer.Ordinal), file,
                  $"came back as [{string.Join(", ", back)}] against [{string.Join(", ", texts)}]");

            float past = 0;
            foreach (var note in now.Annotations) past = MathF.Max(past, note.Time - now.Duration);
            Check("annotations inside the clip", past <= 1f / 60f, file,
                  $"an annotation sits {past:F3}s past the end of a {now.Duration:F3}s clip");

            Check("motion carried over", motion.Any == nowMotion.Any, file,
                  motion.Any ? "the travel was lost" : "a travel object appeared from nowhere");

            if (!motion.Any) continue;

            int wanted = motion.Samples.Count == was.NumFrames ? kept
                       : motion.Samples.Count == 2 ? 2
                       : kept;

            Check("motion sample count", nowMotion.Samples.Count == wanted, file,
                  $"{nowMotion.Samples.Count} sample(s) against {wanted}, from {motion.Samples.Count} " +
                  $"over {was.NumFrames} frame(s)");

            Check("motion duration", MathF.Abs(nowMotion.Duration - cut.Animation.Duration) <= 1e-3f, file,
                  $"the travel says {nowMotion.Duration:F4}s and the clip says {cut.Animation.Duration:F4}s");

            bool started = nowMotion.Samples.Count > 0 &&
                           nowMotion.Samples[0].Position.Length() < 1e-3f &&
                           MathF.Abs(nowMotion.Samples[0].TurnRadians) < 1e-3f;
            Check("motion starts at the origin", started, file,
                  nowMotion.Samples.Count > 0 ? $"starts at {nowMotion.Samples[0]}" : "has no samples");

            if (files.Length > 1) continue;

            Console.WriteLine($"{Path.GetFileName(file)}: frames {first} to {last} kept");
            Console.WriteLine($"  {was.NumFrames} frame(s) of {was.Duration:F3}s became " +
                              $"{now.NumFrames} of {now.Duration:F3}s");
            Console.WriteLine($"  travel: {motion} became {nowMotion}");
            Console.WriteLine($"  annotations: {was.Annotations.Count} became {now.Annotations.Count}, " +
                              $"{cut.AnnotationsDropped} outside the cut");

            foreach (var note in was.Annotations.OrderBy(a => a.Time))
                Console.WriteLine($"    was {note.Time,8:F3}s  {note.Text}");
            foreach (var note in now.Annotations.OrderBy(a => a.Time))
                Console.WriteLine($"    now {note.Time,8:F3}s  {note.Text}");
        }

        Console.WriteLine($"\n{done} spline clip(s) cut to their middle half and saved: " +
                          $"{refused} refused, {threw} threw, {unreadable} could not be read back, " +
                          $"{skipped} not clips this can cut");

        Console.WriteLine($"\nof the clips looked at, {motionSeen} carry travel and {motionAtOrigin} of " +
                          "those start it at the origin, which is the invariant a cut rebases to keep");

        Console.WriteLine("\nbreak matrix");
        Console.WriteLine($"  {"check",-28}{"held",8}{"broke",8}   first break");
        foreach (string check in checks)
            Console.WriteLine($"  {check,-28}{held[check],8}{broke[check],8}   " +
                              firstBreak.GetValueOrDefault(check, ""));

        Console.WriteLine($"\nlimits: position {positionLimit} unit(s), rotation {rotationLimit} radian(s)");
        Console.WriteLine($"worst position  {worstPos:F5} unit(s)   {worstPosFile}");
        Console.WriteLine($"worst rotation  {worstRot:F6} radian(s) {worstRotFile}");
        if (before > 0)
            Console.WriteLine($"file size: {after} byte(s) against {before} shipped, {100.0 * after / before:F1}%");

        if (refusals.Count > 0)
        {
            Console.WriteLine("\nrefused, by reason");
            foreach (var (reason, count) in refusals.OrderByDescending(r => r.Value))
                Console.WriteLine($"  x{count,-6} {reason}");
        }

        return broke.Values.Sum() + threw + unreadable + refused == 0 ? 0 : 1;
    }

    private static int Retime(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        int everyNth = argv.Length > 2 && int.TryParse(argv[2], out int n) && n > 0 ? n : 1;
        if (everyNth > 1) Console.WriteLine($"every {everyNth}th file");

        const float positionLimit = 0.05f;
        const float rotationLimit = 0.01f;

        string work = Path.Combine(Path.GetTempPath(), "symrm-retime");
        Directory.CreateDirectory(work);

        var passes = new (string Name, float Scale, bool KeepRate)[]
        {
            ("twice as long", 2f, true),
            ("half as long", 0.5f, true),
            ("twice, same frames", 2f, false),
        };

        var checks = new[]
        {
            "frame count", "clip duration", "frame duration", "annotations kept",
            "annotations keep their text", "annotations moved with the clip",
            "annotations inside the clip", "motion carried over", "motion sample count",
            "motion duration", "travels the same distance", "frames survive when nothing is resampled",
        };

        var held = new Dictionary<(string, string), int>();
        var broke = new Dictionary<(string, string), int>();
        var firstBreak = new Dictionary<(string, string), string>();
        foreach (var pass in passes)
            foreach (string check in checks) { held[(pass.Name, check)] = 0; broke[(pass.Name, check)] = 0; }

        void Check(string pass, string name, bool ok, string file, string detail)
        {
            if (ok) { held[(pass, name)]++; return; }
            broke[(pass, name)]++;
            if (!firstBreak.ContainsKey((pass, name)))
                firstBreak[(pass, name)] = $"{Path.GetFileName(file)}: {detail}";
        }

        var reader = new HkxBinaryReader();
        var refusals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int done = 0, skipped = 0, refused = 0, threw = 0, unreadable = 0;

        var cost = passes.ToDictionary(p => p.Name, _ => new List<(float Position, float Rotation)>());
        var overBudget = passes.ToDictionary(p => p.Name, _ => 0);

        for (int i = 0; i < files.Length; i++)
        {
            if (i % everyNth != 0) continue;
            string file = files[i];

            HkxAnimationData was;
            RootMotion.Motion motion;
            try
            {
                if (!reader.TryReadAnimation(file, out was)) { skipped++; continue; }
                if (was.AnimationClass != NativeAnimation.SplineClass) { skipped++; continue; }
                if (was.NumFrames < 4 || was.Tracks.Count == 0) { skipped++; continue; }
                if (was.Tracks[0].Translations.Count < was.NumFrames) { skipped++; continue; }
                motion = RootMotion.Read(file);
            }
            catch (Exception) { skipped++; continue; }

            done++;
            float wasLength = (was.NumFrames - 1) * AnimationEdit.FrameDuration(was);

            foreach (var (name, scale, keepRate) in passes)
            {
                AnimationEdit.Retimed made;

                try { made = AnimationEdit.Retime(was, motion, scale, keepRate); }
                catch (InvalidOperationException e)
                {
                    refused++;
                    refusals[Head(e.Message)] = refusals.GetValueOrDefault(Head(e.Message)) + 1;
                    continue;
                }

                cost[name].Add((made.PositionError, made.RotationError));
                if (made.PositionError > AnimationEdit.Budget.Tail.Position ||
                    made.RotationError > AnimationEdit.Budget.Tail.Rotation)
                    overBudget[name]++;

                NativeAnimation.Result written;
                try
                {
                    written = NativeAnimation.Recompress(file, made.Animation, null, true,
                                                         NativeAnimation.Timeline.Of(made, wasLength));
                }
                catch (InvalidOperationException e)
                {
                    refused++;
                    refusals[Head(e.Message)] = refusals.GetValueOrDefault(Head(e.Message)) + 1;
                    continue;
                }
                catch (Exception e)
                {
                    threw++;
                    refusals[Head(e.Message)] = refusals.GetValueOrDefault(Head(e.Message)) + 1;
                    continue;
                }

                string saved = Path.Combine(work, "retimed.hkx");
                File.WriteAllBytes(saved, written.Bytes);

                HkxAnimationData now;
                RootMotion.Motion nowMotion;
                try { now = reader.ReadAnimation(saved); nowMotion = RootMotion.Read(saved); }
                catch (Exception) { unreadable++; continue; }

                Check(name, "frame count", now.NumFrames == made.Animation.NumFrames, file,
                      $"{now.NumFrames} frame(s) against {made.Animation.NumFrames}");
                Check(name, "clip duration",
                      MathF.Abs(now.Duration - made.Animation.Duration) <= 1e-3f, file,
                      $"{now.Duration:F4}s against {made.Animation.Duration:F4}s");
                Check(name, "frame duration",
                      MathF.Abs(now.FrameDuration - made.Animation.FrameDuration) <= 1e-5f, file,
                      $"{now.FrameDuration:F6} against {made.Animation.FrameDuration:F6}");

                Check(name, "annotations kept",
                      now.Annotations.Count == was.Annotations.Count, file,
                      $"{now.Annotations.Count} annotation(s) against {was.Annotations.Count}");

                var wanted = made.Animation.Annotations.Select(a => a.Text)
                                .OrderBy(t => t, StringComparer.Ordinal);
                var back = now.Annotations.Select(a => a.Text).OrderBy(t => t, StringComparer.Ordinal);
                Check(name, "annotations keep their text",
                      wanted.SequenceEqual(back, StringComparer.Ordinal), file,
                      $"came back as [{string.Join(", ", back)}] against [{string.Join(", ", wanted)}]");

                var wantTimes = made.Animation.Annotations.Select(a => a.Time).OrderBy(t => t).ToList();
                var haveTimes = now.Annotations.Select(a => a.Time).OrderBy(t => t).ToList();
                float moved = wantTimes.Count == haveTimes.Count && wantTimes.Count > 0
                    ? Enumerable.Range(0, wantTimes.Count).Max(k => MathF.Abs(wantTimes[k] - haveTimes[k]))
                    : 0;
                Check(name, "annotations moved with the clip",
                      wantTimes.Count == haveTimes.Count && moved <= 1e-3f, file,
                      $"worst annotation is {moved:F4}s from where the retime put it");

                float past = 0;
                foreach (var note in now.Annotations) past = MathF.Max(past, note.Time - now.Duration);
                Check(name, "annotations inside the clip", past <= 1f / 60f, file,
                      $"an annotation sits {past:F3}s past the end of a {now.Duration:F3}s clip");

                Check(name, "motion carried over", motion.Any == nowMotion.Any, file,
                      motion.Any ? "the travel was lost" : "a travel object appeared from nowhere");

                if (motion.Any)
                {
                    Check(name, "motion sample count",
                          nowMotion.Samples.Count == made.Motion!.Samples.Count, file,
                          $"{nowMotion.Samples.Count} sample(s) against {made.Motion.Samples.Count}");
                    Check(name, "motion duration",
                          MathF.Abs(nowMotion.Duration - made.Animation.Duration) <= 1e-3f, file,
                          $"the travel says {nowMotion.Duration:F4}s and the clip says " +
                          $"{made.Animation.Duration:F4}s");

                    float went = motion.Travel.Length(), goes = nowMotion.Travel.Length();
                    Check(name, "travels the same distance",
                          MathF.Abs(went - goes) <= MathF.Max(0.5f, went * 0.02f), file,
                          $"travelled {went:F2} units and now travels {goes:F2}");
                }

                if (keepRate) continue;

                float pos = 0, rot = 0;
                for (int t = 0; t < was.Tracks.Count && t < now.Tracks.Count; t++)
                    for (int f = 0; f < was.NumFrames && f < now.Tracks[t].Translations.Count; f++)
                    {
                        pos = MathF.Max(pos, (was.Tracks[t].Translations[f] -
                                              now.Tracks[t].Translations[f]).Length());
                        rot = MathF.Max(rot, SplineQuat.AngleBetween(was.Tracks[t].Rotations[f],
                                                                     now.Tracks[t].Rotations[f]));
                    }

                Check(name, "frames survive when nothing is resampled",
                      pos <= positionLimit && rot <= rotationLimit, file,
                      $"drifted {pos:F4} unit(s), {rot:F5} radian(s)");
            }
        }

        Console.WriteLine($"\n{done} spline clip(s) retimed three ways and saved: {refused} refused, " +
                          $"{threw} threw, {unreadable} could not be read back, " +
                          $"{skipped} not clips this can retime");

        Console.WriteLine("\nbreak matrix");
        Console.WriteLine($"  {"check",-42}" + string.Concat(passes.Select(p => $"{p.Name,-24}")));
        foreach (string check in checks)
        {
            string row = $"  {check,-42}";
            foreach (var pass in passes)
            {
                var key = (pass.Name, check);
                row += $"{held[key] + "/" + broke[key],-24}";
            }
            Console.WriteLine(row);
        }

        foreach (var pass in passes)
            foreach (string check in checks)
                if (firstBreak.TryGetValue((pass.Name, check), out string? example))
                    Console.WriteLine($"  first break, {pass.Name}, {check}: {example}");

        Console.WriteLine("\n(held/broke per pass)");

        Console.WriteLine("\nwhat the resampling cost, against the frames it came from");
        Console.WriteLine($"  {"pass",-30}{"clips",8}{"median",12}{"p90",12}{"p99",12}{"worst",12}" +
                          "   over the budget");
        foreach (var pass in passes)
        {
            var costs = cost[pass.Name];
            if (costs.Count == 0) continue;

            var position = costs.Select(c => c.Position).OrderBy(v => v).ToList();
            var rotation = costs.Select(c => c.Rotation).OrderBy(v => v).ToList();

            Console.WriteLine($"  {pass.Name + ", position",-30}{position.Count,8}" +
                              $"{At(position, 0.5f),12:F4}{At(position, 0.9f),12:F4}" +
                              $"{At(position, 0.99f),12:F4}{position[^1],12:F4}   {overBudget[pass.Name]}");
            Console.WriteLine($"  {pass.Name + ", rotation",-30}{rotation.Count,8}" +
                              $"{At(rotation, 0.5f),12:F5}{At(rotation, 0.9f),12:F5}" +
                              $"{At(rotation, 0.99f),12:F5}{rotation[^1],12:F5}");
        }

        Console.WriteLine($"\nnothing refuses on error by default. The last column counts against " +
                          $"AnimationEdit.Budget.Tail, {AnimationEdit.Budget.Tail}, which is a number " +
                          "a caller can pass rather than one anything applies.");
        Console.WriteLine("rotation error is the angle between two rotations, so it cannot read past " +
                          "1.5708 radians however wrong the rotation is.");
        Console.WriteLine($"pose limits for the pass that resamples nothing: position {positionLimit} " +
                          $"unit(s), rotation {rotationLimit} radian(s)");

        if (refusals.Count > 0)
        {
            Console.WriteLine("\nrefused, by reason");
            foreach (var (reason, count) in refusals.OrderByDescending(r => r.Value))
                Console.WriteLine($"  x{count,-6} {reason}");
        }

        return broke.Values.Sum() + threw + unreadable + refused == 0 ? 0 : 1;
    }

    private static float At(List<float> sorted, float fraction) =>
        sorted.Count == 0 ? 0 : sorted[Math.Clamp((int)(fraction * (sorted.Count - 1)), 0, sorted.Count - 1)];

    private static string Head(string message)
    {
        int stop = message.IndexOf(". ", StringComparison.Ordinal);
        string head = stop > 0 ? message[..(stop + 1)] : message;
        return head.Length > 140 ? head[..140] : head;
    }

    private static int SaveSpline(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories).OrderBy(f => f).ToArray()
            : new[] { target };

        int everyNth = argv.Length > 2 && int.TryParse(argv[2], out int n) && n > 0 ? n : 1;
        if (everyNth > 1) Console.WriteLine($"every {everyNth}th file");

        const float positionLimit = 0.05f;
        const float rotationLimit = 0.01f;

        string work = Path.Combine(Path.GetTempPath(), "symrm-savespline");
        Directory.CreateDirectory(work);

        var reader = new HkxBinaryReader();
        int done = 0, clean = 0, bad = 0, refused = 0, skipped = 0;
        long before = 0, after = 0;
        float worstPos = 0, worstRot = 0;
        string worstPosFile = "", worstRotFile = "";
        var failures = new List<string>();

        for (int i = 0; i < files.Length; i++)
        {
            if (i % everyNth != 0) continue;
            string file = files[i];

            HkxAnimationData was;
            try
            {
                if (!reader.TryReadAnimation(file, out was)) { skipped++; continue; }
                if (was.AnimationClass != "hkaSplineCompressedAnimation") { skipped++; continue; }
                if (was.NumFrames <= 0 || was.Tracks.Count == 0) { skipped++; continue; }
            }
            catch (Exception) { skipped++; continue; }

            done++;

            NativeAnimation.Result written;
            try { written = NativeAnimation.Recompress(file, was); }
            catch (InvalidOperationException e)
            {
                refused++;
                failures.Add($"{Path.GetFileName(file)}: refused, {e.Message}");
                continue;
            }
            catch (Exception e)
            {
                bad++;
                failures.Add($"{Path.GetFileName(file)}: threw, {e.Message}");
                continue;
            }

            string saved = Path.Combine(work, Path.GetFileNameWithoutExtension(file) + "-spline.hkx");
            File.WriteAllBytes(saved, written.Bytes);

            HkxAnimationData now;
            try { now = reader.ReadAnimation(saved); }
            catch (Exception e)
            {
                bad++;
                failures.Add($"{Path.GetFileName(file)}: could not be read back, {e.Message}");
                continue;
            }

            if (now.AnimationClass != NativeAnimation.SplineClass)
            {
                bad++;
                failures.Add($"{Path.GetFileName(file)}: came back as {now.AnimationClass}");
                continue;
            }

            var wrong = new List<string>();
            if (now.NumFrames != was.NumFrames) wrong.Add($"{now.NumFrames} frames against {was.NumFrames}");
            if (now.Tracks.Count != was.Tracks.Count) wrong.Add($"{now.Tracks.Count} tracks against {was.Tracks.Count}");
            if (MathF.Abs(now.Duration - was.Duration) > 1e-4f) wrong.Add($"duration {now.Duration} against {was.Duration}");
            if (MathF.Abs(now.FrameDuration - was.FrameDuration) > 1e-5f)
                wrong.Add($"frame duration {now.FrameDuration} against {was.FrameDuration}");
            if (now.Annotations.Count != was.Annotations.Count)
                wrong.Add($"{now.Annotations.Count} annotations against {was.Annotations.Count}");

            else if (!was.Annotations.Select(a => a.Text).OrderBy(t => t, StringComparer.Ordinal)
                        .SequenceEqual(now.Annotations.Select(a => a.Text)
                        .OrderBy(t => t, StringComparer.Ordinal), StringComparer.Ordinal))
                wrong.Add($"{now.Annotations.Count(a => a.Text.Length == 0)} of {now.Annotations.Count} " +
                          "annotations came back with different text");

            if (wrong.Count > 0)
            {
                bad++;
                failures.Add($"{Path.GetFileName(file)}: {string.Join(", ", wrong)}");
                continue;
            }

            float filePos = 0, fileRot = 0;
            for (int t = 0; t < was.Tracks.Count; t++)
                for (int f = 0; f < was.NumFrames; f++)
                {
                    var a = was.Tracks[t];
                    var b = now.Tracks[t];
                    if (f < a.Translations.Count && f < b.Translations.Count)
                        filePos = MathF.Max(filePos, (a.Translations[f] - b.Translations[f]).Length());
                    if (f < a.Rotations.Count && f < b.Rotations.Count)
                        fileRot = MathF.Max(fileRot, SplineQuat.AngleBetween(a.Rotations[f], b.Rotations[f]));
                }

            if (filePos > worstPos) { worstPos = filePos; worstPosFile = Path.GetFileName(file); }
            if (fileRot > worstRot) { worstRot = fileRot; worstRotFile = Path.GetFileName(file); }

            before += new FileInfo(file).Length;
            after += written.Bytes.Length;

            if (filePos > positionLimit || fileRot > rotationLimit)
            {
                bad++;
                failures.Add($"{Path.GetFileName(file)}: drifted {filePos:F4} unit(s), {fileRot:F5} radian(s)");
            }
            else clean++;

            try { File.Delete(saved); } catch (Exception) { }
        }

        Console.WriteLine($"\n{done} spline animation(s): {clean} saved and read back within the limits, " +
                          $"{bad} did not, {refused} refused, {skipped} not spline compressed");
        Console.WriteLine($"worst position  {worstPos:F5} unit(s)   {worstPosFile}");
        Console.WriteLine($"worst rotation  {worstRot:F6} radian(s) {worstRotFile}");
        if (before > 0)
            Console.WriteLine($"file size: {after} byte(s) against {before} shipped, {100.0 * after / before:F1}%");

        foreach (string line in failures.Take(20)) Console.WriteLine($"  {line}");
        if (failures.Count > 20) Console.WriteLine($"  and {failures.Count - 20} more");

        return bad + refused == 0 ? 0 : 1;
    }

    private static int Spline(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories).OrderBy(f => f).ToArray()
            : new[] { target };

        int everyNth = argv.Length > 2 && int.TryParse(argv[2], out int n) && n > 0 ? n : 1;
        if (everyNth > 1) Console.WriteLine($"every {everyNth}th file");

        const float positionLimit = 0.05f;
        const float rotationLimit = 0.01f;
        const float scaleLimit = 0.01f;

        var reader = new HkxBinaryReader();
        int checkedFiles = 0, clean = 0, bad = 0, refused = 0, skipped = 0;
        long originalBytes = 0, writtenBytes = 0;
        float worstPos = 0, worstRot = 0, worstScale = 0;
        string worstPosFile = "", worstRotFile = "", worstScaleFile = "";
        var failures = new List<string>();

        for (int i = 0; i < files.Length; i++)
        {
            if (i % everyNth != 0) continue;
            string file = files[i];

            HkxAnimationData before;
            try
            {
                if (!reader.TryReadAnimation(file, out before)) { skipped++; continue; }
                if (before.AnimationClass != "hkaSplineCompressedAnimation") { skipped++; continue; }
                if (before.NumFrames <= 0 || before.Tracks.Count == 0) { skipped++; continue; }
            }
            catch (Exception) { skipped++; continue; }

            checkedFiles++;

            SplineEncoder.Blob blob;
            try { blob = SplineEncoder.Encode(before); }
            catch (InvalidOperationException e)
            {
                refused++;
                failures.Add($"{Path.GetFileName(file)}: refused, {e.Message}");
                continue;
            }

            var after = new HkxAnimationData { NumFrames = before.NumFrames };
            try
            {
                SplineEncoder.Decode(blob.Data, blob.BlockOffsets, before.Tracks.Count, before.NumFrames,
                    blob.MaskAndQuantizationSize, blob.MaxFramesPerBlock, after);
            }
            catch (Exception e)
            {
                bad++;
                failures.Add($"{Path.GetFileName(file)}: the blob could not be read back, {e.Message}");
                continue;
            }

            float filePos = 0, fileRot = 0, fileScale = 0;
            bool shapeWrong = false;

            for (int t = 0; t < before.Tracks.Count && !shapeWrong; t++)
            {
                var was = before.Tracks[t];
                var now = after.Tracks[t];

                if (now.Translations.Count != before.NumFrames || now.Rotations.Count != before.NumFrames ||
                    now.Scales.Count != before.NumFrames)
                {
                    shapeWrong = true;
                    failures.Add($"{Path.GetFileName(file)}: track {t} came back with " +
                                 $"{now.Translations.Count}/{now.Rotations.Count}/{now.Scales.Count} " +
                                 $"frame(s) instead of {before.NumFrames}");
                    break;
                }

                for (int f = 0; f < before.NumFrames; f++)
                {
                    if (f < was.Translations.Count)
                        filePos = MathF.Max(filePos, (was.Translations[f] - now.Translations[f]).Length());
                    if (f < was.Rotations.Count)
                        fileRot = MathF.Max(fileRot, SplineQuat.AngleBetween(was.Rotations[f], now.Rotations[f]));
                    if (f < was.Scales.Count)
                        fileScale = MathF.Max(fileScale, (was.Scales[f] - now.Scales[f]).Length());
                }
            }

            if (shapeWrong) { bad++; continue; }

            if (filePos > worstPos) { worstPos = filePos; worstPosFile = Path.GetFileName(file); }
            if (fileRot > worstRot) { worstRot = fileRot; worstRotFile = Path.GetFileName(file); }
            if (fileScale > worstScale) { worstScale = fileScale; worstScaleFile = Path.GetFileName(file); }

            originalBytes += OriginalBlobSize(file);
            writtenBytes += blob.Data.Length;

            if (filePos > positionLimit || fileRot > rotationLimit || fileScale > scaleLimit)
            {
                bad++;
                failures.Add($"{Path.GetFileName(file)}: drifted {filePos:F4} unit(s), " +
                             $"{fileRot:F5} radian(s), {fileScale:F5} of scale");
            }
            else clean++;
        }

        Console.WriteLine($"\n{checkedFiles} spline animation(s): {clean} came back within the limits, " +
                          $"{bad} did not, {refused} refused, {skipped} not spline compressed");
        Console.WriteLine($"limits: {positionLimit} unit(s), {rotationLimit} radian(s), {scaleLimit} of scale");
        Console.WriteLine($"worst position  {worstPos:F5} unit(s)   {worstPosFile}");
        Console.WriteLine($"worst rotation  {worstRot:F6} radian(s) {worstRotFile}");
        Console.WriteLine($"worst scale     {worstScale:F6}         {worstScaleFile}");

        if (originalBytes > 0)
            Console.WriteLine($"size: {writtenBytes} byte(s) written against {originalBytes} shipped, " +
                              $"{100.0 * writtenBytes / originalBytes:F1}%");

        foreach (string line in failures.Take(20)) Console.WriteLine($"  {line}");
        if (failures.Count > 20) Console.WriteLine($"  and {failures.Count - 20} more");

        return bad + refused == 0 ? 0 : 1;
    }

    private static long OriginalBlobSize(string file)
    {
        try
        {
            var image = PackfileImage.Read(InputFilePolicy.ReadHkx(file));
            var objects = new PackfileObjects(image);
            foreach (var anim in objects.OfClass("hkaSplineCompressedAnimation"))
            {
                var blob = objects.ReadArray(anim, "data");
                if (blob != null) return blob.Count;
            }
        }
        catch (Exception) { }
        return 0;
    }

    private static int SplineStats(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories).OrderBy(f => f).ToArray()
            : new[] { target };

        var posQuant = new SortedDictionary<int, long>();
        var rotQuant = new SortedDictionary<int, long>();
        var scaleQuant = new SortedDictionary<int, long>();
        var posFlags = new SortedDictionary<byte, long>();
        var rotFlags = new SortedDictionary<byte, long>();
        var scaleFlags = new SortedDictionary<byte, long>();
        var degrees = new SortedDictionary<byte, long>();
        var blockSizes = new SortedDictionary<int, long>();
        var maskSizes = new SortedDictionary<string, long>();

        int spline = 0, skipped = 0, tracksSeen = 0;
        long framesTotal = 0;

        foreach (string file in files)
        {
            byte[] bytes;
            PackfileImage image;
            PackfileObjects objects;
            try
            {
                bytes = InputFilePolicy.ReadHkx(file);
                image = PackfileImage.Read(bytes);
                objects = new PackfileObjects(image);
            }
            catch (Exception) { skipped++; continue; }

            var data = image.Sections.FirstOrDefault(s => s.Tag == "__data__");
            if (data == null) { skipped++; continue; }

            foreach (var anim in objects.OfClass("hkaSplineCompressedAnimation").ToList())
            {
                int numTracks = objects.ReadInt(anim, "numberOfTransformTracks") ?? 0;
                int numFrames = objects.ReadInt(anim, "numFrames") ?? 0;
                int numBlocks = objects.ReadInt(anim, "numBlocks") ?? 0;
                int perBlock  = objects.ReadInt(anim, "maxFramesPerBlock") ?? 0;
                int maskSize  = objects.ReadInt(anim, "maskAndQuantizationSize") ?? 0;
                if (numTracks <= 0 || numFrames <= 0 || numBlocks <= 0 || perBlock <= 0) continue;

                var offsets = objects.ReadValueArray(anim, "blockOffsets", 4,
                    (b, at) => BitConverter.ToInt32(b, at));
                var blob = objects.ReadArray(anim, "data");
                if (offsets == null || blob == null) continue;

                spline++;
                framesTotal += numFrames;
                blockSizes[perBlock] = blockSizes.GetValueOrDefault(perBlock) + 1;

                string shape = maskSize == 4 * numTracks ? "4 per track"
                             : maskSize == SplineFormat.Align(4 * numTracks, 16) ? "4 per track, rounded to 16"
                             : $"other ({maskSize} for {numTracks})";
                maskSizes[shape] = maskSizes.GetValueOrDefault(shape) + 1;

                for (int b = 0; b < numBlocks && b < offsets.Count; b++)
                {
                    int blockStart = blob.At + offsets[b];
                    if (blockStart < 0 || blockStart + 4 * numTracks > data.Data.Length) continue;

                    for (int t = 0; t < numTracks; t++)
                    {
                        int m = blockStart + t * 4;
                        byte q = data.Data[m], p = data.Data[m + 1];
                        byte r = data.Data[m + 2], s = data.Data[m + 3];
                        tracksSeen++;

                        posQuant[q & 3] = posQuant.GetValueOrDefault(q & 3) + 1;
                        rotQuant[(q >> 2) & 0x0F] = rotQuant.GetValueOrDefault((q >> 2) & 0x0F) + 1;
                        scaleQuant[(q >> 6) & 3] = scaleQuant.GetValueOrDefault((q >> 6) & 3) + 1;
                        posFlags[p] = posFlags.GetValueOrDefault(p) + 1;
                        rotFlags[r] = rotFlags.GetValueOrDefault(r) + 1;
                        scaleFlags[s] = scaleFlags.GetValueOrDefault(s) + 1;
                    }

                    byte first = data.Data[blockStart + 1];
                    bool opensOnPosSpline = (first & 0x70) != 0;
                    int degreeAt = blockStart + maskSize + 2;
                    if (opensOnPosSpline && degreeAt < data.Data.Length)
                    {
                        byte d = data.Data[degreeAt];
                        degrees[d] = degrees.GetValueOrDefault(d) + 1;
                    }
                }
            }
        }

        static void Report(string what, IEnumerable<KeyValuePair<int, long>> counts)
        {
            Console.WriteLine($"  {what,-22} " + string.Join("  ", counts.Select(c => $"{c.Key}: {c.Value}")));
        }
        static void ReportBytes(string what, IEnumerable<KeyValuePair<byte, long>> counts)
        {
            Console.WriteLine($"  {what,-22} " + string.Join("  ", counts.Select(c => $"0x{c.Key:x2}: {c.Value}")));
        }

        Console.WriteLine($"\n{files.Length} file(s): {spline} carry a spline compressed animation, " +
                          $"{skipped} could not be read");
        Console.WriteLine($"{tracksSeen} track block(s) across {framesTotal} frame(s)");
        Console.WriteLine("\nquantisation formats, by how many track blocks chose each:");
        Report("position", posQuant);
        Report("rotation", rotQuant);
        Report("scale", scaleQuant);
        Console.WriteLine("\nchannel flag bytes:");
        ReportBytes("position", posFlags);
        ReportBytes("rotation", rotFlags);
        ReportBytes("scale", scaleFlags);
        Console.WriteLine("\ncurve degree, where it can be found without walking the block:");
        ReportBytes("degree", degrees);
        Console.WriteLine("\nframes per block:");
        Report("maxFramesPerBlock", blockSizes);
        Console.WriteLine("\nmaskAndQuantizationSize:");
        foreach (var kv in maskSizes) Console.WriteLine($"  {kv.Key,-30} {kv.Value}");

        Console.WriteLine("\ndecoded fingerprint:");
        Console.WriteLine("  " + DecodeFingerprint(files));
        return 0;
    }

    private static string DecodeFingerprint(IEnumerable<string> files)
    {
        var reader = new HkxBinaryReader();
        ulong hash = 1469598103934665603UL;
        int decoded = 0;
        long values = 0;

        void Feed(float v)
        {

            int q = (int)MathF.Round(v * 4096f);
            for (int b = 0; b < 4; b++)
            {
                hash ^= (byte)(q >> (b * 8));
                hash *= 1099511628211UL;
            }
            values++;
        }

        foreach (string file in files.OrderBy(f => f))
        {
            HkxAnimationData animation;
            try
            {
                if (!reader.TryReadAnimation(file, out animation)) continue;
                if (animation.AnimationClass != "hkaSplineCompressedAnimation") continue;
            }
            catch (Exception) { continue; }

            decoded++;
            foreach (var track in animation.Tracks)
            {
                foreach (var t in track.Translations) { Feed(t.X); Feed(t.Y); Feed(t.Z); }
                foreach (var r in track.Rotations) { Feed(r.X); Feed(r.Y); Feed(r.Z); Feed(r.W); }
                foreach (var s in track.Scales) { Feed(s.X); Feed(s.Y); Feed(s.Z); }
            }
        }

        return $"{decoded} file(s), {values} value(s), {hash:x16}";
    }

    private static bool Nudged(HkxBinaryReader reader, string file, HkxAnimationData before,
                               string work, ref string why)
    {
        var by = new System.Numerics.Vector3(1.5f, -2.25f, 0.75f);
        int track = before.NumTracks / 2, frame = before.NumFrames / 2;

        var edited = reader.ReadAnimation(file);
        var was = edited.Tracks[track].Translations[frame];
        edited.Tracks[track].Translations[frame] = was + by;

        NativeAnimation.Result written;
        try { written = NativeAnimation.Interleave(file, edited); }
        catch (Exception e) { Console.WriteLine("  NUDGE THREW  " + e.Message); why = e.Message; return false; }

        string path = Path.Combine(work, Path.GetFileNameWithoutExtension(file) + "-nudged.hkx");
        File.WriteAllBytes(path, written.Bytes);

        HkxAnimationData after;
        try { after = reader.ReadAnimation(path); }
        catch (Exception e) { Console.WriteLine("  NUDGE UNREADABLE  " + e.Message); why = e.Message; return false; }

        var landed = after.Tracks[track].Translations[frame];
        float off = (landed - (was + by)).Length();

        float elsewhere = 0;
        for (int t = 0; t < before.NumTracks; t++)
            for (int f = 0; f < before.NumFrames; f++)
            {
                if (t == track && f == frame) continue;
                elsewhere = Math.Max(elsewhere,
                    (before.Tracks[t].Translations[f] - after.Tracks[t].Translations[f]).Length());
            }

        bool ok = off < 0.001f && elsewhere < 0.001f;
        Console.WriteLine($"  moved track {track} frame {frame} by {by}: landed {off:E2} from where it " +
                          $"was asked to, worst movement anywhere else {elsewhere:E2}");
        if (!ok) why = "the nudge did not land where it was asked to";
        return ok;
    }

    private static float Angle(System.Numerics.Quaternion a, System.Numerics.Quaternion b)
    {
        a = System.Numerics.Quaternion.Normalize(a);
        b = System.Numerics.Quaternion.Normalize(b);

        double near = Math.Min(Distance(a, b, 1), Distance(a, b, -1));
        return (float)(2 * Math.Asin(Math.Clamp(near / 2, 0, 1)) * 180 / Math.PI);
    }

    private static double Distance(System.Numerics.Quaternion a, System.Numerics.Quaternion b, int sign)
    {
        double x = a.X - sign * b.X, y = a.Y - sign * b.Y, z = a.Z - sign * b.Z, w = a.W - sign * b.W;
        return Math.Sqrt(x * x + y * y + z * z + w * w);
    }

    private static int QsTransform(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        var files = Directory.Exists(argv[1])
            ? Directory.EnumerateFiles(argv[1], "*.hkx", SearchOption.AllDirectories).OrderBy(f => f).ToList()
            : new List<string> { Path.GetFullPath(argv[1]) };

        var translation = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var scale = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int poses = 0, read = 0;

        void Count(SortedDictionary<string, int> into, float w)
        {
            string key = w.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            into[key] = into.TryGetValue(key, out int had) ? had + 1 : 1;
        }

        foreach (string file in files)
        {
            PackfileImage image;
            PackfileObjects objects;
            try
            {
                image = PackfileImage.Read(file);
                objects = new PackfileObjects(image);
            }
            catch (Exception) { continue; }

            var data = image.Section("__data__");
            if (data == null) continue;

            bool any = false;
            foreach (var instance in objects.OfClass("hkaSkeleton"))
            {
                int? at = objects.FieldAt(instance, "referencePose");
                if (at == null) continue;

                var array = objects.ArrayAt(at.Value);
                if (array == null) continue;

                for (int i = 0; i < array.Count; i++)
                {
                    int p = array.At + i * 48;
                    if (p + 48 > data.Data.Length) break;

                    Count(translation, BitConverter.ToSingle(data.Data, p + 12));
                    Count(scale, BitConverter.ToSingle(data.Data, p + 44));
                    poses++;
                    any = true;
                }
            }
            if (any) read++;
        }

        Console.WriteLine($"{read} skeleton file(s), {poses} transform(s)\n");
        Console.WriteLine("the fourth lane of the translation");
        foreach (var (key, n) in translation) Console.WriteLine($"  {key,-14} {n}");

        Console.WriteLine("\nthe fourth lane of the scale");
        foreach (var (key, n) in scale) Console.WriteLine($"  {key,-14} {n}");
        return 0;
    }

    private static int Capacity(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        var files = Directory.Exists(argv[1])
            ? Directory.EnumerateFiles(argv[1], "*.hkx", SearchOption.AllDirectories).OrderBy(f => f).ToList()
            : new List<string> { Path.GetFullPath(argv[1]) };

        var all = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var structs = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var mismatched = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int read = 0;

        void Count(SortedDictionary<string, int> into, string key) =>
            into[key] = into.TryGetValue(key, out int had) ? had + 1 : 1;

        var types = HavokClassTypes.Shipped;

        foreach (string file in files)
        {
            PackfileImage image;
            PackfileObjects objects;
            try
            {
                image = PackfileImage.Read(file);
                objects = new PackfileObjects(image);
            }
            catch (Exception) { continue; }

            var data = image.Section("__data__");
            if (data == null) continue;
            read++;

            foreach (var instance in objects.Instances)
            {
                if (!types.Knows(instance.ClassName)) continue;

                foreach (var member in types.Members(instance.ClassName))
                {
                    if (member.VType is not ("TYPE_ARRAY" or "TYPE_SIMPLEARRAY")) continue;

                    int at = instance.Offset + member.Offset;
                    if (at + 16 > data.Data.Length) continue;

                    int count = BitConverter.ToInt32(data.Data, at + 8);
                    uint capacity = BitConverter.ToUInt32(data.Data, at + 12);
                    string key = $"{(count == 0 ? "empty" : "holds something")}  flags=0x{capacity & 0xC0000000u:x8}";

                    Count(all, key);
                    if (member.VSub == "TYPE_STRUCT") Count(structs, key);

                    if ((capacity & 0x3FFFFFFFu) != (uint)count)
                        Count(mismatched, $"{instance.ClassName}.{member.Name} count={count} " +
                                          $"capacity={capacity & 0x3FFFFFFFu}");
                }
            }
        }

        Console.WriteLine($"{read} file(s) read\n");
        Console.WriteLine("every array");
        foreach (var (key, n) in all) Console.WriteLine($"  {key,-34} {n}");

        Console.WriteLine("\narrays of structs only");
        foreach (var (key, n) in structs) Console.WriteLine($"  {key,-34} {n}");

        Console.WriteLine($"\ncapacity disagreeing with the count beside it: {mismatched.Values.Sum()}");
        foreach (var (key, n) in mismatched.Take(10)) Console.WriteLine($"  {key}  x{n}");
        return 0;
    }

    private static int Objects(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string file = Path.GetFullPath(argv[1]);
        string? wanted = argv.Length > 2 ? argv[2] : null;

        var image = PackfileImage.Read(file);
        var objects = new PackfileObjects(image);
        var (known, unknown) = objects.Coverage();

        Console.WriteLine($"{Path.GetFileName(file)}: {objects.Instances.Count} objects, " +
                          $"{known} of a class we have the layout for, {unknown} we do not");

        if (unknown > 0)
            Console.WriteLine("  no layout for: " + string.Join(", ", objects.UnknownClasses().Take(8)));

        if (wanted == null)
        {
            Console.WriteLine($"\n{"class",-44} count");
            foreach (var group in objects.Instances.GroupBy(i => i.ClassName)
                                                   .OrderByDescending(g => g.Count()))
                Console.WriteLine($"{group.Key,-44} {group.Count()}");
            return 0;
        }

        var members = HavokClasses.Shipped.Members(wanted);
        if (members.Count == 0)
        {
            Console.WriteLine($"\nno layout for {wanted}");
            return 1;
        }

        var instances = objects.OfClass(wanted).ToList();
        Console.WriteLine($"\n{wanted}: {members.Count} fields, {instances.Count} in this file");

        foreach (var instance in instances.Take(4))
        {
            Console.WriteLine($"\n  at 0x{instance.Offset:x}");
            foreach (var member in members)
            {
                string shown = member.Type switch
                {
                    "real" => objects.ReadFloat(instance, member.Name)?.ToString("0.####") ?? "?",
                    "stringptr" or "cstring" => objects.ReadString(instance, member.Name) is { } s
                        ? $"\"{s}\"" : "null",
                    "int32" or "uint32" or "int16" or "uint16" or "int8" or "uint8" or "bool" or "enum"
                        => Narrow(objects.ReadInt(instance, member.Name), member.Type),
                    _ => "",
                };
                if (shown.Length > 0)
                    Console.WriteLine($"    +{member.Offset,-5} {member.Name,-38} {shown}");
            }
        }

        if (instances.Count > 4) Console.WriteLine($"\n  ... and {instances.Count - 4} more");
        return 0;
    }

    private static string Narrow(int? value, string type)
    {
        if (value is not int raw) return "?";
        return type switch
        {
            "bool" => ((raw & 0xFF) != 0).ToString().ToLowerInvariant(),
            "int8" or "uint8" or "enum" => (raw & 0xFF).ToString(),
            "int16" or "uint16" => (raw & 0xFFFF).ToString(),
            _ => raw.ToString(),
        };
    }

    private static int Layout(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        var types = HavokClassTypes.Shipped;
        long runsSeen = 0, runsInOrder = 0, runsReused = 0, runsBesideTheirObject = 0;
        long gapBytes = 0, runBytes = 0;
        long placedSeen = 0, placedWhereExpected = 0;
        int cleanFiles = 0, oddFiles = 0, skipped = 0;
        var notes = new List<string>();

        var padding = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);
        void Pad(string kind, int amount)
        {
            if (!padding.TryGetValue(kind, out var counts))
                padding[kind] = counts = new Dictionary<int, int>();
            counts[amount] = counts.GetValueOrDefault(amount) + 1;
        }

        foreach (string file in files)
        {
            PackfileObjects objects;
            PackfileImage image;
            try
            {
                image = PackfileImage.Read(file);
                objects = new PackfileObjects(image);
            }
            catch (Exception e)
            {
                skipped++;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: {e.Message}");
                continue;
            }

            var data = image.Section("__data__");
            if (data == null || objects.Instances.Any(i => !types.Knows(i.ClassName))) { skipped++; continue; }

            bool odd = false;

            var starts = objects.Instances.Select(i => i.Offset).ToList();
            int Owner(int offset)
            {
                int found = -1;
                for (int k = 0; k < starts.Count && starts[k] <= offset; k++) found = k;
                return found;
            }

            foreach (var instance in objects.Instances)
            {
                int size = types[instance.ClassName]?.Size ?? 0;
                int next = starts.FirstOrDefault(s => s > instance.Offset, data.Data.Length);
                if (size > 0) gapBytes += Math.Max(0, next - (instance.Offset + size));
            }

            var order = FixupOrder.Sources(objects, types, data, global: false);
            var aims = new Dictionary<int, int>();
            foreach (var (source, destination) in data.Locals()) aims[source] = destination;

            int highest = -1;
            var seen = new HashSet<int>();
            foreach (int source in order)
            {
                if (!aims.TryGetValue(source, out int destination)) continue;
                runsSeen++;

                int owner = Owner(source);
                int endOfOwner = owner < 0 ? data.Data.Length
                                 : starts.FirstOrDefault(s => s > starts[owner], data.Data.Length);
                if (owner >= 0 && destination >= starts[owner] && destination < endOfOwner)
                    runsBesideTheirObject++;
                else
                {
                    odd = true;
                    if (notes.Count < 10)
                        notes.Add($"{Path.GetFileName(file)}: a pointer at 0x{source:x} aims at " +
                                  $"0x{destination:x}, outside the stretch its own object owns");
                }

                if (!seen.Add(destination)) { runsReused++; continue; }
                if (destination >= highest) { runsInOrder++; highest = destination; }
                else
                {
                    odd = true;
                    if (notes.Count < 10)
                        notes.Add($"{Path.GetFileName(file)}: 0x{destination:x} was allocated after " +
                                  $"0x{highest:x}, so the walk is not the write order");
                }
            }

            runBytes += seen.Count;

            var items = PackfileLayout.Of(image, types);
            if (items == null) { skipped++; continue; }

            if (!PackfileLayout.Accounted(items, data.Data.Length))
            {
                odd = true;
                if (notes.Count < 10)
                    notes.Add($"{Path.GetFileName(file)}: the walk does not account for the whole " +
                              "data section, so laying it out again would lose what it misses");
            }

            var predicted = PackfileLayout.Where(items);
            for (int k = 0; k < items.Count; k++)
            {
                Pad(items[k].Kind, items[k].At % 16);

                placedSeen++;
                if (predicted[k] == items[k].At) placedWhereExpected++;
                else
                {
                    odd = true;
                    if (notes.Count < 10)
                        notes.Add($"{Path.GetFileName(file)}: a {items[k].Kind} sits at 0x{items[k].At:x}, " +
                                  $"laying the file out from nothing put it at 0x{predicted[k]:x}");
                }
            }

            if (odd) oddFiles++; else cleanFiles++;
        }

        foreach (string note in notes) Console.WriteLine("  " + note);

        if (padding.Count > 0)
        {
            Console.WriteLine("\nwhere each thing starts within a sixteen byte boundary:");
            foreach (var (kind, counts) in padding.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                string spread = string.Join(", ", counts.OrderBy(c => c.Key)
                                                        .Select(c => $"{c.Value} at {c.Key}"));
                Console.WriteLine($"  {kind}: {spread}");
            }
        }

        Console.WriteLine($"\n{files.Length} file(s): {cleanFiles} laid out the way the walk predicts, " +
                          $"{oddFiles} not, {skipped} not read");
        Console.WriteLine($"pointed at: {runsSeen} pointer(s), {runsInOrder} allocated in walk order, " +
                          $"{runsReused} sharing a run with an earlier pointer, " +
                          $"{runsBesideTheirObject} landing inside their own object's stretch");
        Console.WriteLine($"{runBytes} distinct run(s) in {gapBytes} byte(s) of space between objects");
        Console.WriteLine($"laid out from scratch: {placedWhereExpected}/{placedSeen} " +
                          "object(s) and run(s) land where the walk puts them");
        return oddFiles == 0 && skipped == 0 ? 0 : 1;
    }

    private static int Notes(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        var types = HavokClassTypes.Shipped;
        long fields = 0, described = 0, explained = 0;
        int skipped = 0;
        var explainedBy = new Dictionary<string, int>(StringComparer.Ordinal);
        var classes = new HashSet<string>(StringComparer.Ordinal);

        foreach (string file in files)
        {
            PackfileObjects objects;
            try { objects = new PackfileObjects(PackfileImage.Read(file)); }
            catch (Exception) { skipped++; continue; }

            foreach (var instance in objects.Instances)
            {
                if (!types.Knows(instance.ClassName)) continue;
                classes.Add(instance.ClassName);

                var shown = ClassFields.Of(objects, instance, types);
                if (shown == null) continue;

                foreach (var field in shown)
                {
                    fields++;
                    classes.Add(field.Owner);

                    if (FieldNotes.Structure(field.Owner, field.Name) != null) described++;

                    if (FieldNotes.Meaning(field.Owner, field.Name) is { } note)
                    {
                        explained++;
                        string key = $"{field.Owner}.{field.Name}";
                        explainedBy[key] = explainedBy.GetValueOrDefault(key) + 1;
                    }
                }
            }
        }

        foreach (var (what, count) in explainedBy.OrderByDescending(e => e.Value))
            Console.WriteLine($"  {count,7}  {what}");

        Console.WriteLine($"\n{files.Length} file(s), {classes.Count} class(es) seen, {skipped} not read");
        Console.WriteLine($"{fields} field(s) a panel would show");
        Console.WriteLine($"described from the class table: {described} ({100.0 * described / Math.Max(1, fields):0.0}%)");
        Console.WriteLine($"explained by something we established: {explained} " +
                          $"({100.0 * explained / Math.Max(1, fields):0.0}%)");
        return described == fields ? 0 : 1;
    }

    private static int Chain(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string file = Path.GetFullPath(argv[1]);
        var chain = ProjectChain.Resolve(file);

        foreach (var link in chain.Links)
            Console.WriteLine($"  {link.Role,-12} {(link.Exists ? "found  " : "MISSING")} {link.Declared}");

        Console.WriteLine($"  animations   {chain.Animations.Count} declared by the character");
        Console.WriteLine($"  bones        {chain.Bones.Count} in the skeleton");

        foreach (string problem in chain.Problems) Console.WriteLine("  problem: " + problem);

        var checkResult = ProjectCheck.Run(chain);
        int unread = checkResult.Files.Count(f => f.Error.Length > 0);

        foreach (var unreadable in checkResult.Files.Where(f => f.Error.Length > 0).Take(5))
            Console.WriteLine($"  unread: {unreadable.Name}, {unreadable.Error}");

        Console.WriteLine($"\n{chain.Links.Count} link(s), {chain.Problems.Count} problem(s)");
        Console.WriteLine($"checked {checkResult.Files.Count} behaviour file(s), {unread} unread, " +
                          $"{checkResult.Errors} error(s), {checkResult.Warnings} warning(s)");
        return chain.Links.Count == 0 || unread > 0 ? 1 : 0;
    }

    private static int Lifecycle(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string root = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(path => path, StringComparer.Ordinal).ToList()
            : new List<string> { root };

        int graphs = 0, animations = 0, skeletons = 0, skipped = 0, failed = 0;
        foreach (string file in files)
        {
            try
            {
                string xml;
                try { xml = HkxTextEdit.TextOf(file); }
                catch (Exception ex)
                {
                    skipped++;
                    Console.WriteLine($"SKIP {file}: native XML unavailable ({ex.Message.Split('\n')[0]})");
                    continue;
                }
                if (xml.Length == 0) { skipped++; Console.WriteLine($"SKIP {file}: native XML unavailable"); continue; }
                var reader = new HkxBinaryReader();
                if (Path.GetFileName(file).Equals("skeleton.hkx", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var skeleton = reader.ReadSkeleton(file);
                        var pose = AnimationPose.ReferencePose(skeleton);
                        if (pose.Bones.Count == 0) throw new InvalidOperationException("reference pose is empty");
                        skeletons++;
                        Console.WriteLine($"PASS skeleton {file}: open validate render (not editable by this tool)");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Console.WriteLine($"FAIL skeleton {file}: {ex.Message.Split('\n')[0]}");
                    }
                    continue;
                }

                BehaviourGraphModel model;
                try { model = BehaviourGraphModel.Parse(xml); }
                catch (InvalidDataException ex) when (ex.Message.Contains("Missing __classnames__", StringComparison.Ordinal))
                {
                    skipped++;
                    Console.WriteLine($"SKIP {file}: not an editable HKX packfile");
                    continue;
                }

                if (model.Objects.Any(o => o.Class == "hkbBehaviorGraph"))
                {
                    if (!LifecycleGraph(file, xml, model, out string reason))
                    {
                        failed++;
                        Console.WriteLine($"FAIL graph {file}: {reason}");
                    }
                    else
                    {
                        graphs++;
                        Console.WriteLine($"PASS graph {file}: open edit save reload validate render");
                    }
                    continue;
                }

                if (reader.TryReadAnimation(file, out var animation))
                {
                    if (!LifecycleAnimation(reader, file, animation, out string reason))
                    {
                        failed++;
                        Console.WriteLine($"FAIL animation {file}: {reason}");
                    }
                    else
                    {
                        animations++;
                        Console.WriteLine($"PASS animation {file}: open edit save reload validate render");
                    }
                    continue;
                }

                skipped++;
                Console.WriteLine($"SKIP {file}: not a supported editable graph or animation");
            }
            catch (Exception ex) when (ex.Message.Contains("Missing __classnames__", StringComparison.Ordinal))
            {
                skipped++;
                Console.WriteLine($"SKIP {file}: not an editable HKX packfile");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"FAIL {file}: {ex.Message.Split('\n')[0]}");
            }
        }

        Console.WriteLine($"LIFECYCLE graph={graphs} animation={animations} skeleton={skeletons} skipped={skipped} failed={failed}");
        return graphs > 0 && animations > 0 && failed == 0 ? 0 : 1;
    }

    private static bool LifecycleGraph(string file, string xml, BehaviourGraphModel model, out string reason)
    {
        reason = "";
        if (!TryLifecycleEdit(xml, out string edited, out var plan))
        {
            reason = "no supported native userData edit";
            return false;
        }

        byte[] written = NativeSave.Apply(file, plan);
        string reread = NativeXml.From(written);
        var after = BehaviourGraphModel.Parse(reread);
        if (after.Objects.Count != model.Objects.Count) { reason = "object count changed after reload"; return false; }
        if (GraphValidator.Check(reread).Any(f => f.Level == GraphValidator.Level.Error))
        {
            reason = "validator reported an error after reload";
            return false;
        }
        if (GraphAuthor.Layout(after, 10000).Count == 0) { reason = "graph produced no render nodes"; return false; }
        if (reread == xml || edited == xml) { reason = "edit did not change the native document"; return false; }
        return true;
    }

    private static bool TryLifecycleEdit(string xml, out string edited, out NativeSave.Plan plan)
    {
        edited = xml;
        plan = new NativeSave.Plan(new List<NativeSave.Change>(), "no native edit was attempted");
        foreach (string id in HkxTextEdit.ObjectIds(xml))
        {
            foreach (var param in HkxTextEdit.ReadParams(xml, id).Where(p => p.Name == "userData"))
            {
                string value = param.Value.Trim() == "0" ? "1" : "0";
                string candidate;
                try { candidate = HkxTextEdit.SetParamAt(xml, id, "userData", value); }
                catch { continue; }

                var candidatePlan = NativeSave.Compare(xml, candidate);
                if (!candidatePlan.Possible || candidatePlan.Empty) continue;
                edited = candidate;
                plan = candidatePlan;
                return true;
            }
        }
        return false;
    }

    private static bool LifecycleAnimation(HkxBinaryReader reader, string file, HkxAnimationData before,
                                           out string reason)
    {
        reason = "";
        if (before.NumTracks == 0 || before.NumFrames == 0) { reason = "no editable animation frames"; return false; }

        var edited = reader.ReadAnimation(file);
        int track = edited.NumTracks / 2;
        int frame = edited.NumFrames / 2;
        edited.Tracks[track].Translations[frame] += new System.Numerics.Vector3(1.5f, -2.25f, 0.75f);

        var written = NativeAnimation.Interleave(file, edited);
        string work = WorkDirectory("symrm-lifecycle-", file);
        HkxTextEdit.ResetDirectory(work);
        string output = Path.Combine(work, Path.GetFileNameWithoutExtension(file) + "-native.hkx");
        File.WriteAllBytes(output, written.Bytes);
        var after = reader.ReadAnimation(output);
        if (after.NumTracks != before.NumTracks || after.NumFrames != before.NumFrames)
        {
            reason = "animation shape changed after reload";
            return false;
        }

        var skeleton = LifecycleSkeleton(file, reader);
        if (skeleton == null) { reason = "no sibling skeleton to compose a pose"; return false; }
        if (AnimationPose.At(skeleton, after, frame).Bones.Count == 0)
        {
            reason = "animation produced no pose bones";
            return false;
        }
        return true;
    }

    private static HkxSkeleton? LifecycleSkeleton(string file, HkxBinaryReader reader)
    {
        string? assets = SiblingSkeletonFolder(file);
        string? candidate = assets == null ? null : Path.Combine(assets, "skeleton.hkx");
        if (candidate == null || !File.Exists(candidate)) return null;
        try { return reader.ReadSkeleton(candidate); }
        catch { return null; }
    }

    private static int SaveNumbers(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        int saved = 0, refused = 0, wrong = 0, none = 0;
        var refusals = new Dictionary<string, int>(StringComparer.Ordinal);
        var notes = new List<string>();

        foreach (string file in files)
        {
            string xml;
            try
            {
                var image = PackfileImage.Read(file);
                xml = NativeXml.From(new PackfileObjects(image), image);
            }
            catch (Exception e)
            {
                refused++;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: {e.Message}");
                continue;
            }

            var found = System.Text.RegularExpressions.Regex.Match(
                xml, "<hkparam name=\"(?<field>[A-Za-z0-9_]+)\" numelements=\"(?<n>[1-9][0-9]*)\">(?<body>[-0-9 \\r\\n\\t]+)</hkparam>");
            if (!found.Success) { none++; continue; }

            string body = found.Groups["body"].Value;
            string edited = xml.Remove(found.Index, found.Length)
                               .Insert(found.Index,
                                       $"<hkparam name=\"{found.Groups["field"].Value}\" " +
                                       $"numelements=\"{int.Parse(found.Groups["n"].Value) + 1}\">" +
                                       $"{body} 7</hkparam>");

            byte[] after;
            try
            {
                var plan = NativeSave.Compare(xml, edited);
                if (!plan.Possible)
                {
                    refused++;
                    refusals[plan.Refusal!] = refusals.GetValueOrDefault(plan.Refusal!) + 1;
                    if (notes.Count < 10)
                        notes.Add($"{Path.GetFileName(file)}: {found.Groups["field"].Value} -> {plan.Refusal}");
                    continue;
                }

                after = NativeSave.Apply(file, plan);
            }
            catch (Exception e)
            {
                refused++;
                string why = e.Message.Split('\n')[0];
                refusals[why] = refusals.GetValueOrDefault(why) + 1;
                continue;
            }

            string back;
            try
            {
                var image = PackfileImage.Read(after);
                back = NativeXml.From(new PackfileObjects(image), image);
            }
            catch (Exception e)
            {
                wrong++;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: will not read back, {e.Message}");
                continue;
            }

            int want = int.Parse(found.Groups["n"].Value) + 1;
            var now = System.Text.RegularExpressions.Regex.Match(
                back, $"<hkparam name=\"{found.Groups["field"].Value}\" numelements=\"(?<n>[0-9]+)\">(?<body>[^<]*)</hkparam>");

            if (!now.Success || int.Parse(now.Groups["n"].Value) != want)
            {
                wrong++;
                if (notes.Count < 10)
                    notes.Add($"{Path.GetFileName(file)}: {found.Groups["field"].Value} came back " +
                              $"{(now.Success ? now.Groups["n"].Value : "missing")}, expected {want}");
                continue;
            }

            var read = now.Groups["body"].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        var wasNumbers = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (read.Length != want || read[^1] != "7" ||
                !read.Take(wasNumbers.Length).SequenceEqual(wasNumbers))
            {
                wrong++;
                if (notes.Count < 10)
                    notes.Add($"{Path.GetFileName(file)}: {found.Groups["field"].Value} does not read " +
                              "back as the numbers that went in");
                continue;
            }

            saved++;
        }

        foreach (string note in notes) Console.WriteLine("  " + note);

        if (refusals.Count > 0)
        {
            Console.WriteLine("\nrefused because:");
            foreach (var (why, count) in refusals.OrderByDescending(r => r.Value))
                Console.WriteLine($"  {count,5}  {why}");
        }

        Console.WriteLine($"\n{files.Length} file(s): {saved} saved with an array of numbers longer, " +
                          $"{wrong} came back wrong, {refused} refused, " +
                          $"{none} with no array of numbers in them");
        return wrong == 0 && refused == 0 ? 0 : 1;
    }

    private static int SaveWide(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        int saved = 0, refused = 0, wrong = 0, none = 0;
        var refusals = new Dictionary<string, int>(StringComparer.Ordinal);
        var notes = new List<string>();

        foreach (string file in files)
        {
            string xml;
            long wasLength;
            try
            {
                var image = PackfileImage.Read(file);
                xml = NativeXml.From(new PackfileObjects(image), image);
                wasLength = new FileInfo(file).Length;
            }
            catch (Exception e)
            {
                refused++;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: {e.Message}");
                continue;
            }

            var found = System.Text.RegularExpressions.Regex.Match(
                xml, "<hkparam name=\"(?<field>[A-Za-z0-9_]+)\">\\((?<body>[-0-9.e ]+)\\)</hkparam>");
            if (!found.Success) { none++; continue; }

            const string Wanted = "(1.5 -2.25 3.75 0.5)";
            string edited = xml.Remove(found.Index, found.Length)
                               .Insert(found.Index,
                                       $"<hkparam name=\"{found.Groups["field"].Value}\">{Wanted}</hkparam>");

            byte[] after;
            try
            {
                var plan = NativeSave.Compare(xml, edited);
                if (!plan.Possible)
                {
                    refused++;
                    refusals[plan.Refusal!] = refusals.GetValueOrDefault(plan.Refusal!) + 1;
                    if (notes.Count < 10)
                        notes.Add($"{Path.GetFileName(file)}: {found.Groups["field"].Value} -> {plan.Refusal}");
                    continue;
                }

                after = NativeSave.Apply(file, plan);
            }
            catch (Exception e)
            {
                refused++;
                string why = e.Message.Split('\n')[0];
                refusals[why] = refusals.GetValueOrDefault(why) + 1;
                continue;
            }

            if (after.Length != wasLength)
            {
                wrong++;
                if (notes.Count < 10)
                    notes.Add($"{Path.GetFileName(file)}: {wasLength} bytes in, {after.Length} out, " +
                              "and a fixed width field should move nothing");
                continue;
            }

            string back;
            try
            {
                var image = PackfileImage.Read(after);
                back = NativeXml.From(new PackfileObjects(image), image);
            }
            catch (Exception e)
            {
                wrong++;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: will not read back, {e.Message}");
                continue;
            }

            if (!back.Contains(Wanted, StringComparison.Ordinal))
            {
                wrong++;
                if (notes.Count < 10)
                    notes.Add($"{Path.GetFileName(file)}: {found.Groups["field"].Value} does not read " +
                              $"back as {Wanted}");
                continue;
            }

            saved++;
        }

        foreach (string note in notes) Console.WriteLine("  " + note);

        if (refusals.Count > 0)
        {
            Console.WriteLine("\nrefused because:");
            foreach (var (why, count) in refusals.OrderByDescending(r => r.Value))
                Console.WriteLine($"  {count,5}  {why}");
        }

        Console.WriteLine($"\n{files.Length} file(s): {saved} saved with a vector changed, " +
                          $"{wrong} came back wrong, {refused} refused, {none} with no vector in them");
        return wrong == 0 && refused == 0 ? 0 : 1;
    }

    private static int SaveEvent(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        const string Added = "symrm_added_event";
        int saved = 0, refused = 0, wrong = 0, noStringData = 0;
        var refusals = new Dictionary<string, int>(StringComparer.Ordinal);
        var notes = new List<string>();

        foreach (string file in files)
        {
            string xml;
            List<string?> was;
            try
            {
                var image = PackfileImage.Read(file);
                var objects = new PackfileObjects(image);
                xml = NativeXml.From(objects, image);

                var holder = objects.Instances.FirstOrDefault(i => i.ClassName == "hkbBehaviorGraphStringData");
                was = holder == null ? new List<string?>()
                                     : (objects.ReadStringArray(holder, "eventNames") ?? new List<string?>()).ToList();
            }
            catch (Exception e)
            {
                refused++;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: {e.Message}");
                continue;
            }

            if (HkxTextEdit.IdsOfClass(xml, "hkbBehaviorGraphStringData").Count == 0) { noStringData++; continue; }

            byte[] after;
            string edited = "";
            try
            {
                edited = SymbolEditor.AddEvent(xml, Added, out _);
                var plan = NativeSave.Compare(xml, edited);

                if (!plan.Possible)
                {
                    refused++;
                    refusals[plan.Refusal!] = refusals.GetValueOrDefault(plan.Refusal!) + 1;
                    continue;
                }

                after = NativeSave.Apply(file, plan);
            }
            catch (Exception e)
            {
                refused++;
                string why = e.Message.Split('\n')[0];
                refusals[why] = refusals.GetValueOrDefault(why) + 1;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: {why}");
                continue;
            }

            string trouble = Named(after, edited, was, Added);
            if (trouble.Length > 0)
            {
                wrong++;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: {trouble}");
                continue;
            }

            saved++;
            if (files.Length == 1 && argv.Length > 2)
            {
                File.WriteAllBytes(argv[2], after);
                Console.WriteLine($"  wrote {argv[2]}");
            }
        }

        foreach (string note in notes) Console.WriteLine("  " + note);

        if (refusals.Count > 0)
        {
            Console.WriteLine("\nrefused because:");
            foreach (var (why, count) in refusals.OrderByDescending(r => r.Value))
                Console.WriteLine($"  {count,5}  {why}");
        }

        Console.WriteLine($"\n{files.Length} file(s): {saved} saved with an event declared, " +
                          $"{wrong} came back wrong, {refused} refused, " +
                          $"{noStringData} with nowhere to declare one");
        return wrong == 0 && refused == 0 ? 0 : 1;
    }

    private static string Named(byte[] after, string edited, List<string?> was, string added)
    {
        PackfileImage image;
        PackfileObjects objects;
        try
        {
            image = PackfileImage.Read(after);
            objects = new PackfileObjects(image);
        }
        catch (Exception e) { return "will not read back: " + e.Message; }

        var holder = objects.Instances.FirstOrDefault(i => i.ClassName == "hkbBehaviorGraphStringData");
        if (holder == null) return "the string data object is gone";

        var names = objects.ReadStringArray(holder, "eventNames");
        if (names == null) return "eventNames cannot be read back";

        var wanted = System.Xml.Linq.XDocument.Parse(edited).Descendants("hkobject")
            .Where(o => o.Attribute("class")?.Value == "hkbBehaviorGraphStringData")
            .SelectMany(o => o.Elements("hkparam")
                              .Where(p => p.Attribute("name")?.Value == "eventNames")
                              .SelectMany(p => p.Elements("hkcstring")))
            .Select(t => t.Value).ToList();

        if (names.Count != wanted.Count)
            return $"eventNames is {names.Count} long, the document says {wanted.Count}";

        for (int e = 0; e < wanted.Count; e++)
            if (names[e] != wanted[e])
                return $"eventNames[{e}] reads '{names[e]}', the document says '{wanted[e]}'";

        if (names.Count == 0 || names[^1] != added)
            return "the name added is not the last one in the array";

        for (int e = 0; e < was.Count; e++)
            if (names[e] != was[e])
                return $"eventNames[{e}] was '{was[e]}' in the file and reads '{names[e]}' now";

        var data = image.Section("__data__")!;
        var items = PackfileLayout.Of(image);
        if (items == null) return "the walk cannot account for the result";
        if (!PackfileLayout.Reaches(items, data, image.Sections.IndexOf(data)))
            return "the result has a pointer aiming outside everything written";

        return "";
    }

    private static int ClassCheck(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string path = Path.GetFullPath(argv[1]);
        if (!File.Exists(path)) { Console.WriteLine($"no dump at {path}"); return 1; }

        var types = HavokClassTypes.Shipped;

        var head = new System.Text.RegularExpressions.Regex(@"^class (?<name>\S+) : (?<parent>\S+)\s+size=(?<size>\d+)\s+members=(?<count>\d+)");
        var member = new System.Text.RegularExpressions.Regex(@"^\s+\+(?<at>\d+)\s+(?<name>\S+)\s+(?<type>.*?)\s*$");

        string current = "";
        var fromGame = new Dictionary<string, List<(int At, string Name, string Type)>>(StringComparer.Ordinal);
        var sizes = new Dictionary<string, int>(StringComparer.Ordinal);
        var parents = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string line in File.ReadLines(path))
        {
            var isHead = head.Match(line);
            if (isHead.Success)
            {
                current = isHead.Groups["name"].Value;
                sizes[current] = int.Parse(isHead.Groups["size"].Value, System.Globalization.CultureInfo.InvariantCulture);
                fromGame[current] = new List<(int, string, string)>();

                string parent = isHead.Groups["parent"].Value;
                if (parent != "-")
                    parents[current] = parent.EndsWith("Class", StringComparison.Ordinal)
                                       ? parent[..^"Class".Length] : parent;
                continue;
            }

            var isMember = member.Match(line);
            if (!isMember.Success || current.Length == 0) continue;

            fromGame[current].Add((int.Parse(isMember.Groups["at"].Value, System.Globalization.CultureInfo.InvariantCulture),
                                   isMember.Groups["name"].Value, isMember.Groups["type"].Value));
        }

        List<(int At, string Name, string Type)> Whole(string className)
        {
            var chain = new List<string>();
            for (string? at = className; at != null && fromGame.ContainsKey(at);
                 at = parents.TryGetValue(at, out string? up) ? up : null)
                chain.Add(at);
            chain.Reverse();

            var all = new List<(int, string, string)>();
            foreach (string link in chain) all.AddRange(fromGame[link]);
            return all;
        }

        int shared = 0, sizeAgreed = 0, sizeDiffered = 0;
        int membersChecked = 0, offsetAgreed = 0, offsetDiffered = 0, missingFromGame = 0;
        int onlyInDump = 0, onlyHere = 0;
        var notes = new List<string>();

        foreach (string name in types.Names.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (!fromGame.ContainsKey(name)) { onlyHere++; continue; }
            var theirs = Whole(name);
            shared++;

            if (types[name]?.Size is int size)
            {
                if (size == sizes[name]) sizeAgreed++;
                else
                {
                    sizeDiffered++;
                    if (notes.Count < 20)
                        notes.Add($"{name} is {size} bytes here and {sizes[name]} in the game");
                }
            }

            int at = 0;
            foreach (var mine in types.Members(name))
            {
                membersChecked++;

                int found = -1;
                for (int k = at; k < theirs.Count; k++)
                    if (theirs[k].Name == mine.Name) { found = k; break; }

                if (found < 0)
                {
                    missingFromGame++;
                    if (notes.Count < 20)
                        notes.Add($"{name}.{mine.Name} is not a member the game declares after this point");
                    continue;
                }

                at = found + 1;

                if (theirs[found].At == mine.Offset) offsetAgreed++;
                else
                {
                    offsetDiffered++;
                    if (notes.Count < 20)
                        notes.Add($"{name}.{mine.Name} is at +{mine.Offset} here and " +
                                  $"+{theirs[found].At} in the game");
                }
            }
        }

        onlyInDump = fromGame.Keys.Count(k => !types.Knows(k));

        foreach (string note in notes) Console.WriteLine("  " + note);

        Console.WriteLine($"\n{fromGame.Count} class(es) in the game's dump, {types.Names.Count()} in this " +
                          $"build's table, {shared} in both");
        Console.WriteLine($"size: {sizeAgreed} agree, {sizeDiffered} do not");
        Console.WriteLine($"members: {membersChecked} checked, {offsetAgreed} at the same offset, " +
                          $"{offsetDiffered} not, {missingFromGame} the game does not declare");
        Console.WriteLine($"coverage: {onlyInDump} class(es) only the game has, {onlyHere} only this build has");

        var unvalidated = types.Names.Where(n => !fromGame.ContainsKey(n))
                               .OrderBy(n => n, StringComparer.Ordinal).ToList();
        if (unvalidated.Count > 0)
            Console.WriteLine("only this build has: " + string.Join(", ", unvalidated));
        return sizeDiffered == 0 && offsetDiffered == 0 && missingFromGame == 0 ? 0 : 1;
    }

    private static int SaveDelete(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        int saved = 0, refused = 0, wrong = 0, nothingToDelete = 0;
        long objectsGone = 0;
        var refusals = new Dictionary<string, int>(StringComparer.Ordinal);
        var notes = new List<string>();

        foreach (string file in files)
        {
            string xml;
            PackfileObjects objects;
            try
            {
                var image = PackfileImage.Read(file);
                objects = new PackfileObjects(image);
                xml = NativeXml.From(objects, image);
            }
            catch (Exception e)
            {
                refused++;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: {e.Message}");
                continue;
            }

            int wasCount = objects.Instances.Count;
            int last = -1;
            for (int k = objects.Instances.Count - 1; k >= 0; k--)
                if (GraphAuthor.CanDelete(objects.Instances[k].ClassName)) { last = k; break; }

            if (last < 0) { nothingToDelete++; continue; }

            string id = (NativeGraphModel.FirstId + last).ToString();

            string edited;
            NativeSave.Plan plan;
            byte[] after;
            try
            {
                edited = GraphAuthor.DeleteNode(xml, id, out _);
                plan = NativeSave.Compare(xml, edited);

                if (!plan.Possible)
                {
                    refused++;
                    refusals[plan.Refusal!] = refusals.GetValueOrDefault(plan.Refusal!) + 1;
                    continue;
                }

                if (Environment.GetEnvironmentVariable("SYMRM_EVENT_DUMP") == "1")
                {
                    foreach (var c in plan.Changes.Where(c => c.Array && c.Text))
                        Console.WriteLine($"  change {c.ClassName}[{c.Index}].{c.Field} -> " +
                                          $"{c.Value.Split('\0').Length} element(s)");
                    Console.WriteLine($"  string data objects: " +
                        System.Xml.Linq.XDocument.Parse(edited).Descendants("hkobject")
                          .Count(o => o.Attribute("class")?.Value == "hkbBehaviorGraphStringData"));
                }

                after = NativeSave.Apply(file, plan);
            }
            catch (Exception e)
            {
                refused++;
                string why = e.Message.Split('\n')[0];
                refusals[why] = refusals.GetValueOrDefault(why) + 1;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: {why}");
                continue;
            }

            string trouble = Sound(after, wasCount - plan.Gone.Count);
            if (trouble.Length > 0)
            {
                wrong++;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: {trouble}");
                continue;
            }

            saved++;
            objectsGone += plan.Gone.Count;

            if (files.Length == 1 && argv.Length > 2)
            {
                File.WriteAllBytes(argv[2], after);
                Console.WriteLine($"  wrote {argv[2]}, {plan.Gone.Count} object(s) fewer");
            }
        }

        foreach (string note in notes) Console.WriteLine("  " + note);

        if (refusals.Count > 0)
        {
            Console.WriteLine("\nrefused because:");
            foreach (var (why, count) in refusals.OrderByDescending(r => r.Value))
                Console.WriteLine($"  {count,5}  {why}");
        }

        Console.WriteLine($"\n{files.Length} file(s): {saved} saved with a node deleted, " +
                          $"{wrong} came back wrong, {refused} refused, " +
                          $"{nothingToDelete} with nothing the author will delete");
        Console.WriteLine($"{objectsGone} object(s) taken out across them");
        return wrong == 0 ? 0 : 1;
    }

    private static string Sound(byte[] after, int expected)
    {
        PackfileImage image;
        PackfileObjects objects;
        try
        {
            image = PackfileImage.Read(after);
            objects = new PackfileObjects(image);
        }
        catch (Exception e) { return "will not read back: " + e.Message; }

        if (objects.Instances.Count != expected)
            return $"holds {objects.Instances.Count} object(s), expected {expected}";

        var data = image.Section("__data__")!;
        var items = PackfileLayout.Of(image);
        if (items == null) return "the walk cannot account for the result";

        if (!PackfileLayout.Accounted(items, data.Data.Length))
            return "the result has bytes in it nothing accounts for";

        if (!PackfileLayout.Reaches(items, data, image.Sections.IndexOf(data)))
            return "the result has a pointer aiming outside everything written";

        return "";
    }

    private static int DeleteObject(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        int deleted = 0, refused = 0, wrong = 0;
        long bytesSaved = 0;
        var notes = new List<string>();

        foreach (string file in files)
        {
            PackfileImage image;
            PackfileObjects objects;
            try
            {
                image = PackfileImage.Read(file);
                objects = new PackfileObjects(image);
            }
            catch (Exception e)
            {
                refused++;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: {e.Message}");
                continue;
            }

            if (objects.Instances.Count < 2) { refused++; continue; }

            int id = argv.Length > 2 && int.TryParse(argv[2], out int asked)
                     ? asked
                     : NativeGraphModel.FirstId + objects.Instances.Count - 1;

            int index = id - NativeGraphModel.FirstId;
            if (index < 0 || index >= objects.Instances.Count) { refused++; continue; }

            string className = objects.Instances[index].ClassName;
            int wasCount = objects.Instances.Count;
            var wasClasses = objects.Instances.Select(i => i.ClassName).ToList();
            int wasLength = image.Section("__data__")!.Data.Length;

            byte[] after;
            try
            {
                NativeRemove.Orphan(image, id);
                NativeRemove.Delete(image, new[] { id });
                after = image.Rebuild();
            }
            catch (Exception e)
            {
                refused++;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: {e.Message}");
                continue;
            }

            string why = Wrong(after, wasCount, wasClasses, className);
            if (why.Length > 0)
            {
                wrong++;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: {why}");
                continue;
            }

            deleted++;
            bytesSaved += wasLength - PackfileImage.Read(after).Section("__data__")!.Data.Length;
        }

        foreach (string note in notes) Console.WriteLine("  " + note);

        Console.WriteLine($"\n{files.Length} file(s): {deleted} had an object taken out cleanly, " +
                          $"{wrong} came back wrong, {refused} refused");
        if (deleted > 0) Console.WriteLine($"{bytesSaved} byte(s) of data section reclaimed across them");
        return wrong == 0 ? 0 : 1;
    }

    private static string Wrong(byte[] after, int wasCount, List<string> wasClasses, string className)
    {
        PackfileImage image;
        PackfileObjects objects;
        try
        {
            image = PackfileImage.Read(after);
            objects = new PackfileObjects(image);
        }
        catch (Exception e) { return "will not read back: " + e.Message; }

        if (objects.Instances.Count != wasCount - 1)
            return $"holds {objects.Instances.Count} object(s), expected {wasCount - 1}";

        var left = objects.Instances.Select(i => i.ClassName).ToList();
        var expected = new List<string>(wasClasses);
        expected.Remove(className);
        if (!left.SequenceEqual(expected))
            return "the objects left are not the objects that were there minus the one deleted";

        var data = image.Section("__data__")!;
        var items = PackfileLayout.Of(image);
        if (items == null) return "the walk cannot account for the result";
        if (!PackfileLayout.Accounted(items, data.Data.Length))
            return "the result has bytes in it nothing accounts for";

        var spans = items.Select(i => (i.At, End: i.At + i.Length)).OrderBy(x => x.At).ToList();
        bool Lands(int offset) => spans.Exists(sp => offset >= sp.At && offset < sp.End);

        int section = image.Sections.IndexOf(data);
        foreach (var (source, destination) in data.Locals())
            if (!Lands(source) || !Lands(destination))
                return $"a local pointer at 0x{source:x} aims at 0x{destination:x}, outside everything";

        foreach (var (source, which, destination) in data.Globals())
            if (!Lands(source) || (which == section && !Lands(destination)))
                return $"a pointer at 0x{source:x} aims at 0x{destination:x}, outside everything";

        foreach (var (source, _, _) in data.Virtuals())
            if (!Lands(source)) return $"an object is listed at 0x{source:x}, which is not written";

        return "";
    }

    private static int Conditions(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        var conditions = new Dictionary<string, int>(StringComparer.Ordinal);
        var expressions = new Dictionary<string, int>(StringComparer.Ordinal);
        int filesWithConditions = 0, filesWithExpressions = 0, unread = 0;
        int parsedOk = 0, unparsed = 0, undecided = 0, trueAtStart = 0, falseAtStart = 0;
        int expressionModifiers = 0, filesWithExpressionModifiers = 0;
        int expressionParsed = 0, expressionEvaluated = 0, expressionUnsupported = 0, expressionEventRecords = 0;
        int driven = 0, flipped = 0, reachedFromState = 0, sweepEnters = 0, falseNow = 0;
        var problems = new List<string>();
        var assignments = new List<string>();
        var undeclared = new List<string>();
        var stuck = new List<string>();
        var expressionRefusals = new List<string>();
        var eventPredicates = new List<string>();

        foreach (string file in files)
        {
            PackfileObjects objects;
            try { objects = new PackfileObjects(PackfileImage.Read(file)); }
            catch (Exception) { unread++; continue; }

            int here = 0, there = 0;

            var declared = VariableTable(objects);
            var expressionValues = new Dictionary<string, double>(declared, StringComparer.Ordinal);
            int modifierCount = objects.OfClass("hkbEvaluateExpressionModifier").Count();
            expressionModifiers += modifierCount;
            if (modifierCount > 0) filesWithExpressionModifiers++;

            foreach (var instance in objects.OfClass("hkbExpressionCondition"))
            {
                string text = objects.ReadString(instance, "expression") ?? "";
                conditions[text] = conditions.GetValueOrDefault(text) + 1;
                here++;

                var parsed = Expression.Parse(text);
                if (!parsed.Ok)
                {
                    unparsed++;
                    problems.Add($"{Path.GetFileName(file)}: \"{text}\" {parsed.Problem}");
                    continue;
                }

                parsedOk++;
                if (parsed.IsAssignment) assignments.Add($"{Path.GetFileName(file)}: {text}");

                foreach (string name in parsed.Names)
                    if (!declared.ContainsKey(name))
                        undeclared.Add($"{Path.GetFileName(file)}: \"{text}\" names {name}, which this file does not declare");

                var verdict = Expression.Evaluate(parsed, n => declared.TryGetValue(n, out double v) ? v : null);
                if (verdict == Expression.Verdict.Unknown) undecided++;
                else if (verdict == Expression.Verdict.True) trueAtStart++;
                else falseAtStart++;
            }

            foreach (var instance in objects.OfClass("hkbExpressionDataArray"))
            {
                var array = objects.ReadArray(instance, "expressionsData");
                int stride = HavokClassTypes.Shipped["hkbExpressionData"]?.Size ?? 0;
                if (array == null || stride <= 0) continue;

                for (int e = 0; e < array.Count; e++)
                {
                    string text = objects.ReadStringAt(array.At + e * stride) ?? "";
                    expressions[text] = expressions.GetValueOrDefault(text) + 1;
                    there++;

                    if (text.Contains(" if ", StringComparison.Ordinal))
                    {
                        expressionEventRecords++;
                        eventPredicates.Add($"{Path.GetFileName(file)}: \"{text}\"");
                        continue;
                    }
                    var parsed = Expression.Parse(text);
                    if (!parsed.Ok)
                    {
                        expressionUnsupported++;
                        expressionRefusals.Add($"{Path.GetFileName(file)}: \"{text}\" " +
                            parsed.Problem);
                        continue;
                    }
                    if (parsed.Root is not Expression.Node.Assign assignment)
                    {
                        expressionUnsupported++;
                        expressionRefusals.Add($"{Path.GetFileName(file)}: \"{text}\" does not assign a runtime variable");
                        continue;
                    }
                    if (!expressionValues.ContainsKey(assignment.Variable))
                    {
                        expressionUnsupported++;
                        expressionRefusals.Add($"{Path.GetFileName(file)}: \"{text}\" does not assign a declared variable");
                        continue;
                    }

                    expressionParsed++;
                    var value = Expression.EvaluateNumber(parsed,
                        name => expressionValues.TryGetValue(name, out double number) ? number : null);
                    if (!value.Possible)
                    {
                        expressionUnsupported++;
                        expressionRefusals.Add($"{Path.GetFileName(file)}: \"{text}\" {value.Refusal}");
                        continue;
                    }

                    expressionEvaluated++;
                    expressionValues[assignment.Variable] = value.Value!.Value;
                }
            }

            if (here > 0) filesWithConditions++;
            if (there > 0) filesWithExpressions++;
        }

        Console.WriteLine($"{files.Length} file(s), {unread} unread");
        Console.WriteLine($"transition conditions: {conditions.Values.Sum()} in {filesWithConditions} file(s), " +
                          $"{conditions.Count} distinct");
        foreach (var (text, count) in conditions.OrderByDescending(c => c.Value))
            Console.WriteLine($"  {count,5}  {text}");

        foreach (string file in files)
        {
            BehaviourGraphModel? model;
            try { model = NativeGraphModel.From(new PackfileObjects(PackfileImage.Read(InputFilePolicy.ReadHkx(file)))); }
            catch (Exception) { continue; }
            if (model == null) continue;

            var run = GraphRun.Start(model);
            if (run.RootId.Length == 0) continue;

            var withCondition = run.Conditions();
            if (withCondition.Count == 0) continue;

            var reached = run.Reachable().Reachable.ToHashSet(StringComparer.Ordinal);
            var landed = StepEverywhere(model, out _, out _);

            int analysisReaches = withCondition.Count(c => reached.Contains(c.Route.FromId));
            int stepReaches = withCondition.Count(c => landed.Contains(c.Route.FromId));
            int wildcard = withCondition.Count(c => c.Route.Wildcard);
            int wouldHold = withCondition.Count(c => c.Verdict == Expression.Verdict.False);

            reachedFromState += analysisReaches;
            sweepEnters += stepReaches;
            falseNow += wouldHold;

            foreach (var (route, condition, _) in withCondition)
            {
                var parsed = Expression.Parse(condition);
                if (!parsed.Ok || parsed.IsAssignment || parsed.Names.Count == 0) continue;
                if (parsed.Names.Any(n => run.ValueOf(n) == null)) continue;

                driven++;
                var seen = new HashSet<Expression.Verdict>();
                var restore = parsed.Names.ToDictionary(n => n, n => run.ValueOf(n)!.Value);

                foreach (double a in Spread)
                foreach (double b in parsed.Names.Count > 1 ? Spread : new double[] { 0 })
                {
                    run.Set(parsed.Names[0], a);
                    if (parsed.Names.Count > 1) run.Set(parsed.Names[1], b);
                    seen.Add(run.Test(condition));
                }

                foreach (var (name, was) in restore) run.Set(name, was);

                if (seen.Contains(Expression.Verdict.True) && seen.Contains(Expression.Verdict.False))
                    { flipped++; continue; }

                stuck.Add($"{Path.GetFileName(file)}: \"{condition}\" on #{route.FromId} never changes " +
                          $"its mind whatever its variables hold, it is always {string.Join("/", seen)}");
            }

            Console.WriteLine($"  {Path.GetFileName(file)}: {withCondition.Count} conditional transition(s), " +
                              $"{wildcard} of them wildcards, {analysisReaches} leave a state the analysis " +
                              $"reaches, {stepReaches} leave a state the sweep enters, {wouldHold} are false " +
                              "at the starting values");
        }

        Console.WriteLine($"\nread: {parsedOk} parsed, {unparsed} did not");
        Console.WriteLine($"answered against each file's own starting values: {trueAtStart} true, " +
                          $"{falseAtStart} false, {undecided} undecided, which is a transition that " +
                          "still fires because nothing here can say it should not");

        Console.WriteLine($"driven through a spread of values: {driven} condition(s) on transitions, " +
                          $"{flipped} change their answer as their variables change, {driven - flipped} do not");
        Console.WriteLine($"conditional transitions: {reachedFromState} leave a state the analysis reaches, " +
                          $"{sweepEnters} leave one the sweep enters, {falseNow} are false at the starting values");

        foreach (string one in stuck) Console.WriteLine("  " + one);
        foreach (string problem in problems) Console.WriteLine("  cannot read: " + problem);
        foreach (string one in assignments)
            Console.WriteLine("  an assignment where a test was expected, so it stays undecided: " + one);
        foreach (string one in undeclared) Console.WriteLine("  " + one);

        Console.WriteLine($"\nexpression modifier lines: {expressions.Values.Sum()} in " +
                          $"{filesWithExpressions} file(s), {expressions.Count} distinct");
        Console.WriteLine($"  {expressionModifiers} modifier object(s) in {filesWithExpressionModifiers} file(s)");
        Console.WriteLine($"  {expressionParsed} assignment records parse; {expressionEvaluated} evaluate from " +
                          $"each file's starting values; {expressionUnsupported} assignment record(s) are safely held");
        Console.WriteLine($"  {expressionEventRecords} event-style predicate record(s) are outside variable-assignment scope");
        foreach (var (text, count) in expressions.OrderByDescending(c => c.Value).Take(15))
            Console.WriteLine($"  {count,5}  {text}");
        foreach (string refusal in expressionRefusals.Take(12)) Console.WriteLine("  cannot evaluate: " + refusal);
        if (expressionRefusals.Count > 12) Console.WriteLine($"  ... and {expressionRefusals.Count - 12} more");
        foreach (string eventClause in eventPredicates.Take(12)) Console.WriteLine("  event predicate: " + eventClause);

        return unparsed == 0 && stuck.Count == 0 ? 0 : 1;
    }

    private static readonly double[] Spread = { -1, 0, 1, 2, 3, 5, 9, 10, 18, 20, 21, 100 };

    private static Dictionary<string, double> VariableTable(PackfileObjects objects)
    {
        var table = new Dictionary<string, double>(StringComparer.Ordinal);

        var strings = objects.OfClass("hkbBehaviorGraphStringData").FirstOrDefault();
        var data = objects.OfClass("hkbBehaviorGraphData").FirstOrDefault();
        var set = objects.OfClass("hkbVariableValueSet").FirstOrDefault();
        if (strings == null || set == null) return table;

        var names = objects.ReadStringArray(strings, "variableNames");
        if (names == null) return table;

        var values = objects.ReadArray(set, "wordVariableValues");
        int stride = HavokClassTypes.Shipped["hkbVariableValue"]?.Size ?? 0;

        var infos = data == null ? null : objects.ReadArray(data, "variableInfos");
        int infoStride = HavokClassTypes.Shipped["hkbVariableInfo"]?.Size ?? 0;
        var typeMember = HavokClassTypes.Shipped.Members("hkbVariableInfo")
                                                .FirstOrDefault(m => m.Name == "type");

        for (int i = 0; i < names.Count; i++)
        {
            if (names[i] is not string name || name.Length == 0) continue;
            if (values == null || stride <= 0 || i >= values.Count) continue;

            if (objects.ReadIntAt(values.At + i * stride) is not int word) continue;

            int type = 0;
            if (infos != null && infoStride > 0 && typeMember != null && i < infos.Count)
                type = objects.ReadNarrowAt(infos.At + i * infoStride + typeMember.Offset,
                                            HavokClassTypes.Width(typeMember.VType)) ?? 0;

            table[name] = type == 2 ? BitConverter.Int32BitsToSingle(word) : word;
        }

        return table;
    }

    private static int Template(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        string[] shapes = { "hkbClipGenerator", "hkbBlenderGenerator", "hkbStateMachineStateInfo" };
        var roots = new SortedDictionary<string, int>();
        var liftable = new SortedDictionary<string, int>();
        var shares = new SortedDictionary<string, int>();
        var needsSymbols = new SortedDictionary<string, int>();
        var sizes = new SortedDictionary<string, long>();
        int read = 0;
        long symbolsUsed = 0;
        var examples = new List<string>();

        foreach (string file in files)
        {
            PackfileImage image;
            PackfileObjects objects;
            try
            {
                image = PackfileImage.Read(file);
                objects = new PackfileObjects(image, HavokClasses.Shipped);
            }
            catch (Exception) { continue; }
            read++;

            foreach (string shape in shapes)
                foreach (var instance in objects.OfClass(shape))
                {
                    int id = NativeGraphModel.FirstId + objects.IndexOf(instance);

                    NativePaste.Subtree tree;
                    try { tree = NativePaste.Of(image, id); }
                    catch (Exception) { continue; }

                    roots[shape] = roots.GetValueOrDefault(shape) + 1;

                    if (tree.Shared.Count > 0)
                    {
                        shares[shape] = shares.GetValueOrDefault(shape) + 1;
                        continue;
                    }

                    liftable[shape] = liftable.GetValueOrDefault(shape) + 1;
                    sizes[shape] = sizes.GetValueOrDefault(shape) + tree.Ids.Count;

                    int symbols = tree.Events.Count + tree.Variables.Count;
                    symbolsUsed += symbols;
                    if (symbols > 0)
                    {
                        needsSymbols[shape] = needsSymbols.GetValueOrDefault(shape) + 1;
                        if (examples.Count < 8)
                            examples.Add($"{Path.GetFileName(file)} #{id} {shape}: {tree.Ids.Count} object(s), " +
                                         $"needs {string.Join(", ", tree.Events.Concat(tree.Variables).Take(4))}");
                    }
                }
        }

        Console.WriteLine($"\n{read} file(s) read");
        Console.WriteLine($"{"shape",-28} {"roots",8} {"liftable",9} {"shares",8} {"needs symbols",14} {"mean size",10}");
        foreach (string shape in shapes)
        {
            int all = roots.GetValueOrDefault(shape);
            int can = liftable.GetValueOrDefault(shape);
            Console.WriteLine($"{shape,-28} {all,8} {can,9} {shares.GetValueOrDefault(shape),8} " +
                              $"{needsSymbols.GetValueOrDefault(shape),14} " +
                              $"{(can > 0 ? (double)sizes.GetValueOrDefault(shape) / can : 0),10:F1}");
        }
        Console.WriteLine($"symbol uses across every liftable subtree: {symbolsUsed}");
        foreach (string line in examples) Console.WriteLine($"  {line}");

        Console.WriteLine();
        int lifted = 0, applied = 0, wrong = 0, refused = 0;
        int sharing = 0, properlyRefused = 0, wronglyKept = 0;
        var complaints = new List<string>();

        string folder = Path.Combine(Path.GetTempPath(), "symrm-template-gate");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
        TemplateStore.Folder = folder;

        string work = Path.Combine(folder, "work");
        Directory.CreateDirectory(work);

        int nth = argv.Length > 2 && int.TryParse(argv[2], out int n) && n > 0 ? n : 37;
        int seen = 0;

        foreach (string file in files)
        {
            PackfileImage image;
            PackfileObjects objects;
            try
            {
                image = PackfileImage.Read(file);
                objects = new PackfileObjects(image, HavokClasses.Shipped);
            }
            catch (Exception) { continue; }

            foreach (string shape in shapes)
                foreach (var instance in objects.OfClass(shape))
                {
                    int id = NativeGraphModel.FirstId + objects.IndexOf(instance);

                    NativePaste.Subtree tree;
                    try { tree = NativePaste.Of(image, id); }
                    catch (Exception) { continue; }

                    if (tree.Shared.Count > 0)
                    {
                        if (sharing++ % nth != 0) continue;
                        try
                        {
                            TemplateStore.Lift(file, id, $"s{sharing}");
                            wronglyKept++;
                            TemplateStore.Remove(TemplateStore.Slug($"s{sharing}"));
                            if (complaints.Count < 10)
                                complaints.Add($"{Path.GetFileName(file)} #{id}: shares " +
                                               $"{tree.Shared.Count} object(s) and was lifted anyway");
                        }
                        catch (InvalidOperationException) { properlyRefused++; }
                        continue;
                    }

                    if (seen++ % nth != 0) continue;

                    string slug;
                    try
                    {
                        slug = TemplateStore.Lift(file, id, $"t{seen}").Slug;
                        lifted++;
                    }
                    catch (Exception e)
                    {
                        refused++;
                        if (complaints.Count < 10)
                            complaints.Add($"{Path.GetFileName(file)} #{id}: lift refused, {e.Message.Split('\n')[0]}");
                        continue;
                    }

                    string into = Path.Combine(work, $"t{seen}.hkx");
                    File.Copy(file, into, overwrite: true);

                    int before = objects.Instances.Count;
                    try
                    {
                        var result = TemplateStore.Apply(TemplateStore.Get(slug)!, into);
                        var after = new PackfileObjects(PackfileImage.Read(result.Bytes), HavokClasses.Shipped);

                        if (after.Instances.Count != before + tree.Ids.Count)
                        {
                            wrong++;
                            if (complaints.Count < 10)
                                complaints.Add($"{Path.GetFileName(file)} #{id}: expected " +
                                               $"{before + tree.Ids.Count} object(s), got {after.Instances.Count}");
                        }
                        else if (result.RootId != NativeGraphModel.FirstId + before)
                        {
                            wrong++;
                            if (complaints.Count < 10)
                                complaints.Add($"{Path.GetFileName(file)} #{id}: applied root came back as " +
                                               $"#{result.RootId} rather than #{NativeGraphModel.FirstId + before}");
                        }
                        else
                        {
                            applied++;
                        }
                    }
                    catch (Exception e)
                    {
                        refused++;
                        if (complaints.Count < 10)
                            complaints.Add($"{Path.GetFileName(file)} #{id}: apply refused, {e.Message.Split('\n')[0]}");
                    }
                    finally
                    {
                        TemplateStore.Remove(slug);
                        if (File.Exists(into)) File.Delete(into);
                    }
                }
        }

        Directory.Delete(folder, true);

        Console.WriteLine($"lifted and applied every {nth}th liftable shape: {lifted} kept, {applied} " +
                          $"went into a different file correctly, {wrong} came back wrong, {refused} refused");
        Console.WriteLine($"every {nth}th shape that shares an object: {properlyRefused} refused as they " +
                          $"must be, {wronglyKept} lifted anyway");
        foreach (string line in complaints) Console.WriteLine($"  {line}");

        return wrong + refused + wronglyKept == 0 ? 0 : 1;
    }

    private static int Paste(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        int pasted = 0, wrong = 0, nothing = 0, copiedObjects = 0, rewritten = 0, sharedKept = 0;
        int across = 0, acrossRefused = 0, withinRefused = 0, undone = 0, attached = 0;
        var notes = new List<string>();
        var refusals = new Dictionary<string, int>(StringComparer.Ordinal);
        var chosen = new Dictionary<string, (int Root, int Size)>(StringComparer.Ordinal);

        foreach (string file in files)
        {
            NativePaste.Subtree? tree;
            PackfileImage image;
            try
            {
                image = PackfileImage.Read(file);
                tree = BiggestSubtree(image);
            }
            catch (Exception e)
            {
                withinRefused++;
                refusals[Kind(e.Message)] = refusals.GetValueOrDefault(Kind(e.Message)) + 1;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: {e.Message}");
                continue;
            }

            if (tree == null) { nothing++; continue; }
            chosen[file] = (tree.RootId, tree.Ids.Count);

            var before = new PackfileObjects(image);
            var wasClasses = before.Instances.Select(i => i.ClassName).ToList();
            var ownedOffsets = tree.Ids.Select(id => before.Instances[id - NativeGraphModel.FirstId].Offset)
                                       .ToHashSet();
            var sharedOffsets = tree.Shared.Select(id => before.Instances[id - NativeGraphModel.FirstId].Offset)
                                           .ToHashSet();
            string was = Shape(image, before, tree.RootId);

            byte[] after;
            NativePaste.Result result;
            try
            {
                result = NativePaste.Into(image, image, tree, sameFile: true);
                after = image.Rebuild();
            }
            catch (Exception e)
            {
                withinRefused++;
                refusals[Kind(e.Message)] = refusals.GetValueOrDefault(Kind(e.Message)) + 1;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: {e.Message}");
                continue;
            }

            var copiedClasses = tree.Ids
                .Select(id => wasClasses[id - NativeGraphModel.FirstId]).ToList();

            string why = PasteWrong(after, wasClasses, copiedClasses, tree, ownedOffsets,
                                    sharedOffsets, was, result);
            if (why.Length > 0)
            {
                wrong++;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: {why}");
                continue;
            }

            pasted++;
            copiedObjects += tree.Ids.Count;
            rewritten += result.Pointers;
            sharedKept += tree.Shared.Count;

            try
            {
                var undo = PackfileImage.Read(after);
                var added = Enumerable.Range(0, tree.Ids.Count)
                                      .Select(k => NativeGraphModel.FirstId + wasClasses.Count + k)
                                      .ToList();
                NativeRemove.Delete(undo, added);
                var left = new PackfileObjects(PackfileImage.Read(undo.Rebuild()))
                           .Instances.Select(o => o.ClassName).ToList();

                if (left.SequenceEqual(wasClasses)) undone++;
                else
                {
                    wrong++;
                    if (notes.Count < 10)
                        notes.Add($"{Path.GetFileName(file)}: undoing the paste did not give the file back");
                }
            }
            catch (Exception e)
            {
                wrong++;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: cannot undo the paste: {e.Message}");
            }

            if (tree.RootClass != "hkbStateMachineStateInfo") continue;

            try
            {
                var again = PackfileImage.Read(file);
                var holders = new PackfileObjects(again);
                var machine = holders.OfClass("hkbStateMachine")
                    .FirstOrDefault(m => holders.ReadRefArray(m, "states")?
                                                .Any(st => st != null && st.Offset ==
                                                     holders.Instances[tree.RootId - NativeGraphModel.FirstId].Offset)
                                         == true);
                if (machine == null) continue;

                int machineId = holders.IndexOf(machine) + NativeGraphModel.FirstId;
                int held = holders.ReadArray(machine, "states")?.Count ?? 0;
                var stateIdsWere = StateIds(holders, machine);

                NativePaste.Into(again, again, NativePaste.Of(again, tree.RootId), sameFile: true,
                                 machineId, "states");
                var back = PackfileImage.Read(again.Rebuild());
                var now = new PackfileObjects(back);
                var grown = now.Instances[machineId - NativeGraphModel.FirstId];

                int count = now.ReadArray(grown, "states")?.Count ?? 0;
                var stateIdsNow = StateIds(now, grown);

                if (count != held + 1)
                    { wrong++; if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: attaching left {count} state(s) where {held + 1} were expected"); }
                else if (stateIdsNow.Distinct().Count() != stateIdsNow.Count)
                    { wrong++; if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: the attached state kept a number another state in the machine already has"); }
                else if (!stateIdsWere.All(stateIdsNow.Contains))
                    { wrong++; if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: attaching changed the number of a state that was already there"); }
                else attached++;
            }
            catch (Exception e)
            {
                wrong++;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: cannot attach: {e.Message}");
            }
        }

        for (int i = 0; i < files.Length && files.Length > 1; i++)
        {
            if (!chosen.TryGetValue(files[i], out var pick)) continue;

            string other = files[(i + 1) % files.Length];
            if (!chosen.ContainsKey(other)) continue;

            try
            {
                var into = PackfileImage.Read(other);
                var from = PackfileImage.Read(files[i]);
                var tree = NativePaste.Of(from, pick.Root);
                var fromObjects = new PackfileObjects(from);
                var copiedClasses = tree.Ids
                    .Select(id => fromObjects.Instances[id - NativeGraphModel.FirstId].ClassName).ToList();
                string was = Shape(from, fromObjects, tree.RootId);

                var wasClasses = new PackfileObjects(into).Instances.Select(o => o.ClassName).ToList();
                var result = NativePaste.Into(into, from, tree, sameFile: false);
                byte[] bytes = into.Rebuild();

                string why = PasteWrong(bytes, wasClasses, copiedClasses, tree, new HashSet<int>(),
                                        new HashSet<int>(), was, result);
                if (why.Length > 0)
                {
                    wrong++;
                    if (notes.Count < 10) notes.Add($"{Path.GetFileName(files[i])} into {Path.GetFileName(other)}: {why}");
                    continue;
                }
                across++;
            }
            catch (Exception e)
            {
                acrossRefused++;
                refusals[Kind(e.Message)] = refusals.GetValueOrDefault(Kind(e.Message)) + 1;
            }
        }

        int cycles = 0, cyclicFiles = 0;
        foreach (string file in files)
        {
            try
            {
                int found = PointerCycles(PackfileImage.Read(file));
                if (found > 0) { cycles += found; cyclicFiles++; }
            }
            catch (Exception) { }
        }

        foreach (string note in notes) Console.WriteLine("  " + note);

        Console.WriteLine($"pointer cycles among objects: {cycles} in {cyclicFiles} file(s), which " +
                          "is what the ownership rule cannot take into a copy");

        Console.WriteLine($"\nwithin a file: {files.Length} file(s), {pasted} pasted and read back " +
                          $"correctly, {wrong} came back wrong, {withinRefused} refused, " +
                          $"{nothing} hold nothing worth copying");
        Console.WriteLine($"{copiedObjects} object(s) copied, {rewritten} reference(s) rewritten, " +
                          $"{sharedKept} shared object(s) left pointing at the original");
        Console.WriteLine($"{undone} undone by deleting exactly what was pasted, " +
                          $"{attached} attached to a state machine with a number of its own");
        Console.WriteLine($"between files: {across} taken, {acrossRefused} refused");
        foreach (var (kind, count) in refusals.OrderByDescending(r => r.Value))
            Console.WriteLine($"  {count} refused for {kind}");

        return wrong == 0 ? 0 : 1;
    }

    private static int PointerCycles(PackfileImage image)
    {
        var data = image.Section("__data__");
        if (data == null) return 0;

        var objects = new PackfileObjects(image);
        var items = PackfileLayout.Of(image);
        if (items == null) return 0;

        var runs = PackfileLayout.ByObject(items);
        if (runs.Count != objects.Instances.Count) return 0;

        var spans = new List<(int At, int End, int Which)>();
        for (int i = 0; i < runs.Count; i++)
            foreach (var item in runs[i]) spans.Add((item.At, item.At + item.Length, i));
        spans.Sort((a, b) => a.At.CompareTo(b.At));

        int Owner(int offset)
        {
            int low = 0, high = spans.Count - 1;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (offset < spans[mid].At) high = mid - 1;
                else if (offset >= spans[mid].End) low = mid + 1;
                else return spans[mid].Which;
            }
            return -1;
        }

        var startsAt = new Dictionary<int, int>();
        for (int i = 0; i < objects.Instances.Count; i++) startsAt[objects.Instances[i].Offset] = i;

        int section = image.Sections.IndexOf(data);
        var outs = new List<List<int>>();
        for (int i = 0; i < objects.Instances.Count; i++) outs.Add(new List<int>());

        int self = 0;
        foreach (var (source, which, destination) in data.Globals())
        {
            if (which != section || !startsAt.TryGetValue(destination, out int to)) continue;
            int from = Owner(source);
            if (from < 0) continue;
            if (from == to) { self++; continue; }
            outs[from].Add(to);
        }

        int count = objects.Instances.Count;
        var index = new int[count];
        var low = new int[count];
        var onStack = new bool[count];
        Array.Fill(index, -1);
        var stack = new Stack<int>();
        int next = 0, onCycle = self;

        for (int start = 0; start < count; start++)
        {
            if (index[start] >= 0) continue;

            var work = new Stack<(int Node, int Edge)>();
            work.Push((start, 0));
            index[start] = low[start] = next++;
            stack.Push(start);
            onStack[start] = true;

            while (work.Count > 0)
            {
                var (node, edge) = work.Pop();
                if (edge < outs[node].Count)
                {
                    work.Push((node, edge + 1));
                    int to = outs[node][edge];
                    if (index[to] < 0)
                    {
                        index[to] = low[to] = next++;
                        stack.Push(to);
                        onStack[to] = true;
                        work.Push((to, 0));
                    }
                    else if (onStack[to]) low[node] = Math.Min(low[node], index[to]);
                    continue;
                }

                if (work.Count > 0)
                {
                    var (parent, _) = work.Peek();
                    low[parent] = Math.Min(low[parent], low[node]);
                }

                if (low[node] != index[node]) continue;

                int size = 0;
                while (true)
                {
                    int member = stack.Pop();
                    onStack[member] = false;
                    size++;
                    if (member == node) break;
                }
                if (size > 1) onCycle += size;
            }
        }

        return onCycle;
    }

    private static List<int> StateIds(PackfileObjects objects, PackfileObjects.Instance machine)
    {
        var ids = new List<int>();
        foreach (var state in objects.ReadRefArray(machine, "states") ?? new List<PackfileObjects.Instance?>())
            if (state != null && objects.ReadInt(state, "stateId") is int held) ids.Add(held);
        return ids;
    }

    private static string Kind(string message) =>
        message.Contains("does not declare", StringComparison.Ordinal)
            ? "an event or variable the other file does not declare"
        : message.Contains("shares", StringComparison.Ordinal)
            ? "an object shared with the rest of the file it came from"
        : message.Contains("there is no name to copy it across by", StringComparison.Ordinal)
            ? "an index pointing past the end of the file's own symbol list"
        : message;

    private static NativePaste.Subtree? BiggestSubtree(PackfileImage image)
    {
        string[] wanted =
        {
            "hkbStateMachineStateInfo", "hkbBlenderGenerator", "hkbManualSelectorGenerator",
            "hkbModifierGenerator", "hkbStateMachine", "hkbClipGenerator",
        };

        var objects = new PackfileObjects(image);
        NativePaste.Subtree? best = null;

        foreach (string className in wanted)
        {
            var instance = objects.OfClass(className).FirstOrDefault();
            if (instance == null) continue;

            var tree = NativePaste.Of(image, objects.IndexOf(instance) + NativeGraphModel.FirstId);
            if (best == null || tree.Ids.Count > best.Ids.Count) best = tree;
            if (best.Ids.Count >= 4) break;
        }

        return best is { Ids.Count: > 1 } ? best : null;
    }

    private static string PasteWrong(byte[] bytes, List<string> wasClasses, List<string> copiedClasses,
                                     NativePaste.Subtree tree, HashSet<int> ownedWas,
                                     HashSet<int> sharedWas, string was, NativePaste.Result result)
    {
        PackfileImage image;
        PackfileObjects objects;
        try
        {
            image = PackfileImage.Read(bytes);
            objects = new PackfileObjects(image);
        }
        catch (Exception e) { return "will not read back: " + e.Message; }

        int expected = wasClasses.Count + tree.Ids.Count;
        if (objects.Instances.Count != expected)
            return $"holds {objects.Instances.Count} object(s), expected {expected}";

        for (int i = 0; i < wasClasses.Count; i++)
            if (objects.Instances[i].ClassName != wasClasses[i])
                return $"object {i} was a {wasClasses[i]} and is now a {objects.Instances[i].ClassName}";

        for (int k = 0; k < tree.Ids.Count; k++)
        {
            string mine = objects.Instances[wasClasses.Count + k].ClassName;
            if (mine != copiedClasses[k])
                return $"the copy holds a {mine} where the original holds a {copiedClasses[k]}";
        }

        var data = image.Section("__data__")!;
        var items = PackfileLayout.Of(image);
        if (items == null) return "the walk cannot account for the result";

        int section = image.Sections.IndexOf(data);
        if (!PackfileLayout.Reaches(items, data, section))
            return "something in the result points at bytes the walk cannot place";

        var runs = PackfileLayout.ByObject(items);
        if (runs.Count != objects.Instances.Count) return "the walk found a different number of objects";

        var pastedAt = new HashSet<int>();
        var mineSpans = new List<(int At, int End)>();
        for (int k = 0; k < tree.Ids.Count; k++)
        {
            int index = wasClasses.Count + k;
            pastedAt.Add(objects.Instances[index].Offset);
            foreach (var item in runs[index]) mineSpans.Add((item.At, item.At + item.Length));
        }

        bool InCopy(int offset) => mineSpans.Exists(s => offset >= s.At && offset < s.End);

        foreach (var (source, which, destination) in data.Globals())
        {
            if (which != section || !InCopy(source)) continue;
            if (pastedAt.Contains(destination) || sharedWas.Contains(destination)) continue;

            if (ownedWas.Contains(destination))
                return $"a pointer at 0x{source:x} inside the copy still names the original object " +
                       "it was copied from";

            return $"a pointer at 0x{source:x} inside the copy names 0x{destination:x}, which is " +
                   "neither part of the copy nor one of the objects the subtree shares";
        }

        if (was.Length > 0)
        {
            string now = Shape(image, objects, result.RootId);
            if (now != was)
                return "the copy is not the same shape as the subtree it was made from";
        }

        return "";
    }

    private static string Shape(PackfileImage image, PackfileObjects objects, int rootId)
    {
        var types = HavokClassTypes.Shipped;
        var data = image.Section("__data__")!;
        int section = image.Sections.IndexOf(data);

        var symbols = new Dictionary<int, string>();
        foreach (bool events in new[] { true, false })
        {
            var names = SymbolNames(objects, events ? "eventNames" : "variableNames");
            foreach (var site in SymbolIndexFixup.IndexSites(objects, events))
                symbols[site.At] = site.Value >= 0 && site.Value < names.Count
                                   ? (events ? "event " : "variable ") + names[site.Value]
                                   : (events ? "event " : "variable ") + site.Value;
        }

        var aims = new Dictionary<int, int>();
        foreach (var (source, destination) in data.Locals()) aims[source] = destination;
        var points = new Dictionary<int, int>();
        foreach (var (source, which, destination) in data.Globals())
            if (which == section) points[source] = destination;

        var startsAt = new Dictionary<int, int>();
        for (int i = 0; i < objects.Instances.Count; i++) startsAt[objects.Instances[i].Offset] = i;

        var seen = new Dictionary<int, int>();
        var text = new System.Text.StringBuilder();

        void Walk(int index, int offset, string className, int depth)
        {
            if (depth > 12) return;

            foreach (var member in types.Members(className))
            {
                if (!member.Written) continue;
                int at = offset + member.Offset;
                text.Append(member.Name).Append('=');

                if (member.VType is "TYPE_STRINGPTR" or "TYPE_CSTRING")
                {
                    text.Append(aims.TryGetValue(at, out int t) ? Text(data.Data, t) : "-").Append(';');
                    continue;
                }

                if (member.VType == "TYPE_POINTER")
                {
                    text.Append(points.TryGetValue(at, out int d) ? Visit(d).ToString() : "-").Append(';');
                    continue;
                }

                if (member.VType == "TYPE_STRUCT")
                {
                    if (member.CType != null && types.Knows(member.CType))
                    {
                        text.Append('{');
                        Walk(index, at, member.CType, depth + 1);
                        text.Append('}');
                    }
                    continue;
                }

                if (member.VType is "TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY")
                {
                    var array = objects.ArrayAt(at);
                    int count = array?.Count ?? 0;
                    text.Append('[').Append(count);

                    if (array != null && count > 0)
                    {
                        if (member.VSub == "TYPE_STRUCT" && member.CType != null && types.Knows(member.CType))
                        {
                            int stride = types[member.CType]?.Size ?? 0;
                            for (int e = 0; e < count && stride > 0; e++)
                                Walk(index, array.At + e * stride, member.CType, depth + 1);
                        }
                        else if (member.VSub is "TYPE_STRINGPTR" or "TYPE_CSTRING")
                        {
                            for (int e = 0; e < count; e++)
                                text.Append(aims.TryGetValue(array.At + e * 8, out int t)
                                            ? Text(data.Data, t) : "-").Append(',');
                        }
                        else if (member.VSub == "TYPE_POINTER")
                        {
                            for (int e = 0; e < count; e++)
                                text.Append(points.TryGetValue(array.At + e * 8, out int d)
                                            ? Visit(d).ToString() : "-").Append(',');
                        }
                        else
                        {
                            int width = HavokClassTypes.Width(member.VSub);
                            for (int e = 0; e < count * width && width > 0; e++)
                                text.Append(data.Data[array.At + e].ToString("x2"));
                        }
                    }

                    text.Append("];");
                    continue;
                }

                int span = Math.Max(1, HavokClassTypes.Width(member.VType));
                for (int e = 0; e < Math.Max(1, member.ArrSize); e++)
                {
                    int elementAt = at + e * span;
                    if (symbols.TryGetValue(elementAt, out string? name)) { text.Append(name); continue; }

                    for (int b = 0; b < span && elementAt + b < data.Data.Length; b++)
                        text.Append(data.Data[elementAt + b].ToString("x2"));
                }
                text.Append(';');
            }
        }

        int Visit(int destination)
        {
            if (!startsAt.TryGetValue(destination, out int which)) return -1;
            if (seen.TryGetValue(which, out int already)) return already;

            int position = seen.Count;
            seen[which] = position;
            text.Append('<').Append(position).Append(':').Append(objects.Instances[which].ClassName);
            Walk(which, objects.Instances[which].Offset, objects.Instances[which].ClassName, 0);
            text.Append('>');
            return position;
        }

        int root = rootId - NativeGraphModel.FirstId;
        if (root < 0 || root >= objects.Instances.Count) return "";
        Visit(objects.Instances[root].Offset);
        return text.ToString();
    }

    private static string Text(byte[] data, int at)
    {
        int end = Array.IndexOf(data, (byte)0, at);
        return end < 0 ? "" : System.Text.Encoding.UTF8.GetString(data, at, end - at);
    }

    private static List<string> SymbolNames(PackfileObjects objects, string field)
    {
        var strings = objects.OfClass("hkbBehaviorGraphStringData").FirstOrDefault();
        if (strings == null) return new List<string>();

        var names = objects.ReadStringArray(strings, field);
        return names == null ? new List<string>() : names.Select(n => n ?? "").ToList();
    }

    private static int Compare(string[] argv)
    {
        if (argv.Length < 3) { Usage(); return 1; }
        var a = PackfileImage.Read(Path.GetFullPath(argv[1]));
        var b = PackfileImage.Read(Path.GetFullPath(argv[2]));

        var lines = new List<string>();
        int total = CompareImages(a, b, lines);

        foreach (var line in lines) Console.WriteLine(line);
        Console.WriteLine(total == 0 ? "identical" : "differs");
        return total == 0 ? 0 : 1;
    }

    // The number of differing regions between two packfiles, with a human line per difference.
    // "identical" means every loading-critical field agrees: the whole header (layout rules,
    // predicates, root and class-name root pointers, flags and version), the complete section
    // set (a section present in only one file is a difference), and for each common section
    // its data, every fixup table, and its exports and imports.
    internal static int CompareImages(PackfileImage a, PackfileImage b, List<string> lines)
    {
        int total = 0;

        if (a.UserTag != b.UserTag) { lines.Add("header: user tag differs"); total++; }
        if (a.FileVersion != b.FileVersion) { lines.Add("header: file version differs"); total++; }
        if (!a.LayoutRules.SequenceEqual(b.LayoutRules)) { lines.Add("header: layout rules differ"); total++; }
        if (a.ContentsSectionIndex != b.ContentsSectionIndex) { lines.Add("header: root section differs"); total++; }
        if (a.ContentsSectionOffset != b.ContentsSectionOffset) { lines.Add("header: root offset differs"); total++; }
        if (a.ContentsClassNameSectionIndex != b.ContentsClassNameSectionIndex)
        { lines.Add("header: class-name root section differs"); total++; }
        if (a.ContentsClassNameSectionOffset != b.ContentsClassNameSectionOffset)
        { lines.Add("header: class-name root offset differs"); total++; }
        if (!a.ContentsVersion.SequenceEqual(b.ContentsVersion)) { lines.Add("header: contents version differs"); total++; }
        if (a.Flags != b.Flags) { lines.Add("header: flags differ"); total++; }
        if (a.MaxPredicate != b.MaxPredicate) { lines.Add("header: predicate count differs"); total++; }
        if (!a.Predicates.SequenceEqual(b.Predicates)) { lines.Add("header: predicates differ"); total++; }

        var tags = a.Sections.Select(s => s.Tag).Concat(b.Sections.Select(s => s.Tag))
                    .Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList();
        foreach (var tag in tags)
        {
            var sa = a.Section(tag);
            var sb = b.Section(tag);
            if (sa == null || sb == null) { lines.Add($"{tag}: present in only one file"); total++; continue; }

            string detail = "";
            int diffs = SectionDiff(sa, sb, ref detail);
            lines.Add($"{tag}: {(diffs == 0 ? "identical" : diffs + " differing regions" + detail)}");
            total += diffs;
        }
        return total;
    }

    private static int SectionDiff(PackfileSection a, PackfileSection b, ref string detail)
    {
        int diffs = 0;
        diffs += ByteDiff("data", a.Data, b.Data, ref detail);
        diffs += ByteDiff("local", a.LocalFixups, b.LocalFixups, ref detail);
        diffs += ByteDiff("global", a.GlobalFixups, b.GlobalFixups, ref detail);
        diffs += ByteDiff("virtual", a.VirtualFixups, b.VirtualFixups, ref detail);
        diffs += ByteDiff("export", a.Exports, b.Exports, ref detail);
        diffs += ByteDiff("import", a.Imports, b.Imports, ref detail);
        return diffs;
    }

    private static int ByteDiff(string what, byte[] a, byte[] b, ref string detail)
    {
        if (a.Length != b.Length) { detail += $" [{what}: {a.Length} vs {b.Length} bytes]"; return 1; }
        int diffs = 0, first = -1;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) { diffs++; if (first < 0) first = i; }
        if (diffs > 0) detail += $" [{what}: {diffs} bytes, first at 0x{first:x}]";
        return diffs > 0 ? 1 : 0;
    }

    private static int Convert(string[] argv)
    {
        if (argv.Length < 4) { Usage(); return 1; }
        if (!int.TryParse(argv[3], out int bytes) || (bytes != 4 && bytes != 8)) { Usage(); return 1; }

        var image = PackfileImage.Read(Path.GetFullPath(argv[1]));
        if (!PackfileConverter.ConvertTo(image, new PointerLayout(bytes)))
        {
            Console.WriteLine("could not convert: the file holds a class, section or fixup the converter " +
                              "will not vouch for (see the fail-closed section rules)");
            return 1;
        }
        image.Save(Path.GetFullPath(argv[2]));
        Console.WriteLine($"wrote {bytes}-byte layout to {Path.GetFullPath(argv[2])}");
        return 0;
    }

    private static int Ground(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        var image = PackfileImage.Read(Path.GetFullPath(argv[1]));
        var types = HavokClassTypes.Shipped;
        var data = image.Section("__data__");
        if (data == null) { Console.WriteLine("the reference file has no __data__ section"); return 1; }

        var (placed, refused, objects, reference, predicted, unexplained) = GroundPrediction(image, types);

        Console.WriteLine($"reference layout {image.Layout.PointerSize}-byte, {objects} objects " +
                          $"({placed} placed, {refused} the walker will not vouch for)");
        Console.WriteLine($"reference pointer fixups: {reference}, walker predicted sites: {predicted}");
        Console.WriteLine($"fixups the walker did not predict: {unexplained.Count}");
        foreach (int at in unexplained.Take(25))
            Console.WriteLine($"  0x{at:x}: the reference has a pointer here, the walker placed none");

        return unexplained.Count == 0 ? 0 : 1;
    }

    // Every pointer-sized fixup site a placeable object graph should carry, compared against
    // the file's own fixup sources. The walker must predict each fixed-array element slot and
    // both slots of every TYPE_VARIANT, not just the first.
    internal static (int Placed, int Refused, int Objects, int ReferenceSites, int PredictedSites, List<int> Unexplained)
        GroundPrediction(PackfileImage image, HavokClassTypes types)
    {
        var data = image.Section("__data__")!;
        var objects = new PackfileObjects(image, types: types);

        var referenceSites = new SortedSet<int>();
        foreach (var (source, _) in data.Locals()) referenceSites.Add(source);
        foreach (var (source, _, _) in data.Globals()) referenceSites.Add(source);

        var predicted = new HashSet<int>();
        int placed = 0, refused = 0;
        foreach (var instance in objects.Instances)
        {
            if (!LayoutWalker.CanPlace(types, instance.ClassName)) { refused++; continue; }
            placed++;
            CollectSites(types, objects, image.Layout, instance.Offset, instance.ClassName, predicted, 0);
        }

        var unexplained = referenceSites.Where(s => !predicted.Contains(s)).ToList();
        return (placed, refused, objects.Instances.Count, referenceSites.Count, predicted.Count, unexplained);
    }

    private static int Offsets(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(argv[1])));
        var reflected = doc.RootElement.GetProperty("reflected");
        var types = HavokClassTypes.Shipped;
        string? dumpPath = argv.Length >= 3 ? Path.GetFullPath(argv[2]) : null;
        var dump = new System.Text.StringBuilder("{\n");

        int checkedClasses = 0, badClasses = 0, badMembers = 0, skipped = 0;
        foreach (var prop in reflected.EnumerateObject())
        {
            string cls = prop.Name;
            var v = prop.Value;
            if (v.TryGetProperty("empty", out var e) && e.GetBoolean()) continue;
            if (!types.Knows(cls) || !LayoutWalker.CanPlace(types, cls)) { skipped++; continue; }

            var laid = LayoutWalker.Of(types, cls, PointerLayout.FourByte);
            string? parent = types[cls]?.Parent;
            int parentSize = parent != null && types.Knows(parent)
                ? LayoutWalker.Of(types, parent, PointerLayout.FourByte).Size : 0;

            checkedClasses++;
            bool shown = false, bad = false;
            var rows = new List<string>();
            foreach (var m in v.GetProperty("members").EnumerateArray())
            {
                string name = m.GetProperty("name").GetString()!;
                int reference = m.GetProperty("offset").GetInt32();
                int? walk = laid.OffsetOf(name);
                if (walk == null) continue;
                rows.Add($"[\"{name}\",{walk},{reference}]");
                if (walk == reference) continue;
                bad = true; badMembers++;
                if (!shown)
                {
                    Console.WriteLine($"{cls}: parentSize4={parentSize} size4={laid.Size} " +
                                      $"first-bad {name} walk={walk} reference={reference}");
                    shown = true;
                }
            }
            if (bad) badClasses++;
            if (dumpPath != null)
                dump.Append($"  \"{cls}\": {{\"parent4\":{parentSize},\"size4\":{laid.Size}," +
                            $"\"m\":[{string.Join(",", rows)}]}},\n");
        }

        if (dumpPath != null)
        {
            if (dump.Length > 2) dump.Length -= 2;
            dump.Append("\n}\n");
            File.WriteAllText(dumpPath, dump.ToString());
            Console.WriteLine($"wrote walk/reference dump to {dumpPath}");
        }
        Console.WriteLine($"checked {checkedClasses} placeable reflected classes ({skipped} skipped); " +
                          $"{badClasses} classes differ, {badMembers} member offsets differ");
        return badClasses == 0 ? 0 : 1;
    }

    private static void CollectSites(HavokClassTypes types, PackfileObjects objects, PointerLayout layout,
                                     int offset, string className, HashSet<int> sites, int depth)
    {
        if (depth > 12) return;

        int p = layout.PointerSize;
        var laid = LayoutWalker.Of(types, className, layout);
        var members = types.Members(className);

        for (int i = 0; i < members.Count && i < laid.Offsets.Count; i++)
        {
            var member = members[i];
            if (!member.Written) continue;
            int at = offset + laid.Offsets[i];
            int count = Math.Max(1, member.ArrSize);

            // Fixed arrays are inline: every element is its own pointer site, at pointer stride.
            if (member.VType is "TYPE_POINTER" or "TYPE_STRINGPTR" or "TYPE_CSTRING")
            {
                for (int e = 0; e < count; e++) sites.Add(at + e * p);
                continue;
            }

            // A variant holds two pointer-sized slots per element: the object pointer and the
            // class-name pointer.
            if (member.VType == "TYPE_VARIANT")
            {
                for (int e = 0; e < count; e++)
                {
                    sites.Add(at + e * 2 * p);
                    sites.Add(at + e * 2 * p + p);
                }
                continue;
            }

            if (member.VType == "TYPE_STRUCT")
            {
                if (member.CType != null && types.Knows(member.CType))
                {
                    int stride = LayoutWalker.Of(types, member.CType, layout).Size;
                    if (stride <= 0) continue;
                    for (int e = 0; e < count; e++)
                        CollectSites(types, objects, layout, at + e * stride, member.CType, sites, depth + 1);
                }
                continue;
            }

            if (member.VType is not ("TYPE_ARRAY" or "TYPE_SIMPLEARRAY")) continue;

            sites.Add(at);
            var array = objects.ArrayAt(at);
            if (array == null || array.Count == 0) continue;

            if (member.VSub is "TYPE_POINTER" or "TYPE_STRINGPTR" or "TYPE_CSTRING")
            {
                for (int e = 0; e < array.Count; e++) sites.Add(array.At + e * p);
            }
            else if (member.VSub == "TYPE_STRUCT" && member.CType != null && types.Knows(member.CType))
            {
                int stride = LayoutWalker.Of(types, member.CType, layout).Size;
                if (stride <= 0) continue;
                for (int e = 0; e < array.Count; e++)
                    CollectSites(types, objects, layout, array.At + e * stride, member.CType, sites, depth + 1);
            }
        }
    }

    private static int Relayout(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        int same = 0, differed = 0, refused = 0;
        var notes = new List<string>();

        foreach (string file in files)
        {
            byte[] original;
            PackfileImage image;
            try
            {
                original = InputFilePolicy.ReadHkx(file);
                image = PackfileImage.Read(original);
            }
            catch (Exception e)
            {
                refused++;
                if (notes.Count < 10) notes.Add($"{Path.GetFileName(file)}: {e.Message}");
                continue;
            }

            if (!PackfileLayout.Rewrite(image))
            {
                refused++;
                if (notes.Count < 10)
                    notes.Add($"{Path.GetFileName(file)}: the walk could not account for this file, " +
                              "so it was left alone");
                continue;
            }

            byte[] rebuilt = image.Rebuild();
            int firstDifference = FirstDifference(original, rebuilt);
            if (firstDifference < 0) { same++; continue; }

            differed++;
            if (notes.Count < 10)
                notes.Add($"{Path.GetFileName(file)}: {original.Length} bytes in, {rebuilt.Length} out, " +
                          $"first difference at 0x{firstDifference:x}" +
                          Around(original, rebuilt, firstDifference));
        }

        foreach (string note in notes) Console.WriteLine("  " + note);

        Console.WriteLine($"\n{files.Length} file(s): {same} came back as the file they were, " +
                          $"{differed} did not, {refused} left alone");
        return differed == 0 && refused == 0 ? 0 : 1;
    }

    private static int Packfile(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories).OrderBy(f => f).ToArray()
            : new[] { target };

        int same = 0, differed = 0, refused = 0;
        var firstFailures = new List<string>();

        foreach (string file in files)
        {
            byte[] original;
            try { original = InputFilePolicy.ReadHkx(file); }
            catch (Exception e) { refused++; firstFailures.Add($"{Path.GetFileName(file)}: {e.Message}"); continue; }

            PackfileImage image;
            try { image = PackfileImage.Read(original); }
            catch (Exception e)
            {
                refused++;
                if (firstFailures.Count < 10) firstFailures.Add($"{Path.GetFileName(file)}: {e.Message}");
                continue;
            }

            byte[] rebuilt = image.Rebuild();
            int at = FirstDifference(original, rebuilt);
            if (at < 0) { same++; continue; }

            differed++;
            if (firstFailures.Count < 10)
            {
                firstFailures.Add($"{Path.GetFileName(file)}: {original.Length} bytes in, {rebuilt.Length} out, " +
                                  $"first difference at 0x{at:x}" + Around(original, rebuilt, at));
            }
        }

        if (files.Length == 1 && same == 1) Describe(PackfileImage.Read(files[0]));

        Console.WriteLine($"\n{files.Length} file(s): {same} rebuilt identically, {differed} differed, " +
                          $"{refused} could not be read");
        foreach (string failure in firstFailures) Console.WriteLine("  " + failure);

        return differed == 0 && refused == 0 ? 0 : 1;
    }

    private static void Describe(PackfileImage image)
    {
        Console.WriteLine($"version {image.FileVersion}, layout {string.Join(".", image.LayoutRules)}, " +
                          $"{image.Predicates.Length} bytes before the section headers");
        Console.WriteLine($"{"section",-16} {"data",10} {"local",8} {"global",8} {"virtual",8}");
        foreach (var section in image.Sections)
        {
            Console.WriteLine($"{section.Tag,-16} {section.Data.Length,10} " +
                              $"{section.Locals().Count(),8} {section.Globals().Count(),8} " +
                              $"{section.Virtuals().Count(),8}");
        }
    }

    private static int FirstDifference(byte[] a, byte[] b)
    {
        int shared = Math.Min(a.Length, b.Length);
        for (int i = 0; i < shared; i++) if (a[i] != b[i]) return i;
        return a.Length == b.Length ? -1 : shared;
    }

    private static string Around(byte[] a, byte[] b, int at)
    {
        int from = Math.Max(0, at - 4);
        return $"\n      was {Hex(a, from, at)}\n      now {Hex(b, from, at)}";
    }

    private static string Hex(byte[] bytes, int from, int at)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = from; i < Math.Min(bytes.Length, at + 8); i++)
            sb.Append(i == at ? $"[{bytes[i]:x2}]" : $" {bytes[i]:x2} ");
        return sb.ToString();
    }

    private static int Channels(string[] argv)
    {
        if (argv.Length < 3) { Usage(); return 1; }

        var skeleton = new HkxBinaryReader().ReadSkeleton(Path.GetFullPath(argv[1]));
        string target = Path.GetFullPath(argv[2]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories).OrderBy(f => f).ToArray()
            : new[] { target };

        Console.WriteLine($"{skeleton.Name}: {skeleton.BoneNames.Count} bones");
        Console.WriteLine($"{"file",-40} {"tracks",7} {"noTrans",8} {"noRot",7} {"noScale",8} " +
                          $"{"offsetT",8} {"maxT",8} {"offsetR",8} {"maxRdeg",8} {"offsetS",8}");

        int filesRead = 0, disagreements = 0;
        foreach (string file in files)
        {
            if (!new HkxBinaryReader().TryReadAnimation(file, out var animation)) continue;
            if (animation.Tracks.Count == 0) continue;
            if (AnimationPose.WhyNotPosable(skeleton, animation) != null) continue;
            filesRead++;

            var byBone = AnimationPose.TracksByBone(skeleton, animation);
            int noTrans = 0, noRot = 0, noScale = 0;
            int offsetT = 0, offsetR = 0, offsetS = 0;
            float maxT = 0, maxR = 0;

            for (int bone = 0; bone < byBone.Length; bone++)
            {
                int track = byBone[bone];
                if (track < 0 || track >= animation.Tracks.Count) continue;

                var data = animation.Tracks[track];
                bool anyTrans = data.TranslationAnimated[0] || data.TranslationAnimated[1] ||
                                data.TranslationAnimated[2];
                bool anyScale = data.ScaleAnimated[0] || data.ScaleAnimated[1] || data.ScaleAnimated[2];

                if (!anyTrans) noTrans++;
                if (!data.RotationAnimated) noRot++;
                if (!anyScale) noScale++;

                if (bone >= skeleton.ReferencePose.Count) continue;
                var rest = skeleton.ReferencePose[bone];

                if (!anyTrans && rest.Translation.Length() > 0.01f)
                {
                    offsetT++;
                    maxT = Math.Max(maxT, rest.Translation.Length());
                }

                if (!data.RotationAnimated)
                {
                    float w = Math.Abs(System.Numerics.Quaternion.Normalize(rest.Rotation).W);
                    float degrees = 2 * (float)(Math.Acos(Math.Min(1, w)) * 180 / Math.PI);
                    if (degrees > 0.5f) { offsetR++; maxR = Math.Max(maxR, degrees); }
                }

                if (!anyScale && (rest.Scale - System.Numerics.Vector3.One).Length() > 0.01f) offsetS++;
            }

            if (offsetT > 0 || offsetR > 0 || offsetS > 0) disagreements++;
            Console.WriteLine($"{Path.GetFileName(file),-40} {animation.Tracks.Count,7} {noTrans,8} " +
                              $"{noRot,7} {noScale,8} {offsetT,8} {maxT,8:F2} {offsetR,8} {maxR,8:F1} " +
                              $"{offsetS,8}");
        }

        Console.WriteLine($"\n{filesRead} animations read, {disagreements} where an undriven channel " +
                          "covers a bone the rig does not place at Havok's constant");
        return 0;
    }

    private static int Pose(string[] argv)
    {
        if (argv.Length < 3) { Usage(); return 1; }

        var skeleton = new HkxBinaryReader().ReadSkeleton(Path.GetFullPath(argv[1]));
        if (!new HkxBinaryReader().TryReadAnimation(Path.GetFullPath(argv[2]), out var animation))
        {
            Console.WriteLine($"{Path.GetFileName(argv[2])}: {animation.AnimationClass} is not decoded");
            return 1;
        }

        Console.WriteLine($"{skeleton.Name}: {skeleton.BoneNames.Count} bones");
        Console.WriteLine($"{Path.GetFileName(argv[2])}: {animation.GetSummary()}");

        string? refusal = AnimationPose.WhyNotPosable(skeleton, animation);
        if (refusal != null) { Console.WriteLine("refused: " + refusal); return 1; }

        var driven = AnimationPose.TracksByBone(skeleton, animation);
        Console.WriteLine($"{driven.Count(t => t >= 0)} of {skeleton.BoneNames.Count} bones are driven by a track");

        int frame = argv.Length > 3 && int.TryParse(argv[3], out int n) ? n : 0;
        var first = AnimationPose.At(skeleton, animation, 0);
        var last = AnimationPose.At(skeleton, animation, animation.NumFrames - 1);
        var here = AnimationPose.At(skeleton, animation, frame);

        Console.WriteLine($"\nframe 0 to frame {animation.NumFrames - 1}: " +
                          $"{AnimationPose.Distance(first, last):F3} units of movement summed over every bone");
        Console.WriteLine($"reference pose to frame 0: " +
                          $"{AnimationPose.Distance(AnimationPose.ReferencePose(skeleton), first):F3}");

        Console.WriteLine($"\nframe {here.Frame} at {here.Time:F3}s");
        Console.WriteLine($"  {"bone",-28} {"world position",-34} {"moved since frame 0",-12} driven by");
        foreach (var bone in here.Bones.Take(24))
        {
            var p = bone.Position;
            float moved = System.Numerics.Vector3.Distance(p, first.Bones[bone.Index].Position);
            Console.WriteLine($"  {bone.Name,-28} {p.X,10:F3}{p.Y,11:F3}{p.Z,11:F3}   {moved,10:F3}   " +
                              (driven[bone.Index] >= 0 ? $"track {driven[bone.Index]}" : "reference pose"));
        }
        if (here.Bones.Count > 24) Console.WriteLine($"  ... and {here.Bones.Count - 24} more");
        return 0;
    }

    private static int DrawMesh(string[] argv)
    {
        if (argv.Length < 4) { Usage(); return 1; }

        var nif = OpenCommonwealth.Services.Nif.NifFile.Read(Path.GetFullPath(argv[1]));
        var shapes = OpenCommonwealth.Services.Nif.NifGeometry.Shapes(nif);
        var skeleton = new HkxBinaryReader().ReadSkeleton(Path.GetFullPath(argv[2]));
        string outPath = Path.GetFullPath(argv[3]);

        var wanted = argv.Skip(4).ToList();
        var colours = new (byte R, byte G, byte B)[]
        {
            (255, 80, 80), (90, 220, 120), (110, 170, 255), (250, 200, 80),
        };

        var rest = AnimationPose.ReferencePose(skeleton);
        const int Side = 900, Height = 1000;
        var image = new Png(Side * 2, Height);

        var all = new List<System.Numerics.Vector3>();
        var drawn = new List<(OpenCommonwealth.Services.Nif.NifShape Shape,
                              System.Numerics.Vector3[] Posed)>();

        foreach (var shape in shapes)
        {
            var binding = OpenCommonwealth.Services.Nif.SkinnedMesh.Bind(shape, skeleton);
            var posed = OpenCommonwealth.Services.Nif.SkinnedMesh.Pose(shape, binding, rest, skeleton);
            drawn.Add((shape, posed));
            all.AddRange(posed);
        }

        if (all.Count == 0) { Console.WriteLine("nothing to draw"); return 1; }

        var min = new System.Numerics.Vector3(float.MaxValue);
        var max = new System.Numerics.Vector3(float.MinValue);
        foreach (var p in all)
        {
            min = System.Numerics.Vector3.Min(min, p);
            max = System.Numerics.Vector3.Max(max, p);
        }

        float span = Math.Max(Math.Max(max.X - min.X, max.Y - min.Y), max.Z - min.Z);
        if (span <= 0) span = 1;
        float scale = (Height - 60) / span;
        var centre = (min + max) * 0.5f;

        (int X, int Y) Place(System.Numerics.Vector3 p, bool front)
        {
            float across = front ? p.X - centre.X : p.Y - centre.Y;
            float up = p.Z - centre.Z;
            int x = (int)(Side / 2 + across * scale) + (front ? 0 : Side);
            int y = (int)(Height / 2 - up * scale);
            return (x, y);
        }

        int marked = 0;
        foreach (var (shape, posed) in drawn)
        {

            var owner = new int[shape.Vertices.Count];
            System.Array.Fill(owner, -1);

            if (shape.IsSkinned)
                for (int v = 0; v < shape.Vertices.Count; v++)
                {
                    float best = 0;
                    for (int s = 0; s < 4; s++)
                    {
                        float w = shape.BoneWeights[v * 4 + s];
                        if (w <= best) continue;
                        best = w;
                        owner[v] = shape.BoneIndices[v * 4 + s];
                    }
                }

            foreach (var (a, b) in OpenCommonwealth.Services.Nif.SkinnedMesh.Edges(shape))
            {
                int which = -1;
                for (int i = 0; i < wanted.Count && which < 0; i++)
                {
                    string name = wanted[i];
                    bool hit = Named(shape, owner, a, name) || Named(shape, owner, b, name);
                    if (hit) which = i;
                }

                var (r, g, bl) = which >= 0 ? colours[which % colours.Length] : ((byte)70, (byte)70, (byte)78);
                if (which >= 0) marked++;

                foreach (bool front in new[] { true, false })
                {
                    var (x0, y0) = Place(posed[a], front);
                    var (x1, y1) = Place(posed[b], front);
                    image.Line(x0, y0, x1, y1, r, g, bl);
                }
            }
        }

        foreach (string name in wanted)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            int count = 0;

            foreach (var (shape, posed) in drawn)
            {
                if (!shape.IsSkinned) continue;
                int b = shape.BoneNames.FindIndex(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (b < 0) continue;

                for (int v = 0; v < shape.Vertices.Count; v++)
                {
                    float best = 0; int owns = -1;
                    for (int sl = 0; sl < 4; sl++)
                    {
                        float w = shape.BoneWeights[v * 4 + sl];
                        if (w <= best) continue;
                        best = w; owns = shape.BoneIndices[v * 4 + sl];
                    }
                    if (owns != b) continue;

                    lo = Math.Min(lo, posed[v].Z);
                    hi = Math.Max(hi, posed[v].Z);
                    count++;
                }
            }

            Console.WriteLine(count == 0
                ? $"  {name}: no vertex is weighted mostly to it"
                : $"  {name}: {count} vertices, posed z {lo:F1} to {hi:F1}");
        }

        Console.WriteLine($"  whole mesh posed z {min.Z:F1} to {max.Z:F1}");

        image.Save(outPath);
        Console.WriteLine($"{Path.GetFileName(argv[1])} on {skeleton.Name}: {drawn.Count} shape(s), " +
                          $"{all.Count} vertices, {marked} edge(s) marked, written to {outPath}");
        for (int i = 0; i < wanted.Count; i++)
            Console.WriteLine($"  {wanted[i]} drawn as {colours[i % colours.Length]}");
        return 0;
    }

    private static bool Named(OpenCommonwealth.Services.Nif.NifShape shape, int[] owner, int vertex,
                              string boneName)
    {
        int b = owner[vertex];
        return b >= 0 && b < shape.BoneNames.Count &&
               shape.BoneNames[b].Equals(boneName, StringComparison.OrdinalIgnoreCase);
    }

    private static int Mesh(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        var nif = OpenCommonwealth.Services.Nif.NifFile.Read(Path.GetFullPath(argv[1]));
        Console.WriteLine($"{Path.GetFileName(argv[1])}: NIF {nif.Version:x} user {nif.UserVersion} " +
                          $"BSVersion {nif.BsVersion}, {nif.BlockCount} blocks, {nif.Strings.Count} strings");

        var shapes = OpenCommonwealth.Services.Nif.NifGeometry.Shapes(nif);
        Console.WriteLine($"{shapes.Count} drawable shape{(shapes.Count == 1 ? "" : "s")}");
        foreach (var s in shapes)
            Console.WriteLine($"  {s}  {OpenCommonwealth.Services.Nif.SkinnedMesh.Edges(s).Count} unique edges");

        if (argv.Length < 3) return shapes.Count > 0 ? 0 : 1;

        var skeleton = new HkxBinaryReader().ReadSkeleton(Path.GetFullPath(argv[2]));
        Console.WriteLine($"\nagainst {skeleton.Name}, {skeleton.BoneNames.Count} bones");

        int unmatched = 0;
        float worstDrift = 0;
        float worstShare = 0;
        foreach (var s in shapes)
        {
            var binding = OpenCommonwealth.Services.Nif.SkinnedMesh.Bind(s, skeleton);
            Console.WriteLine($"  {s.Name,-26} {binding}");
            unmatched += binding.Unmatched.Count;

            float drift = OpenCommonwealth.Services.Nif.SkinnedMesh
                .BindError(s, binding, skeleton, out int measured);
            if (measured > 0) worstDrift = Math.Max(worstDrift, drift);

            Console.WriteLine($"    bones disagree by {drift:F3} at most, across the {measured} that " +
                              "matched" +
                              (measured > 1 && drift > DriftLimit ? "   THE BIND TRANSFORMS ARE NOT COMPOSING"
                               : measured < 2 ? "   nothing to measure, fewer than two bones matched" : ""));

            var restMin = new System.Numerics.Vector3(float.MaxValue);
            var restMax = new System.Numerics.Vector3(float.MinValue);
            foreach (var p in s.Vertices)
            {
                restMin = System.Numerics.Vector3.Min(restMin, p);
                restMax = System.Numerics.Vector3.Max(restMax, p);
            }
            Console.WriteLine($"    as authored: bounds {restMin.X:F1},{restMin.Y:F1},{restMin.Z:F1} " +
                              $"to {restMax.X:F1},{restMax.Y:F1},{restMax.Z:F1}, node at " +
                              $"{s.NodeTranslation.X:F2} {s.NodeTranslation.Y:F2} {s.NodeTranslation.Z:F2} " +
                              $"scale {s.NodeScale:F3}");

            var rest = AnimationPose.ReferencePose(skeleton);
            var posed = OpenCommonwealth.Services.Nif.SkinnedMesh.Pose(s, binding, rest, skeleton);

            var min = new System.Numerics.Vector3(float.MaxValue);
            var max = new System.Numerics.Vector3(float.MinValue);
            int bad = 0;
            foreach (var p in posed)
            {
                if (float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z)) { bad++; continue; }
                min = System.Numerics.Vector3.Min(min, p);
                max = System.Numerics.Vector3.Max(max, p);
            }
            Console.WriteLine($"    posed on the reference pose: bounds {min.X:F1},{min.Y:F1},{min.Z:F1} " +
                              $"to {max.X:F1},{max.Y:F1},{max.Z:F1}" + (bad > 0 ? $", {bad} NaN" : ""));

            if (drift > DriftLimit) worstShare = Math.Max(worstShare, PerBone(s, binding, rest));
        }

        Console.WriteLine(unmatched == 0
            ? "\nevery mesh bone found a skeleton bone"
            : $"\n{unmatched} mesh bone reference(s) had no skeleton bone of that name");

        bool ok = worstShare <= 0.25f;
        Console.WriteLine(ok
            ? $"PASS  at most {worstShare:P0} of a shape's matched bones disagree by more than " +
              $"{DriftLimit:F1}, worst disagreement {worstDrift:F3}"
            : $"FAIL  {worstShare:P0} of a shape's matched bones disagree by more than " +
              $"{DriftLimit:F1}, worst disagreement {worstDrift:F3}, so the bind is not composing");
        return ok ? 0 : 1;
    }

    private static float PerBone(OpenCommonwealth.Services.Nif.NifShape shape,
                                 OpenCommonwealth.Services.Nif.SkinnedMesh.Binding binding,
                                 AnimationPose.Pose rest)
    {
        var rows = new List<(string Name, float Error, int Vertices, System.Numerics.Vector3 Off)>();
        var placement = OpenCommonwealth.Services.Nif.SkinnedMesh.Placement(shape, binding, rest)
                        ?? System.Numerics.Matrix4x4.Identity;

        for (int b = 0; b < shape.BoneNames.Count; b++)
        {
            var m = OpenCommonwealth.Services.Nif.SkinnedMesh.BoneMatrix(shape, binding, rest, b);
            if (m == null) continue;

            int owned = 0;
            for (int v = 0; v < shape.Vertices.Count; v++)
                for (int s = 0; s < 4; s++)
                    if (shape.BoneIndices[v * 4 + s] == b && shape.BoneWeights[v * 4 + s] > 0)
                    {
                        owned++;
                        break;
                    }

            rows.Add((shape.BoneNames[b],
                      OpenCommonwealth.Services.Nif.SkinnedMesh.Disagreement(placement, m.Value), owned,
                      System.Numerics.Vector3.Transform(System.Numerics.Vector3.Zero, m.Value)));
        }

        int clean = rows.Count(r => r.Error <= DriftLimit);
        Console.WriteLine($"    per bone, on the reference pose: {clean} of {rows.Count} matched " +
                          "bones agree with the first one");

        foreach (var r in rows.OrderByDescending(r => r.Error).Take(12))
            Console.WriteLine($"      {r.Name,-28} {r.Error,9:F3} over {r.Vertices,5} vertices, " +
                              $"origin to {r.Off.X,8:F2} {r.Off.Y,8:F2} {r.Off.Z,8:F2}" +
                              (r.Error <= DriftLimit ? "" : "   <-"));

        return rows.Count == 0 ? 0 : (float)(rows.Count - clean) / rows.Count;
    }

    private const float DriftLimit = 0.5f;

    private static int Skeleton(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        var skeleton = new HkxBinaryReader().ReadSkeleton(Path.GetFullPath(argv[1]));
        Console.WriteLine($"{skeleton.Name}: {skeleton.BoneNames.Count} bones, " +
                          $"{skeleton.ParentIndices.Count} parent indices, {skeleton.ReferencePose.Count} poses");

        var rest = AnimationPose.ReferencePose(skeleton);

        string wanted = argv.Length > 2 ? argv[2] : "Hand";
        int leaf = skeleton.BoneNames.FindIndex(n => n.Contains(wanted, StringComparison.OrdinalIgnoreCase));
        if (leaf < 0) { Console.WriteLine($"no bone matching '{wanted}'"); return 1; }

        var chain = new List<int>();
        for (int i = leaf; i >= 0; i = skeleton.ParentIndices[i]) chain.Insert(0, i);

        Console.WriteLine($"\nchain to {skeleton.BoneNames[leaf]}, {chain.Count} bones");
        Console.WriteLine($"  {"bone",-28} {"stored translation",-34} {"composed world position",-34}");
        foreach (int i in chain)
        {
            var t = skeleton.ReferencePose[i].Translation;
            var w = rest.Bones[i].Position;
            Console.WriteLine($"  {skeleton.BoneNames[i],-28} " +
                              $"{t.X,10:F3}{t.Y,11:F3}{t.Z,11:F3}   " +
                              $"{w.X,10:F3}{w.Y,11:F3}{w.Z,11:F3}");
        }
        return 0;
    }

    private static int Rig(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        if (!Directory.Exists(target))
            return RigOne(target, argv.Length > 2 ? argv[2] : null, verbose: true) ? 0 : 1;

        var files = Directory.EnumerateFiles(target, "*.hkx", SearchOption.AllDirectories)
                             .Where(f => f.Contains($"{Path.DirectorySeparatorChar}CharacterAssets{Path.DirectorySeparatorChar}",
                                                    StringComparison.OrdinalIgnoreCase))
                             .OrderBy(f => f).ToList();

        int ok = 0, failed = 0, noSkeleton = 0, multiRootChild = 0, multiRoot = 0;
        var forks = new List<string>();

        foreach (string file in files)
        {
            try
            {
                if (!RigOne(file, null, verbose: false)) { failed++; Console.WriteLine($"  MISMATCH  {Short(file, target)}"); continue; }
                ok++;

                var skeleton = new HkxBinaryReader().ReadSkeleton(file);
                var counts = SkeletonJson.ChildCounts(skeleton);
                int roots = skeleton.ParentIndices.Count(p => p < 0);
                if (roots > 1) multiRoot++;

                for (int i = 0; i < counts.Count; i++)
                {
                    if (skeleton.ParentIndices[i] >= 0 || counts[i] <= 1) continue;
                    multiRootChild++;
                    if (forks.Count < 8)
                        forks.Add($"{Short(file, target)}  root '{skeleton.BoneNames[i]}' has {counts[i]} children");
                    break;
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("No skeleton", StringComparison.Ordinal)) noSkeleton++;
                else { failed++; Console.WriteLine($"  THREW  {Short(file, target)}: {ex.Message.Split('\n')[0]}"); }
            }
        }

        Console.WriteLine($"\n{files.Count} files under CharacterAssets: {ok} emitted and read back identical, " +
                          $"{failed} failed, {noSkeleton} hold no skeleton");
        Console.WriteLine($"{multiRoot} with more than one root bone, {multiRootChild} whose root has more than one child");
        foreach (string f in forks) Console.WriteLine($"  {f}");
        return failed == 0 ? 0 : 1;
    }

    private static bool RigOne(string path, string? outPath, bool verbose)
    {
        var skeleton = new HkxBinaryReader().ReadSkeleton(path);
        string json = SkeletonJson.Write(skeleton, path);
        var back = SkeletonJson.Read(json);

        bool names = skeleton.BoneNames.SequenceEqual(back.BoneNames);
        bool parents = skeleton.ParentIndices.Take(skeleton.BoneNames.Count)
                               .SequenceEqual(back.ParentIndices);
        bool count = skeleton.BoneNames.Count == back.BoneNames.Count;

        if (outPath != null) File.WriteAllText(outPath, json);

        if (verbose)
        {
            var counts = SkeletonJson.ChildCounts(skeleton);
            Console.WriteLine($"{Path.GetFileName(path)}  '{skeleton.Name}'");
            Console.WriteLine($"  bone count   read {skeleton.BoneNames.Count,4}   json {back.BoneNames.Count,4}   {(count ? "same" : "DIFFERENT")}");
            Console.WriteLine($"  names        {(names ? "identical, in order" : "DIFFERENT")}");
            Console.WriteLine($"  parents      {(parents ? "identical, in order" : "DIFFERENT")}");
            Console.WriteLine($"  roots        {skeleton.ParentIndices.Count(p => p < 0)}");

            for (int i = 0; i < counts.Count; i++)
                if (skeleton.ParentIndices[i] < 0)
                    Console.WriteLine($"  root '{skeleton.BoneNames[i]}' has {counts[i]} children");

            int forks = counts.Count(c => c > 1);
            Console.WriteLine($"  {forks} bones have more than one child, most is {(counts.Count > 0 ? counts.Max() : 0)}");
            if (outPath != null) Console.WriteLine($"  written to {outPath}");
        }

        return names && parents && count;
    }

    private static string Work(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "symrm_" + name);
        HkxTextEdit.ResetDirectory(dir);
        return dir;
    }

    private static int Check(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        var files = Directory.GetFiles(argv[1], "*.xml").OrderBy(f => f).ToList();
        int clean = 0, broken = 0, errorCount = 0, warningCount = 0;
        var byKind = new Dictionary<string, int>();

        foreach (string file in files)
        {
            List<GraphValidator.Finding> findings;
            try { findings = GraphValidator.Check(HkxTextEdit.ReadXml(file)); }
            catch (Exception ex) { Console.WriteLine($"  THREW {Path.GetFileName(file)}: {ex.Message.Split('\n')[0]}"); broken++; continue; }

            var errors = findings.Where(f => f.Level == GraphValidator.Level.Error).ToList();
            errorCount += errors.Count;
            warningCount += findings.Count - errors.Count;
            if (errors.Count == 0) clean++; else broken++;

            string label = Path.GetFileNameWithoutExtension(file);
            if (label.Length > 46) label = label[..46];
            foreach (var f in findings)
            {
                Console.WriteLine($"  {label,-46} {f}");
                string kind = f.Level + ": " + f.What.Split(',')[0];
                byKind[kind] = byKind.GetValueOrDefault(kind) + 1;
            }
        }

        Console.WriteLine($"\n{files.Count} files: {clean} with no errors, {broken} with errors");
        Console.WriteLine($"{errorCount} errors, {warningCount} warnings in total\n");
        foreach (var kv in byKind.OrderByDescending(k => k.Value))
            Console.WriteLine($"  {kv.Value,5}  {kv.Key}");

        return errorCount == 0 ? 0 : 1;
    }

    private static void Show(string label, string xml)
    {
        var model = BehaviourGraphModel.Parse(xml);
        int reachable = RootReachable(model);
        int drawn = GraphAuthor.Layout(model, 10000).Count;

        Console.WriteLine($"  {label,-20} {model.Objects.Count} objects in the file, " +
                          $"{reachable} reachable from the root, {drawn} drawn by the canvas");
    }

    private static int RootReachable(BehaviourGraphModel model)
    {
        var root = model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraph")
                   ?? model.Objects.FirstOrDefault();
        if (root == null) return 0;

        var seen = new HashSet<string> { root.Id };
        var queue = new Queue<HkObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (string target in GraphAuthor.PointsAt(model, current))
            {
                if (!seen.Add(target)) continue;
                var next = model.Get(target);
                if (next != null) queue.Enqueue(next);
            }
        }
        return seen.Count;
    }

    private static string FirstTransitionEffect(BehaviourGraphModel model, string machine, string xml)
    {
        foreach (var state in StateEditor.States(model, machine))
        {
            var array = model.Get(state.TransitionsRef.TrimStart('#'));
            if (array == null || !array.StructLists.TryGetValue("transitions", out var rows)) continue;
            foreach (var row in rows)
                if (row.TryGetValue("transition", out string? value) && value.StartsWith('#'))
                    return value;
        }
        return "null";
    }

    private static void Report(string label, string xml)
    {
        var model = BehaviourGraphModel.Parse(xml);
        Console.WriteLine($"{label}: {model.Objects.Count} objects   {SymbolEditor.Audit(model)}");
    }

    private static int SymbolCount(string xml, bool events)
    {
        var model = BehaviourGraphModel.Parse(xml);
        return events ? SymbolEditor.EventNames(model).Count : SymbolEditor.VariableNames(model).Count;
    }

    private static int FirstMatching(string xml, bool events, bool wanted)
    {
        int limit = SymbolCount(xml, events);
        for (int i = 0; i < limit; i++)
            if (SymbolIndexFixup.ReferencesTo(xml, events, i).Count > 0 == wanted) return i;
        return -1;
    }

    private static int CountAbove(string xml, bool events, int index)
    {
        int limit = SymbolCount(xml, events), total = 0;
        for (int i = index + 1; i < limit; i++) total += SymbolIndexFixup.ReferencesTo(xml, events, i).Count;
        return total;
    }

    private sealed record Snapshot(List<string> Bindings, List<string> Transitions);

    private static Snapshot Resolved(string xml)
    {
        var model = BehaviourGraphModel.Parse(xml);
        var variables = SymbolEditor.VariableNames(model);
        var events = SymbolEditor.EventNames(model);

        var bindings = new List<string>();
        foreach (var set in model.Objects.Where(o => o.Class == "hkbVariableBindingSet"))
        {
            if (!set.StructLists.TryGetValue("bindings", out var rows)) continue;
            foreach (var row in rows)
            {
                row.TryGetValue("memberPath", out string? path);
                row.TryGetValue("variableIndex", out string? raw);
                int index = int.TryParse(raw, out int v) ? v : -1;
                bindings.Add($"{path} <- {(index >= 0 && index < variables.Count ? variables[index] : "index " + index)}");
            }
        }

        var transitions = new List<string>();
        foreach (var machine in model.Objects.Where(o => o.Class == "hkbStateMachine"))
            foreach (var t in StateEditor.Transitions(model, machine.Id))
                transitions.Add($"{(t.EventId >= 0 && t.EventId < events.Count ? events[t.EventId] : "index " + t.EventId)} -> {t.ToStateId}");

        bindings.Sort(StringComparer.Ordinal);
        transitions.Sort(StringComparer.Ordinal);
        return new Snapshot(bindings, transitions);
    }

    private static bool Compare(string what, List<string> before, List<string> after, params string[] ignore)
    {
        bool Skip(string s) => ignore.Any(i => i.Length > 0 && s.Contains(i, StringComparison.Ordinal));
        var b = before.Where(x => !Skip(x)).ToList();
        var a = after.Where(x => !Skip(x)).ToList();
        var lost = b.Except(a).ToList();
        var gained = a.Except(b).ToList();

        bool same = lost.Count == 0 && gained.Count == 0;
        Console.WriteLine($"\n{what}: {b.Count} before, {a.Count} after, resolved names identical: {same}");
        foreach (string x in lost.Take(5)) Console.WriteLine("   lost   " + x);
        foreach (string x in gained.Take(5)) Console.WriteLine("   gained " + x);
        return same;
    }
}
