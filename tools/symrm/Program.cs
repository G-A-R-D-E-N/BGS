using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.Tools;

// symrm: the checks behind the claims in the README, runnable rather than described.
//
// The point of this tool is that nothing it reports is taken on trust. A structural edit is proved
// by repacking with hkxpack and reading the binary back, and the validator is proved by running it
// over Bethesda's own shipping files, where anything it reports is by definition a false alarm.
public static class Program
{
    private static string _java = "";
    private static string _jar = "";
    private static string _root = "";

    public static int Main(string[] argv)
    {
        _root = RepoRoot();

        if (argv.Length == 0) { Usage(); return 1; }

        switch (argv[0])
        {
            case "corpus": return Corpus(argv);
            case "unpack": return Unpack(argv);
            case "check": return Check(argv);
            case "events": return Events(argv);
            case "anims": return Anims(argv);
            case "repack": return Repack(argv);
            case "frames": return Frames(argv);
            case "skeleton": return Skeleton(argv);
            case "rig": return Rig(argv);
            case "remove": return Remove(argv);
            case "door": return Door(argv);
            case "link": return Link(argv);
            case "draw": return Draw(argv);
            case "test": return Tests.Run();
            default: Usage(); return 1;
        }
    }

    private static void Usage() => Console.WriteLine("""
        symrm, the verification harness for Behaviour Graph Studio.

          dotnet run --project tools/symrm/symrm.csproj -- corpus <Fallout4 - Animations.ba2> <outDir>
              Pull every vanilla behaviour .hkx out of the archive. 531 of them.

          dotnet run --project tools/symrm/symrm.csproj -- unpack <hkxDir> [everyNth] [outDir]
              Run hkxpack over them, writing to <hkxDir>/xml unless told otherwise. One JVM at a
              time on purpose; running these in parallel will bury a six core machine. everyNth
              defaults to 4, so 132 of the 531.

          dotnet run --project tools/symrm/symrm.csproj -- check <xmlDir>
              GraphValidator over every unpacked file. It should report zero errors: anything it
              says about vanilla data is a false alarm in the checker, not a fault in the game.

          dotnet run --project tools/symrm/symrm.csproj -- events <xmlDir | file.xml>
              What each declared event is used for: raised here, listened for here, or written
              somewhere this does not recognise. Reports no verdict, because an event with
              listeners and no sender in the file is the ordinary case. Over a directory it
              prints the totals and every class member pair it saw, which is how the role table
              was built.

          dotnet run --project tools/symrm/symrm.csproj -- anims <behaviour.hkx | Data folder>
              The full validator, including the checks that need the folder around the file: every
              clip whose animation is not on disk, or that the character does not declare. Point it
              at a directory and it sweeps every project root beneath it. Needs real project
              folders, not loose files.

          dotnet run --project tools/symrm/symrm.csproj -- repack <behaviour.hkx>
              Unpack, repack, unpack again, and compare the object count and the multiset of class
              names. hkxpack renumbers, so ids are expected to change and nothing else is.

          dotnet run --project tools/symrm/symrm.csproj -- frames <animation.hkx | Data folder> [tracks]
              What the binary reader gets out of an animation: duration, frame count, per bone
              track lengths, the first few frames of each, annotations, and which frame a given
              userControlledTimeFraction lands on. Point it at a directory to read every animation
              under it and report how many decode to nothing.

          dotnet run --project tools/symrm/symrm.csproj -- skeleton <skeleton.hkx> [bone]
              Bone names, parents, and a chain composed from the root, to see where the reference
              pose actually puts things. Defaults to a bone matching "Hand".

          dotnet run --project tools/symrm/symrm.csproj -- rig <skeleton.hkx | Data folder> [out.json]
              Emits the skeleton as JSON for an importer to build a rig from, and reads it straight
              back to prove the bone count, names and parents survived. A directory sweeps every
              skeleton under CharacterAssets and reports which roots fork.

          dotnet run --project tools/symrm/symrm.csproj -- remove <behaviour.hkx>
              The symbol removal round trip. Adds a variable and an event, refuses to remove one
              that is in use, removes ones that are not, repacks, reads the binary back, and
              confirms every binding and transition still resolves to the same name as before.

          dotnet run --project tools/symrm/symrm.csproj -- test
              Regression checks on graphs built in memory. No game install, no hkxpack, no JVM,
              so this one can be run on every change. Exits non zero on any failure.

          dotnet run --project tools/symrm/symrm.csproj -- door <SpecialCaseDoors Behavior.hkx> <out.hkx>
              The additive door edit: two new events and two new states that give a door a way to
              be placed already open or already closed without playing the transition, in the shape
              DN151_DoorSeal already drives. Touches no existing transition. Repacks, reads the
              binary back and runs the validator over the result.
        """);

