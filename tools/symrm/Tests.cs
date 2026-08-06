using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.Tools;

// Regressions worth catching without a game install. Everything here works on a graph built in
// memory, so it needs no BA2, no hkxpack and no JVM, which means it can actually be run every time
// rather than only when someone remembers to extract a corpus.
public static class Tests
{
    private static int _failed;
    private static int _ran;

    /// One entry per group of checks, named so a runner can report them individually. The console
    /// runner walks the whole list; a test host runs them one at a time through RunOne.
    public static readonly (string Name, Action Check)[] Cases =
    {
        ("DetachedSubtreeStaysDrawn", DetachedSubtreeStaysDrawn),
        ("ReplacingLinkSaysWhatItDisplaced", ReplacingLinkSaysWhatItDisplaced),
        ("BlenderChildIsWrapped", BlenderChildIsWrapped),
        ("AnyNodeCanBeDeleted", AnyNodeCanBeDeleted),
        ("StructuralObjectsAreProtected", StructuralObjectsAreProtected),
        ("PortTypesRefuseNonsense", PortTypesRefuseNonsense),
        ("BundledHkxPackIsFound", BundledHkxPackIsFound),
        ("Fo4CharacterListsItsAnimations", Fo4CharacterListsItsAnimations),
        ("MissingClipAnimationIsReported", MissingClipAnimationIsReported),
        ("RepackDriftNamesWhatMoved", RepackDriftNamesWhatMoved),
        ("AnUnreachableStateIsReported", AnUnreachableStateIsReported),
        ("EventUsageSaysWhoSendsAndWhoListens", EventUsageSaysWhoSendsAndWhoListens),
        ("ScaleIsShownOnlyWhenItIsRealScale", ScaleIsShownOnlyWhenItIsRealScale),
        ("AFractionLandsOnAFrame", AFractionLandsOnAFrame),
        ("LosslessScaleFollowsTheEngine", LosslessScaleFollowsTheEngine),
        ("AnEmptyStateIsFoundTheSameWayEverywhere", AnEmptyStateIsFoundTheSameWayEverywhere),
        ("AddedVariablesCarryTheirDeclaredType", AddedVariablesCarryTheirDeclaredType),
        ("EveryFindingPointsAtAnObject", EveryFindingPointsAtAnObject),
        ("AShortBoundsArrayStaysLinedUp", AShortBoundsArrayStaysLinedUp),
        ("WindowsLineEndingsStillEdit", WindowsLineEndingsStillEdit),
        ("RepackDriftCatchesAChangedValue", RepackDriftCatchesAChangedValue),
        ("AnAnimationIsRefusedForSaving", AnAnimationIsRefusedForSaving),
        ("TwoFilesDiffToWhatEachChanged", TwoFilesDiffToWhatEachChanged),
        ("EverySymbolUsageNamesItsObject", EverySymbolUsageNamesItsObject),
        ("PapyrusSendersAreReportedNotJudged", PapyrusSendersAreReportedNotJudged),
        ("APoseComposesDownTheBoneChain", APoseComposesDownTheBoneChain),
        ("AClearChannelKeepsTheReferencePose", AClearChannelKeepsTheReferencePose),
        ("SplineUndrivenChannelsReadAsIdentity", SplineUndrivenChannelsReadAsIdentity),
        ("APackfileSurvivesBeingRebuilt", APackfileSurvivesBeingRebuilt),
        ("ScrubbingLandsOnDifferentPoses", ScrubbingLandsOnDifferentPoses),
        ("TracksDriveTheBonesTheyName", TracksDriveTheBonesTheyName),
        ("AnimationsForAnotherRigAreRefused", AnimationsForAnotherRigAreRefused),
        ("AModelIsFoundOnlyWhenThereIsNoDoubt", AModelIsFoundOnlyWhenThereIsNoDoubt),
        ("AValueThatIsNotANumberIsRefused", AValueThatIsNotANumberIsRefused),
        ("AStringIsWrittenAtWhateverLength", AStringIsWrittenAtWhateverLength),
        ("WideAndVectorFieldsReadFromTheBytes", WideAndVectorFieldsReadFromTheBytes),
        ("ReferencesAndArraysReadFromTheBytes", ReferencesAndArraysReadFromTheBytes),
        ("AnUndeclaredEnumValueIsNotNamed", AnUndeclaredEnumValueIsNotNamed),
        ("TheModelComparisonCatchesFaultsPutThereOnPurpose", TheModelComparisonCatchesFaultsPutThereOnPurpose),
        ("AFloatIsSpelledTheWayHkxPackSpellsIt", AFloatIsSpelledTheWayHkxPackSpellsIt),
        ("AnAppendedObjectLandsWhereItsNumberSaysItWill", AnAppendedObjectLandsWhereItsNumberSaysItWill),
        ("WideFloatFieldsAreWrittenInBracketedFours", WideFloatFieldsAreWrittenInBracketedFours),
        ("TheConsumerComparisonCatchesADifferentAnswer", TheConsumerComparisonCatchesADifferentAnswer),
        ("APointerIsRewiredByMovingItsFixup", APointerIsRewiredByMovingItsFixup),
        ("APointerChangeIsPlannedAsOne", APointerChangeIsPlannedAsOne),
        ("ThePointerTableKeepsTheOrderItWasWrittenIn", ThePointerTableKeepsTheOrderItWasWrittenIn),
        ("AnAddedObjectHasToLandWhereItsIdSays", AnAddedObjectHasToLandWhereItsIdSays),
        ("TheReadingFromTheBytesRefusesWhatItCannotDescribe", TheReadingFromTheBytesRefusesWhatItCannotDescribe),
        ("ThePanelReadsItsListFromTheTable", ThePanelReadsItsListFromTheTable),
        ("AnEscapedValueIsShownAsItself", AnEscapedValueIsShownAsItself),
        ("ASpaceInAValueIsKept", ASpaceInAValueIsKept),
        ("TheClassTableKnowsWhatTheDumpCannot", TheClassTableKnowsWhatTheDumpCannot),
        ("AFieldListIsBuiltWithoutHkxPack", AFieldListIsBuiltWithoutHkxPack),
        ("AClassSignedDifferentlyIsRefused", AClassSignedDifferentlyIsRefused),
        ("AMisSignedFileIsNotWrittenInto", AMisSignedFileIsNotWrittenInto),
        ("AnEnumIsNamedSignedAndPrintedUnsigned", AnEnumIsNamedSignedAndPrintedUnsigned),
        ("APaddedStructIsKnownFromHkxPacksIdeaOfIt", APaddedStructIsKnownFromHkxPacksIdeaOfIt),
    };

    /// Runs one case in isolation and returns how many of its checks failed. The counters are static,
    /// so they are reset here rather than shared with whatever ran before.
    public static int RunOne(string name)
    {
        var match = Array.Find(Cases, c => c.Name == name);
        if (match.Check == null) throw new ArgumentException($"no test case called {name}");

        _failed = 0;
        _ran = 0;
        match.Check();
        return _failed;
    }

    public static int Run()
    {
        _failed = 0;
        _ran = 0;

        foreach (var (_, check) in Cases) check();

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

    // Deleting a generator clears the link that held it rather than refusing, so a state can be left
    // holding nothing. Check graph has always reported it; the views and Save now mark it too, and all
    // three ask the same function so they cannot drift into disagreeing about what empty means.
    private static void AnEmptyStateIsFoundTheSameWayEverywhere()
    {
        Console.WriteLine("\na state left holding nothing is found the same way everywhere");

        string xml = SmallGraph();
        var model = BehaviourGraphModel.Parse(xml);
        Check("a whole graph has no empty states", 0, GraphValidator.StatesWithNoGenerator(model).Count);

        // #94 is state A's clip. Deleting it clears A's generator link, which is the shape the ticket
        // is about: the delete is correct, and the state it leaves behind looks ordinary.
        string after = GraphAuthor.DeleteNode(xml, "94", out _);
        var afterModel = BehaviourGraphModel.Parse(after);
        var empty = GraphValidator.StatesWithNoGenerator(afterModel);

        Check("deleting a state's generator leaves one empty state", 1, empty.Count);
        CheckTrue("and it is state A, the one that held the clip", empty.Contains("93"));
        Check("the state itself is still there, not deleted with it", "A", afterModel.Get("93")?.Str("name"));
        Check("its generator link reads null rather than dangling", "null", afterModel.Get("93")?.Str("generator"));

        // The views mark what this set contains, so it has to agree with what Check graph reports or
        // one of them is lying to the person reading it.
        var reported = GraphValidator.Check(after)
            .Where(f => f.What.Contains("nothing to play", StringComparison.Ordinal)).ToList();
        Check("Check graph reports exactly the same count", empty.Count, reported.Count);
        CheckTrue("and reports it as an error", reported.All(f => f.Level == GraphValidator.Level.Error));
        CheckTrue("naming the state", reported.Any(f => f.Where.Contains("'A'")));

        // Vanilla never ships this, so a mark appearing on an unedited file would be a false alarm.
        Check("an untouched graph stays unmarked", 0,
              GraphValidator.StatesWithNoGenerator(BehaviourGraphModel.Parse(SmallGraph())).Count);

        // The game crashed while loading a graph carrying exactly one of these, so Save refuses
        // rather than warns. Both come from the same set above, so the refusal cannot disagree
        // with the mark.
        Check("a whole graph is not refused", null, GraphValidator.RefuseToSave(xml));
        Check("an empty file is not refused either", null, GraphValidator.RefuseToSave(""));

        // Spelled as an empty string rather than null so a missing refusal fails all four of these
        // instead of throwing on the first and hiding the other three.
        string refusal = GraphValidator.RefuseToSave(after) ?? "";
        CheckTrue("one empty state is refused", refusal.Length > 0);
        CheckTrue("saying nothing was written", refusal.Contains("original is untouched"));
        CheckTrue("and why the game cannot take it", refusal.Contains("crashes on load"));
        CheckTrue("without claiming the state has to be entered",
                  refusal.Contains("whether or not anything can enter"));

        // Being stopped without being told which state, or what to do about it, is worse than not
        // checking at all. A count on its own sends someone hunting through the tree.
        CheckTrue("naming the state rather than only counting it", refusal.Contains("'A'"));
        CheckTrue("and the machine it sits in", refusal.Contains("in Root"));
        CheckTrue("saying how to fix it", refusal.Contains("give each one a generator"));
        CheckTrue("and that deleting the state is the other way out", refusal.Contains("delete the state"));

        // Four names is the cap, so a file with many does not produce an unreadable wall.
        var many = GraphValidator.EmptyStates(BehaviourGraphModel.Parse(after));
        Check("one empty state is found by name", 1, many.Count);
        Check("named the way the refusal prints it", "'A' in Root", many[0].ToString());
        CheckTrue("counting the states rather than guessing", refusal.Contains("1 state has"));
    }

    // A finding the canvas cannot place is a finding nobody can act on: the red outline and the jump
    // from the problem list both key off the object id, so a finding that loses it silently drops out
    // of both while still being printed.
    private static void EveryFindingPointsAtAnObject()
    {
        Console.WriteLine("\nevery finding carries the object it is about");

        string xml = GraphAuthor.DeleteNode(SmallGraph(), "94", out _);
        var findings = GraphValidator.Check(xml);

        CheckTrue("the check found something", findings.Count > 0);
        CheckTrue("and each one that is about an object carries its id",
                  findings.Where(f => f.Where.StartsWith('#')).All(f => f.ObjectId.Length > 0));
        CheckTrue("with the # and any trailing words stripped off",
                  findings.All(f => f.ObjectId.All(char.IsAsciiDigit)));

        var byObject = GraphValidator.ByObject(findings);
        CheckTrue("the empty state is one of them", byObject.ContainsKey("93"));
        Check("and it is marked as an error", GraphValidator.Level.Error, byObject["93"]);

        // Errors win over warnings on the same node, or a node with both is drawn amber and reads as
        // something that can be left alone.
        var mixed = GraphValidator.ByObject(new List<GraphValidator.Finding>
        {
            new() { Level = GraphValidator.Level.Warning, Where = "#7 thing", ObjectId = "7" },
            new() { Level = GraphValidator.Level.Error,   Where = "#7 thing", ObjectId = "7" },
        });
        Check("a node with both is an error", GraphValidator.Level.Error, mixed["7"]);

        Check("a finding about nothing in particular is not placed", 0,
              GraphValidator.ByObject(new List<GraphValidator.Finding>
              {
                  new() { Level = GraphValidator.Level.Error, Where = "hkbBehaviorGraphData" },
              }).Count);

        // A symbol index past the end of the declared list used to report the class and member only,
        // which named the fault without saying which of the file's objects carried it. Over the 531
        // vanilla files those were the last 11 findings the canvas could not place.
        var reaching = SymbolIndexFixup.ReferencesAtOrAbove(EventGraph(), events: true, 0);
        CheckTrue("an event index reference is found at all", reaching.Count > 0);
        CheckTrue("and it names the object that carries it", reaching.All(r => r.StartsWith('#')));
    }

    // variableBounds is positional and is allowed to stop short: hkbVariableBounds is 8 bytes holding
    // min and max and nothing else, so there is no field in it that could name a variable and
    // position is the only key there can be. 87 of the 531 vanilla files ship a short one.
    //
    // Removing a variable inside that range therefore has to take its bound with it. Skipping it
    // because the array is not full length slides every bound above the removed variable onto its
    // neighbour, which is silent: the file stays valid and the wrong variable gets clamped.
    private static void AShortBoundsArrayStaysLinedUp()
    {
        Console.WriteLine("\na short bounds array stays lined up when a variable is removed");

        string xml = ThreeVariablesWithTwoBounds();
        var before = SymbolEditor.Audit(BehaviourGraphModel.Parse(xml));
        Check("three variables", 3, before.Names);
        Check("and only two bounds", 2, before.Bounds);
        CheckTrue("so the array is short, not parallel", !before.BoundsAreParallel);

        // Removing the first variable, which is inside the bounds array.
        string after = SymbolEditor.RemoveVariable(xml, 0, force: true, out _);
        var counts = SymbolEditor.Audit(BehaviourGraphModel.Parse(after));
        Check("two variables are left", 2, counts.Names);
        Check("and one bound, because its entry went with it", 1, counts.Bounds);
        Check("the bound left behind is the second one, not the first", "20",
              BoundMax(after, 0));

        // Removing past the end of the bounds array must not touch it.
        string tail = SymbolEditor.RemoveVariable(xml, 2, force: true, out _);
        var tailCounts = SymbolEditor.Audit(BehaviourGraphModel.Parse(tail));
        Check("removing a variable past the bounds leaves them alone", 2, tailCounts.Bounds);
        Check("with the first bound untouched", "10", BoundMax(tail, 0));
    }

    // The bound values sit in nested hkbVariableValue objects rather than as plain members, so this
    // reads them out of the text rather than through the model's scalar view.
    private static string BoundMax(string xml, int index)
    {
        int start = xml.IndexOf("name=\"variableBounds\"", StringComparison.Ordinal);
        if (start < 0) return "";
        var maxima = System.Text.RegularExpressions.Regex
            .Matches(xml[start..], "name=\"max\".*?name=\"value\">(-?\\d+)<",
                     System.Text.RegularExpressions.RegexOptions.Singleline);
        return index < maxima.Count ? maxima[index].Groups[1].Value : "";
    }

    // No vanilla file carries a scale on a lossless compressed animation: all 856 leave both arrays
    // empty with every word clear, so the static and dynamic branches never run on real data. The
    // rules below are therefore taken from the engine rather than from a file, out of
    // hkaLosslessCompressedAnimation::getType, ::getOffset and ::getFrameTransform in the 1.10.163
    // unpacked binary, and this is what holds the reader to them.
    private static void LosslessScaleFollowsTheEngine()
    {
        Console.WriteLine("\nlossless scale decodes the way the engine's own sampler does");

        // getType<u64>:   (word >> (component * 16)) & 3
        // getOffset<u64>: ((word >> (component * 16)) >> 2) & 0x3FFF
        // So one 64 bit word carries four fields, one per component, each (offset << 2) | type.
        ulong word = Field(0, 5, 1) | Field(1, 9, 2) | Field(2, 0, 0) | Field(3, 0x3FFF, 2);

        Check("component 0 is static", 1, HkxBinaryReader.LosslessType(word, 0));
        Check("with offset 5", 5, HkxBinaryReader.LosslessOffset(word, 0));
        Check("component 1 is dynamic", 2, HkxBinaryReader.LosslessType(word, 1));
        Check("with offset 9", 9, HkxBinaryReader.LosslessOffset(word, 1));
        Check("component 2 is clear", 0, HkxBinaryReader.LosslessType(word, 2));
        // The top field lives above bit 32, which is the half hkxpack's XML drops. Reading it from the
        // binary rather than from a dump is the only reason this one is right.
        Check("component 3 carries the widest offset the format allows", 0x3FFF,
              HkxBinaryReader.LosslessOffset(word, 3));

        var constants = new List<float> { 9f, 9f, 9f, 9f, 9f, 0.5f };
        var dynamic = new List<float>();
        for (int i = 0; i < 40; i++) dynamic.Add(i);

        Check("static reads the constant at its offset", 0.5f,
              HkxBinaryReader.LosslessValue(word, 0, frame: 3, stride: 4, dynamic, constants, 1f));

        // The trap that nearly shipped on translations: the dynamic arrays are frame major, so the
        // index is offset + frame * stride, not offset * frames + frame. Both look plausible and only
        // one moves per frame.
        Check("dynamic is frame major, frame 0", 9f,
              HkxBinaryReader.LosslessValue(word, 1, frame: 0, stride: 4, dynamic, constants, 1f));
        Check("dynamic is frame major, frame 3", 21f,
              HkxBinaryReader.LosslessValue(word, 1, frame: 3, stride: 4, dynamic, constants, 1f));

        // The engine prefills the transform before it touches anything: translation 0, rotation
        // identity, scale 1,1,1,1, read from the constant at 0x143828480. A clear word writes nothing,
        // so the prefill is the answer. Scale falling back to 0 would collapse whatever it drives.
        Check("a clear scale component is 1, not 0", 1f,
              HkxBinaryReader.LosslessValue(word, 2, frame: 3, stride: 4, dynamic, constants, 1f));
        Check("a clear translation component is 0", 0f,
              HkxBinaryReader.LosslessValue(word, 2, frame: 3, stride: 4, dynamic, constants, 0f));

        // An offset past the end of its array is a corrupt file, not a crash.
        ulong wild = Field(0, 4000, 1) | Field(1, 4000, 2);
        Check("a static offset past the array falls back", 1f,
              HkxBinaryReader.LosslessValue(wild, 0, frame: 0, stride: 4, dynamic, constants, 1f));
        Check("so does a dynamic one", 1f,
              HkxBinaryReader.LosslessValue(wild, 1, frame: 0, stride: 4, dynamic, constants, 1f));
    }

    private static ulong Field(int component, int offset, int type) =>
        ((ulong)(((offset & 0x3FFF) << 2) | (type & 3))) << (component * 16);

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
        return lines.Count > 0
            ? lines[0]
            : new EventUsage.Line(EventUsage.Role.Referenced, "not found", "", 0, Array.Empty<string>());
    }

