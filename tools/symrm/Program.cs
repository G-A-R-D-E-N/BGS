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
            case "states": return States(argv);
            case "events": return Events(argv);
            case "anims": return Anims(argv);
            case "repack": return Repack(argv);
            case "frames": return Frames(argv);
            case "scale": return Scale(argv);
            case "skeleton": return Skeleton(argv);
            case "rig": return Rig(argv);
            case "extract": return Extract(argv);
            case "pose": return Pose(argv);
            case "channels": return Channels(argv);
            case "packfile": return Packfile(argv);
            case "objects": return Objects(argv);
            case "crosscheck": return CrossCheck(argv);
            case "savecheck": return SaveCheck(argv);
            case "mesh": return Mesh(argv);
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

          dotnet run --project tools/symrm/symrm.csproj -- states <xmlDir>
              Every state in the corpus and what its generator resolves to, by class. This is the
              measurement behind the reading claim in the README, so the number there can be
              rechecked rather than taken on trust. Unpack with everyNth 1 first or the count is
              of whatever subset was unpacked.

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

          dotnet run --project tools/symrm/symrm.csproj -- scale <animation.hkx | Data folder>
              Every animation whose scale is not the identity, with the range it spans and whether
              any of it is zero. 130 of the 13133 vanilla spline animations scale something; all 856
              lossless ones leave scale empty, which is why that branch is still unproven.

          dotnet run --project tools/symrm/symrm.csproj -- skeleton <skeleton.hkx> [bone]
              Bone names, parents, and a chain composed from the root, to see where the reference
              pose actually puts things. Defaults to a bone matching "Hand".

          dotnet run --project tools/symrm/symrm.csproj -- rig <skeleton.hkx | Data folder> [out.json]
              Emits the skeleton as JSON for an importer to build a rig from, and reads it straight
              back to prove the bone count, names and parents survived. A directory sweeps every
              skeleton under CharacterAssets and reports which roots fork.

          dotnet run --project tools/symrm/symrm.csproj -- extract <archive.ba2> <substring> <outDir> [.ext] [--tree]
              Anything from a BA2 whose path contains the substring, which is how a corpus for the
              commands here gets built without a mod manager in the way. Flat by default, because 531
              files called Behavior.hkx would otherwise overwrite each other; --tree keeps the
              archive's folders, which is what resolving a project chain afterwards needs.

          dotnet run --project tools/symrm/symrm.csproj -- objects <file.hkx> [class]
              Every object in a file and what class it is, or with a class named, that class's
              fields read straight out of the bytes. Also reports how many objects are of a class
              whose field layout we do not have, which is the number worth watching.

          dotnet run --project tools/symrm/symrm.csproj -- crosscheck <file.hkx>
              Reads every field it can out of the bytes and compares it against what hkxpack says
              the same field holds. Two independent readings of one file, ours by byte offset and
              hkxpack's by its own schema, so agreement across a whole file is what says the offsets
              are right rather than plausible. Needs Java and the jar. Exits non zero on any
              disagreement.

          dotnet run --project tools/symrm/symrm.csproj -- packfile <file.hkx | folder>
              Takes a .hkx apart and puts it back together, and reports whether the result is the
              same file. This is the gate on writing .hkx bytes without hkxpack in the way: every
              offset in a packfile is derived from the sizes of what precedes it, so a byte for byte
              match means the derivation is right. Exits non zero on any file that differs or cannot
              be read. Needs no game and no Java.

          dotnet run --project tools/symrm/symrm.csproj -- channels <skeleton.hkx> <animation.hkx | folder>
              How many bone tracks leave each channel undriven, and for the undriven translations,
              how far the skeleton's reference pose puts that bone from its parent. Havok treats an
              undriven channel as zero translation and unit scale, so a track that leaves a bone's
              translation undriven while the rig places that bone away from zero is the case where
              the two readings disagree and one of them moves the bone.

          dotnet run --project tools/symrm/symrm.csproj -- pose <skeleton.hkx> <animation.hkx> [frame]
              The pose the viewport draws, printed: which bones a track drives, how far the last
              frame is from the first, and every bone's world position at a frame. Same
              AnimationPose call the window makes, so a shape that looks wrong on screen can be read
              as numbers here.

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
                    string xml = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, hkx, Work("sweep")));
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

        string xml = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, hkx, Work("anims")));
        var findings = GraphValidator.Check(xml, chain);
        foreach (var f in findings) Console.WriteLine("  " + f);

        int errors = findings.Count(f => f.Level == GraphValidator.Level.Error);
        Console.WriteLine($"\n{errors} errors, {findings.Count - errors} warnings");
        return errors == 0 ? 0 : 1;
    }

    // The reading claim in the README, re-runnable. It walks with the tool's own model rather than
    // a script, so what it reports is what the tool sees, not what a separate parser sees.
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

    private static int Repack(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }
        NeedHkxPack();

        string hkx = Path.GetFullPath(argv[1]);
        string xmlPath = HkxTextEdit.Unpack(_java, _jar, hkx, Work("repack_in"));
        var before = RepackCheck.Take(HkxTextEdit.ReadXml(xmlPath));

        string packed = HkxTextEdit.Repack(_java, _jar, xmlPath);
        var after = RepackCheck.Take(HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, packed, Work("repack_out"))));

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
                // A flat 1,1,1 on every row of every animation would bury the ones that really scale.
                string scl = scaled && f < track.Scales.Count
                    ? $"  scale {track.Scales[f].X,7:F4} {track.Scales[f].Y,7:F4} {track.Scales[f].Z,7:F4}" : "";
                Console.WriteLine($"    frame {f,4}  t={f * anim.FrameDuration,7:F3}s  {pos}{rot}{scl}");
            }
        }

        // The question the clip work actually asks: a variable drives userControlledTimeFraction,
        // and this says which frame that lands on.
        Console.WriteLine();
        foreach (float fraction in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            Console.WriteLine($"  userControlledTimeFraction {fraction:F2} -> frame {anim.FrameAt(fraction)} " +
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

    // Scale was decoded and then never shown anywhere, so nothing said whether it was right. This
    // sweeps for animations whose scale is not the identity and reports what came out, which is the
    // only way to find real data to check the decode against.
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
                        // A scale of zero collapses whatever it drives. Worth counting separately from
                        // a merely unusual value, because it is the shape a decode bug takes.
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

    // Answers one question before any rig work starts: is the reference pose stored parent relative
    // or already in world space. Composing a chain and looking at where the bones land settles it,
    // and guessing it wrong would put every bone in the wrong place.
    // Anything out of a BA2 whose path contains a substring, which is how a corpus for the checks
    // below gets built without a mod manager in the way.
    private static int Extract(string[] argv)
    {
        if (argv.Length < 4) { Usage(); return 1; }

        bool tree = Array.IndexOf(argv, "--tree") >= 0;
        string extension = argv.Length > 4 && argv[4] != "--tree" ? argv[4] : ".hkx";
        int written = Ba2.ExtractMatching(Path.GetFullPath(argv[1]), argv[2], Path.GetFullPath(argv[3]),
                                          extension, Console.WriteLine, tree);
        Console.WriteLine($"wrote {written} files to {Path.GetFullPath(argv[3])}");
        return written > 0 ? 0 : 1;
    }

    // The pose the viewport draws, printed. Same AnimationPose call the window makes, so a shape that
    // looks wrong on screen can be read as numbers here rather than argued about.
    // Puts a real edit through the whole save path and then checks the file that came out, which is
    // a stronger question than whether reading agrees. Changes a few values, writes them into the
    // bytes, and then asks three things of the result: hkxpack can still read it, every value in it
    // still agrees with our reading of it, and it differs from the original only where it was meant
    // to. The last is the one that catches a save that quietly damages something elsewhere.
    private static int SaveCheck(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }
        NeedHkxPack();

        string file = Path.GetFullPath(argv[1]);
        // Named after the file rather than shared. savecheck calls crosscheck, which unpacks into a
        // directory of its own, and a fixed name means one run wiping the directory another run is
        // still reading from. That showed up as hkxpack "produced no XML" on a file that passes on
        // its own, which reads as a bug in the save rather than in the harness around it.
        string work = Path.Combine(Path.GetTempPath(),
                                   "symrm-savecheck-" + Path.GetFileNameWithoutExtension(file));
        if (Directory.Exists(work)) Directory.Delete(work, true);

        string xmlFile = HkxTextEdit.Unpack(_java, _jar, file, work);
        string original = HkxTextEdit.ReadXml(xmlFile);

        if (!NullSaveIsByteIdentical(file, original)) return 1;
        if (!ResizeIsRefused(file, original)) return 1;

        var edits = Invent(original);
        if (edits.Count == 0)
        {
            Console.WriteLine($"{Path.GetFileName(file)}: nothing here to change, skipped");
            return 0;
        }

        string edited = original;
        foreach (var (was, now) in edits) edited = ReplaceFirst(edited, was, now);

        var plan = NativeSave.Compare(original, edited);
        if (!plan.Possible)
        {
            Console.WriteLine($"{Path.GetFileName(file)}: refused, {plan.Refusal}");
            return 1;
        }

        byte[] saved = NativeSave.Apply(file, plan);
        string savedPath = Path.Combine(work, "saved-" + Path.GetFileName(file));
        File.WriteAllBytes(savedPath, saved);

        byte[] before = File.ReadAllBytes(file);
        int changedBytes = before.Length == saved.Length
            ? Enumerable.Range(0, saved.Length).Count(i => before[i] != saved[i])
            : -1;

        Console.WriteLine($"{Path.GetFileName(file)}: {plan.Changes.Count} value(s) changed, " +
                          (changedBytes < 0
                              ? $"BUT THE FILE CHANGED SIZE, {before.Length} to {saved.Length}"
                              : $"{changedBytes} bytes differ from the original"));
        foreach (var change in plan.Changes.Take(4)) Console.WriteLine("    " + change);

        if (changedBytes < 0) return 1;

        // The saved file has to survive being read by the other implementation, and then agree with
        // ours field for field. Reusing crosscheck means the number quoted here is the same measure
        // as the one quoted for an unedited file.
        int verdict = CrossCheck(new[] { "crosscheck", savedPath });

        // And the change has to actually be in there, or a save that wrote nothing would pass every
        // check above by doing nothing at all.
        string savedXml = HkxTextEdit.ReadXml(
            HkxTextEdit.Unpack(_java, _jar, savedPath, Path.Combine(work, "reread")));

        int landed = edits.Count(e => savedXml.Contains(e.Now, StringComparison.Ordinal));
        Console.WriteLine($"  {landed} of {edits.Count} edited value(s) present in the saved file");

        return verdict == 0 && landed == edits.Count ? 0 : 1;
    }

    /// A few edits that exercise different widths: a float, a whole word, and a single byte flag.
    /// Picked out of the file rather than fixed, so this runs on whatever it is pointed at.
    /// Saving a file without changing anything has to give back the file that went in, byte for byte.
    /// This is the check that matters most before saving is switched over: it is the one case where
    /// the right answer is known exactly and in advance, so any drift at all is the writer's fault
    /// and not a judgement call. Every other check compares one reading to another reading.
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

        byte[] before = File.ReadAllBytes(file);
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

    /// The one case the writer cannot handle is anything that changes a size, because every offset in
    /// a packfile is derived from the sizes of what precedes it. That has to be a hard refusal rather
    /// than a best effort, so this hands it a longer string and requires two things: that the plan
    /// says no, and that applying that plan throws rather than writing something.
    private static bool ResizeIsRefused(string file, string originalXml)
    {
        // animationName rather than the first name in the document. The first one belongs to an
        // inline struct inside hkRootLevelContainer, so lengthening it is refused for being inside
        // an array of struct, whatever the string rule says. The guard would then still pass with
        // strings wrongly marked writable, which is the one regression it exists to catch.
        var match = System.Text.RegularExpressions.Regex.Match(
            originalXml, "<hkparam name=\"animationName\">([^<]{3,})</hkparam>");
        if (!match.Success)
        {
            Console.WriteLine("  resize guard: no string field to lengthen here, skipped");
            return true;
        }

        string longer = match.Value.Replace(match.Groups[1].Value,
                                            match.Groups[1].Value + "_longer_than_it_was");
        var plan = NativeSave.Compare(originalXml, ReplaceFirst(originalXml, match.Value, longer));

        if (plan.Possible)
        {
            Console.WriteLine("  resize guard: FAILED, lengthening a string was accepted as writable");
            return false;
        }

        try
        {
            NativeSave.Apply(file, plan);
            Console.WriteLine("  resize guard: FAILED, applying a refused plan wrote bytes anyway");
            return false;
        }
        catch (InvalidOperationException)
        {
            // The reason matters as much as the refusal. A refusal that arrives for some other
            // reason leaves the string rule itself untested.
            if (plan.Refusal?.Contains("stringptr", StringComparison.Ordinal) != true)
            {
                Console.WriteLine($"  resize guard: FAILED, refused for the wrong reason, {plan.Refusal}");
                return false;
            }

            Console.WriteLine($"  resize guard: refused as it should, {plan.Refusal}");
            return true;
        }
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

        // An animation carries none of the fields above, and it is the case worth proving: the old
        // route refused to write one at all, because the XML cannot carry a lossless compressed
        // animation without cutting it short. duration is a plain real on hkaAnimation, so changing
        // it exercises a real save of the very format that used to be refused.
        Try("<hkparam name=\"duration\">[0-9.]+</hkparam>",
            "<hkparam name=\"duration\">3.5</hkparam>");
        return edits;
    }

    private static string ReplaceFirst(string text, string was, string now)
    {
        int at = text.IndexOf(was, StringComparison.Ordinal);
        return at < 0 ? text : text[..at] + now + text[(at + was.Length)..];
    }

    // Reads every field we can out of the raw bytes and compares it to what hkxpack says the same
    // field holds. Two independent readings of the same file: ours by byte offset from layouts read
    // out of the game, hkxpack's by its own schema. Agreement across a whole file is what turns
    // "these offsets look plausible" into "these offsets are right", and it is the check that has to
    // pass before anything writes bytes for real.
    private static int CrossCheck(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        NeedHkxPack();
        string file = Path.GetFullPath(argv[1]);
        string work = Path.Combine(Path.GetTempPath(),
                                   "symrm-crosscheck-" + Path.GetFileNameWithoutExtension(file));
        if (Directory.Exists(work)) Directory.Delete(work, true);

        string xmlFile = HkxTextEdit.Unpack(_java, _jar, file, work);

        var byClass = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.Ordinal);
        foreach (var element in System.Xml.Linq.XDocument.Load(xmlFile).Descendants("hkobject"))
        {
            string? cls = element.Attribute("class")?.Value;
            if (cls == null) continue;

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in element.Elements("hkparam"))
            {
                string? name = p.Attribute("name")?.Value;
                if (name != null) fields[name] = (p.Value ?? "").Trim();
            }
            if (!byClass.TryGetValue(cls, out var list)) byClass[cls] = list = new();
            list.Add(fields);
        }

        var objects = new PackfileObjects(PackfileImage.Read(file));
        int compared = 0, agreed = 0;
        var disagreements = new List<string>();

        foreach (var group in objects.Instances.GroupBy(i => i.ClassName))
        {
            if (!byClass.TryGetValue(group.Key, out var theirs)) continue;
            var ours = group.ToList();
            if (ours.Count != theirs.Count)
            {
                disagreements.Add($"{group.Key}: we see {ours.Count}, hkxpack sees {theirs.Count}");
                continue;
            }

            var members = HavokClasses.Shipped.Members(group.Key);
            for (int i = 0; i < ours.Count; i++)
            {
                foreach (var member in members)
                {
                    if (!theirs[i].TryGetValue(member.Name, out string? expected)) continue;

                    string? actual = Rendered(objects, ours[i], member);
                    if (actual == null) continue;

                    compared++;
                    if (Same(actual, expected)) { agreed++; continue; }
                    if (disagreements.Count < 12)
                        disagreements.Add($"{group.Key}[{i}].{member.Name} (+{member.Offset}): " +
                                          $"we read {actual}, hkxpack says {expected}");
                }
            }
        }

        Console.WriteLine($"{Path.GetFileName(file)}: {compared} field values compared against hkxpack, " +
                          $"{agreed} agreed, {compared - agreed} did not");
        foreach (string line in disagreements) Console.WriteLine("  " + line);

        return compared > 0 && compared == agreed && disagreements.Count == 0 ? 0 : 1;
    }

    private static string? Rendered(PackfileObjects objects, PackfileObjects.Instance instance,
                                    HavokClasses.Member member) => member.Type switch
    {
        "real" => objects.ReadFloat(instance, member.Name)?.ToString("R"),
        "stringptr" or "cstring" => objects.ReadString(instance, member.Name) ?? "∅",
        "bool" or "int8" or "uint8" or "int16" or "uint16" or "int32" or "uint32"
            => Narrow(objects.ReadInt(instance, member.Name), member.Type),
        _ => null,
    };

    /// hkxpack and a raw read spell the same value differently: 1 against 1.0, true against 1, and a
    /// null pointer against an empty element. Comparing the text as typed would report every one of
    /// those as a disagreement and drown the real ones.
    private static bool Same(string ours, string theirs)
    {
        if (string.Equals(ours, theirs, StringComparison.Ordinal)) return true;
        if (ours == "∅") return theirs.Length == 0 || theirs == "null";

        if (float.TryParse(ours, out float a) && float.TryParse(theirs, out float b))
            return Math.Abs(a - b) <= 1e-6f * Math.Max(1f, Math.Abs(b));

        if (ours is "true" or "false")
            return theirs.Equals(ours, StringComparison.OrdinalIgnoreCase) ||
                   theirs == (ours == "true" ? "1" : "0");

        return false;
    }

    // What the object layer sees in a file: every object, its class, and the fields of whichever
    // class is asked about. The second half is the one that matters, because reading a field out of
    // the bytes is checkable against the same field in hkxpack's XML, and the two agreeing is what
    // says the offsets are right rather than merely plausible.
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

    /// A field narrower than four bytes still reads as four, so the extra has to be masked off or a
    /// one byte flag reports whatever its neighbours happen to hold.
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

    // The gate on writing .hkx bytes ourselves. Reading a file apart and putting it back has to
    // produce the same file: every offset in a packfile is derived from the sizes of what came
    // before it, so a byte for byte match means the derivation is right, and one wrong byte means it
    // is not. Nothing here needs the game, which is the point of doing it this way first.
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
            try { original = File.ReadAllBytes(file); }
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

                // The disagreement: Havok puts an undriven channel at zero, one, or no rotation,
                // while the reference pose puts it wherever the rig does. Anything away from
                // Havok's constant means the two readings draw a different skeleton.
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

    // What the mesh reader got out of a NIF, and how well it lines up with a skeleton. The bone
    // matching is the part worth printing: a mesh bone with no skeleton bone of that name is the
    // failure that shows up as a limb quietly missing from a drawing.
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
        foreach (var s in shapes)
        {
            var binding = OpenCommonwealth.Services.Nif.SkinnedMesh.Bind(s, skeleton);
            Console.WriteLine($"  {s.Name,-26} {binding}");
            unmatched += binding.Unmatched.Count;

            // The mesh is authored on the skeleton's own reference pose, so posing it back onto that
            // pose must not move it. Anything above about half a unit means the bind transforms are
            // being composed wrongly, whatever the drawing looks like.
            float drift = OpenCommonwealth.Services.Nif.SkinnedMesh
                .BindError(s, binding, skeleton, out int measured);
            if (measured > 0) worstDrift = Math.Max(worstDrift, drift);

            Console.WriteLine($"    rest pose drift {drift:F3} per vertex, over the {measured} of " +
                              $"{s.Vertices.Count} vertices whose bones all matched" +
                              (measured > 0 && drift > DriftLimit ? "   THE BIND TRANSFORMS ARE NOT COMPOSING"
                               : measured == 0 ? "   nothing to measure, no vertex is fully bound" : ""));

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
        }

        // A bone the skeleton does not have is reported and not failed: a shared mesh naming a bone a
        // particular rig lacks is ordinary. Drift is different. It means the bind transforms are
        // being composed wrongly, which is wrong for every mesh in the game at once, so it fails.
        Console.WriteLine(unmatched == 0
            ? "\nevery mesh bone found a skeleton bone"
            : $"\n{unmatched} mesh bone reference(s) had no skeleton bone of that name");

        bool ok = worstDrift <= DriftLimit;
        Console.WriteLine(ok
            ? $"PASS  worst rest pose drift {worstDrift:F3}, under the {DriftLimit:F1} limit"
            : $"FAIL  worst rest pose drift {worstDrift:F3}, over the {DriftLimit:F1} limit");
        return ok ? 0 : 1;
    }

    // Half a unit per vertex. Half precision vertex positions alone cost about a quarter of a unit on
    // a body a hundred units long, so this is loose enough not to trip on the format and tight enough
    // that any transform read the wrong way round, which costs tens of units, cannot pass.
    private const float DriftLimit = 0.5f;

    private static int Skeleton(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        var skeleton = new HkxBinaryReader().ReadSkeleton(Path.GetFullPath(argv[1]));
        Console.WriteLine($"{skeleton.Name}: {skeleton.BoneNames.Count} bones, " +
                          $"{skeleton.ParentIndices.Count} parent indices, {skeleton.ReferencePose.Count} poses");

        // Composed through AnimationPose rather than here, so the corpus tool and the viewport cannot
        // disagree about where a bone is.
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
        HkxTextEdit.ResetDirectory(dir);
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

    private static int Remove(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }
        NeedHkxPack();

        string work = Path.Combine(Path.GetTempPath(), "symrm", Path.GetFileNameWithoutExtension(argv[1]));
        HkxTextEdit.ResetDirectory(work);
        Directory.CreateDirectory(work);

        string xml = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, argv[1], work));
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
        string back = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, packed, Path.Combine(packedDir, "back")));

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
        HkxTextEdit.ResetDirectory(work);
        Directory.CreateDirectory(work);

        string xml = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, argv[1], work));
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
        HkxTextEdit.ResetDirectory(work);
        Directory.CreateDirectory(work);

        string xml = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, argv[1], work));
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
        return HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, packed, Path.Combine(dir, "back")));
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
        HkxTextEdit.ResetDirectory(work);
        Directory.CreateDirectory(work);

        string xml = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, argv[1], work));
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

        string back = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, argv[2], Path.Combine(work, "back")));
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
