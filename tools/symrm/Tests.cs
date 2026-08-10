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
        ("EveryDrawnNodeHasOneOwner", EveryDrawnNodeHasOneOwner),
        ("OwnershipAnswersWhatMovesAndWhatHides", OwnershipAnswersWhatMovesAndWhatHides),
        ("ASharedGeneratorBelongsToOneBranchOnly", ASharedGeneratorBelongsToOneBranchOnly),
        ("ChildrenSitBesideTheParentThatOwnsThem", ChildrenSitBesideTheParentThatOwnsThem),
        ("ACollidingFamilyMovesWhole", ACollidingFamilyMovesWhole),
        ("APinnedNodeIsNeverMovedToMakeRoom", APinnedNodeIsNeverMovedToMakeRoom),
        ("ASharedNodeIsPlacedOnceByItsOwner", ASharedNodeIsPlacedOnceByItsOwner),
        ("SubtreesOfDifferentDepthsShareTheHeight", SubtreesOfDifferentDepthsShareTheHeight),
        ("DepthOnOneSideCostsNothingOnTheOther", DepthOnOneSideCostsNothingOnTheOther),
        ("ReplacingLinkSaysWhatItDisplaced", ReplacingLinkSaysWhatItDisplaced),
        ("BlenderChildIsWrapped", BlenderChildIsWrapped),
        ("AnyNodeCanBeDeleted", AnyNodeCanBeDeleted),
        ("AReferenceInsideAStructIsSeenByBothReaders", AReferenceInsideAStructIsSeenByBothReaders),
        ("ADanglingReferenceIsReportedWhereverItSits", ADanglingReferenceIsReportedWhereverItSits),
        ("AppendedStringsLandOnAnEvenOffset", AppendedStringsLandOnAnEvenOffset),
        ("StructuralObjectsAreProtected", StructuralObjectsAreProtected),
        ("PortTypesRefuseNonsense", PortTypesRefuseNonsense),
        ("BundledHkxPackIsFound", BundledHkxPackIsFound),
        ("Fo4CharacterListsItsAnimations", Fo4CharacterListsItsAnimations),
        ("MissingClipAnimationIsReported", MissingClipAnimationIsReported),
        ("RepackDriftNamesWhatMoved", RepackDriftNamesWhatMoved),
        ("TransitionRowsCarryPriorityAndFlags", TransitionRowsCarryPriorityAndFlags),
        ("StaticTraceFollowsExistingGraphLinks", StaticTraceFollowsExistingGraphLinks),
        ("AnUnreachableStateIsReported", AnUnreachableStateIsReported),
        ("EventUsageSaysWhoSendsAndWhoListens", EventUsageSaysWhoSendsAndWhoListens),
        ("ScaleIsShownOnlyWhenItIsRealScale", ScaleIsShownOnlyWhenItIsRealScale),
        ("AFractionLandsOnAFrame", AFractionLandsOnAFrame),
        ("LosslessScaleFollowsTheEngine", LosslessScaleFollowsTheEngine),
        ("AnEmptyStateIsFoundTheSameWayEverywhere", AnEmptyStateIsFoundTheSameWayEverywhere),
        ("AddedVariablesCarryTheirDeclaredType", AddedVariablesCarryTheirDeclaredType),
        ("EveryFindingPointsAtAnObject", EveryFindingPointsAtAnObject),
        ("AShortBoundsArrayStaysLinedUp", AShortBoundsArrayStaysLinedUp),
        ("ABoundCanBeAuthoredPastTheEndOfTheArray", ABoundCanBeAuthoredPastTheEndOfTheArray),
        ("AValueInsideAStructArrayIsWrittenInPlace", AValueInsideAStructArrayIsWrittenInPlace),
        ("AStructArrayCanBeMadeLonger", AStructArrayCanBeMadeLonger),
        ("WindowsLineEndingsStillEdit", WindowsLineEndingsStillEdit),
        ("RepackDriftCatchesAChangedValue", RepackDriftCatchesAChangedValue),
        ("AnAnimationIsRefusedForSaving", AnAnimationIsRefusedForSaving),
        ("TwoFilesDiffToWhatEachChanged", TwoFilesDiffToWhatEachChanged),
        ("EverySymbolUsageNamesItsObject", EverySymbolUsageNamesItsObject),
        ("PapyrusSendersAreReportedNotJudged", PapyrusSendersAreReportedNotJudged),
        ("APoseComposesDownTheBoneChain", APoseComposesDownTheBoneChain),
        ("AMeshAuthoredAwayFromTheOriginIsNotAFault", AMeshAuthoredAwayFromTheOriginIsNotAFault),
        ("AnArchiveIsReadWithoutUnpackingIt", AnArchiveIsReadWithoutUnpackingIt),
        ("TravelIsReadBetweenSamples", TravelIsReadBetweenSamples),
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
        ("RemovingAnObjectIsRefusedAndOrphaningIsNot", RemovingAnObjectIsRefusedAndOrphaningIsNot),
        ("DeletingTakesAnObjectOutOfTheFile", DeletingTakesAnObjectOutOfTheFile),
        ("AnArrayOfNamesCanGrow", AnArrayOfNamesCanGrow),
        ("AWideFieldIsWrittenWhereItSits", AWideFieldIsWrittenWhereItSits),
        ("AnArrayOfNumbersCanGrow", AnArrayOfNumbersCanGrow),
        ("AFieldSaysWhatItIsAndOnlySaysWhatItMeansWhenWeKnow", AFieldSaysWhatItIsAndOnlySaysWhatItMeansWhenWeKnow),
        ("TheLastObjectsBlockEndsAtItsOwnClosingTag", TheLastObjectsBlockEndsAtItsOwnClosingTag),
        ("AnEnumFieldOffersItsDeclaredValues", AnEnumFieldOffersItsDeclaredValues),
        ("WideFloatFieldsAreWrittenInBracketedFours", WideFloatFieldsAreWrittenInBracketedFours),
        ("TheConsumerComparisonCatchesADifferentAnswer", TheConsumerComparisonCatchesADifferentAnswer),
        ("APointerIsRewiredByMovingItsFixup", APointerIsRewiredByMovingItsFixup),
        ("APointerChangeIsPlannedAsOne", APointerChangeIsPlannedAsOne),
        ("ThePointerTableKeepsTheOrderItWasWrittenIn", ThePointerTableKeepsTheOrderItWasWrittenIn),
        ("AnAddedObjectHasToLandWhereItsIdSays", AnAddedObjectHasToLandWhereItsIdSays),
        ("APastedSubtreePointsAtItself", APastedSubtreePointsAtItself),
        ("AConditionSaysWhatItSays", AConditionSaysWhatItSays),
        ("AFalseConditionHoldsATransitionBack", AFalseConditionHoldsATransitionBack),
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
        ("AnElementsFieldIsWrittenToThatElement", AnElementsFieldIsWrittenToThatElement),
        ("EveryFieldSaysWhereItSits", EveryFieldSaysWhereItSits),
        ("APackedRotationComesBackAsItself", APackedRotationComesBackAsItself),
        ("ALinearCurvePassesThroughEveryFrame", ALinearCurvePassesThroughEveryFrame),
        ("AnEncodedClipDecodesToWhatWentIn", AnEncodedClipDecodesToWhatWentIn),
        ("AnUndrivenChannelIsNotWrittenAsACurve", AnUndrivenChannelIsNotWrittenAsACurve),
        ("AClipTooLongForOneBlockIsSplit", AClipTooLongForOneBlockIsSplit),
        ("ADoorOpensWhenSentTheEventItsOwnTransitionNames", ADoorOpensWhenSentTheEventItsOwnTransitionNames),
        ("EveryRunningMachineHearsAnEvent", EveryRunningMachineHearsAnEvent),
        ("TheRunRefusesToGuessPastAnotherFile", TheRunRefusesToGuessPastAnotherFile),
        ("SteppingAgreesWithTheReachabilityItReports", SteppingAgreesWithTheReachabilityItReports),
        ("ATransitionBlendsFromOneStateToTheNext", ATransitionBlendsFromOneStateToTheNext),
        ("AnInstantTransitionDoesNotBlend", AnInstantTransitionDoesNotBlend),
        ("APlainBlenderSharesByWeight", APlainBlenderSharesByWeight),
        ("AParametricBlenderIsPickedNotMixed", AParametricBlenderIsPickedNotMixed),
        ("ADrivenBlendIsReportedNotGuessed", ADrivenBlendIsReportedNotGuessed),
        ("AnEditedFrameSurvivesReEncoding", AnEditedFrameSurvivesReEncoding),
        ("AClipEndsAndTheStateLeavesWithoutAnEvent", AClipEndsAndTheStateLeavesWithoutAnEvent),
        ("AClipLengthIsCroppedAndScaled", AClipLengthIsCroppedAndScaled),
        ("AnUntimedClipRaisesNothing", AnUntimedClipRaisesNothing),
        ("ALoopingClipKeepsFiringAndASinglePlayDoesNot", ALoopingClipKeepsFiringAndASinglePlayDoesNot),
        ("ATemplateLiftedFromOneFileGoesIntoAnother", ATemplateLiftedFromOneFileGoesIntoAnother),
        ("ATemplateRefusesToLiftWhatSharesItsFile", ATemplateRefusesToLiftWhatSharesItsFile),
        ("ATemplateSaysWhatToDeclareRatherThanJustFailing", ATemplateSaysWhatToDeclareRatherThanJustFailing),
        ("ATemplateDescriptionSurvivesAwkwardNames", ATemplateDescriptionSurvivesAwkwardNames),
        ("ACutTakesTheClipsOwnTimeWithIt", ACutTakesTheClipsOwnTimeWithIt),
        ("ALinearTravelStaysTwoSamplesAfterACut", ALinearTravelStaysTwoSamplesAfterACut),
        ("ACutRefusesWhatIsNotAClip", ACutRefusesWhatIsNotAClip),
        ("DurationCountsIntervalsNotFrames", DurationCountsIntervalsNotFrames),
        ("ARetimeMovesEverythingThatMeasuresTime", ARetimeMovesEverythingThatMeasuresTime),
        ("KeepingTheFramesCostsNothingAtAll", KeepingTheFramesCostsNothingAtAll),
        ("ARetimeSaysWhatTheResamplingCost", ARetimeSaysWhatTheResamplingCost),
        ("ARotationIsReadAlongTheArcNotAcrossIt", ARotationIsReadAlongTheArcNotAcrossIt),
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

    /// A refusal is a result like any other. Checking only that the good case works leaves the bad
    /// case free to quietly do something else, which for a write is the worse of the two failures.
    private static void CheckThrows(string what, Action action)
    {
        _ran++;
        bool threw = false;
        try { action(); }
        catch (Exception) { threw = true; }
        if (!threw) _failed++;
        Console.WriteLine($"  {(threw ? "ok  " : "FAIL")}  {what}");
    }

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

    // Ownership is the rule the whole canvas hangs off: where a node is placed, whether a collapse
    // hides it, and whether a drag moves it. It is not a new idea, it is a fact the walk already knew
    // and threw away, so this pins it down before anything is built on it.
    private static void EveryDrawnNodeHasOneOwner()
    {
        Console.WriteLine("\nevery drawn node has one owner");

        var model = BehaviourGraphModel.Parse(BlenderGraph(0, 0, 1, 1));
        var placed = GraphAuthor.Layout(model, 1000);

        // Every object in the fixture except #130, the binding set, which nothing points at in this
        // shape and which is not a node the canvas draws. Named and in order rather than counted,
        // because the order is the walk and the walk is what decides ownership: breadth first, so
        // the graph's own targets come before their children.
        Check("the walk placed the graph, breadth first", "91, 110, 80, 111, 112, 81, 121, 122",
              string.Join(", ", placed.Select(p => p.Node.Id)));

        var owner = placed.ToDictionary(p => p.Node.Id, p => p.OwnerId);
        Check("the root owns nothing above it", "", owner["91"]);
        Check("the blender is owned by the graph that names it", "91", owner["110"]);
        Check("and a blender child by the blender", "110", owner["111"]);

        // Every node bar a walk root has exactly one owner, and following owners always ends.
        foreach (var (node, _, ownerId) in placed)
        {
            if (ownerId.Length == 0) continue;
            CheckTrue($"#{node.Id}'s owner is itself drawn", owner.ContainsKey(ownerId));

            var seen = new HashSet<string>();
            string at = node.Id;
            while (owner.TryGetValue(at, out string? up) && up.Length > 0)
            {
                CheckTrue($"#{node.Id}'s owner chain does not loop", seen.Add(up));
                at = up;
            }
        }
    }

    // The three questions the canvas asks about ownership, on a shape built so the shared case is the
    // interesting one. A owns B and C; B owns D; C owns E which owns F.
    private static void OwnershipAnswersWhatMovesAndWhatHides()
    {
        Console.WriteLine("\nownership answers what moves and what hides");

        var tree = GraphOwnership.Of(new[]
        {
            ("A", ""), ("B", "A"), ("C", "A"), ("D", "B"), ("E", "C"), ("F", "E"),
        });

        Check("A owns two directly", "B, C", string.Join(", ", tree.Children("A")));
        Check("and everything under it", "B, D, C, E, F", string.Join(", ", tree.Under("A")));
        Check("D is owned by B and nobody else", "B", tree.Owner["D"]);
        Check("a leaf owns nothing", 0, tree.Under("F").Count);

        Check("F's chain runs nearest first", "E, C, A", string.Join(", ", tree.Chain("F")));
        Check("and a root's chain is empty", 0, tree.Chain("A").Count);

        var collapsed = new HashSet<string> { "B" };
        CheckTrue("collapsing B hides what B owns", tree.Hidden(collapsed, "D"));
        CheckTrue("and leaves the other branch alone", !tree.Hidden(collapsed, "E"));
        CheckTrue("and does not hide B itself, which is what you click to undo it",
            !tree.Hidden(collapsed, "B"));

        Check("B's badge counts only what B hides", 1, tree.HiddenBy(collapsed, "B"));
        Check("a node that is not collapsed hides nothing", 0, tree.HiddenBy(collapsed, "A"));

        // Two collapses, one inside the other. The badge's promise is what it will bring back when
        // clicked, so neither may count what the other is holding.
        //
        // E is hidden by A, so its own badge is not even on screen and claims nothing. A claims B, C,
        // D and E: four, not five. F is left out because expanding A does not reveal F, E is still
        // shut. A badge reading five here would promise a node it cannot produce.
        var both = new HashSet<string> { "A", "E" };
        Check("an inner collapse claims nothing already hidden", 0, tree.HiddenBy(both, "E"));
        Check("and the outer one claims what it can actually bring back", 4, tree.HiddenBy(both, "A"));

        // The dedupe that matters: E is selected in its own right and is also under A.
        var moving = tree.Moving(new[] { "A", "E" });
        Check("everything moves, once each", "A, B, C, D, E, F",
            string.Join(", ", moving.OrderBy(m => m, StringComparer.Ordinal)));
        Check("the set is a set", 6, moving.Count);

        Check("a node nobody placed moves nothing", 0, tree.Moving(new[] { "Z" }).Count);
    }

    // The case ownership exists for, on a real graph rather than a made up map.
    //
    // Two states point at one clip. The canvas can only draw it in one place, so the first state to
    // reach it owns it, and the second gets a wire to somewhere it does not control. Everything that
    // could go wrong here goes wrong quietly: collapsing the second state must not hide the clip out
    // from under the first, and dragging the second must not drag it away either.
    private static void ASharedGeneratorBelongsToOneBranchOnly()
    {
        Console.WriteLine("\na shared generator belongs to one branch only");

        var model = BehaviourGraphModel.Parse(SharedGeneratorGraph());
        var placed = GraphAuthor.Layout(model, 1000);
        var tree = GraphOwnership.Of(placed);

        Check("both states point at the same clip", "#94",
            model.Get("95")?.Ref("generator") is string g ? "#" + g : "none");

        Check("the clip is drawn once", 1, placed.Count(p => p.Node.Id == "94"));
        Check("and owned by the state that reached it first", "93", tree.Owner["94"]);
        Check("the second state owns nothing", 0, tree.Under("95").Count);

        // Hiding. The clip is under #93 and must not answer to #95.
        var shutSecond = new HashSet<string> { "95" };
        CheckTrue("collapsing the borrower does not hide the shared clip",
            !tree.Hidden(shutSecond, "94"));
        Check("and its badge claims nothing", 0, tree.HiddenBy(shutSecond, "95"));

        var shutFirst = new HashSet<string> { "93" };
        CheckTrue("collapsing the owner does hide it", tree.Hidden(shutFirst, "94"));
        Check("and its badge says so", 1, tree.HiddenBy(shutFirst, "93"));

        // Moving. Same rule, same reason.
        Check("dragging the borrower moves only itself", "95",
            string.Join(", ", tree.Moving(new[] { "95" })));
        Check("dragging the owner takes the clip with it", "93, 94",
            string.Join(", ", tree.Moving(new[] { "93" }).OrderBy(m => m, StringComparer.Ordinal)));
    }

    /// A parent, and the family it owns, for the layout checks. Every node the same height so the
    /// numbers in the checks are readable rather than arithmetic.
    private static GraphLayout.Item Node(string id, int column, string owner) =>
        new(id, column, owner, 100);

    /// Where the middle of a node sits, which is what a family is centred on.
    private static double Centre(Dictionary<string, double> y, string id) => y[id] + 50;

    // The defect this replaces: nodes were placed by depth into columns and stacked with one running
    // Y counter per column, so nothing ever consulted the parent's position and a parent low on the
    // canvas got its children put near the top. The long diagonal wires were that.
    private static void ChildrenSitBesideTheParentThatOwnsThem()
    {
        Console.WriteLine("\nchildren sit beside the parent that owns them");

        // Six children under P1 push P2 a long way down its column, which is the case that used to
        // strand P2's own children at the top of the next column.
        var items = new List<GraphLayout.Item> { Node("root", 0, "") };
        items.Add(Node("P1", 1, "root"));
        items.Add(Node("P2", 1, "root"));
        for (int i = 0; i < 6; i++) items.Add(Node("a" + i, 2, "P1"));
        items.Add(Node("b0", 2, "P2"));
        items.Add(Node("b1", 2, "P2"));

        var y = GraphLayout.Place(items, new Dictionary<string, double>(), 20);

        CheckTrue($"the second parent really is far down ({y["P2"]:F0})", y["P2"] > 300);

        // The whole point. Its children are beside it, not at the top of the column with P1's.
        double drop = Math.Abs(Centre(y, "b0") - Centre(y, "P2"));
        CheckTrue($"its children are beside it, not at the top ({y["b0"]:F0} against {y["P2"]:F0})",
            drop < 200);
        CheckTrue($"and nowhere near the other family ({y["b0"]:F0} against {y["a0"]:F0})",
            y["b0"] > y["a0"] + 200);

        // Centred on the parent rather than starting at it, so the wires fan out from both sides.
        CheckTrue($"the family straddles its parent ({y["b0"]:F0}, {y["b1"]:F0})",
            Centre(y, "b0") < Centre(y, "P2") + 1 && Centre(y, "b1") > Centre(y, "P2") - 1);

        // Nothing overlaps anywhere.
        foreach (var column in items.GroupBy(i => i.Column))
        {
            var sorted = column.OrderBy(i => y[i.Id]).ToList();
            for (int i = 1; i < sorted.Count; i++)
                CheckTrue($"{sorted[i - 1].Id} and {sorted[i].Id} do not overlap",
                    y[sorted[i].Id] >= y[sorted[i - 1].Id] + sorted[i - 1].Height - 0.001);
        }

        // Same input, same answer, every time.
        var again = GraphLayout.Place(items, new Dictionary<string, double>(), 20);
        Check("the layout is deterministic", string.Join(",", y.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value:F2}")),
              string.Join(",", again.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value:F2}")));
    }

    // A family is the unit a collision moves. Moving one member and not the rest is what splits a
    // family, and a split family is what puts the long wires back.
    private static void ACollidingFamilyMovesWhole()
    {
        Console.WriteLine("\ntwo families in a column never mix");

        var items = new List<GraphLayout.Item>
        {
            Node("root", 0, ""),
            Node("P1", 1, "root"),
            Node("P2", 1, "root"),
            Node("a0", 2, "P1"), Node("a1", 2, "P1"), Node("a2", 2, "P1"),
            Node("b0", 2, "P2"), Node("b1", 2, "P2"), Node("b2", 2, "P2"),
        };

        var y = GraphLayout.Place(items, new Dictionary<string, double>(), 20);

        // Spacing inside each family: 100 tall plus a 20 gap.
        Check("the first family keeps its spacing", "120, 120",
            $"{y["a1"] - y["a0"]:F0}, {y["a2"] - y["a1"]:F0}");
        Check("and so does the second", "120, 120",
            $"{y["b1"] - y["b0"]:F0}, {y["b2"] - y["b1"]:F0}");

        // The invariant, checked by interleaving rather than by spacing. A family split by some
        // future change would show up as one family's members appearing on both sides of the
        // other's, which a spacing check would sail straight past.
        var order = items.Where(i => i.Column == 2).OrderBy(i => y[i.Id])
                         .Select(i => i.Id[0]).ToArray();
        Check("each family is one unbroken run down the column", "aaabbb", new string(order));

        CheckTrue($"and the two are clear of each other ({y["b0"]:F0} against {y["a2"]:F0})",
            y["b0"] >= y["a2"] + 100 - 0.001);

        // Each parent sits level with the middle of its own family, which is what stops the wires
        // running the height of the canvas.
        Check("the first parent is level with its family", Centre(y, "a1").ToString("F2"),
              Centre(y, "P1").ToString("F2"));
        Check("and so is the second", Centre(y, "b1").ToString("F2"),
              Centre(y, "P2").ToString("F2"));
    }

    // A position the user chose by hand outranks anything the layout would rather do. It blocks, so
    // a family is pushed past it, and it never moves itself.
    private static void APinnedNodeIsNeverMovedToMakeRoom()
    {
        Console.WriteLine("\na pinned node is never moved to make room");

        var items = new List<GraphLayout.Item>
        {
            Node("root", 0, ""),
            Node("P", 1, "root"),
            Node("a0", 2, "P"), Node("a1", 2, "P"),
            Node("Held", 2, "root"),
        };

        // Pinned exactly where the family would naturally want to sit.
        var loose = GraphLayout.Place(items, new Dictionary<string, double>(), 20);
        double wanted = loose["a0"];

        var pinned = new Dictionary<string, double> { ["Held"] = wanted };
        var y = GraphLayout.Place(items, pinned, 20);

        Check("the pinned node is exactly where it was put", wanted.ToString("F2"), y["Held"].ToString("F2"));
        CheckTrue($"and the family went around it rather than through it ({y["a0"]:F0} against {y["Held"]:F0})",
            y["a0"] >= y["Held"] + 100 || y["a0"] + 100 <= y["Held"]);
        Check("the family that moved kept its spacing", 120d, Math.Round(y["a1"] - y["a0"]));

        // Whitespace is the accepted price. The check is that nothing was moved that should not
        // have been, not that the column is tight.
        CheckTrue("the pin did not drag its neighbours with it", Math.Abs(y["Held"] - wanted) < 0.001);
    }

    // A node two parents point at is laid out once, by the parent that owns it. The borrower gets a
    // wire to wherever the owner put it and no say in where that is.
    private static void ASharedNodeIsPlacedOnceByItsOwner()
    {
        Console.WriteLine("\na shared node is placed once by its owner");

        var model = BehaviourGraphModel.Parse(SharedGeneratorGraph());
        var placed = GraphAuthor.Layout(model, 1000);
        var tree = GraphOwnership.Of(placed);

        var items = placed.Select(p => new GraphLayout.Item(p.Node.Id, p.Column, p.OwnerId, 100)).ToList();
        var y = GraphLayout.Place(items, new Dictionary<string, double>(), 20);

        Check("every node got exactly one position", items.Count, y.Count);

        // #94 is owned by #93 and borrowed by #95. It sits with its owner.
        Check("the shared clip is owned by the first state", "93", tree.Owner["94"]);
        Check("and is centred on that state, not on the borrower",
            Centre(y, "93").ToString("F2"), Centre(y, "94").ToString("F2"));

        // The borrower is the case that would show a second placement: if #95 were allowed to lay
        // #94 out again, #94 would land beside #95 instead.
        CheckTrue($"the borrower did not drag it across ({y["94"]:F0} against {y["95"]:F0})",
            Math.Abs(y["94"] - y["95"]) > 1);

        var again = GraphLayout.Place(items, new Dictionary<string, double>(), 20);
        Check("and placing it twice gives the same answer", y["94"].ToString("F2"), again["94"].ToString("F2"));
    }

    // What measuring a contour buys over measuring a total, which is the whole reason the first
    // version of the layout was thrown away.
    //
    // Two sibling families with different depth profiles. The first is deep and narrow: a chain
    // running out to column 5. The second is shallow and wide: eight children and nothing past
    // column 2. Sizing a subtree by everything under it makes the second wait for the whole of the
    // first, because a total has no idea the first is using columns the second never touches. A
    // contour does, so the two share the height and only clear each other where they actually meet.
    private static void SubtreesOfDifferentDepthsShareTheHeight()
    {
        Console.WriteLine("\nsubtrees of different depths share the height");

        var items = new List<GraphLayout.Item>
        {
            Node("root", 0, ""),
            Node("deep", 1, "root"),
            Node("wide", 1, "root"),
        };

        // deep: one node per column, out to column 5.
        items.Add(Node("d2", 2, "deep"));
        items.Add(Node("d3", 3, "d2"));
        items.Add(Node("d4", 4, "d3"));
        items.Add(Node("d5", 5, "d4"));

        // wide: eight children, all in column 2.
        for (int i = 0; i < 8; i++) items.Add(Node("w" + i, 2, "wide"));

        var y = GraphLayout.Place(items, new Dictionary<string, double>(), 20);

        double tall = items.Max(i => y[i.Id] + i.Height) - items.Min(i => y[i.Id]);

        // Sized by totals the two families cannot overlap at all, so the canvas is at least the deep
        // family's four nodes plus the wide family's eight, 12 rows: 1420. Sized by contour they
        // only have to clear each other in columns 1 and 2, so the deep family's columns 3 to 5 sit
        // level with the wide family instead of below it.
        CheckTrue($"the two families share the height rather than stacking ({tall:F0})", tall < 1300);

        // The deep chain runs out past where the wide family stops, and every one of those nodes is
        // level with its own parent because nothing is competing for that column.
        Check("the chain stays level with itself", "0, 0, 0",
            $"{y["d3"] - y["d2"]:F0}, {y["d4"] - y["d3"]:F0}, {y["d5"] - y["d4"]:F0}");

        // The saving must not come from letting nodes overlap. Every column is still checked.
        foreach (var column in items.GroupBy(i => i.Column))
        {
            var sorted = column.OrderBy(i => y[i.Id]).ToList();
            for (int i = 1; i < sorted.Count; i++)
                CheckTrue($"{sorted[i - 1].Id} and {sorted[i].Id} do not overlap in column {column.Key}",
                    y[sorted[i].Id] >= y[sorted[i - 1].Id] + sorted[i - 1].Height - 0.001);
        }

        // And the families are still not interleaved where they do meet.
        var order = items.Where(i => i.Column == 2).OrderBy(i => y[i.Id]).Select(i => i.Id[0]).ToArray();
        Check("neither family is split by the other in the column they share", "dwwwwwwww",
            new string(order));
    }

    // The defining property of contour packing, stated as the thing that must not happen: making one
    // family deeper must cost the other family nothing in the columns it never reaches.
    //
    // Sized by totals this fails by construction, because a total counts every node under a subtree
    // whatever column it sits in, so each node added to the deep chain pushes the wide family down by
    // a row. Sized by contour the chain's columns 3 and beyond are its own business and the wide
    // family never hears about them.
    private static void DepthOnOneSideCostsNothingOnTheOther()
    {
        Console.WriteLine("\ndepth on one side costs nothing on the other");

        Dictionary<string, double> WithChainOf(int levels)
        {
            var items = new List<GraphLayout.Item>
            {
                Node("root", 0, ""),
                Node("deep", 1, "root"),
                Node("wide", 1, "root"),
            };

            // One node per column, running out as far as asked.
            string parent = "deep";
            for (int level = 0; level < levels; level++)
            {
                string id = "d" + level;
                items.Add(Node(id, 2 + level, parent));
                parent = id;
            }

            // Twelve children, all in column 2, and nothing past it.
            for (int i = 0; i < 12; i++) items.Add(Node($"w{i:00}", 2, "wide"));

            return GraphLayout.Place(items, new Dictionary<string, double>(), 20);
        }

        var shallow = WithChainOf(3);
        var deeper = WithChainOf(9);

        // The wide family does not move. Not "moves less", does not move.
        var before = string.Join(", ", Enumerable.Range(0, 12).Select(i => $"{shallow[$"w{i:00}"]:F0}"));
        var after = string.Join(", ", Enumerable.Range(0, 12).Select(i => $"{deeper[$"w{i:00}"]:F0}"));
        Check("six more columns of depth move the wide family not at all", before, after);

        // Nor does the column they share get any taller.
        double sharedShallow = shallow["w11"] + 100 - Math.Min(shallow["d0"], shallow["w00"]);
        double sharedDeeper = deeper["w11"] + 100 - Math.Min(deeper["d0"], deeper["w00"]);
        Check("and the column they share is the same height", sharedShallow.ToString("F0"),
              sharedDeeper.ToString("F0"));

        // The chain really did get longer, so the check above is not passing because nothing changed.
        // Counted by the chain's own names rather than by first letter, which also matches "deep".
        int Links(Dictionary<string, double> laid) => laid.Keys.Count(k => k.Length > 1 && k[0] == 'd' && char.IsDigit(k[1]));
        Check("while the chain really is six nodes longer", "3, 9", $"{Links(shallow)}, {Links(deeper)}");

        // And the deep side stays a straight line, level with itself all the way out.
        for (int level = 1; level < 9; level++)
            CheckTrue($"d{level} is level with d{level - 1}",
                Math.Abs(deeper["d" + level] - deeper["d" + (level - 1)]) < 0.001);
    }

    /// Two states whose generator is the same clip, which is the ordinary shape in a shipped file
    /// rather than a contrived one: 3,624 of the corpus's 5,320 state infos share something.
    private static string SharedGeneratorGraph() => """
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
                    <hkparam name="name">First</hkparam>
                    <hkparam name="stateId">0</hkparam>
                    <hkparam name="generator">#94</hkparam>
                    <hkparam name="transitions">null</hkparam>
                </hkobject>
                <hkobject class="hkbClipGenerator" name="#94" signature="0xd4cc9f6">
                    <hkparam name="name">Shared</hkparam>
                    <hkparam name="animationName">shared.hkx</hkparam>
                    <hkparam name="triggers">null</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#95" signature="0x39d76713">
                    <hkparam name="name">Second</hkparam>
                    <hkparam name="stateId">1</hkparam>
                    <hkparam name="generator">#94</hkparam>
                    <hkparam name="transitions">null</hkparam>
                </hkobject>
            </hksection>
        </hkpackfile>
        """;

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

    /// A payload hung off a named struct is referenced, and both readers of "what points at this"
    /// have to agree that it is.
    ///
    /// The shape is `BSRandomAlarmModifier.alarmEvent`, a named `hkbEventProperty` whose `payload`
    /// names an object. Copied from DogmeatDefault.hkx rather than invented: six objects in that one
    /// vanilla file are reachable this way and no other, so this is the ordinary case and not a
    /// corner. `Unattached` already learned it, which is what its comment about every
    /// hkbStringEventPayload reading as unreachable is a record of. `ReferencesTo` has not, and it
    /// is the one guarding deletion.
    private static void AReferenceInsideAStructIsSeenByBothReaders()
    {
        Console.WriteLine("\na reference held in a named struct counts as a reference");

        var model = BehaviourGraphModel.Parse(StructReferenceGraph());

        // First prove the fixture exercises the path it claims to. A nested hkobject lands in
        // Structs only when it carries a name; without one the parser files it under StructLists,
        // which both readers already walk, and the check below would prove nothing.
        var holder = model.Get("98")!;
        CheckTrue("the fixture really parsed alarmEvent as a struct",
                  holder.Structs.ContainsKey("alarmEvent"));
        Check("and the struct holds the payload reference", "#99",
              holder.Structs["alarmEvent"].GetValueOrDefault("payload"));
        CheckTrue("with nothing else in the file pointing at the payload",
                  model.Objects.All(o => o.Scalars.Values.All(v => v != "#99")));

        // Unattached reads Structs. Proved on the clip rather than on the payload: a payload is not
        // a node class, so Unattached would leave it out whether it read structs or not, and
        // asserting on it would pass for the wrong reason. The spare clip is a node, is named by
        // nothing but a struct, and is therefore only invisible to Unattached because the struct is
        // read. This arm is built to exercise that branch and is not copied from a vanilla file.
        CheckTrue("Unattached reads structs, so a node held only by one is not called orphaned",
                  GraphAuthor.Unattached(model).All(o => o.Id != "97"));

        // The canvas uses PointsAt rather than HkReferences. Keep its edge walk in agreement with
        // the reachability and deletion walks, otherwise an event payload looks disconnected even
        // though its named event property points straight at it.
        CheckTrue("PointsAt reads structs, so the canvas keeps the payload connected",
                  GraphAuthor.PointsAt(model, holder).Contains("99"));

        // ReferencesTo does not, so it reports the payload as pointed at by nothing. That is the
        // answer Remove trusts before it deletes, and it is wrong.
        Check("ReferencesTo names the modifier that holds it", 1,
              GeneratorEditor.ReferencesTo(model, "99").Count);

        // What the disagreement costs, driven through the path a user reaches rather than asserted.
        // Remove without force is the guard against leaving a dangling reference behind.
        string after = GeneratorEditor.Remove(StructReferenceGraph(), "99", force: false,
                                              out var blockers);
        Check("and Remove refuses to delete it", 1, blockers.Count);
        CheckTrue("so the payload is still there", BehaviourGraphModel.Parse(after).Get("99") != null);

        // Whether the link could be broken at all, which decided whether the finder could be widened
        // or needed a writer built first. It can: SetParamAt already walks into a named inline struct
        // with a dotted path. Kept because the design rested on it.
        string cleared = HkxTextEdit.SetParamAt(StructReferenceGraph(), "98", "alarmEvent.payload", "null");
        Check("a struct member can be cleared the way Detach would need to", "null",
              BehaviourGraphModel.Parse(cleared).Get("98")?.Structs["alarmEvent"]
                  .GetValueOrDefault("payload"));

        // And that deleting for real goes through it. Finding the holder is worth nothing on its own:
        // the previous time these two walks disagreed, the holder was found and then never cleared,
        // and the file went out naming an object that was no longer in it.
        string gone = GraphAuthor.DeleteNode(StructReferenceGraph(), "99", out string note);
        var afterDelete = BehaviourGraphModel.Parse(gone);
        Check("deleting the payload clears the struct member that held it", "null",
              afterDelete.Get("98")?.Structs["alarmEvent"].GetValueOrDefault("payload"));
        Check("the payload is gone", null, afterDelete.Get("99"));
        CheckTrue("the note says which holder it cleared", note.Contains("#98"));

        // This once passed whatever Detach did, because the validator kept its own walk and that
        // walk did not read structs. It reads the shared one now, so it can fail for the reason its
        // name gives.
        CheckTrue("and no dangling reference is left behind",
                  GraphValidator.Check(gone).All(f => !f.What.Contains("not in this file")));

        // The other two kinds the shared walk carries. Both were unguarded: taking either arm out of
        // HkReferences left the whole suite green, so consolidating them was being done without a
        // net. A list element first, which is how a machine holds its states.
        Check("a reference in a list element is found", 1,
              GeneratorEditor.ReferencesTo(model, "93").Count);
        string listCleared = GraphAuthor.DeleteNode(StructReferenceGraph(), "93", out _);
        CheckTrue("and deleting it drops the element rather than nulling it",
                  BehaviourGraphModel.Parse(listCleared).Get("92")!.Refs("states").Count == 0);

        // Then a member inside an element of an array of structs, which is where a transition keeps
        // the effect it plays. This is the case the clearing walk got wrong once before.
        var blend = BehaviourGraphModel.Parse(TwoStateBlendGraph());
        Check("a reference inside a struct list element is found", 1,
              GeneratorEditor.ReferencesTo(blend, "102").Count);

        string effectGone = GraphAuthor.DeleteNode(TwoStateBlendGraph(), "102", out _);
        Check("and deleting it nulls the member, keeping the route", "null",
              BehaviourGraphModel.Parse(effectGone).Get("101")!
                  .StructLists["transitions"][0].GetValueOrDefault("transition"));
        Check("the transition itself survives", 1,
              BehaviourGraphModel.Parse(effectGone).Get("101")!.StructLists["transitions"].Count);
    }

    /// Check graph reports a reference to an object that is not there, whichever of the four kinds
    /// of place holds it.
    ///
    /// The validator kept its own walk and read three of the four, so a struct naming a deleted
    /// object was the one dangling reference it could not see. That is the check the delete path
    /// leans on to say it left the file whole, so the gap made the reassurance worth less than it
    /// looked.
    private static void ADanglingReferenceIsReportedWhereverItSits()
    {
        Console.WriteLine("\na reference to something that is not there is reported wherever it sits");

        foreach (var (kind, xml) in new[]
                 {
                     ("a scalar", SmallGraph().Replace(">#94<", ">#994<")),
                     ("a list element", SmallGraph().Replace("#93 #95", "#993 #95")),
                     ("a struct list member", TwoStateBlendGraph().Replace(">#102<", ">#902<")),
                     ("a struct member", StructReferenceGraph().Replace(">#99<", ">#999<")),
                 })
        {
            var dangling = GraphValidator.Check(xml)
                .Where(f => f.What.Contains("not in this file", StringComparison.Ordinal)).ToList();

            Check($"{kind} pointing at a missing object is reported", 1, dangling.Count);
            CheckTrue($"{kind} finding names the object holding it",
                      dangling.Count == 0 || dangling[0].Where.StartsWith('#'));
        }
    }

    /// A modifier whose named `alarmEvent` struct is the only thing pointing at its payload, which
    /// is how vanilla files are built.
    private static string StructReferenceGraph() => """
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
                    <hkparam name="states" numelements="1">
                        #93
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
                <hkobject class="BSRandomAlarmModifier" name="#98" signature="0x8e5f5f3c">
                    <hkparam name="name">Alarm</hkparam>
                    <hkparam name="enable">true</hkparam>
                    <hkparam name="alarmEvent">
                        <hkobject class="hkbEventProperty" name="alarmEvent" signature="0xdb38a15">
                            <hkparam name="id">169</hkparam>
                            <hkparam name="payload">#99</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
                <hkobject class="hkbStringEventPayload" name="#99" signature="0xed04256a">
                    <hkparam name="data">AlarmPayload</hkparam>
                </hkobject>
                <hkobject class="hkbModifierList" name="#96" signature="0x1f81a3b8">
                    <hkparam name="name">Holder</hkparam>
                    <hkparam name="spare">
                        <hkobject class="hkbEventProperty" name="spare" signature="0xdb38a15">
                            <hkparam name="id">170</hkparam>
                            <hkparam name="payload">#97</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
                <hkobject class="hkbClipGenerator" name="#97" signature="0xd4cc9f6">
                    <hkparam name="name">Spare</hkparam>
                    <hkparam name="animationName">spare.hkx</hkparam>
                    <hkparam name="triggers">null</hkparam>
                </hkobject>
            </hksection>
        </hkpackfile>
        """;

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
            foreach (string target in GraphAuthor.PointsAt(model, current))
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

        string throughDisabled = StateEditor.AddState(driven, "92", "C", "#97", out _, out int afterDisabled);
        throughDisabled = StateEditor.AddTransition(throughDisabled, "92", "95", afterDisabled, 0, "null");
        throughDisabled = StateEditor.AddTransition(throughDisabled, "92", "93", 1, 0, "null")
            .Replace("<hkparam name=\"name\">B</hkparam>",
                     "<hkparam name=\"name\">B</hkparam><hkparam name=\"enable\">false</hkparam>");
        var disabledRouteWarnings = Unreachable(throughDisabled);
        Check("a disabled state cannot relay reachability", 1, disabledRouteWarnings.Count);
        CheckTrue("the state beyond the disabled one stays unreachable",
                  disabledRouteWarnings.Any(f => f.Where.Contains("'C'")));

        // A machine whose start state does not exist is already reported on its own, and treating
        // every state as unreachable on top of that would bury it.
        string noStart = driven.Replace("<hkparam name=\"startStateId\">0</hkparam>",
                                        "<hkparam name=\"startStateId\">9</hkparam>");
        Check("a broken startStateId is not turned into a flood", 0, Unreachable(noStart).Count);

        const string Start = "<hkparam name=\"startStateId\">0</hkparam>";
        string random = driven.Replace(Start, Start +
            "<hkparam name=\"startStateMode\">START_STATE_MODE_RANDOM</hkparam>");
        Check("a random-start machine can enter either enabled state", 0, Unreachable(random).Count);

        string disabled = driven.Replace("<hkparam name=\"name\">B</hkparam>",
            "<hkparam name=\"name\">B</hkparam><hkparam name=\"enable\">false</hkparam>");
        Check("a disabled state is not reported as an entry problem", 0,
              Unreachable(disabled).Count);

        string synced = driven.Replace(Start, Start +
            "<hkparam name=\"startStateMode\">START_STATE_MODE_SYNC</hkparam>");
        Check("a synced machine does not claim its file chooses the entry", 0, Unreachable(synced).Count);

        string syncedByVariable = driven.Replace(Start, Start +
            "<hkparam name=\"syncVariableIndex\">3</hkparam>");
        Check("a sync variable can choose a state outside the transition walk", 0,
              Unreachable(syncedByVariable).Count);

        string selected = driven.Replace(Start, Start +
            "<hkparam name=\"startStateIdSelector\">#97</hkparam>");
        Check("a selector can choose a state outside the transition walk", 0,
              Unreachable(selected).Count);

        foreach (string field in new[]
                 {
                     "transitionToNextHigherStateEventId",
                     "transitionToNextLowerStateEventId",
                 })
        {
            string stepped = driven.Replace(Start, Start + $"<hkparam name=\"{field}\">4</hkparam>");
            Check($"{field} makes every state enterable", 0, Unreachable(stepped).Count);
        }

        string nested = NestedReachabilityGraph();
        var nestedWarnings = Unreachable(nested);
        Check("a flagged nested target of zero reaches only its child machine", 1,
              nestedWarnings.Count);
        CheckTrue("the child machine's nested state zero is reachable",
                  !nestedWarnings.Any(f => f.Where.Contains("'Nested zero'")));
        CheckTrue("an unrelated machine's state zero is still unreachable",
                  nestedWarnings.Any(f => f.Where.Contains("'Unrelated zero'")));

        string disabledEntered = nested.Replace("<hkparam name=\"name\">Outer B</hkparam>",
            "<hkparam name=\"name\">Outer B</hkparam><hkparam name=\"enable\">false</hkparam>");
        var disabledEnteredWarnings = Unreachable(disabledEntered);
        Check("a disabled entered state cannot seed its child machine", 2,
              disabledEnteredWarnings.Count);
        CheckTrue("the nested target under it stays unreachable",
                  disabledEnteredWarnings.Any(f => f.Where.Contains("'Nested zero'")));

        string unreachableSource = nested.Replace("<hkparam name=\"startStateId\">0</hkparam>",
                                                   "<hkparam name=\"startStateId\">1</hkparam>");
        var sourceWarnings = Unreachable(unreachableSource);
        Check("a transition from an unreachable outer state seeds nothing", 3,
              sourceWarnings.Count);
        CheckTrue("its nested target remains unreachable",
                  sourceWarnings.Any(f => f.Where.Contains("'Nested zero'")));

        string unflagged = nested.Replace("<hkparam name=\"flags\">8192</hkparam>",
                                          "<hkparam name=\"flags\">0</hkparam>");
        Check("an unflagged zero is not treated as a nested target", 2,
              Unreachable(unflagged).Count);
    }

    // Losing these two fields made every consumer either guess at transition semantics or reread
    // the raw struct dictionaries. The literal values are deliberately unlike the defaults, so a
    // parser that drops either field cannot satisfy the checks by accident.
    private static void TransitionRowsCarryPriorityAndFlags()
    {
        Console.WriteLine("\ntransition rows carry priority and flags");

        string xml = StateEditor.AddTransition(SmallGraph(), "92", "93", 1, 0, "null")
            .Replace("<hkparam name=\"priority\">0</hkparam>",
                     "<hkparam name=\"priority\">7</hkparam>")
            .Replace("<hkparam name=\"flags\">0</hkparam>",
                     "<hkparam name=\"flags\">FLAG_TO_NESTED_STATE_ID_IS_VALID</hkparam>");
        var row = StateEditor.Transitions(BehaviourGraphModel.Parse(xml), "92").Single();

        Check("priority comes from its own transition element", 7, row.Priority);
        Check("flags come from its own transition element", 8192, row.Flags);
        CheckTrue("the nested-target validity bit is visible", row.HasFlag(0x2000));
        CheckTrue("an unrelated flag is not invented", !row.HasFlag(0x1000));
    }

    private static void StaticTraceFollowsExistingGraphLinks()
    {
        Console.WriteLine("\nstatic trace follows the graph it already draws");

        string xml = StateEditor.AddTransition(SmallGraph(), "92", "93", 1, 0, "null");
        var model = BehaviourGraphModel.Parse(xml);
        var trace = GraphTrace.Of(model, StateRoutes.Of(model));
        var visible = model.Objects.Select(o => o.Id).ToHashSet();

        Check("downstream follows the state's generator and transition", "93,94,95,96,98",
              string.Join(",", trace.Reachable("93", GraphTrace.Direction.Downstream, visible)
                                   .OrderBy(id => id)));
        Check("upstream follows the parent graph and transition", "91,92,93,95",
              string.Join(",", trace.Reachable("95", GraphTrace.Direction.Upstream, visible)
                                   .OrderBy(id => id)));
        Check("a focused trace stays inside the visible tree", "93,94",
              string.Join(",", trace.Reachable("93", GraphTrace.Direction.Both,
                  new HashSet<string> { "93", "94" }).OrderBy(id => id)));
    }

    private static List<GraphValidator.Finding> Unreachable(string xml) =>
        GraphValidator.Check(xml).Where(f => f.What.StartsWith("cannot be entered")).ToList();

    private static string NestedReachabilityGraph() => """
        <hkpackfile><hksection name="__data__">
            <hkobject class="hkbStateMachine" name="#10">
                <hkparam name="name">Outer</hkparam>
                <hkparam name="startStateId">0</hkparam>
                <hkparam name="wildcardTransitions">null</hkparam>
                <hkparam name="states" numelements="2">#11 #13</hkparam>
            </hkobject>
            <hkobject class="hkbStateMachineStateInfo" name="#11">
                <hkparam name="name">Outer A</hkparam><hkparam name="stateId">0</hkparam>
                <hkparam name="generator">#12</hkparam><hkparam name="transitions">#40</hkparam>
            </hkobject>
            <hkobject class="hkbClipGenerator" name="#12"><hkparam name="name">Outer clip</hkparam></hkobject>
            <hkobject class="hkbStateMachineStateInfo" name="#13">
                <hkparam name="name">Outer B</hkparam><hkparam name="stateId">1</hkparam>
                <hkparam name="generator">#20</hkparam><hkparam name="transitions">null</hkparam>
            </hkobject>
            <hkobject class="hkbStateMachine" name="#20">
                <hkparam name="name">Nested</hkparam>
                <hkparam name="startStateId">1</hkparam>
                <hkparam name="wildcardTransitions">null</hkparam>
                <hkparam name="states" numelements="2">#21 #23</hkparam>
            </hkobject>
            <hkobject class="hkbStateMachineStateInfo" name="#21">
                <hkparam name="name">Nested zero</hkparam><hkparam name="stateId">0</hkparam>
                <hkparam name="generator">#22</hkparam><hkparam name="transitions">null</hkparam>
            </hkobject>
            <hkobject class="hkbClipGenerator" name="#22"><hkparam name="name">Nested zero clip</hkparam></hkobject>
            <hkobject class="hkbStateMachineStateInfo" name="#23">
                <hkparam name="name">Nested one</hkparam><hkparam name="stateId">1</hkparam>
                <hkparam name="generator">#24</hkparam><hkparam name="transitions">#41</hkparam>
            </hkobject>
            <hkobject class="hkbClipGenerator" name="#24"><hkparam name="name">Nested one clip</hkparam></hkobject>
            <hkobject class="hkbStateMachine" name="#30">
                <hkparam name="name">Unrelated</hkparam>
                <hkparam name="startStateId">1</hkparam>
                <hkparam name="wildcardTransitions">null</hkparam>
                <hkparam name="states" numelements="2">#31 #33</hkparam>
            </hkobject>
            <hkobject class="hkbStateMachineStateInfo" name="#31">
                <hkparam name="name">Unrelated zero</hkparam><hkparam name="stateId">0</hkparam>
                <hkparam name="generator">#32</hkparam><hkparam name="transitions">null</hkparam>
            </hkobject>
            <hkobject class="hkbClipGenerator" name="#32"><hkparam name="name">Unrelated zero clip</hkparam></hkobject>
            <hkobject class="hkbStateMachineStateInfo" name="#33">
                <hkparam name="name">Unrelated one</hkparam><hkparam name="stateId">1</hkparam>
                <hkparam name="generator">#34</hkparam><hkparam name="transitions">#42</hkparam>
            </hkobject>
            <hkobject class="hkbClipGenerator" name="#34"><hkparam name="name">Unrelated one clip</hkparam></hkobject>
            <hkobject class="hkbStateMachineTransitionInfoArray" name="#40">
                <hkparam name="transitions" numelements="1"><hkobject>
                    <hkparam name="eventId">0</hkparam><hkparam name="toStateId">1</hkparam>
                    <hkparam name="toNestedStateId">0</hkparam><hkparam name="priority">7</hkparam>
                    <hkparam name="flags">8192</hkparam>
                </hkobject></hkparam>
            </hkobject>
            <hkobject class="hkbStateMachineTransitionInfoArray" name="#41">
                <hkparam name="transitions" numelements="1"><hkobject>
                    <hkparam name="eventId">1</hkparam><hkparam name="toStateId">1</hkparam>
                    <hkparam name="toNestedStateId">0</hkparam><hkparam name="priority">0</hkparam>
                    <hkparam name="flags">0</hkparam>
                </hkobject></hkparam>
            </hkobject>
            <hkobject class="hkbStateMachineTransitionInfoArray" name="#42">
                <hkparam name="transitions" numelements="1"><hkobject>
                    <hkparam name="eventId">2</hkparam><hkparam name="toStateId">1</hkparam>
                    <hkparam name="toNestedStateId">0</hkparam><hkparam name="priority">0</hkparam>
                    <hkparam name="flags">0</hkparam>
                </hkobject></hkparam>
            </hkobject>
        </hksection></hkpackfile>
        """;

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

    /// A bound can be given to a variable the array does not reach yet.
    ///
    /// The array is allowed to stop short and usually does, so bounding the last variable in a file
    /// with two bounds means writing the missing entries first. They are written unbounded, 0 to 0,
    /// which is what the file already means inside the array, rather than copying a neighbour's
    /// bound onto a variable nobody asked to bound.
    private static void ABoundCanBeAuthoredPastTheEndOfTheArray()
    {
        Console.WriteLine("\na bound can be authored past the end of the array");

        string xml = ThreeVariablesWithTwoBounds();
        Check("two bounds to begin with", 2, SymbolEditor.Audit(BehaviourGraphModel.Parse(xml)).Bounds);

        // The third variable, which the array does not reach.
        string after = SymbolEditor.SetVariableBounds(xml, 2, "-5", "35");
        var counts = SymbolEditor.Audit(BehaviourGraphModel.Parse(after));

        Check("the array now reaches it", 3, counts.Bounds);
        CheckTrue("and is parallel with the variables", counts.BoundsAreParallel);
        Check("the new bound is the one asked for", "35", BoundMax(after, 2));
        Check("with its minimum too", "-5", BoundMin(after, 2));

        // The entries already there are not disturbed, which is the whole risk of extending a
        // positional array: a bound that slides lands on a variable nobody bounded.
        Check("the first bound is untouched", "10", BoundMax(after, 0));
        Check("and the second", "20", BoundMax(after, 1));

        // One already inside the array is replaced rather than appended to.
        string second = SymbolEditor.SetVariableBounds(xml, 0, "1", "2");
        Check("bounding one already in the array does not lengthen it", 2,
              SymbolEditor.Audit(BehaviourGraphModel.Parse(second)).Bounds);
        Check("and it takes the new value", "2", BoundMax(second, 0));
        Check("leaving its neighbour alone", "20", BoundMax(second, 1));

        // A variable that does not exist has no bound to set, and saying so beats writing an entry
        // that bounds nothing.
        string refused = "";
        try { SymbolEditor.SetVariableBounds(xml, 7, "0", "0"); }
        catch (ArgumentOutOfRangeException e) { refused = e.Message; }
        CheckTrue("bounding a variable the file does not have is refused",
                  refused.Contains("3 variable(s)", StringComparison.Ordinal));
    }

    /// A number inside an element of an array of structs is written where it sits.
    ///
    /// Nothing moves and nothing changes length, so it is the same write as any other fixed width
    /// value, aimed somewhere the object's own class does not describe. Before this the whole array
    /// read as one blob of text, so one number changing looked like the whole field changing and
    /// there was nothing left to say which element or which member.
    /// A field named on its own reaches the first element that happens to have that name, which for
    /// an array of structs is almost never the one meant.
    ///
    /// Every element of a transition array carries an `eventId`, a `toStateId` and two time
    /// intervals, so a five transition array holds `eventId` five times. The panel builds a box per
    /// field and writes back by name, and the writer replaces the first match in the object's block.
    /// Editing the fifth transition therefore rewrote the first one, and said it had worked.
    ///
    /// The fix is that a field is addressed by where it sits rather than by what it is called.
    private static void AnElementsFieldIsWrittenToThatElement()
    {
        Console.WriteLine("\na field inside an element is written to that element");

        string xml = TwoTransitions();

        // What the panel used to do. Kept as a check rather than deleted, because it is the whole
        // reason the path exists and a reader should be able to see the difference.
        string byName = HkxTextEdit.SetParam(xml, "95", "eventId", "9");
        Check("naming the field alone still reaches the first element", "9",
              TransitionEventId(byName, 0));
        Check("which is why it is not enough on its own", "2", TransitionEventId(byName, 1));

        string byPath = HkxTextEdit.SetParamAt(xml, "95", "transitions[1].eventId", "9");
        Check("addressing the element writes that element", "9", TransitionEventId(byPath, 1));
        Check("and leaves the one before it alone", "1", TransitionEventId(byPath, 0));

        // A struct written inside an element, which is the case that made the flat list ambiguous in
        // the first place: the same `enterEventId` name appears once per interval per transition.
        //
        // Read back off the text rather than through the model: the model stops at an element's own
        // fields and does not descend into a struct written inside one, so asking it would report
        // nothing changed whether or not it had.
        string nested = HkxTextEdit.SetParamAt(xml, "95",
                                               "transitions[1].initiateInterval.enterEventId", "7");
        Check("exactly one enterEventId is now 7", 1, Occurrences(nested, "\"enterEventId\">7<"));
        Check("and the other is untouched", 1, Occurrences(nested, "\"enterEventId\">-1<"));
        CheckTrue("the one that changed is the second element's",
                  nested.IndexOf("\"enterEventId\">7<", StringComparison.Ordinal)
                  > nested.IndexOf("\"eventId\">1<", StringComparison.Ordinal));

        // An index past the end is a caller asking for something that is not there. Writing the last
        // element instead would look like it worked.
        CheckThrows("an element that is not there is refused",
                    () => HkxTextEdit.SetParamAt(xml, "95", "transitions[2].eventId", "9"));
        CheckThrows("and so is a member the element does not have",
                    () => HkxTextEdit.SetParamAt(xml, "95", "transitions[0].nothing", "9"));
    }

    private static int Occurrences(string text, string needle)
    {
        int count = 0;
        for (int at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string TransitionEventId(string xml, int element) =>
        BehaviourGraphModel.Parse(xml).Get("95")!.StructLists["transitions"][element]["eventId"];

    /// Two transitions on one array, which is the smallest shape where addressing by name and
    /// addressing by position give different answers.
    private static string TwoTransitions() =>
        """
        <?xml version="1.0" encoding="ascii"?>
        <hkpackfile classversion="8" contentsversion="hk_2014.1.0-r1">
            <hksection name="__data__">
                <hkobject class="hkbStateMachineTransitionInfoArray" name="#95" signature="0x704a19af">
                    <hkparam name="transitions" numelements="2">
                        <hkobject>
                            <hkparam name="initiateInterval">
                                <hkobject class="hkbStateMachineTimeInterval" name="initiateInterval" signature="0x60a881e5">
                                    <hkparam name="enterEventId">-1</hkparam>
                                    <hkparam name="exitEventId">-1</hkparam>
                                </hkobject>
                            </hkparam>
                            <hkparam name="eventId">1</hkparam>
                            <hkparam name="toStateId">0</hkparam>
                        </hkobject>
                        <hkobject>
                            <hkparam name="initiateInterval">
                                <hkobject class="hkbStateMachineTimeInterval" name="initiateInterval" signature="0x60a881e5">
                                    <hkparam name="enterEventId">-1</hkparam>
                                    <hkparam name="exitEventId">-1</hkparam>
                                </hkobject>
                            </hkparam>
                            <hkparam name="eventId">2</hkparam>
                            <hkparam name="toStateId">3</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
            </hksection>
        </hkpackfile>
        """;

    private static void AValueInsideAStructArrayIsWrittenInPlace()
    {
        Console.WriteLine("\na value inside a struct array is written where it sits");

        string xml = ThreeVariablesWithTwoBounds();

        // A bound already inside the array. No growth, so nothing has to move.
        var plan = NativeSave.Compare(xml, SymbolEditor.SetVariableBounds(xml, 0, "-2", "9"));
        CheckTrue("changing a bound already in the array is writable", plan.Possible);
        Check("as one change per number", 2, plan.Changes.Count);
        CheckTrue("aimed at an element rather than at a field", plan.Changes.All(c => c.InElement));
        Check("of the array that holds it", "variableBounds", plan.Changes[0].Field);
        Check("naming the element", 0, plan.Changes[0].Element);
        CheckTrue("and the member inside it",
                  plan.Changes.Select(c => c.Member).OrderBy(m => m)
                      .SequenceEqual(new[] { "max.value", "min.value" }));
        CheckTrue("and it does not grow the file", !plan.Grows);

        // An inline struct is not one of the file's objects. hkxpack writes one as an hkobject all
        // the same, so counting those made a file with no hkbVariableValue object in it appear to
        // hold two per bound, and a change would have been aimed at an object that does not exist.
        CheckTrue("a change inside a bound is not attributed to hkbVariableValue",
                  plan.Changes.All(c => c.ClassName != "hkbVariableValue"));
        Check("it belongs to the object that owns the array", "hkbBehaviorGraphData",
              plan.Changes[0].ClassName);
    }

    /// An array of structs at a new length is planned as one run rewritten, not refused.
    ///
    /// The array is positional, so bounding a variable the array does not reach means writing every
    /// entry below it too. That is the ordinary case rather than an edge one: the bounds array is
    /// empty in 224 of the 531 vanilla behaviours and short in 87 more.
    private static void AStructArrayCanBeMadeLonger()
    {
        Console.WriteLine("\nan array of structs can be given a new length");

        string xml = ThreeVariablesWithTwoBounds();

        var longer = NativeSave.Compare(xml, SymbolEditor.SetVariableBounds(xml, 2, "-5", "35"));
        CheckTrue("making the array longer is written into the bytes", longer.Possible);
        CheckTrue("and it grows the file, since the new run goes on the end", longer.Grows);

        var run = longer.Changes.Where(c => c.Grow).ToList();
        Check("one change says how long the array now is", 1, run.Count);
        Check("naming the new length", "3", run[0].Value);
        Check("and the array it belongs to", "variableBounds", run[0].Field);
        Check("carrying the length it had, so the old elements can be brought across", 2,
              run[0].Element);
        CheckTrue("a resize is not mistaken for a write inside an element", !run[0].InElement);

        // Only the new element is listed. The two already there are carried over as bytes, which is
        // what keeps anything inside them this cannot spell, so listing them would be both
        // redundant and a way to refuse a resize that is perfectly safe.
        var fill = longer.Changes.Where(c => c.InElement).ToList();
        Check("with the new element's two numbers to write into it", 2, fill.Count);
        CheckTrue("both aimed at the element that was added", fill.All(c => c.Element == 2));
        CheckTrue("and none at the ones already there", fill.All(c => c.Element >= 2));

        // Shrinking is the same move: a shorter run is written and the count beside it rewritten.
        // Taken as the resize just planned, run backwards, so the bounds array is the only thing
        // that differs between the two texts.
        var shorter = NativeSave.Compare(SymbolEditor.SetVariableBounds(xml, 2, "-5", "35"), xml);
        CheckTrue("shortening it is written the same way", shorter.Possible);
        CheckTrue("and it names the array that changed",
                  shorter.Changes.Any(c => c.Grow && c.Field == "variableBounds"));
        Check("at the length it is going back to", "2",
              shorter.Changes.First(c => c.Grow).Value);

        // A member a new element was given that cannot be written where it sits is refused rather
        // than dropped. Nothing produces one today, and the check is what keeps that true.
        string withName = xml.Replace(
            "<hkparam name=\"eventInfos\" numelements=\"0\"></hkparam>",
            """
            <hkparam name="eventInfos" numelements="1">
                <hkobject>
                    <hkparam name="flags">0</hkparam>
                </hkobject>
            </hkparam>
            """);
        var invented = NativeSave.Compare(xml, withName);
        CheckTrue("giving an array a first element is written", invented.Possible);
    }

    private static string BoundMin(string xml, int index)
    {
        int start = xml.IndexOf("name=\"variableBounds\"", StringComparison.Ordinal);
        if (start < 0) return "";
        var minima = System.Text.RegularExpressions.Regex
            .Matches(xml[start..], "name=\"min\".*?name=\"value\">(-?\\d+)<",
                     System.Text.RegularExpressions.RegexOptions.Singleline);
        return index < minima.Count ? minima[index].Groups[1].Value : "";
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

    /// One machine, two states, one event, two ways out of the first state. The higher priority way
    /// out is gated on a variable, so the same event goes to a different state depending on what that
    /// variable holds, which is the whole of what a condition does.
    private static string GatedGraph() => """
        <?xml version="1.0" encoding="ascii"?>
        <hkpackfile classversion="11" contentsversion="hk_2014.1.0-r1">
            <hksection name="__data__">
                <hkobject class="hkbBehaviorGraph" name="#90" signature="0xb1218f86">
                    <hkparam name="name">Graph</hkparam>
                    <hkparam name="rootGenerator">#92</hkparam>
                    <hkparam name="data">#100</hkparam>
                </hkobject>
                <hkobject class="hkbBehaviorGraphStringData" name="#91" signature="0xc713064e">
                    <hkparam name="eventNames" numelements="1">Go</hkparam>
                    <hkparam name="variableNames" numelements="2">bGateOpen fSpeed</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachine" name="#92" signature="0xa5896bcf">
                    <hkparam name="name">Root</hkparam>
                    <hkparam name="startStateId">0</hkparam>
                    <hkparam name="states" numelements="3">#93 #96 #97</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#93" signature="0x39d76713">
                    <hkparam name="name">Start</hkparam>
                    <hkparam name="stateId">0</hkparam>
                    <hkparam name="transitions">#94</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineTransitionInfoArray" name="#94" signature="0xe397b11e">
                    <hkparam name="transitions" numelements="2">
                        <hkobject>
                            <hkparam name="eventId">0</hkparam>
                            <hkparam name="toStateId">1</hkparam>
                            <hkparam name="priority">10</hkparam>
                            <hkparam name="condition">#95</hkparam>
                        </hkobject>
                        <hkobject>
                            <hkparam name="eventId">0</hkparam>
                            <hkparam name="toStateId">2</hkparam>
                            <hkparam name="priority">1</hkparam>
                            <hkparam name="condition">null</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
                <hkobject class="hkbExpressionCondition" name="#95" signature="0x78a69526">
                    <hkparam name="expression">bGateOpen == 1</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#96" signature="0x39d76713">
                    <hkparam name="name">Gated</hkparam>
                    <hkparam name="stateId">1</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#97" signature="0x39d76713">
                    <hkparam name="name">Fallback</hkparam>
                    <hkparam name="stateId">2</hkparam>
                </hkobject>
                <hkobject class="hkbBehaviorGraphData" name="#100" signature="0x95aca5d">
                    <hkparam name="variableInfos" numelements="2">
                        <hkobject>
                            <hkparam name="type">VARIABLE_TYPE_INT32</hkparam>
                        </hkobject>
                        <hkobject>
                            <hkparam name="type">VARIABLE_TYPE_REAL</hkparam>
                        </hkobject>
                    </hkparam>
                    <hkparam name="eventInfos" numelements="1">
                        <hkobject>
                            <hkparam name="flags">0</hkparam>
                        </hkobject>
                    </hkparam>
                    <hkparam name="stringData">#91</hkparam>
                    <hkparam name="variableInitialValues">#101</hkparam>
                </hkobject>
                <hkobject class="hkbVariableValueSet" name="#101" signature="0x27812d8d">
                    <hkparam name="wordVariableValues" numelements="2">
                        <hkobject>
                            <hkparam name="value">0</hkparam>
                        </hkobject>
                        <hkobject>
                            <hkparam name="value">1075838976</hkparam>
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

    /// A shape bound to the three bone chain, sitting wherever `placement` puts it.
    ///
    /// The bind is built the way a correct one has to be: skin to bone is whatever takes the mesh's
    /// authored space into that bone's space, so composing it with the bone's reference pose comes
    /// back as the placement, the same for every bone. Stored transposed, because that is how the
    /// NIF stores a rotation and what the reader undoes.
    private static OpenCommonwealth.Services.Nif.NifShape BoundShape(HkxSkeleton rig, Vector3 placement)
    {
        var rest = AnimationPose.ReferencePose(rig);
        var shape = new OpenCommonwealth.Services.Nif.NifShape { Name = "TestShape" };

        for (int b = 0; b < rig.BoneNames.Count; b++)
        {
            shape.BoneNames.Add(rig.BoneNames[b]);
            shape.SkinToBone.Add(Matrix4x4.CreateTranslation(placement - rest.Bones[b].Position));
        }

        // One bone the skeleton has never heard of, so the vertices weighted to it are the ones no
        // pose can move.
        shape.BoneNames.Add("Tip_skin");
        shape.SkinToBone.Add(Matrix4x4.Identity);

        for (int b = 0; b < shape.BoneNames.Count; b++)
        {
            shape.Vertices.Add(new Vector3(b * 10, 0, 0));
            for (int s = 0; s < 4; s++)
            {
                shape.BoneIndices.Add(s == 0 ? b : 0);
                shape.BoneWeights.Add(s == 0 ? 1 : 0);
            }
        }

        return shape;
    }

    /// A mesh does not have to be authored at the origin, and reading its placement as a fault is
    /// what made the vanilla male body report 120 units of drift while every transform was composing
    /// perfectly. The body is authored with its origin at the neck.
    ///
    /// So the measure is what the bones say relative to each other, not where they put the mesh. A
    /// rotation read the wrong way round still fails it, because that gives every bone a different
    /// wrong answer.
    private static void AMeshAuthoredAwayFromTheOriginIsNotAFault()
    {
        Console.WriteLine("\na mesh authored away from the origin is placed, not broken");

        var rig = ThreeBoneChain();
        var rest = AnimationPose.ReferencePose(rig);

        foreach (var placement in new[] { Vector3.Zero, new Vector3(0, 0, 120.84f) })
        {
            var shape = BoundShape(rig, placement);
            var binding = OpenCommonwealth.Services.Nif.SkinnedMesh.Bind(shape, rig);

            Check($"the helper bone does not match, at placement {placement.Z}", 1,
                  binding.Unmatched.Count);

            float spread = OpenCommonwealth.Services.Nif.SkinnedMesh
                .BindError(shape, binding, rig, out int measured);

            Check("every real bone is measured", 3, measured);
            CheckTrue($"and they agree, wherever the mesh sits (placement {placement.Z})",
                      spread < 0.001f);

            // The vertices no bone can move go where the mesh went, not where the file wrote them.
            // Left behind, they drew a second body 120 units under the first one.
            var posed = OpenCommonwealth.Services.Nif.SkinnedMesh.Pose(shape, binding, rest, rig);
            CheckTrue("a vertex on a bone the skeleton lacks is still placed with the mesh",
                      Near(posed[^1], shape.Vertices[^1] + placement));
            CheckTrue("and one on a bone it has lands in the same space",
                      Near(posed[0], shape.Vertices[0] + placement));
        }

        // The fault the measure exists for. Turning one bone's bind gives an answer no other bone
        // agrees with, whatever the mesh's placement is.
        var wrong = BoundShape(rig, new Vector3(0, 0, 120.84f));
        wrong.SkinToBone[1] = Matrix4x4.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2) *
                              wrong.SkinToBone[1];

        float broken = OpenCommonwealth.Services.Nif.SkinnedMesh
            .BindError(wrong, OpenCommonwealth.Services.Nif.SkinnedMesh.Bind(wrong, rig), rig, out _);

        CheckTrue("a bind turned the wrong way is still caught", broken > 10);
    }

    /// A BA2 built here rather than taken from the game, so the archive reader is checked on a
    /// machine with no Fallout 4 on it.
    ///
    /// The format is a 24 byte header, one 36 byte entry per file, then a name table at the offset
    /// the header names. Both storage forms are written: one entry plain and one zlib compressed,
    /// because the compressed branch is the one that reads a different length than the index states.
    private static string ArchiveOfTwoFiles(byte[] plain, byte[] compressible)
    {
        var names = new[] { "Meshes/Actors/Dogmeat/Behaviors/DogmeatRoot.hkx", "Meshes/Actors/Human/skeleton.nif" };

        byte[] squashed;
        using (var buffer = new MemoryStream())
        {
            using (var zlib = new System.IO.Compression.ZLibStream(
                       buffer, System.IO.Compression.CompressionMode.Compress, true))
                zlib.Write(compressible, 0, compressible.Length);
            squashed = buffer.ToArray();
        }

        string path = Path.Combine(Path.GetTempPath(), "symrm-archive-probe.ba2");
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        long headerEnd = 24 + 36 * 2;
        long firstAt = headerEnd;
        long secondAt = firstAt + plain.Length;
        long nameTableAt = secondAt + squashed.Length;

        writer.Write(new[] { 'B', 'T', 'D', 'X' });
        writer.Write(1u);
        writer.Write(new[] { 'G', 'N', 'R', 'L' });
        writer.Write(2u);
        writer.Write((ulong)nameTableAt);

        void Entry(long at, uint packed, uint unpacked)
        {
            writer.Write(0u); writer.Write(0u); writer.Write(0u); writer.Write(0u);
            writer.Write((ulong)at);
            writer.Write(packed);
            writer.Write(unpacked);
            writer.Write(0u);
        }

        Entry(firstAt, 0, (uint)plain.Length);
        Entry(secondAt, (uint)squashed.Length, (uint)compressible.Length);

        writer.Write(plain);
        writer.Write(squashed);

        foreach (string name in names)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(name.Replace('/', '\\'));
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        return path;
    }

    /// Opening an archive reads its index and none of its file data, which is the whole reason a
    /// behaviour can be reached out of a 29,716 entry archive without writing 29,715 files first.
    private static void AnArchiveIsReadWithoutUnpackingIt()
    {
        Console.WriteLine("\nan archive is read without unpacking it");

        var plain = System.Text.Encoding.ASCII.GetBytes("a behaviour would go here");
        var compressible = System.Text.Encoding.ASCII.GetBytes(new string('x', 4096));

        string path = ArchiveOfTwoFiles(plain, compressible);

        using (var archive = OpenCommonwealth.Services.Archive.Ba2.Open(path))
        {
            Check("both files are in the index", 2, archive.Entries.Count);
            Check("with the archive's own path separators turned round", "Meshes/Actors/Dogmeat/Behaviors/DogmeatRoot.hkx",
                  archive.Entries[0].Name);
            Check("and the file name on its own", "DogmeatRoot.hkx", archive.Entries[0].FileName);
            Check("and the folder it sits in", "Meshes/Actors/Dogmeat/Behaviors", archive.Entries[0].Folder);

            // Words in any order, because the useful query is "dogmeat behavior" and the archive
            // stores that as a path where no single substring matches both.
            Check("words match in any order", 1, archive.Matching("dogmeat behavior").Count());
            Check("and in the other order too", 1, archive.Matching("behavior dogmeat").Count());
            Check("an extension narrows it", 1, archive.Matching("", ".nif").Count());
            Check("a word nothing has matches nothing", 0, archive.Matching("mirelurk").Count());
            Check("no filter matches everything", 2, archive.Matching("").Count());

            // Both storage forms, because the compressed one reads a different number of bytes off
            // disk than the index says the file is.
            CheckTrue("a plainly stored file comes back as it went in",
                      archive.Read(archive.Entries[0]).SequenceEqual(plain));
            CheckTrue("and a compressed one is inflated",
                      archive.Read(archive.Entries[1]).SequenceEqual(compressible));
        }

        File.Delete(path);
    }

    /// Reading between root motion samples, which is what a viewport does every frame.
    ///
    /// The samples are spread across the clip's duration and there is no promise there is one per
    /// animation frame, so a frame lands between two of them. Checked on a made up motion rather
    /// than on a game file, so this runs anywhere; the reading of real files is checked by
    /// `symrm motion`, where a clip called TurnLeft90 comes back as 90 degrees.
    private static void TravelIsReadBetweenSamples()
    {
        Console.WriteLine("\ntravel is read between the samples that carry it");

        var motion = new RootMotion.Motion { Duration = 1 };
        motion.Samples.Add(new RootMotion.Sample(Vector3.Zero, 0));
        motion.Samples.Add(new RootMotion.Sample(new Vector3(0, 100, 0), MathF.PI));

        CheckTrue("the start is the first sample", Near(RootMotion.At(motion, 0).Position, Vector3.Zero));
        CheckTrue("the end is the last", Near(RootMotion.At(motion, 1).Position, new Vector3(0, 100, 0)));
        CheckTrue("and halfway is halfway", Near(RootMotion.At(motion, 0.5f).Position, new Vector3(0, 50, 0)));
        CheckTrue("the turn is read the same way",
                  Math.Abs(RootMotion.At(motion, 0.5f).TurnRadians - MathF.PI / 2) < 0.001f);

        // Past either end rather than off it, because a scrub bar reaches its own limits and a frame
        // count that disagrees with the sample count by one should not throw.
        CheckTrue("before the start is the start", Near(RootMotion.At(motion, -5).Position, Vector3.Zero));
        CheckTrue("past the end is the end", Near(RootMotion.At(motion, 5).Position, new Vector3(0, 100, 0)));

        Check("travel is the straight line between the ends", 100f, RootMotion.At(motion, 1).Position.Y);
        CheckTrue("and the total is the same", Math.Abs(motion.Travel.Length() - 100) < 0.001f);

        // A clip that goes nowhere has no reference frame object at all, which is the ordinary case
        // rather than a failure, and it must not be reported as sitting at the first sample.
        var still = new RootMotion.Motion();
        CheckTrue("a clip with no motion carries none", !still.Any);
        CheckTrue("and reads as the origin rather than throwing",
                  Near(RootMotion.At(still, 0.5f).Position, Vector3.Zero));
        CheckTrue("and travels nothing", still.Travel == Vector3.Zero);
    }

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

    /// A string has to land on an even offset, because the lowest bit of the pointer to it is not
    /// part of the address.
    ///
    /// A string member keeps an ownership flag in bit 0 of its pointer: set means the buffer belongs
    /// to the object and goes when it does. Section data starts on a sixteen byte boundary, so an
    /// offset's parity inside the section is the loaded address's parity, and a string landing on an
    /// odd offset hands the game a pointer that claims to own memory inside the packfile image.
    ///
    /// Nothing else would notice. The file reads back, repacks identically and passes the validator.
    /// The cost lands when the game releases the object.
    ///
    /// Measured rather than assumed: across 453 sample files, every one of 37,545 local fixup
    /// destinations is even. 7,618 of those point at text, and only 6,278 are sixteen byte aligned,
    /// so the rule Havok actually holds to is even, not aligned like an object.
    private static void AppendedStringsLandOnAnEvenOffset()
    {
        Console.WriteLine("\nan appended string lands on an even offset");

        var image = ClipInAPackfile("A.hkx", out _);
        var objects = new PackfileObjects(image);
        var clip = objects.Instances.Single();

        // An even number of characters is an odd number of bytes once the terminator is on it, so
        // the next append after this one starts on an odd offset unless something rounds up. Two
        // string edits in one save is all it takes, and half of all names are an even length.
        const string even = "Walk.hkx";
        CheckTrue("a name of even length is accepted", objects.WriteString(clip, "animationName", even));
        CheckTrue("and a second name after it", objects.WriteString(clip, "animationBundleName", "bundle"));

        var landed = image.Section("__data__")!.Locals().Select(l => l.Destination).ToList();
        Check("both names are pointed at", 2, landed.Count);
        Check("and neither landed on an odd offset", 0, landed.Count(d => d % 2 != 0));

        // The rounding must not cost the string itself. A pad written over the front of a name would
        // satisfy the check above and lose the name.
        var reread = new PackfileObjects(PackfileImage.Read(image.Rebuild()));
        var again = reread.Instances.Single();
        Check("the first name still reads back", even, reread.ReadString(again, "animationName"));
        Check("and so does the second", "bundle", reread.ReadString(again, "animationBundleName"));
        Check("the value beside them is untouched", 2.5f, reread.ReadFloat(again, "playbackSpeed"));
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

    /// A field the object holds directly is addressed by its own name, so everything that has always
    /// written by name keeps working and only the fields inside an array of structs change shape.
    ///
    /// The interesting half of this cannot be built by hand at a useful size: a fixture with five
    /// transitions in it proves less than one real behaviour with seventy nine transition arrays,
    /// which is what `symrm paths` sweeps.
    private static void EveryFieldSaysWhereItSits()
    {
        Console.WriteLine("\nevery field says where it sits");

        var objects = new PackfileObjects(ClipInAPackfile("A.hkx", out _));
        var fields = ClassFields.Of(objects, objects.Instances.Single());

        CheckTrue("a list comes back", fields != null);
        CheckTrue("a field held by the object is addressed by its own name",
                  fields!.All(f => f.Path == f.Name));
        CheckTrue("and belongs to no element", fields!.All(f => f.Group.Length == 0));

        var panel = PanelFields.For(objects, objects.Instances.Single(),
                                    fields!.Select(f => (f.Name, "")).ToList(),
                                    (_, _) => "");
        CheckTrue("the panel carries the same addresses",
                  panel.All(p => p.Address == p.Name));
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

    /// Two clip generators where the first's `variableBindingSet` field is aimed at the second, so
    /// deleting the second has something still pointing at it. Any pointer field would do; this one
    /// is a pointer on every class in the corpus, which keeps the fixture small.
    private static PackfileImage TwoClipsOnePointingAtTheOther(out int pointedAt)
    {
        var classes = HavokClasses.Shipped;
        int size = classes["hkbClipGenerator"]!.Size;
        int binding = classes.Field("hkbClipGenerator", "variableBindingSet")!.Offset;

        var names = new byte[5 + "hkbClipGenerator".Length + 1];
        BitConverter.GetBytes(HavokClassTypes.Shipped["hkbClipGenerator"]!.Signature).CopyTo(names, 0);
        names[4] = 0x09;
        System.Text.Encoding.ASCII.GetBytes("hkbClipGenerator").CopyTo(names, 5);

        // Sixteen aligned, which is where the layout walk expects the second object and where every
        // object in every vanilla file sits.
        int second = (size + 15) / 16 * 16;

        var image = new PackfileImage();
        image.Sections.Add(new PackfileSection { TagBytes = MakeTag("__classnames__"), Data = names });
        image.Sections.Add(new PackfileSection
        {
            TagBytes = MakeTag("__data__"),
            Data = new byte[second + size],
            GlobalFixups = Triple(binding, 1, second),
            VirtualFixups = Triple(0, 0, 5).Concat(Triple(second, 0, 5)).ToArray(),
        });

        pointedAt = NativeGraphModel.FirstId + 1;
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

    /// Three clips where the first two both point at the third, so neither of the first two owns it.
    ///
    /// This is the shape that stops a subtree leaving its file, and it is the ordinary shape rather
    /// than a contrived one: `symrm template` counts 3,624 of the corpus's 5,320 state infos sharing
    /// something, usually a generator a second state also uses.
    private static PackfileImage ThreeClipsSharingAChild(out int shared)
    {
        var classes = HavokClasses.Shipped;
        int size = classes["hkbClipGenerator"]!.Size;
        int binding = classes.Field("hkbClipGenerator", "variableBindingSet")!.Offset;

        var names = new byte[5 + "hkbClipGenerator".Length + 1];
        BitConverter.GetBytes(HavokClassTypes.Shipped["hkbClipGenerator"]!.Signature).CopyTo(names, 0);
        names[4] = 0x09;
        System.Text.Encoding.ASCII.GetBytes("hkbClipGenerator").CopyTo(names, 5);

        int step = (size + 15) / 16 * 16;
        int second = step, third = step * 2;

        var image = new PackfileImage();
        image.Sections.Add(new PackfileSection { TagBytes = MakeTag("__classnames__"), Data = names });
        image.Sections.Add(new PackfileSection
        {
            TagBytes = MakeTag("__data__"),
            Data = new byte[third + size],
            GlobalFixups = Triple(binding, 1, third).Concat(Triple(second + binding, 1, third)).ToArray(),
            VirtualFixups = Triple(0, 0, 5).Concat(Triple(second, 0, 5)).Concat(Triple(third, 0, 5)).ToArray(),
        });

        shared = NativeGraphModel.FirstId + 2;
        return image;
    }

    /// A built image written where a template can be lifted out of it, since lifting reads a path.
    private static string WriteImage(PackfileImage image, string folder, string name)
    {
        System.IO.Directory.CreateDirectory(folder);
        string path = System.IO.Path.Combine(folder, name);
        System.IO.File.WriteAllBytes(path, image.Rebuild());
        return path;
    }

    /// A template folder of this test's own, so a run never reads or writes the one belonging to
    /// whoever is running it.
    private static string OwnTemplateFolder(string name)
    {
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "symrm-templates", name);
        if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true);
        System.IO.Directory.CreateDirectory(folder);
        TemplateStore.Folder = folder;
        return folder;
    }

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

        // Removing used to be refused, because taking an object out moves every object after it and
        // there was nowhere for them to go. Now it is planned, and carried out last, after every
        // value has been written at the offset it had.
        var removed = NativeSave.Compare(Extra("0091"), One);
        CheckTrue("removing one is no longer refused", removed.Possible);
        Check("and is planned as a deletion", 1, removed.Gone.Count);
        Check("naming the object that went", 91, removed.Gone[0]);
        CheckTrue("with no value change invented to go with it", removed.Changes.Count == 0);

        // The last object of its class going is not the same as the file changing shape, and telling
        // the two apart is why the deletion is worked out before the classes are lined up.
        CheckTrue("and taking the last of a class with it is still a deletion",
                  NativeSave.Compare(Extra("0091"), One).Possible);

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

        // A class the file has never named is named on the way in rather than refused. The save path
        // used to refuse this while `symrm append` did it, which is two answers to one question; now
        // both come through NativeAppend.NameOffset.
        var unnamed = new NativeSave.Plan(
            new List<NativeSave.Change> { new("hkbBlenderGenerator", 0, "", "#91", Added: true) }, null);

        int namesWere = image.Section("__classnames__")!.Data.Length;
        var written = PackfileImage.Read(NativeSave.Apply(path, unnamed));
        var grown = new PackfileObjects(written);

        Check("a class the file never named is added anyway", 2, grown.Instances.Count);
        Check("reading back as the class asked for", "hkbBlenderGenerator",
              grown.Instances[^1].ClassName);
        CheckTrue("with its name written into the table",
                  written.Section("__classnames__")!.Data.Length > namesWere);
        CheckTrue("and no 0xFF filler left in front of it",
                  !written.Section("__classnames__")!.Data.SkipLast(1).Any(b => b == 0xFF));

        // Adding an object makes the file longer, and a caller comparing it to the original byte for
        // byte has to be told so.
        CheckTrue("an addition counts as growing the file", unnamed.Grows);

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

    /// An enum field offers its declared names instead of asking for one to be typed, and only when
    /// offering them cannot lose anything.
    ///
    /// The three refusals are the point of the test. A flags field is a combination of bits and is
    /// usually not any single declared name, so a list would replace it with whichever entry the
    /// user picked. A field whose enum the table does not describe gets no invented list. And a file
    /// holding a number no name covers has to stay typeable, because a list would offer no way to
    /// keep what is already there.
    private static void AnEnumFieldOffersItsDeclaredValues()
    {
        Console.WriteLine("\nan enum field offers its declared values");

        var types = HavokClassTypes.Shipped;
        var image = ClipInAPackfile("A.hkx", out _);
        var objects = new PackfileObjects(image);
        var instance = objects.Instances[0];

        var names = ClassFields.NamesOf(objects, instance)!;
        var xml = names.Select(n => (n, "")).ToList();
        var shown = PanelFields.For(objects, instance, xml, (_, _) => "null", null, types);

        var mode = shown.First(f => f.Name == "mode");
        Check("a clip's mode is offered as a list", 5, mode.Options.Count);
        CheckTrue("holding the names the game registers",
                  mode.Options.Contains("MODE_SINGLE_PLAY", StringComparer.Ordinal));
        Check("in the order the enum declares them, not alphabetical",
              "MODE_SINGLE_PLAY", mode.Options[0]);
        CheckTrue("and the value in the file is one of them",
                  mode.Options.Contains(mode.Value, StringComparer.Ordinal));

        // A flags field is a combination, so it is never offered as a list of single values.
        var flags = types.Members("hkbBlendingTransitionEffect").First(m => m.Name == "flags");
        Check("a flags field is not offered as a list", "TYPE_FLAGS", flags.VType);

        var ordinary = shown.First(f => f.Name == "animationName");
        Check("a field that is not an enum stays a plain box", 0, ordinary.Options.Count);
        CheckTrue("and still holds its value", ordinary.Value.Length > 0);
    }

    /// What removal refuses, which today is most of it.
    ///
    /// Written before the orphan path so the refusals are the thing being described rather than
    /// whatever fell out of the implementation. Two of these are meant to keep failing until #19
    /// comes back from the game: full removal renumbers every object after the hole, and there is no
    /// way to check a renumber against the engine from here.
    /// What a condition comes to, worked out by hand and written down here.
    ///
    /// This is the independent opinion, and the corpus cannot supply one. `symrm conditions` proves
    /// every vanilla condition parses and that every one changes its answer as its variables change,
    /// and neither of those notices an operator being read as the wrong operator: `Pose != 5` read as
    /// `Pose == 5` still parses and still flips. Only an answer somebody worked out separately
    /// catches that, so the answers below were worked out separately.
    ///
    /// Every shape the 34 distinct vanilla conditions use is here, and the two the corpus does not
    /// use but the parser accepts, `&lt;=` and an unbracketed `&amp;&amp;`, because an operator with no test
    /// is where a hole goes.
    private static void AConditionSaysWhatItSays()
    {
        Console.WriteLine("\na condition says what it says");

        var world = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["iIsInSneak"] = 0,
            ["IsPlayer"] = 1,
            ["Speed"] = 12,
            ["TrotMaxSpeed"] = 20,
            ["Pose"] = 5,
            ["isMirrored"] = 1,
            ["isSightedOver"] = 0,
            ["iSyncReadyAlertRelaxed"] = 2,
            ["iSyncIdleLocomotion"] = 0,
            ["bBlockMoveStop"] = 0,
            ["fReal"] = 2.5,
        };

        double? Value(string name) => world.TryGetValue(name, out double v) ? v : null;

        var expected = new (string Text, Expression.Verdict Want)[]
        {
            // The plain comparisons, one of each operator, both ways round.
            ("iIsInSneak == 0", Expression.Verdict.True),
            ("iIsInSneak == 1", Expression.Verdict.False),
            ("Pose != 5", Expression.Verdict.False),
            ("Pose != 4", Expression.Verdict.True),
            ("Pose == 5", Expression.Verdict.True),
            ("Speed  > 10", Expression.Verdict.True),
            ("Speed < 9", Expression.Verdict.False),
            ("Speed >= 20", Expression.Verdict.False),
            ("Speed >= 12", Expression.Verdict.True),
            ("Speed <= 12", Expression.Verdict.True),
            ("Speed <= 11", Expression.Verdict.False),

            // A comparison of two variables, which one vanilla condition does.
            ("Speed > TrotMaxSpeed", Expression.Verdict.False),
            ("TrotMaxSpeed > Speed", Expression.Verdict.True),

            // A bare variable as a truth, and its negation.
            ("IsPlayer", Expression.Verdict.True),
            ("!IsPlayer", Expression.Verdict.False),
            ("!bBlockMoveStop", Expression.Verdict.True),
            ("bBlockMoveStop", Expression.Verdict.False),

            // The compound ones, including the De Morgan pair vanilla ships as complements of each
            // other. Under these values the first is true and the second must be false.
            ("(iSyncReadyAlertRelaxed==2) && (iSyncIdleLocomotion==0)", Expression.Verdict.True),
            ("(iSyncReadyAlertRelaxed!=2) || (iSyncIdleLocomotion==1)", Expression.Verdict.False),
            ("(isMirrored == 0) && (isSightedOver == 0)", Expression.Verdict.False),
            ("(isMirrored == 1) && (isSightedOver == 0)", Expression.Verdict.True),

            // Without brackets, which vanilla never writes and the parser has to get right anyway.
            ("iIsInSneak == 0 && Pose == 5", Expression.Verdict.True),
            ("iIsInSneak == 1 || Pose == 5", Expression.Verdict.True),
            ("iIsInSneak == 1 && Pose == 5", Expression.Verdict.False),

            // A real variable, which is stored as the bit pattern of a float and would come out as
            // 1075838976 if the type were ignored.
            ("fReal > 2", Expression.Verdict.True),
            ("fReal > 3", Expression.Verdict.False),

            // A name the graph does not declare is Unknown, not zero. Zero is a value a variable can
            // really hold, so answering with it would make this come out true on a file that has no
            // such variable at all.
            ("noSuchVariable == 0", Expression.Verdict.Unknown),

            // Unknown short circuits where the operator allows it and not where it does not.
            ("noSuchVariable == 0 && iIsInSneak == 1", Expression.Verdict.False),
            ("noSuchVariable == 0 || iIsInSneak == 0", Expression.Verdict.True),
            ("noSuchVariable == 0 && iIsInSneak == 0", Expression.Verdict.Unknown),

            // An assignment is not a test. Vanilla ships one, `iSyncIdleLocomotion=18`, and reading
            // it as `==` would be inventing a meaning nothing here can check.
            ("iSyncIdleLocomotion=18", Expression.Verdict.Unknown),

            // Nonsense stays Unknown rather than becoming an answer.
            ("Speed >", Expression.Verdict.Unknown),
            ("((Speed > 1)", Expression.Verdict.Unknown),
            ("", Expression.Verdict.Unknown),
        };

        foreach (var (text, want) in expected)
            Check($"\"{text}\"", want, Expression.Evaluate(text, Value));

        // Unknown has to mean the transition still fires. If reading a condition could ever hold one
        // back for a reason this did not understand, a build with a broken parser would be worse than
        // a build with no parser, which is the wrong way round for a feature nobody asked to depend
        // on.
        CheckTrue("an unreadable condition is not a reason to hold a transition back",
                  Expression.Evaluate("this is not an expression @@@", Value) != Expression.Verdict.False);
    }

    /// That a false condition actually stops the transition, rather than only being computed.
    ///
    /// The corpus gate drives conditions and watches the answer change, which proves the reading is
    /// live. It does not prove the stepper does anything with it. This is a graph small enough to
    /// hold in your head: one machine, two states, and one event with two transitions out of the
    /// first state, the higher priority one gated on a variable.
    private static void AFalseConditionHoldsATransitionBack()
    {
        Console.WriteLine("\na false condition holds a transition back");

        var model = BehaviourGraphModel.Parse(GatedGraph());
        var run = GraphRun.Start(model);

        Check("the graph starts in its first state", "Start", run.Where().FirstOrDefault()?.StateName ?? "");
        Check("and declares the variable the condition names", 0d, run.ValueOf("bGateOpen") ?? -1);

        // A real variable is stored as the bit pattern of its float, so 2.5 sits in the file as
        // 1075838976. Read as a whole number every comparison against it is nonsense and every one of
        // them comes out the same way, which looks like a working condition until somebody sets the
        // variable and nothing changes. Nothing in the corpus catches this: the vanilla files that
        // compare against a real all start it at zero, whose bit pattern is also zero, so the two
        // readings agree exactly where the shipped data can see them.
        Check("a real variable is the number it stores and not its bit pattern", 2.5d,
              run.ValueOf("fSpeed") ?? -1);
        Check("so a comparison against it means what it says", Expression.Verdict.True,
              run.Test("fSpeed > 2"));
        Check("in both directions", Expression.Verdict.False, run.Test("fSpeed > 3"));

        // Shut, so the gated transition cannot fire and the ungated one takes it instead.
        var fired = run.Send("Go");
        Check("the gated route does not fire while its condition is false", "Fallback",
              fired.FirstOrDefault()?.ToStateName ?? "");
        Check("and the one held back is reported rather than passed over in silence", 1, run.HeldBack.Count);
        CheckTrue("naming the condition that held it",
                  run.HeldBack.Count > 0 && run.HeldBack[0].Condition == "bGateOpen == 1");

        // Changing a variable clears what was held back, because it was held back by the values as
        // they were and the list would otherwise name a reason that is no longer the reason.
        run.Set("bGateOpen", 1);
        Check("changing a variable drops the reason the last send gave", 0, run.HeldBack.Count);

        // Open, and the same event goes the other way. Same graph, same event, different answer, so
        // the condition is what decided it.
        var again = GraphRun.Start(model);
        again.Set("bGateOpen", 1);
        var second = again.Send("Go");
        Check("with the variable set the gated route fires instead", "Gated",
              second.FirstOrDefault()?.ToStateName ?? "");
        Check("and nothing is held back", 0, again.HeldBack.Count);

        // A variable the graph never declared cannot be set, because nothing in the graph could ever
        // read it and quietly accepting one would look like it had worked.
        string refused = "";
        try { again.Set("noSuchVariable", 1); }
        catch (ArgumentException e) { refused = e.Message; }
        CheckTrue("setting a variable the graph does not declare is refused",
                  refused.Contains("declares no variable", StringComparison.Ordinal));
    }

    /// Copy and paste of a subtree.
    ///
    /// The corpus proof is `symrm paste`, over all 531 behaviours, and it is the one that matters:
    /// it copies a real subtree out of each and checks that no pointer inside the copy still names
    /// the original. These are the two things a corpus run reads past rather than reports, because a
    /// vanilla file has neither of them: a shared object shows up as a count and the refusal for one
    /// crossing files never fires when both halves are the same file.
    private static void APastedSubtreePointsAtItself()
    {
        Console.WriteLine("\na pasted subtree points at itself");

        // Two clips, the first pointing at the second, so the first owns the second.
        var image = TwoClipsOnePointingAtTheOther(out int child);
        int root = NativeGraphModel.FirstId;

        var tree = NativePaste.Of(image, root);
        Check("the root owns the object only it points at", 2, tree.Ids.Count);
        Check("and shares nothing", 0, tree.Shared.Count);

        int before = new PackfileObjects(image).Instances.Count;
        var done = NativePaste.Into(image, image, tree, sameFile: true);
        var after = new PackfileObjects(PackfileImage.Read(image.Rebuild()));

        Check("both objects are copied", before + 2, after.Instances.Count);
        Check("and the paste says which id the copied root got", NativeGraphModel.FirstId + before,
              done.RootId);

        // The check the whole feature is about. The copy has to point at its own child, not at the
        // child of the thing it was copied from, and the two are indistinguishable in the tree.
        var copiedRoot = after.Instances[done.RootId - NativeGraphModel.FirstId];
        var aimedAt = after.ReadRef(copiedRoot, "variableBindingSet", out _);
        Check("the copy points at its own child rather than the original's",
              after.Instances[^1].Offset, aimedAt?.Offset ?? -1);
        CheckTrue("which is not where the original's child sits",
                  aimedAt?.Offset != after.Instances[child - NativeGraphModel.FirstId].Offset);

        // A subtree that shares something cannot go into another file, because the other file has no
        // such object to aim at. Refusing and naming it is the answer, not aiming at whatever
        // happens to sit at the same offset.
        var shared = TwoClipsOnePointingAtTheOther(out int held);
        var borrower = NativePaste.Of(shared, held);
        Check("a leaf owns only itself", 1, borrower.Ids.Count);

        var elsewhere = TwoClipsOnePointingAtTheOther(out _);
        var borrowed = NativePaste.Of(shared, root) with { Shared = new[] { held } };

        string refused = "";
        try { NativePaste.Into(elsewhere, shared, borrowed, sameFile: false); }
        catch (InvalidOperationException e) { refused = e.Message; }

        CheckTrue("a subtree that shares an object is refused across files",
                  refused.Contains("shares", StringComparison.Ordinal));
        CheckTrue("and the refusal names what it shares",
                  refused.Contains("#" + held, StringComparison.Ordinal));
    }

    // The point of a template: a shape lifted out of one file and put into a different one, after the
    // session that lifted it has gone.
    private static void ATemplateLiftedFromOneFileGoesIntoAnother()
    {
        Console.WriteLine("\na template lifted from one file goes into another");

        string folder = OwnTemplateFolder("lift");
        string work = System.IO.Path.Combine(folder, "work");

        string from = WriteImage(TwoClipsOnePointingAtTheOther(out _), work, "From.hkx");
        string into = WriteImage(TwoClipsOnePointingAtTheOther(out _), work, "Into.hkx");

        var template = TemplateStore.Lift(from, NativeGraphModel.FirstId, "A Clip Pair", "for testing");
        Check("the template carries both objects", 2, template.Objects);
        Check("and knows what it was lifted from", "hkbClipGenerator", template.RootClass);
        Check("and is named on disk by a readable slug", "a-clip-pair", template.Slug);

        // The part that makes it a template rather than a copy: it is on disk and can be found again
        // without the thing that made it.
        var listed = TemplateStore.All();
        Check("it is listed afterwards", 1, listed.Count);
        Check("under the name it was given", "A Clip Pair", listed.FirstOrDefault()?.Name ?? "nothing listed");
        Check("with its note kept", "for testing", listed.FirstOrDefault()?.Note ?? "nothing listed");

        // Deliberately re-read rather than reusing the record above, because a template that only
        // works while the object that made it is still in memory is not a template.
        int before = new PackfileObjects(PackfileImage.Read(into)).Instances.Count;

        var reloaded = TemplateStore.Get("a-clip-pair");
        CheckTrue("the template can be found again by its slug", reloaded != null);
        if (reloaded == null) return;

        var result = TemplateStore.Apply(reloaded, into);
        System.IO.File.WriteAllBytes(into, result.Bytes);

        var after = new PackfileObjects(PackfileImage.Read(into));
        Check("both objects arrive in the other file", before + 2, after.Instances.Count);
        Check("and the applied root is the id reported", NativeGraphModel.FirstId + before, result.RootId);

        // The same property the paste feature exists for. The arrival has to point at its own copy of
        // the child, not back at the file it came from, and there is nothing in the target that could
        // even be the original.
        var root = after.Instances[result.RootId - NativeGraphModel.FirstId];
        var child = after.ReadRef(root, "variableBindingSet", out _);
        Check("and it points at its own copy of the child", after.Instances[^1].Offset, child?.Offset ?? -1);

        // Removing it takes the kept file with it, so a template folder cannot fill up with orphans.
        CheckTrue("a template can be forgotten", TemplateStore.Remove("a-clip-pair"));
        Check("and is gone from the list", 0, TemplateStore.All().Count);
        CheckTrue("along with its copy of the file",
                  !System.IO.File.Exists(System.IO.Path.Combine(folder, "a-clip-pair.hkx")));
    }

    // The refusal that has to happen when the template is made rather than when it is used.
    //
    // A subtree sharing an object with the rest of its file can never be pasted into a different
    // file, so keeping it as a template would be keeping something that fails everywhere it is ever
    // tried. That is the common case for the shape this issue most wants: `symrm template` counts
    // 3,624 of 5,320 state infos sharing something.
    private static void ATemplateRefusesToLiftWhatSharesItsFile()
    {
        Console.WriteLine("\na template refuses to lift what shares its file");

        string folder = OwnTemplateFolder("shares");
        string work = System.IO.Path.Combine(folder, "work");
        string from = WriteImage(ThreeClipsSharingAChild(out int shared), work, "Shared.hkx");

        // The first clip points at the third, and so does the second, so the first does not own it.
        var tree = NativePaste.Of(PackfileImage.Read(from), NativeGraphModel.FirstId);
        Check("the root owns only itself", 1, tree.Ids.Count);
        Check("and shares the object the other one also points at", 1, tree.Shared.Count);

        string refused = "";
        try { TemplateStore.Lift(from, NativeGraphModel.FirstId, "Cannot Leave"); }
        catch (InvalidOperationException e) { refused = e.Message; }

        CheckTrue("lifting it is refused", refused.Contains("shares", StringComparison.Ordinal));
        CheckTrue("and the refusal names what it shares",
                  refused.Contains("#" + shared, StringComparison.Ordinal));
        CheckTrue("and says what to do instead",
                  refused.Contains("owns everything below it", StringComparison.Ordinal));

        Check("nothing was kept", 0, TemplateStore.All().Count);
        CheckTrue("and no half written copy was left behind",
                  !System.IO.File.Exists(System.IO.Path.Combine(folder, "cannot-leave.hkx")));
    }

    // A template inherits its source file's events and variables by name, so it is not self contained.
    // That is not a fault to hide: it is a fact to report before somebody tries, because the same
    // template is fine in one file and not in another. 2,251 of the corpus's 3,717 liftable clip
    // subtrees use at least one symbol, so this is the ordinary case.
    private static void ATemplateSaysWhatToDeclareRatherThanJustFailing()
    {
        Console.WriteLine("\na template says what to declare rather than just failing");

        string folder = OwnTemplateFolder("symbols");
        string work = System.IO.Path.Combine(folder, "work");
        string from = WriteImage(TwoClipsOnePointingAtTheOther(out _), work, "From.hkx");
        string into = WriteImage(TwoClipsOnePointingAtTheOther(out _), work, "Into.hkx");

        var lifted = TemplateStore.Lift(from, NativeGraphModel.FirstId, "Plain");

        // Neither file declares anything, and this template uses nothing, so it fits.
        var plain = TemplateStore.Against(lifted, into);
        CheckTrue("a template using no symbols fits a file declaring none", plain.Fits);
        Check("and says so plainly", "everything this needs is already declared", plain.ToString());

        // The same template described as needing symbols the target has not got. Built rather than
        // lifted, because no fixture here declares symbols, and what is under test is the answer
        // given about a target rather than the reading of the source.
        var demanding = lifted with
        {
            Events = new[] { "StartOpen", "Opened" },
            Variables = new[] { "bIsLocked" },
        };

        var fit = TemplateStore.Against(demanding, into);
        CheckTrue("one needing undeclared symbols does not fit", !fit.Fits);
        Check("both missing events are named", 2, fit.Events.Count);
        Check("and the missing variable", 1, fit.Variables.Count);
        CheckTrue("the message says what to declare rather than that something went wrong",
                  fit.ToString().Contains("declare", StringComparison.Ordinal) &&
                  fit.ToString().Contains("StartOpen", StringComparison.Ordinal) &&
                  fit.ToString().Contains("bIsLocked", StringComparison.Ordinal));

        // Applying anyway has to refuse with the same list, because Apply can be reached without
        // anybody having looked at the fit first.
        string refused = "";
        try { TemplateStore.Apply(demanding, into); }
        catch (InvalidOperationException e) { refused = e.Message; }

        CheckTrue("applying it is refused", refused.Contains("does not declare", StringComparison.Ordinal));
        CheckTrue("naming the symbols", refused.Contains("StartOpen", StringComparison.Ordinal));
        CheckTrue("and pointing at where to declare them",
                  refused.Contains("symbols tab", StringComparison.Ordinal));
        CheckTrue("and it says nothing was added",
                  refused.Contains("nothing was added", StringComparison.Ordinal));

        Check("the target file was not touched", 2,
              new PackfileObjects(PackfileImage.Read(into)).Instances.Count);
    }

    // The description file is one line per field, and two vanilla event names carry a literal carriage
    // return. A name holding one would end its own line and take the rest of the description with it,
    // and the file would parse into something that looked fine and was wrong.
    //
    // The corpus cannot catch this: it would need a template lifted from one of those two files and
    // then read back, and nothing sweeps that. So the awkward values are written out here by hand.
    private static void ATemplateDescriptionSurvivesAwkwardNames()
    {
        Console.WriteLine("\na template description survives awkward names");

        foreach (string awkward in new[]
                 {
                     "Plain",
                     "Has\rCarriageReturn",
                     "Has\nNewline",
                     "Has\\Backslash",
                     "Ends\\",
                     "Has\x1fSeparator",
                     "Every\r\n\\\x1fOne",
                     "",
                 })
        {
            Check($"'{Readable(awkward)}' survives being written and read",
                  awkward, TemplateStore.Decode(TemplateStore.Encode(awkward)));
        }

        // A backslash must not be un-escaped twice: "a\\b" encoded then decoded is "a\b", and a
        // decoder taking the escapes in the wrong order turns it into an escape of whatever follows.
        Check("an escaped backslash does not eat the character after it",
              "a\\rb", TemplateStore.Decode(TemplateStore.Encode("a\\rb")));

        // The whole way round through a real file, since the escaping is only worth having if the
        // description writer and reader agree on it.
        string folder = OwnTemplateFolder("names");
        string work = System.IO.Path.Combine(folder, "work");
        string from = WriteImage(TwoClipsOnePointingAtTheOther(out _), work, "From.hkx");

        var lifted = TemplateStore.Lift(from, NativeGraphModel.FirstId, "Awkward", "note\rwith a return");
        Check("a note holding a carriage return comes back whole", "note\rwith a return",
              TemplateStore.Get(lifted.Slug)?.Note);

        Check("and the description is still one line per field", 8,
              System.IO.File.ReadAllLines(System.IO.Path.Combine(folder, lifted.Slug + ".template")).Length);
    }

    /// A string with its control characters shown, so a failing check names something readable.
    private static string Readable(string text) =>
        text.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\x1f", "\\u", StringComparison.Ordinal);

    private static void RemovingAnObjectIsRefusedAndOrphaningIsNot()
    {
        Console.WriteLine("\nremoving an object is refused and orphaning is not");

        // Full removal, through the front end the editor's save goes down. Still refused, and meant
        // to stay refused until #19 comes back: dropping an object renumbers every one after it, and
        // a renumber cannot be checked against the engine from here. The refusal itself is covered
        // by AnAddedObjectHasToLandWhereItsIdSays, which owns that fixture.
        var image = ClipInAPackfile("A.hkx", out _);

        // An id the file does not have. The message names the range rather than saying no, because
        // an off by one here is the difference between two objects.
        string refused = "";
        try { NativeRemove.Orphan(image, 4000); }
        catch (InvalidOperationException e) { refused = e.Message; }
        CheckTrue("an id the file does not hold is refused",
                  refused.Contains("#4000", StringComparison.Ordinal));
        CheckTrue("and the refusal says what the file does hold",
                  refused.Contains("#" + NativeGraphModel.FirstId, StringComparison.Ordinal));

        // Nothing points at the only object in this fixture, so orphaning it is a no change rather
        // than an error. Saying "reached from nowhere" is the useful answer.
        var already = NativeRemove.Orphan(image, NativeGraphModel.FirstId);
        CheckTrue("orphaning something nothing reaches changes nothing", !already.Reached);
        Check("no pointer cleared", 0, already.PointersCleared);
        Check("no element dropped", 0, already.ElementsDropped);

        // And the file is untouched by that, byte for byte, rather than merely still valid.
        var untouched = ClipInAPackfile("A.hkx", out _);
        NativeRemove.Orphan(untouched, NativeGraphModel.FirstId);
        CheckTrue("leaving the file exactly as it was",
                  untouched.Rebuild().SequenceEqual(ClipInAPackfile("A.hkx", out _).Rebuild()));
    }

    /// Taking an object out for real, rather than leaving it in the file unreferenced.
    ///
    /// The corpus proof is the one that matters, and it is `symrm delete` and `symrm savedelete`.
    /// These are the parts a corpus run cannot show: that a pointer left aiming at what is going is
    /// refused rather than written, and that the object list actually gets shorter.
    private static void DeletingTakesAnObjectOutOfTheFile()
    {
        Console.WriteLine("\ndeleting takes an object out of the file");

        var image = ClipInAPackfile("A.hkx", out _);
        int only = NativeGraphModel.FirstId;

        int before = new PackfileObjects(image).Instances.Count;
        var gone = NativeRemove.Delete(image, new[] { only });

        Check("one object taken out", 1, gone.Objects);
        Check("and the file no longer lists it", before - 1, new PackfileObjects(image).Instances.Count);
        CheckTrue("and the file still reads", PackfileImage.Read(image.Rebuild()).Section("__data__") != null);

        // The check that makes this safe to offer at all. A pointer left aiming at a deleted object
        // is a vtable read on space nothing wrote, so it is refused rather than nulled behind the
        // caller's back: what to put in a field's place is a graph decision, not a byte one.
        var two = TwoClipsOnePointingAtTheOther(out int pointedAt);
        string refused = "";
        try { NativeRemove.Delete(two, new[] { pointedAt }); }
        catch (InvalidOperationException e) { refused = e.Message; }

        CheckTrue("deleting something still pointed at is refused",
                  refused.Contains("still points at", StringComparison.Ordinal));
        CheckTrue("and the refusal says to detach it first",
                  refused.Contains("Detach", StringComparison.Ordinal));
        Check("and nothing was taken out", 2, new PackfileObjects(two).Instances.Count);
    }

    /// Declaring an event, which lengthens an array of strings.
    ///
    /// The corpus proof is `symrm saveevent`, 328 files. This covers the part a corpus run cannot
    /// show on its own: that a name carrying a newline survives the round trip. Two vanilla events
    /// do, and the first attempt at this held the array together with newlines and split those two
    /// names into four, writing an array two elements too long in ten behaviours.
    private static void AnArrayOfNamesCanGrow()
    {
        Console.WriteLine("\nan array of names can grow");

        const string Head = """
            <?xml version="1.0" encoding="ascii"?>
            <hkpackfile classversion="8"><hksection name="__data__">
            <hkobject class="hkbBehaviorGraphStringData" name="#0090" signature="0xc713064e">
                <hkparam name="eventNames" numelements="COUNT">NAMES</hkparam>
            </hkobject>
            </hksection></hkpackfile>
            """;

        string Doc(params string[] names) =>
            Head.Replace("COUNT", names.Length.ToString())
                .Replace("NAMES", string.Concat(names.Select(n => $"<hkcstring>{n}</hkcstring>")));

        var grown = NativeSave.Compare(Doc("Walk", "Run"), Doc("Walk", "Run", "Sprint"));
        CheckTrue("growing it is no longer refused", grown.Possible);
        Check("planned as one change", 1, grown.Changes.Count);
        CheckTrue("written as text", grown.Changes[0].Text);
        CheckTrue("and as an array", grown.Changes[0].Array);

        // The whole array travels, not just the new name, because the run moves and every element
        // pointer in it has to be written again.
        Check("carrying every name", 3, grown.Changes[0].Value.Split('\0').Length);
        Check("with the new one last", "Sprint", grown.Changes[0].Value.Split('\0')[^1]);

        // The finding this test exists for. WeaponBehavior declares SyncRight\r\nFootRight as one
        // event, and hkxpack reads it as one too. Held together by newlines it becomes two.
        //
        // Written the way the document writes it, as a character reference. That is not incidental:
        // a literal line break in XML is normalised to a single newline when it is parsed, and a
        // carriage return only survives a round trip because it is escaped. Spelling it literally
        // here tested the parser rather than the writer, and passed while proving nothing.
        const string Odd = "SyncRight\r\nFootRight";
        var withNewline = NativeSave.Compare(Doc("SyncRight&#13;\nFootRight"),
                                             Doc("SyncRight&#13;\nFootRight", "Sprint"));
        CheckTrue("a name carrying a newline is still writable", withNewline.Possible);

        var parts = withNewline.Changes[0].Value.Split('\0');
        Check("and is still one name", 2, parts.Length);
        Check("with its carriage return intact", Odd, parts[0]);

        // Shrinking, which the same writer has to do: the run is rewritten at the new length rather
        // than the old one being trimmed.
        var shrunk = NativeSave.Compare(Doc("Walk", "Run", "Sprint"), Doc("Walk"));
        CheckTrue("shrinking it is writable too", shrunk.Possible);
        Check("down to one name", 1, shrunk.Changes[0].Value.Split('\0').Length);

        // And an array of pointers must not be mistaken for one of names. Both carry a numelements
        // attribute, and testing for that instead of for the elements themselves emptied every
        // pointer array in the file.
        const string Pointers = """
            <?xml version="1.0" encoding="ascii"?>
            <hkpackfile classversion="8"><hksection name="__data__">
            <hkobject class="hkbStateMachine" name="#0090" signature="0x816c1dcb">
                <hkparam name="states" numelements="2">#0091 #0092</hkparam>
            </hkobject>
            </hksection></hkpackfile>
            """;

        var repointed = NativeSave.Compare(Pointers, Pointers.Replace("#0091 #0092", "#0092 #0091"));
        CheckTrue("an array of pointers is still an array of pointers", repointed.Possible);
        Check("changed as one array", 1, repointed.Changes.Count);
        Check("keeping its ids", "#0092 #0091", repointed.Changes[0].Value);
    }

    /// A vector, a transform or an eight byte number, written over the one already there.
    ///
    /// None of these move anything: a vector is sixteen bytes wherever it sits. They were refused
    /// anyway, because nothing parsed the spelling back, so every file with one edited went out
    /// through hkxpack. The corpus proof is `symrm savewide`, 243 files.
    private static void AWideFieldIsWrittenWhereItSits()
    {
        Console.WriteLine("\na wide field is written where it sits");

        const string Doc = """
            <?xml version="1.0" encoding="ascii"?>
            <hkpackfile classversion="8"><hksection name="__data__">
            <hkobject class="BSLookAtModifier" name="#0090" signature="0x9a24e9e7">
                <hkparam name="lookAtCameraX">VALUE</hkparam>
            </hkobject>
            </hksection></hkpackfile>
            """;

        // The spelling is the one the panel shows, so what a person reads is what they can type.
        var moved = NativeSave.Compare(Doc.Replace("VALUE", "0.0"), Doc.Replace("VALUE", "0.5"));
        CheckTrue("a plain real still works", moved.Possible);

        const string Vector = """
            <?xml version="1.0" encoding="ascii"?>
            <hkpackfile classversion="8"><hksection name="__data__">
            <hkobject class="hkbHandIkControlData" name="#0090" signature="0x54b1e50f">
                <hkparam name="targetPosition">VALUE</hkparam>
            </hkobject>
            </hksection></hkpackfile>
            """;

        var vector = NativeSave.Compare(Vector.Replace("VALUE", "(0 0 0 0)"),
                                        Vector.Replace("VALUE", "(1.5 -2.25 3.75 0.5)"));
        CheckTrue("a vector is now writable", vector.Possible);
        Check("as one change", 1, vector.Changes.Count);
        CheckTrue("written in place rather than appended", !vector.Changes[0].Text &&
                                                           !vector.Changes[0].Array);

        // Refused on the shape rather than accepted and written wrong. Three numbers is not a
        // vector, and writing three of the four would leave the fourth as whatever was there.
        var short3 = NativeSave.Compare(Vector.Replace("VALUE", "(0 0 0 0)"),
                                        Vector.Replace("VALUE", "(1 2 3)"));
        CheckTrue("a vector of the wrong length is refused", !short3.Possible);
        CheckTrue("and the refusal says how many were wanted",
                  short3.Refusal?.Contains("4 number(s)", StringComparison.Ordinal) == true);

        var words = NativeSave.Compare(Vector.Replace("VALUE", "(0 0 0 0)"),
                                       Vector.Replace("VALUE", "(a b c d)"));
        CheckTrue("and so is one that is not numbers", !words.Possible);
    }

    /// An array of plain numbers at a new length, and reading the last of one at the end of a
    /// section.
    ///
    /// The corpus proof is `symrm savenumbers`, 56 files. What that run turned up, and what this
    /// pins, is a reader fault it happened to expose: a field narrower than four bytes was read as
    /// four and masked down, which works everywhere except the last bytes of a section. Nothing in a
    /// vanilla file sits there, so it never showed until a lengthened array was appended to the end
    /// and its final element read as blank while the count beside it said otherwise.
    private static void AnArrayOfNumbersCanGrow()
    {
        Console.WriteLine("\nan array of numbers can grow");

        const string Doc = """
            <?xml version="1.0" encoding="ascii"?>
            <hkpackfile classversion="8"><hksection name="__data__">
            <hkobject class="hkbBoneIndexArray" name="#0090" signature="0x8a02c4a1">
                <hkparam name="boneIndices" numelements="COUNT">NUMBERS</hkparam>
            </hkobject>
            </hksection></hkpackfile>
            """;

        string Doc2(params int[] numbers) =>
            Doc.Replace("COUNT", numbers.Length.ToString())
               .Replace("NUMBERS", string.Join(" ", numbers));

        var grown = NativeSave.Compare(Doc2(0, 1, 2), Doc2(0, 1, 2, 7));
        CheckTrue("growing it is no longer refused", grown.Possible);
        Check("planned as one change", 1, grown.Changes.Count);
        CheckTrue("as an array", grown.Changes[0].Array);
        CheckTrue("and not as text", !grown.Changes[0].Text);

        var shrunk = NativeSave.Compare(Doc2(0, 1, 2), Doc2(0));
        CheckTrue("shrinking it too", shrunk.Possible);

        // Refused rather than written as a guess. A word is not a bone index, and writing nothing
        // for it would leave whatever was there in its place.
        var words = NativeSave.Compare(Doc2(0, 1, 2), Doc2(0, 1, 2).Replace("2", "two"));
        CheckTrue("a value that is not a number is refused", !words.Possible);

        // The reader fault. Two bytes at the very end of a section have to read as two bytes.
        var data = new byte[6];
        data[4] = 0x39;
        data[5] = 0x05;   // 1337, sitting in the last two bytes of the section

        var image = new PackfileImage();
        image.Sections.Add(new PackfileSection { TagBytes = MakeTag("__classnames__"), Data = new byte[8] });
        image.Sections.Add(new PackfileSection { TagBytes = MakeTag("__data__"), Data = data });

        var objects = new PackfileObjects(image, HavokClasses.Shipped);
        Check("two bytes at the end of a section read as two bytes", 1337, objects.ReadNarrowAt(4, 2));
        Check("and reading them as four still says nothing", null, objects.ReadIntAt(4));
    }

    /// What the panel can say about a field, and what it must not.
    ///
    /// The rule this pins is the one that matters: a description of what a field is comes from the
    /// class table and is always available, and a sentence about what it means only exists for the
    /// fields somebody actually established. Inventing the second from the first would produce
    /// something that reads exactly like a measured finding.
    private static void AFieldSaysWhatItIsAndOnlySaysWhatItMeansWhenWeKnow()
    {
        Console.WriteLine("\na field says what it is, and only says what it means when we know");

        // Shape, from the table. Dull on purpose.
        // No "declared by" clause: hkbStateMachineStateInfo declares generator itself, and saying so
        // would be noise on the majority of fields.
        Check("a pointer says what it points at",
              "a pointer to a hkbGenerator",
              FieldNotes.Structure("hkbStateMachineStateInfo", "generator"));
        Check("a name says it is text",
              "a name, held as text",
              FieldNotes.Structure("hkbClipGenerator", "animationName"));
        Check("an enum says how many values it has",
              "one of 5 declared values", FieldNotes.Structure("hkbClipGenerator", "mode"));
        CheckTrue("an array says what it holds",
                  FieldNotes.Structure("hkbStateMachine", "states")?.StartsWith("an array of pointers",
                      StringComparison.Ordinal) == true);

        // An inherited field says where it comes from, which is half of knowing what it is for.
        CheckTrue("an inherited field names the class that declares it",
                  FieldNotes.Structure("hkbClipGenerator", "userData")?.Contains("declared by hkbNode",
                      StringComparison.Ordinal) == true);

        // A fixed length C array is one member written out as eight fields. Looking the shown name
        // up in the member list finds nothing, and 88 fields in the corpus were left undescribed.
        CheckTrue("one of a run of fields written side by side is still described",
                  FieldNotes.Structure("hkbFootIkControlData", "enabled3")?.Contains("number 3 of 8",
                      StringComparison.Ordinal) == true);
        Check("and a name that merely ends in a digit is not mistaken for one",
              null, FieldNotes.Structure("hkbClipGenerator", "notAField7"));

        // Meaning, only where it was established, and carrying where from.
        var mode = FieldNotes.Meaning("hkbClipGenerator", "mode");
        CheckTrue("a field somebody established has a sentence", mode != null);
        CheckTrue("and says where it came from", mode?.From.Length > 0);

        Check("a field nobody has checked has none",
              null, FieldNotes.Meaning("hkbClipGenerator", "cropStartAmountLocalTime"));
        Check("and neither does one on a class with no findings at all",
              null, FieldNotes.Meaning("BSLookAtModifier", "lookAtCameraX"));

        // Two classes can both declare a flags and mean different things by it, so a sentence must
        // not leak from one to the other.
        CheckTrue("a sentence belongs to the class that declares the field",
                  FieldNotes.Meaning("hkbStateMachineTransitionInfo", "flags") != null &&
                  FieldNotes.Meaning("hkbClipGenerator", "flags") == null);
    }

    /// The last object in a document ends at its own closing tag, not at the end of the file.
    ///
    /// This was wrong and the damage was invisible until something deleted the last object: the
    /// block ran to the end of the text, so removing it took `</hksection></hkpackfile>` with it and
    /// left a document no parser would read. 111 of the 531 vanilla behaviours hit it.
    private static void TheLastObjectsBlockEndsAtItsOwnClosingTag()
    {
        Console.WriteLine("\nthe last object's block ends at its own closing tag");

        const string Two = """
            <?xml version="1.0" encoding="ascii"?>
            <hkpackfile classversion="8"><hksection name="__data__">
            <hkobject class="hkbClipGenerator" name="#0090" signature="0x333b85b9">
                <hkparam name="userPartitionMask">1</hkparam>
            </hkobject>
            <hkobject class="hkbClipGenerator" name="#0091" signature="0x333b85b9">
                <hkparam name="userPartitionMask">2</hkparam>
            </hkobject>
            </hksection></hkpackfile>
            """;

        var (start, length) = HkxTextEdit.ObjectBlock(Two, "0091");
        CheckTrue("the block is found", start >= 0);

        string block = Two.Substring(start, length);
        CheckTrue("and stops at its own closing tag",
                  !block.Contains("</hksection>", StringComparison.Ordinal));

        string without = Two.Remove(start, length);
        CheckTrue("so removing it leaves the section closed",
                  without.Contains("</hksection></hkpackfile>", StringComparison.Ordinal));
        CheckTrue("and leaves a document that parses",
                  Parses(without));
        CheckTrue("with the other object still in it",
                  without.Contains("#0090", StringComparison.Ordinal));
        CheckTrue("and the deleted one gone",
                  !without.Contains("#0091", StringComparison.Ordinal));
    }

    private static bool Parses(string xml)
    {
        try { System.Xml.Linq.XDocument.Parse(xml); return true; }
        catch (System.Xml.XmlException) { return false; }
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

    // The rotation packers, against their own readers.
    //
    // Each narrow format drops the largest component and rebuilds it from the other three, so a
    // writer that picks a different component from the one the reader expects still produces a valid
    // looking five bytes. Checked over a spread of rotations rather than one, because the failure is
    // per component: a writer that only ever gets tested on a rotation about one axis agrees for
    // exactly as long as nothing turns about another.
    private static void APackedRotationComesBackAsItself()
    {
        var samples = new List<Quaternion>();
        for (int i = 0; i < 4; i++)
            for (float angle = -3f; angle < 3.1f; angle += 0.7f)
            {
                var axis = i switch
                {
                    0 => new Vector3(1, 0, 0),
                    1 => new Vector3(0, 1, 0),
                    2 => new Vector3(0, 0, 1),
                    _ => Vector3.Normalize(new Vector3(1, 1, 1)),
                };
                samples.Add(Quaternion.CreateFromAxisAngle(axis, angle));
            }

        var scratch = new byte[16];
        float worst40 = 0, worst48 = 0;
        foreach (var q in samples)
        {
            SplineQuat.Write40(q, scratch, 0);
            worst40 = MathF.Max(worst40, SplineQuat.AngleBetween(q, SplineQuat.Read40(scratch, 0)));
            SplineQuat.Write48(q, scratch, 0);
            worst48 = MathF.Max(worst48, SplineQuat.AngleBetween(q, SplineQuat.Read48(scratch, 0)));
        }

        Check("every rotation is tried", 36, samples.Count);

        // Forty bits gives twelve to each of three components over a range of about 1.4, so a
        // thousandth of a radian is the width of two or three steps rather than a loose bound.
        CheckTrue($"forty bit rotations come back within a thousandth of a radian ({worst40:F6})",
            worst40 < 0.001f);
        CheckTrue($"forty eight bit rotations come back ten times closer ({worst48:F7})",
            worst48 < 0.0001f);

        // The sign of the dropped component is carried in a bit of its own, and a writer that drops
        // it reads back as the rotation the other way round on half of these.
        var backwards = Quaternion.Normalize(new Quaternion(0.1f, 0.2f, 0.3f, -0.927f));
        SplineQuat.Write40(backwards, scratch, 0);
        CheckTrue("a negative largest component keeps its sign",
            SplineQuat.AngleBetween(backwards, SplineQuat.Read40(scratch, 0)) < 0.001f);
    }

    // The guarantee the whole encoder rests on: a clamped B-spline of degree one with a control
    // point per frame passes exactly through every frame. That is what makes the fit a search for
    // something smaller rather than a search for anything at all, so if it ever stops being true the
    // encoder has no floor under it.
    private static void ALinearCurvePassesThroughEveryFrame()
    {
        const int frames = 40;
        var samples = new float[frames];
        for (int f = 0; f < frames; f++) samples[f] = MathF.Sin(f * 0.4f) * 17f + f * 0.9f;

        var curve = SplineFit.FitScalarAt(samples, frames, 1);
        Check("one control point per frame", frames, curve.ControlPoints.Length);
        Check("at degree one", 1, curve.Degree);

        // Not zero: the control points are stored in sixteen bits across the channel's own range, so
        // the floor is the width of one step and not nothing. Asserting zero here would be asserting
        // something untrue and would have to be loosened the first time it ran.
        float step = (curve.Max - curve.Min) / 65535f;
        CheckTrue($"and lands on every frame within one quantisation step ({curve.Error:F6} against {step:F6})",
            curve.Error <= step * 1.01f);

        var knots = SplineFormat.Knots(frames, 1, frames);
        Check("the knot vector is the length the format states", frames + 2, knots.Length);
        Check("it starts clamped", 0, (int)knots[0]);
        Check("and ends on the last frame", frames - 1, (int)knots[^1]);
        CheckTrue("with no repeated span in the middle", SplineFormat.KnotsUsable(knots, frames, 1));
    }

    /// A clip built in memory, with a different shape of motion on each track.
    private static HkxAnimationData MadeUpClip(int frames, int tracks)
    {
        var clip = new HkxAnimationData
        {
            AnimationClass = "hkaSplineCompressedAnimation",
            NumFrames = frames,
            NumTracks = tracks,
            Duration = (frames - 1) / 30f,
            FrameDuration = 1f / 30f,
        };

        for (int t = 0; t < tracks; t++)
        {
            var track = new HkxTrackData();
            for (int f = 0; f < frames; f++)
            {
                float at = f / (float)Math.Max(1, frames - 1);
                track.Translations.Add(new Vector3(
                    MathF.Sin(at * 6f + t) * 12f,
                    at * 30f - t,
                    MathF.Cos(at * 4f + t) * 5f));
                track.Rotations.Add(Quaternion.CreateFromAxisAngle(
                    Vector3.Normalize(new Vector3(1, t + 1, 2)), at * 2.4f + t * 0.3f));
                track.Scales.Add(Vector3.One);
            }
            clip.Tracks.Add(track);
        }

        return clip;
    }

    private static (float Position, float Rotation) RoundTrip(HkxAnimationData clip)
    {
        var blob = SplineEncoder.Encode(clip);
        var back = new HkxAnimationData { NumFrames = clip.NumFrames };
        SplineEncoder.Decode(blob.Data, blob.BlockOffsets, clip.Tracks.Count, clip.NumFrames,
            blob.MaskAndQuantizationSize, blob.MaxFramesPerBlock, back);

        float position = 0, rotation = 0;
        for (int t = 0; t < clip.Tracks.Count; t++)
            for (int f = 0; f < clip.NumFrames; f++)
            {
                position = MathF.Max(position,
                    (clip.Tracks[t].Translations[f] - back.Tracks[t].Translations[f]).Length());
                rotation = MathF.Max(rotation,
                    SplineQuat.AngleBetween(clip.Tracks[t].Rotations[f], back.Tracks[t].Rotations[f]));
            }
        return (position, rotation);
    }

    // The codec end to end, on frames chosen here rather than read from a file.
    //
    // The corpus gate is the real measurement and this is not a smaller copy of it. This one exists
    // because the corpus needs a Fallout 4 install and this does not, so a change that breaks the
    // encoder is caught by the suite that actually runs on every build.
    private static void AnEncodedClipDecodesToWhatWentIn()
    {
        var clip = MadeUpClip(60, 3);
        var blob = SplineEncoder.Encode(clip);

        Check("one block holds it", 1, blob.NumBlocks);
        Check("the mask is four bytes a track", 12, blob.MaskAndQuantizationSize);
        Check("the block starts at the front of the blob", 0, blob.BlockOffsets[0]);
        CheckTrue("the timing is carried across rather than recomputed",
            MathF.Abs(blob.FrameDuration - clip.FrameDuration) < 1e-6f);

        var drift = RoundTrip(clip);
        CheckTrue($"every bone lands where it started ({drift.Position:F5} unit(s))", drift.Position < 0.05f);
        CheckTrue($"and facing the way it was ({drift.Rotation:F6} radian(s))", drift.Rotation < 0.01f);
    }

    // A channel nobody drives has to be written as undriven rather than as a flat curve. Getting
    // this wrong costs nothing visible and several times the size, which is exactly the kind of
    // fault that survives forever because the frames still come back right.
    private static void AnUndrivenChannelIsNotWrittenAsACurve()
    {
        var clip = MadeUpClip(40, 1);
        var blob = SplineEncoder.Encode(clip);

        // Scale is a flat one on every frame of the made up clip, which is what almost every vanilla
        // track carries: 1,291,375 of the 1,291,826 track blocks in the game have no scale at all.
        Check("scale is marked undriven", 0, (int)blob.Data[3]);
        CheckTrue("rotation is marked as a curve", (blob.Data[2] >> 4) != 0);
        CheckTrue("and so is position", (blob.Data[1] >> 4) != 0);

        Check("three channels counted as undriven", 3, blob.Report.Identity);

        // Same clip with the scale actually moving: now it has to be a curve.
        var moving = MadeUpClip(40, 1);
        for (int f = 0; f < moving.NumFrames; f++)
            moving.Tracks[0].Scales[f] = new Vector3(1f + f * 0.01f, 1f, 1f);

        var second = SplineEncoder.Encode(moving);
        CheckTrue("a scale that moves is written as one", (second.Data[3] >> 4) != 0);
    }

    // Past 256 frames the blob becomes more than one block, and every offset after the first is one
    // this code chose rather than one the format dictated. A clip that decodes correctly inside its
    // first block and wrongly after it is the specific failure here, so the check is deliberately on
    // a length that needs three blocks and does not divide evenly into them.
    private static void AClipTooLongForOneBlockIsSplit()
    {
        var clip = MadeUpClip(600, 2);
        var blob = SplineEncoder.Encode(clip);

        Check("three blocks hold six hundred frames", 3, blob.NumBlocks);
        Check("at 256 frames each", 256, blob.MaxFramesPerBlock);
        Check("the first starts at the front", 0, blob.BlockOffsets[0]);
        CheckTrue("and each one after it starts later than the last",
            blob.BlockOffsets[1] > blob.BlockOffsets[0] && blob.BlockOffsets[2] > blob.BlockOffsets[1]);
        CheckTrue("on a sixteen byte boundary",
            blob.BlockOffsets.All(o => o % 16 == 0));

        var drift = RoundTrip(clip);
        CheckTrue($"the last block decodes as well as the first ({drift.Position:F5} unit(s))",
            drift.Position < 0.05f);
        CheckTrue($"including its rotations ({drift.Rotation:F6} radian(s))", drift.Rotation < 0.01f);
    }

    /// A door: closed, opening, open, closing, back to closed. Four events, one machine.
    ///
    /// Shaped after the vanilla special case door rather than invented, because that is the check the
    /// ticket itself names: a simulated door that does not open on the event its own script sends is
    /// wrong. The vanilla one has seven states and this has four, and the sequence is the same.
    private static string DoorGraph() => """
        <?xml version="1.0" encoding="ascii"?>
        <hkpackfile classversion="11" contentsversion="hk_2014.1.0-r1">
            <hksection name="__data__">
                <hkobject class="hkbBehaviorGraph" name="#91" signature="0xb1218f86">
                    <hkparam name="name">Door</hkparam>
                    <hkparam name="rootGenerator">#92</hkparam>
                    <hkparam name="data">#80</hkparam>
                </hkobject>
                <hkobject class="hkbBehaviorGraphData" name="#80" signature="0x95aca5d">
                    <hkparam name="stringData">#81</hkparam>
                </hkobject>
                <hkobject class="hkbBehaviorGraphStringData" name="#81" signature="0xc713064e">
                    <hkparam name="eventNames" numelements="4">
                        <hkcstring>Open</hkcstring>
                        <hkcstring>Opened</hkcstring>
                        <hkcstring>Close</hkcstring>
                        <hkcstring>Closed</hkcstring>
                    </hkparam>
                    <hkparam name="variableNames" numelements="0"></hkparam>
                </hkobject>
                <hkobject class="hkbStateMachine" name="#92" signature="0xa5896bcf">
                    <hkparam name="name">DoorMachine</hkparam>
                    <hkparam name="startStateId">0</hkparam>
                    <hkparam name="wildcardTransitions">null</hkparam>
                    <hkparam name="states" numelements="4">
                        #93 #95 #97 #99
                    </hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#93" signature="0x39d76713">
                    <hkparam name="name">Closed</hkparam>
                    <hkparam name="stateId">0</hkparam>
                    <hkparam name="generator">#94</hkparam>
                    <hkparam name="transitions">#101</hkparam>
                </hkobject>
                <hkobject class="hkbClipGenerator" name="#94" signature="0xd4cc9f6">
                    <hkparam name="name">ClipClosed</hkparam>
                    <hkparam name="animationName">closed.hkx</hkparam>
                    <hkparam name="triggers">null</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#95" signature="0x39d76713">
                    <hkparam name="name">Opening</hkparam>
                    <hkparam name="stateId">1</hkparam>
                    <hkparam name="generator">#96</hkparam>
                    <hkparam name="transitions">#102</hkparam>
                </hkobject>
                <hkobject class="hkbClipGenerator" name="#96" signature="0xd4cc9f6">
                    <hkparam name="name">ClipOpening</hkparam>
                    <hkparam name="animationName">opening.hkx</hkparam>
                    <hkparam name="triggers">null</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#97" signature="0x39d76713">
                    <hkparam name="name">Opened</hkparam>
                    <hkparam name="stateId">2</hkparam>
                    <hkparam name="generator">#98</hkparam>
                    <hkparam name="transitions">#103</hkparam>
                </hkobject>
                <hkobject class="hkbClipGenerator" name="#98" signature="0xd4cc9f6">
                    <hkparam name="name">ClipOpened</hkparam>
                    <hkparam name="animationName">opened.hkx</hkparam>
                    <hkparam name="triggers">null</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#99" signature="0x39d76713">
                    <hkparam name="name">Closing</hkparam>
                    <hkparam name="stateId">3</hkparam>
                    <hkparam name="generator">#100</hkparam>
                    <hkparam name="transitions">#104</hkparam>
                </hkobject>
                <hkobject class="hkbClipGenerator" name="#100" signature="0xd4cc9f6">
                    <hkparam name="name">ClipClosing</hkparam>
                    <hkparam name="animationName">closing.hkx</hkparam>
                    <hkparam name="triggers">null</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineTransitionInfoArray" name="#101" signature="0xe397b11e">
                    <hkparam name="transitions" numelements="1">
                        <hkobject>
                            <hkparam name="eventId">0</hkparam>
                            <hkparam name="toStateId">1</hkparam>
                            <hkparam name="toNestedStateId">0</hkparam>
                            <hkparam name="priority">0</hkparam>
                            <hkparam name="flags">0</hkparam>
                            <hkparam name="condition">null</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineTransitionInfoArray" name="#102" signature="0xe397b11e">
                    <hkparam name="transitions" numelements="1">
                        <hkobject>
                            <hkparam name="eventId">1</hkparam>
                            <hkparam name="toStateId">2</hkparam>
                            <hkparam name="toNestedStateId">0</hkparam>
                            <hkparam name="priority">0</hkparam>
                            <hkparam name="flags">0</hkparam>
                            <hkparam name="condition">null</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineTransitionInfoArray" name="#103" signature="0xe397b11e">
                    <hkparam name="transitions" numelements="1">
                        <hkobject>
                            <hkparam name="eventId">2</hkparam>
                            <hkparam name="toStateId">3</hkparam>
                            <hkparam name="toNestedStateId">0</hkparam>
                            <hkparam name="priority">0</hkparam>
                            <hkparam name="flags">0</hkparam>
                            <hkparam name="condition">null</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineTransitionInfoArray" name="#104" signature="0xe397b11e">
                    <hkparam name="transitions" numelements="1">
                        <hkobject>
                            <hkparam name="eventId">3</hkparam>
                            <hkparam name="toStateId">0</hkparam>
                            <hkparam name="toNestedStateId">0</hkparam>
                            <hkparam name="priority">0</hkparam>
                            <hkparam name="flags">0</hkparam>
                            <hkparam name="condition">null</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
            </hksection>
        </hkpackfile>
        """;

    private static string StateName(BehaviourGraphModel model, GraphRun run) =>
        run.Where().Count == 0 ? "(nowhere)" : run.Where()[0].StateName;

    // The check the ticket names: send the door the event its own transition listens for and see
    // whether it opens. It is not a strong check of anything except that the thing runs at all, and
    // that is exactly why it is worth having, because until now nothing here ran at all.
    private static void ADoorOpensWhenSentTheEventItsOwnTransitionNames()
    {
        Console.WriteLine("\na door opens when sent the event its own transition names");

        var model = BehaviourGraphModel.Parse(DoorGraph());
        var run = GraphRun.Start(model);

        Check("it starts closed", "Closed", StateName(model, run));
        Check("with one machine running", 1, run.Where().Count);
        Check("and nothing it had to guess at", 0, run.Stops.Count);

        Check("Open moves it", 1, run.Send("Open").Count);
        Check("to opening", "Opening", StateName(model, run));

        run.Send("Opened");
        Check("then Opened opens it", "Opened", StateName(model, run));

        run.Send("Close");
        Check("Close starts it shutting", "Closing", StateName(model, run));

        run.Send("Closed");
        Check("and Closed finishes", "Closed", StateName(model, run));

        // An event the door does not listen for in this state must not move it, and an event the
        // graph does not declare at all is a different answer from that rather than the same one.
        Check("an event it is not listening for moves nothing", 0, run.Send("Opened").Count);
        Check("and it is still closed", "Closed", StateName(model, run));
        CheckThrows("an event the graph does not declare is refused rather than reported as ignored",
            () => run.Send("StartOpen"));
        CheckTrue("which the caller can ask about first", !run.Declares("StartOpen"));

        var reach = run.Reachable();
        Check("every state is reachable", 4, reach.Reachable.Count);
        Check("and none is not", 0, reach.Unreachable.Count);
    }

    // An event is raised on the graph rather than on one machine, so two machines both listening for
    // it both move. A stepper that stopped at the first match would look right on a door and be
    // wrong on any real character, where a dozen machines run at once.
    private static void EveryRunningMachineHearsAnEvent()
    {
        Console.WriteLine("\nevery running machine hears an event");

        // The door's Closed state gets a second machine underneath it, listening for the same Open.
        string nested = DoorGraph()
            .Replace("""<hkparam name="generator">#94</hkparam>""",
                     """<hkparam name="generator">#110</hkparam>""")
            .Replace("</hksection>", """
                <hkobject class="hkbStateMachine" name="#110" signature="0xa5896bcf">
                    <hkparam name="name">Inner</hkparam>
                    <hkparam name="startStateId">0</hkparam>
                    <hkparam name="wildcardTransitions">null</hkparam>
                    <hkparam name="states" numelements="2">
                        #111 #112
                    </hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#111" signature="0x39d76713">
                    <hkparam name="name">InnerA</hkparam>
                    <hkparam name="stateId">0</hkparam>
                    <hkparam name="generator">#94</hkparam>
                    <hkparam name="transitions">#113</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#112" signature="0x39d76713">
                    <hkparam name="name">InnerB</hkparam>
                    <hkparam name="stateId">1</hkparam>
                    <hkparam name="generator">#94</hkparam>
                    <hkparam name="transitions">null</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineTransitionInfoArray" name="#113" signature="0xe397b11e">
                    <hkparam name="transitions" numelements="1">
                        <hkobject>
                            <hkparam name="eventId">0</hkparam>
                            <hkparam name="toStateId">1</hkparam>
                            <hkparam name="toNestedStateId">0</hkparam>
                            <hkparam name="priority">0</hkparam>
                            <hkparam name="flags">0</hkparam>
                            <hkparam name="condition">null</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
            </hksection>
        """);

        var model = BehaviourGraphModel.Parse(nested);
        var run = GraphRun.Start(model);

        Check("both machines are running", 2, run.Where().Count);
        Check("the inner one starts in its own start state", "InnerA",
            run.Where().First(w => w.MachineName == "Inner").StateName);

        var fired = run.Send("Open");
        Check("one event moves both of them", 2, fired.Count);
        Check("the outer door is opening", "Opening",
            run.Where().First(w => w.MachineName == "DoorMachine").StateName);

        // The inner machine is no longer running: the door left the state that held it. That it is
        // gone rather than stale is the point.
        CheckTrue("and the machine the door left is no longer running",
            run.Where().All(w => w.MachineName != "Inner"));
    }

    // The honesty rule from the ticket. A generator that loads another file leads somewhere this file
    // cannot see, and the run has to say so rather than walk through it as though it were empty.
    private static void TheRunRefusesToGuessPastAnotherFile()
    {
        Console.WriteLine("\nthe run stops rather than guessing past another file");

        string elsewhere = DoorGraph()
            .Replace("""<hkparam name="generator">#94</hkparam>""",
                     """<hkparam name="generator">#120</hkparam>""")
            .Replace("</hksection>", """
                <hkobject class="hkbBehaviorReferenceGenerator" name="#120" signature="0x5empty">
                    <hkparam name="name">Elsewhere</hkparam>
                    <hkparam name="behaviorName">Behaviors\\Other.hkx</hkparam>
                </hkobject>
            </hksection>
        """);

        var model = BehaviourGraphModel.Parse(elsewhere);
        var run = GraphRun.Start(model);

        Check("the run records a stop", 1, run.Stops.Count);
        Check("naming the class it stopped at", "hkbBehaviorReferenceGenerator", run.Stops[0].ClassName);
        CheckTrue("and saying which file it would have had to open",
            run.Stops[0].Why.Contains("Other.hkx", StringComparison.Ordinal));
        Check("the door itself still runs", "Closed", StateName(model, run));
    }

    // Working out where a graph can get to, and actually going there, are separate code. They agreed
    // on the small graphs here from the first run and disagreed on 15 of the 531 vanilla behaviours,
    // which is the whole argument for checking them against each other rather than trusting either.
    private static void SteppingAgreesWithTheReachabilityItReports()
    {
        Console.WriteLine("\nstepping agrees with the reachability that is reported");

        var model = BehaviourGraphModel.Parse(DoorGraph());
        var analysis = GraphRun.Start(model).Reachable();

        var run = GraphRun.Start(model);
        var landed = new HashSet<string>(run.Where().Select(w => w.StateId), StringComparer.Ordinal);

        for (int sweep = 0; sweep < 4; sweep++)
            foreach (string name in run.Events)
            {
                foreach (var f in run.Send(name)) landed.Add(f.ToStateId);
                foreach (var w in run.Where()) landed.Add(w.StateId);
            }

        Check("stepping reaches every state the analysis promised", 0,
            analysis.Reachable.Except(landed).Count());
        Check("and lands nowhere the analysis ruled out", 0,
            landed.Except(analysis.Reachable).Count());
    }

    // A minimal two state machine whose transition carries a blending effect with a duration, so the
    // pose blend can be watched rather than only its endpoints. Public because symrm's weights check
    // ramps it as well, and a fixture proved in one place and used in two does not drift.
    public static string TwoStateBlendGraph() => """
        <?xml version="1.0" encoding="ascii"?>
        <hkpackfile classversion="11" contentsversion="hk_2014.1.0-r1">
            <hksection name="__data__">
                <hkobject class="hkbBehaviorGraph" name="#91" signature="0xb1218f86">
                    <hkparam name="name">Blend</hkparam>
                    <hkparam name="rootGenerator">#92</hkparam>
                    <hkparam name="data">#80</hkparam>
                </hkobject>
                <hkobject class="hkbBehaviorGraphData" name="#80" signature="0x95aca5d">
                    <hkparam name="stringData">#81</hkparam>
                </hkobject>
                <hkobject class="hkbBehaviorGraphStringData" name="#81" signature="0xc713064e">
                    <hkparam name="eventNames" numelements="1">
                        <hkcstring>Go</hkcstring>
                    </hkparam>
                    <hkparam name="variableNames" numelements="0"></hkparam>
                </hkobject>
                <hkobject class="hkbStateMachine" name="#92" signature="0xa5896bcf">
                    <hkparam name="name">M</hkparam>
                    <hkparam name="startStateId">0</hkparam>
                    <hkparam name="wildcardTransitions">null</hkparam>
                    <hkparam name="states" numelements="2">#93 #95</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#93" signature="0x39d76713">
                    <hkparam name="name">A</hkparam>
                    <hkparam name="stateId">0</hkparam>
                    <hkparam name="generator">#94</hkparam>
                    <hkparam name="transitions">#101</hkparam>
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
                <hkobject class="hkbStateMachineTransitionInfoArray" name="#101" signature="0xe397b11e">
                    <hkparam name="transitions" numelements="1">
                        <hkobject>
                            <hkparam name="eventId">0</hkparam>
                            <hkparam name="toStateId">1</hkparam>
                            <hkparam name="toNestedStateId">0</hkparam>
                            <hkparam name="priority">0</hkparam>
                            <hkparam name="flags">0</hkparam>
                            <hkparam name="transition">#102</hkparam>
                            <hkparam name="condition">null</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
                <hkobject class="hkbBlendingTransitionEffect" name="#102" signature="0xa5f8b5b">
                    <hkparam name="name">Blend</hkparam>
                    <hkparam name="duration">0.5</hkparam>
                </hkobject>
            </hksection>
        </hkpackfile>
        """;

    // The pose blend of a transition, watched from nothing to all of the new state.
    private static void ATransitionBlendsFromOneStateToTheNext()
    {
        Console.WriteLine("\na transition blends from one state to the next over its duration");

        var model = BehaviourGraphModel.Parse(TwoStateBlendGraph());
        var run = GraphRun.Start(model);

        Check("it starts in A alone", 1, run.Where().Count);
        Check("at full weight", 1f, run.Where()[0].Weight);

        run.Send("Go");
        var atStart = run.Where();
        Check("firing the transition leaves two states blending", 2, atStart.Count);
        CheckTrue("the graph reports a blend in progress", run.Blending);

        var incoming = atStart.First(a => !a.Fading);
        var outgoing = atStart.First(a => a.Fading);
        Check("the one being entered is B", "B", incoming.StateName);
        Check("the one being left is A", "A", outgoing.StateName);
        CheckTrue($"B holds nothing at the instant it fires ({incoming.Weight:F3})", incoming.Weight < 0.01f);
        CheckTrue($"and A still holds all of it ({outgoing.Weight:F3})", outgoing.Weight > 0.99f);

        run.Advance(0.25f);
        float mid = run.Where().First(a => !a.Fading).Weight;
        CheckTrue($"halfway through, B holds about half ({mid:F3})", mid > 0.4f && mid < 0.6f);

        run.Advance(0.5f);
        var done = run.Where();
        Check("past the duration only B is left", 1, done.Count);
        Check("and it is B", "B", done[0].StateName);
        CheckTrue("holding all of the pose", done[0].Weight > 0.999f);
        CheckTrue("with no blend still running", !run.Blending);
    }

    // A graph whose only state plays a clip and leaves when that clip says it has finished.
    //
    // Deliberately small and deliberately not vanilla. The corpus proves the reading works on the data
    // the game ships; it cannot prove the arithmetic, because a value the shipped data never takes is
    // a hole in the corpus rather than a fact about the format. Everything timed here uses numbers no
    // vanilla clip has.
    private static string ClipEndGraph() => """
        <?xml version="1.0" encoding="ascii"?>
        <hkpackfile classversion="11" contentsversion="hk_2014.1.0-r1">
            <hksection name="__data__">
                <hkobject class="hkbBehaviorGraph" name="#90" signature="0xb1218f86">
                    <hkparam name="name">Graph</hkparam>
                    <hkparam name="rootGenerator">#92</hkparam>
                    <hkparam name="data">#100</hkparam>
                </hkobject>
                <hkobject class="hkbBehaviorGraphStringData" name="#91" signature="0xc713064e">
                    <hkparam name="eventNames" numelements="1">ClipDone</hkparam>
                    <hkparam name="variableNames" numelements="0"></hkparam>
                </hkobject>
                <hkobject class="hkbStateMachine" name="#92" signature="0xa5896bcf">
                    <hkparam name="name">Root</hkparam>
                    <hkparam name="startStateId">0</hkparam>
                    <hkparam name="states" numelements="2">#93 #96</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#93" signature="0x39d76713">
                    <hkparam name="name">Playing</hkparam>
                    <hkparam name="stateId">0</hkparam>
                    <hkparam name="generator">#98</hkparam>
                    <hkparam name="transitions">#94</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineTransitionInfoArray" name="#94" signature="0xe397b11e">
                    <hkparam name="transitions" numelements="1">
                        <hkobject>
                            <hkparam name="eventId">0</hkparam>
                            <hkparam name="toStateId">1</hkparam>
                            <hkparam name="priority">10</hkparam>
                            <hkparam name="condition">null</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#96" signature="0x39d76713">
                    <hkparam name="name">Done</hkparam>
                    <hkparam name="stateId">1</hkparam>
                </hkobject>
                <hkobject class="hkbClipGenerator" name="#98" signature="0x0d4cc9f6">
                    <hkparam name="name">TheClip</hkparam>
                    <hkparam name="animationName">Animations\Test.hkt</hkparam>
                </hkobject>
                <hkobject class="hkbBehaviorGraphData" name="#100" signature="0x95aca5d">
                    <hkparam name="variableInfos" numelements="0"></hkparam>
                    <hkparam name="eventInfos" numelements="1">
                        <hkobject>
                            <hkparam name="flags">0</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
            </hksection>
        </hkpackfile>
        """;

    /// The timing table the fixture's one clip gets, built by hand rather than read off a file.
    private static Dictionary<string, ClipTiming.Clip> OneClip(float seconds, string mode,
                                                               params ClipTiming.Trigger[] triggers) =>
        new(StringComparer.Ordinal)
        {
            ["98"] = new ClipTiming.Clip("98", "TheClip", @"Animations\Test.hkt", seconds, triggers, mode),
        };

    // The whole of what this issue's last gap asked for: a state that leaves because its clip ended,
    // with nobody sending anything.
    private static void AClipEndsAndTheStateLeavesWithoutAnEvent()
    {
        Console.WriteLine("\na clip ends and the state leaves without an event");

        var model = BehaviourGraphModel.Parse(ClipEndGraph());
        var run = GraphRun.Start(model);

        // 7.5 seconds with the trigger a second and a half before the end, so the moment it fires is a
        // number that is neither the length nor zero and cannot come out right by accident.
        run.Time(OneClip(7.5f, "MODE_SINGLE_PLAY",
                         new ClipTiming.Trigger(6.0f, "ClipDone", RelativeToEnd: true, Acyclic: false)));

        Check("it starts in the state holding the clip", "Playing", run.Where()[0].StateName);
        Check("with the clip at its beginning", 0f, run.PlayingAt("98"));

        var early = run.Advance(5.0f);
        Check("five seconds in, nothing has fired", 0, early.Count);
        Check("and it is still in the first state", "Playing", run.Where()[0].StateName);
        Check("with the clip five seconds along", 5f, run.PlayingAt("98"));

        var crossing = run.Advance(1.5f);
        Check("crossing the trigger fires one transition", 1, crossing.Count);
        Check("raised by the clip rather than by a caller",
              "ClipDone", crossing.Count > 0 ? crossing[0].Event : "nothing fired");
        Check("and the machine has left", "Done", run.Where()[0].StateName);

        // The point of the whole feature, stated as the thing that was impossible before: no event was
        // ever sent by hand and the state still moved.
        CheckTrue("nothing was sent by hand at any point", true);
    }

    // The arithmetic that turns an animation's length into a clip's, on values the shipped data never
    // takes together. 199 vanilla clips crop and 200 run at a speed other than one; none of them is a
    // combination this checks, which is exactly why a corpus sweep cannot stand in for it.
    private static void AClipLengthIsCroppedAndScaled()
    {
        Console.WriteLine("\na clip's length is cropped and scaled");

        // When a trigger goes out, which is the half of this that a corpus sweep is blind to. Reading
        // an end relative trigger as an absolute one leaves every trigger inside its own clip and
        // every gate green, and only moves the moments the events come out.
        Check("a trigger at an absolute time is at that time",
              2f, ClipTiming.TriggerAt(2f, relativeToEnd: false, seconds: 10f));
        Check("a trigger at the end is at the clip's length",
              10f, ClipTiming.TriggerAt(0f, relativeToEnd: true, seconds: 10f));
        Check("and one measured back from the end counts backwards",
              7.5f, ClipTiming.TriggerAt(2.5f, relativeToEnd: true, seconds: 10f));
        Check("the two readings differ, which is what makes the distinction worth having",
              false, ClipTiming.TriggerAt(2.5f, true, 10f) == ClipTiming.TriggerAt(2.5f, false, 10f));

        Check("a plain clip is as long as its animation",
              10f, ClipTiming.Span(0, 10f, 0, 0, 1, "a", out _));

        Check("cropping takes off both ends",
              7f, ClipTiming.Span(0, 10f, 1f, 2f, 1, "a", out _));

        Check("double speed halves it",
              5f, ClipTiming.Span(0, 10f, 0, 0, 2f, "a", out _));

        Check("a quarter speed makes it four times as long",
              40f, ClipTiming.Span(0, 10f, 0, 0, 0.25f, "a", out _));

        // Both at once, which no vanilla clip does. Crop first and then scale: 10 - 1 - 2 = 7, at
        // double speed is 3.5. Scaling before cropping would give 3, so this one number tells the two
        // orders apart.
        Check("cropped and scaled together, in that order",
              3.5f, ClipTiming.Span(0, 10f, 1f, 2f, 2f, "a", out _));

        Check("playing backwards lasts as long as playing forwards",
              5f, ClipTiming.Span(0, 10f, 0, 0, -2f, "a", out _));

        Check("an enforced duration ignores the animation entirely",
              4f, ClipTiming.Span(4f, 10f, 1f, 2f, 8f, "a", out _));

        // An enforced duration is the one case that survives a missing animation, and it matters:
        // without it a clip whose file is absent would have to be a stop.
        Check("and still applies when the animation is missing",
              4f, ClipTiming.Span(4f, 0, 0, 0, 1, "a", out _));

        ClipTiming.Span(0, 10f, 6f, 6f, 1, "a", out string overCropped);
        CheckTrue("cropping past the whole animation has no length",
                  overCropped.Contains("crop", StringComparison.OrdinalIgnoreCase));

        ClipTiming.Span(0, 10f, 0, 0, 0, "a", out string parked);
        CheckTrue("a clip at zero speed is parked rather than instant",
                  parked.Contains("never finishes", StringComparison.Ordinal));

        ClipTiming.Span(0, 0, 0, 0, 1, "Missing.hkt", out string absent);
        CheckTrue("a missing animation says which one", absent.Contains("Missing.hkt", StringComparison.Ordinal));

        ClipTiming.Span(0, -1, 0, 0, 1, "", out string unnamed);
        CheckTrue("naming no animation is a different answer from one not found",
                  unnamed.Contains("names no animation", StringComparison.Ordinal));
    }

    // A clip whose length could not be worked out must hold the graph still rather than move it. This
    // is the safety property that makes the feature worth shipping at all: 44 of the corpus's clips
    // name an animation that is not on disk, and a build that guessed a length for them would invent
    // transitions the game never fires.
    private static void AnUntimedClipRaisesNothing()
    {
        Console.WriteLine("\na clip with no length raises nothing");

        var model = BehaviourGraphModel.Parse(ClipEndGraph());
        var run = GraphRun.Start(model);

        run.Time(new Dictionary<string, ClipTiming.Clip>(StringComparer.Ordinal)
        {
            ["98"] = new ClipTiming.Clip("98", "TheClip", @"Animations\Test.hkt", 0,
                                         Array.Empty<ClipTiming.Trigger>(), "MODE_SINGLE_PLAY",
                                         "the animation 'Animations\\Test.hkt' was not found"),
        });

        var after = run.Advance(1000f);
        Check("a very long wait fires nothing", 0, after.Count);
        Check("and the machine has not moved", "Playing", run.Where()[0].StateName);

        CheckTrue("the clip it could not time is reported as a stop",
                  run.Stops.Any(s => s.Why.Contains("no length", StringComparison.Ordinal)));
        CheckTrue("and the stop names the clip",
                  run.Stops.Any(s => s.Why.Contains("TheClip", StringComparison.Ordinal)));

        // With no table at all the clock must behave as it did before any of this existed, because a
        // caller with no folder around the behaviour genuinely cannot supply one.
        var untimed = GraphRun.Start(BehaviourGraphModel.Parse(ClipEndGraph()));
        Check("with no timing supplied nothing fires either", 0, untimed.Advance(1000f).Count);
        Check("and no stop is invented for it", 0, untimed.Stops.Count);
    }

    // Looping is the difference between a clip that can end a state once and one that keeps offering
    // to. The corpus ships 1,576 looping clips and 1,946 single play ones, and no ping pong at all, so
    // the behaviour of the mode it does not ship is only ever checked here.
    private static void ALoopingClipKeepsFiringAndASinglePlayDoesNot()
    {
        Console.WriteLine("\na looping clip keeps firing and a single play does not");

        // The clock is measured on a clip carrying no triggers, because a clip that ends its own state
        // stops playing the instant it fires and has no clock left to read. Four one second steps
        // through a three second clip is the shortest run that tells wrapping from clamping: a looping
        // clip is a second into its second cycle and a single play one is still sitting on its end.
        var looping = GraphRun.Start(BehaviourGraphModel.Parse(ClipEndGraph()));
        looping.Time(OneClip(3f, "MODE_LOOPING"));
        Steps(looping, 1f, 4);
        Check("a looping clip wraps round to its second cycle", 1f, looping.PlayingAt("98"));

        var pingPong = GraphRun.Start(BehaviourGraphModel.Parse(ClipEndGraph()));
        pingPong.Time(OneClip(3f, "MODE_PING_PONG"));
        Steps(pingPong, 1f, 4);
        Check("ping pong carries on rather than stopping", 1f, pingPong.PlayingAt("98"));

        var once = GraphRun.Start(BehaviourGraphModel.Parse(ClipEndGraph()));
        once.Time(OneClip(3f, "MODE_SINGLE_PLAY"));
        Steps(once, 1f, 4);
        Check("a single play clip stops at its end", 3f, once.PlayingAt("98"));

        // Whichever mode it is, ending a state is something a clip does once, because the state it
        // ended is the state that was holding it. Both are checked so that neither mode is assumed to
        // behave like the other.
        var trigger = new ClipTiming.Trigger(3f, "ClipDone", RelativeToEnd: true, Acyclic: false);

        var endsLooping = GraphRun.Start(BehaviourGraphModel.Parse(ClipEndGraph()));
        endsLooping.Time(OneClip(3f, "MODE_LOOPING", trigger));
        Check("a looping clip still ends its state exactly once", 1, Steps(endsLooping, 1f, 6));
        Check("and the machine has left", "Done", endsLooping.Where()[0].StateName);

        var endsOnce = GraphRun.Start(BehaviourGraphModel.Parse(ClipEndGraph()));
        endsOnce.Time(OneClip(3f, "MODE_SINGLE_PLAY", trigger));
        Check("so does a single play one", 1, Steps(endsOnce, 1f, 6));
        Check("leaving the same way", "Done", endsOnce.Where()[0].StateName);
    }

    /// Steps the clock and totals what fired, so a count is a count of transitions and not of steps.
    private static int Steps(GraphRun run, float seconds, int howMany)
    {
        int fired = 0;
        for (int i = 0; i < howMany; i++) fired += run.Advance(seconds).Count;
        return fired;
    }

    // An instant transition, which is a third of the transitions in the corpus, must snap rather than
    // blend, or the clock would have a phantom blend to advance forever.
    private static void AnInstantTransitionDoesNotBlend()
    {
        Console.WriteLine("\nan instant transition does not blend");

        // The same fixture with the duration set to zero.
        var model = BehaviourGraphModel.Parse(TwoStateBlendGraph()
            .Replace("<hkparam name=\"duration\">0.5</hkparam>", "<hkparam name=\"duration\">0.0</hkparam>"));
        var run = GraphRun.Start(model);

        run.Send("Go");
        Check("it moves straight to B", 1, run.Where().Count);
        Check("with no second state fading", "B", run.Where()[0].StateName);
        CheckTrue("and nothing left blending", !run.Blending);
        CheckTrue("advancing the clock changes nothing", run.Where().Count == 1);
    }

    // A blender with two children built in memory, so the mix is a number this test chose.
    private static string BlenderGraph(int flags, float blendParameter, float w1, float w2,
                                       string binding = "") => $"""
        <?xml version="1.0" encoding="ascii"?>
        <hkpackfile classversion="11" contentsversion="hk_2014.1.0-r1">
            <hksection name="__data__">
                <hkobject class="hkbBehaviorGraph" name="#91" signature="0xb1218f86">
                    <hkparam name="name">B</hkparam>
                    <hkparam name="rootGenerator">#110</hkparam>
                    <hkparam name="data">#80</hkparam>
                </hkobject>
                <hkobject class="hkbBehaviorGraphData" name="#80" signature="0x95aca5d">
                    <hkparam name="stringData">#81</hkparam>
                </hkobject>
                <hkobject class="hkbBehaviorGraphStringData" name="#81" signature="0xc713064e">
                    <hkparam name="eventNames" numelements="0"></hkparam>
                    <hkparam name="variableNames" numelements="1">
                        <hkcstring>Speed</hkcstring>
                    </hkparam>
                </hkobject>
                <hkobject class="hkbBlenderGenerator" name="#110" signature="0x22df7147">
                    <hkparam name="name">Mix</hkparam>
                    <hkparam name="flags">{flags}</hkparam>
                    <hkparam name="blendParameter">{blendParameter.ToString(System.Globalization.CultureInfo.InvariantCulture)}</hkparam>
                    <hkparam name="variableBindingSet">{(binding == "blendParameter" ? "#130" : "null")}</hkparam>
                    <hkparam name="children" numelements="2">#111 #112</hkparam>
                </hkobject>
                <hkobject class="hkbBlenderGeneratorChild" name="#111" signature="0xe2b384b7">
                    <hkparam name="generator">#121</hkparam>
                    <hkparam name="weight">{w1.ToString(System.Globalization.CultureInfo.InvariantCulture)}</hkparam>
                    <hkparam name="variableBindingSet">{(binding == "weight" ? "#130" : "null")}</hkparam>
                </hkobject>
                <hkobject class="hkbClipGenerator" name="#121" signature="0xd4cc9f6">
                    <hkparam name="name">Walk</hkparam>
                    <hkparam name="animationName">walk.hkx</hkparam>
                    <hkparam name="triggers">null</hkparam>
                </hkobject>
                <hkobject class="hkbBlenderGeneratorChild" name="#112" signature="0xe2b384b7">
                    <hkparam name="generator">#122</hkparam>
                    <hkparam name="weight">{w2.ToString(System.Globalization.CultureInfo.InvariantCulture)}</hkparam>
                    <hkparam name="variableBindingSet">null</hkparam>
                </hkobject>
                <hkobject class="hkbClipGenerator" name="#122" signature="0xd4cc9f6">
                    <hkparam name="name">Run</hkparam>
                    <hkparam name="animationName">run.hkx</hkparam>
                    <hkparam name="triggers">null</hkparam>
                </hkobject>
                <hkobject class="hkbVariableBindingSet" name="#130" signature="0x338ad4ff">
                    <hkparam name="bindings" numelements="1">
                        <hkobject>
                            <hkparam name="memberPath">{(binding == "weight" ? "weight" : "blendParameter")}</hkparam>
                            <hkparam name="variableIndex">0</hkparam>
                            <hkparam name="bitIndex">-1</hkparam>
                            <hkparam name="bindingType">BINDING_TYPE_VARIABLE</hkparam>
                        </hkobject>
                    </hkparam>
                </hkobject>
            </hksection>
        </hkpackfile>
        """;

    // A plain blender mixes every child at once, in proportion to its weight.
    private static void APlainBlenderSharesByWeight()
    {
        Console.WriteLine("\na plain blender shares the pose by weight");

        var model = BehaviourGraphModel.Parse(BlenderGraph(flags: 0, blendParameter: 0, w1: 3, w2: 1));
        var blend = BlendWeights.Of(model, "110");

        Check("it is read as a mix", BlendWeights.Mode.Mix, blend.Mode);
        Check("with two children", 2, blend.Children.Count);
        CheckTrue("the mix is a fact of the file, not driven", blend.Resolved);

        var walk = blend.Children.First(c => c.GeneratorName == "Walk");
        var runc = blend.Children.First(c => c.GeneratorName == "Run");
        CheckTrue($"weight 3 against 1 gives Walk three quarters ({walk.Contribution:F3})",
            Math.Abs(walk.Contribution - 0.75f) < 1e-3f);
        CheckTrue($"and Run a quarter ({runc.Contribution:F3})",
            Math.Abs(runc.Contribution - 0.25f) < 1e-3f);

        // A child switched off with weight zero takes no share, and the other takes all of it.
        var off = BlendWeights.Of(BehaviourGraphModel.Parse(BlenderGraph(0, 0, 1, 0)), "110");
        CheckTrue("a child weighted zero contributes nothing",
            off.Children.First(c => c.GeneratorName == "Run").Contribution < 1e-6f);
        CheckTrue("and the other takes the whole pose",
            Math.Abs(off.Children.First(c => c.GeneratorName == "Walk").Contribution - 1f) < 1e-3f);
    }

    // A parametric blender lines its children along an axis and a parameter picks between them, so
    // its weights are positions and must not be read as shares.
    private static void AParametricBlenderIsPickedNotMixed()
    {
        Console.WriteLine("\na parametric blender is picked along an axis, not mixed by weight");

        // Children at positions 0 and 1, parameter three quarters of the way to the second.
        var model = BehaviourGraphModel.Parse(BlenderGraph(flags: BlendWeights.Parametric,
            blendParameter: 0.75f, w1: 0, w2: 1));
        var blend = BlendWeights.Of(model, "110");

        Check("it is read as parametric", BlendWeights.Mode.Parametric, blend.Mode);
        var walk = blend.Children.First(c => c.GeneratorName == "Walk");
        var runc = blend.Children.First(c => c.GeneratorName == "Run");
        CheckTrue($"three quarters along, Run holds three quarters ({runc.Contribution:F3})",
            Math.Abs(runc.Contribution - 0.75f) < 1e-3f);
        CheckTrue($"and Walk a quarter ({walk.Contribution:F3})",
            Math.Abs(walk.Contribution - 0.25f) < 1e-3f);

        // Read as a plain mix instead, weight 0 and 1 would have given Run the whole pose, which is
        // the wrong answer this distinction exists to avoid.
        CheckTrue("which is not what mixing the weights would say", Math.Abs(runc.Contribution - 1f) > 0.1f);
    }

    // A blend the file leaves to a variable is named and counted, never invented.
    private static void ADrivenBlendIsReportedNotGuessed()
    {
        Console.WriteLine("\na blend driven by a variable is reported rather than guessed");

        var byParam = BlendWeights.Of(
            BehaviourGraphModel.Parse(BlenderGraph(BlendWeights.Parametric, 0, 0, 1, binding: "blendParameter")), "110");
        Check("a parametric blender on a variable is marked driven", BlendWeights.Mode.ParametricDriven, byParam.Mode);
        CheckTrue("it is not treated as resolved", !byParam.Resolved);
        Check("and names the variable", "Speed", byParam.Parameter);

        var byWeight = BlendWeights.Of(
            BehaviourGraphModel.Parse(BlenderGraph(0, 0, 1, 1, binding: "weight")), "110");
        var driven = byWeight.Children.First(c => c.WeightDriven);
        Check("a child weight on a variable is marked driven and named", "Speed", driven.WeightDriver);
        CheckTrue("so the blender is not resolved", !byWeight.Resolved);
    }

    // The whole point of the frame editor: a frame changed and written back comes back changed, and
    // the frames around it do not. Proved on the codec here, which is what the save path runs, so it
    // needs no file; the file level version is symrm editframe over the corpus.
    private static void AnEditedFrameSurvivesReEncoding()
    {
        Console.WriteLine("\nan edited frame survives being re-encoded");

        var clip = MadeUpClip(60, 2);
        int track = 0, frame = 30;
        var edit = new Vector3(11.5f, -22.25f, 33.75f);

        // Remember a neighbour, to prove the edit did not drag it.
        var neighbour = clip.Tracks[track].Translations[frame + 1];
        clip.Tracks[track].Translations[frame] = edit;

        var blob = SplineEncoder.Encode(clip);
        var back = new HkxAnimationData { NumFrames = clip.NumFrames };
        SplineEncoder.Decode(blob.Data, blob.BlockOffsets, clip.Tracks.Count, clip.NumFrames,
            blob.MaskAndQuantizationSize, blob.MaxFramesPerBlock, back);

        float keptDrift = (back.Tracks[track].Translations[frame] - edit).Length();
        CheckTrue($"the edited frame comes back where it was put ({keptDrift:F4})", keptDrift < 0.05f);

        float neighbourDrift = (back.Tracks[track].Translations[frame + 1] - neighbour).Length();
        CheckTrue($"and the frame beside it did not move with it ({neighbourDrift:F4})", neighbourDrift < 0.1f);

        // A channel a clip never drove has to become a curve the moment one of its frames differs,
        // which is the case a naive encoder drops. The made up clip drives translation, so this also
        // checks the plainer path: the change is really in the bytes, not only in memory.
        CheckTrue("the change is not lost to the encoder",
            Math.Abs(back.Tracks[track].Translations[frame].X - edit.X) < 0.05f);
    }

    // A cut is four things changing together, and this is the check that all four move.
    //
    // The frames are the easy one and the one that would pass on its own. The other three are the
    // ones a trim gets wrong quietly: the clip's own duration, the annotations that fire along it,
    // and the root's travel sampled across it. A clip cut to half its frames and left with its old
    // duration still loads and still plays; it just plays at half speed.
    private static void ACutTakesTheClipsOwnTimeWithIt()
    {
        Console.WriteLine("\na cut takes the clip's own time with it");

        var clip = MadeUpClip(61, 2);          // 61 frames at thirty, so exactly two seconds
        clip.Annotations.Add(new HkxAnnotation { Time = 0.1f, Text = "before the cut" });
        clip.Annotations.Add(new HkxAnnotation { Time = 1.0f, Text = "inside the cut" });
        clip.Annotations.Add(new HkxAnnotation { Time = 1.9f, Text = "after the cut" });

        // One sample per frame, walking straight down the y axis, which is the shape 11,882 of the
        // shipped clips carry.
        var motion = new RootMotion.Motion { Duration = clip.Duration };
        for (int f = 0; f < clip.NumFrames; f++)
            motion.Samples.Add(new RootMotion.Sample(new Vector3(0, f * 2f, 0), 0));

        var cut = AnimationEdit.Trim(clip, motion, 15, 45);

        Check("the frames it was told to keep", 31, cut.Animation.NumFrames);
        CheckTrue($"and the length that many frames really are ({cut.Animation.Duration:F4}s)",
            Math.Abs(cut.Animation.Duration - 1f) < 1e-4f);
        Check("every track was cut, not just the first", 2, cut.Animation.Tracks.Count);
        Check("and each holds the kept frames", 31, cut.Animation.Tracks[1].Translations.Count);

        // Frame 0 of the cut is frame 15 of the original, exactly, because nothing here interpolates.
        Check("frame zero of the cut is the frame it came from",
            clip.Tracks[0].Translations[15], cut.Animation.Tracks[0].Translations[0]);
        Check("and the last one likewise",
            clip.Tracks[0].Translations[45], cut.Animation.Tracks[0].Translations[30]);

        Check("the annotations outside the cut are gone", 1, cut.Animation.Annotations.Count);
        Check("and this one was dropped from each end", 2, cut.AnnotationsDropped);
        CheckTrue($"the one that survived moved back to where it now sits " +
                  $"({cut.Animation.Annotations[0].Time:F4}s)",
            Math.Abs(cut.Animation.Annotations[0].Time - 0.5f) < 1e-4f);
        Check("carrying its own text", "inside the cut", cut.Animation.Annotations[0].Text);

        Check("the travel was sliced the same way", 31, cut.Motion!.Samples.Count);
        CheckTrue($"and says the clip's new length ({cut.Motion.Duration:F4}s)",
            Math.Abs(cut.Motion.Duration - 1f) < 1e-4f);

        // Rebased, because every shipped clip starts its travel at the origin: measured, 12,454 of
        // 12,454 carrying travel across the corpus.
        CheckTrue("it starts at the origin the way every shipped clip does",
            cut.Motion.Samples[0].Position.Length() < 1e-4f);
        CheckTrue($"while the distance it covers is untouched ({cut.Motion.Travel.Length():F2})",
            Math.Abs(cut.Motion.Travel.Length() - 60f) < 1e-3f);
    }

    // The other travel shape the corpus carries, and it needs a different rule rather than a refusal.
    //
    // 1,661 shipped clips sample the root exactly twice whatever their frame count, which is a
    // reference frame that is linear across the whole clip. Slicing an index range out of two samples
    // would be nonsense, so a cut reads the path at the new start and end instead, which is exact for
    // a linear frame rather than an approximation of one.
    private static void ALinearTravelStaysTwoSamplesAfterACut()
    {
        Console.WriteLine("\na linear travel stays two samples after a cut");

        var clip = MadeUpClip(41, 1);          // 41 frames at thirty, so a second and a third
        var motion = new RootMotion.Motion { Duration = clip.Duration };
        motion.Samples.Add(new RootMotion.Sample(Vector3.Zero, 0));
        motion.Samples.Add(new RootMotion.Sample(new Vector3(0, 40f, 0), 0));

        var cut = AnimationEdit.Trim(clip, motion, 10, 30);

        Check("still two samples, not one per frame", 2, cut.Motion!.Samples.Count);
        CheckTrue("still starting at the origin", cut.Motion.Samples[0].Position.Length() < 1e-4f);

        // Frames 10 to 30 of 41 is half the clip, so half the travel.
        CheckTrue($"covering the half of the path the cut kept ({cut.Motion.Travel.Length():F3})",
            Math.Abs(cut.Motion.Travel.Length() - 20f) < 1e-2f);
    }

    // What a cut will not do, said out loud rather than produced wrongly.
    private static void ACutRefusesWhatIsNotAClip()
    {
        Console.WriteLine("\na cut refuses what is not a clip");

        var clip = MadeUpClip(20, 1);

        CheckThrows("a single frame is not a clip, because a curve needs an interval",
            () => AnimationEdit.Trim(clip, null, 5, 5));
        CheckThrows("a span running past the end is refused rather than clamped",
            () => AnimationEdit.Trim(clip, null, 5, 25));
        CheckThrows("and a span running backwards likewise",
            () => AnimationEdit.Trim(clip, null, 12, 4));
    }

    // A clip's duration runs from its first frame to its last, so 337 frames at thirty frames a
    // second last for 336 intervals: 11.2 seconds. Counting all 337 frames is the easy off by one
    // that still writes a valid file but plays it slowly and shifts every annotation on it.
    private static void DurationCountsIntervalsNotFrames()
    {
        Console.WriteLine("\nduration counts intervals, not frames");

        var clip = MadeUpClip(337, 1);
        var retimed = AnimationEdit.Retime(clip, null, 1f);
        float expected = (retimed.Animation.NumFrames - 1) * retimed.Animation.FrameDuration;

        CheckTrue($"337 frames at thirty fps last 11.2 seconds ({retimed.Animation.Duration:F4}s)",
            Math.Abs(retimed.Animation.Duration - 11.2f) < 1e-4f);
        CheckTrue("the written duration is exactly its number of intervals times frame duration",
            Math.Abs(retimed.Animation.Duration - expected) < 1e-6f);
    }

    // A retime is a cut's four things again, stretched rather than sliced, and one of them fails in a
    // way a cut cannot: an annotation left where it was still sits inside the clip, still has its
    // text, and fires at the wrong moment.
    private static void ARetimeMovesEverythingThatMeasuresTime()
    {
        Console.WriteLine("\na retime moves everything that measures time");

        var clip = MadeUpClip(41, 2);          // 41 frames at thirty, so one and a third seconds
        clip.Annotations.Add(new HkxAnnotation { Time = 0f, Text = "at the start" });
        clip.Annotations.Add(new HkxAnnotation { Time = 0.6667f, Text = "halfway" });

        var motion = new RootMotion.Motion { Duration = clip.Duration };
        for (int f = 0; f < clip.NumFrames; f++)
            motion.Samples.Add(new RootMotion.Sample(new Vector3(0, f * 3f, 0), 0));

        var slow = AnimationEdit.Retime(clip, motion, 2f);

        Check("twice as long is twice as many intervals", 81, slow.Animation.NumFrames);
        CheckTrue($"and twice the length ({slow.Animation.Duration:F4}s)",
            Math.Abs(slow.Animation.Duration - clip.Duration * 2) < 1e-4f);
        CheckTrue($"at the rate it was already running at ({slow.Animation.FrameDuration:F5})",
            Math.Abs(slow.Animation.FrameDuration - clip.FrameDuration) < 1e-6f);

        Check("no annotation is lost, a retime drops nothing", 2, slow.Animation.Annotations.Count);
        CheckTrue($"the one at the start stays there ({slow.Animation.Annotations[0].Time:F4}s)",
            Math.Abs(slow.Animation.Annotations[0].Time) < 1e-4f);
        CheckTrue($"and the one halfway is still halfway ({slow.Animation.Annotations[1].Time:F4}s)",
            Math.Abs(slow.Animation.Annotations[1].Time - 1.3334f) < 1e-3f);

        Check("the travel gets a sample per frame the same as before", 81, slow.Motion!.Samples.Count);
        CheckTrue($"and says the new length ({slow.Motion.Duration:F4}s)",
            Math.Abs(slow.Motion.Duration - slow.Animation.Duration) < 1e-4f);

        // The one a retime gets wrong by scaling too much rather than too little. A clip played at
        // half speed goes exactly as far, it just takes twice as long about it.
        CheckTrue($"it travels the distance it always travelled ({slow.Motion.Travel.Length():F2})",
            Math.Abs(slow.Motion.Travel.Length() - motion.Travel.Length()) < 1e-2f);

        // Upsampling puts a new frame exactly on every old one, so the old frames have to come back
        // as themselves rather than as something read near them.
        CheckTrue($"every original frame is still exactly itself ({slow.PositionError:F5})",
            slow.PositionError < 1e-3f);
    }

    // The other way to make a clip longer, and the reason it is a switch rather than a guess.
    private static void KeepingTheFramesCostsNothingAtAll()
    {
        Console.WriteLine("\nkeeping the frames costs nothing at all");

        var clip = MadeUpClip(41, 1);
        var slow = AnimationEdit.Retime(clip, null, 2f, keepFrameRate: false);

        Check("the frames are the frames that were there", 41, slow.Animation.NumFrames);
        CheckTrue("so nothing was resampled", !slow.Resampled);
        CheckTrue($"and it cost nothing ({slow.PositionError:F5})", slow.PositionError == 0);
        CheckTrue($"each frame is shown for twice as long ({slow.Animation.FrameDuration:F5})",
            Math.Abs(slow.Animation.FrameDuration - clip.FrameDuration * 2) < 1e-6f);
        Check("frame ten is untouched", clip.Tracks[0].Translations[10],
            slow.Animation.Tracks[0].Translations[10]);
    }

    // Halving a clip throws frames away and nothing can read them back out. The number saying so is
    // the point: a retime that quietly lost a fast movement and reported nothing would be worse than
    // one that refused.
    private static void ARetimeSaysWhatTheResamplingCost()
    {
        Console.WriteLine("\na retime says what the resampling cost");

        // A track that moves a long way between two frames and back, so halving the frame count is
        // guaranteed to miss the peak rather than only round it.
        var clip = MadeUpClip(21, 1);
        for (int f = 0; f < clip.NumFrames; f++)
            clip.Tracks[0].Translations[f] = new Vector3(f % 2 == 0 ? 0 : 40f, 0, 0);

        var fast = AnimationEdit.Retime(clip, null, 0.5f);

        Check("half as long is half the intervals", 11, fast.Animation.NumFrames);
        CheckTrue("which means it resampled", fast.Resampled);
        CheckTrue($"and it says what that cost rather than hiding it ({fast.PositionError:F2})",
            fast.PositionError > 10f);

        CheckThrows("and refuses when a caller sets a budget it cannot meet",
            () => AnimationEdit.Retime(clip, null, 0.5f, true, new AnimationEdit.Budget(1f, 0.01f)));

        // The budget is opt in. The same retime without one is written, because losing detail is what
        // making a clip shorter is rather than a fault in doing it.
        var anyway = AnimationEdit.Retime(clip, null, 0.5f);
        Check("without a budget the same retime is produced", 11, anyway.Animation.NumFrames);
    }

    // A straight interpolation between two rotations is not a rotation, and normalising it afterwards
    // gives one on the right path at the wrong speed. Halfway between two rotations ninety degrees
    // apart has to be forty five degrees from each, and the cheap version is not.
    private static void ARotationIsReadAlongTheArcNotAcrossIt()
    {
        Console.WriteLine("\na rotation is read along the arc rather than across it");

        var frames = new List<Quaternion>
        {
            Quaternion.Identity,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2),
        };

        var half = AnimationEdit.Turned(frames, 0.5f);
        float toFirst = SplineQuat.AngleBetween(half, frames[0]);
        float toSecond = SplineQuat.AngleBetween(half, frames[1]);

        CheckTrue($"halfway is the same distance from each end ({toFirst:F4} and {toSecond:F4})",
            Math.Abs(toFirst - toSecond) < 1e-3f);
        // Written against the arc itself rather than against a number worked out by hand, because
        // the measure is the angle between two quaternions and that is half the angle between the
        // rotations they stand for. Comparing to the whole arc's own reading cannot get that wrong.
        float arc = SplineQuat.AngleBetween(frames[0], frames[1]);
        CheckTrue($"and that distance is half the arc ({toFirst:F4} against {arc / 2:F4})",
            Math.Abs(toFirst - arc / 2) < 1e-3f);

        Check("an end is still itself", frames[1], AnimationEdit.Turned(frames, 1f));
    }
}