    // Walk up for the folder holding the csproj, so the tool does not care where it is run from.
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Hkx")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static void NeedHkxPack()
    {
        _java = HkxTextEdit.FindJava("") ?? throw new InvalidOperationException("no java on PATH or in JAVA_HOME");
        _jar = HkxTextEdit.FindHkxPack("", _root) ?? throw new InvalidOperationException(
            "hkxpack-cli.jar not found; put it in tools/ or next to the FO4AnimForge checkout");
    }

    private static int Corpus(string[] argv)
    {
        if (argv.Length < 3) { Usage(); return 1; }
        int written = Ba2.ExtractMatching(argv[1], "behavior", argv[2], ".hkx", Console.WriteLine);
        Console.WriteLine($"wrote {written} behaviour files to {argv[2]}");
        return 0;
    }

    private static int Anims(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }
        NeedHkxPack();

        string target = Path.GetFullPath(argv[1]);
        return Directory.Exists(target) ? AnimsSweep(target) : AnimsOne(target);
    }

    // Every behaviour under one project root shares that root's animation folder, so the chain is
    // resolved once per root rather than once per file.
    private static int AnimsSweep(string root)
    {
        var roots = Directory.EnumerateDirectories(root, "Behaviors", SearchOption.AllDirectories)
                             .Select(d => Path.GetDirectoryName(d)!)
                             .OrderBy(d => d).ToList();

        int files = 0, errorCount = 0, warningCount = 0, chainless = 0;
        var byKind = new Dictionary<string, int>();

        foreach (string project in roots)
        {
            var chain = ProjectChain.Resolve(Path.Combine(project, "Behaviors"), _java, _jar);
            if (chain.Animations.Count == 0)
            {
                chainless++;
                Console.WriteLine($"  no animation list  {project[(root.Length + 1)..]}" +
                                  (chain.Problems.Count > 0 ? "  (" + chain.Problems[0] + ")" : ""));
            }

            foreach (string hkx in Directory.EnumerateFiles(Path.Combine(project, "Behaviors"), "*.hkx").OrderBy(f => f))
            {
                List<GraphValidator.Finding> findings;
                try
                {
                    string xml = File.ReadAllText(HkxTextEdit.Unpack(_java, _jar, hkx, Work("sweep")));
                    findings = GraphValidator.Check(xml, chain);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  THREW {Path.GetFileName(hkx)}: {ex.Message.Split('\n')[0]}");
                    continue;
                }

                files++;
                foreach (var f in findings)
                {
                    if (f.Level == GraphValidator.Level.Error) errorCount++; else warningCount++;
                    Console.WriteLine($"  {Path.GetFileName(hkx),-46} {f}");
                    string kind = f.Level + ": " + f.What.Split(',')[0].Split('\'')[0].Trim();
                    byKind[kind] = byKind.GetValueOrDefault(kind) + 1;
                }
            }
        }

        Console.WriteLine($"\n{roots.Count} project roots, {files} behaviours");
        Console.WriteLine($"{chainless} roots whose character declares no animations");
        Console.WriteLine($"{errorCount} errors, {warningCount} warnings");
        foreach (var kv in byKind.OrderByDescending(k => k.Value))
            Console.WriteLine($"  {kv.Value,5}  {kv.Key}");

        return errorCount == 0 ? 0 : 1;
    }

    private static int AnimsOne(string hkx)
    {
        var chain = ProjectChain.Resolve(hkx, _java, _jar);

        Console.WriteLine($"project root  {chain.Root}");
        Console.WriteLine($"{chain.Animations.Count} animations declared by the character");
        foreach (string anim in chain.Animations) Console.WriteLine("    " + anim);
        foreach (string problem in chain.Problems) Console.WriteLine("  problem  " + problem);

        string xml = File.ReadAllText(HkxTextEdit.Unpack(_java, _jar, hkx, Work("anims")));
        var findings = GraphValidator.Check(xml, chain);
        foreach (var f in findings) Console.WriteLine("  " + f);

        int errors = findings.Count(f => f.Level == GraphValidator.Level.Error);
        Console.WriteLine($"\n{errors} errors, {findings.Count - errors} warnings");
        return errors == 0 ? 0 : 1;
    }

    private static int Repack(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }
        NeedHkxPack();

        string hkx = Path.GetFullPath(argv[1]);
        string xmlPath = HkxTextEdit.Unpack(_java, _jar, hkx, Work("repack_in"));
        var before = RepackCheck.Take(File.ReadAllText(xmlPath));

        string packed = HkxTextEdit.Repack(_java, _jar, xmlPath);
        var after = RepackCheck.Take(File.ReadAllText(HkxTextEdit.Unpack(_java, _jar, packed, Work("repack_out"))));

        var drift = RepackCheck.Compare(before, after);
        Console.WriteLine($"{Path.GetFileName(hkx)}: {drift}");
        Console.WriteLine(drift.Clean ? "clean" : "DRIFT");
        return drift.Clean ? 0 : 1;
    }

    // What the binary reader actually gets out of an animation, before any of it is put on screen.
    // A directory reports one line per file, which is how a decode that works on one animation and
    // quietly returns nothing on a hundred others gets caught.
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

        // A digest the independent Python probe can produce too, so "the reader decodes it" can be
        // checked against "the packing is right" rather than assumed from it.
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
            Console.WriteLine($"\n  {bone}: {track.Translations.Count} translations, " +
                              $"{track.Rotations.Count} rotations, {track.Scales.Count} scales");

            int frames = Math.Min(4, Math.Max(track.Rotations.Count, track.Translations.Count));
            for (int f = 0; f < frames; f++)
            {
                string pos = f < track.Translations.Count
                    ? $"pos {track.Translations[f].X,8:F3} {track.Translations[f].Y,8:F3} {track.Translations[f].Z,8:F3}" : "";
                string rot = f < track.Rotations.Count
                    ? $"  rot {track.Rotations[f].X,7:F4} {track.Rotations[f].Y,7:F4} {track.Rotations[f].Z,7:F4} {track.Rotations[f].W,7:F4}" : "";
                Console.WriteLine($"    frame {f,4}  t={f * anim.FrameDuration,7:F3}s  {pos}{rot}");
            }
        }

        // The question the clip work actually asks: a variable drives userControlledTimeFraction,
        // and this says which frame that lands on.
        Console.WriteLine();
        foreach (float fraction in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            Console.WriteLine($"  userControlledTimeFraction {fraction:F2} -> frame {FrameAt(anim, fraction)} " +
                              $"of {Math.Max(anim.NumFrames - 1, 0)}");

        return 0;
    }

    // An animation carries no bone names of its own. Its annotation tracks are named after bones by
    // convention and are empty in plenty of vanilla files, so the real name comes from the skeleton
    // through transformTrackToBoneIndices. The skeleton sits in the project's CharacterAssets, one
    // level up from Animations, and reading it needs no JVM.
    private static HkxSkeleton? SiblingSkeleton(string animationPath)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(animationPath)) ?? "");
        for (int up = 0; up < 4 && dir != null; up++, dir = dir.Parent)
        {
            string assets = Path.Combine(dir.FullName, "CharacterAssets");
            if (!Directory.Exists(assets)) continue;

            foreach (string file in Directory.EnumerateFiles(assets, "*.hkx").OrderBy(f => f))
            {
                try { return new HkxBinaryReader().ReadSkeleton(file); }
                catch { /* not a skeleton, try the next */ }
            }
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

    private static int FrameAt(HkxAnimationData anim, float fraction)
    {
        if (anim.NumFrames <= 1) return 0;
        int frame = (int)Math.Round(Math.Clamp(fraction, 0f, 1f) * (anim.NumFrames - 1));
        return frame;
    }

    // Measures the summary the way the other checks were measured, because a table built from a
    // corpus is only honest while the corpus still fits in it. Anything landing in "not recognised"
    // is a class member pair the table has never seen.
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
            string xml = File.ReadAllText(file);
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

    private static string Short(string file, string root) =>
        file.StartsWith(root, StringComparison.Ordinal) ? file[(root.Length + 1)..] : file;

    // Answers one question before any rig work starts: is the reference pose stored parent relative
    // or already in world space. Composing a chain and looking at where the bones land settles it,
    // and guessing it wrong would put every bone in the wrong place.
    private static int Skeleton(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        var skeleton = new HkxBinaryReader().ReadSkeleton(Path.GetFullPath(argv[1]));
        Console.WriteLine($"{skeleton.Name}: {skeleton.BoneNames.Count} bones, " +
                          $"{skeleton.ParentIndices.Count} parent indices, {skeleton.ReferencePose.Count} poses");

        var world = new System.Numerics.Matrix4x4[skeleton.BoneNames.Count];
        for (int i = 0; i < skeleton.BoneNames.Count; i++)
        {
            var p = i < skeleton.ReferencePose.Count ? skeleton.ReferencePose[i] : new HkxBonePose();
            var local = System.Numerics.Matrix4x4.CreateScale(p.Scale)
                      * System.Numerics.Matrix4x4.CreateFromQuaternion(p.Rotation)
                      * System.Numerics.Matrix4x4.CreateTranslation(p.Translation);

            int parent = i < skeleton.ParentIndices.Count ? skeleton.ParentIndices[i] : -1;
            world[i] = parent >= 0 && parent < i ? local * world[parent] : local;
        }

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
            var w = world[i].Translation;
            Console.WriteLine($"  {skeleton.BoneNames[i],-28} " +
                              $"{t.X,10:F3}{t.Y,11:F3}{t.Z,11:F3}   " +
                              $"{w.X,10:F3}{w.Y,11:F3}{w.Z,11:F3}");
        }
        return 0;
    }

    // Emits the skeleton as JSON and immediately reads it back, because an emitter that quietly
    // drops or reorders a bone produces a file that looks fine and rigs wrong. A directory sweeps.
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
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        return dir;
    }

    private static int Unpack(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }
        NeedHkxPack();

        int everyNth = argv.Length > 2 ? int.Parse(argv[2]) : 4;
        // Unpack copies the input next to its output, so the destination has to be a different
        // folder from the corpus or every file collides with itself.
        string outDir = argv.Length > 3 ? argv[3] : Path.Combine(argv[1], "xml");
        Directory.CreateDirectory(outDir);

        var files = Directory.GetFiles(argv[1], "*.hkx").OrderBy(f => f).ToList();
        int done = 0, failed = 0;

        for (int i = 0; i < files.Count; i++)
        {
            if (i % everyNth != 0) continue;
            try { HkxTextEdit.Unpack(_java, _jar, files[i], outDir); done++; }
            catch (Exception ex) { failed++; Console.WriteLine($"  failed {Path.GetFileName(files[i])}: {ex.Message.Split('\n')[0]}"); }
        }

        Console.WriteLine($"unpacked {done} of {files.Count} into {outDir}, {failed} failed");
        return failed == 0 ? 0 : 1;
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
            try { findings = GraphValidator.Check(File.ReadAllText(file)); }
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

    private static int Remove(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }
        NeedHkxPack();

        string work = Path.Combine(Path.GetTempPath(), "symrm", Path.GetFileNameWithoutExtension(argv[1]));
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);

        string xml = File.ReadAllText(HkxTextEdit.Unpack(_java, _jar, argv[1], work));
        Console.WriteLine("FILE " + Path.GetFileName(argv[1]));
        Report("BEFORE", xml);
        var before = Resolved(xml);

        Console.WriteLine("\n--- add a variable and an event ---");
        xml = SymbolEditor.AddVariable(xml, "fSymrmProbe", SymbolEditor.VariableType.Real, out int newVar);
        xml = SymbolEditor.AddEvent(xml, "SymrmProbeEvent", out int newEvent);
        var counts = SymbolEditor.Audit(BehaviourGraphModel.Parse(xml));
        Console.WriteLine($"  variable index {newVar}, event index {newEvent}");
        Console.WriteLine($"  {counts}   consistent={counts.VariablesConsistent && counts.EventsConsistent}");

        var names = SymbolEditor.VariableNames(BehaviourGraphModel.Parse(xml));
        int inUse = FirstMatching(xml, events: false, wanted: true);
        if (inUse >= 0)
        {
            Console.WriteLine($"\n--- refuse to remove variable {inUse} '{names[inUse]}', which is in use ---");
            string untouched = SymbolEditor.RemoveVariable(xml, inUse, force: false, out var blockers);
            Console.WriteLine($"  blockers {blockers.Count}: {string.Join(", ", blockers.Distinct().Take(3))}");
            Console.WriteLine($"  file unchanged: {untouched == xml}");
        }

        int freeVar = FirstMatching(xml, events: false, wanted: false);
        string removedVar = freeVar >= 0 ? names[freeVar] : "";
        if (freeVar >= 0)
        {
            Console.WriteLine($"\n--- remove variable {freeVar} '{removedVar}', which nothing references ---");
            Console.WriteLine($"  references above it that must shift: {CountAbove(xml, events: false, freeVar)}");
            xml = SymbolEditor.RemoveVariable(xml, freeVar, force: false, out _);
        }

        var events = SymbolEditor.EventNames(BehaviourGraphModel.Parse(xml));
        int freeEvent = FirstMatching(xml, events: true, wanted: false);
        string removedEvent = freeEvent >= 0 ? events[freeEvent] : "";
        if (freeEvent >= 0)
        {
            Console.WriteLine($"--- remove event {freeEvent} '{removedEvent}', which nothing references ---");
            Console.WriteLine($"  references above it that must shift: {CountAbove(xml, events: true, freeEvent)}");
            xml = SymbolEditor.RemoveEvent(xml, freeEvent, force: false, out _);
        }

        string packedDir = Path.Combine(work, "repack");
        Directory.CreateDirectory(packedDir);
        string xmlPath = Path.Combine(packedDir, "edited.xml");
        File.WriteAllText(xmlPath, xml);
        string packed = HkxTextEdit.Repack(_java, _jar, xmlPath);
        Console.WriteLine($"\nrepacked to {new FileInfo(packed).Length} bytes, reading the binary back");
        string back = File.ReadAllText(HkxTextEdit.Unpack(_java, _jar, packed, Path.Combine(packedDir, "back")));

        Report("AFTER, ROUND TRIPPED", back);
        var after = Resolved(back);

        bool ok = Compare("bindings", before.Bindings, after.Bindings, "fSymrmProbe", removedVar)
                & Compare("transitions", before.Transitions, after.Transitions, "SymrmProbeEvent", removedEvent);

        Console.WriteLine("\n--- validator on the round tripped file ---");
        var findings = GraphValidator.Check(back);
        int errors = findings.Count(f => f.Level == GraphValidator.Level.Error);
        Console.WriteLine($"  {errors} errors, {findings.Count - errors} warnings");
        foreach (var f in findings.Take(8)) Console.WriteLine("   " + f);

        return ok && errors == 0 ? 0 : 1;
    }

    // What the canvas will actually draw, before and after a link is retargeted.
    //
    // Retargeting is the ordinary way to change what a node points at, and it detaches whatever the
    // link used to lead to. Drawing only what the root reaches makes all of that vanish, which is
    // what "I dragged something and all my other nodes were removed" was.
    private static int Draw(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }
        NeedHkxPack();

        string work = Path.Combine(Path.GetTempPath(), "symrm", "draw");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);

        string xml = File.ReadAllText(HkxTextEdit.Unpack(_java, _jar, argv[1], work));
        Console.WriteLine($"FILE {Path.GetFileName(argv[1])}");
        Show("before any edit", xml);

        var model = BehaviourGraphModel.Parse(xml);
        var graph = model.Objects.First(o => o.Class == "hkbBehaviorGraph");
        var leaf = model.Objects.First(o => o.Class == "hkbClipGenerator");

        Console.WriteLine($"\n--- drag #{graph.Id}.rootGenerator onto clip #{leaf.Id}, which replaces what it held ---");
        xml = GraphLinks.Connect(xml, graph.Id, "rootGenerator", leaf.Id, out string note);
        Console.WriteLine("  " + note);
        Show("after the retarget", xml);
        return 0;
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
            var targets = GraphLinks.OutSlots(model, current).SelectMany(s => s.Targets)
                .Concat(current.StructLists.Values.SelectMany(rows => rows)
                    .SelectMany(row => row.Values)
                    .Where(v => v.StartsWith('#')).Select(v => v[1..]));

            foreach (string target in targets)
            {
                if (!seen.Add(target)) continue;
                var next = model.Get(target);
                if (next != null) queue.Enqueue(next);
            }
        }
        return seen.Count;
    }

    // Exercises the wiring the graph view performs when a link is dragged between two ports. The
    // canvas cannot be driven from a script, so this drives the same calls it makes.
    private static int Link(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }
        NeedHkxPack();

        string work = Path.Combine(Path.GetTempPath(), "symrm", "link");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);

        string xml = File.ReadAllText(HkxTextEdit.Unpack(_java, _jar, argv[1], work));
        var model = BehaviourGraphModel.Parse(xml);
        Console.WriteLine($"FILE {Path.GetFileName(argv[1])}   {model.Objects.Count} objects");

        Console.WriteLine("\n--- ports the canvas offers, which is a port per link the class may hold ---");
        foreach (var obj in model.Objects.Take(60))
        {
            var slots = GraphLinks.OutSlots(model, obj);
            if (slots.Count == 0) continue;
            Console.WriteLine($"  #{obj.Id,-4} {obj.Class,-34} {string.Join("  ", slots.Select(s => $"{s}({s.Targets.Count})"))}");
        }

        string machine = model.Objects.First(o => o.Class == "hkbStateMachine").Id;
        var blender = model.Objects.FirstOrDefault(o => o.Class == "hkbBlenderGenerator");
        var state = model.Objects.First(o => o.Class == "hkbStateMachineStateInfo");

        Console.WriteLine("\n--- create a clip with nothing pointing at it, the way a drag to empty canvas does ---");
        xml = GeneratorEditor.Add(xml, "clip", "SymrmLinkClip", "Meshes\\Probe\\link.hkx", "", out string clip);
        Console.WriteLine($"  clip is #{clip}");

        if (blender != null)
        {
            xml = GraphLinks.Connect(xml, blender.Id, "children", clip, out string note);
            Console.WriteLine($"  drag blender.children  -> {note}");
        }

        xml = GraphLinks.Connect(xml, machine, "states", clip, out string stateNote);
        Console.WriteLine($"  drag machine.states    -> {stateNote}");

        xml = GraphLinks.Connect(xml, state.Id, "generator", clip, out string genNote);
        Console.WriteLine($"  drag state.generator   -> {genNote}");

        string back = RoundTripTo(xml, Path.Combine(work, "wired"));
        var wired = BehaviourGraphModel.Parse(back);
        Console.WriteLine($"\nAFTER ROUND TRIP: {wired.Objects.Count} objects");

        var clipAfter = wired.Objects.First(o => o.Class == "hkbClipGenerator" && o.Str("name") == "SymrmLinkClip");
        var holders = GeneratorEditor.ReferencesTo(wired, clipAfter.Id);
        Console.WriteLine($"  the clip survived as #{clipAfter.Id}, referenced by {holders.Count}: " +
                          string.Join(", ", holders.Select(h => $"#{h} {wired.Get(h)?.Class}")));

        var blenderAfter = wired.Objects.FirstOrDefault(o => o.Class == "hkbBlenderGenerator");
        if (blenderAfter != null)
        {
            Console.WriteLine("\n--- drag the blender child off again ---");
            string wrapper = blenderAfter.Refs("children")
                .First(id => wired.Get(id)?.Ref("generator") == clipAfter.Id);
            back = GraphLinks.Disconnect(back, blenderAfter.Id, "children", wrapper, out string offNote);
            Console.WriteLine($"  {offNote}");
        }

        string final = RoundTripTo(back, Path.Combine(work, "unwired"));
        var end = BehaviourGraphModel.Parse(final);
        Console.WriteLine($"\nFINAL: {end.Objects.Count} objects, " +
                          $"{end.Objects.Count(o => o.Class == "hkbBlenderGeneratorChild")} blender children left");

        Console.WriteLine("\n--- validator ---");
        var findings = GraphValidator.Check(final);
        int errors = findings.Count(f => f.Level == GraphValidator.Level.Error);
        Console.WriteLine($"  {errors} errors, {findings.Count - errors} warnings");
        foreach (var f in findings.Take(8)) Console.WriteLine("   " + f);
        return errors == 0 ? 0 : 1;
    }

    private static string RoundTripTo(string xml, string dir)
    {
        Directory.CreateDirectory(dir);
        string xmlPath = Path.Combine(dir, "edited.xml");
        File.WriteAllText(xmlPath, xml);
        string packed = HkxTextEdit.Repack(_java, _jar, xmlPath);
        Console.WriteLine($"repacked to {new FileInfo(packed).Length} bytes");
        return File.ReadAllText(HkxTextEdit.Unpack(_java, _jar, packed, Path.Combine(dir, "back")));
    }

    // Adds DN151_DoorSeal's StartOpen and StartClosed to SpecialCaseDoors, which does not have them.
    //
    // Strictly additive. Two events, two states, two sequence generators, and four new transition
    // entries. No existing transition is retargeted, because the same event ids are shared across
    // every door that uses this behaviour and changing one would change all of them.
    //
    // Each new event enters a state that PLAYS its sequence and then moves on to the resting state
    // when the sequence sends its end event. Worth knowing that vanilla does the opposite:
    // SwitchDoorExLarge01 points StartOpen straight at its held pose state and reaches the playing
    // states through Play01 instead, so there a door placed open is simply open, with no animation.
    // Sending StartOpen here will make the door visibly open itself as the cell loads.
    private static int Door(string[] argv)
    {
        if (argv.Length < 3) { Usage(); return 1; }
        NeedHkxPack();

        string work = Path.Combine(Path.GetTempPath(), "symrm", "door");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);

        string xml = File.ReadAllText(HkxTextEdit.Unpack(_java, _jar, argv[1], work));
        var model = BehaviourGraphModel.Parse(xml);

        string machine = model.Objects.First(o => o.Class == "hkbStateMachine").Id;
        var states = StateEditor.States(model, machine);
        Console.WriteLine($"BEFORE: {model.Objects.Count} objects, {states.Count} states, " +
                          $"{StateEditor.Transitions(model, machine).Count} transitions, " +
                          $"{SymbolEditor.EventNames(model).Count} events");

        int StateIdNamed(string name) => states.FirstOrDefault(s => s.Name == name)?.StateId
            ?? throw new InvalidOperationException($"this graph has no state called {name}");
        int EventNamed(string name)
        {
            int i = SymbolEditor.EventNames(BehaviourGraphModel.Parse(xml)).IndexOf(name);
            return i >= 0 ? i : throw new InvalidOperationException($"this graph declares no event called {name}");
        }

        // Reuse whatever blending effect the door's own transitions already use rather than
        // inventing one, so the new transitions blend exactly like the existing ones.
        string effect = FirstTransitionEffect(model, machine, xml);
        Console.WriteLine($"  reusing transition effect {effect}");

        int openedState = StateIdNamed("Opened");
        int closedState = StateIdNamed("Closed");

        // enterState is where the new event actually lands. StartOpen goes straight to the held open
        // pose, because that is what vanilla does: SwitchDoorExLarge01 sends StartOpen to its posed
        // state and reaches the playing states through Play01. A door placed open should be open,
        // not open itself while the cell is still loading. StartClosed keeps the playing shape.
        foreach (var (eventName, stateName, sequence, endEvent, target, poseEntry) in new[]
                 {
                     ("StartOpen", "StartOpening", "Opening", "Opened", openedState, true),
                     ("StartClosed", "StartClosing", "Closing", "Closed", closedState, false),
                 })
        {
            xml = SymbolEditor.AddEvent(xml, eventName, out int eventId);
            int enterState = target;

            // A pose entry event needs no state of its own. It lands on one the door already has,
            // and building a playing state for it would leave that state with nothing pointing at
            // it, duplicating the Open state the graph already has.
            if (!poseEntry)
            {
                xml = GeneratorEditor.Add(xml, "sequence", stateName, sequence, "", out string generator);
                xml = StateEditor.AddState(xml, machine, stateName, "#" + generator, out string stateObject, out enterState);
                // Out of the new state on the event the sequence itself sends when it finishes.
                xml = StateEditor.AddTransition(xml, machine, stateObject, target, EventNamed(endEvent), effect);
            }

            // Into it from anywhere, which is how this graph already handles pose entry events.
            xml = StateEditor.AddTransition(xml, machine, "", enterState, eventId, effect);

            Console.WriteLine(poseEntry
                ? $"  {eventName,-12} event {eventId,2}  ->  state {enterState} directly, the held pose, no animation, no new state"
                : $"  {eventName,-12} event {eventId,2}  ->  state {enterState} '{stateName}' " +
                  $"playing sequence '{sequence}'  ->  on {endEvent} to state {target}");
        }

        string xmlPath = Path.Combine(work, "edited.xml");
        File.WriteAllText(xmlPath, xml);
        string packed = HkxTextEdit.Repack(_java, _jar, xmlPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(argv[2]))!);
        File.Copy(packed, argv[2], true);
        Console.WriteLine($"\nrepacked to {new FileInfo(argv[2]).Length} bytes at {argv[2]}");

        string back = File.ReadAllText(HkxTextEdit.Unpack(_java, _jar, argv[2], Path.Combine(work, "back")));
        var after = BehaviourGraphModel.Parse(back);
        string machineAfter = after.Objects.First(o => o.Class == "hkbStateMachine").Id;
        var statesAfter = StateEditor.States(after, machineAfter);

        Console.WriteLine($"AFTER:  {after.Objects.Count} objects, {statesAfter.Count} states, " +
                          $"{StateEditor.Transitions(after, machineAfter).Count} transitions, " +
                          $"{SymbolEditor.EventNames(after).Count} events\n");

        var events = SymbolEditor.EventNames(after);
        foreach (var s in statesAfter)
        {
            var generator = after.Get(s.GeneratorRef.TrimStart('#'));
            Console.WriteLine($"  state {s.StateId,2} {s.Name,-16} {generator?.Class} '{generator?.Str("pSequence")}'");
        }
        Console.WriteLine();
        foreach (var t in StateEditor.Transitions(after, machineAfter))
            Console.WriteLine($"  {(t.Wildcard ? "wildcard" : "        ")} on {events[t.EventId],-34} -> state {t.ToStateId}");

        Console.WriteLine("\n--- validator ---");
        var findings = GraphValidator.Check(back);
        int errors = findings.Count(f => f.Level == GraphValidator.Level.Error);
        Console.WriteLine($"  {errors} errors, {findings.Count - errors} warnings");
        foreach (var f in findings) Console.WriteLine("   " + f);
        return errors == 0 ? 0 : 1;
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

    // Resolve every index to the name it lands on. A renumbering that went wrong then shows up as a
    // changed name rather than a changed number, which is the only comparison worth making.
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
