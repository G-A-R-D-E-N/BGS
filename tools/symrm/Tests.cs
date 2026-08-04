using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.Tools;

// Regressions worth catching without a game install. Everything here works on a graph built in
// memory, so it needs no BA2, no hkxpack and no JVM, which means it can actually be run every time
// rather than only when someone remembers to extract a corpus.
public static class Tests
{
    private static int _failed;
    private static int _ran;

    public static int Run()
    {
        _failed = 0;
        _ran = 0;

        DetachedSubtreeStaysDrawn();
        ReplacingLinkSaysWhatItDisplaced();
        BlenderChildIsWrapped();
        AnyNodeCanBeDeleted();
        StructuralObjectsAreProtected();
        PortTypesRefuseNonsense();
        BundledHkxPackIsFound();
        Fo4CharacterListsItsAnimations();
        MissingClipAnimationIsReported();
        RepackDriftNamesWhatMoved();
        AnUnreachableStateIsReported();
        EventUsageSaysWhoSendsAndWhoListens();
        ScaleIsShownOnlyWhenItIsRealScale();
        AFractionLandsOnAFrame();

        Console.WriteLine($"\n{_ran} checks, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    // Nullable on purpose: half of what these assert is that something became null, or stayed
    // null. Printing that as a blank made a failing line unreadable, so null and empty are spelled
    // out rather than rendered as nothing.
    private static void Check(string what, object? expected, object? actual)
    {
        _ran++;
        bool ok = Equals(expected, actual);
        if (!ok) _failed++;
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {what,-58} expected {Text(expected)}, got {Text(actual)}");
    }

    private static string Text(object? value) => value switch
    {
        null => "null",
        string s when s.Length == 0 => "an empty string",
        // HkObject has no ToString, so a failure would otherwise name the type and tell you
        // nothing about which object was found.
        HkObject o => $"#{o.Id} {o.Class}" + (o.Str("name").Length > 0 ? $" '{o.Str("name")}'" : ""),
        _ => value.ToString() ?? "null",
    };

    private static void CheckTrue(string what, bool value)
    {
        _ran++;
        if (!value) _failed++;
        Console.WriteLine($"  {(value ? "ok  " : "FAIL")}  {what}");
    }

    // The one that bit us. Retargeting a link that already held something detaches whatever it held,
    // along with everything under it. Drawing only what the root reaches made all of that disappear
    // from the canvas and read as deletion. Layout has to walk each detached subtree from its own
    // head, so the drawn count must not follow the reachable count down.
    private static void DetachedSubtreeStaysDrawn()
    {
        Console.WriteLine("detached subtree stays drawn after a retarget");

        string xml = SmallGraph();
        var before = BehaviourGraphModel.Parse(xml);

        Check("objects in the file", 7, before.Objects.Count);
        Check("reachable from the root before", 6, Reachable(before));
        Check("drawn before", 7, GraphAuthor.Layout(before, 1000).Count);

        // #97 is the spare clip nothing points at. Dropping the graph's root generator on it is the
        // drag that caused the report.
        xml = GraphLinks.Connect(xml, "91", "rootGenerator", "97", out _);
        var after = BehaviourGraphModel.Parse(xml);

        Check("objects after, nothing was deleted", 7, after.Objects.Count);
        Check("reachable from the root after", 2, Reachable(after));
        Check("drawn after, the whole point", 7, GraphAuthor.Layout(after, 1000).Count);

        var drawn = GraphAuthor.Layout(after, 1000).Select(l => l.Node.Id).ToHashSet();
        CheckTrue("the displaced state machine #92 is still drawn", drawn.Contains("92"));
        CheckTrue("its child state #93 is still drawn", drawn.Contains("93"));
        CheckTrue("the clip under that state #94 is still drawn", drawn.Contains("94"));
    }

    private static void ReplacingLinkSaysWhatItDisplaced()
    {
        Console.WriteLine("\na replacing connection reports what it displaced");

        GraphLinks.Connect(SmallGraph(), "91", "rootGenerator", "97", out string note);
        CheckTrue("note names the displaced object", note.Contains("#92"));
        CheckTrue("note says it is detached", note.Contains("detached"));

        GraphLinks.Connect(SmallGraph(), "93", "transitions", "97", out string plain);
        CheckTrue("an empty link says nothing about replacing", !plain.Contains("replacing"));
    }

    // A blender holds weighted wrappers, not generators. Writing a bare reference into children
    // passes hkxpack and gives the engine something it cannot read, so the wrapper has to be built.
    private static void BlenderChildIsWrapped()
    {
        Console.WriteLine("\na generator dropped on a blender is wrapped");

        string xml = SmallGraph().Replace(
            "<hkobject class=\"hkbClipGenerator\" name=\"#97\"",
            "<hkobject class=\"hkbBlenderGenerator\" name=\"#98\" signature=\"0xce45c088\">\n" +
            "            <hkparam name=\"name\">Blend</hkparam>\n" +
            "            <hkparam name=\"children\" numelements=\"0\">\n</hkparam>\n" +
            "        </hkobject>\n" +
            "        <hkobject class=\"hkbClipGenerator\" name=\"#97\"");

        xml = GraphLinks.Connect(xml, "98", "children", "94", out string note);
        var model = BehaviourGraphModel.Parse(xml);
        var blender = model.Get("98")!;

        Check("the blender has one child", 1, blender.Refs("children").Count);
        string child = blender.Refs("children")[0];
        Check("the child is a wrapper, not the clip", "hkbBlenderGeneratorChild", model.Get(child)?.Class);
        Check("the wrapper points at the clip", "94", model.Get(child)?.Ref("generator"));
        CheckTrue("the note mentions the wrapper", note.Contains(child));
    }

    // A node that shipped with the game is referenced by something, always. Refusing to delete
    // while references exist therefore made vanilla nodes undeletable, which is not how a graph
    // editor behaves. Deleting breaks the links into it instead.
    private static void AnyNodeCanBeDeleted()
    {
        Console.WriteLine("\nany node can be deleted, links into it are broken first");

        string xml = SmallGraph();
        var before = BehaviourGraphModel.Parse(xml);
        CheckTrue("the clip starts out referenced", GeneratorEditor.ReferencesTo(before, "94").Count == 1);

        xml = GraphAuthor.DeleteNode(xml, "94", out string note);
        var after = BehaviourGraphModel.Parse(xml);

        Check("the object is gone", null, after.Get("94"));
        Check("nothing else was removed", 6, after.Objects.Count);
        Check("the state that held it survives", "A", after.Get("93")?.Str("name"));
        Check("its generator link was cleared, not left dangling", "null", after.Get("93")?.Str("generator"));
        CheckTrue("the note says what it cleared", note.Contains("#93"));
        CheckTrue("no dangling reference remains", GraphValidator.Check(xml)
            .All(f => !f.What.Contains("not in this file")));
    }

    private static void StructuralObjectsAreProtected()
    {
        Console.WriteLine("\nthe objects the file is built around cannot be deleted");

        foreach (string id in new[] { "91" })
        {
            string cls = BehaviourGraphModel.Parse(SmallGraph()).Get(id)!.Class;
            bool refused = false;
            try { GraphAuthor.DeleteNode(SmallGraph(), id, out _); }
            catch (InvalidOperationException) { refused = true; }
            CheckTrue($"deleting #{id} {cls} is refused", refused);
        }

        CheckTrue("a clip is not protected", GraphAuthor.CanDelete("hkbClipGenerator"));
        CheckTrue("a state machine is not protected", GraphAuthor.CanDelete("hkbStateMachine"));
    }

    // The canvas types its ports so GraphEdit refuses a drag that could not work. These are the
    // pairings behind that, checked here because the canvas itself cannot be scripted.
    private static void PortTypesRefuseNonsense()
    {
        Console.WriteLine("\nport types accept what fits and refuse what does not");

        bool Allowed(string field, string className)
        {
            int from = GraphLinks.Accepts(field), to = GraphLinks.FamilyOf(className);
            return from == to || GraphLinks.ValidPairs.Contains((from, to));
        }

        CheckTrue("a generator slot takes a clip", Allowed("generator", "hkbClipGenerator"));
        CheckTrue("a generator slot takes a state machine", Allowed("generator", "hkbStateMachine"));
        CheckTrue("a states array takes a state info", Allowed("states", "hkbStateMachineStateInfo"));
        CheckTrue("a states array takes a generator to wrap", Allowed("states", "hkbClipGenerator"));
        CheckTrue("a blender's children take a wrapper", Allowed("children", "hkbBlenderGeneratorChild"));
        CheckTrue("a modifier slot takes a modifier", Allowed("modifier", "hkbEventDrivenModifier"));

        CheckTrue("a generator slot refuses a modifier", !Allowed("generator", "hkbEventDrivenModifier"));
        CheckTrue("a generator slot refuses a transition array",
            !Allowed("generator", "hkbStateMachineTransitionInfoArray"));
        CheckTrue("a modifier slot refuses a clip", !Allowed("modifier", "hkbClipGenerator"));
        CheckTrue("a triggers slot refuses a clip", !Allowed("triggers", "hkbClipGenerator"));
        CheckTrue("a states array refuses a trigger array",
            !Allowed("states", "hkbClipTriggerArray"));
    }

    // A release ships the jar in tools/ beside the executable. There is no project directory in an
    // exported build and res:// cannot be globalized out of the binary, so if the search stops
    // looking relative to the executable the shipped tool silently becomes read only.
    private static void BundledHkxPackIsFound()
    {
        Console.WriteLine("\nthe bundled jar is found from the executable's own directory");

        string app = Directory.CreateTempSubdirectory("bgs-bundle").FullName;
        string project = Directory.CreateTempSubdirectory("bgs-project").FullName;
        string saved = HkxTextEdit.AppDirectory;
        try
        {
            HkxTextEdit.AppDirectory = app;
            Check("nothing is found before it is bundled", null, HkxTextEdit.FindHkxPack("", project));

            Directory.CreateDirectory(Path.Combine(app, "tools"));
            string jar = Path.Combine(app, "tools", "hkxpack-cli.jar");
            File.WriteAllText(jar, "not really a jar");
            Check("the bundled jar is found", jar, HkxTextEdit.FindHkxPack("", project));

            string chosen = Path.Combine(project, "elsewhere.jar");
            File.WriteAllText(chosen, "not really a jar either");
            Check("an explicitly configured jar still wins", chosen, HkxTextEdit.FindHkxPack(chosen, project));
            Check("a configured path that does not exist is ignored", jar,
                HkxTextEdit.FindHkxPack(Path.Combine(project, "gone.jar"), project));
        }
        finally
        {
            HkxTextEdit.AppDirectory = saved;
            Directory.Delete(app, true);
            Directory.Delete(project, true);
        }
    }

    private static int Reachable(BehaviourGraphModel model)
    {
        var root = model.Objects.First(o => o.Class == "hkbBehaviorGraph");
        var seen = new HashSet<string> { root.Id };
        var queue = new Queue<HkObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var slot in GraphLinks.OutSlots(model, current))
                foreach (string target in slot.Targets)
                {
                    if (!seen.Add(target)) continue;
                    var next = model.Get(target);
                    if (next != null) queue.Enqueue(next);
                }
        }
        return seen.Count;
    }

