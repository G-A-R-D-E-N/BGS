using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
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
            case "ba2": return Ba2Browse(argv);
            case "motion": return Motion(argv);
            case "xml": return Xml(argv);
            case "pose": return Pose(argv);
            case "channels": return Channels(argv);
            case "packfile": return Packfile(argv);
            case "layout": return Layout(argv);
            case "relayout": return Relayout(argv);
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
            case "model": return Model(argv);
            case "consumers": return Consumers(argv);
            case "symbols": return Symbols(argv);
            case "walk": return Walk(argv);
            case "append": return Append(argv);
            case "orphan": return Orphan(argv);
            case "classes": return Classes(argv);
            case "fields": return Fields(argv);
            case "signatures": return Signatures(argv);
            case "panel": return Panel(argv);
            case "paths": return Paths(argv);
            case "elements": return Elements(argv);
            case "nesting": return Nesting(argv);
            case "objects": return Objects(argv);
            case "capacity": return Capacity(argv);
            case "grow": return Grow(argv);
            case "qstransform": return QsTransform(argv);
            case "interleave": return Interleave(argv);
            case "splinestats": return SplineStats(argv);
            case "spline": return Spline(argv);
            case "savespline": return SaveSpline(argv);
            case "editframe": return EditFrame(argv);
            case "run": return Run(argv);
            case "weights": return Weights(argv);
            case "cliptime": return ClipTime(argv);
            case "crosscheck": return CrossCheck(argv);
            case "savecheck": return SaveCheck(argv);
            case "mesh": return Mesh(argv);
            case "meshpng": return DrawMesh(argv);
            case "remove": return Remove(argv);
            case "door": return Door(argv);
            case "link": return Link(argv);
            case "draw": return Draw(argv);
            case "test": return Tests.Run();
            case "defaults": return Defaults(argv);
            default: Usage(); return 1;
        }
    }

    private static void Usage() => Console.WriteLine("""
        symrm, the verification harness for Behaviour Graph Studio.

          dotnet run --project tools/symrm/symrm.csproj -- corpus <Fallout4 - Animations.ba2> <outDir> [pathFilter]
              Pull every vanilla behaviour .hkx out of the archive. 531 of them. The filter is a
              path substring and defaults to "behavior"; pass "" to pull the animation clips as
              well, which is what the spline gate measures against.

          dotnet run --project tools/symrm/symrm.csproj -- run <behaviour.hkx> [event...]
              Step the graph. With no events it reports where the graph starts, which machines are
              running, and which states any sequence of events can reach. With events it sends them
              in order and says what moved.

          dotnet run --project tools/symrm/symrm.csproj -- run <behaviourDir>
              The same over the corpus. Reports what it refuses to guess at, and checks two things:
              that inside a machine the run entered it reaches at least what the validator's own
              per machine rule reaches, and that actually stepping never lands somewhere the
              reachability analysis calls impossible.

          dotnet run --project tools/symrm/symrm.csproj -- template <behaviourDir> [everyNth]
              What a kept shape could be, and whether keeping one works. Counts how many clip
              generators, blenders and state infos could leave their file at all, how many share an
              object and so cannot, and how many carry event or variable names a file they land in
              would have to declare. Then lifts every Nth of them, applies it into a separate copy of
              its own file, and checks the objects arrive. Also checks that every Nth shape which does
              share is refused, since otherwise the sweep would only ever exercise the shapes that
              were going to work. everyNth defaults to 37.

          dotnet run --project tools/symrm/symrm.csproj -- cliptime <behaviourDir | behaviour.hkx>
              How long every clip plays for and when the events it carries go out. Needs the corpus
              extracted with --tree, because a clip's length is in the animation file the project
              around the behaviour points at rather than in the behaviour. Checks that no trigger
              resolves outside its own clip, that a trigger written as the end of the clip lands on
              the clip's length, that a clip with no length offers no triggers at all, and that
              running the clock never reaches a state the reachability analysis rules out.

          dotnet run --project tools/symrm/symrm.csproj -- splinestats <animDir | file.hkx>
              What the game's own compressor chose, counted across the animations it shipped:
              quantisation formats, channel flags, curve degree, frames per block. This is where
              the encoder's fixed choices come from, so it is what to rerun before changing one.

          dotnet run --project tools/symrm/symrm.csproj -- spline <animDir | file.hkx> [everyNth]
              The spline codec on its own. Decode a vanilla clip, encode those frames again, decode
              the result, and compare. Reports the worst bone in the corpus rather than an average,
              and the size against what the game shipped.

          dotnet run --project tools/symrm/symrm.csproj -- savespline <animDir | file.hkx> [everyNth]
              The same trip through a real file: written into the packfile, rebuilt, and read back
              with the ordinary reader. Covers the header fields, the four arrays and the pointer
              retargeting that the codec check cannot see.

          dotnet run --project tools/symrm/symrm.csproj -- editframe <animDir | file.hkx> [everyNth]
              The frame editor's whole path: change a bone at one frame, save, and read back. Proves
              the edited frame comes back changed and no other frame moved.

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

          dotnet run --project tools/symrm/symrm.csproj -- capacity <file.hkx | hkxDir>
              What the top bits of every array's capacity word hold, split by whether the array is
              empty, since growing one means writing a capacity for it and the flag is what tells
              the game whether it owns the memory.

          dotnet run --project tools/symrm/symrm.csproj -- grow <file.hkx | hkxDir>
              Bounds the last variable, which lengthens an array of structs, then writes the file,
              reads it back and checks that the bound is what was asked for and nothing else moved.
              With a Java runtime the read back goes through hkxpack, which is a second
              implementation; without one it goes through our own reader, which is what proves the
              edit needs no Java.

          dotnet run --project tools/symrm/symrm.csproj -- interleave <animation.hkx | animDir>
              Writes an animation's frames back out uncompressed, then decodes the file it produced
              and checks every frame of every track against what went in, along with the bone names,
              the annotations and the duration. Then moves one frame of one track by a known amount
              and checks that exactly that moved and nothing else did. With Java it also asks
              hkxpack, which is a second implementation, whether the file holds what it should.

          dotnet run --project tools/symrm/symrm.csproj -- qstransform <skeleton.hkx | hkxDir>
              What the fourth lane of a transform's translation and scale holds across the game's own
              skeletons. Writing a transform means writing that lane, and it is the one nobody can
              look up, so it is counted rather than reasoned about.

          dotnet run --project tools/symrm/symrm.csproj -- crosscheck <file.hkx>
              Reads every field it can out of the bytes and compares it against what hkxpack says
              the same field holds. Two independent readings of one file, ours by byte offset and
              hkxpack's by its own schema, so agreement across a whole file is what says the offsets
              are right rather than plausible. Needs Java and the jar. Exits non zero on any
              disagreement.

          dotnet run --project tools/symrm/symrm.csproj -- elements <file.hkx | folder>
              The line the panel puts at the head of each transition, so they can be read against
              the file's own XML. Needs no Java.

          dotnet run --project tools/symrm/symrm.csproj -- nesting <file.hkx | folder>
              How much of a machine's routing an arrow from one state to another can carry: how
              many transitions are wildcard, how many carry a nested state id, how many
              transitions a machine holds, and whether a nested id resolves to a real state of the
              machine under the state being entered. This is the measurement behind drawing
              transitions on the canvas, so the decision can be rechecked rather than taken on
              trust. Needs no Java.

          dotnet run --project tools/symrm/symrm.csproj -- paths <file.hkx | folder>
              Writes a sentinel through every field the panel shows, addressed by where the field
              sits, and checks that exactly that field moved. The panel's boxes line up with the
              file's values by position, and a name does not preserve that: an array of structs
              repeats every name once per element, so a write by name lands on the first of them.
              Reports how many fields sit inside an element and how many of those a name alone
              would have missed, which is the size of what this fixes. Needs no Java.

          dotnet run --project tools/symrm/symrm.csproj -- packfile <file.hkx | folder>
              Takes a .hkx apart and puts it back together, and reports whether the result is the
              same file. This is the gate on writing .hkx bytes without hkxpack in the way: every
              offset in a packfile is derived from the sizes of what precedes it, so a byte for byte
              match means the derivation is right. Exits non zero on any file that differs or cannot
              be read. Needs no game and no Java.

          dotnet run --project tools/symrm/symrm.csproj -- delete <file.hkx | folder> [id]
              Takes one object out of each file for real, not by orphaning it, and checks the result
              reads back with exactly that object gone, fully accounted for, and no pointer left
              aiming into the hole. Defaults to the last object in the file, orphaned first so
              nothing points at it. Changes nothing on disk. Needs no game and no Java.

          dotnet run --project tools/symrm/symrm.csproj -- conditions <file.hkx | folder>
              Reads every transition condition and every expression modifier line in the files, and
              reports what the language actually contains. Then checks each condition parses, that
              every variable it names is one the file declares, and that its answer changes as its
              variables are driven through a spread of values, which is what tells a condition being
              read from one being ignored. Exits non zero on a condition it cannot read or one that
              never changes its mind. Changes nothing on disk. Needs no game and no Java.

          dotnet run --project tools/symrm/symrm.csproj -- paste <file.hkx | folder>
              Copies a subtree out of each file and pastes it back, then reads the result and checks
              that no pointer inside the copy still names the object it was copied from, that every
              one lands on a real object, that the copy is the same shape as the original, that
              deleting exactly what was pasted gives the file back, and that a state pasted into a
              machine gets a number nothing else in that machine has. Then pastes each file's subtree
              into the next file along, where a missing event or a shared object is refused by name.
              Changes nothing on disk. Needs no game and no Java.

          dotnet run --project tools/symrm/symrm.csproj -- savenumbers <file.hkx | folder>
              Gives an array of plain numbers one more element than it had and checks it reads back
              at the new length with the old values still in front of the new one. The last kind of
              array that had to go out through a rebuild.

          dotnet run --project tools/symrm/symrm.csproj -- savewide <file.hkx | folder>
              Changes a vector through the document, the way the window would, and checks it reads
              back and that the file is exactly as long as it was. These are fixed width and moved
              nothing, and were refused anyway because nothing parsed the spelling back.

          dotnet run --project tools/symrm/symrm.csproj -- saveevent <file.hkx | folder> [out.hkx]
              Declares an event the way the window does, all the way to the bytes, and checks the
              file comes back holding it. Adding one lengthens an array of strings, which used to be
              the last edit that forced a save out through hkxpack. Needs no Java.

          dotnet run --project tools/symrm/symrm.csproj -- notes <file.hkx | folder>
              How much the properties panel can say about the fields it shows. Every field gets a
              description of what it is, from the class table. Only the handful this project has
              established get a sentence about what they mean, and this prints which ones, so the
              gap is a number rather than an impression.

          dotnet run --project tools/symrm/symrm.csproj -- chain <behaviour.hkx>
              The project around a file: its character, its skeleton, the animations it declares and
              the bones it has. Reads the other files the same way it reads this one. Run it once
              with Java on PATH and once under tools/no-java.sh and the output has to match.

          dotnet run --project tools/symrm/symrm.csproj -- classcheck <hkclass-field-layouts.txt>
              Sets this build's class table against Fallout 4's own account of itself, read out of
              the startup initializers rather than out of any tool. Every offset written into a file
              comes from that table, so this is the check that it is right rather than merely
              self consistent. Exits non zero on any size or offset that disagrees.

          dotnet run --project tools/symrm/symrm.csproj -- savedelete <file.hkx | folder> [out.hkx]
              (out.hkx only when pointed at a single file, and the only time anything is written)
              Deletes a node the way the window does, all the way to the bytes: text written from
              the file's own bytes, the node taken out and everything pointing at it detached, the
              change worked out and written. Checks the result reads back with the right objects in
              it and no pointer aiming at nothing. Writes nothing to disk, and needs no Java, which
              is half the point.

          dotnet run --project tools/symrm/symrm.csproj -- relayout <file.hkx | folder>
              Throws the data section away and writes it again from nothing, then checks the result
              is the file it started as. This is the gate on removing an object: removing one moves
              every object after it, so nothing can be removed until a file can be laid out rather
              than edited. Exits non zero on any file that differs or that the walk cannot account
              for. Needs no game and no Java.

          dotnet run --project tools/symrm/symrm.csproj -- layout <file.hkx | folder>
              The next gate after that one. `packfile` keeps the data section's bytes as it found
              them and only recomputes the offsets around them, which is why removing an object is
              still refused: removing one moves every object after it. This works out where every
              object and every run it points at would go if the file were laid out from nothing,
              and says how many land where they actually are. Also prints where each kind of thing
              starts within a sixteen byte boundary, which is how the alignment rule was found
              rather than guessed. Exits non zero on any disagreement. Needs no game and no Java.

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

          dotnet run --project tools/symrm/symrm.csproj -- defaults <Fallout4.exe.unpacked.exe> <Fallout4_163_functions.txt> [class]
              What the game says every field starts out as, read off the class registrations in the
              executable, against what the class table believes. The table's own 625 defaults are
              the gate: this has to reproduce all of them or it is reading the blob wrongly. Naming
              a class prints everything the game says about it instead of gating.

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

    /// Somewhere to unpack a file to, which is never the directory the file itself is in.
    ///
    /// These directories get emptied before use, and the name is built from the file's own name, so
    /// pointing one of these commands at a file that already sits in a directory of that name
    /// deletes the file being read. Found by doing it: a run of crosscheck against a file left in an
    /// earlier crosscheck's working directory took the file with it.
    /// What the game says every field starts out as, against what the class table believes.
    ///
    /// The table is generated from hkxpack's database, which records a default for 625 members and
    /// none at all for the ones whose value comes from a fixed set. The game registers every class at
    /// startup and hands the constructor a blob of defaults, so it knows all of them.
    ///
    /// The 625 the table already has are the gate. If this cannot reproduce those exactly then it is
    /// reading the blob wrongly and nothing it says about the rest is worth having.
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

        // Naming a class prints everything the game says about it, which is the shape a person wants
        // when checking one field rather than gating the whole table.
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
                    // The blob only carries a default that is not zero. A field the game leaves out
                    // starts as zero, which is what the table says for these, so the two agree.
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

        // The gate. Reading the blob wrongly shows up here first, because these are the values two
        // independent sources both claim to know.
        return differed == 0 ? 0 : 1;
    }

    /// Whether two spellings of the same default mean the same thing. The table spells a real as
    /// `0.000000` and this spells it `0.0`, which is a difference in the writing and not the value.
    /// Folds the defaults the game knows and the table does not into `HavokClassTypes.json`.
    ///
    /// Additive only. A default the table already has is left alone even though the two agree,
    /// because the table is generated from hkxpack's database and keeping its own values means a
    /// rebuild by `symrm classes` changes only what it should.
    ///
    /// Written the way `symrm classes` writes it, one class per line, so the diff is the members
    /// that gained a default and nothing else. A class that gains nothing is re-emitted from its own
    /// parsed form, which is the check that this is writing the same shape: if it were not, every
    /// line would move.
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

    /// What the class table says about itself, in one place because two commands write the file and
    /// a note that drifted would be worse than none: the whole point of it is telling the next
    /// person that a rebuild alone loses the defaults read out of the game.
    private static readonly string TableNote =
        "What a Havok class is made of. The member types, which members are ever written to a " +
        "file, the class of every inline struct and every enum's values come from the class " +
        "database inside hkxpack's jar (MIT, see THIRD_PARTY_NOTICES.md), read out as a zip. " +
        "The instance sizes come from HavokClassLayouts.json, which was read out of Fallout 4 " +
        "itself. Rebuild with `symrm classes`, then run `symrm defaults --write` after it: that " +
        "database records no default for a member whose value comes from a fixed set, and those " +
        "are read out of the game's own class registrations. A rebuild on its own drops them.";

    /// Whether a spelling means zero, in any of the shapes the table writes one.
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
            // Relative, because these run from 0 to 1.8e19 and a fixed tolerance calls the big ones
            // different when only their spelling is.
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

    private static void NeedHkxPack()
    {
        _java = HkxTextEdit.FindJava("") ?? throw new InvalidOperationException("no java on PATH or in JAVA_HOME");
        _jar = HkxTextEdit.FindHkxPack("", _root) ?? throw new InvalidOperationException(
            "hkxpack-cli.jar not found; put it in tools/ or next to the FO4AnimForge checkout");
    }

    private static int Corpus(string[] argv)
    {
        if (argv.Length < 3) { Usage(); return 1; }

        // The filter is a path substring and defaults to the behaviours, because that is what every
        // existing gate is measured against and the numbers in the readme are counts of those 531.
        // Passing an empty one pulls the animations out too, which is what the spline gate needs and
        // what the behaviour corpus deliberately does not contain.
        string filter = argv.Length > 3 ? argv[3] : "behavior";
        int written = OpenCommonwealth.Services.Archive.Ba2.ExtractMatching(argv[1], filter, argv[2], ".hkx", Console.WriteLine);
        Console.WriteLine($"wrote {written} file(s) matching \"{filter}\" to {argv[2]}");
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
        int written = OpenCommonwealth.Services.Archive.Ba2.ExtractMatching(Path.GetFullPath(argv[1]), argv[2], Path.GetFullPath(argv[3]),
                                          extension, Console.WriteLine, tree);
        Console.WriteLine($"wrote {written} files to {Path.GetFullPath(argv[3])}");
        return written > 0 ? 0 : 1;
    }

    /// The text form written from the bytes, set against the text hkxpack writes for the same file.
    ///
    /// This is the last thing holding the Java requirement. Reading a behaviour has not needed
    /// hkxpack for a while, but an edit is made by rewriting the unpacked text, so with no hkxpack
    /// there is no text to rewrite. Producing the same text ourselves removes that without touching
    /// any of the consumers written against it, and the only measure that matters is whether the two
    /// are the same line for line.
    private static int Xml(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }
        NeedHkxPack();

        string file = Path.GetFullPath(argv[1]);
        string work = WorkDirectory("symrm-xml-", file);
        HkxTextEdit.ResetDirectory(work);

        string theirs = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, file, work));
        string ours = NativeXml.From(File.ReadAllBytes(file));

        // Written out beside the reference, so a disagreement can be read in place rather than only
        // through this summary.
        string beside = Path.Combine(work, "ours.xml");
        File.WriteAllText(beside, ours);
        Console.WriteLine($"ours written to {beside}");

        var mine = ours.Replace("\r\n", "\n").Split('\n');
        var them = theirs.Replace("\r\n", "\n").Split('\n');

        // A real diff rather than comparing line one against line one. One extra line early on shifts
        // every line after it, so an index by index comparison reports a whole file as different and
        // buries the single rule that caused it. This walks both sides and resynchronises.
        // Classes whose array elements hkxpack strides wrongly. Its own reading of one of these is
        // misaligned from the second element on, which was measured and written up long before this:
        // BSLookAtModifierBoneData is 528 bytes and hkxpack derives 520. Where our text differs
        // inside one of those, hkxpack is the one that is wrong, so those differences are counted
        // apart rather than chased.
        var strided = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, className) in new PackfileObjects(PackfileImage.Read(file)).ClassNames())
            foreach (var member in HavokClassTypes.Shipped.Members(className))
                if (member.CType != null && HavokClassTypes.Shipped.PaddedBeyondHkxPack(member.CType))
                    strided.Add(member.CType);

        var edits = Diff(them, mine);
        int differ = edits.Count;

        // Which member names belong to one of those classes, so a differing line can be attributed to
        // hkxpack's stride rather than merely coinciding with a file that holds one.
        var theirFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (string className in strided)
            foreach (var member in HavokClassTypes.Shipped.Members(className))
                theirFields.Add(member.Name);

        // A comment counts as well as a value. The misaligned run inside one of these structs shifts
        // its SERIALIZE_IGNORED lines along with its fields, and those carry the member name in a
        // comment rather than in an attribute.
        bool Excused(string line)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                line, "<hkparam name=\"(\\w+)\"|<!-- (\\w+) SERIALIZE_IGNORED -->");
            if (!m.Success) return false;

            string name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            return theirFields.Contains(name);
        }

        // The element boundaries around a misread struct, which carry no member name to attribute
        // them by. Only counted in a file that holds one of those classes at all, and reported apart
        // from the fields so the two are never one number.
        bool Boundary(string line) =>
            strided.Count > 0 && (line.Trim() == "<hkobject>" || line.Trim() == "</hkobject>");

        int excused = edits.Count(e => Excused(e.Line));
        int boundaries = edits.Count(e => !Excused(e.Line) && Boundary(e.Line));
        differ -= excused + boundaries;
        int shown = 0;

        foreach (var (side, at, line) in edits)
        {
            if (Excused(line) || Boundary(line)) continue;
            if (shown++ >= 16) break;
            Console.WriteLine(side == '-' ? $"  hkxpack line {at + 1} has, and we do not:\n    {line}"
                                          : $"  we have at line {at + 1}, and hkxpack does not:\n    {line}");
        }

        if (differ > shown) Console.WriteLine($"  and {differ - shown} more");

        Console.WriteLine($"\n{Path.GetFileName(file)}: {them.Length} lines from hkxpack, " +
                          $"{mine.Length} from us, {differ} line(s) differing" +
                          (excused > 0 ? $", and {excused} where hkxpack strides a padded struct wrongly"
                                       : "") +
                          (boundaries > 0 ? $" with {boundaries} element boundary line(s) around them" : ""));

        // A file holding one of those classes cannot be compared line by line with any confidence:
        // hkxpack's own reading of it is misaligned, so the two texts genuinely diverge and a diff
        // resynchronising through the wreckage reports more than is really there. Said as a flag so a
        // sweep can hold the two kinds of file apart rather than averaging them.
        Console.WriteLine(strided.Count > 0 ? "COMPARABLE=no" : "COMPARABLE=yes");
        return differ == 0 ? 0 : 1;
    }

    /// The lines one side has and the other does not.
    ///
    /// Walks both sides together and, where they part, looks ahead on each for the nearest place they
    /// agree again. That is the right shape for this comparison: the two texts are meant to be the
    /// same file, so a disagreement is a handful of lines and not a rewrite, and a full longest
    /// common subsequence over thirty thousand lines is nine hundred million cells for an answer that
    /// resynchronises within twenty.
    ///
    /// The look ahead is bounded. Past the bound the two are not the same file with a rule wrong in
    /// it, and saying so beats grinding.
    private static List<(char Side, int At, string Line)> Diff(string[] left, string[] right)
    {
        const int Reach = 400;
        var edits = new List<(char, int, string)>();

        int a = 0, b = 0;
        while (a < left.Length && b < right.Length)
        {
            if (left[a] == right[b]) { a++; b++; continue; }

            // The nearest resynchronisation, preferring the shortest run of lines dropped from
            // either side, so one inserted line is reported as one line and not as two.
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

    /// The first variableBounds element's min or max, out of hkxpack's text.
    private static string FirstBound(string xml, string which)
    {
        int start = xml.IndexOf("name=\"variableBounds\"", StringComparison.Ordinal);
        if (start < 0) return "absent";

        var m = System.Text.RegularExpressions.Regex.Match(
            xml[start..], $"name=\"{which}\".*?name=\"value\">(-?\\d+)<",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : "absent";
    }

    /// Where a clip travels, read off its extracted motion.
    ///
    /// A walk does not move its bones across the ground: it plays on the spot and carries a separate
    /// track saying where the character has got to. Point this at a folder to see which animations
    /// carry one at all, which is the number worth knowing, since an idle carrying root motion and a
    /// walk carrying none are both worth a second look.
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

        // Every eighth, because a walk carries dozens and the shape of the path is what is being
        // looked at rather than each step of it.
        for (int i = 0; i < motion.Samples.Count; i += Math.Max(1, motion.Samples.Count / 8))
            Console.WriteLine($"  sample {i,3}  {motion.Samples[i]}");

        Console.WriteLine($"  sample {motion.Samples.Count - 1,3}  {motion.Samples[^1]}");

        // Reading between samples is what the viewport does, so it is checked here rather than only
        // on screen. Halfway has to land between the ends and not outside them.
        var half = RootMotion.At(motion, 0.5f);
        Console.WriteLine($"halfway through: {half}");
        return 0;
    }

    /// Reads an archive's index and finds files in it without unpacking anything, which is what the
    /// window's own archive browser does.
    ///
    /// The index is the whole point. Fallout4 - Animations.ba2 holds 29,716 entries and reaching one
    /// of them used to mean writing the other 29,715 to disk first. Reading one file out of it is
    /// checked here rather than only in the window, because a file pulled out of an archive has to be
    /// byte for byte what the same file is on disk.
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

        // Reading one, so the index is not the only thing proved. A behaviour that comes out of the
        // archive has to be one our own reader can take apart, which is the whole reason for opening
        // it from here rather than extracting it first.
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
        string work = WorkDirectory("symrm-savecheck-", file);
        if (Directory.Exists(work)) Directory.Delete(work, true);

        string xmlFile = HkxTextEdit.Unpack(_java, _jar, file, work);
        string original = HkxTextEdit.ReadXml(xmlFile);

        if (!NullSaveIsByteIdentical(file, original)) return 1;
        if (!GrowingAnArrayOfStringsWorks(file, original)) return 1;

        var edits = Invent(original);
        if (edits.Count == 0)
        {
            Console.WriteLine($"{Path.GetFileName(file)}: nothing here to change, skipped");
            return 0;
        }

        string edited = original;
        foreach (var (was, now) in edits) edited = ReplaceFirst(edited, was, now);

        // A brand new object, added the way the editor adds one, with something in the file pointed
        // at it. This is the case a longer array does not cover: the array work only ever moved
        // pointers at objects that were already there.
        var host = System.Text.RegularExpressions.Regex.Match(
            original, "<hkobject class=\"hkbClipGenerator\" name=\"#[0-9]+\" " +
                      "signature=\"(?<sig>0x[0-9a-f]+)\">");

        var lastGenerator = System.Text.RegularExpressions.Regex
            .Matches(edited, "<hkparam name=\"generator\">#[0-9]+</hkparam>")
            .LastOrDefault();

        if (host.Success && lastGenerator != null)
        {
            edited = HkxTextEdit.AddObject(
                edited, "hkbClipGenerator", host.Groups["sig"].Value,
                "            <hkparam name=\"userPartitionMask\">7</hkparam>", out string added);

            string pointer = $"<hkparam name=\"generator\">#{added}</hkparam>";
            edited = edited.Remove(lastGenerator.Index, lastGenerator.Length)
                           .Insert(lastGenerator.Index, pointer);

            // Two things to find afterwards: the object itself and the pointer at it.
            edits.Add(("", $"name=\"#{added}\""));
            edits.Add(("", pointer));
        }

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
        bool sameSize = before.Length == saved.Length;
        int changedBytes = Enumerable.Range(0, Math.Min(before.Length, saved.Length))
                                     .Count(i => before[i] != saved[i]);

        // Two things legitimately change the length. Text grows it, because the new text goes on
        // the end rather than over what was there. Clearing a pointer shrinks it, because a null
        // pointer is the absence of a fixup rather than a fixup to nowhere, so the entry is dropped
        // and the table is twelve bytes shorter, sixteen once it is padded back to the boundary.
        // Anything else changing size means something moved, which is the fault this is watching
        // for.
        int cleared = plan.Changes.Count(c => c.Ref && c.Value == "null");
        bool shrankAsExpected = cleared > 0 && before.Length > saved.Length
                                            && before.Length - saved.Length <= 16 * cleared;

        string size = sameSize
            ? $"{changedBytes} bytes differ from the original"
            : plan.Grows && saved.Length > before.Length
                ? $"{before.Length} bytes to {saved.Length}, as appending text does, " +
                  $"{changedBytes} of the original bytes differ"
                : shrankAsExpected
                    ? $"{before.Length} bytes to {saved.Length}, as dropping {cleared} pointer " +
                      $"entr{(cleared == 1 ? "y" : "ies")} does, {changedBytes} of the original " +
                      "bytes differ"
                    : $"BUT THE FILE CHANGED SIZE WITHOUT APPENDING ANYTHING, {before.Length} to {saved.Length}";

        Console.WriteLine($"{Path.GetFileName(file)}: {plan.Changes.Count} value(s) changed, {size}");
        foreach (var change in plan.Changes.Take(5)) Console.WriteLine("    " + change);

        if (!sameSize && !(plan.Grows && saved.Length > before.Length) && !shrankAsExpected) return 1;
        if (!OnlyAppended(before, saved, plan)) return 1;

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
    /// A file that grew will differ from the original over thousands of bytes, and almost all of that
    /// is innocent: the fixup tables follow the data inside the section, so appending text pushes
    /// them along without changing a word of what they say. A raw byte count cannot tell that apart
    /// from real damage, so this compares the pieces instead of the bytes. Everything must be
    /// unchanged except the data the text was added to, and the destination of the fixups that were
    /// deliberately repointed.
    private static bool OnlyAppended(byte[] before, byte[] after, NativeSave.Plan plan)
    {
        var was = PackfileImage.Read(before);
        var now = PackfileImage.Read(after);

        // How many pointer entries each planned change is allowed to move. A repointed field moves
        // one. A resized array moves every element it had and every element it now has, because the
        // run moved to the end of the section and each element's fixup names a position inside it.
        // Counted from the original file rather than assumed, so an array that was longer than the
        // plan expects cannot hide extra movement inside the allowance.
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

            // The data the text was added to also holds the values written over in place, so it is
            // not expected to be identical. What it must not do is differ anywhere else: a value
            // write touches at most the four bytes of the field it names.
            // A pointer change writes nothing into the data at all. It moves an entry in the
            // pointer table, so it buys no allowance here and the data has to be untouched by it.
            int touched = Enumerable.Range(0, a.Data.Length).Count(k => a.Data[k] != b.Data[k]);
            int allowed = 4 * plan.Changes.Count(c => !c.Text && !c.Ref);
            if (touched > allowed)
            {
                Console.WriteLine($"  append check: FAILED, {touched} byte(s) of {a.Tag} changed, " +
                                  $"more than the {allowed} the planned values can account for");
                return false;
            }

            // Every pointer the plan does not name has to be untouched, and no more of them may
            // move than the plan repoints. Compared by source rather than by position: dropping an
            // entry shifts every entry after it, which is not a change to any pointer.
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

            // One new entry per object added, and every entry that was there before untouched. The
            // table says which class each object is, so an entry changing under an object that was
            // already in the file would be that object turning into something else.
            // Objects only live in the data section, so only that one gains entries. Counting the
            // added ones against every section would demand a new object in the class name section
            // too, which is not a thing.
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

            // The local table gains an entry when an array goes from empty to holding something,
            // and loses one when it goes the other way, so its length is only fixed while no array
            // changes. Compared by source rather than by position for the same reason as the global
            // table: an entry appearing or going shifts the rest without changing any pointer.
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

    /// Growing an array of strings, which is what declaring an event or a variable is.
    ///
    /// This guard used to assert the opposite, that the edit was refused, and it was right to: an
    /// array cannot grow where it sits and there was nowhere for the rest of the file to go. The run
    /// is appended now, so the guard asserts the write instead, and that the file comes back holding
    /// the longer array.
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

        // Read back from the bytes rather than from the plan, since the plan saying it wrote
        // something is exactly what is in question.
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

        // An animation carries none of the fields above, and it is the case worth proving: the old
        // route refused to write one at all, because the XML cannot carry a lossless compressed
        // animation without cutting it short. duration is a plain real on hkaAnimation, so changing
        // it exercises a real save of the very format that used to be refused.
        Try("<hkparam name=\"duration\">[0-9.]+</hkparam>",
            "<hkparam name=\"duration\">3.5</hkparam>");

        // A name longer than the one it replaces, which is the case a value save could not do at all
        // until strings were written by appending. Longer on purpose: a shorter one could be written
        // over the old bytes and would prove nothing.
        Try("<hkparam name=\"animationName\">[^<]{3,}</hkparam>",
            "<hkparam name=\"animationName\">Animations\\Renamed_By_Symrm_Longer.hkx</hkparam>");

        // Rewiring a node, which is a structural edit to the graph and not one to the file: no
        // object moves, nothing is appended, and one entry in the pointer table names a different
        // destination. The target is taken from a second generator field in the same file rather
        // than invented, so it is an id the file actually has.
        var generators = System.Text.RegularExpressions.Regex
            .Matches(xml, "<hkparam name=\"generator\">#(?<id>[0-9]+)</hkparam>")
            .Select(m => m.Groups["id"].Value).Distinct().ToList();

        if (generators.Count >= 2 && generators[0] != generators[1])
            Try($"<hkparam name=\"generator\">#{generators[0]}</hkparam>",
                $"<hkparam name=\"generator\">#{generators[1]}</hkparam>");

        // And the other direction: a pointer set to nothing, which drops the fixup rather than
        // aiming it at offset zero. Aiming it at zero would quietly point the field at whichever
        // object sits first.
        Try("<hkparam name=\"variableBindingSet\">#[0-9]+</hkparam>",
            "<hkparam name=\"variableBindingSet\">null</hkparam>");

        // An array of object pointers made one element longer, by repeating an element it already
        // holds. Longer on purpose: a shorter one could be written over the run that is already
        // there and would prove nothing about appending. The element is one the array already names
        // rather than an invented id, so the target is an object the file has.
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

    // Reads every field we can out of the raw bytes and compares it to what hkxpack says the same
    // field holds. Two independent readings of the same file: ours by byte offset from layouts read
    // out of the game, hkxpack's by its own schema. Agreement across a whole file is what turns
    // "these offsets look plausible" into "these offsets are right", and it is the check that has to
    // pass before anything writes bytes for real.
    /// Builds `HavokClassTypes.json` out of the class database hkxpack carries inside its own jar,
    /// merged with the instance sizes read out of the game.
    ///
    /// The jar is opened as what it is, a zip, so this runs Java no more than unzipping does. What
    /// comes out is the half of a class description the game's own startup code does not keep: which
    /// members are ever written to a file, what class an inline struct is, and every enum's values.
    ///
    /// One class per line on purpose. It is a generated file either way, and a generated file that
    /// cannot be read in a diff hides its own mistakes.
    private static int Classes(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string output = Path.GetFullPath(argv[^1]);
        string? jar = argv.Length > 2 ? Path.GetFullPath(argv[1]) : HkxTextEdit.FindHkxPack("", _root);

        if (jar == null || !File.Exists(jar))
        {
            Console.WriteLine("No hkxpack-cli.jar to read the class database out of. " +
                              "Pass its path: symrm classes <jar> <out.json>");
            return 1;
        }

        var sizes = HavokClasses.Shipped;
        var classes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        int members = 0, ignored = 0, structs = 0, enums = 0, values = 0, sized = 0, fixedArrays = 0;

        using (var zip = System.IO.Compression.ZipFile.OpenRead(jar))
        {
            foreach (var item in zip.Entries.Where(e => e.FullName.StartsWith("classxml/", StringComparison.Ordinal)
                                                        && e.FullName.EndsWith(".xml", StringComparison.Ordinal)))
            {
                using var stream = item.Open();
                var root = System.Xml.Linq.XDocument.Load(stream).Root!;
                string name = root.Attribute("name")!.Value;

                var declared = new List<object>();
                foreach (var m in root.Element("members")?.Elements("member")
                                  ?? Enumerable.Empty<System.Xml.Linq.XElement>())
                {
                    // Trimmed: a handful of entries in the database carry a stray space beside the
                    // flag, and a flag compared as text is a flag that goes unnoticed when it does.
                    string flags = (m.Attribute("flags")?.Value ?? "").Trim();
                    bool written = !flags.Split('|').Any(f => f.Trim() == "SERIALIZE_IGNORED");
                    int size = int.Parse(m.Attribute("arrsize")?.Value ?? "0");

                    members++;
                    if (!written) ignored++;
                    if (m.Attribute("ctype") != null) structs++;
                    if (size > 0) fixedArrays++;

                    declared.Add(new Dictionary<string, object?>
                    {
                        ["name"] = m.Attribute("name")?.Value,
                        ["offset"] = int.Parse(m.Attribute("offset")!.Value),
                        ["vtype"] = m.Attribute("vtype")?.Value,
                        ["vsub"] = m.Attribute("vsubtype")?.Value,
                        ["ctype"] = m.Attribute("ctype")?.Value,
                        ["etype"] = m.Attribute("etype")?.Value,
                        ["arrsize"] = size,
                        ["written"] = written,
                        ["default"] = m.Attribute("default")?.Value,
                    });
                }

                var declaredEnums = new SortedDictionary<string, object>(StringComparer.Ordinal);
                foreach (var e in root.Element("enums")?.Elements("enum")
                                  ?? Enumerable.Empty<System.Xml.Linq.XElement>())
                {
                    var items = new SortedDictionary<string, long>(StringComparer.Ordinal);
                    foreach (var i in e.Elements("enumitem"))
                        items[i.Attribute("name")!.Value] = long.Parse(i.Attribute("value")!.Value);

                    declaredEnums[e.Attribute("name")!.Value] = items;
                    enums++;
                    values += items.Count;
                }

                int? size2 = sizes[name]?.Size;
                if (size2 != null) sized++;

                classes[name] = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["parent"] = root.Attribute("parent")?.Value,
                    ["signature"] = root.Attribute("signature")?.Value,
                    ["size"] = size2,
                    ["members"] = declared,
                    ["enums"] = declaredEnums,
                });
            }
        }

        if (classes.Count == 0)
        {
            Console.WriteLine($"{Path.GetFileName(jar)} holds no classxml/ entries.");
            return 1;
        }

        var text = new System.Text.StringBuilder();
        text.Append("{\n\"note\":");
        text.Append(JsonSerializer.Serialize(TableNote));
        text.Append(",\n\"havokVersion\":\"hk_2014.1.0-r1\",\n\"classes\":{\n");
        text.Append(string.Join(",\n", classes.Select(c => JsonSerializer.Serialize(c.Key) + ":" + c.Value)));
        text.Append("\n}\n}\n");

        File.WriteAllText(output, text.ToString());

        Console.WriteLine($"{classes.Count} classes, {members} members, {sized} with an instance size");
        Console.WriteLine($"  {ignored} members the engine never writes out");
        Console.WriteLine($"  {structs} members naming the class of a struct");
        Console.WriteLine($"  {fixedArrays} members that are a fixed length array");
        Console.WriteLine($"  {values} values across {enums} enums");
        Console.WriteLine($"written to {output}, {new FileInfo(output).Length / 1024} KB");

        var reread = HavokClassTypes.Parse(File.OpenRead(output));
        Console.WriteLine($"reads back as {reread.Count} classes");
        return reread.Count == classes.Count ? 0 : 1;
    }

    /// Every class every file names, against the definition this build holds for it.
    ///
    /// A packfile stores four bytes in front of each class name, and those four bytes are what a
    /// class definition is: change a member's type or add one and the signature changes with it. So
    /// this is the one check that can say a file was written against the same classes we read it
    /// with, rather than merely that it parsed.
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

    /// The gate on the class table: build the list of fields a file holds from the table alone, and
    /// compare it to the list hkxpack writes for the same file.
    ///
    /// This is the whole question the table exists to answer. The panel's field list comes from
    /// hkxpack's XML today, and it can only stop doing that if the table produces the same list —
    /// the same names, in the same order, including the fields of every struct written inline, which
    /// is where the count of elements has to be read out of the file itself.
    private static int Fields(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }
        NeedHkxPack();

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx").OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };

        int exact = 0, wrong = 0, unresolved = 0, skipped = 0;
        var examples = new List<string>();

        foreach (string file in files)
        {
            string work = Path.Combine(Path.GetTempPath(), "symrm-fields");
            string xml;
            try
            {
                HkxTextEdit.ResetDirectory(work);
                xml = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, file, work));
            }
            catch (Exception e)
            {
                skipped++;
                if (examples.Count < 8) examples.Add($"{Path.GetFileName(file)}: {e.Message.Split('\n')[0]}");
                continue;
            }

            var objects = new PackfileObjects(PackfileImage.Read(file));
            var ids = HkxTextEdit.ObjectIds(xml);
            if (ids.Count != objects.Instances.Count) { skipped++; continue; }

            for (int i = 0; i < ids.Count; i++)
            {
                var predicted = ClassFields.NamesOf(objects, objects.Instances[i]);
                if (predicted == null) { unresolved++; continue; }

                var seen = HkxTextEdit.ReadParams(xml, ids[i]).Select(p => p.Name).ToList();
                if (predicted.SequenceEqual(seen, StringComparer.Ordinal)) { exact++; continue; }

                wrong++;
                if (examples.Count < 8)
                    examples.Add($"{Path.GetFileName(file)} #{ids[i]} {objects.Instances[i].ClassName}\n" +
                                 $"      from the table: {string.Join(" ", predicted.Take(20))}\n" +
                                 $"      from hkxpack  : {string.Join(" ", seen.Take(20))}");
            }
        }

        Console.WriteLine($"{files.Length} file(s): {exact} object(s) whose field list the table " +
                          $"predicts exactly, {wrong} wrong, {unresolved} it could not work out, " +
                          $"{skipped} file(s) skipped");
        foreach (string line in examples) Console.WriteLine("   " + line);

        return wrong == 0 && unresolved == 0 && exact > 0 ? 0 : 1;
    }

    /// What the properties panel would show for every object in a file, against what hkxpack says
    /// about the same fields.
    ///
    /// Not the same question as `crosscheck`, and that is the point of having both. Crosscheck asks
    /// whether the byte reader agrees with hkxpack. This asks whether the values that reach the
    /// window agree with hkxpack, which is a different thing, because between the two sits the
    /// choice of which fields come from the bytes and which fall back. A fallback that silently
    /// returned the wrong value instead of falling back would pass the first check and fail this
    /// one.
    ///
    /// It calls the same `PanelFields.For` the window calls, so what it reports is what is on
    /// screen rather than a second implementation of it.
    /// How much of a state machine's routing an arrow from one state to another can actually carry.
    ///
    /// Drawing transitions on the canvas is only worth doing if a transition is mostly one state to
    /// one state. Two things would make it not worth doing, and both are countable rather than
    /// arguable:
    ///
    /// A `toNestedStateId` other than zero means the transition enters a state *and* sets the
    /// machine inside that state to a particular state of its own. One arrow cannot say that, so
    /// every such transition is either drawn as a stop or drawn wrongly.
    ///
    /// Density is the other. A machine with two hundred transitions in it draws as a hairball
    /// whether or not each arrow is honest, so the count per machine decides whether labels can ever
    /// be on screen at once or only ever on selection.
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
                model = BehaviourGraphModel.Parse(NativeXml.From(File.ReadAllBytes(file)));
            }
            catch
            {
                filesFailed++;
                continue;
            }
            filesRead++;

            // A machine sitting in another machine's state, which is the hierarchy the graph already
            // draws through ownership. Counted to say how common nesting is at all, separately from
            // whether a single transition crosses a level.
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

                // What drawing a wildcard the obvious way would cost. A wildcard fires from any
                // state, so showing it as "from each state to the target" is one line per state per
                // wildcard. Drawing it from the machine instead is one line, and says the same
                // thing: the machine is what any state has in common.
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

                    // What a nested id is has to be established rather than assumed. The reading
                    // being tested is that it names a state of the machine sitting inside the state
                    // being entered. If that holds for every one of them, the reading is right; if
                    // it holds for none, it means something else entirely and nothing should be
                    // drawn for it.
                    string? entered = states.FirstOrDefault(s => s.StateId == row.ToStateId)?.GeneratorRef;
                    var inner = model.Get(entered?.TrimStart('#'));

                    // A machine is often not the state's generator directly: a modifier generator or
                    // a bone switch wraps it, and looking only at the immediate generator counts
                    // those as unexplained. Walk the wrappers before giving up.
                    //
                    // The canvas's own walk, not a second one written here: a measurement that says
                    // every route resolves is worth nothing if the thing drawing the routes resolves
                    // them differently.
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

                // fromNestedStateId sits on the same struct and the reader does not surface it, so
                // it is read here directly rather than assumed to be zero. The flags come off the
                // same pass: a wildcard declares whether it is local to this machine or global, and
                // that is the difference between "any state in here" and "any state at all".
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

                        // Decoded a bit at a time rather than by the text, because the text is
                        // hkxpack's spelling: it prints a name when the value is exactly one
                        // declared flag and the bare number when it is a combination, and a
                        // combination is the common case here. Counting the strings counts 1536 and
                        // 2560 as two unrelated things when they share a bit.
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

        // What the canvas will actually draw, counted the same way. A route that resolves in the
        // measurement above and then does not come back from StateRoutes is a route the picture
        // silently drops, which is the failure this is here to catch.
        long drawable = 0, drawableNested = 0, startStates = 0;
        long waysOut = 0, rewriteWrong = 0, notAState = 0, selfDirect = 0;
        foreach (string file in files)
        {
            try
            {
                var model = BehaviourGraphModel.Parse(NativeXml.From(File.ReadAllBytes(file)));
                var routes = StateRoutes.Of(model);
                drawable += routes.Routes.Count;
                drawableNested += routes.Routes.Count(r => r.IntoId.Length > 0);
                startStates += routes.StartStates.Count;

                // Every way out of every state, which is what the canvas draws once a state is
                // picked out. A wildcard is rewritten to leave the state being asked about rather
                // than the machine, so this checks the rewrite keeps every route between two real
                // states and drops only self transitions.
                foreach (string stateId in routes.MachineOfState.Keys)
                {
                    var leaving = routes.LeavingState(stateId).ToList();
                    waysOut += leaving.Count;

                    // A route that does not leave the state it was asked for, or lands somewhere
                    // that is not a state, is the rewrite being wrong.
                    if (leaving.Any(r => r.FromId != stateId)) rewriteWrong++;
                    if (leaving.Any(r => !routes.MachineOfState.ContainsKey(r.ToId))) notAState++;

                    // A state transitioning to itself is real and is counted rather than faulted. A
                    // wildcard into the state you are standing in is dropped, since it is a self
                    // transition the machine declares for everybody; one a state writes on itself is
                    // that state saying so, and belongs on screen.
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

    /// The state machine a state's generator leads to, through whatever wraps it. A machine is often
    /// not the generator itself: a modifier generator or a bone switch holds it, and a behaviour
    /// reference generator loads another file entirely and leads nowhere this file can see.
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

    /// What the panel puts at the head of each element of an array of structs, printed.
    ///
    /// The panel collapses an element behind this line, so a wrong line hides a wrong element rather
    /// than showing one. Printing them for a whole file is how they get read against the file's own
    /// XML, which is what people were reading before the panel could group anything.
    ///
    /// Needs no Java.
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

        string xml = NativeXml.From(File.ReadAllBytes(target));
        var model = BehaviourGraphModel.Parse(xml);

        int arrays = 0, summarised = 0, unnamed = 0;
        foreach (var obj in model.Objects)
        {
            if (obj.Class != "hkbStateMachineTransitionInfoArray") continue;
            arrays++;

            var lines = ElementSummary.For(model, obj.Id);
            if (lines.Count == 0)
            {
                // An array nothing points at. Its numbers cannot be resolved, because a toStateId
                // only means something inside the machine that owns the array.
                unnamed++;
                Console.WriteLine($"  #{obj.Id}  no state machine points at this array");
                continue;
            }

            summarised += lines.Count;
            string machine = ElementSummary.MachineOwning(model, obj.Id);
            Console.WriteLine($"  #{obj.Id}  on #{machine} {model.Get(machine)?.Str("name")}");
            // By element number, not by the text of the key: sorting `transitions[10]` as a string
            // puts it before `transitions[2]`, which reads as a file whose transitions are shuffled.
            foreach (var key in lines.Keys.OrderBy(ElementNumber))
                Console.WriteLine($"      {key,-16} {lines[key]}");
        }

        Console.WriteLine($"{Path.GetFileName(target),-34} {arrays,4} transition array(s), " +
                          $"{summarised,5} element(s) summarised, {unnamed,3} array(s) with no owner");
        return 0;
    }

    /// A flags value from the text, however hkxpack chose to spell it: a bare number for a
    /// combination, a single declared name when the value is exactly one flag, and names joined by
    /// bars when something has already decoded it.
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

    /// Every field on the panel, written by its path, checked to have moved that field and nothing
    /// else.
    ///
    /// The panel's boxes and the file's values line up by position, and that is the assumption the
    /// whole panel rests on. Naming a field does not preserve it: an array of structs repeats every
    /// name once per element, so a write by name lands on the first of them however far down the
    /// panel the box was. Writing a sentinel through each path and reading the whole object back is
    /// the check that says box N moves value N, for every box of every object in a real file rather
    /// than for a fixture built to pass.
    ///
    /// Needs no Java: the text comes from NativeXml, the same way the window builds it.
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
        string xml = NativeXml.From(File.ReadAllBytes(file));
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
                // The two readings disagree about what is in this object, which the panel already
                // refuses to work from. Nothing to prove about addressing until that is settled.
                unaddressable += fields.Count;
                continue;
            }

            for (int f = 0; f < fields.Count; f++)
            {
                // A sentinel no vanilla value takes, so a field that did not change cannot be
                // mistaken for one that did.
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

                // What the same box did before it carried a path. Addressing by name reaches the
                // first field with that name, so every later one wrote somebody else's value and
                // said it had worked. Counted rather than described, because the size of it is the
                // reason the path exists.
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

    private static int Panel(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }
        NeedHkxPack();

        // A directory sweeps every file under it, the same way model, consumers and walk do. It did
        // not before, and pointing it at one made it try to unpack the directory itself and fall over
        // with a permission error, which reads as a broken tool rather than a wrong argument.
        string target = Path.GetFullPath(argv[1]);
        if (Directory.Exists(target))
        {
            int clean = 0, bad = 0;
            foreach (string each in Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                                             .OrderBy(f => f, StringComparer.Ordinal))
            {
                var carried = new[] { argv[0], each }.Concat(argv.Skip(2)).ToArray();
                if (Panel(carried) == 0) clean++; else bad++;
            }

            Console.WriteLine($"\n{clean} file(s) with nothing wrong on the panel, {bad} not");
            return bad == 0 ? 0 : 1;
        }

        string file = target;
        string work = WorkDirectory("symrm-panel-", file);
        if (Directory.Exists(work)) Directory.Delete(work, true);

        string xmlText = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, file, work));
        var ids = HkxTextEdit.ObjectIds(xmlText);
        var objects = new PackfileObjects(PackfileImage.Read(file));

        if (ids.Count != objects.Instances.Count)
        {
            Console.WriteLine($"{Path.GetFileName(file)}: the window would refuse this file, " +
                              $"{objects.Instances.Count} objects in the bytes against {ids.Count} in the xml");
            return 1;
        }

        string Reference(PackfileObjects.Instance? target, bool wasNull)
        {
            if (wasNull) return "null";
            if (target == null) return "";
            int at = objects.IndexOf(target);
            return at >= 0 && at < ids.Count ? "#" + ids[at] : "";
        }

        int shown = 0, fromBytes = 0, fell = 0, agreed = 0, strided = 0, offered = 0;
        var byClassFallback = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var stridedClasses = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var disagreements = new List<string>();

        for (int i = 0; i < ids.Count; i++)
        {
            var xml = HkxTextEdit.ReadParams(xmlText, ids[i])
                                 .Select(p => (p.Name, p.Value)).ToList();
            var fields = PanelFields.For(objects, objects.Instances[i], xml, Reference);

            for (int f = 0; f < fields.Count; f++)
            {
                shown++;
                if (fields[f].Options.Count > 0) offered++;
                if (fields[f].From == PanelFields.Source.Fallback)
                {
                    fell++;
                    string what = objects.Instances[i].ClassName + "." + fields[f].Name;
                    byClassFallback[what] = byClassFallback.GetValueOrDefault(what) + 1;
                }
                else fromBytes++;

                // Against hkxpack's own text for the same field, whichever way the value came. A
                // fallback compares to the thing it was taken from and must therefore always agree;
                // if one ever does not, the fallback is not doing what it says.
                // hkxpack's text is read out of the file rather than parsed, so an escape is still
                // an escape: `Speed &gt; TrotMaxSpeed`. The bytes hold the character itself. The
                // panel is right to show the character, so the escape is undone here rather than
                // counted as a difference.
                string theirs = System.Net.WebUtility.HtmlDecode(xml[f].Value);
                // Against the raw form as well as the shown one: an enum shows its name and
                // hkxpack sometimes prints the number, and those are the same value.
                if (Same(fields[f].Raw, theirs) || Same(fields[f].Value, theirs))
                {
                    agreed++;
                    continue;
                }

                // Not every disagreement is ours. A struct holding a vector is sixteen aligned, so
                // the compiler pads it and the game records the padded size; hkxpack has no size in
                // its data and rounds the end of the last member up to eight, so from the second
                // element of an array of one of those onwards it reads from the wrong place. Those
                // are counted apart and named, because calling them our disagreements would be
                // wrong and dropping them would be worse.
                if (fields[f].Owner.Length > 0 &&
                    HavokClassTypes.Shipped.PaddedBeyondHkxPack(fields[f].Owner))
                {
                    strided++;
                    stridedClasses[fields[f].Owner] = stridedClasses.GetValueOrDefault(fields[f].Owner) + 1;
                    continue;
                }

                if (disagreements.Count < 20)
                    disagreements.Add($"#{ids[i]} {objects.Instances[i].ClassName}.{fields[f].Name} " +
                                      $"({fields[f].From}): panel shows '{fields[f].Value}', " +
                                      $"hkxpack says '{theirs}'");
            }
        }

        Console.WriteLine($"{Path.GetFileName(file)}: {shown} values on the panel, " +
                          $"{fromBytes} from the bytes, {fell} fallen back to hkxpack, " +
                          $"{agreed} agreeing, {shown - agreed - strided} not" +
                          (strided > 0 ? $", {strided} where hkxpack strides a padded struct wrongly" : "") +
                          $", {offered} offered as a list of declared values");

        foreach (var (cls, count) in stridedClasses.OrderByDescending(c => c.Value))
            Console.WriteLine($"  hkxpack mis-strides {cls} x{count}");

        foreach (var (what, count) in byClassFallback.OrderByDescending(f => f.Value).Take(8))
            Console.WriteLine($"  fell back: {what} x{count}");
        foreach (string line in disagreements) Console.WriteLine("  " + line);

        return shown == agreed + strided ? 0 : 1;
    }

    /// Two readings of one file, set beside each other field by field.
    ///
    /// Both readings come from the same producer at the moment, which sounds like a command that
    /// cannot say anything and is the point: it is how the comparison gets to report zero before
    /// anything is asked of it. The second reading becomes the byte reader when there is one, and
    /// this is what will say whether it agrees. The faults the comparison has to catch are in the
    /// suite rather than here, because deliberately breaking a file to prove a checker works is a
    /// test, not a thing to run over a corpus.
    private static int Model(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }
        NeedHkxPack();

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToList()
            : new List<string> { target };

        int clean = 0, bad = 0, unreadable = 0, objects = 0, compared = 0, disagreements = 0, strided = 0;
        var stridedBy = new Dictionary<string, int>(StringComparer.Ordinal);
        bool one = files.Count == 1;

        foreach (string file in files)
        {
            string work = WorkDirectory("symrm-model-", file);
            string xml;
            try
            {
                HkxTextEdit.ResetDirectory(work);
                xml = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, file, work));
            }
            catch (Exception e)
            {
                Console.WriteLine($"  {Path.GetFileName(file)}: skipped, {e.Message.Split('\n')[0]}");
                continue;
            }

            var first = BehaviourGraphModel.Parse(xml);
            var second = SecondReading(xml, file);

            // Not a disagreement. The reading refuses whole rather than coming back with holes in it
            // when the class table cannot describe everything in the file, and saying so is the
            // point: a file counted as agreeing because neither side read it would be the worst
            // possible way to pass.
            if (second == null)
            {
                unreadable++;
                Console.WriteLine($"{Path.GetFileName(file)}: no reading, the class table does not " +
                                  "describe every class in it");
                continue;
            }

            var result = ModelDiff.Compare(first, second, 40, MisStrided);

            objects += result.Objects;
            compared += result.Compared;
            disagreements += result.Total;
            strided += result.Strided;
            foreach (var (field, count) in result.StridedBy)
                stridedBy[field] = stridedBy.GetValueOrDefault(field) + count;
            if (result.Clean) clean++; else bad++;

            if (one || !result.Clean)
            {
                Console.WriteLine($"{Path.GetFileName(file)}: {result}");
                foreach (var difference in result.Shown) Console.WriteLine("  " + difference);
                if (result.Total > result.Shown.Count)
                    Console.WriteLine($"  and {result.Total - result.Shown.Count} more");
            }
        }

        Console.WriteLine($"\n{clean} file(s) agreeing, {bad} not, {unreadable} without a reading, " +
                          $"{objects} object(s), {compared} field(s) compared, " +
                          $"{disagreements} disagreement(s), {strided} where hkxpack strides a " +
                          "padded struct wrongly");

        foreach (var (field, count) in stridedBy.OrderByDescending(f => f.Value))
            Console.WriteLine($"  strided: {field} x{count}");

        return bad == 0 ? 0 : 1;
    }

    /// What the tool does with each of the two readings, set beside each other.
    ///
    /// The model command says the readings hold the same values. This says the tool behaves the same
    /// way on them: the same wires on the canvas, the same variables and events, the same findings
    /// from the checker, the same rows in every state machine. If the fields agree these agree too,
    /// which is exactly why it is worth running: it is what catches a field comparison that passed
    /// for the wrong reason.
    private static int Consumers(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }
        NeedHkxPack();

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToList()
            : new List<string> { target };

        int clean = 0, bad = 0, unreadable = 0, compared = 0, differing = 0;
        int roleLines = 0, roleAgreeing = 0, roleDiffering = 0;
        bool one = files.Count == 1;

        foreach (string file in files)
        {
            string work = WorkDirectory("symrm-consumers-", file);
            string xml;
            try
            {
                HkxTextEdit.ResetDirectory(work);
                xml = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, file, work));
            }
            catch (Exception e)
            {
                Console.WriteLine($"  {Path.GetFileName(file)}: skipped, {e.Message.Split('\n')[0]}");
                continue;
            }

            var objects = new PackfileObjects(PackfileImage.Read(file));
            var second = NativeGraphModel.From(objects);
            if (second == null)
            {
                unreadable++;
                Console.WriteLine($"{Path.GetFileName(file)}: no reading from the bytes");
                continue;
            }

            // What each event is used for, from the text and from the bytes. Not part of the model
            // comparison because it is not read from the model: it is a walk of every place an index
            // is written, including nesting the model does not carry, which is why it was the last
            // thing here still needing Java. Compared here because this is where both readings of a
            // file are already open.
            var told = Roles(EventUsage.ByEvent(xml));
            var read = Roles(EventUsage.ByEvent(objects));
            roleLines += Math.Max(told.Count, read.Count);

            for (int i = 0; i < Math.Max(told.Count, read.Count); i++)
            {
                string fromText = i < told.Count ? told[i] : "(nothing)";
                string fromBytes = i < read.Count ? read[i] : "(nothing)";

                if (string.Equals(fromText, fromBytes, StringComparison.Ordinal)) roleAgreeing++;
                else
                {
                    roleDiffering++;
                    if (roleDiffering <= 10)
                        Console.WriteLine($"{Path.GetFileName(file)} roles: {fromText}\n" +
                                          $"  against {fromBytes}");
                }
            }

            var result = ConsumerDiff.Compare(BehaviourGraphModel.Parse(xml), second);
            compared += result.Compared;
            differing += result.Differences.Count;
            if (result.Clean) clean++; else bad++;

            if (one || !result.Clean)
            {
                Console.WriteLine($"{Path.GetFileName(file)}: {result}");
                foreach (var difference in result.Differences) Console.WriteLine("  " + difference);
            }
        }

        Console.WriteLine($"\n{clean} file(s) behaving the same, {bad} not, {unreadable} without a " +
                          $"reading, {compared} output(s) compared, {differing} differing");
        Console.WriteLine($"event roles: {roleLines} line(s) compared, {roleAgreeing} agreeing, " +
                          $"{roleDiffering} not");

        return bad == 0 && roleDiffering == 0 ? 0 : 1;
    }

    /// What order the two pointer tables are in, worked out from the classes and checked against
    /// the file.
    ///
    /// The rule itself lives in FixupOrder, because writing has to reproduce it. This is what says
    /// the rule is the file's own and not our idea of it.
    /// Adds one object to a real file and checks what hkxpack makes of the result.
    ///
    /// The count is not the check. A count matches even when every object after an insertion point
    /// shifted by one, which is exactly the failure worth catching, so this compares the class of
    /// every number before and after and then asserts the new object is the last number rather than
    /// somewhere in the middle.
    private static int Append(string[] argv)
    {
        if (argv.Length < 3) { Usage(); return 1; }
        NeedHkxPack();

        string file = Path.GetFullPath(argv[1]);
        string className = argv[2];

        // A save that changes nothing has to give back the same bytes, or nothing measured after an
        // append can be attributed to the append.
        var original = File.ReadAllBytes(file);
        if (!PackfileImage.Read(original).Rebuild().SequenceEqual(original))
        {
            Console.WriteLine("the file does not survive a save that changes nothing, so nothing " +
                              "below would mean anything");
            return 1;
        }
        Console.WriteLine($"{Path.GetFileName(file)}: a save with no changes is byte identical");

        string work = WorkDirectory("symrm-append-", file);
        HkxTextEdit.ResetDirectory(work);
        var told = Numbered(HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, file, work)));

        var image = PackfileImage.Read(original);
        var added = NativeAppend.Object(image, className);
        Console.WriteLine($"appended {className} as {added}");

        // Attaching is the half that makes it an edit rather than an orphan. Given a source object
        // and one of its pointer fields, the new object is wired into the graph and the round trip
        // below has to show hkxpack reading that field as the new number.
        string attachTo = argv.Length > 4 ? argv[4] : "";
        int attachFrom = argv.Length > 3 && int.TryParse(argv[3], out int f) ? f : -1;

        if (attachFrom >= 0 && attachTo.Length > 0)
        {
            NativeAppend.Attach(image, attachFrom, attachTo, added.Id);
            Console.WriteLine($"attached: #{attachFrom}.{attachTo} now points at #{added.Id}");
        }

        string written = Path.Combine(work, "appended.hkx");
        image.Save(written);

        // Read back from disk rather than from the image in memory, so anything the rebuild gets
        // wrong shows up here rather than being carried over.
        var reloaded = new PackfileObjects(PackfileImage.Read(written));
        Console.WriteLine($"reloaded from disk: {reloaded.Instances.Count} object(s), " +
                          $"last is {reloaded.Instances[^1].ClassName}");

        string second = WorkDirectory("symrm-append-out-", written);
        HkxTextEdit.ResetDirectory(second);
        var read = Numbered(HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, written, second)));

        int moved = 0;
        foreach (var (id, was) in told)
            if (!read.TryGetValue(id, out string? now) || now != was)
            {
                if (moved < 8)
                    Console.WriteLine($"  #{id} was {was} and is now {(now ?? "absent")}");
                moved++;
            }

        bool numbered = read.TryGetValue(added.Id, out string? newClass) && newClass == className;

        // Read out of hkxpack's own text rather than out of our reading of the file, because the
        // question is whether hkxpack sees the wire, not whether we do.
        bool wired = attachFrom < 0 || attachTo.Length == 0;
        if (!wired)
        {
            string after2 = HkxTextEdit.ReadXml(Path.Combine(second,
                                Path.GetFileNameWithoutExtension(written) + ".xml"));
            var block = HkxTextEdit.ReadParams(after2, attachFrom.ToString());
            string held = block.FirstOrDefault(p => p.Name == attachTo)?.Value ?? "(absent)";
            wired = held == "#" + added.Id;
            Console.WriteLine($"hkxpack reads #{attachFrom}.{attachTo} as {held}, " +
                              $"expected #{added.Id}");
        }

        // The editor's save path adds an object too, and it used to answer a class the file has
        // never named by refusing while this path named it. There is one answer now, and this is
        // what says so: the same addition made through NativeSave lands the same class name in the
        // same section, on the file hkxpack just agreed about.
        string viaSave = Path.Combine(work, "via-save.hkx");
        File.WriteAllBytes(viaSave, original);

        var plan = new NativeSave.Plan(
            new List<NativeSave.Change> { new(className, 0, "", "#" + added.Id, Added: true) }, null);

        var saved = PackfileImage.Read(NativeSave.Apply(viaSave, plan));
        var savedObjects = new PackfileObjects(saved);

        // Both read back off their written bytes rather than one out of memory, because the name
        // table is trimmed of its 0xFF filler while a name is being added and padded again on the
        // way out, so an in memory section and a written one differ for a reason that is not this.
        bool agrees = savedObjects.Instances.Count == reloaded.Instances.Count &&
                      savedObjects.Instances[^1].ClassName == className &&
                      saved.Section("__classnames__")!.Data
                           .SequenceEqual(PackfileImage.Read(written).Section("__classnames__")!.Data);

        Console.WriteLine($"the save path adds it too: {savedObjects.Instances.Count} object(s), " +
                          $"last is {savedObjects.Instances[^1].ClassName}, name table " +
                          (agrees ? "identical to the append path" : "DIFFERENT from the append path"));

        Console.WriteLine($"\nhkxpack read {told.Count} object(s) before and {read.Count} after, " +
                          $"{moved} of the original numbers holding something else, " +
                          $"the new one is {(numbered ? $"#{added.Id} {className} as predicted" : "not where it was predicted")}");

        return moved == 0 && numbered && wired && agrees && read.Count == told.Count + 1 ? 0 : 1;
    }

    /// Takes an object out of the graph without taking it out of the file, and checks what hkxpack
    /// makes of the result.
    ///
    /// The check is not that the object is gone, because it is not meant to be. It is that nothing
    /// reaches it any more, that it still holds the class it held, that every other number is
    /// untouched, and that an array which held it got shorter rather than gaining a null. That last
    /// one matters more than it looks: the engine reads a child's vtable without a null check, so a
    /// null left in a children array is a crash on load.
    private static int Orphan(string[] argv)
    {
        if (argv.Length < 3) { Usage(); return 1; }
        NeedHkxPack();

        string file = Path.GetFullPath(argv[1]);
        int id = int.Parse(argv[2]);

        var original = File.ReadAllBytes(file);
        if (!PackfileImage.Read(original).Rebuild().SequenceEqual(original))
        {
            Console.WriteLine("the file does not survive a save that changes nothing");
            return 1;
        }

        string work = WorkDirectory("symrm-orphan-", file);
        HkxTextEdit.ResetDirectory(work);
        string beforeXml = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, file, work));
        var told = Numbered(beforeXml);

        int pointedAt = References(beforeXml, id);
        Console.WriteLine($"{Path.GetFileName(file)}: #{id} is a {told.GetValueOrDefault(id, "?")}, " +
                          $"reached from {pointedAt} place(s)");

        var image = PackfileImage.Read(original);
        var result = NativeRemove.Orphan(image, id);
        Console.WriteLine($"orphaned {result}");

        string written = Path.Combine(work, "orphaned.hkx");
        image.Save(written);

        string second = WorkDirectory("symrm-orphan-out-", written);
        HkxTextEdit.ResetDirectory(second);
        string afterXml = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, written, second));
        var read = Numbered(afterXml);

        int moved = told.Count(p => !read.TryGetValue(p.Key, out string? now) || now != p.Value);
        int left = References(afterXml, id);
        bool present = read.ContainsKey(id) && read[id] == told.GetValueOrDefault(id);

        // A null child is the thing that must not appear. Counted across the whole file rather than
        // in the arrays that changed, because an orphan that pushes one anywhere is still a crash.
        int nullsBefore = Nulls(beforeXml), nullsAfter = Nulls(afterXml);

        Console.WriteLine($"\nhkxpack read {told.Count} object(s) before and {read.Count} after, " +
                          $"{moved} of the original numbers holding something else, " +
                          $"#{id} is {(present ? "still there" : "gone")} and now reached from " +
                          $"{left} place(s), null children {nullsBefore} before and {nullsAfter} after");

        return moved == 0 && present && left == 0 && read.Count == told.Count
               && nullsAfter == nullsBefore ? 0 : 1;
    }

    /// How many places in the text point at an object, not counting the line that declares it.
    /// hkxpack writes the object's own id as its name attribute, and counting that as a reference
    /// makes an orphan look like it is still reached from one place.
    private static int References(string xml, int id) =>
        System.Text.RegularExpressions.Regex.Matches(xml, $@"#{id}\b").Count
        - System.Text.RegularExpressions.Regex.Matches(xml, $@"name=""#{id}""").Count;

    /// Null elements inside an array of object pointers, which is the shape the engine crashes on.
    private static int Nulls(string xml) =>
        System.Xml.Linq.XDocument.Parse(xml).Descendants("hkparam")
            .Where(p => p.Attribute("numelements") != null && !p.Elements().Any())
            .Sum(p => (p.Value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                                     .Count(t => t == "null"));

    /// Every object hkxpack numbers, by number, with the class it holds.
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

            // The object list is the virtual fixup table read in order, and an object's `#id` is its
            // position in that list. Appending a new object puts its bytes at the end of the section
            // and its entry at the end of this table, and that only gives the new object the last
            // number if table order and file order are already the same thing. Measured rather than
            // assumed, because everything downstream of an append rests on it: the per class index a
            // change names, and the `#id` hkxpack will print.
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

    /// Where every event and variable index is written, read out of the text and out of the bytes,
    /// set beside each other.
    ///
    /// This is the last thing the symbols tab needed hkxpack for. The graph model cannot answer it,
    /// because these indices sit deeper than the one level of nesting the model records, so the byte
    /// side walks the class table rather than the model. Two different walks over two different
    /// forms of the same file, which is what makes agreement worth anything.
    private static int Symbols(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }
        NeedHkxPack();

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       .OrderBy(f => f, StringComparer.Ordinal).ToList()
            : new List<string> { target };

        int clean = 0, bad = 0, compared = 0, differing = 0;
        bool one = files.Count == 1;

        foreach (string file in files)
        {
            string work = WorkDirectory("symrm-symbols-", file);
            string xml;
            try
            {
                HkxTextEdit.ResetDirectory(work);
                xml = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, file, work));
            }
            catch (Exception e)
            {
                Console.WriteLine($"  {Path.GetFileName(file)}: skipped, {e.Message.Split('\n')[0]}");
                continue;
            }

            var objects = new PackfileObjects(PackfileImage.Read(file));
            var problems = new List<string>();

            foreach (bool events in new[] { true, false })
            {
                var text = SymbolIndexFixup.Usages(xml, events);
                var bytes = SymbolIndexFixup.Usages(objects, events);
                compared += text.Count;

                string what = events ? "events" : "variables";
                if (text.Count != bytes.Count)
                {
                    problems.Add($"{what}: {text.Count} usage(s) in the text against {bytes.Count}");
                    continue;
                }

                for (int i = 0; i < text.Count; i++)
                    if (!Same(text[i], bytes[i]))
                    {
                        problems.Add($"{what} {i}: {Spell(text[i])} against {Spell(bytes[i])}");
                        if (problems.Count > 8) break;
                    }
            }

            // What the symbols tab actually draws under each event, not only the sites it is built
            // from. The rows are the thing a person sees, so agreeing on the sites and disagreeing
            // on the rows would still be a different tab.
            var rowsText = EventUsage.ByEvent(xml);
            var rowsBytes = EventUsage.ByEvent(objects);
            compared += rowsText.Sum(e => e.Value.Count);

            if (rowsText.Count != rowsBytes.Count)
                problems.Add($"events with usage: {rowsText.Count} in the text against {rowsBytes.Count}");
            else
                foreach (var (index, lines) in rowsText.OrderBy(e => e.Key))
                {
                    if (!rowsBytes.TryGetValue(index, out var mine))
                    {
                        problems.Add($"event {index}: {lines.Count} line(s) in the text against none");
                        continue;
                    }
                    if (EventUsage.Summarise(lines) != EventUsage.Summarise(mine))
                        problems.Add($"event {index}: \"{EventUsage.Summarise(lines)}\" against " +
                                     $"\"{EventUsage.Summarise(mine)}\"");
                }

            var unknownText = SymbolIndexFixup.UnknownIndexFields(xml);
            var unknownBytes = SymbolIndexFixup.UnknownIndexFields(objects);
            compared++;
            if (!unknownText.SequenceEqual(unknownBytes, StringComparer.Ordinal))
                problems.Add($"unrecognised index fields: {unknownText.Count} in the text " +
                             $"against {unknownBytes.Count}");

            differing += problems.Count;
            if (problems.Count == 0) clean++; else bad++;

            if (one || problems.Count > 0)
            {
                Console.WriteLine($"{Path.GetFileName(file)}: {compared} usage(s) compared, " +
                                  $"{problems.Count} differing");
                foreach (string problem in problems) Console.WriteLine("  " + problem);
            }
        }

        Console.WriteLine($"\n{clean} file(s) agreeing, {bad} not, {compared} usage(s) compared, " +
                          $"{differing} differing");

        return bad == 0 ? 0 : 1;
    }

    private static bool Same(SymbolIndexFixup.Usage a, SymbolIndexFixup.Usage b) =>
        a.Index == b.Index && a.Owner == b.Owner && a.Member == b.Member &&
        a.ObjectId == b.ObjectId && a.OwnerClass == b.OwnerClass;

    private static string Spell(SymbolIndexFixup.Usage u) =>
        $"#{u.ObjectId} {u.OwnerClass} {u.Owner}.{u.Member}={u.Index}";

    /// Every event's roles as lines, so two readings of them can be set beside each other.
    private static List<string> Roles(Dictionary<int, List<EventUsage.Line>> byEvent)
    {
        var lines = new List<string>();
        foreach (var (index, sites) in byEvent.OrderBy(e => e.Key))
            foreach (var site in sites)
                lines.Add($"event {index} {site.Role} {site.Site} x{site.Count} " +
                          string.Join(",", site.ObjectIds));
        return lines;
    }

    /// The fields hkxpack reads at the wrong stride: an array whose elements are a struct aligned to
    /// sixteen, which it sizes by rounding the last member up to eight. Three classes in the vanilla
    /// corpus qualify, and the size we use comes from the game's own class registration rather than
    /// from a rule about where members end.
    private static bool MisStrided(string owningClass, string field)
    {
        var types = HavokClassTypes.Shipped;
        foreach (var member in types.Members(owningClass))
            if (member.Name == field)
                return member.CType != null && types.PaddedBeyondHkxPack(member.CType);

        return false;
    }

    /// The reading being checked. One line, and it is the whole of what changes when the byte reader
    /// takes over, which is why it is a method rather than sitting inline in the loop above.
    private static BehaviourGraphModel? SecondReading(string xml, string hkxPath) =>
        NativeGraphModel.From(new PackfileObjects(PackfileImage.Read(hkxPath)));

    private static int CrossCheck(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        NeedHkxPack();

        // A directory sweeps every file under it, the same way model, consumers, walk and panel do.
        // It did not before, and pointing it at one made it try to unpack the directory itself.
        string target = Path.GetFullPath(argv[1]);
        if (Directory.Exists(target))
        {
            int clean = 0, bad = 0;
            foreach (string each in Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                                             .OrderBy(f => f, StringComparer.Ordinal))
            {
                var carried = new[] { argv[0], each }.Concat(argv.Skip(2)).ToArray();
                if (CrossCheck(carried) == 0) clean++; else bad++;
            }

            Console.WriteLine($"\n{clean} file(s) where every value agrees, {bad} not");
            return bad == 0 ? 0 : 1;
        }

        string file = target;
        string work = WorkDirectory("symrm-crosscheck-", file);
        if (Directory.Exists(work)) Directory.Delete(work, true);

        string xmlFile = HkxTextEdit.Unpack(_java, _jar, file, work);

        var document = System.Xml.Linq.XDocument.Load(xmlFile);
        var objects = new PackfileObjects(PackfileImage.Read(file));

        // hkxpack names its objects `#90`, `#91` and so on in the order they sit in the file, which
        // is the order the virtual fixups give us, so the two lists line up position for position.
        // That is checked rather than assumed: if it does not hold, references are compared by the
        // class they point at instead of by which object exactly, and the file says so.
        // An id, not any name: an inline struct carries a name attribute too, and it holds the
        // field it sits in rather than an id, so counting those makes 1,519 objects out of 906.
        var named = document.Descendants("hkobject")
                            .Where(e => e.Attribute("name")?.Value.StartsWith('#') == true).ToList();
        bool idsLineUp = named.Count == objects.Instances.Count &&
                         named.Select((e, i) => e.Attribute("class")?.Value == objects.Instances[i].ClassName)
                              .All(matched => matched);

        if (!idsLineUp)
        {
            int at = named.Zip(objects.Instances)
                          .TakeWhile(p => p.First.Attribute("class")?.Value == p.Second.ClassName)
                          .Count();
            Console.WriteLine($"  the two orderings differ: hkxpack has {named.Count} named objects, " +
                              $"we have {objects.Instances.Count}, first differing at {at}" +
                              (at < named.Count && at < objects.Instances.Count
                                   ? $" where hkxpack says {named[at].Attribute("class")?.Value} " +
                                     $"and we say {objects.Instances[at].ClassName}"
                                   : ""));
        }

        var indexOfId = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < named.Count; i++) indexOfId[named[i].Attribute("name")!.Value] = i;

        var indexOf = new Dictionary<PackfileObjects.Instance, int>();
        for (int i = 0; i < objects.Instances.Count; i++) indexOf[objects.Instances[i]] = i;

        string Reference(string id) =>
            idsLineUp && indexOfId.TryGetValue(id, out int at) ? "@" + at : id;

        var byClass = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.Ordinal);
        foreach (var element in document.Descendants("hkobject"))
        {
            string? cls = element.Attribute("class")?.Value;
            if (cls == null) continue;

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in element.Elements("hkparam"))
            {
                string? name = p.Attribute("name")?.Value;
                if (name != null) fields[name] = Canonical(p, Reference);
            }
            if (!byClass.TryGetValue(cls, out var list)) byClass[cls] = list = new();
            list.Add(fields);
        }

        int compared = 0, agreed = 0;
        var disagreements = new List<string>();
        var unread = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int countedOnly = 0;

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

                    string? actual = Rendered(objects, ours[i], member, indexOf, expected);
                    if (actual == null)
                    {
                        // Counted rather than passed over. A field type nobody reads is the reason
                        // hkxpack is still needed to open a file, so it has to show up somewhere.
                        unread[member.Type] = unread.GetValueOrDefault(member.Type) + 1;
                        continue;
                    }

                    compared++;
                    // An array of inline structs is compared by how many elements it has and no
                    // further: the layout dump does not name the struct's own class, so there is
                    // nothing to read the elements with. Counted separately rather than presented
                    // as a field we can read.
                    if (member.Type == "array of struct") countedOnly++;
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

        if (countedOnly > 0)
            Console.WriteLine($"  of those, {countedOnly} are arrays of inline structs, where only " +
                              "the element count is read and not the elements");

        if (unread.Count > 0)
            Console.WriteLine("  still needing hkxpack to read: " +
                              string.Join(", ", unread.OrderByDescending(u => u.Value)
                                                      .Select(u => $"{u.Key} x{u.Value}")));

        return compared > 0 && compared == agreed && disagreements.Count == 0 ? 0 : 1;
    }

    /// How hkxpack writes a value, so the two sides can be compared as text. An element it renders
    /// as a nest of objects is reduced to how many there are, which is the part of it we can read;
    /// the count is still a real check, since it comes from the array's own header.
    private static string Canonical(System.Xml.Linq.XElement p, Func<string, string> reference)
    {
        string raw = p.Value ?? "";
        string text = raw.Trim();

        // A single value is taken exactly as it stands, spaces and all. Trimming it looked like
        // tidying and was throwing data away: four state machines and a layer generator in vanilla
        // are named with a leading space, and one event payload ends in one. Measured before
        // changing it, across 374,120 single valued fields in the unpacked corpus: six carry a
        // space that matters and not one runs over more than a line, so there is nothing here that
        // trimming would have been normalising.
        if (p.Attribute("numelements") == null)
            return raw.StartsWith('#') ? reference(raw) : raw;

        int count = int.Parse(p.Attribute("numelements")!.Value);
        if (p.Elements("hkobject").Any()) return List(count, "structs");

        var strings = p.Elements("hkcstring").ToList();
        if (strings.Count > 0)
            return List(count, strings.Select(s => (s.Value ?? "").Trim()));

        // An element that is itself several numbers is written in brackets, so the brackets are the
        // element boundary. Splitting on whitespace instead turns one vector into four elements and
        // reports a file that agrees as a file that does not.
        if (text.Contains('('))
        {
            var groups = System.Text.RegularExpressions.Regex.Matches(text, @"\([^)]*\)")
                             .Select(m => m.Value).ToList();

            // hkxpack writes one bracket per vector, so an element that is more than one vector
            // arrives as several: a transform is a translation, a rotation and a scale, three
            // brackets for one element. Put back together per element, or a skeleton's 9 element
            // reference pose reads as 27 elements and disagrees with itself.
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

    /// An empty array has nothing in it to describe, however unreadable its elements would be, so it
    /// reads the same on both sides rather than as a count with a word after it.
    private static string List(int count, string what) => count == 0 ? "[0: ]" : $"[{count}: {what}]";

    /// One renderer, not two. The window reads a field through `FieldRender`, so a check that used
    /// its own copy of the same switch would be checking something nobody runs. This is the shim
    /// that spells a reference the way the checker wants it and hands the rest over.
    private static string? Rendered(PackfileObjects objects, PackfileObjects.Instance instance,
                                    HavokClasses.Member member,
                                    Dictionary<PackfileObjects.Instance, int> indexOf,
                                    string expected)
    {
        string Reference(PackfileObjects.Instance? target, bool wasNull) =>
            wasNull ? "null"
            : target != null && indexOf.TryGetValue(target, out int at) ? "@" + at
            : "a pointer landing where no object begins";

        // The checker walks a class's members from the dump; the renderer works from the table.
        // Where a member is in both, the offsets agree, which is a thing 3,894 comparisons say and
        // not an assumption. Where it is only in the dump there is nothing to render it with, and
        // it is counted as unread rather than guessed at.
        var described = HavokClassTypes.Shipped.Members(instance.ClassName)
                                       .FirstOrDefault(m => m.Name == member.Name);
        if (described == null) return null;

        return FieldRender.Render(objects, instance.Offset + described.Offset, instance.ClassName,
                                  described, Reference, expected);
    }

    /// hkxpack and a raw read spell the same value differently: 1 against 1.0, true against 1, and a
    /// null pointer against an empty element. Comparing the text as typed would report every one of
    /// those as a disagreement and drown the real ones.
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

        // An enum, carried as its number and its name. hkxpack prints one or the other, so whichever
        // it printed is what gets compared.
        int colon = ours.IndexOf(':');
        if (colon > 0 && long.TryParse(ours[..colon], out long number))
            return long.TryParse(theirs, out long theirNumber)
                ? number == theirNumber
                : ours[(colon + 1)..] == theirs;

        // A list: same length, then the same values, compared as numbers when both sides are
        // numbers. The two spell a float differently, so comparing the text would report every
        // array of reals as a disagreement.
        if (ours.StartsWith('[') && theirs.StartsWith('['))
        {
            // Trimmed: the space after the count belongs to the list, not to its first element, and
            // left on it the first element of every list of vectors stops looking like a vector.
            var mine = ours[(ours.IndexOf(':') + 1)..^1]
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var yours = theirs[(theirs.IndexOf(':') + 1)..^1]
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ours[..ours.IndexOf(':')] != theirs[..theirs.IndexOf(':')]) return false;
            return mine.Length == yours.Length && mine.Zip(yours).All(p => Same(p.First, p.Second));
        }

        // A vector is a list of numbers and the two sides spell them differently: 0 against 0.0,
        // and hkxpack breaks a transform over several lines. Compared number by number, which is
        // the only comparison that says anything about the bytes.
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

        // No last chance rule that ignores whitespace. Both sides now carry a value's spaces as
        // they are, so a difference in them is a difference in the data and has to be reported as
        // one rather than trimmed until it agrees.
        return false;
    }

    // What the object layer sees in a file: every object, its class, and the fields of whichever
    // class is asked about. The second half is the one that matters, because reading a field out of
    // the bytes is checkable against the same field in hkxpack's XML, and the two agreeing is what
    // says the offsets are right rather than merely plausible.
    // Lengthening an array of structs, carried out rather than planned.
    //
    // Bounding a variable is the edit that needs it: the bounds array is positional, so a bound on
    // variable 83 means an array 84 long, and it is empty in 224 of the 531 vanilla behaviours. The
    // file is written, read back through hkxpack, and set against what the edit asked for, because a
    // plan that says it can be written proves nothing about what lands in the bytes.
    private static int Grow(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        // Java is what makes this worth proving twice. With a runtime the written file is read back
        // through hkxpack, which is a second implementation and therefore the stronger check. Without
        // one the whole edit still has to work, because lengthening an array is exactly the operation
        // that used to send bounds through hkxpack, and the reason for doing it natively was to stop
        // needing Java at all. So the read and the read back go through our own reader instead, and
        // the run says which of the two it was.
        bool java = false;
        try { NeedHkxPack(); java = true; }
        catch (InvalidOperationException) { }

        Console.WriteLine(java
            ? "reading and checking through hkxpack"
            : "no Java runtime, so reading and checking through our own reader");

        var files = Directory.Exists(argv[1])
            ? Directory.EnumerateFiles(argv[1], "*.hkx", SearchOption.AllDirectories).OrderBy(f => f).ToList()
            : new List<string> { Path.GetFullPath(argv[1]) };

        int done = 0, refused = 0, wrong = 0;

        foreach (string file in files)
        {
            string work = WorkDirectory("symrm-grow-", file);
            HkxTextEdit.ResetDirectory(work);
            Directory.CreateDirectory(work);

            string xml;
            try
            {
                xml = java ? HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, file, work))
                           : NativeXml.From(File.ReadAllBytes(file));
            }
            catch (Exception e) { Console.WriteLine($"{Path.GetFileName(file),-46} unreadable: {e.Message}"); continue; }

            var model = BehaviourGraphModel.Parse(xml);
            var names = SymbolEditor.VariableNames(model);
            if (names.Count == 0) { Console.WriteLine($"{Path.GetFileName(file),-46} no variables"); continue; }

            int had = SymbolEditor.Audit(model).Bounds;
            int target = names.Count - 1;
            if (had > target) { Console.WriteLine($"{Path.GetFileName(file),-46} bounds already reach the last variable"); continue; }

            const string low = "-1", high = "7";
            string bounded = SymbolEditor.SetVariableBounds(xml, target, low, high);
            var plan = NativeSave.Compare(xml, bounded);

            Console.WriteLine($"\nFILE {Path.GetFileName(file)}");
            Console.WriteLine($"  variableBounds {had} -> {names.Count} for {names.Count} variable(s), " +
                              $"bounding '{names[target]}' from {low} to {high}");

            if (!plan.Possible)
            {
                Console.WriteLine($"  REFUSED  {plan.Refusal}");
                refused++;
                continue;
            }

            Console.WriteLine($"  planned: {plan.Changes.Count} change(s), grows={plan.Grows}");

            byte[] written;
            try { written = NativeSave.Apply(file, plan); }
            catch (Exception e) { Console.WriteLine($"  THREW  {e.Message}"); wrong++; continue; }

            string grownPath = Path.Combine(work, "grown.hkx");
            File.WriteAllBytes(grownPath, written);

            string reread;
            if (java)
            {
                string back = Path.Combine(work, "back");
                HkxTextEdit.ResetDirectory(back);
                reread = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, grownPath, back));
            }
            else reread = NativeXml.From(written);

            // What the edit asked for, and nothing else. Every object hkxpack reads out of the
            // written file is set against the same object in the text the edit produced, so a value
            // that moved anywhere it was not asked to move shows up as a difference.
            var wanted = RepackCheck.Take(bounded);
            var got = RepackCheck.Take(reread);

            int compared = Math.Min(wanted.InOrder.Count, got.InOrder.Count);
            var differing = new List<string>();
            for (int o = 0; o < compared; o++)
                if (wanted.InOrder[o].Body != got.InOrder[o].Body)
                    differing.Add($"#{wanted.InOrder[o].Id} {wanted.InOrder[o].Class}");

            string readBack = BoundAt(reread, target);
            bool right = readBack == $"{low} to {high}" && differing.Count == 0 &&
                         wanted.Objects == got.Objects;

            Console.WriteLine($"  bound {target} reads back as {readBack}");
            Console.WriteLine($"  {wanted.Objects} object(s) asked for, {got.Objects} in the written " +
                              $"file, {differing.Count} whose values differ" +
                              (differing.Count > 0 ? ": " + string.Join(", ", differing.Take(4)) : ""));
            Console.WriteLine($"  file grew by {written.Length - new FileInfo(file).Length} bytes");
            Console.WriteLine("  " + (right ? "GOOD" : "WRONG"));

            if (right) done++; else wrong++;
        }

        Console.WriteLine($"\nGROW written={done} refused={refused} wrong={wrong}");
        return wrong == 0 ? 0 : 1;
    }

    /// One element of the bounds array as hkxpack wrote it, so a bound can be read back by position
    /// rather than by hunting for the first one in the file.
    private static string BoundAt(string xml, int index)
    {
        // Read through the document rather than off the text. A bound is two objects nested inside
        // an element of an array, so the closing tag that ends the array is not the first one after
        // it, and a reader that takes it as the first one reports an array of nothing however many
        // bounds are really there.
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

    // Writing an animation out uncompressed, and reading it back to see whether it survived.
    //
    // The check is the whole point. A compressed clip is decoded, written out frame by frame, and
    // decoded again from the file that was produced, and every frame of every track has to come back
    // the same. Nothing else in the file may move: the same skeleton, the same bone names, the same
    // annotations, the same duration.
    //
    // Tolerance is not zero on purpose. Both compressed formats decode through floating point, and
    // the written file stores exactly what came out of that, so the two decodes agree exactly on
    // everything except the rotation, which is normalised on the way in the way Havok normalises it.
    // Blend weights and transition timing, over one file or the corpus.
    //
    // This is the part the weapon idle work asks for: how much of each animation a blender is
    // actually playing, and what a transition looks like part way through rather than only at its
    // ends. Neither can be read off a static graph.
    //
    // What it checks over the corpus is consistency, since there is no runtime to check against. A
    // plain blender's resolved shares must sum to one, or be all zero when every child is switched
    // off. A transition blend must start at nothing of the new state and reach all of it, and no
    // sooner than its own duration. Anything it cannot resolve, a parametric blender driven by a
    // variable or a child weight bound to one, is reported as driven and counted, not guessed.
    // What a clip's own length would buy the stepper, counted before any of it is built.
    //
    // The stepper leaves a state when an event moves it and never when the clip it is playing runs
    // out. The mechanism that would change that is not a state machine field at all: a clip carries a
    // trigger array, and a trigger at a point in the clip raises an event. A trigger marked
    // `relativeToEndOfClip` is the one that says "when this animation finishes", and its absolute time
    // cannot be worked out without the animation file, because the length lives there.
    //
    // So this counts three separate things, and they are worth telling apart before designing
    // anything: how many clips carry a trigger at all, how many of those triggers need a length that
    // is not in the behaviour, and how many of the events they raise are actually listened for by a
    // transition in the same file. The last is the one that says whether closing this gap moves any
    // answer the tool gives, rather than only adding a number to it.
    private static int ClipTime(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories)
                       // The same filter the corpus itself is built with, so the file count here is
                       // the corpus's 531 rather than a subset of it that happens to sit in a folder
                       // called Behaviors.
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

        // A chain is five files and resolving it reads three of them, so it is worked out once per
        // project root rather than once per behaviour.
        var rootOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lengths = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var reader = new HkxBinaryReader();

        foreach (string file in files)
        {
            PackfileObjects objects;
            BehaviourGraphModel? model;
            try
            {
                objects = new PackfileObjects(PackfileImage.Read(File.ReadAllBytes(file)));
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

            // Running the clock on this file, which is the only way to see the feature do anything.
            // Everything above counts what is written down; this checks that stepping it moves states
            // and that it never steps somewhere the file's own reachability analysis rules out, which
            // is the invariant the event driven half already holds itself to.
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
                // A clip whose length could not be worked out must offer no triggers at all. This is
                // the safety rule the whole feature rests on: a trigger measured back from an end
                // nobody knows would fire at an invented moment, and 44 of the corpus's clips name an
                // animation that is not on disk, so the rule is exercised rather than theoretical.
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

                    // A trigger written as relative to the end with nothing subtracted is the clip
                    // finishing, so it has to land on the clip's own length. This is the one shape of
                    // the end relative reading the corpus can check for itself, and it is worth
                    // checking because reading those triggers as absolute times leaves every one of
                    // them inside its clip and breaks no other rule here.
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

            // Ten seconds a tenth at a time, which is longer than all but a handful of the corpus's
            // clips and fine enough that a trigger cannot be stepped over.
            for (int step = 0; step < 100; step++)
                firedHere += run.Advance(0.1f).Count;

            // Stepping from the start configuration alone barely touches this: a clip that ends a
            // state can only do so while something is sitting in that state, and the states holding
            // most of these clips are several events deep. So the graph is driven the way the
            // conditions gate drives its variables, by sending each declared event and letting the
            // clock run after it. The number that measures the reading is this one; the number from
            // the start configuration measures only where the graph happens to begin.
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

        // The length itself, which is the environmental half: it is in the animation file, and that
        // file is found through the project root rather than beside the behaviour. Every clip goes
        // through this, not only the ones carrying a trigger, or the denominator is the wrong
        // population and the resolution rate reads better than it is.
        void ResolveLength(string file, string root, string animation)
        {
            if (animation.Length == 0) return;

            // A behaviour with no project file above it has no root to resolve against. That is not a
            // broken file: `Meshes\Actors\Shared` and `Meshes\GenericBehaviors` hold behaviours that
            // several characters run, and the animations they name belong to whichever character is
            // running them. Counted rather than skipped, because it is the population the phrase
            // "per character" in the ticket is about.
            if (string.IsNullOrEmpty(root)) { rootless++; return; }

            string path = ProjectChain.ResolvePath(root, animation);
            if (File.Exists(path))
            {
                resolved++;
                if (Length(reader, lengths, path) > 0) durationRead++;
                return;
            }

            // The path a clip names is not always a file. Dogmeat's character declares
            // `Animations\WalkForward_B.hkt`, nothing of that name is on disk, and two files of that
            // name sit in `Animations\Default\Neutral` and `Animations\Default\Sneak`. Those folders
            // are variants the game swaps between while it runs, so the clip names a base and the
            // running character decides which copy plays.
            //
            // That matters here only if the copies differ in length, so this counts the candidates and
            // whether their durations agree. Where they agree the length is knowable without knowing
            // the variant; where they do not, the honest answer is a range or a refusal rather than
            // whichever copy was found first.
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

    /// A playback mode's name, so a count reads as a mode rather than as a number.
    private static string ModeName(int mode) =>
        HavokClassTypes.Shipped.Enum("hkbClipGenerator", "PlaybackMode")
                       ?.FirstOrDefault(v => v.Value == mode).Key ?? $"mode {mode}";

    /// Every file under the project that could be the animation a clip names, when the name itself is
    /// not a file.
    ///
    /// Matched on the leaf name under the folder the clip points into, which is what the variant
    /// folders differ in: `Animations\WalkForward_B.hkt` against
    /// `Animations\Default\Neutral\WalkForward_B.hkx`. This is a measurement of how many copies exist
    /// and whether they agree, not a rule for picking one. Picking one would be a guess about which
    /// variant the character is in, and that is a runtime fact this tool does not have.
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

    /// An animation's length, read once per file. Zero means it did not decode.
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
            try { model = NativeGraphModel.From(new PackfileObjects(PackfileImage.Read(File.ReadAllBytes(file)))); }
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

                // A resolved mix has to add up. The only way out is every child switched off, which
                // is a real state and sums to zero rather than one.
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

            // Every transition's blend, walked from nothing to all of the new state.
            var run = GraphRun.Start(model);
            foreach (var route in StateRoutes.Of(model).Routes)
            {
                float d = TransitionSeconds(model, route);
                if (d <= 0) { instant++; continue; }
                timed++;
            }
        }

        // The blend curve itself, checked on a made up transition so it does not depend on a file: at
        // the start the new state holds nothing, at the end all of it, and halfway a fraction in
        // between, never past its ends.
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
        // Reads the effect the same way GraphRun does, off the transition row. Kept here as a small
        // reimplementation rather than exposed from GraphRun, because it is one field lookup and
        // widening the run's surface for a measurement is the wrong trade.
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

    /// The transition blend, on a two state machine built in memory so it needs no file.
    private static bool BlendRamps(out string why)
    {
        why = "";
        var model = BehaviourGraphModel.Parse(Tests.TwoStateBlendGraph());
        var run = GraphRun.Start(model);

        string startState = run.Where().First().StateId;
        run.Send("Go");

        // At the instant it fires, the new state holds nothing and the old one holds all of it.
        var atStart = run.Where();
        var incoming = atStart.FirstOrDefault(a => !a.Fading);
        var outgoing = atStart.FirstOrDefault(a => a.Fading);
        if (incoming == null || outgoing == null) { why = "a transition with a duration did not blend two states"; return false; }
        if (incoming.Weight > 0.01f) { why = $"the new state started at {incoming.Weight:F2} rather than nothing"; return false; }

        run.Advance(0.25f);   // half of the fixture's 0.5s duration
        var half = run.Where();
        float mid = half.First(a => !a.Fading).Weight;
        if (mid < 0.4f || mid > 0.6f) { why = $"halfway the new state was {mid:F2} rather than about half"; return false; }

        run.Advance(0.5f);    // past the end
        var done = run.Where();
        if (done.Count != 1) { why = "the blend did not finish after its duration"; return false; }
        if (done[0].Weight < 0.999f) { why = $"the settled state held {done[0].Weight:F2} rather than all of it"; return false; }

        return true;
    }

    private static int WeightsOne(string file)
    {
        BehaviourGraphModel? model;
        try { model = NativeGraphModel.From(new PackfileObjects(PackfileImage.Read(File.ReadAllBytes(file)))); }
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

    // Stepping the graph, over one file or over the corpus.
    //
    // There is no reference implementation to check this against: Havok never shipped the behaviour
    // product's source. So the check is vanilla itself, and it is a check of self consistency rather
    // than of correctness. Over the corpus it reports three things that would each be a fault if they
    // came out wrong, and prints them rather than asserting a number nobody has justified.
    //
    // The one thing it does assert is the comparison against the validator's own reachability rule.
    // That rule works one machine at a time; the run crosses machine boundaries and follows a reached
    // state into what it holds. So the run must reach a superset of what the validator reaches, on
    // every file. A file where the run reaches less is a fault in the run, and it exits non-zero.
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
            try { model = NativeGraphModel.From(new PackfileObjects(PackfileImage.Read(File.ReadAllBytes(file)))); }
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

            // The comparison against the validator's own reachability rule.
            //
            // The two do not answer the same question and the difference is the interesting part. The
            // validator works one machine at a time and assumes every machine in the file is running,
            // so it reaches states inside machines nothing ever enters. The run starts at the graph's
            // root and only counts a machine once something reaches it, and in exchange it crosses
            // machine boundaries, which the validator cannot.
            //
            // So the assertion is the part where they do answer the same question: inside a machine
            // the run actually entered, the run applies the validator's rule and more, so it must
            // reach at least as much. A file where it does not is a fault in the run.
            var validatorSays = ValidatorReaches(model);
            var entered = reach.Reachable
                .Select(id => model.Get(id))
                .Where(o => o != null)
                .Select(o => MachineOf(model, o!.Id))
                .Where(m => m.Length > 0)
                .ToHashSet(StringComparer.Ordinal);

            // Stepping has to agree with the analysis.
            //
            // Reachable() works out where the graph can get to without ever moving it. Send() actually
            // moves it. They are separate code and they can disagree, and the direction that matters
            // is a step landing somewhere the analysis says is impossible: that is either the analysis
            // being too narrow or the stepper going somewhere it should not, and both are faults.
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

    /// Every state a run actually lands in, by sending each declared event in turn until nothing new
    /// happens.
    ///
    /// Deliberately not a search over every ordering. The point is to move the graph for real and see
    /// where it ends up, not to enumerate; a fixed number of sweeps over the event list reaches a
    /// settled set on every file in the corpus and keeps the whole gate under two seconds.
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

    /// The machine a state info object belongs to, by asking which machine lists it.
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

    /// What GraphValidator's reachability rule reaches, as state object ids, one machine at a time.
    ///
    /// Reimplemented here rather than exported from the validator, because the validator reports
    /// what it cannot reach and this needs what it can. Kept deliberately identical in rule: start
    /// state, then any transition whose from state is reached, wildcards always live.
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

    /// One file, optionally with a list of events to send.
    private static int RunOne(string file, string[] events)
    {
        PackfileObjects objects;
        BehaviourGraphModel? model;
        try
        {
            objects = new PackfileObjects(PackfileImage.Read(File.ReadAllBytes(file)));
            model = NativeGraphModel.From(objects);
        }
        catch (Exception e) { Console.WriteLine($"could not read {file}: {e.Message}"); return 1; }
        if (model == null) { Console.WriteLine("nothing to run in this file"); return 1; }

        var run = GraphRun.Start(model);
        if (run.RootId.Length == 0) { Console.WriteLine("this file has no generator to start from"); return 1; }

        // The clip lengths, read out of the animation files the project around this behaviour points
        // at. Handed over before anything is stepped, so a clip reaching its own end can raise what it
        // carries from the first step rather than from the second.
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
            // A step is written as a number of seconds rather than as an event name, so one command
            // line can say "send this, then let a second pass, then send that".
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

    // Editing a frame and saving it, the whole way the window does.
    //
    // The window lets a person pick a frame, change a bone's position, and save. That is three things
    // in a row and the last of them, the save, re-encodes the whole clip. So the question this answers
    // is the one the feature lives or dies on: after all that, does the frame that was changed come
    // back changed, and does every frame that was not stay where it was.
    //
    // It edits a bone's translation because that is the plainest edit and the one most likely to catch
    // a fault: a channel a vanilla clip left undriven has to become a curve the moment a single frame
    // of it differs, which is exactly the case a naive encoder drops. The edit is a value no vanilla
    // frame holds, so a frame that comes back near it came back because of the edit and not by luck.
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
        const float keptLimit = 0.1f;      // the edited frame has to come back within this
        const float elsewhereLimit = 0.1f; // and no other frame may move by more than this

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

            // The frames as they were, to measure what the edit disturbed. A copy, because the edit
            // and the re-encode both run over the same lists.
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

            // Every other frame of that channel, against where it was. Editing one frame must not drag
            // its neighbours, which a curve fitted too loosely would.
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

    // The same trip as `spline`, but through a real file rather than a blob held in memory.
    //
    // `spline` proves the codec. This proves the file: the animation is written into the packfile as
    // a new object, the file is rebuilt, and it is read back with the ordinary reader that knows
    // nothing about any of this. Everything between the two is the part `spline` cannot see, which is
    // the object's header fields, its four arrays, the blob's own run, and every pointer in the file
    // that named the animation being aimed at the new one.
    //
    // A file that comes back with the right frames but the wrong duration still plays wrongly, so the
    // header is compared too rather than only the motion.
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

    // The spline codec, measured on every animation the game ships.
    //
    // Take a vanilla clip, decode it, encode those frames again, decode the result, and compare the
    // two sets of frames. That is the whole gate, and what it proves is bounded: it says the encoder
    // and the decoder agree about a format, and that the motion survives the trip within a stated
    // distance. It does not say the engine will accept the file, which is #19 and needs a Windows
    // machine.
    //
    // What it does rule out is the failure that matters most here, which is a blob that decodes to
    // something plausible rather than to what went in. A wrong stride or a missed pad does not throw;
    // it shifts a run and comes back as a different pose, and only comparing values catches that.
    //
    // The comparison is per bone rather than averaged. A mean over a hundred bones hides one bone
    // being wrong, and one bone being wrong is a broken animation.
    private static int Spline(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string target = Path.GetFullPath(argv[1]);
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.hkx", SearchOption.AllDirectories).OrderBy(f => f).ToArray()
            : new[] { target };

        int everyNth = argv.Length > 2 && int.TryParse(argv[2], out int n) && n > 0 ? n : 1;
        if (everyNth > 1) Console.WriteLine($"every {everyNth}th file");

        // The limits the gate holds the codec to. Position is in Havok units, where a human is about
        // 115 tall, and rotation is in radians. Both are far below anything that could be seen, and
        // they are stated here rather than buried in the encoder so a run that loosens them is a
        // visible change to the gate rather than a quiet change to a default.
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

            // The size comparison is the point of the whole issue, so it is measured rather than
            // claimed. The original is the blob the game shipped, not the file around it, because
            // everything else in the file is unchanged by this.
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

    /// The size of the blob a file already carries, for comparing against what the encoder produces.
    private static long OriginalBlobSize(string file)
    {
        try
        {
            var image = PackfileImage.Read(File.ReadAllBytes(file));
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

    // What the game's own compressor chose, counted across every animation it shipped.
    //
    // An encoder has a lot of free choices: how many frames go in a block, how finely to quantise,
    // what degree of curve to fit. Every one of them is a guess unless the shipped files are counted,
    // and the shipped files are the only statement available about what the engine is known to
    // accept, since nothing here can ask the engine directly.
    //
    // The masks are read straight off the front of each block, where they need no walk. The degree is
    // read only where it can be located without one, which is a block whose first track opens with a
    // position spline: that puts the degree byte immediately after the masks. That is a subset of
    // blocks rather than all of them, and it is reported as a count so it cannot be mistaken for all.
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
                bytes = File.ReadAllBytes(file);
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

                // Stated as a formula rather than a number: the interesting thing is whether it is
                // ever anything other than four bytes a track, not what it comes to on one file.
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

                    // Only where it needs no walk to find, which is a block opening on a position
                    // spline: the count and degree sit right after the last mask.
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

    // One number for what the whole corpus decodes to.
    //
    // The evaluator that turns control points into frames is shared by the reader and the encoder, so
    // a change meant for one silently reaches the other. Nothing else here would notice: an encoder
    // fitted with a changed curve and read back with the same changed curve agrees with itself
    // perfectly while every vanilla file quietly decodes to something new. This is the number that
    // does notice, and it is meant to be compared across a change rather than read on its own.
    private static string DecodeFingerprint(IEnumerable<string> files)
    {
        var reader = new HkxBinaryReader();
        ulong hash = 1469598103934665603UL;
        int decoded = 0;
        long values = 0;

        void Feed(float v)
        {
            // Rounded before hashing, because the last bit of a float moves with the order the
            // compiler happens to fold a sum in and the question here is whether the frames moved,
            // not whether the arithmetic is bit for bit the same.
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

    private static int Interleave(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        var files = Directory.Exists(argv[1])
            ? Directory.EnumerateFiles(argv[1], "*.hkx", SearchOption.AllDirectories).OrderBy(f => f).ToList()
            : new List<string> { Path.GetFullPath(argv[1]) };

        string work = Path.Combine(Path.GetTempPath(), "symrm-interleave");
        Directory.CreateDirectory(work);

        // hkxpack is a second implementation, so a file it reads is a file whose layout is not just
        // ours read back by ourselves. It is not required: without Java the round trip through our
        // own reader still says whether the frames survived.
        bool java = false;
        try { NeedHkxPack(); java = true; }
        catch (InvalidOperationException) { }

        Console.WriteLine(java ? "checking the written files with hkxpack as well"
                               : "no Java runtime, so checking with our own reader only");

        int done = 0, refused = 0, wrong = 0, skipped = 0;
        var reader = new HkxBinaryReader();

        foreach (string file in files)
        {
            HkxAnimationData before;
            try
            {
                if (!reader.TryReadAnimation(file, out before)) { skipped++; continue; }
                if (before.NumFrames <= 0 || before.NumTracks <= 0) { skipped++; continue; }
                if (Array.IndexOf(NativeAnimation.Compressed, before.AnimationClass) < 0) { skipped++; continue; }
            }
            catch (Exception) { skipped++; continue; }

            Console.WriteLine($"\nFILE {Path.GetFileName(file)}");
            Console.WriteLine($"  {before.AnimationClass}: {before.NumFrames} frame(s) of " +
                              $"{before.NumTracks} track(s), {before.Duration:F4}s, " +
                              $"{before.Annotations.Count} annotation(s)");

            NativeAnimation.Result written;
            try { written = NativeAnimation.Interleave(file, before); }
            catch (InvalidOperationException e) { Console.WriteLine($"  REFUSED  {e.Message}"); refused++; continue; }
            catch (Exception e) { Console.WriteLine($"  THREW  {e.Message}"); wrong++; continue; }

            string outPath = Path.Combine(work, Path.GetFileNameWithoutExtension(file) + "-plain.hkx");
            File.WriteAllBytes(outPath, written.Bytes);

            HkxAnimationData after;
            try { after = reader.ReadAnimation(outPath); }
            catch (Exception e) { Console.WriteLine($"  UNREADABLE  {e.Message}"); wrong++; continue; }

            if (after.AnimationClass != NativeAnimation.InterleavedClass)
            {
                Console.WriteLine($"  WRONG CLASS  came back as {after.AnimationClass}");
                wrong++;
                continue;
            }

            // Every frame of every track, both ways.
            float worstT = 0, worstR = 0, worstS = 0;
            int compared = 0;
            string mismatch = "", worstWhere = "";

            if (after.NumFrames != before.NumFrames || after.NumTracks != before.NumTracks)
                mismatch = $"came back as {after.NumFrames} frame(s) of {after.NumTracks} track(s)";

            if (mismatch.Length == 0)
                for (int t = 0; t < before.NumTracks; t++)
                {
                    var a = before.Tracks[t];
                    var b = after.Tracks[t];
                    for (int f = 0; f < before.NumFrames; f++)
                    {
                        worstT = Math.Max(worstT, (a.Translations[f] - b.Translations[f]).Length());
                        worstS = Math.Max(worstS, (a.Scales[f] - b.Scales[f]).Length());

                        float turn = Angle(a.Rotations[f], b.Rotations[f]);
                        if (turn > worstR)
                        {
                            worstR = turn;
                            worstWhere = $"track {t} frame {f}: {a.Rotations[f]} became {b.Rotations[f]}";
                        }
                        compared++;
                    }
                }

            string second = "not asked";
            if (java)
            {
                try
                {
                    string checkDir = Path.Combine(work, "check");
                    HkxTextEdit.ResetDirectory(checkDir);
                    string xml = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, outPath, checkDir));

                    var held = System.Text.RegularExpressions.Regex.Match(
                        xml, "class=\"" + NativeAnimation.InterleavedClass + "\"");
                    var count = System.Text.RegularExpressions.Regex.Match(
                        xml, "name=\"transforms\" numelements=\"(\\d+)\"");

                    second = !held.Success ? "does not hold one"
                        : !count.Success ? "holds one with no transforms"
                        : count.Groups[1].Value == (before.NumFrames * before.NumTracks).ToString()
                            ? $"reads {count.Groups[1].Value} transform(s)"
                            : $"reads {count.Groups[1].Value} transform(s), not " +
                              $"{before.NumFrames * before.NumTracks}";
                }
                catch (Exception e) { second = "could not read it: " + e.Message.Split('\n')[0]; }
            }

            bool secondAgrees = !java ||
                second == $"reads {before.NumFrames * before.NumTracks} transform(s)";

            bool sameNames = before.BoneNames.SequenceEqual(after.BoneNames);
            bool sameAnnotations = before.Annotations.Count == after.Annotations.Count &&
                before.Annotations.Zip(after.Annotations).All(p => p.First.Text == p.Second.Text &&
                                                                   Math.Abs(p.First.Time - p.Second.Time) < 1e-6f);
            bool sameDuration = Math.Abs(before.Duration - after.Duration) < 1e-6f;

            // A hundredth of a degree, and a thousandth of a unit on a body measured in tens of
            // units. Anything real is orders of magnitude above this; float rounding is orders
            // below.
            const float Place = 0.001f, Degree = 0.01f;
            bool right = mismatch.Length == 0 && worstT < Place && worstS < Place && worstR < Degree &&
                         sameNames && sameAnnotations && sameDuration && secondAgrees;

            Console.WriteLine(mismatch.Length > 0
                ? $"  {mismatch}"
                : $"  read back: {compared} frame(s) compared, worst translation {worstT:E2}, " +
                  $"scale {worstS:E2}, rotation {worstR:F5} degrees");
            Console.WriteLine($"  bone names {(sameNames ? "identical" : "DIFFERENT")}, " +
                              $"annotations {(sameAnnotations ? "identical" : "DIFFERENT")}, " +
                              $"duration {(sameDuration ? "identical" : "DIFFERENT")}");
            if (java) Console.WriteLine($"  hkxpack {second}");
            Console.WriteLine($"  file grew by {written.Grew} bytes");
            if (!right && worstWhere.Length > 0) Console.WriteLine("  worst rotation at " + worstWhere);

            // Writing a clip back unchanged is the machinery, not the point. The point is changing
            // one, so one frame of one track is moved by a known amount and the file is asked
            // whether that is what happened: the nudged frame moved by exactly that, and nothing
            // else moved at all.
            if (right) right = Nudged(reader, file, before, work, ref mismatch);

            Console.WriteLine("  " + (right ? "GOOD" : "WRONG"));

            if (right) done++; else wrong++;
        }

        Console.WriteLine($"\nINTERLEAVE written={done} refused={refused} wrong={wrong} skipped={skipped}");
        return wrong == 0 ? 0 : 1;
    }

    /// Moves one frame of one track and checks that the file says so.
    ///
    /// A clip that survives being written out unchanged proves the format was written correctly and
    /// nothing about editing. This changes one number by an amount no animation would produce on its
    /// own, writes it, reads it back, and requires two things: the frame that was moved moved by
    /// exactly that, and every other frame of every other track did not move at all.
    private static bool Nudged(HkxBinaryReader reader, string file, HkxAnimationData before,
                               string work, ref string why)
    {
        var by = new System.Numerics.Vector3(1.5f, -2.25f, 0.75f);
        int track = before.NumTracks / 2, frame = before.NumFrames / 2;

        // Decoded again rather than reused, so the comparison below is against a reading that this
        // edit has not touched.
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

    /// How far apart two rotations are, in degrees, which is the only comparison that means anything
    /// for a quaternion. Two things make comparing the components directly misleading: the same
    /// rotation can be written two ways, negated throughout, and a quaternion of any length stands
    /// for the same rotation as the unit one along it. The compressed decoders return rotations a
    /// little off unit length, so without normalising both first this reports a fraction of a degree
    /// of disagreement between two rotations that are the same rotation.
    ///
    /// Not `acos` of the dot product, which is the formula everyone writes and is useless here.
    /// Near zero disagreement the dot product is near one, where `acos` has an infinite slope, so a
    /// rounding error of one part in ten million comes out as four hundredths of a degree. That is
    /// large enough to fail a threshold, and it reads as the data being wrong when the arithmetic
    /// is. Measured the other way round instead, from how far apart the two lie, where the same
    /// rounding error stays a rounding error.
    private static float Angle(System.Numerics.Quaternion a, System.Numerics.Quaternion b)
    {
        a = System.Numerics.Quaternion.Normalize(a);
        b = System.Numerics.Quaternion.Normalize(b);

        // A rotation and its negation are the same rotation, so the nearer of the two is the one
        // that means anything.
        double near = Math.Min(Distance(a, b, 1), Distance(a, b, -1));
        return (float)(2 * Math.Asin(Math.Clamp(near / 2, 0, 1)) * 180 / Math.PI);
    }

    private static double Distance(System.Numerics.Quaternion a, System.Numerics.Quaternion b, int sign)
    {
        double x = a.X - sign * b.X, y = a.Y - sign * b.Y, z = a.Z - sign * b.Z, w = a.W - sign * b.W;
        return Math.Sqrt(x * x + y * y + z * z + w * w);
    }

    // The two lanes of a transform that nothing reads, counted in the game's own files.
    //
    // A Havok transform is 48 bytes: a translation, a rotation and a scale, each four floats wide.
    // Only three of the four are the value. Writing a transform means writing the fourth as well,
    // and it is the one nobody can look up: the decoders never produce it, the class table only says
    // the field is a transform, and reasoning from Havok's identity constructor gives an answer about
    // Havok rather than about Bethesda's data. So it is counted here out of every reference pose in
    // every skeleton it is given, which is real vanilla transform data sitting in the same files.
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

    // What the top bits of an array's capacity word hold, across the corpus.
    //
    // Growing an array means writing a capacity for it, and the existing writer keeps whatever flags
    // were there and rewrites only the length. That is right for an array that already holds
    // something and says nothing about one that starts empty, whose flags may be nothing like the
    // flags a full array carries. Since the flag is what tells the game whether it owns the memory,
    // getting it wrong is not cosmetic, so it is counted rather than reasoned about.
    private static int Capacity(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        var files = Directory.Exists(argv[1])
            ? Directory.EnumerateFiles(argv[1], "*.hkx", SearchOption.AllDirectories).OrderBy(f => f).ToList()
            : new List<string> { Path.GetFullPath(argv[1]) };

        // Keyed by whether the array holds anything and what its top two bits are, then by the same
        // split again for arrays of structs on their own, since those are the ones being grown.
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

                    // The length half of the word against the count beside it. If they part company
                    // in vanilla data then rewriting the length from the count is not safe either.
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

    // Whether the data section can be laid out from scratch rather than edited in place.
    //
    // Rebuilding a file today keeps the data section's bytes exactly as they were read and
    // recomputes only the offsets around them, which is why `symrm packfile` matches on all 531
    // files while removing an object is still refused. Removing one means every later object moves,
    // and moving them means knowing where the writer would have put everything.
    //
    // Two claims decide whether that is knowable, and both are measured here rather than argued
    // about:
    //
    //   Everything a file points at is allocated in walk order, so the walk that already reproduces
    //   both fixup tables also says what order the bytes were written in.
    //   An object's runs sit between that object and the next one, rather than after all of them.
    //
    // If both hold, laying a file out from scratch is arithmetic over what is already read. If
    // either does not, the ordering rule is not the one assumed and a writer built on it would
    // produce a file the game reads wrongly rather than one it refuses.
    //
    // The first thing measured here was neither of those. It was that objects are packed back to
    // back, and they are not: 19 of Dogmeat's 906 land where packing predicts, because what an
    // object points at is written straight after it rather than after the last object.
    //
    // Both claims hold on all 531 vanilla behaviours, so the last thing needed is where each item
    // starts, and that is the rule this ended up measuring:
    //
    //   An object is written at the size the game registers for its class, not at the end of its
    //   last member. BSRootTwistModifier is 144 bytes registered and 112 to the end of its members,
    //   and the shorter reading puts the next string 32 bytes early.
    //   Objects and array runs start on a sixteen byte boundary. No exceptions in 36,340 objects
    //   and 17,000 runs.
    //   A string that is an element of an array of strings is packed against the one before it on a
    //   two byte boundary.
    //   A string that is a field of its own starts on a sixteen byte boundary, unless it is the
    //   first thing written after an array run, in which case it starts at that run's last byte.
    //
    // That last clause is not a guess dressed up. Three rules were tried before it and each was
    // measured: aligning strings to two everywhere gets 2,019 of Dogmeat's 2,493 items right,
    // sixteen everywhere gets 2,064, and treating every record as a fresh block gets 2,296.
    // The rule above gets all 138,420 items in all 531 files.
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

        // Where each kind of thing starts within a sixteen byte boundary. A kind that only ever
        // starts at nought is written on a sixteen byte boundary; one that starts at nought or eight
        // is on an eight byte one; one that lands anywhere has no alignment at all.
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

            // Objects in the order the virtual table lists them, which is the order they sit in.
            // The stretch after one object's body and before the next object starts is where that
            // object's runs have to be if they are written straight after it.
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

            // Everything pointed at, in the order the walk reaches it. A destination seen before is
            // counted apart: the writer is free to point two fields at one run, and that is a reuse
            // rather than a step backwards.
            var order = FixupOrder.Sources(objects, types, data, global: false);
            var aims = new Dictionary<int, int>();
            foreach (var (source, destination) in data.Locals()) aims[source] = destination;

            int highest = -1;
            var seen = new HashSet<int>();
            foreach (int source in order)
            {
                if (!aims.TryGetValue(source, out int destination)) continue;
                runsSeen++;

                // Whose run it is: the object the pointer sits in, or, for a pointer sitting inside
                // a run of its own, the object that run belongs to. Both are the last object to
                // start before it.
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

            // What the runs occupy, to say whether the gaps between objects are them and not padding.
            runBytes += seen.Count;

            // The same walk again, this time carrying how long each thing is, so the space the
            // writer left before it can be read off rather than reasoned about.
            // Where each thing starts, modulo sixteen. Read off the file rather than from a running
            // cursor: a cursor carries any mistake about one thing's length into every thing after
            // it, and the first attempt at this said strings were padded to eight for that reason.
            //
            // Then the thing this is all for: every offset predicted from nothing but the walk, the
            // lengths and the alignment those columns imply. A file whose every offset comes out
            // right is a file that could have been written rather than edited.
            var items = PackfileLayout.Of(image, types);
            if (items == null) { skipped++; continue; }

            // Anything the walk did not reach. This used to pass files it had only half read,
            // because a stretch nothing accounts for looks the same as padding when all you check
            // is whether the items you did find are where you predicted.
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

    // What the panel can say about a field when somebody hovers over its name.
    //
    // Two numbers, and the gap between them is the point. Every field can be described, because the
    // class table knows what shape it is. Almost none can be explained, because there is nowhere
    // honest to get a sentence from: the Havok manual issue #36 names is not on this machine, and
    // writing plausible sentences from field names would produce something that reads exactly like
    // the handful that were actually established.
    //
    // So this reports the coverage rather than hiding it, and the explained count is meant to go up
    // one finding at a time.
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

                // The panel's own field list rather than the class's members, so the fields inside
                // an array element are counted too. Those are most of what a state machine shows,
                // and counting only an object's own members put the transitions outside the measure
                // entirely.
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

    // The project around a file: which character, which skeleton, which animations it declares.
    //
    // This reads other files, and until now it read them through hkxpack, so the Chain tab and the
    // Check project button were the last two things in the window that asked for Java after opening
    // a file stopped needing it. Run this with Java hidden and with Java present and the output has
    // to be the same, which is the whole point.
    private static int Chain(string[] argv)
    {
        if (argv.Length < 2) { Usage(); return 1; }

        string file = Path.GetFullPath(argv[1]);
        string? java = HkxTextEdit.FindJava("");
        string? jar = HkxTextEdit.FindHkxPack("", AppContext.BaseDirectory);

        Console.WriteLine($"java {(java == null ? "hidden" : "present")}, " +
                          $"hkxpack {(jar == null ? "missing" : "present")}");

        var chain = ProjectChain.Resolve(file, java, jar);

        foreach (var link in chain.Links)
            Console.WriteLine($"  {link.Role,-12} {(link.Exists ? "found  " : "MISSING")} {link.Declared}");

        Console.WriteLine($"  animations   {chain.Animations.Count} declared by the character");
        Console.WriteLine($"  bones        {chain.Bones.Count} in the skeleton");

        foreach (string problem in chain.Problems) Console.WriteLine("  problem: " + problem);

        // The other half of what the Check project button does, and the other thing that used to
        // demand Java. Every behaviour in the project read and run through the validator.
        var checkResult = ProjectCheck.Run(chain, java, jar);
        int unread = checkResult.Files.Count(f => f.Error.Length > 0);

        foreach (var unreadable in checkResult.Files.Where(f => f.Error.Length > 0).Take(5))
            Console.WriteLine($"  unread: {unreadable.Name}, {unreadable.Error}");

        Console.WriteLine($"\n{chain.Links.Count} link(s), {chain.Problems.Count} problem(s)");
        Console.WriteLine($"checked {checkResult.Files.Count} behaviour file(s), {unread} unread, " +
                          $"{checkResult.Errors} error(s), {checkResult.Warnings} warning(s)");
        return chain.Links.Count == 0 || unread > 0 ? 1 : 0;
    }

    // Lengthening an array of plain numbers, the last kind of array that needed a rebuild.
    //
    // Simpler than the array of names in the same position: nothing inside it points anywhere, so it
    // is one run and one fixup rather than a run of pointers with a fixup each.
    //
    // The array is given one more element than it had, and the result has to read back at the new
    // length with the old values still in front of the new one.
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

            // An array written as inline whole numbers, which is the shape a run of them takes. Ids
            // are excluded by requiring no hash anywhere in the body, since an array of pointers is
            // written the same way otherwise.
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

    // Writing a vector or a transform, which are fixed width and were refused anyway.
    //
    // These move nothing: a vector is sixteen bytes wherever it sits and writing one over another
    // leaves the file exactly as long as it was. They were refused because nothing parsed the
    // spelling back, so any file with one of these edited went out through hkxpack.
    //
    // The edit is made through the document, the way the window makes one, and the result is read
    // back from the bytes and set against what was asked for. The file must also be the same length
    // afterwards, since a wide field that grew would mean it was not written where it sits.
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

            // A vector written as four numbers in brackets, anywhere in the document. Replaced with
            // one that cannot be there already, so finding it afterwards proves the write and not a
            // coincidence.
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

    // Declaring an event the way the window does it, all the way through to the bytes.
    //
    // This was the last refusal standing between a person and authoring without Java. Adding an
    // event lengthens an array of strings, a run cannot grow where it sits, and every save that hit
    // it went out through hkxpack instead. `symrm savecheck`'s resize guard asserted that refusal on
    // purpose, which is how it was known to still be there.
    //
    // The result is asked the questions that would catch a bad write: does it read back, is the name
    // in it, is it the last one, is the array one longer, and does every pointer still land inside
    // something written.
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

                // The names as the file itself holds them, read from the bytes. Both sides of a
                // comparison against the document are line ending normalised by the XML parser, so
                // a name losing its carriage return would agree with itself and prove nothing. Two
                // vanilla events carry one.
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

    /// Whether a file that just had an event declared really carries it, read back from its bytes.
    ///
    /// Set against the edited document element for element rather than by counting. Counting with a
    /// regex was the first attempt and it was wrong: an empty name is written as a self closing tag,
    /// so `<hkcstring>` misses it and thirteen files reported an array two longer than expected when
    /// the array was right and the count was not.
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

        // Bytes against bytes. Everything above went through a parser that normalises line endings,
        // so this is the only check here that would notice a name coming back subtly different.
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

    // The class table set against the game's own account of itself.
    //
    // Every offset this tool writes comes from HavokClassTypes.json, which was built from hkxpack's
    // class data. That is one source, and the whole write path rests on it. Fallout 4's startup
    // initializers carry the same information, read straight out of the binary rather than out of
    // anybody's tool, so the two can be set against each other and neither has to be trusted.
    //
    // What a disagreement means depends which way it goes. A different offset for a field is a bug
    // in one of them and would put a value in somebody else's field. A class one has and the other
    // does not is usually coverage rather than error, since the dump has every class the game
    // registers and this tool only needs the ones that appear in files.
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

                // The dump names a parent the way the binary does, by its class object:
                // `hkbGeneratorClass` for `hkbGenerator`. A dash means there is no parent.
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

        // The dump lists what a class declares itself and names its parent. This table flattens the
        // chain, so the comparison has to flatten the dump the same way or every inherited member
        // reads as one the game does not have. That was the first answer this gave and it was an
        // artefact: 3,185 members supposedly undeclared, every one of them inherited.
        // Parent first, then each class's own, which is the order this table flattens a chain in and
        // the order the file is written in. Building it the other way round matched an inherited
        // member against a class's own member of the same name: hkbRadialSelectorGenerator declares
        // a `pad` and so does hkbNode above it, and comparing those two reads as a wrong offset.
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

            // Walked in step rather than looked up by name, so the order is checked as well as the
            // offsets. A member this table does not carry is skipped over rather than desyncing the
            // rest, which matters because there are a few the game declares and no file writes.
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

    // Deleting a node the way the window does it, all the way through to the bytes.
    //
    // `delete` proves the library call. This proves the path a person actually takes: the editable
    // text is written from the file's own bytes, GraphAuthor takes the node out of it and detaches
    // everything pointing at it, NativeSave works out what changed, and NativeSave.Apply writes it.
    // No Java anywhere in that, which is the other thing being checked.
    //
    // The node chosen is the last one the author will agree to delete, so the same file gives the
    // same answer every run.
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

            // Written only when asked, so the sweep stays a read only measurement. One file and a
            // path is the case where somebody wants the result to put in front of another reader.
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

    /// Whether a saved file is sound: it reads, it holds the objects expected, every byte in it is
    /// accounted for, and no pointer aims anywhere that is not written.
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

    // The gate on deleting an object for real, rather than leaving it in the file unreferenced.
    //
    // Deleting takes the object out of the virtual fixup table, which is the object list, so every
    // object after it renumbers and every byte after it moves. This does one to each file and then
    // asks the result the questions that would catch a bad write: does it read back, does it hold
    // exactly one object fewer, is that object's class the one that went, is the section still fully
    // accounted for, and does every pointer in it still land inside something.
    //
    // The object chosen is the last one in the file, orphaned first so nothing points at it. Last
    // because it is the case where the fewest things move, which makes a failure easier to read, and
    // orphaned first because deleting something still pointed at is refused on purpose.
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

    /// What is wrong with a file an object was just deleted from, or nothing.
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

        // Every pointer has to land inside something. A fixup left aiming into the hole is the
        // failure this whole check exists for, and it does not announce itself: the file reads, it
        // just crashes the game.
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

    /// What the expression language a file can carry actually says, counted rather than guessed at.
    ///
    /// The stepper treats a transition carrying a condition as able to fire, because nothing here
    /// evaluates one. Whether that is a small gap or a large one depends entirely on what the
    /// expressions are, and that is a question about the shipped data rather than about the format.
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
        int driven = 0, flipped = 0, reachedFromState = 0, sweepEnters = 0, falseNow = 0;
        var problems = new List<string>();
        var assignments = new List<string>();
        var undeclared = new List<string>();
        var stuck = new List<string>();

        foreach (string file in files)
        {
            PackfileObjects objects;
            try { objects = new PackfileObjects(PackfileImage.Read(file)); }
            catch (Exception) { unread++; continue; }

            int here = 0, there = 0;

            var declared = VariableTable(objects);

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

                // Every name a condition uses has to be a variable the file declares, or the answer
                // can never be anything but Unknown and the condition is dead text.
                foreach (string name in parsed.Names)
                    if (!declared.ContainsKey(name))
                        undeclared.Add($"{Path.GetFileName(file)}: \"{text}\" names {name}, which this file does not declare");

                var verdict = Expression.Evaluate(parsed, n => declared.TryGetValue(n, out double v) ? v : null);
                if (verdict == Expression.Verdict.Unknown) undecided++;
                else if (verdict == Expression.Verdict.True) trueAtStart++;
                else falseAtStart++;
            }

            // The same language, reached from the other direction. An expression modifier assigns
            // the result of one of these to a variable or sends an event, so anything that evaluates
            // a condition can evaluate these too, and they are the larger population.
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

        // Where the conditional transitions actually sit, which is the difference between a reading
        // that changes what the stepper does and one that changes nothing. A condition on a
        // transition out of a state nothing enters can never hold anything back.
        foreach (string file in files)
        {
            BehaviourGraphModel? model;
            try { model = NativeGraphModel.From(new PackfileObjects(PackfileImage.Read(File.ReadAllBytes(file)))); }
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

            // The causal half, and the one that means something.
            //
            // A count of how many conditions came out false at the values the file happens to ship
            // with proves nothing about the reading being live: an evaluator that returned False for
            // everything would score well on it. So every condition is driven instead. Its variables
            // are set to each of a spread of values through the run's own setter, and the condition
            // has to come out true for some of them and false for others. One that never changes its
            // mind whatever its variables hold is being read, if at all, by something that is not
            // looking at the variables.
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
        foreach (var (text, count) in expressions.OrderByDescending(c => c.Value).Take(15))
            Console.WriteLine($"  {count,5}  {text}");

        // A condition this cannot read is not a soft failure. It means the stepper is back to firing
        // that transition whatever the variables hold, silently, which is the state this work exists
        // to leave behind.
        // A condition that cannot be read, or one that never changes its mind whatever its variables
        // hold, both mean the same thing in the end: the stepper is back to firing that transition
        // regardless, silently, which is the state this work exists to leave behind.
        return unparsed == 0 && stuck.Count == 0 ? 0 : 1;
    }

    /// Values to drive a condition's variables through. Chosen to straddle every constant the vanilla
    /// conditions compare against, which `symrm conditions` prints: 0, 1, 2, 3, 5, 9, 10, 18, 20.
    private static readonly double[] Spread = { -1, 0, 1, 2, 3, 5, 9, 10, 18, 20, 21, 100 };

    /// What a file's variables are called and what they start at, as numbers.
    ///
    /// A word value is thirty two bits whose meaning is the variable's declared type, so a real is
    /// stored as the bit pattern of its float and reading it as a whole number gives something like
    /// 1065353216 for 1.0. The type comes from `variableInfos`, positionally, which is the only key
    /// the format has.
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

            // VARIABLE_TYPE_REAL is 2 in the enum the game registers, and it is the only one whose
            // word is not already the number.
            table[name] = type == 2 ? BitConverter.Int32BitsToSingle(word) : word;
        }

        return table;
    }

    /// The gate on copying a subtree and pasting it.
    ///
    /// The fault this exists to catch does not announce itself. A paste that leaves one pointer
    /// naming an object of the original draws the same tree, passes the checker, reads back with the
    /// right number of objects of the right classes, and plays the original's child. So the headline
    /// check is not "did it come back" but "does anything inside the copy still name an original",
    /// asked of every pointer in the pasted stretch of the section rather than of the fields anybody
    /// thought to look at.
    ///
    /// Two passes. Within a file, over every behaviour that has a subtree worth copying. Between
    /// files, the same subtree into the next file along, which is where symbols and shared objects
    /// decide whether it is taken or refused.
    // What a lifted template could actually be, counted before any of it is built.
    //
    // A template is a subtree lifted out of a real behaviour and kept, so the question that decides
    // whether the idea works at all is how many subtrees can survive leaving their file. Two things
    // stop one. It can share an object with the rest of the file it came from, which a paste into a
    // different file refuses outright because there is nothing there to point at. Or it can use an
    // event or variable by name that the file it lands in does not declare, which is refused with a
    // list of what to declare.
    //
    // The three shapes this issue names are counted separately, because "templates are mostly
    // unusable" and "templates are mostly usable but need two events declaring first" are different
    // answers and only the second is worth building.
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

        // Measuring what could be lifted says nothing about whether lifting works, so every Nth
        // liftable root is actually lifted, kept, and applied into a different file on disk.
        //
        // The target is a copy of the file the shape came from. That is not a same file paste: it is
        // a different file, taken down the cross file path with its shared object check and its
        // symbol remapping, and it is the only target guaranteed to declare the symbols the shape
        // uses. A target picked at random would mostly be refused for symbols it does not declare,
        // which is correct behaviour and would measure nothing about the copy itself.
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

                    // A subtree that shares has to be refused, and the corpus has thousands of them,
                    // so the refusal is checked against real data rather than only against the one
                    // built by hand. Without this the sweep would only ever exercise the shapes that
                    // were going to work anyway, and a build that had stopped refusing would sail
                    // through it.
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

            // A paste that turns out to be the wrong thing has to be undoable, or trying one is a one
            // way door on the file. Deleting exactly what the paste added has to give the file back.
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

            // The other half of the ticket's last line: a pasted root that is given somewhere to hang.
            // A state carries a number unique inside its machine, so the one case worth checking is a
            // state going into a machine that already has states.
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

        // The other half of the ticket. A subtree only goes into another file when everything it
        // needs is there, so the interesting number here is not how many were taken but that the ones
        // turned away were turned away for a reason that names what is missing.
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

        // The one thing the ownership rule is soft about, counted rather than asserted. Two objects in
        // a pointer cycle each wait for the other, so neither is ever taken into a copy and both come
        // out shared. That is the safe way round and it would still be worth knowing about.
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

    /// How many objects in a file sit on a cycle of pointers. Read off the same fixup table the
    /// ownership rule reads, by Tarjan's strongly connected components: anything in a component of
    /// more than one object, or pointing at itself, is on a cycle.
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

            // Iterative, because a behaviour is deep enough that recursion here would be a stack
            // overflow on a real file rather than on a pathological one.
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

    /// The numbers the states of a machine carry, which are unique inside that machine and nowhere
    /// else, so a pasted state cannot keep the one it was copied with.
    private static List<int> StateIds(PackfileObjects objects, PackfileObjects.Instance machine)
    {
        var ids = new List<int>();
        foreach (var state in objects.ReadRefArray(machine, "states") ?? new List<PackfileObjects.Instance?>())
            if (state != null && objects.ReadInt(state, "stateId") is int held) ids.Add(held);
        return ids;
    }

    /// A refusal grouped by what it was about, so a new reason shows up rather than being lost among
    /// one line per file.
    private static string Kind(string message) =>
        message.Contains("does not declare", StringComparison.Ordinal)
            ? "an event or variable the other file does not declare"
        : message.Contains("shares", StringComparison.Ordinal)
            ? "an object shared with the rest of the file it came from"
        : message.Contains("there is no name to copy it across by", StringComparison.Ordinal)
            ? "an index pointing past the end of the file's own symbol list"
        : message;

    /// The subtree worth copying out of a file: the largest owned by one of the shapes a person
    /// actually duplicates. Deterministic, so a run of this gate compares against the last one.
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

    /// What is wrong with a file a subtree was just pasted into, or nothing.
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

        // Nothing below the paste may have moved in the list, because a renumber there would aim
        // every id anybody already holds one object early.
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

        // The check this whole gate exists for. Every pointer stored anywhere inside the pasted
        // stretch has to land on one of the pasted objects, or on one of the objects the subtree
        // shares with the rest of the file. Landing on an object the copy was made from is the fault
        // that looks exactly like success.
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

    /// A subtree written out as text, so two of them can be compared without comparing offsets.
    ///
    /// Everything that is a value goes in as it stands, every string goes in as its text, every array
    /// as its length, and a pointer goes in as the position of whatever it names within this walk, so
    /// an original and its copy read the same while naming different objects.
    private static string Shape(PackfileImage image, PackfileObjects objects, int rootId)
    {
        var types = HavokClassTypes.Shipped;
        var data = image.Section("__data__")!;
        int section = image.Sections.IndexOf(data);

        // An event or a variable is stored as a number and the number is the one thing a paste into
        // another file deliberately changes, so comparing the bytes would report every correct
        // remap as a difference. The name is what has to survive, so the name is what goes in.
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

    // The gate on removing an object, and on anything else that moves one.
    //
    // `packfile` proves the arithmetic around the data section while keeping the section itself
    // exactly as it was read. This throws the section away and writes it again from nothing: every
    // object and every run placed by the walk, every entry in all three tables moved to match. A
    // vanilla file laid out this way has to come back as the file it already was, because no offset
    // in it was carried over from the file it came from.
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
                original = File.ReadAllBytes(file);
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

    /// Draws the posed mesh to a PNG, so how it looks is something that can be answered here rather
    /// than only by a person with the window open.
    ///
    /// Two views, front and side, side by side. Bones can be picked out by name and are drawn in
    /// their own colour, which is the whole point: a mesh where one bone out of thirteen sits wrong
    /// is a question about that bone, and grey wireframe on grey wireframe does not answer it.
    private static int DrawMesh(string[] argv)
    {
        if (argv.Length < 4) { Usage(); return 1; }

        var nif = OpenCommonwealth.Services.Nif.NifFile.Read(Path.GetFullPath(argv[1]));
        var shapes = OpenCommonwealth.Services.Nif.NifGeometry.Shapes(nif);
        var skeleton = new HkxBinaryReader().ReadSkeleton(Path.GetFullPath(argv[2]));
        string outPath = Path.GetFullPath(argv[3]);

        // Bones named on the command line get a colour each, in this order.
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

        // Front looks down the Y axis and side looks down the X, which for this game's axes puts the
        // character upright in both.
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
            // Which bone owns each vertex, by the heaviest weight, so a vertex is drawn in the colour
            // of the bone that actually moves it rather than of whichever slot came first.
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

        // Where each named bone's vertices actually land, which is the number behind whatever the
        // picture looks like.
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
        float worstShare = 0;
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

        // A bone the skeleton does not have is reported and not failed: a shared mesh naming a bone a
        // particular rig lacks is ordinary.
        Console.WriteLine(unmatched == 0
            ? "\nevery mesh bone found a skeleton bone"
            : $"\n{unmatched} mesh bone reference(s) had no skeleton bone of that name");

        // What fails is a fault in how the transforms are composed, and that is not the same thing as
        // the worst single number. Reading the stored rotation the wrong way round is wrong for every
        // bone at once, because each bone is turned differently, so it shows up as most of them
        // disagreeing. One bone out of thirteen is authoring: the vanilla male body's LLeg_Toe1 sits
        // 5.140 units away from where the other twelve agree the mesh is, and its own right hand
        // twin is 0.172, so the mesh disagrees with the skeleton about that toe rather than the
        // reader disagreeing with itself.
        bool ok = worstShare <= 0.25f;
        Console.WriteLine(ok
            ? $"PASS  at most {worstShare:P0} of a shape's matched bones disagree by more than " +
              $"{DriftLimit:F1}, worst disagreement {worstDrift:F3}"
            : $"FAIL  {worstShare:P0} of a shape's matched bones disagree by more than " +
              $"{DriftLimit:F1}, worst disagreement {worstDrift:F3}, so the bind is not composing");
        return ok ? 0 : 1;
    }

    /// Which bones a drifting mesh is drifting on.
    ///
    /// A mesh is authored on the skeleton's reference pose, so on that pose every bone's composed
    /// transform has to leave a point where it found it. Printing the per bone error turns "this mesh
    /// is 120 units out" into "these bones are and those are not", which is the question worth asking
    /// next. It only prints when something is already wrong, because on a mesh that passes it is 95
    /// lines of zeroes.
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

        // Where each one puts the origin, because a bone that only translates and a bone that also
        // turns are different faults and the number on its own does not tell them apart.
        foreach (var r in rows.OrderByDescending(r => r.Error).Take(12))
            Console.WriteLine($"      {r.Name,-28} {r.Error,9:F3} over {r.Vertices,5} vertices, " +
                              $"origin to {r.Off.X,8:F2} {r.Off.Y,8:F2} {r.Off.Z,8:F2}" +
                              (r.Error <= DriftLimit ? "" : "   <-"));

        return rows.Count == 0 ? 0 : (float)(rows.Count - clean) / rows.Count;
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

        // Bounding a variable, which is the one symbol edit that has to lengthen a positional array
        // to reach the variable it is about. Reported rather than assumed: whether it can be written
        // into the file's own bytes or has to go back through hkxpack is worth knowing, since one of
        // those needs Java and the other does not.
        {
            var names0 = SymbolEditor.VariableNames(BehaviourGraphModel.Parse(xml));
            if (names0.Count > 0)
            {
                int last = names0.Count - 1;
                int had = SymbolEditor.Audit(BehaviourGraphModel.Parse(xml)).Bounds;

                string bounded = SymbolEditor.SetVariableBounds(xml, last, "-1", "7");
                var lined = SymbolEditor.Audit(BehaviourGraphModel.Parse(bounded));

                Console.WriteLine($"\n--- bound variable {last} '{names0[last]}' from -1 to 7 ---");
                Console.WriteLine($"  bounds array {had} -> {lined.Bounds} for {lined.Names} variable(s), " +
                                  $"parallel={lined.BoundsAreParallel}");

                var plan = NativeSave.Compare(xml, bounded);
                Console.WriteLine(plan.Possible
                    ? $"  written into the bytes: {plan.Changes.Count} change(s), grows={plan.Grows}"
                    : $"  needs hkxpack: {plan.Refusal}");

                // Changing a bound the array already holds is a different question from adding one:
                // nothing is lengthened, so it is a value write like any other.
                if (had > 0)
                {
                    string edited = SymbolEditor.SetVariableBounds(xml, 0, "-2", "9");
                    var inPlace = NativeSave.Compare(xml, edited);
                    Console.WriteLine(inPlace.Possible
                        ? $"  changing a bound already there: written into the bytes, " +
                          $"{inPlace.Changes.Count} change(s)"
                        : $"  changing a bound already there: needs hkxpack, {inPlace.Refusal}");

                    // Carried out, not merely planned. The file is written, read back, and set
                    // against hkxpack's own reading of it, because a plan that says it can be
                    // written proves nothing about what lands in the bytes.
                    if (inPlace.Possible)
                    {
                        byte[] written = NativeSave.Apply(argv[1], inPlace);

                        string boundedPath = Path.Combine(work, "bounded.hkx");
                        File.WriteAllBytes(boundedPath, written);

                        string check = WorkDirectory("symrm-bounded-", boundedPath);
                        HkxTextEdit.ResetDirectory(check);
                        string reread = HkxTextEdit.ReadXml(HkxTextEdit.Unpack(_java, _jar, boundedPath, check));

                        // Read out of the text rather than through the model, because a bound sits in
                        // a nested object and the model records anything with contents of its own as
                        // an empty string.
                        Console.WriteLine($"  read back through hkxpack: bound 0 is " +
                                          $"{FirstBound(reread, "min")} to {FirstBound(reread, "max")}");

                        // Nothing else moved. Every value hkxpack reads out of the written file has
                        // to match what it read out of the edited text, or the write went somewhere
                        // it was not asked to go.
                        var wanted = RepackCheck.Take(edited);
                        var got = RepackCheck.Take(reread);

                        int differ = 0;
                        int compared = Math.Min(wanted.InOrder.Count, got.InOrder.Count);
                        for (int o = 0; o < compared; o++)
                            if (wanted.InOrder[o].Body != got.InOrder[o].Body) differ++;

                        Console.WriteLine($"  {wanted.Objects} object(s) asked for, {got.Objects} in " +
                                          $"the written file, {differ} whose values differ");
                        Console.WriteLine($"  file grew by {written.Length - new FileInfo(argv[1]).Length} bytes");
                    }
                }
            }
        }

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
            foreach (string target in GraphAuthor.PointsAt(model, current))
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