    // A variable lives in three arrays at once, and the one that silently went missing was its
    // declared type. BindingEditor.AddVariable used to test variableInfos against Lists, but that
    // field is a struct list, so the check was always false: the name and the value were written, the
    // info element was not, and nothing raised an error. The engine then read a variable with no
    // declared type.
    //
    // The only thing exercising this path was symrm remove, which needs hkxpack, a JVM and a real game
    // file, so it never ran in CI. This does, on a graph built in memory.
    private static void AddedVariablesCarryTheirDeclaredType()
    {
        Console.WriteLine("\nan added variable carries its declared type into variableInfos");

        foreach (var (type, expected) in new[]
                 {
                     (SymbolEditor.VariableType.Real,  "VARIABLE_TYPE_REAL"),
                     (SymbolEditor.VariableType.Int32, "VARIABLE_TYPE_INT32"),
                     (SymbolEditor.VariableType.Bool,  "VARIABLE_TYPE_BOOL"),
                 })
        {
            string xml = SymbolEditor.AddVariable(SymbolGraph(), "fProbe", type, out int index);
            var model = BehaviourGraphModel.Parse(xml);
            var counts = SymbolEditor.Audit(model);

            Check($"{type}: the new variable takes the next index", 2, index);
            Check($"{type}: its name is declared", "fProbe", SymbolEditor.VariableNames(model)[index]);
            Check($"{type}: an info element was written for it", 3, counts.Infos);
            Check($"{type}: with the right declared type", expected, TypeOfVariable(model, index));
            CheckTrue($"{type}: the three arrays still agree", counts.VariablesConsistent);
        }

        // The binding path is the one that had the bug, so it gets its own check rather than trusting
        // that delegation stayed in place.
        string bound = BindingEditor.AddVariable(SymbolGraph(), "fBoundProbe", out int boundIndex);
        var boundModel = BehaviourGraphModel.Parse(bound);
        Check("BindingEditor declares a real variable too", "VARIABLE_TYPE_REAL",
              TypeOfVariable(boundModel, boundIndex));
        CheckTrue("and leaves the arrays consistent", SymbolEditor.Audit(boundModel).VariablesConsistent);

        // The failure this guards against was silent: names grew, infos did not. Assert the shape
        // rather than only the count, so a future edit that writes an element with no type still fails.
        string twice = SymbolEditor.AddVariable(
            SymbolEditor.AddVariable(SymbolGraph(), "fOne", SymbolEditor.VariableType.Real, out _),
            "bTwo", SymbolEditor.VariableType.Bool, out int second);
        var twiceModel = BehaviourGraphModel.Parse(twice);
        Check("adding two keeps names and infos in step", 4, SymbolEditor.Audit(twiceModel).Infos);
        Check("and each keeps its own type", "VARIABLE_TYPE_BOOL", TypeOfVariable(twiceModel, second));
        Check("the earlier one is untouched", "VARIABLE_TYPE_REAL", TypeOfVariable(twiceModel, second - 1));
    }

    private static string TypeOfVariable(BehaviourGraphModel model, int index)
    {
        var data = model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraphData");
        if (data == null || !data.StructLists.TryGetValue("variableInfos", out var infos)) return "no variableInfos";
        if (index < 0 || index >= infos.Count) return "no element at that index";
        return infos[index].TryGetValue("type", out string? t) ? t : "element with no type";
    }

    // The three arrays a variable lives in, with two variables already declared so an append has
    // something to line up against.
    private static string SymbolGraph() => """
        <?xml version="1.0" encoding="ascii"?>
        <hkpackfile classversion="11" contentsversion="hk_2014.1.0-r1">
            <hksection name="__data__">
                <hkobject class="hkbBehaviorGraphStringData" name="#90" signature="0xc713064e">
                    <hkparam name="variableNames" numelements="2">
                        <hkcstring>fExisting</hkcstring>
                        <hkcstring>bExisting</hkcstring>
                    </hkparam>
                    <hkparam name="eventNames" numelements="0"></hkparam>
                </hkobject>
                <hkobject class="hkbBehaviorGraphData" name="#91" signature="0x95aca5d">
                    <hkparam name="variableInfos" numelements="2">
                        <hkobject>
                            <hkparam name="type">VARIABLE_TYPE_REAL</hkparam>
                        </hkobject>
                        <hkobject>
                            <hkparam name="type">VARIABLE_TYPE_BOOL</hkparam>
                        </hkobject>
                    </hkparam>
                    <hkparam name="eventInfos" numelements="0"></hkparam>
                    <hkparam name="variableBounds" numelements="0"></hkparam>
                </hkobject>
                <hkobject class="hkbVariableValueSet" name="#92" signature="0x27812d8d">
                    <hkparam name="wordVariableValues" numelements="2">
                        <hkobject>
                            <hkparam name="value">0</hkparam>
                        </hkobject>
                        <hkobject>
                            <hkparam name="value">0</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
            </hksection>
        </hkpackfile>
        """;

    // Three variables, two bounds. The shape 87 vanilla files ship and the one a parallel-only rule
    // silently mishandles.
    private static string ThreeVariablesWithTwoBounds() => """
        <?xml version="1.0" encoding="ascii"?>
        <hkpackfile classversion="11" contentsversion="hk_2014.1.0-r1">
            <hksection name="__data__">
                <hkobject class="hkbBehaviorGraphStringData" name="#90" signature="0xc713064e">
                    <hkparam name="variableNames" numelements="3">
                        <hkcstring>fFirst</hkcstring>
                        <hkcstring>fSecond</hkcstring>
                        <hkcstring>fThird</hkcstring>
                    </hkparam>
                    <hkparam name="eventNames" numelements="0"></hkparam>
                </hkobject>
                <hkobject class="hkbBehaviorGraphData" name="#91" signature="0x95aca5d">
                    <hkparam name="variableInfos" numelements="3">
                        <hkobject>
                            <hkparam name="type">VARIABLE_TYPE_REAL</hkparam>
                        </hkobject>
                        <hkobject>
                            <hkparam name="type">VARIABLE_TYPE_REAL</hkparam>
                        </hkobject>
                        <hkobject>
                            <hkparam name="type">VARIABLE_TYPE_REAL</hkparam>
                        </hkobject>
                    </hkparam>
                    <hkparam name="eventInfos" numelements="0"></hkparam>
                    <hkparam name="variableBounds" numelements="2">
                        <hkobject>
                            <hkparam name="min">
                                <hkobject class="hkbVariableValue" name="min" signature="0xb99bd6a">
                                    <hkparam name="value">0</hkparam>
                                </hkobject>
                            </hkparam>
                            <hkparam name="max">
                                <hkobject class="hkbVariableValue" name="max" signature="0xb99bd6a">
                                    <hkparam name="value">10</hkparam>
                                </hkobject>
                            </hkparam>
                        </hkobject>
                        <hkobject>
                            <hkparam name="min">
                                <hkobject class="hkbVariableValue" name="min" signature="0xb99bd6a">
                                    <hkparam name="value">0</hkparam>
                                </hkobject>
                            </hkparam>
                            <hkparam name="max">
                                <hkobject class="hkbVariableValue" name="max" signature="0xb99bd6a">
                                    <hkparam name="value">20</hkparam>
                                </hkobject>
                            </hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
                <hkobject class="hkbVariableValueSet" name="#92" signature="0x27812d8d">
                    <hkparam name="wordVariableValues" numelements="3">
                        <hkobject>
                            <hkparam name="value">0</hkparam>
                        </hkobject>
                        <hkobject>
                            <hkparam name="value">0</hkparam>
                        </hkobject>
                        <hkobject>
                            <hkparam name="value">0</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
            </hksection>
        </hkpackfile>
        """;

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

    // hkxpack writes the platform's line ending, so every unpacked file on Windows is CRLF. The
    // parameter regex is anchored to end of line, and .NET's multiline $ sits between the \r and the
    // \n, so it matched nothing there: every object reported zero editable fields and every edit
    // routed through SetParam failed, which includes connecting and disconnecting nodes. Reading and
    // drawing were unaffected, so the window looked like it was working. Reported from a Windows
    // build of the first beta.
    private static void WindowsLineEndingsStillEdit()
    {
        Console.WriteLine("a file with Windows line endings is still editable");

        string lf = SmallGraph().Replace("\r\n", "\n");
        string crlf = lf.Replace("\n", "\r\n");

        Check("fields read from a unix file", 3, HkxTextEdit.ReadParams(lf, "96").Count);
        Check("fields read from a windows file", 3, HkxTextEdit.ReadParams(crlf, "96").Count);

        string edited = HkxTextEdit.SetParam(crlf, "96", "animationName", "changed.hkx");
        Check("a field set on a windows file",
              "changed.hkx", BehaviourGraphModel.Parse(edited).Get("96")!.Str("animationName"));

        // Connecting goes through SetParam, so it failed the same way and looked like a dead canvas.
        string linked = GraphLinks.Connect(crlf, "95", "generator", "97", out _);
        Check("a node connected on a windows file",
              "97", BehaviourGraphModel.Parse(linked).Get("95")!.Ref("generator"));

        // Whatever it was read as, one line ending comes out, or the splices disagree with the file.
        string normalised = lf.Replace("\n", "\r\n");
        CheckTrue("reading normalises the line endings",
                  !NormaliseLike(normalised).Contains('\r'));
    }