    // Seven objects: a graph, a machine with two states, a clip under each, and one spare clip that
    // nothing points at. Small enough to reason about, shaped like the real thing.
    // Fallout 4 does not use animationNames. Reading only that field left the chain's animation
    // list empty for every vanilla file, which is why nothing ever checked a clip against disk.
    private static void Fo4CharacterListsItsAnimations()
    {
        Console.WriteLine("\na Fallout 4 character's animation list is read");

        var model = BehaviourGraphModel.Parse(Fo4Character());
        var strings = model.Objects.First(o => o.Class == "hkbCharacterStringData");

        Check("the old Skyrim field is empty here", 0, strings.Strings("animationNames").Count);

        var declared = ProjectChain.DeclaredAnimations(strings);
        Check("both bundled animations are found", 2, declared.Count);
        Check("the first one", @"Animations\Anim01.HKT", declared.FirstOrDefault());

        Check("separator and extension do not matter to the key",
              ProjectChain.AnimationKey(@"Animations\Anim01.HKT"),
              ProjectChain.AnimationKey("animations/anim01.hkx"));
    }

    private static void MissingClipAnimationIsReported()
    {
        Console.WriteLine("\na clip pointing at an animation that is not on disk is an error");

        string root = Directory.CreateTempSubdirectory("bgs-anims").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "a.hkx"), "not really an animation");

            var chain = new ProjectChain { Root = root };
            chain.Animations.AddRange(new[] { "a.hkx", "b.hkx", "spare.hkx" });

            var missing = GraphValidator.Check(SmallGraph(), chain)
                                        .Where(f => f.What.Contains("not on disk")).ToList();

            Check("two of the three clips are missing their animation", 2, missing.Count);
            CheckTrue("reported as a warning, because vanilla trips it too",
                      missing.All(f => f.Level == GraphValidator.Level.Warning));
            CheckTrue("ClipB is named", missing.Any(f => f.Where.Contains("ClipB")));
            CheckTrue("the spare clip is named", missing.Any(f => f.Where.Contains("Spare")));
            CheckTrue("ClipA, which is on disk, is not", !missing.Any(f => f.Where.Contains("ClipA")));

            Check("nothing is reported without a chain to check against", 0,
                  GraphValidator.Check(SmallGraph()).Count(f => f.What.Contains("not on disk")));

            // Fallout 4 declares .HKT and ships .hkx, so the swap has to happen before the check.
            File.Move(Path.Combine(root, "a.hkx"), Path.Combine(root, "b.hkx"));
            var swapped = new ProjectChain { Root = root };
            Check("a .HKT declaration resolves to the .hkx on disk", 0,
                  GraphValidator.Check(SmallGraph().Replace("b.hkx", "b.HKT"), swapped)
                                .Count(f => f.Where.Contains("ClipB") && f.What.Contains("not on disk")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void RepackDriftNamesWhatMoved()
    {
        Console.WriteLine("\na repack that drops objects is caught and says what went");

        var before = RepackCheck.Take(SmallGraph());
        Check("the graph has seven objects", 7, before.Objects);

        CheckTrue("an unchanged file drifts by nothing",
                  RepackCheck.Compare(before, RepackCheck.Take(SmallGraph())).Clean);

        // Renumbering is what hkxpack always does, and is not drift.
        var renumbered = RepackCheck.Take(SmallGraph().Replace("\"#9", "\"#20"));
        CheckTrue("renumbering every object is not drift", RepackCheck.Compare(before, renumbered).Clean);

        string short1 = SmallGraph().Replace(
            "<hkobject class=\"hkbClipGenerator\" name=\"#97\"",
            "<hkobject class=\"hkbClipGeneratorGONE\" name=\"#97\"");
        var drift = RepackCheck.Compare(before, RepackCheck.Take(short1));

        CheckTrue("a swapped class is drift", !drift.Clean);
        CheckTrue("it says what was lost", drift.ToString().Contains("lost 1 hkbClipGenerator"));
        CheckTrue("it says what appeared", drift.ToString().Contains("invented 1 hkbClipGeneratorGONE"));

        int cut = SmallGraph().IndexOf("<hkobject class=\"hkbClipGenerator\" name=\"#97\"", StringComparison.Ordinal);
        var dropped = RepackCheck.Compare(before, RepackCheck.Take(SmallGraph()[..cut]));
        CheckTrue("a dropped object is drift", !dropped.Clean);
        CheckTrue("it counts both sides", dropped.ToString().Contains("7 objects and came back with 6"));
    }

    // The one the door edit slipped past. A state info is always referenced, because the machine
    // lists it, so asking whether anything points at it can never catch a state no transition can
    // enter. Driven starts from a machine that has a transition, because a machine with none is
    // engine driven and deliberately exempt.
    private static void AnUnreachableStateIsReported()
    {
        Console.WriteLine("\na state nothing can transition to is reported");

        Check("a machine with no transitions at all is left alone", 0, Unreachable(SmallGraph()).Count);

        string driven = StateEditor.AddTransition(SmallGraph(), "92", "93", 0, 0, "null");
        var dead = Unreachable(driven);
        Check("one state cannot be entered", 1, dead.Count);
        CheckTrue("it is B, the one nothing targets", dead.Any(f => f.Where.Contains("'B'")));
        CheckTrue("not A, which is the start state", !dead.Any(f => f.Where.Contains("'A'")));
        CheckTrue("a warning, because vanilla does this on purpose 123 times",
                  dead.All(f => f.Level == GraphValidator.Level.Warning));

        string wired = StateEditor.AddTransition(driven, "92", "93", 1, 0, "null");
        Check("a transition from A clears it", 0, Unreachable(wired).Count);

        string wild = StateEditor.AddTransition(driven, "92", "", 1, 0, "null");
        Check("a wildcard transition clears it too", 0, Unreachable(wild).Count);

        // Two hops, because reachability has to keep going rather than stopping at the start state's
        // own targets.
        string chained = StateEditor.AddTransition(
            StateEditor.AddState(driven, "92", "C", "#97", out _, out int third), "92", "95", third, 0, "null");
        var chainDead = Unreachable(chained);
        CheckTrue("a state reached only through B is still dead while B is",
                  chainDead.Any(f => f.Where.Contains("'C'")));
        Check("both B and C are named", 2, chainDead.Count);

        string reached = StateEditor.AddTransition(chained, "92", "93", 1, 0, "null");
        Check("wiring A to B makes C reachable too", 0, Unreachable(reached).Count);

        // A machine whose start state does not exist is already reported on its own, and treating
        // every state as unreachable on top of that would bury it.
        string noStart = driven.Replace("<hkparam name=\"startStateId\">0</hkparam>",
                                        "<hkparam name=\"startStateId\">9</hkparam>");
        Check("a broken startStateId is not turned into a flood", 0, Unreachable(noStart).Count);
    }

    private static List<GraphValidator.Finding> Unreachable(string xml) =>
        GraphValidator.Check(xml).Where(f => f.What.StartsWith("cannot be entered")).ToList();

    private static string Fo4Character() => """
        <?xml version="1.0" encoding="ascii"?>
        <hkpackfile classversion="11" contentsversion="hk_2014.1.0-r1">
            <hksection name="__data__">
                <hkobject class="hkbCharacterStringData" name="#93" signature="0xb9d8a52">
                    <hkparam name="skinNames" numelements="0"/>
                    <hkparam name="boneAttachmentNames" numelements="0"/>
                    <hkparam name="animationBundleNameData" numelements="1">
                        <hkobject>
                            <hkparam name="bundleName"/>
                            <hkparam name="assetNames" numelements="2">
                                <hkcstring>Animations\Anim01.HKT</hkcstring>
                                <hkcstring>Animations\Anim02.HKT</hkcstring>
                            </hkparam>
                        </hkobject>
                    </hkparam>
                    <hkparam name="animationNames" numelements="0"/>
                    <hkparam name="rigName">CharacterAssets\Skeleton.HKT</hkparam>
                    <hkparam name="behaviorFilename">Behaviors\Behavior00.hkx</hkparam>
                </hkobject>
            </hksection>
        </hkpackfile>
        """;

    // The summary has to name the member that holds the event rather than the struct that carries it,
    // because every clip trigger and every alarm is an hkbEventProperty and that name separates
    // nothing. It also has to keep quiet about whether any of it is right.
    private static void EventUsageSaysWhoSendsAndWhoListens()
    {
        Console.WriteLine("\nwho sends and who listens for each event, with no verdict");

        var usage = EventUsage.ByEvent(EventGraph());

        // Indexed through a helper on purpose. A missing event used to throw out of here and take the
        // rest of the suite with it, which reads as a crash rather than as the one thing that broke.
        Check("the enter notify event is seen at all", 1, Lines(usage, 3).Count);
        Check("and it is a send", EventUsage.Role.Raised, Line(usage, 3).Role);
        Check("named by the member holding it", "hkbStateMachineEventPropertyArray.events", Line(usage, 3).Site);

        Check("the transition's event is listened for", EventUsage.Role.Listened, Line(usage, 1).Role);
        Check("by the transition array", "hkbStateMachineTransitionInfoArray.eventId", Line(usage, 1).Site);

        Check("the clip trigger is a send", EventUsage.Role.Raised, Line(usage, 2).Role);
        Check("named by the trigger array, not hkbEventProperty", "hkbClipTriggerArray.event", Line(usage, 2).Site);

        // A member the table has never seen is reported as written here and nothing more. Guessing a
        // direction would be a verdict, which is the thing this deliberately does not do.
        Check("an unrecognised member has no role", EventUsage.Role.Referenced, Line(usage, 0).Role);
        Check("it is still named", "BSLimbCycleModifier.EventCycleLeft", Line(usage, 0).Site);
        Check("with no note invented for it", "", Line(usage, 0).Note);

        CheckTrue("an event listened for with no sender here is not called dead",
                  !EventUsage.Summarise(usage[1]).Contains("dead", StringComparison.OrdinalIgnoreCase)
                  && !EventUsage.Summarise(usage[1]).Contains("unused", StringComparison.OrdinalIgnoreCase));
        Check("it just says what it saw", "1 listened for here", EventUsage.Summarise(usage[1]));

        // The notify array carries its event inline with no class of its own, so leaving it out of the
        // carrier set hid it from the summary and, worse, from renumbering. The notify event is the
        // highest index in the fixture on purpose: it has to move when anything below it goes, and
        // while the array was unrecognised it silently did not, leaving a state sending whatever
        // ended up at its old index.
        Check("a notify event is visible to the reference walk", 1,
              SymbolIndexFixup.ReferencesTo(EventGraph(), events: true, 3).Count);

        SymbolIndexFixup.ShiftDown(EventGraph(), events: true, removedIndex: 0, out int all);
        Check("removing event 0 moves all three above it, notify event included", 3, all);

        string shifted = SymbolIndexFixup.ShiftDown(EventGraph(), events: true, removedIndex: 1, out int rewritten);
        Check("removing event 1 renumbers the two above it", 2, rewritten);
        var after = EventUsage.ByEvent(shifted);
        Check("the notify event came down to 2", "hkbStateMachineEventPropertyArray.events", Line(after, 2).Site);
        Check("the clip trigger came down to 1", "hkbClipTriggerArray.event", Line(after, 1).Site);
        CheckTrue("and nothing is left pointing at the old top index", !after.ContainsKey(3));
    }

    // Scale was decoded and then printed nowhere, so a wrong value and a right one looked the same.
    // Now that it is on screen, what counts as worth showing has to be pinned down: a track really at
    // 1,1,1 is not the same as one whose scale never decoded, and a track scaled to zero is the shape
    // a decode bug takes rather than something to hide.
    private static void ScaleIsShownOnlyWhenItIsRealScale()
    {
        Console.WriteLine("\nscale is reported when it is real and quiet when it is not");

        CheckTrue("a track with no scale at all is not called scaled",
                  !HkxTrackData.IsScaled(new HkxTrackData()));

        CheckTrue("a flat 1,1,1 is not called scaled",
                  !HkxTrackData.IsScaled(Scaled(Vector3.One, Vector3.One)));

        // The crow's folded wing, the real value read out of PerchedIdle.hkx.
        CheckTrue("the crow's 0.4599 wing counts as scaled",
                  HkxTrackData.IsScaled(Scaled(new Vector3(0.4599f, 0.4599f, 0.4599f))));

        CheckTrue("one scaled frame among unscaled ones still counts",
                  HkxTrackData.IsScaled(Scaled(Vector3.One, new Vector3(1f, 0.5f, 1f), Vector3.One)));

        CheckTrue("a single axis is enough",
                  HkxTrackData.IsScaled(Scaled(new Vector3(1f, 1f, 0.82f))));

        // Zero is the failure a wrong decode produces: whatever the track drives collapses. It has to
        // read as scaled so it is visible, not filtered out as uninteresting.
        CheckTrue("a zero scale is reported rather than hidden",
                  HkxTrackData.IsScaled(Scaled(Vector3.Zero)));

        // Float noise either side of 1 is not scale. The epsilon exists so quantised values that come
        // back as 0.99999994 do not light up every track in the game.
        CheckTrue("float noise just under 1 is not scale",
                  !HkxTrackData.IsScaled(Scaled(new Vector3(0.99999994f, 1f, 1.00000006f))));
        CheckTrue("but a real 0.999 is",
                  HkxTrackData.IsScaled(Scaled(new Vector3(0.999f, 1f, 1f))));
    }

    // A clip driven by a variable is sampled, not played, so the only question that matters is which
    // frame a given userControlledTimeFraction is sitting on. The trap is off by one: the fraction
    // spans the clip, so 1.0 is the last frame's index and not the frame count.
    private static void AFractionLandsOnAFrame()
    {
        Console.WriteLine("\na userControlledTimeFraction lands on a frame");

        var clip = new HkxAnimationData { NumFrames = 41 };
        Check("0 is the first frame", 0, clip.FrameAt(0f));
        Check("1 is the last frame, not one past it", 40, clip.FrameAt(1f));
        Check("half way is frame 20 of 40, not 20.5", 20, clip.FrameAt(0.5f));
        Check("a quarter", 10, clip.FrameAt(0.25f));

        // Out of range comes from a variable the graph drives, so it is clamped rather than throwing
        // or wrapping around to the other end of the clip.
        Check("below zero clamps to the first frame", 0, clip.FrameAt(-2f));
        Check("above one clamps to the last", 40, clip.FrameAt(7f));

        var single = new HkxAnimationData { NumFrames = 1 };
        Check("a one frame clip is always frame 0", 0, single.FrameAt(0.5f));
        var empty = new HkxAnimationData { NumFrames = 0 };
        Check("and an empty one does not divide by its own length", 0, empty.FrameAt(0.5f));

        // The real one, from Idle_TrainTrain_Song05: 3685 frames, well past the 300 row page size.
        var long_ = new HkxAnimationData { NumFrames = 3685 };
        Check("the longest vanilla animation ends on 3684", 3684, long_.FrameAt(1f));
        Check("and its midpoint is 13 pages in", 1842, long_.FrameAt(0.5f));
    }

    private static HkxTrackData Scaled(params Vector3[] frames)
    {
        var track = new HkxTrackData();
        track.Scales.AddRange(frames);
        return track;
    }

    private static List<EventUsage.Line> Lines(Dictionary<int, List<EventUsage.Line>> usage, int index) =>
        usage.TryGetValue(index, out var lines) ? lines : new List<EventUsage.Line>();

    private static EventUsage.Line Line(Dictionary<int, List<EventUsage.Line>> usage, int index)
    {
        var lines = Lines(usage, index);
        return lines.Count > 0 ? lines[0] : new EventUsage.Line(EventUsage.Role.Referenced, "not found", "", 0);
    }

    private static string EventGraph() => """
        <?xml version="1.0" encoding="ascii"?>
        <hkpackfile classversion="11" contentsversion="hk_2014.1.0-r1">
            <hksection name="__data__">
                <hkobject class="hkbStateMachine" name="#92" signature="0xa5896bcf">
                    <hkparam name="name">Root</hkparam>
                    <hkparam name="startStateId">0</hkparam>
                    <hkparam name="states" numelements="1">#93</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#93" signature="0x39d76713">
                    <hkparam name="name">A</hkparam>
                    <hkparam name="stateId">0</hkparam>
                    <hkparam name="enterNotifyEvents">#94</hkparam>
                    <hkparam name="transitions">#95</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineEventPropertyArray" name="#94" signature="0x71957c2d">
                    <hkparam name="events" numelements="1">
                        <hkobject>
                            <hkparam name="id">3</hkparam>
                            <hkparam name="payload">null</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineTransitionInfoArray" name="#95" signature="0xe397b11e">
                    <hkparam name="transitions" numelements="1">
                        <hkobject>
                            <hkparam name="eventId">1</hkparam>
                            <hkparam name="toStateId">0</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
                <hkobject class="hkbClipTriggerArray" name="#96" signature="0xf757cd66">
                    <hkparam name="triggers" numelements="1">
                        <hkobject>
                            <hkparam name="localTime">0.5</hkparam>
                            <hkparam name="event">
                                <hkobject class="hkbEventProperty" name="event" signature="0xdb38a15">
                                    <hkparam name="id">2</hkparam>
                                    <hkparam name="payload">null</hkparam>
                                </hkobject>
                            </hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
                <hkobject class="BSLimbCycleModifier" name="#97" signature="0x1f7a1c1b">
                    <hkparam name="name">Limbs</hkparam>
                    <hkparam name="EventCycleLeft">
                        <hkobject class="hkbEventProperty" name="EventCycleLeft" signature="0xdb38a15">
                            <hkparam name="id">0</hkparam>
                            <hkparam name="payload">null</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
            </hksection>
        </hkpackfile>
        """;

    private static string SmallGraph() => """
        <?xml version="1.0" encoding="ascii"?>
        <hkpackfile classversion="11" contentsversion="hk_2014.1.0-r1">
            <hksection name="__data__">
                <hkobject class="hkbBehaviorGraph" name="#91" signature="0xb1218f86">
                    <hkparam name="name">Graph</hkparam>
                    <hkparam name="rootGenerator">#92</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachine" name="#92" signature="0xa5896bcf">
                    <hkparam name="name">Root</hkparam>
                    <hkparam name="startStateId">0</hkparam>
                    <hkparam name="wildcardTransitions">null</hkparam>
                    <hkparam name="states" numelements="2">
                        #93 #95
                    </hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#93" signature="0x39d76713">
                    <hkparam name="name">A</hkparam>
                    <hkparam name="stateId">0</hkparam>
                    <hkparam name="generator">#94</hkparam>
                    <hkparam name="transitions">null</hkparam>
                </hkobject>
                <hkobject class="hkbClipGenerator" name="#94" signature="0xd4cc9f6">
                    <hkparam name="name">ClipA</hkparam>
                    <hkparam name="animationName">a.hkx</hkparam>
                    <hkparam name="triggers">null</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#95" signature="0x39d76713">
                    <hkparam name="name">B</hkparam>
                    <hkparam name="stateId">1</hkparam>
                    <hkparam name="generator">#96</hkparam>
                    <hkparam name="transitions">null</hkparam>
                </hkobject>
                <hkobject class="hkbClipGenerator" name="#96" signature="0xd4cc9f6">
                    <hkparam name="name">ClipB</hkparam>
                    <hkparam name="animationName">b.hkx</hkparam>
                    <hkparam name="triggers">null</hkparam>
                </hkobject>
                <hkobject class="hkbClipGenerator" name="#97" signature="0xd4cc9f6">
                    <hkparam name="name">Spare</hkparam>
                    <hkparam name="animationName">spare.hkx</hkparam>
                    <hkparam name="triggers">null</hkparam>
                </hkobject>
            </hksection>
        </hkpackfile>
        """;
}