    private static string NormaliseLike(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n");

    // Counting objects and class names catches a repack that came back short. It does not catch one
    // that came back the same size with a value moved, which loads and then behaves wrongly with
    // nothing reporting it, so the contents are compared too. Renumbering is the one difference a
    // repack is allowed to make and must not read as a change.
    private static void RepackDriftCatchesAChangedValue()
    {
        Console.WriteLine("\nrepack drift catches a value that moved, and ignores renumbering");

        string before = SmallGraph();

        // What hkxpack does on every pack: same objects, same order, different numbers.
        string renumbered = Regex.Replace(before, @"#(\d+)",
                                          m => "#" + (int.Parse(m.Groups[1].Value) + 400));
        var same = RepackCheck.Compare(RepackCheck.Take(before), RepackCheck.Take(renumbered));
        CheckTrue("renumbering alone is not drift", same.Clean);
        CheckTrue("and it says so plainly", same.ToString().Contains("every value"));

        string moved = renumbered.Replace("<hkparam name=\"animationName\">b.hkx</hkparam>",
                                          "<hkparam name=\"animationName\">elsewhere.hkx</hkparam>");
        var drift = RepackCheck.Compare(RepackCheck.Take(before), RepackCheck.Take(moved));
        CheckTrue("a changed value is drift", !drift.Clean);
        Check("the object count still agrees", drift.Before, drift.After);
        CheckTrue("nothing was reported lost or gained", drift.Lost.Count == 0 && drift.Gained.Count == 0);
        Check("one value is named", 1, drift.Changed.Count);
        Check("named by field, not just by object", true,
              drift.Changed.Count > 0 && drift.Changed[0].Contains("animationName"));
        Console.WriteLine("        -> " + (drift.Changed.FirstOrDefault() ?? ""));

        string retyped = renumbered.Replace("class=\"hkbClipGenerator\" name=\"#496\"",
                                            "class=\"hkbBlenderGenerator\" name=\"#496\"");
        var swapped = RepackCheck.Compare(RepackCheck.Take(before), RepackCheck.Take(retyped));
        CheckTrue("an object coming back a different class is drift", !swapped.Clean);
    }

    // hkxpack keeps only the low half of the packed words in a lossless compressed animation, so a
    // dump of one repacks into a different animation. Nothing routes an animation into saving today,
    // which is luck rather than a guard, so the refusal is stated rather than assumed.
    private static void AnAnimationIsRefusedForSaving()
    {
        Console.WriteLine("\nan animation hkxpack cannot carry is refused before it is written");

        CheckTrue("a behaviour is not refused", GraphValidator.RefuseToSave(SmallGraph()) == null);

        string withAnimation = SmallGraph().Replace(
            "<hkobject class=\"hkbClipGenerator\" name=\"#97\"",
            "<hkobject class=\"hkaLosslessCompressedAnimation\" name=\"#99\" signature=\"0x1\">\n" +
            "            <hkparam name=\"numFrames\">2</hkparam>\n" +
            "        </hkobject>\n" +
            "        <hkobject class=\"hkbClipGenerator\" name=\"#97\"");

        string? refusal = GraphValidator.RefuseToSave(withAnimation);
        CheckTrue("one that holds a lossless compressed animation is refused", refusal != null);
        CheckTrue("and the refusal names the class",
                  refusal?.Contains("hkaLosslessCompressedAnimation") == true);
        CheckTrue("and says the original is untouched", refusal?.Contains("untouched") == true);
    }

    // A three bone chain along X, each bone one unit out from its parent. Small enough that every
    // world position below can be worked out by hand, which is the point: the arithmetic is checked
    // against known numbers rather than against whatever the code happens to produce.
    private static HkxSkeleton ThreeBoneChain() => new()
    {
        Name = "TestRig",
        BoneNames = { "Root", "Middle", "Tip" },
        ParentIndices = { -1, 0, 1 },
        ReferencePose =
        {
            new HkxBonePose(new Vector3(0, 0, 0), Quaternion.Identity, Vector3.One),
            new HkxBonePose(new Vector3(10, 0, 0), Quaternion.Identity, Vector3.One),
            new HkxBonePose(new Vector3(10, 0, 0), Quaternion.Identity, Vector3.One),
        },
    };

    private static HkxTrackData FullTrack(params (Vector3 Pos, Quaternion Rot)[] frames)
    {
        var track = new HkxTrackData { RotationAnimated = true };
        for (int a = 0; a < 3; a++) track.TranslationAnimated[a] = true;
        foreach (var (pos, rot) in frames)
        {
            track.Translations.Add(pos);
            track.Rotations.Add(rot);
            track.Scales.Add(Vector3.One);
        }
        return track;
    }

    private static void APoseComposesDownTheBoneChain()
    {
        Console.WriteLine("\na pose composes parent relative transforms down the chain");

        var rig = ThreeBoneChain();
        var rest = AnimationPose.ReferencePose(rig);

        Check("every bone is posed", 3, rest.Bones.Count);
        CheckTrue("the root sits at the origin", Near(rest.Bones[0].Position, new Vector3(0, 0, 0)));
        CheckTrue("the middle bone is one offset out", Near(rest.Bones[1].Position, new Vector3(10, 0, 0)));
        CheckTrue("and the tip is two, because offsets accumulate",
                  Near(rest.Bones[2].Position, new Vector3(20, 0, 0)));

        Check("a line is drawn for every bone that has a parent", 2, rest.Links.Count);
        CheckTrue("root to middle", rest.Links.Contains((0, 1)));
        CheckTrue("middle to tip", rest.Links.Contains((1, 2)));

        // The whole reason transforms are stored parent relative: rotating a parent has to swing
        // everything below it. A quarter turn about Z takes the chain from along X to along Y.
        var quarter = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        var anim = new HkxAnimationData
        {
            NumFrames = 1,
            NumTracks = 3,
            FrameDuration = 1f / 30f,
            TrackToBoneIndices = { 0, 1, 2 },
        };
        anim.Tracks.Add(FullTrack((Vector3.Zero, quarter)));
        anim.Tracks.Add(FullTrack((new Vector3(10, 0, 0), Quaternion.Identity)));
        anim.Tracks.Add(FullTrack((new Vector3(10, 0, 0), Quaternion.Identity)));

        var turned = AnimationPose.At(rig, anim, 0);
        CheckTrue("rotating the root swings the middle bone onto Y",
                  Near(turned.Bones[1].Position, new Vector3(0, 10, 0)));
        CheckTrue("and carries the tip with it",
                  Near(turned.Bones[2].Position, new Vector3(0, 20, 0)));
        CheckTrue("the root itself has not moved", Near(turned.Bones[0].Position, Vector3.Zero));
    }

    // The trap that makes a viewport look broken rather than wrong. Havok leaves a channel clear when
    // the animation does not drive it, and both decoders prefill a cleared channel with zero, which is
    // indistinguishable afterwards from a bone genuinely at the origin. Posing on the raw value
    // collapses every rotation-only bone onto its parent, which is most of a character.
    private static void AClearChannelKeepsTheReferencePose()
    {
        Console.WriteLine("\na channel the animation does not drive keeps the reference pose");

        var rig = ThreeBoneChain();
        var anim = new HkxAnimationData
        {
            NumFrames = 1,
            NumTracks = 3,
            FrameDuration = 1f / 30f,
            TrackToBoneIndices = { 0, 1, 2 },
        };

        // What a rotation-only track looks like coming out of either decoder: translation present in
        // the list, zero in value, and flagged as not driven.
        for (int i = 0; i < 3; i++)
        {
            var track = new HkxTrackData { RotationAnimated = true };
            track.Translations.Add(Vector3.Zero);
            track.Rotations.Add(Quaternion.Identity);
            track.Scales.Add(Vector3.One);
            anim.Tracks.Add(track);
        }

        var posed = AnimationPose.At(rig, anim, 0);
        CheckTrue("the middle bone keeps its offset rather than collapsing",
                  Near(posed.Bones[1].Position, new Vector3(10, 0, 0)));
        CheckTrue("and so does the tip", Near(posed.Bones[2].Position, new Vector3(20, 0, 0)));
        Check("which is the reference pose exactly", 0f, AnimationPose.Distance(posed, AnimationPose.ReferencePose(rig)));

        // And the opposite: a driven translation of zero really does mean the origin.
        anim.Tracks[1] = FullTrack((Vector3.Zero, Quaternion.Identity));
        var collapsed = AnimationPose.At(rig, anim, 0);
        CheckTrue("a driven zero translation is honoured",
                  Near(collapsed.Bones[1].Position, Vector3.Zero));
    }

    /// Spline compression is the one format that says outright what an undriven channel means, and it
    /// is not the reference pose: no translation, no rotation, unit scale. On a whole body clip the
    /// two answers coincide, because the bones such a clip leaves undriven are the ones already at
    /// zero. On an additive clip they do not, and Havok's reading is the one that makes it a delta.
    private static void SplineUndrivenChannelsReadAsIdentity()
    {
        Console.WriteLine("\nspline compression reads an undriven channel as identity, not the rest pose");

        var rig = ThreeBoneChain();
        var anim = new HkxAnimationData
        {
            NumFrames = 1,
            NumTracks = 3,
            FrameDuration = 1f / 30f,
            AnimationClass = "hkaSplineCompressedAnimation",
            TrackToBoneIndices = { 0, 1, 2 },
        };

        for (int i = 0; i < 3; i++) anim.Tracks.Add(new HkxTrackData { RotationAnimated = true });

        var posed = AnimationPose.At(rig, anim, 0);
        CheckTrue("every bone folds onto the root, because none of them is given an offset",
                  Near(posed.Bones[1].Position, Vector3.Zero) && Near(posed.Bones[2].Position, Vector3.Zero));

        // The same track shape in a format that has not been shown to mean that is left alone.
        anim.AnimationClass = "hkaLosslessCompressedAnimation";
        var kept = AnimationPose.At(rig, anim, 0);
        CheckTrue("and a format without that guarantee still keeps the rest pose",
                  Near(kept.Bones[1].Position, new Vector3(10, 0, 0)));
    }

    /// Written by hand rather than read from a file, so it runs anywhere: the real proof is
    /// `symrm packfile`, which rebuilds every vanilla .hkx and compares the bytes. What this pins is
    /// the part that has no second opinion in a byte comparison, namely that a section whose
    /// contents are not a multiple of the padding still lands its later tables where its header says
    /// they are.
    private static void APackfileSurvivesBeingRebuilt()
    {
        Console.WriteLine("\na packfile taken apart and rebuilt says the same thing");

        var image = new PackfileImage { Predicates = new byte[16] };
        var section = new PackfileSection
        {
            // 20 bytes, name then the 0xFF the header is filled with, as a real one has.
            TagBytes = MakeTag("__data__"),
            Data = new byte[100],                    // deliberately not a multiple of 16
            LocalFixups = Pair(8, 40),
            GlobalFixups = Triple(16, 2, 64),
            VirtualFixups = Triple(24, 0, 3),
        };
        image.Sections.Add(section);

        var reread = PackfileImage.Read(image.Rebuild());
        CheckTrue("one section survives", reread.Sections.Count == 1);
        Check("named the same", "__data__", reread.Sections[0].Tag);
        // Not 100: the data is padded up to the boundary before the first table, and the offset that
        // says where the data ends is recorded after that padding, so the padding reads back as part
        // of the data. That is the format's own doing and not a loss, since the padding is 0xFF and
        // nothing points into it.
        Check("the odd sized data comes back padded to the boundary", 112, reread.Sections[0].Data.Length);
        Check("the bytes before the section headers survive", 16, reread.Predicates.Length);

        var local = reread.Sections[0].Locals().ToList();
        Check("one local fixup", 1, local.Count);
        Check("pointing where it did", 40, local[0].Destination);

        var virtuals = reread.Sections[0].Virtuals().ToList();
        Check("one virtual fixup", 1, virtuals.Count);
        Check("naming section 0, which is always __classnames__", 0, virtuals[0].Section);

        // Rebuilding twice must not drift: the second pass reads its own output, so any offset that
        // is computed from the wrong base shows up as a difference here rather than in the game.
        byte[] once = image.Rebuild();
        byte[] twice = PackfileImage.Read(once).Rebuild();
        CheckTrue("rebuilding what was rebuilt gives the same bytes", once.SequenceEqual(twice));
    }

    /// Renaming an animation is the commonest edit there is and was the one thing a value save could
    /// not do, because the new name is rarely the length of the old one. Built by hand so it runs
    /// without a game: the corpus proof is `symrm savecheck`, which does this to real files and asks
    /// hkxpack whether the result still reads.
    private static void AStringIsWrittenAtWhateverLength()
    {
        Console.WriteLine("\na string is written at whatever length it wants to be");

        var image = ClipInAPackfile("A.hkx", out int nameField);
        var objects = new PackfileObjects(image);
        var clip = objects.Instances.Single();

        Check("the clip is found by its class", "hkbClipGenerator", clip.ClassName);
        Check("its animation reads back", "A.hkx", objects.ReadString(clip, "animationName"));

        const string longer = @"Animations\Dogmeat\WalkForward_Rebuilt.hkx";
        CheckTrue("a longer name is accepted", objects.WriteString(clip, "animationName", longer));
        // The pointer had nothing in it at all, which is how the file leaves a name it never set.
        CheckTrue("a name the file left empty is accepted",
                  objects.WriteString(clip, "animationBundleName", "bundle"));

        var reread = new PackfileObjects(PackfileImage.Read(image.Rebuild()));
        var again = reread.Instances.Single();

        Check("the longer name survives the rebuild", longer, reread.ReadString(again, "animationName"));
        Check("so does the name that was empty", "bundle", reread.ReadString(again, "animationBundleName"));
        Check("the object did not move", 0, again.Offset);

        // The value beside the string is the check that the append did not land on top of anything:
        // a write that grew into the object rather than past it would take this with it.
        Check("the value next to it is untouched", 2.5f, reread.ReadFloat(again, "playbackSpeed"));

        // A shorter name has the opposite failure: read back over the old bytes it would come back
        // with the tail of what was there before still attached.
        var second = ClipInAPackfile("LongAnimationName.hkx", out _);
        var writing = new PackfileObjects(second);
        writing.WriteString(writing.Instances.Single(), "animationName", "B.hkx");

        var shorter = new PackfileObjects(PackfileImage.Read(second.Rebuild()));
        Check("a shorter name reads back as itself", "B.hkx",
              shorter.ReadString(shorter.Instances.Single(), "animationName"));

        var sources = image.Section("__data__")!.Locals().Select(l => l.Source).ToList();
        CheckTrue("the name field still has exactly one fixup",
                  sources.Count(s => s == nameField) == 1);
        Check("and the field that had none gained one, rather than the table being rebuilt", 2,
              sources.Count);
    }

    /// A field wider than four bytes, and one that is several floats in a row. Both were being read
    /// as an int or not at all, which is right only while the bytes above the first four happen to
    /// be zero. `hkbNode.userData` is the common one: 430 of Dogmeat's 906 objects carry it.
    private static void WideAndVectorFieldsReadFromTheBytes()
    {
        Console.WriteLine("\neight byte and vector fields read from the bytes");

        var classes = HavokClasses.Shipped;
        int userData = classes.Field("hkbClipGenerator", "userData")!.Offset;
        int motion = classes.Field("hkbClipGenerator", "extractedMotion")!.Offset;

        var image = ClipInAPackfile("A.hkx", out _);
        var data = image.Section("__data__")!.Data;

        // A value with both halves set, which is the case an int read gets wrong.
        BitConverter.GetBytes(0x0123_4567_89AB_CDEFUL).CopyTo(data, userData);
        for (int i = 0; i < 12; i++) BitConverter.GetBytes(i + 0.5f).CopyTo(data, motion + i * 4);

        var objects = new PackfileObjects(image);
        var clip = objects.Instances.Single();

        Check("the whole eight bytes are read", 0x0123_4567_89AB_CDEFUL,
              objects.ReadULong(clip, "userData"));
        Check("reading it as an int would have lost the top half", 0x89ABCDEF,
              unchecked((uint)objects.ReadInt(clip, "userData")!.Value));

        var transform = objects.ReadFloats(clip, "extractedMotion", 12);
        Check("a transform is twelve floats", 12, transform?.Length);
        Check("in the order they sit in", 0.5f, transform?[0]);
        Check("to the end", 11.5f, transform?[11]);

        // Past the end of the object rather than into the next one: a short read has to say so.
        Check("a run that does not fit is refused rather than cut short", null,
              objects.ReadFloats(clip, "extractedMotion", 4096));
    }

    /// A reference from one object to another is a global fixup, not a local one, even when both
    /// objects sit in the same section. Reading only the local table finds every string and no
    /// reference at all, which reads as a file where nothing points at anything.
    private static void ReferencesAndArraysReadFromTheBytes()
    {
        Console.WriteLine("\nreferences and arrays read from the bytes");

        var classes = HavokClasses.Shipped;
        int size = classes["hkbClipGenerator"]!.Size;
        int binding = classes.Field("hkbClipGenerator", "variableBindingSet")!.Offset;
        int triggers = classes.Field("hkbClipGenerator", "triggers")!.Offset;

        var image = ClipInAPackfile("A.hkx", out _);
        var data = image.Section("__data__")!;

        // A second object of the same class, so a reference has somewhere real to land.
        int second = data.AppendData(new byte[size]);
        data.VirtualFixups = data.VirtualFixups
            .Concat(Triple(second, 0, 5)).ToArray();

        // The reference itself, and a two element array of them.
        data.GlobalFixups = Triple(binding, 1, second);
        int list = data.AppendData(new byte[16]);
        var arrayHeader = new byte[16];
        BitConverter.GetBytes(2).CopyTo(arrayHeader, 8);
        int header = data.AppendData(arrayHeader);
        // Only the second element gets a pointer. The first is left without one, which is how the
        // format spells a null element, rather than pointed at offset zero, which is a real object.
        data.GlobalFixups = data.GlobalFixups.Concat(Triple(list + 8, 1, second)).ToArray();
        data.SetLocal(triggers, list);
        BitConverter.GetBytes(2).CopyTo(data.Data, triggers + 8);
        _ = header;

        var objects = new PackfileObjects(image);
        Check("both objects are found", 2, objects.Instances.Count);

        var clip = objects.Instances[0];
        var target = objects.ReadRef(clip, "variableBindingSet", out bool wasNull);
        CheckTrue("a reference is not read as null", !wasNull);
        Check("and it names the object it points at", objects.Instances[1]?.Offset, target?.Offset);

        var absent = objects.ReadRef(clip, "mapperData", out bool nothingThere);
        CheckTrue("a field with no pointer reads as null rather than as unresolved", nothingThere);
        Check("with nothing named", null, absent);

        var elements = objects.ReadRefArray(clip, "triggers");
        Check("an array reports its own count", 2, elements?.Count);
        Check("an element pointing nowhere is null", null, elements?[0]);
        Check("an element pointing at an object names it", objects.Instances[1]?.Offset,
              elements?[1]?.Offset);
    }

    /// A value the class table does not declare has no name, and has to read as "no name" rather
    /// than as an invented one: a wrong name is the kind of wrong nobody checks.
    ///
    /// These names used to be measured off vanilla files, one field at a time, and kept in a table
    /// of their own. The class table declares them instead, 1,007 values against the measurement's
    /// 47, and the two agreed on all 47 before the measurement was removed.
    private static void AnUndeclaredEnumValueIsNotNamed()
    {
        Console.WriteLine("\nan enum value the table does not declare is left unnamed");

        var types = HavokClassTypes.Shipped;
        var mode = types.Members("hkbClipGenerator").First(m => m.Name == "mode");

        Check("the field names the enum that gives its values names", "PlaybackMode", mode.EType);
        Check("a declared value is named", "MODE_SINGLE_PLAY",
              types.NameOf("hkbClipGenerator", mode, 0));
        Check("an undeclared one is not", null, types.NameOf("hkbClipGenerator", mode, 99));
        Check("neither is a field whose enum the table has never heard of", null,
              types.NameOf("hkbNothing", new HavokClassTypes.Member { EType = "Nowhere" }, 0));

        // Flags combine, and a combination is only as good as its parts.
        var flags = types.Members("hkbBlendingTransitionEffect").First(m => m.Name == "flags");

        Check("a single flag is named", "FLAG_SYNC",
              types.NameOf("hkbBlendingTransitionEffect", flags, 2));
        Check("so is a combination of declared flags", "FLAG_SYNC|FLAG_IGNORE_TO_WORLD_FROM_MODEL",
              types.NameOf("hkbBlendingTransitionEffect", flags, 6));
        Check("a combination holding a bit with no name is refused whole", null,
              types.NameOf("hkbBlendingTransitionEffect", flags, 6 | 1 << 20));
    }

    /// The panel reads from the bytes and falls back to hkxpack for one field at a time. What must
    /// not happen is the third thing: reading a field off the bytes that is not that object's field
    /// and showing the answer as though it were.
    /// The panel's list of names used to be hkxpack's list of names. It is the class table's now,
    /// and hkxpack is left holding one thing: a value to fall back to, field by field.
    private static void ThePanelReadsItsListFromTheTable()
    {
        Console.WriteLine("\nthe panel reads its list from the table");

        var image = ClipInAPackfile("A.hkx", out _);
        var objects = new PackfileObjects(image);
        var clip = objects.Instances.Single();

        var names = ClassFields.NamesOf(objects, clip)!;
        CheckTrue("the list holds the fields hkxpack writes",
                  names.Contains("animationName") && names.Contains("playbackSpeed"));
        CheckTrue("and not the running state it does not",
                  !names.Contains("localTime") && !names.Contains("atEnd"));

        // Every value hkxpack's side could offer is wrong on purpose. What comes back says which
        // side was read.
        var xml = names.Select(n => (n, "from-hkxpack")).ToList();
        var fields = PanelFields.For(objects, clip, xml, (_, wasNull) => wasNull ? "null" : "");

        Check("one field per name in the table's list", names.Count, fields.Count);
        Check("the name comes from the bytes", "A.hkx",
              fields[names.IndexOf("animationName")].Value);
        Check("and so does a number the text disagrees with", "2.5",
              fields[names.IndexOf("playbackSpeed")].Value);
        Check("a null string is an empty box rather than a symbol", "",
              fields[names.IndexOf("animationBundleName")].Value);
        Check("nothing fell back to hkxpack", 0,
              fields.Count(f => f.From == PanelFields.Source.Fallback));

        // An edit lives in the text form until it is saved, so for that one field the text is newer
        // than the bytes and has to win, or typing would be undone by the next redraw.
        var edited = PanelFields.For(objects, clip, xml, (_, _) => "",
                                     new HashSet<string> { "playbackSpeed" });
        int speed = names.IndexOf("playbackSpeed");
        Check("an edited field shows the edit, not the bytes", "from-hkxpack", edited[speed].Value);
        Check("and says so", PanelFields.Source.Edited, edited[speed].From);

        // The load path puts the byte reader aside when it cannot trust it; this is the same idea
        // one level down. Two lists that do not line up means one of them is wrong about this file.
        var short_ = PanelFields.For(objects, clip, xml.Take(3).ToList(), (_, _) => "");
        Check("a list that does not line up with hkxpack's degrades to hkxpack's", 3, short_.Count);
        Check("and reads none of it from the bytes", 3,
              short_.Count(f => f.From == PanelFields.Source.Fallback));
    }

    /// A value is XML. `cond(x &gt; 0.0, 1.0, -1.0)` is an expression with a greater than sign in
    /// it, and it was being shown with the escape still in it, which is not what it says.
    private static void AnEscapedValueIsShownAsItself()
    {
        Console.WriteLine("\nan escaped value is shown as itself");

        const string xml =
            "<hkobject class=\"hkbExpressionDataArray\" name=\"#90\" signature=\"0x0\">\n" +
            "\t<hkparam name=\"expression\">a &gt; b &amp;&amp; c</hkparam>\n" +
            "</hkobject>\n";

        var read = HkxTextEdit.ReadParams(xml, "90");
        Check("the escape is undone on the way in", "a > b && c", read[0].Value);

        string written = HkxTextEdit.SetParam(xml, "90", "expression", "x < y & z");
        CheckTrue("and put back on the way out", written.Contains("x &lt; y &amp; z"));
        Check("so a round trip gives back what was typed", "x < y & z",
              HkxTextEdit.ReadParams(written, "90")[0].Value);

        // Left alone, this wrote a file no XML reader would take back, which is worse than showing
        // the escape.
        CheckTrue("and the file stays readable", written.Contains("&amp;&amp;") == false);
    }

    /// Four state machines and a layer generator in vanilla are named with a leading space, and one
    /// event payload ends in one. A reader that tidies them up is not reading the file, and a check
    /// that tidies them up on both sides cannot see the difference either way.
    private static void ASpaceInAValueIsKept()
    {
        Console.WriteLine("\na space in a value is kept");

        var image = ClipInAPackfile(" StateMachine00 ", out _);
        var objects = new PackfileObjects(image);
        var clip = objects.Instances.Single();

        Check("both ends survive the read", " StateMachine00 ",
              objects.ReadString(clip, "animationName"));

        var names = ClassFields.NamesOf(objects, clip)!;
        var shown = PanelFields.For(objects, clip, names.Select(n => (n, "tidied")).ToList(),
                                    (_, _) => "");
        Check("and the panel shows what the file holds rather than the tidied text",
              " StateMachine00 ", shown[names.IndexOf("animationName")].Value);

        // A number in an array is spelled the way a number on its own is. hkxpack prints the bytes
        // as they sit, so 0xFFFF is 65535 in both places; -1 in one and 65535 in the other agrees
        // with neither.
        var parents = HavokClasses.Shipped.Field("hkaSkeleton", "parentIndices");
        CheckTrue("a skeleton's parent indices are an array of int16", parents?.Type == "array of int16");
    }

    /// The two halves of a class description, and what each one is for. The dump read out of the
    /// game knows where a field sits and how big an instance is; hkxpack's database knows which
    /// fields are ever written, what an inline struct is an instance of, and what an enum's numbers
    /// are called. Neither is enough on its own.
    private static void TheClassTableKnowsWhatTheDumpCannot()
    {
        Console.WriteLine("\nthe class table knows what the dump cannot");

        var types = HavokClassTypes.Shipped;
        CheckTrue("the table is there at all", types.Count > 900);

        var clip = types["hkbClipGenerator"]!;
        Check("a signature, which the dump has none of", 0xd4cc9f6u, clip.Signature);
        Check("and a size, which hkxpack has none of", 352, clip.Size);
        Check("the same size the dump gives", HavokClasses.Shipped["hkbClipGenerator"]!.Size, clip.Size);

        var members = types.Members("hkbClipGenerator");
        Check("inherited members come first, in the order they are declared", "memSizeAndRefCount",
              members[0].Name);
        CheckTrue("and the class's own come after its parent's",
                  members.ToList().FindIndex(m => m.Name == "animationName") >
                  members.ToList().FindIndex(m => m.Name == "name"));

        var mode = members.Single(m => m.Name == "mode");
        Check("an enum member names its enum", "PlaybackMode", mode.EType);
        Check("and the values have names", "MODE_USER_CONTROLLED", types.NameOf("hkbClipGenerator", mode, 2));
        Check("a value nothing declares stays unnamed", null, types.NameOf("hkbClipGenerator", mode, 99));

        // The fact the whole table exists for: what an inline struct is an instance of.
        var transitions = types.Members("hkbStateMachineTransitionInfoArray")
                               .Single(m => m.Name == "transitions");
        Check("an array of structs names the class of its elements", "hkbStateMachineTransitionInfo",
              transitions.CType);
        Check("which has a size, so the elements can be stepped through", 72,
              types["hkbStateMachineTransitionInfo"]!.Size);

        var ignored = types.Members("hkbStateMachineTransitionInfoArray")
                           .Where(m => !m.Written).Select(m => m.Name).ToList();
        CheckTrue("and the members the engine never writes are marked",
                  ignored.Contains("hasEventlessTransitions"));

        // Flags combine, and a combination is only as good as its parts.
        var flags = types.Members("hkbBlendingTransitionEffect").Single(m => m.Name == "flags");
        Check("flags read as their names", "FLAG_SYNC|FLAG_IGNORE_TO_WORLD_FROM_MODEL",
              types.NameOf("hkbBlendingTransitionEffect", flags, 6));
        Check("a combination holding a bit with no name is refused whole", null,
              types.NameOf("hkbBlendingTransitionEffect", flags, 6 | 1 << 20));
    }

    /// The list of fields an object holds, built from the table and the file rather than from
    /// hkxpack's text. The corpus proof is `symrm fields`; this pins the shape of the walk.
    private static void AFieldListIsBuiltWithoutHkxPack()
    {
        Console.WriteLine("\na field list is built without hkxpack");

        var image = ClipInAPackfile("A.hkx", out _);
        var objects = new PackfileObjects(image);
        var names = ClassFields.NamesOf(objects, objects.Instances.Single());

        CheckTrue("a list comes back at all", names != null);
        CheckTrue("it holds the fields hkxpack writes", names!.Contains("animationName") &&
                                                        names.Contains("playbackSpeed"));
        CheckTrue("and not the ones it never writes", !names.Contains("localTime") &&
                                                      !names.Contains("atEnd"));
        // triggers is a pointer, written as a reference on one line; animDatas is an array, written
        // as its own block and never offered as a value.
        CheckTrue("a pointer is a field", names.Contains("triggers"));
        CheckTrue("an array is not", !names.Contains("animDatas"));

        var order = HavokClassTypes.Shipped.Members("hkbClipGenerator")
                                   .Where(m => m.Written && m.VType != "TYPE_ARRAY" &&
                                               m.VType != "TYPE_STRUCT")
                                   .Select(m => m.Name).ToList();
        Check("in the order the file writes them", string.Join(",", order), string.Join(",", names));
    }

    /// A file whose classes are signed differently was written against a different definition than
    /// the one this build holds, and reading a value out of it by offset would be quiet nonsense.
    private static void AClassSignedDifferentlyIsRefused()
    {
        Console.WriteLine("\na class signed differently is refused");

        var types = HavokClassTypes.Shipped;
        uint right = types["hkbClipGenerator"]!.Signature;

        Check("a file signed the way we expect raises nothing", 0,
              types.SignatureProblems(new[] { (right, "hkbClipGenerator") }).Count);

        var wrong = types.SignatureProblems(new[] { (right ^ 1u, "hkbClipGenerator") });
        Check("one signed differently raises exactly one", 1, wrong.Count);
        CheckTrue("and the message names the class", wrong[0].Contains("hkbClipGenerator"));

        var unknown = types.SignatureProblems(new[] { (1u, "hkbSomethingWeHaveNeverSeen") });
        Check("so does a class we have no definition for", 1, unknown.Count);

        // The file the test packfile is built from carries its own names, and they have to pass.
        var image = ClipInAPackfile("A.hkx", out _);
        Check("the names a real packfile carries pass", 0,
              types.SignatureProblems(new PackfileObjects(image).ClassNames()).Count);
    }

    /// Refusing to *read* a file whose classes we do not describe is the smaller half. Writing into
    /// one is the half that does damage: every offset written comes from this build's idea of the
    /// class, so a value would land in somebody else's field and the file would still look valid.
    private static void AMisSignedFileIsNotWrittenInto()
    {
        Console.WriteLine("\na file signed for other classes is not written into");

        string good = Path.Combine(Path.GetTempPath(), "symrm-signed-right.hkx");
        string bad = Path.Combine(Path.GetTempPath(), "symrm-signed-wrong.hkx");

        ClipInAPackfile("A.hkx", out _).Save(good);

        // The same file with one bit of one signature turned over, which is what a class whose
        // members moved would look like.
        var wrong = ClipInAPackfile("A.hkx", out _);
        var names = wrong.Section("__classnames__")!;
        names.Data[0] ^= 0x01;
        wrong.Save(bad);

        var nothing = new NativeSave.Plan(new List<NativeSave.Change>(), null);
        CheckTrue("a plan that changes nothing is possible either way", nothing.Possible);

        try
        {
            NativeSave.Apply(good, nothing);
            CheckTrue("a file signed the way we expect is written", true);
        }
        catch (Exception e)
        {
            CheckTrue("a file signed the way we expect is written: " + e.Message, false);
        }

        try
        {
            NativeSave.Apply(bad, nothing);
            CheckTrue("a file signed for other classes is refused", false);
        }
        catch (InvalidOperationException e)
        {
            CheckTrue("a file signed for other classes is refused", true);
            CheckTrue("and the refusal names the class", e.Message.Contains("hkbClipGenerator"));
            CheckTrue("and says nothing was written",
                      e.Message.Contains("nothing was written"));
        }

        File.Delete(good);
        File.Delete(bad);
    }

    /// A byte of 0xFF in an enum of int8 is -1 to whoever declared the names and 255 to whoever
    /// prints the bytes. Both are the same byte, and a reading that picks one loses either the name
    /// or the comparison.
    private static void AnEnumIsNamedSignedAndPrintedUnsigned()
    {
        Console.WriteLine("\nan enum is named signed and printed unsigned");

        var types = HavokClassTypes.Shipped;
        var type = types.Members("hkbVariableInfo").Single(m => m.Name == "type");
        Check("the declaration really does go negative", "VARIABLE_TYPE_INVALID",
              types.NameOf("hkbVariableInfo", type, -1));

        var image = ClipInAPackfile("A.hkx", out _);
        var objects = new PackfileObjects(image);
        var clip = objects.Instances.Single();
        int mode = types.Members("hkbClipGenerator").Single(m => m.Name == "mode").Offset;
        var member = types.Members("hkbClipGenerator").Single(m => m.Name == "mode");
        var data = image.Section("__data__")!.Data;

        data[clip.Offset + mode] = 2;
        Check("a value with a name reads as its name", "2:MODE_USER_CONTROLLED",
              FieldRender.Render(objects, clip.Offset + mode, "hkbClipGenerator", member, (_, _) => ""));

        // 0xFF is not one of the playback modes, so there is no name and only the number is left.
        // Printed the way hkxpack prints it, or the same byte would read as a difference.
        data[clip.Offset + mode] = 0xFF;
        Check("a value with none reads as the byte, unsigned", "255",
              FieldRender.Render(objects, clip.Offset + mode, "hkbClipGenerator", member,
                                 (_, _) => "", "255"));
    }

    /// Where hkxpack is wrong rather than us, and how it is told apart from where we are.
    private static void APaddedStructIsKnownFromHkxPacksIdeaOfIt()
    {
        Console.WriteLine("\na padded struct is known from hkxpack's idea of it");

        var types = HavokClassTypes.Shipped;

        // 16 aligned because it holds vectors and transforms: the game says an instance is 528
        // bytes, and the end of its last member rounded up to eight is 520. Every element after the
        // first of an array of these is somewhere hkxpack does not look.
        Check("the game's size for the bone data", 528, types["BSLookAtModifierBoneData"]!.Size);
        CheckTrue("and it is padded past what hkxpack would work out",
                  types.PaddedBeyondHkxPack("BSLookAtModifierBoneData"));

        // 8 aligned, so both arrive at 72 and every one of the 36,340 field lists agreed.
        Check("a struct with nothing wider than a pointer", 72,
              types["hkbStateMachineTransitionInfo"]!.Size);
        CheckTrue("is not padded past it",
                  !types.PaddedBeyondHkxPack("hkbStateMachineTransitionInfo"));

        // Neither is a class smaller than the rounding itself. hkbVariableInfo is six bytes, which
        // is neither eight nor sixteen, and hkxpack strides it perfectly well: 309 arrays of them
        // in the vanilla corpus agree. Calling it padded would let a real disagreement in any of
        // those pass as somebody else's fault, which is worse than not checking.
        Check("a class smaller than the rounding itself", 6, types["hkbVariableInfo"]!.Size);
        CheckTrue("is not called padded", !types.PaddedBeyondHkxPack("hkbVariableInfo"));
        Check("nor is a four byte one", 4, types["hkbEventInfo"]!.Size);
        CheckTrue("either", !types.PaddedBeyondHkxPack("hkbEventInfo"));
    }

    /// One hkbClipGenerator in a packfile of two sections, which is the least a reader needs: a name
    /// in __classnames__ for the virtual fixup to point at, and the object itself in __data__.
    private static PackfileImage ClipInAPackfile(string animation, out int nameField)
    {
        var classes = HavokClasses.Shipped;
        int size = classes["hkbClipGenerator"]!.Size;
        nameField = classes.Field("hkbClipGenerator", "animationName")!.Offset;
        int speed = classes.Field("hkbClipGenerator", "playbackSpeed")!.Offset;

        // Five bytes of bookkeeping precede a class name: the class signature, then a separator.
        // The real signature rather than zeroes, because a file carrying the wrong one is refused,
        // and a fixture that could not survive its own checks is not a fixture.
        var names = new byte[5 + "hkbClipGenerator".Length + 1];
        BitConverter.GetBytes(HavokClassTypes.Shipped["hkbClipGenerator"]!.Signature).CopyTo(names, 0);
        names[4] = 0x09;
        System.Text.Encoding.ASCII.GetBytes("hkbClipGenerator").CopyTo(names, 5);

        var text = System.Text.Encoding.UTF8.GetBytes(animation);
        var data = new byte[size + text.Length + 1];
        text.CopyTo(data, size);
        BitConverter.GetBytes(2.5f).CopyTo(data, speed);

        var image = new PackfileImage();
        image.Sections.Add(new PackfileSection { TagBytes = MakeTag("__classnames__"), Data = names });
        image.Sections.Add(new PackfileSection
        {
            TagBytes = MakeTag("__data__"),
            Data = data,
            LocalFixups = Pair(nameField, size),
            VirtualFixups = Triple(0, 0, 5),
        });
        return image;
    }

    private static byte[] MakeTag(string name)
    {
        var tag = new byte[20];
        Array.Fill(tag, (byte)0xFF);
        var ascii = System.Text.Encoding.ASCII.GetBytes(name);
        Array.Copy(ascii, tag, ascii.Length);
        tag[ascii.Length] = 0;
        return tag;
    }

    private static byte[] Pair(int source, int destination) =>
        BitConverter.GetBytes(source).Concat(BitConverter.GetBytes(destination)).ToArray();

    private static byte[] Triple(int source, int section, int destination) =>
        BitConverter.GetBytes(source)
            .Concat(BitConverter.GetBytes(section))
            .Concat(BitConverter.GetBytes(destination)).ToArray();

    private static void ScrubbingLandsOnDifferentPoses()
    {
        Console.WriteLine("\nscrubbing to different frames gives different poses");

        var rig = ThreeBoneChain();
        var half = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI);
        var anim = new HkxAnimationData
        {
            NumFrames = 3,
            NumTracks = 3,
            FrameDuration = 0.5f,
            Duration = 1.0f,
            TrackToBoneIndices = { 0, 1, 2 },
        };
        anim.Tracks.Add(FullTrack(
            (Vector3.Zero, Quaternion.Identity),
            (Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)),
            (Vector3.Zero, half)));
        for (int i = 0; i < 2; i++)
            anim.Tracks.Add(FullTrack(
                (new Vector3(10, 0, 0), Quaternion.Identity),
                (new Vector3(10, 0, 0), Quaternion.Identity),
                (new Vector3(10, 0, 0), Quaternion.Identity)));

        var first = AnimationPose.At(rig, anim, 0);
        var last = AnimationPose.At(rig, anim, anim.NumFrames - 1);

        CheckTrue("frame 0 and the last frame are not the same pose", AnimationPose.Distance(first, last) > 1f);
        CheckTrue("frame 0 is the chain along X", Near(first.Bones[2].Position, new Vector3(20, 0, 0)));
        CheckTrue("the last frame has turned it right around", Near(last.Bones[2].Position, new Vector3(-20, 0, 0)));

        Check("the frame number is carried on the pose", 2, last.Frame);
        Check("with the time that frame plays at", 1.0f, last.Time);

        // Scrubbing past either end lands on an end rather than throwing or drawing nothing.
        Check("scrubbing before the start clamps", 0, AnimationPose.At(rig, anim, -5).Frame);
        Check("and past the end clamps", 2, AnimationPose.At(rig, anim, 99).Frame);
        Check("clamped low is the same pose as frame 0", 0f, AnimationPose.Distance(AnimationPose.At(rig, anim, -5), first));

        // The scrub bar is driven by the fraction a clip generator uses, so the two have to agree on
        // which frame that is.
        Check("the halfway fraction lands on the middle frame", 1, anim.FrameAt(0.5f));
        CheckTrue("and that frame is neither end",
                  AnimationPose.Distance(AnimationPose.At(rig, anim, anim.FrameAt(0.5f)), first) > 1f);
    }

    private static void TracksDriveTheBonesTheyName()
    {
        Console.WriteLine("\ntracks drive the bones they name, not the bones in order");

        var rig = ThreeBoneChain();
        var anim = new HkxAnimationData { NumFrames = 1, NumTracks = 1, FrameDuration = 1f / 30f };

        // One track, and it names the last bone. Driving bone 0 from track 0 would move the whole
        // chain instead of the tip, which is the failure this mapping exists to prevent.
        anim.TrackToBoneIndices.Add(2);
        anim.Tracks.Add(FullTrack((new Vector3(0, 5, 0), Quaternion.Identity)));

        var byBone = AnimationPose.TracksByBone(rig, anim);
        Check("the root is driven by nothing", -1, byBone[0]);
        Check("the middle bone too", -1, byBone[1]);
        Check("and the tip by track 0", 0, byBone[2]);

        var posed = AnimationPose.At(rig, anim, 0);
        CheckTrue("the undriven bones sit where the skeleton puts them",
                  Near(posed.Bones[1].Position, new Vector3(10, 0, 0)));
        CheckTrue("and only the named bone moved", Near(posed.Bones[2].Position, new Vector3(10, 5, 0)));

        // No mapping in the file at all. One track per bone in order is the only reading left, and it
        // is only safe while the counts agree.
        var unnamed = new HkxAnimationData { NumFrames = 1, NumTracks = 1, FrameDuration = 1f / 30f };
        unnamed.Tracks.Add(FullTrack((Vector3.Zero, Quaternion.Identity)));
        CheckTrue("one track and three bones with no mapping drives nothing",
                  AnimationPose.TracksByBone(rig, unnamed).All(t => t == -1));

        var matched = new HkxAnimationData { NumFrames = 1, NumTracks = 3, FrameDuration = 1f / 30f };
        for (int i = 0; i < 3; i++) matched.Tracks.Add(FullTrack((Vector3.Zero, Quaternion.Identity)));
        CheckTrue("matching counts with no mapping fall back to order",
                  AnimationPose.TracksByBone(rig, matched).SequenceEqual(new[] { 0, 1, 2 }));
    }

    // A shared behaviour naming an animation authored for another creature is the ordinary case, not
    // a broken file, so this has to say which rig rather than refuse in the abstract.
    private static void AnimationsForAnotherRigAreRefused()
    {
        Console.WriteLine("\nan animation for another rig says so rather than drawing a wrong pose");

        var rig = ThreeBoneChain();
        var good = new HkxAnimationData { NumFrames = 2, NumTracks = 1, TrackToBoneIndices = { 1 } };
        good.Tracks.Add(FullTrack((Vector3.Zero, Quaternion.Identity), (Vector3.Zero, Quaternion.Identity)));
        Check("an animation this rig can carry is not refused", null, AnimationPose.WhyNotPosable(rig, good));

        var wrongRig = new HkxAnimationData { NumFrames = 2, NumTracks = 1, TrackToBoneIndices = { 40 } };
        wrongRig.Tracks.Add(FullTrack((Vector3.Zero, Quaternion.Identity), (Vector3.Zero, Quaternion.Identity)));
        string refused = AnimationPose.WhyNotPosable(rig, wrongRig) ?? "";
        CheckTrue("one driving a bone this rig does not have is refused", refused.Length > 0);
        CheckTrue("naming the bone it wanted", refused.Contains("40"));
        CheckTrue("and how many this rig has", refused.Contains("3"));

        CheckTrue("no skeleton at all says so plainly",
                  (AnimationPose.WhyNotPosable(null, good) ?? "").Contains("No skeleton"));
        CheckTrue("an animation that decoded to nothing says that instead",
                  (AnimationPose.WhyNotPosable(rig, new HkxAnimationData()) ?? "").Contains("no frames"));
    }

    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 0.001f;

    // A usage list is only navigable if every entry knows which object it is in. One that does not is
    // a row that looks clickable and goes nowhere.
    private static void EverySymbolUsageNamesItsObject()
    {
        Console.WriteLine("\nevery symbol usage names the object it sits in");

        string xml = EventGraph();
        var events = SymbolIndexFixup.Usages(xml, events: true);

        CheckTrue("the graph writes event indices at all", events.Count > 0);
        CheckTrue("and every one of them names an object", events.All(u => u.ObjectId.Length > 0));
        CheckTrue("with a member to go with it", events.All(u => u.Member.Length > 0));

        // The same walk the Symbols tab lists from, so a row that appears there is a row that resolves
        // back to the object it claims.
        foreach (var lines in EventUsage.ByEvent(xml).Values)
            CheckTrue("event rows carry the objects they came from", lines.All(l => l.ObjectIds.Count > 0));

        string first = events[0].ObjectId;
        var backwards = SymbolIndexFixup.UsagesOf(xml, events: true, first);
        CheckTrue("and the reverse lookup finds the same site", backwards.Any(u => u.Index == events[0].Index));
        CheckTrue("without straying into other objects", backwards.All(u => u.ObjectId == first));
    }

    // The point of this one is what it does NOT say. A name no script sends is the ordinary case,
    // because the engine sends events itself, so the wording must not read as a fault.
    private static void PapyrusSendersAreReportedNotJudged()
    {
        Console.WriteLine("\npapyrus senders are reported, never judged");

        string folder = Path.Combine(Path.GetTempPath(), "bgs_psc_test");
        HkxTextEdit.ResetDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "DoorScript.psc"), """
            Scriptname DoorScript extends ObjectReference
            Event OnActivate(ObjectReference akActionRef)
                Self.PlayAnimation("OpenAnim")
                Self.PlayAnimationAndWait("CloseAnim", "doneClosing")
                Debug.Trace("OpenAnim is not sent from here")
            EndEvent
            """);

        var index = PapyrusEvents.Scan(folder);
        Check("the script was read", 1, index.ScriptsRead);
        Check("the animation it plays is attributed", "DoorScript.psc", index.Senders("OpenAnim").FirstOrDefault());
        Check("so is the wait event", "DoorScript.psc", index.Senders("doneClosing").FirstOrDefault());
        Check("a string that is only printed is not a send", 0, index.Senders("OpenAnim is not sent from here").Count);

        // Papyrus is case insensitive and the graphs are not consistent about it.
        Check("names match without case", "DoorScript.psc", index.Senders("openanim").FirstOrDefault());

        string quiet = PapyrusEvents.Describe(index, "somethingNobodySends");
        CheckTrue("an unsent name says only that nothing was found", quiet == "no sender found in the scanned scripts");
        CheckTrue("and never calls it dead, unused or broken",
                  !new[] { "dead", "unused", "broken", "wrong" }
                      .Any(w => quiet.Contains(w, StringComparison.OrdinalIgnoreCase)));
        Check("with no folder set, nothing is said at all", "", PapyrusEvents.Describe(new PapyrusEvents.Index(), "OpenAnim"));

        Directory.Delete(folder, true);
    }

    // Two mods editing one behaviour is the case this has to read cleanly: the ids will not line up,
    // and the object counts may not either.
    private static void TwoFilesDiffToWhatEachChanged()
    {
        Console.WriteLine("\ntwo files diff to what each one changed");

        string mine = SmallGraph();
        string theirs = Regex.Replace(mine, @"#(\d+)", m => "#" + (int.Parse(m.Groups[1].Value) + 400));

        var same = BehaviourDiff.Compare(RepackCheck.Take(mine), RepackCheck.Take(theirs));
        CheckTrue("renumbering alone is not a difference", same.Identical);

        string edited = theirs.Replace("<hkparam name=\"animationName\">b.hkx</hkparam>",
                                       "<hkparam name=\"animationName\">theirs.hkx</hkparam>");
        var changed = BehaviourDiff.Compare(RepackCheck.Take(mine), RepackCheck.Take(edited));
        Check("one changed value is one line", 1, changed.Changed);
        Check("nothing was added", 0, changed.Added);
        Check("nothing was removed", 0, changed.Removed);
        CheckTrue("and it names the field", changed.Lines[0].Where == "animationName");
        CheckTrue("with both sides of it",
                  changed.Lines[0].Was == "b.hkx" && changed.Lines[0].Now == "theirs.hkx");

        // A whole object gone, so the two sequences are different lengths and have to resynchronise.
        string shortened = Regex.Replace(theirs,
            @"\s*<hkobject class=""hkbClipGenerator"" name=""#497""[\s\S]*?</hkobject>", "");
        var dropped = BehaviourDiff.Compare(RepackCheck.Take(mine), RepackCheck.Take(shortened));
        Check("a dropped object reads as one removal", 1, dropped.Removed);
        Check("and invents nothing", 0, dropped.Added);
        CheckTrue("naming what went", dropped.Lines.Any(l => l.Where == "Spare"));
    }

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

    // A behaviour never names its model, so the only safe answers are "exactly one candidate" and
    // "ask". These check the third case especially: several candidates must NOT resolve to one of
    // them, and must not fall through to a later folder either, since both are guesses wearing
    // different hats.
    private static void AModelIsFoundOnlyWhenThereIsNoDoubt()
    {
        var disk = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["/behaviours"] = Array.Empty<string>(),
            ["/project"] = new[] { "/project/Dogmeat.nif" },
            ["/crowded"] = new[] { "/crowded/b.nif", "/crowded/a.nif" },
            ["/assets"] = new[] { "/assets/skeleton_mesh.nif" },
        };
        IReadOnlyList<string> In(string folder) =>
            disk.TryGetValue(folder, out var files) ? files : Array.Empty<string>();

        var one = MeshLookup.Find(new[] { "/behaviours", "/project" }, In);
        CheckTrue("one model beside the file is used", one.Found);
        Check("and it is that model", "/project/Dogmeat.nif", one.Path);

        var none = MeshLookup.Find(new[] { "/behaviours", "/empty" }, In);
        CheckTrue("no model anywhere finds nothing", !none.Found);
        CheckTrue("and says to use the button", none.Reason.Contains("Mesh..."));

        var many = MeshLookup.Find(new[] { "/crowded", "/project" }, In);
        CheckTrue("several models resolve to none of them", !many.Found);
        CheckTrue("saying how many there were", many.Reason.Contains("2 models"));
        CheckTrue("and naming them", many.Reason.Contains("a.nif") && many.Reason.Contains("b.nif"));
        CheckTrue("and it does not fall through to the next folder", many.Path == null);

        // Nearest first, so a model beside the behaviour wins over one beside the skeleton.
        var nearest = MeshLookup.Find(new[] { "/project", "/assets" }, In);
        Check("the nearest folder decides", "/project/Dogmeat.nif", nearest.Path);

        var places = MeshLookup.Places("/x/Behaviors/Root.hkx", "/x", "/x/CharacterAssets/skeleton.hkt")
                               .ToList();
        Check("three places are searched", 3, places.Count);
        Check("the behaviour's own folder first", "/x/Behaviors", places[0]);
        Check("then the project root", "/x", places[1]);
        Check("then wherever the skeleton lives", "/x/CharacterAssets", places[2]);

        var deduped = MeshLookup.Places("/x/Root.hkx", "/x", "/x/skeleton.hkt").ToList();
        Check("one folder is searched once", 1, deduped.Count);
    }

    // A field's type says how wide the value is, not that what was typed is a value of that type.
    // Left unchecked the writer took whatever it could parse and wrote zero for the rest, so a
    // mistyped speed became a clip that does not play rather than an edit that was refused.
    private static void AValueThatIsNotANumberIsRefused()
    {
        const string Before = """
            <hkpackfile><hksection name="__data__">
            <hkobject name="#0010" class="hkbClipGenerator" signature="0x333b85b9">
                <hkparam name="playbackSpeed">1.0</hkparam>
                <hkparam name="userPartitionMask">0</hkparam>
                <hkparam name="ignoreStartTime">false</hkparam>
            </hkobject></hksection></hkpackfile>
            """;


        foreach (string rubbish in new[] { "abc", "1.5x", "1,5", "" })
        {
            var plan = NativeSave.Compare(Before, Before.Replace(">1.0<", $">{rubbish}<"));
            CheckTrue($"a playbackSpeed of '{rubbish}' is refused", !plan.Possible);
            CheckTrue($"and the refusal names the field, not '{rubbish}'",
                      plan.Refusal?.Contains("playbackSpeed", StringComparison.Ordinal) == true);
        }

        var good = NativeSave.Compare(Before, Before.Replace(">1.0<", ">0.25<"));
        CheckTrue("a real number is still accepted", good.Possible);
        Check("and is the only change", 1, good.Changes.Count);

        // Not a number, and not something to be quietly folded to zero either.
        foreach (string special in new[] { "NaN", "Infinity" })
            CheckTrue($"'{special}' is refused rather than written",
                      !NativeSave.Compare(Before, Before.Replace(">1.0<", $">{special}<")).Possible);

        // The write masks down to the field's width, so a number too big lands as its low bytes.
        var tooBig = NativeSave.Compare(Before, Before.Replace(">0<", ">99999999999<"));
        CheckTrue("a number too big for the field is refused", !tooBig.Possible);

        var fits = NativeSave.Compare(Before, Before.Replace(">0<", ">3<"));
        CheckTrue("one that fits is accepted", fits.Possible);
    }

    /// hkxpack is Java, and Java writes a float by widening it to a double. Every one of these was
    /// read out of a vanilla file rather than worked out from the rule, because the rule is a
    /// reading of Java's documentation and the file is the thing being matched.
    private static void AFloatIsSpelledTheWayHkxPackSpellsIt()
    {
        Console.WriteLine("\na float is spelled the way hkxpack spells it");

        // Plain, and always with a digit after the point. This is the whole of the first pass'
        // 2,397 disagreements on Dogmeat: shortest round trip says "1", the file says "1.0".
        Check("one", "1.0", HkxNumber.Text(1.0f));
        Check("zero", "0.0", HkxNumber.Text(0.0f));
        Check("a half", "0.5", HkxNumber.Text(0.5f));

        // Negative zero is in these files, in a vector on hkbFootIkModifier, and is not the same
        // text as zero.
        Check("negative zero", "-0.0", HkxNumber.Text(-0.0f));

        // The digits are the double's, not the float's, which is why there are seventeen of them.
        Check("a tenth", "0.10000000149011612", HkxNumber.Text(0.1f));
        Check("nine tenths", "0.8999999761581421", HkxNumber.Text(0.9f));
        Check("seven tenths", "0.699999988079071", HkxNumber.Text(0.7f));
        Check("two tenths", "0.20000000298023224", HkxNumber.Text(0.2f));
        Check("a negative", "-0.23399999737739563", HkxNumber.Text(-0.234f));

        // Below a thousandth Java switches to scientific notation, and these two are read straight
        // off hkbFootIkControlData.enabled1 and enabled2 in a vanilla alien behaviour.
        Check("a very small number", "3.8432640863340837E-34", HkxNumber.Text(3.8432640863340837E-34));
        Check("one small enough to be subnormal", "8.127531093083939E-44",
              HkxNumber.Text(8.127531093083939E-44));

        // The two edges of where Java stops writing plainly.
        Check("just inside the small edge", "0.001", HkxNumber.Text(0.001));
        Check("just outside it", "9.99E-4", HkxNumber.Text(0.000999));
        Check("just inside the large edge", "9999999.0", HkxNumber.Text(9999999.0));
        Check("just outside it", "1.0E7", HkxNumber.Text(1.0E7));

        Check("not a number", "NaN", HkxNumber.Text(float.NaN));
        Check("and the infinities", "-Infinity", HkxNumber.Text(float.NegativeInfinity));
    }

    /// The other half of the comparison: not whether the two readings hold the same values, but
    /// whether the tool does the same thing with them.
    ///
    /// Same rule as the field comparison. A run that reports no difference proves nothing unless the
    /// thing can report one, and this one has more room to quietly agree than the field walk does,
    /// because every consumer here is capable of returning an empty list and two empty lists match.
    private static void TheConsumerComparisonCatchesADifferentAnswer()
    {
        Console.WriteLine("\nthe consumer comparison catches a different answer");

        var clean = ConsumerDiff.Compare(Reading(), Reading());
        CheckTrue("two readings of one file behave the same", clean.Clean);
        Check("across every consumer", 13, clean.Compared);

        ConsumerDiff.Result After(Action<BehaviourGraphModel> change) =>
            ConsumerDiff.Compare(Reading(), Broken(change));

        // Two, not one, and the second is the more interesting. Pointing a wire at an object that is
        // not there changes the canvas, and it also gives the checker a dangling reference to
        // report, so a single wrong value surfaces in two places. That is what a consumer comparison
        // is for: the same fault reaching everything downstream of it.
        var rewired = After(m => m.Objects[0].Scalars["triggers"] = "#404");
        Check("a wire pointing at nothing shows up twice", 2, rewired.Differences.Count);
        Check("once in the checker", "checker findings", rewired.Differences[0].Consumer);
        Check("and once in the wiring", "the wiring", rewired.Differences[1].Consumer);
        CheckTrue("naming the line it is on",
                  rewired.Differences[1].What.StartsWith("line 1 of", StringComparison.Ordinal));

        // A class change moves the object out of the shapes table, so it stops having wires at all.
        var reclassed = After(m => m.Objects[0].Class = "hkbNothing");
        CheckTrue("a class the wiring does not know about is a difference too", !reclassed.Clean);

        // Nothing to compare is not the same as agreeing. Two readings of nothing agree, and that
        // has to stay true or every empty file would report a fault.
        var nothing = ConsumerDiff.Compare(new BehaviourGraphModel(), new BehaviourGraphModel());
        CheckTrue("two readings of an empty file still agree", nothing.Clean);

        // But a reading of nothing set against a real one does not.
        CheckTrue("a reading of nothing does not agree with a real one",
                  !ConsumerDiff.Compare(Reading(), new BehaviourGraphModel()).Clean);
    }

    /// Anything wider than four floats is written as a run of bracketed fours, not as one bracket
    /// holding the lot. Read off a vanilla skeleton's reference pose, where a qstransform is three
    /// of them run together with nothing between.
    ///
    /// It reads as a formatting detail and is not. The parser splits an array's text on whitespace,
    /// so `1.0)(0.0` is one token, and a reading that wrote one long bracket gave twelve tokens
    /// where the file gives ten. That is what the corpus sweep caught it as: a reference pose with
    /// the wrong number of elements in it.
    private static void WideFloatFieldsAreWrittenInBracketedFours()
    {
        Console.WriteLine("\nwide float fields are written in bracketed fours");

        Check("one vector is one bracket", "(1 2 3 4)",
              FieldRender.Floats(new[] { 1f, 2f, 3f, 4f }));
        Check("a qstransform is three, run together",
              "(0 0 0 1)(0 0 0 1)(1 1 1 1)",
              FieldRender.Floats(new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, 1f }));
        Check("and a transform is four", 4,
              FieldRender.Floats(new float[16])!.Count(c => c == '('));

        // The token count is the thing the parser sees, and the only reason the grouping matters.
        Check("which splits into ten tokens, not twelve", 10,
              FieldRender.Floats(new float[12])!
                         .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
    }

    /// The reading built from the bytes, on a file small enough to say what should be in it.
    ///
    /// The corpus sweep is what proves this against real data. These are the three things a sweep
    /// cannot show: that the numbering starts where hkxpack starts it, that a file the class table
    /// cannot describe gets refused whole rather than read with holes in it, and that a build with
    /// no table at all refuses rather than throwing.
    private static void TheReadingFromTheBytesRefusesWhatItCannotDescribe()
    {
        Console.WriteLine("\nthe reading from the bytes refuses what it cannot describe");

        var objects = new PackfileObjects(ClipInAPackfile("A.hkx", out _));

        var model = NativeGraphModel.From(objects);
        CheckTrue("a file the table describes is read", model != null);
        Check("with the object in it", 1, model!.Objects.Count);
        Check("numbered where hkxpack starts numbering", "90", model.Objects[0].Id);
        Check("and named by its class", "hkbClipGenerator", model.Objects[0].Class);
        Check("its string read from the bytes", "A.hkx", model.Objects[0].Str("animationName"));
        Check("and its number spelled like the file", "2.5", model.Objects[0].Str("playbackSpeed"));

        // No table, no reading. A build shipped without the data file has to fall back to hkxpack
        // rather than produce a model of nothing, which would compare as every field missing.
        Check("a build with no class table reads nothing", null,
              NativeGraphModel.From(objects, HavokClassTypes.Parse(Stream("""
                  { "classes": {} }
                  """))));

        // A table that knows other classes but not this one is the case that matters: it is what a
        // mod file built against a different Havok would look like, and half a reading of one of
        // those is worse than none.
        var elsewhere = HavokClassTypes.Parse(Stream("""
            { "classes": { "hkbNothing": { "signature": "0x00000001", "members": [] } } }
            """));
        Check("nor does one that does not describe this class", null,
              NativeGraphModel.From(objects, elsewhere));
    }

    private static System.IO.Stream Stream(string json) =>
        new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

    /// Rewiring a node, in bytes.
    ///
    /// It reads as a structural edit because the graph's shape changes, and it is not one in the
    /// file. No object moves, nothing is appended, the file does not change length: one entry in the
    /// pointer table names a different destination. That is why it can be written in place when
    /// adding a node still cannot.
    private static void APointerIsRewiredByMovingItsFixup()
    {
        Console.WriteLine("\na pointer is rewired by moving its fixup");

        var classes = HavokClasses.Shipped;
        int size = classes["hkbClipGenerator"]!.Size;
        int binding = classes.Field("hkbClipGenerator", "variableBindingSet")!.Offset;

        var image = ClipInAPackfile("A.hkx", out _);
        var data = image.Section("__data__")!;
        int second = data.AppendData(new byte[size]);
        data.VirtualFixups = data.VirtualFixups.Concat(Triple(second, 0, 5)).ToArray();

        var objects = new PackfileObjects(image);
        var clip = objects.Instances[0];

        // Nothing points anywhere yet, and a field with no fixup is null rather than a pointer to
        // offset zero, which would be a real object.
        objects.ReadRef(clip, "variableBindingSet", out bool emptyToStart);
        CheckTrue("a field with no fixup starts null", emptyToStart);

        // Aimed at something, from nothing.
        data.SetGlobal(binding, image.Sections.IndexOf(data), second);
        var pointed = new PackfileObjects(image).ReadRef(clip, "variableBindingSet", out bool none);
        CheckTrue("after pointing it, it is not null", !none);
        Check("and it names the object it was aimed at", second, pointed?.Offset);
        Check("with one entry in the table", 1, data.Globals().Count());

        // Aimed somewhere else. This is the rewire, and it must move the entry rather than add one.
        data.SetGlobal(binding, image.Sections.IndexOf(data), clip.Offset);
        var moved = new PackfileObjects(image).ReadRef(clip, "variableBindingSet", out _);
        Check("repointing it names the new object", clip.Offset, moved?.Offset);
        Check("and does not add a second entry for the same field", 1, data.Globals().Count());

        // Set to nothing. The entry goes, rather than being left aiming at offset zero.
        data.SetGlobal(binding, 0, -1);
        objects = new PackfileObjects(image);
        objects.ReadRef(clip, "variableBindingSet", out bool cleared);
        CheckTrue("clearing it reads as null", cleared);
        Check("because the entry is gone, not aimed at zero", 0, data.Globals().Count());

        // The file is the same size throughout. Nothing here appends or moves a byte.
        Check("and the data never changed length", size + size, data.Data.Length - "A.hkx".Length - 1);
    }

    /// Adding an object, and the two things that have to hold for it to be safe.
    ///
    /// Everything downstream turns an object id into a position: the id is hkxpack's numbering, which
    /// counts from #90 in the order the objects sit in the file. A new object is appended, so it is
    /// last in the file and must therefore carry the last id. The editor numbers a new object one
    /// past the highest, so that holds, and it is checked rather than trusted because getting it
    /// wrong aims a pointer at the wrong object without saying anything.
    private static void AnAddedObjectHasToLandWhereItsIdSays()
    {
        Console.WriteLine("\nan added object has to land where its id says");

        const string One = """
            <hkpackfile><hksection name="__data__">
            <hkobject name="#0090" class="hkbClipGenerator" signature="0x333b85b9">
                <hkparam name="userPartitionMask">0</hkparam>
            </hkobject></hksection></hkpackfile>
            """;

        string Extra(string id) => One.Replace("</hksection>",
            $"""
            <hkobject name="#{id}" class="hkbClipGenerator" signature="0x333b85b9">
                <hkparam name="userPartitionMask">7</hkparam>
            </hkobject></hksection>
            """);

        var added = NativeSave.Compare(One, Extra("0091"));
        CheckTrue("adding one is writable", added.Possible);
        CheckTrue("planned as an addition", added.Changes[0].Added);
        Check("naming the id it will have", "#0091", added.Changes[0].Value);
        Check("and its fields come with it", "userPartitionMask", added.Changes[1].Field);
        Check("as a value on the new object rather than the old one", 1, added.Changes[1].Index);

        // Removing is a different operation and is not written in place yet. Saying so beats
        // pretending, because the fallback through hkxpack still does it correctly.
        var removed = NativeSave.Compare(Extra("0091"), One);
        CheckTrue("removing one is refused", !removed.Possible);
        CheckTrue("and the refusal says what it was",
                  removed.Refusal?.Contains("removed", StringComparison.Ordinal) == true);

        // Renumbering breaks the id to position mapping for every object, not just the new one.
        string renumbered = Extra("0091").Replace("#0090", "#0500");
        CheckTrue("renumbering the existing objects is refused",
                  !NativeSave.Compare(One, renumbered).Possible);

        // The assertion that matters, made where it can be acted on. An id that does not match the
        // position the object would land at is refused at the point of writing.
        var image = ClipInAPackfile("A.hkx", out _);
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "symrm-add-probe.hkx");
        image.Save(path);

        var wrong = new NativeSave.Plan(
            new List<NativeSave.Change> { new("hkbClipGenerator", 1, "", "#0500", Added: true) }, null);

        string said = "";
        try { NativeSave.Apply(path, wrong); }
        catch (InvalidOperationException e) { said = e.Message; }

        CheckTrue("an added object whose id does not match where it lands is refused",
                  said.Contains("#0500", StringComparison.Ordinal));
        CheckTrue("and the refusal says which id it would have had",
                  said.Contains("#91", StringComparison.Ordinal));

        // A class the file does not name cannot have an object added, because the entry that says
        // what class an object is has to point at a name that is already there.
        var unnamed = new NativeSave.Plan(
            new List<NativeSave.Change> { new("hkbBlenderGenerator", 0, "", "#91", Added: true) }, null);

        said = "";
        try { NativeSave.Apply(path, unnamed); }
        catch (InvalidOperationException e) { said = e.Message; }

        CheckTrue("a class the file does not name is refused",
                  said.Contains("not named in this file", StringComparison.Ordinal));

        System.IO.File.Delete(path);
    }

    /// Where an entry sits in the pointer table is not free.
    ///
    /// The table is written in the order the writer walked the objects, which is not offset order:
    /// an array's element pointers are written while the array is being walked, before the fields
    /// that follow it in the owning object. On Dogmeat 22 of the 1,151 steps go backwards and every
    /// one is an array.
    ///
    /// This was found the hard way. Resizing an array by dropping its element entries and appending
    /// the new ones made hkxpack read every element of that array as null, while our own reader,
    /// which looks entries up by source, read it perfectly. Sorting the table by source, tried next
    /// on the theory that something binary searched it, made hkxpack misread more than a hundred
    /// fields instead. So order is load bearing, and the fix was to put the new entries back where
    /// the old ones were.
    private static void ThePointerTableKeepsTheOrderItWasWrittenIn()
    {
        Console.WriteLine("\nthe pointer table keeps the order it was written in");

        var section = new PackfileSection();
        var written = new[] { (96, 2, 500), (32, 2, 100), (64, 2, 300) };
        section.SetGlobals(written);

        Check("the entries come back in the order they went in", "96,32,64",
              string.Join(",", section.Globals().Select(g => g.Source)));
        CheckTrue("with their sections and destinations intact",
                  section.Globals().SequenceEqual(written));

        // Setting one that is already there leaves it where it is rather than moving it to the end.
        section.SetGlobal(32, 2, 999);
        Check("changing one does not move it", "96,32,64",
              string.Join(",", section.Globals().Select(g => g.Source)));
        Check("and it holds the new destination", 999,
              section.Globals().First(g => g.Source == 32).Destination);

        // A new one has nowhere else to go, so it goes on the end.
        section.SetGlobal(128, 2, 700);
        Check("a new entry goes on the end", "96,32,64,128",
              string.Join(",", section.Globals().Select(g => g.Source)));

        // Clearing drops it rather than leaving it aimed at zero, which would be a real object.
        section.SetGlobal(64, 0, -1);
        Check("clearing one removes it", "96,32,128",
              string.Join(",", section.Globals().Select(g => g.Source)));
    }

    /// The planner has to tell a pointer change from a value change, and has to refuse a pointer set
    /// to something that is not an object.
    private static void APointerChangeIsPlannedAsOne()
    {
        Console.WriteLine("\na pointer change is planned as one");

        const string Before = """
            <hkpackfile><hksection name="__data__">
            <hkobject name="#0090" class="hkbStateMachineStateInfo" signature="0xed7f9d0">
                <hkparam name="generator">#0091</hkparam>
                <hkparam name="stateId">0</hkparam>
            </hkobject></hksection></hkpackfile>
            """;

        var rewired = NativeSave.Compare(Before, Before.Replace(">#0091<", ">#0092<"));
        CheckTrue("aiming a pointer at another object is writable", rewired.Possible);
        Check("as one change", 1, rewired.Changes.Count);
        CheckTrue("marked as a pointer rather than a value", rewired.Changes[0].Ref);
        CheckTrue("and not as text, which is what would grow the file", !rewired.Grows);

        var cleared = NativeSave.Compare(Before, Before.Replace(">#0091<", ">null<"));
        CheckTrue("clearing a pointer is writable too", cleared.Possible);
        CheckTrue("and is still a pointer change", cleared.Changes[0].Ref);

        foreach (string rubbish in new[] { "#", "12", "#12a", "elsewhere", "" })
        {
            var plan = NativeSave.Compare(Before, Before.Replace(">#0091<", $">{rubbish}<"));
            CheckTrue($"a generator of '{rubbish}' is refused", !plan.Possible);
        }

        // An array of pointers made longer. Planned as an array rather than as a value, and it grows
        // the file, because the new run of pointers goes on the end.
        const string Machine = """
            <hkpackfile><hksection name="__data__">
            <hkobject name="#0090" class="hkbStateMachine" signature="0x816c1dcb">
                <hkparam name="states" numelements="2">#0091
            #0092</hkparam>
            </hkobject></hksection></hkpackfile>
            """;

        var longer = NativeSave.Compare(Machine, Machine.Replace("numelements=\"2\"", "numelements=\"3\"")
                                                        .Replace("#0092<", "#0092 #0091<"));
        CheckTrue("a longer array of pointers is writable", longer.Possible);
        CheckTrue("planned as an array", longer.Changes[0].Array);
        CheckTrue("and it grows the file", longer.Grows);
        Check("with the elements it was given", "#0091 #0092 #0091", longer.Changes[0].Value);

        var rubbishElement = NativeSave.Compare(Machine, Machine.Replace("#0092<", "elsewhere<"));
        CheckTrue("an element that is not an object id is refused", !rubbishElement.Possible);
    }

    /// Putting a new object into a file without moving anything already in it.
    ///
    /// The corpus proof is the one that matters, since it is hkxpack that has to agree about what
    /// the new object is called. These are the parts a corpus run cannot show: that the numbering is
    /// worked out before the write rather than read back afterwards, that a class the file has never
    /// named gets added to the name table, and that a class nobody can lay out is refused instead of
    /// written as a guess.
    private static void AnAppendedObjectLandsWhereItsNumberSaysItWill()
    {
        Console.WriteLine("\nan appended object lands where its number says it will");

        var image = ClipInAPackfile("A.hkx", out _);
        int before = new PackfileObjects(image).Instances.Count;

        var added = NativeAppend.Object(image, "hkbClipGenerator");

        Check("the file had one object", 1, before);
        Check("and the new one is the next number", NativeGraphModel.FirstId + 1, added.Id);
        Check("second of its class", 1, added.Index);
        CheckTrue("landing on a sixteen byte boundary", added.Offset % NativeAppend.Alignment == 0);

        var after = new PackfileObjects(image);
        Check("the file now holds two", 2, after.Instances.Count);
        Check("the first one did not move", 0, after.Instances[0].Offset);
        Check("and the new one is where it said", added.Offset, after.Instances[1].Offset);
        Check("holding the class asked for", "hkbClipGenerator", after.Instances[1].ClassName);

        // A class the file already names is not named twice.
        var names = image.Section("__classnames__")!;
        int length = names.Data.Length;
        NativeAppend.Object(image, "hkbClipGenerator");
        Check("a class already in the name table is not added again", length, names.Data.Length);

        // One it has never named is, and the reader has to be able to find it afterwards. This is
        // the path that failed against hkxpack until the section's 0xFF padding was taken off
        // before appending, while every check on our own side passed.
        var fresh = NativeAppend.Object(image, "hkbStateMachine");
        CheckTrue("a class it has never named makes the table longer", names.Data.Length > length);
        Check("and reads back as itself", "hkbStateMachine",
              new PackfileObjects(image).Instances[^1].ClassName);
        Check("with the number it was promised", fresh.Id,
              NativeGraphModel.FirstId + new PackfileObjects(image).Instances.Count - 1);

        CheckTrue("no 0xFF filler is left inside the name table",
                  !names.Data.SkipLast(1).Any(b => b == 0xFF));

        // A class with no size cannot be laid out, and guessing one writes an object the game will
        // read the wrong number of bytes from.
        string refused = "";
        try { NativeAppend.Object(image, "hkbNotAClass"); }
        catch (InvalidOperationException e) { refused = e.Message; }
        CheckTrue("a class the table does not describe is refused",
                  refused.Contains("hkbNotAClass", StringComparison.Ordinal));
    }

    /// A file holding one of each shape the graph model has a bucket for: plain fields, an array of
    /// references, a struct written inline under a name, and an array of structs written without
    /// one.
    private const string TwoObjects = """
        <hkobject class="hkbClipGenerator" name="#90">
            <hkparam name="name">walk</hkparam>
            <hkparam name="mode">MODE_SINGLE_PLAY</hkparam>
            <hkparam name="triggers" numelements="2">#91 #92</hkparam>
            <hkparam name="range">
                <hkobject name="range">
                    <hkparam name="min">0.0</hkparam>
                    <hkparam name="max">1.0</hkparam>
                </hkobject>
            </hkparam>
            <hkparam name="states" numelements="2">
                <hkobject>
                    <hkparam name="id">3</hkparam>
                </hkobject>
                <hkobject>
                    <hkparam name="id">4</hkparam>
                </hkobject>
            </hkparam>
        </hkobject>
        <hkobject class="hkbStateMachine" name="#91">
            <hkparam name="name">root</hkparam>
        </hkobject>
        """;

    private static BehaviourGraphModel Reading() => BehaviourGraphModel.Parse(TwoObjects);

    /// A second reading of the same file with something wrong put into it on purpose.
    private static BehaviourGraphModel Broken(Action<BehaviourGraphModel> change)
    {
        var reading = Reading();
        change(reading);
        return reading;
    }

    /// The comparison that will decide whether a graph model built from the bytes is the same as the
    /// one built from hkxpack's text, checked before it is trusted to say so.
    ///
    /// A clean run means nothing on its own. Anything that returns "no disagreements" without
    /// looking at a single field passes that way, and it would pass every file in the corpus too, so
    /// this breaks a reading on purpose in each of the ways a wrong producer could break one and
    /// asks for the count back. The count is asserted exactly rather than as "more than none",
    /// because a comparison that reports one fault as forty is not one that can be read.
    private static void TheModelComparisonCatchesFaultsPutThereOnPurpose()
    {
        Console.WriteLine("\nthe model comparison catches faults put there on purpose");

        var clean = ModelDiff.Compare(Reading(), Reading());
        CheckTrue("two readings of one file agree", clean.Clean);
        Check("over both objects", 2, clean.Objects);

        // The check on the check. A comparison that walks nothing agrees with everything, so the
        // count of what it walked is asserted against the count of what is in the file. Worked out
        // from the reading rather than written down as a number, so it stays true if the fixture
        // grows a field.
        var one = Reading();
        int inTheFile = one.Objects.Sum(o => 2 + o.Scalars.Count
                                           + o.Lists.Sum(l => 1 + l.Value.Count)
                                           + o.Structs.Sum(s => s.Value.Count)
                                           + o.StructLists.Sum(s => 1 + s.Value.Sum(e => e.Count)));
        Check("having compared every field the file holds", inTheFile, clean.Compared);

        int Faults(Action<BehaviourGraphModel> break_) =>
            ModelDiff.Compare(Reading(), Broken(break_)).Total;

        string Where(Action<BehaviourGraphModel> break_)
        {
            var second = Reading();
            break_(second);
            return ModelDiff.Compare(Reading(), second).Shown.FirstOrDefault()?.Where ?? "nothing";
        }

        Check("an object missing altogether", 1, Faults(m => m.Objects.RemoveAt(1)));
        Check("an id that does not match", 1, Faults(m => m.Objects[0].Id = "999"));
        Check("a class that does not match", 1, Faults(m => m.Objects[0].Class = "hkbNothing"));

        Check("a field the second reading does not have", 1,
              Faults(m => m.Objects[0].Scalars.Remove("name")));
        Check("a field only the second reading has", 1,
              Faults(m => m.Objects[0].Scalars["extra"] = "1"));
        Check("a field holding something else", 1,
              Faults(m => m.Objects[0].Scalars["mode"] = "MODE_LOOPING"));

        Check("an array of a different length", 1,
              Faults(m => m.Objects[0].Lists["triggers"].RemoveAt(0)));
        Check("an array element holding something else", 1,
              Faults(m => m.Objects[0].Lists["triggers"][1] = "#99"));

        Check("a field inside an inline struct", 1,
              Faults(m => m.Objects[0].Structs["range"]["max"] = "2.0"));
        Check("a struct array of a different length", 1,
              Faults(m => m.Objects[0].StructLists["states"].RemoveAt(1)));
        Check("a field inside one of its elements", 1,
              Faults(m => m.Objects[0].StructLists["states"][1]["id"] = "5"));

        // The lesson from the field crosscheck, where six vanilla values carry meaningful spaces: a
        // comparison that tidies up is agreeing with itself rather than with the file.
        Check("a value differing only by a space", 1,
              Faults(m => m.Objects[0].Scalars["name"] = "walk "));

        // Naming where it went wrong is half of what the comparison is for. A count with no address
        // sends somebody looking through nine hundred objects by hand.
        Check("and the disagreement names the object and the field",
              "#90 hkbClipGenerator.mode", Where(m => m.Objects[0].Scalars["mode"] = "MODE_LOOPING"));
        Check("naming the element too, inside a struct array",
              "#90 hkbClipGenerator.states[1].id",
              Where(m => m.Objects[0].StructLists["states"][1]["id"] = "5"));

        // Everything wrong at once still has to come back readable rather than as a wall. An empty
        // reading is not the case to use for that: the object counts differ, so there is nothing to
        // walk into and it reports one disagreement rather than many. A reading of the right shape
        // holding the wrong values everywhere is the case that produces a wall.
        var wrong = Reading();
        foreach (var o in wrong.Objects)
        {
            foreach (string key in o.Scalars.Keys.ToList()) o.Scalars[key] += "x";
            foreach (var s in o.Structs.Values)
                foreach (string key in s.Keys.ToList()) s[key] += "x";
            foreach (var list in o.StructLists.Values)
                foreach (var element in list)
                    foreach (string key in element.Keys.ToList()) element[key] += "x";
        }

        // The one excuse the comparison accepts, and the checks that it stays one. hkxpack sizes a
        // sixteen aligned struct by rounding up to eight, so every element after the first in an
        // array of one is read eight bytes early. That is hkxpack being wrong, so it is counted
        // apart rather than failing the file, and the earlier lesson from the padded class predicate
        // applies: an excuse that is too wide is worse than no excuse, because it hides real faults.
        ModelDiff.Result Excusing(ModelDiff.Strided excuse) =>
            ModelDiff.Compare(Reading(), Broken(m => m.Objects[0].StructLists["states"][1]["id"] = "5"),
                              40, excuse);

        var named = Excusing((cls, field) => cls == "hkbClipGenerator" && field == "states");
        Check("a mis-strided struct array is not a disagreement", 0, named.Total);
        Check("but is counted and reported", 1, named.Strided);

        var elsewhere = Excusing((cls, field) => cls == "hkbClipGenerator" && field == "triggers");
        Check("an excuse for another field excuses nothing", 1, elsewhere.Total);
        Check("and claims nothing", 0, elsewhere.Strided);

        var wrongClass = Excusing((cls, field) => cls == "hkbStateMachine" && field == "states");
        Check("nor does one for another class", 1, wrongClass.Total);

        Check("a plain field is never excused, whatever the predicate says", 1,
              ModelDiff.Compare(Reading(), Broken(m => m.Objects[0].Scalars["mode"] = "MODE_LOOPING"),
                                40, (_, _) => true).Total);

        // The excuse is decided as the difference is found, not by picking it back out of the shown
        // examples, so it holds when there are no examples left to pick from. The first attempt did
        // filter the examples, and a file with more differences than the cap came back reporting a
        // count with nothing to show for it.
        var capped = ModelDiff.Compare(Reading(),
                                       Broken(m => m.Objects[0].StructLists["states"][1]["id"] = "5"),
                                       0, (cls, field) => field == "states");
        Check("the excuse holds with no examples kept", 0, capped.Total);
        Check("and is still counted", 1, capped.Strided);

        var everything = ModelDiff.Compare(Reading(), wrong, cap: 3);
        CheckTrue("a reading wrong about every value disagrees", !everything.Clean);
        Check("about all seven of them", 7, everything.Total);
        Check("with the examples capped", 3, everything.Shown.Count);
    }
}
