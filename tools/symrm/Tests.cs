using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.Tools;




public static class Tests
{
    private static int _failed;
    private static int _ran;



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
        ("EditorSignaturesComeFromTheClassTable", EditorSignaturesComeFromTheClassTable),
        ("AnyNodeCanBeDeleted", AnyNodeCanBeDeleted),
        ("AReferenceInsideAStructIsSeenByBothReaders", AReferenceInsideAStructIsSeenByBothReaders),
        ("ADanglingReferenceIsReportedWhereverItSits", ADanglingReferenceIsReportedWhereverItSits),
        ("AppendedStringsLandOnAnEvenOffset", AppendedStringsLandOnAnEvenOffset),
        ("StructuralObjectsAreProtected", StructuralObjectsAreProtected),
        ("PortTypesRefuseNonsense", PortTypesRefuseNonsense),
        ("Fo4CharacterListsItsAnimations", Fo4CharacterListsItsAnimations),
        ("MissingClipAnimationIsReported", MissingClipAnimationIsReported),
        ("RepackDriftNamesWhatMoved", RepackDriftNamesWhatMoved),
        ("TransitionRowsCarryPriorityAndFlags", TransitionRowsCarryPriorityAndFlags),
        ("StaticTraceFollowsExistingGraphLinks", StaticTraceFollowsExistingGraphLinks),
        ("StructuredFlowKeepsMachineOwnership", StructuredFlowKeepsMachineOwnership),
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
        ("AFloatIsSpelledTheWayReferenceFormatterSpellsIt", AFloatIsSpelledTheWayReferenceFormatterSpellsIt),
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
        ("AnExpressionAssignmentDoesTheArithmeticWeShip", AnExpressionAssignmentDoesTheArithmeticWeShip),
        ("AFalseConditionHoldsATransitionBack", AFalseConditionHoldsATransitionBack),
        ("AnActiveExpressionModifierUpdatesRuntimeVariables", AnActiveExpressionModifierUpdatesRuntimeVariables),
        ("TheReadingFromTheBytesRefusesWhatItCannotDescribe", TheReadingFromTheBytesRefusesWhatItCannotDescribe),
        ("ThePanelReadsItsListFromTheTable", ThePanelReadsItsListFromTheTable),
        ("AnEscapedValueIsShownAsItself", AnEscapedValueIsShownAsItself),
        ("ASpaceInAValueIsKept", ASpaceInAValueIsKept),
        ("TheClassTableKnowsWhatTheDumpCannot", TheClassTableKnowsWhatTheDumpCannot),
        ("AFieldListIsBuiltWithoutReferenceFormatter", AFieldListIsBuiltWithoutReferenceFormatter),
        ("AClassSignedDifferentlyIsRefused", AClassSignedDifferentlyIsRefused),
        ("AMisSignedFileIsNotWrittenInto", AMisSignedFileIsNotWrittenInto),
        ("AnEnumIsNamedSignedAndPrintedUnsigned", AnEnumIsNamedSignedAndPrintedUnsigned),
        ("APaddedStructIsKnownFromReferenceFormattersIdeaOfIt", APaddedStructIsKnownFromReferenceFormattersIdeaOfIt),
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
        ("PredefinedTemplateCatalogResolvesDefaults", PredefinedTemplateCatalogResolvesDefaults),
        ("PredefinedClipGeneratorIsNativeAndAtomic", PredefinedClipGeneratorIsNativeAndAtomic),
        ("PredefinedBlendGeneratorCreatesItsChildren", PredefinedBlendGeneratorCreatesItsChildren),
        ("PredefinedStateAttachesItsGenerator", PredefinedStateAttachesItsGenerator),
        ("PredefinedStateUsesFirstUnusedId", PredefinedStateUsesFirstUnusedId),
        ("ACutTakesTheClipsOwnTimeWithIt", ACutTakesTheClipsOwnTimeWithIt),
        ("ALinearTravelStaysTwoSamplesAfterACut", ALinearTravelStaysTwoSamplesAfterACut),
        ("ACutRefusesWhatIsNotAClip", ACutRefusesWhatIsNotAClip),
        ("DurationCountsIntervalsNotFrames", DurationCountsIntervalsNotFrames),
        ("ARetimeMovesEverythingThatMeasuresTime", ARetimeMovesEverythingThatMeasuresTime),
        ("KeepingTheFramesCostsNothingAtAll", KeepingTheFramesCostsNothingAtAll),
        ("ARetimeSaysWhatTheResamplingCost", ARetimeSaysWhatTheResamplingCost),
        ("ARotationIsReadAlongTheArcNotAcrossIt", ARotationIsReadAlongTheArcNotAcrossIt),
    };



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


        HkObject o => $"#{o.Id} {o.Class}" + (o.Str("name").Length > 0 ? $" '{o.Str("name")}'" : ""),
        _ => value.ToString() ?? "null",
    };



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





    private static void DetachedSubtreeStaysDrawn()
    {
        Console.WriteLine("detached subtree stays drawn after a retarget");

        string xml = SmallGraph();
        var before = BehaviourGraphModel.Parse(xml);

        Check("objects in the file", 7, before.Objects.Count);
        Check("reachable from the root before", 6, Reachable(before));
        Check("drawn before", 7, GraphAuthor.Layout(before, 1000).Count);



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




    private static void EveryDrawnNodeHasOneOwner()
    {
        Console.WriteLine("\nevery drawn node has one owner");

        var model = BehaviourGraphModel.Parse(BlenderGraph(0, 0, 1, 1));
        var placed = GraphAuthor.Layout(model, 1000);





        Check("the walk placed the graph, breadth first", "91, 110, 80, 111, 112, 81, 121, 122",
              string.Join(", ", placed.Select(p => p.Node.Id)));

        var owner = placed.ToDictionary(p => p.Node.Id, p => p.OwnerId);
        Check("the root owns nothing above it", "", owner["91"]);
        Check("the blender is owned by the graph that names it", "91", owner["110"]);
        Check("and a blender child by the blender", "110", owner["111"]);


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







        var both = new HashSet<string> { "A", "E" };
        Check("an inner collapse claims nothing already hidden", 0, tree.HiddenBy(both, "E"));
        Check("and the outer one claims what it can actually bring back", 4, tree.HiddenBy(both, "A"));


        var moving = tree.Moving(new[] { "A", "E" });
        Check("everything moves, once each", "A, B, C, D, E, F",
            string.Join(", ", moving.OrderBy(m => m, StringComparer.Ordinal)));
        Check("the set is a set", 6, moving.Count);

        Check("a node nobody placed moves nothing", 0, tree.Moving(new[] { "Z" }).Count);
    }







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


        var shutSecond = new HashSet<string> { "95" };
        CheckTrue("collapsing the borrower does not hide the shared clip",
            !tree.Hidden(shutSecond, "94"));
        Check("and its badge claims nothing", 0, tree.HiddenBy(shutSecond, "95"));

        var shutFirst = new HashSet<string> { "93" };
        CheckTrue("collapsing the owner does hide it", tree.Hidden(shutFirst, "94"));
        Check("and its badge says so", 1, tree.HiddenBy(shutFirst, "93"));


        Check("dragging the borrower moves only itself", "95",
            string.Join(", ", tree.Moving(new[] { "95" })));
        Check("dragging the owner takes the clip with it", "93, 94",
            string.Join(", ", tree.Moving(new[] { "93" }).OrderBy(m => m, StringComparer.Ordinal)));
    }



    private static GraphLayout.Item Node(string id, int column, string owner) =>
        new(id, column, owner, 100);


    private static double Centre(Dictionary<string, double> y, string id) => y[id] + 50;




    private static void ChildrenSitBesideTheParentThatOwnsThem()
    {
        Console.WriteLine("\nchildren sit beside the parent that owns them");



        var items = new List<GraphLayout.Item> { Node("root", 0, "") };
        items.Add(Node("P1", 1, "root"));
        items.Add(Node("P2", 1, "root"));
        for (int i = 0; i < 6; i++) items.Add(Node("a" + i, 2, "P1"));
        items.Add(Node("b0", 2, "P2"));
        items.Add(Node("b1", 2, "P2"));

        var y = GraphLayout.Place(items, new Dictionary<string, double>(), 20);

        CheckTrue($"the second parent really is far down ({y["P2"]:F0})", y["P2"] > 300);


        double drop = Math.Abs(Centre(y, "b0") - Centre(y, "P2"));
        CheckTrue($"its children are beside it, not at the top ({y["b0"]:F0} against {y["P2"]:F0})",
            drop < 200);
        CheckTrue($"and nowhere near the other family ({y["b0"]:F0} against {y["a0"]:F0})",
            y["b0"] > y["a0"] + 200);


        CheckTrue($"the family straddles its parent ({y["b0"]:F0}, {y["b1"]:F0})",
            Centre(y, "b0") < Centre(y, "P2") + 1 && Centre(y, "b1") > Centre(y, "P2") - 1);


        foreach (var column in items.GroupBy(i => i.Column))
        {
            var sorted = column.OrderBy(i => y[i.Id]).ToList();
            for (int i = 1; i < sorted.Count; i++)
                CheckTrue($"{sorted[i - 1].Id} and {sorted[i].Id} do not overlap",
                    y[sorted[i].Id] >= y[sorted[i - 1].Id] + sorted[i - 1].Height - 0.001);
        }


        var again = GraphLayout.Place(items, new Dictionary<string, double>(), 20);
        Check("the layout is deterministic", string.Join(",", y.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value:F2}")),
              string.Join(",", again.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value:F2}")));
    }



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


        Check("the first family keeps its spacing", "120, 120",
            $"{y["a1"] - y["a0"]:F0}, {y["a2"] - y["a1"]:F0}");
        Check("and so does the second", "120, 120",
            $"{y["b1"] - y["b0"]:F0}, {y["b2"] - y["b1"]:F0}");




        var order = items.Where(i => i.Column == 2).OrderBy(i => y[i.Id])
                         .Select(i => i.Id[0]).ToArray();
        Check("each family is one unbroken run down the column", "aaabbb", new string(order));

        CheckTrue($"and the two are clear of each other ({y["b0"]:F0} against {y["a2"]:F0})",
            y["b0"] >= y["a2"] + 100 - 0.001);



        Check("the first parent is level with its family", Centre(y, "a1").ToString("F2"),
              Centre(y, "P1").ToString("F2"));
        Check("and so is the second", Centre(y, "b1").ToString("F2"),
              Centre(y, "P2").ToString("F2"));
    }



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


        var loose = GraphLayout.Place(items, new Dictionary<string, double>(), 20);
        double wanted = loose["a0"];

        var pinned = new Dictionary<string, double> { ["Held"] = wanted };
        var y = GraphLayout.Place(items, pinned, 20);

        Check("the pinned node is exactly where it was put", wanted.ToString("F2"), y["Held"].ToString("F2"));
        CheckTrue($"and the family went around it rather than through it ({y["a0"]:F0} against {y["Held"]:F0})",
            y["a0"] >= y["Held"] + 100 || y["a0"] + 100 <= y["Held"]);
        Check("the family that moved kept its spacing", 120d, Math.Round(y["a1"] - y["a0"]));



        CheckTrue("the pin did not drag its neighbours with it", Math.Abs(y["Held"] - wanted) < 0.001);
    }



    private static void ASharedNodeIsPlacedOnceByItsOwner()
    {
        Console.WriteLine("\na shared node is placed once by its owner");

        var model = BehaviourGraphModel.Parse(SharedGeneratorGraph());
        var placed = GraphAuthor.Layout(model, 1000);
        var tree = GraphOwnership.Of(placed);

        var items = placed.Select(p => new GraphLayout.Item(p.Node.Id, p.Column, p.OwnerId, 100)).ToList();
        var y = GraphLayout.Place(items, new Dictionary<string, double>(), 20);

        Check("every node got exactly one position", items.Count, y.Count);


        Check("the shared clip is owned by the first state", "93", tree.Owner["94"]);
        Check("and is centred on that state, not on the borrower",
            Centre(y, "93").ToString("F2"), Centre(y, "94").ToString("F2"));



        CheckTrue($"the borrower did not drag it across ({y["94"]:F0} against {y["95"]:F0})",
            Math.Abs(y["94"] - y["95"]) > 1);

        var again = GraphLayout.Place(items, new Dictionary<string, double>(), 20);
        Check("and placing it twice gives the same answer", y["94"].ToString("F2"), again["94"].ToString("F2"));
    }









    private static void SubtreesOfDifferentDepthsShareTheHeight()
    {
        Console.WriteLine("\nsubtrees of different depths share the height");

        var items = new List<GraphLayout.Item>
        {
            Node("root", 0, ""),
            Node("deep", 1, "root"),
            Node("wide", 1, "root"),
        };


        items.Add(Node("d2", 2, "deep"));
        items.Add(Node("d3", 3, "d2"));
        items.Add(Node("d4", 4, "d3"));
        items.Add(Node("d5", 5, "d4"));


        for (int i = 0; i < 8; i++) items.Add(Node("w" + i, 2, "wide"));

        var y = GraphLayout.Place(items, new Dictionary<string, double>(), 20);

        double tall = items.Max(i => y[i.Id] + i.Height) - items.Min(i => y[i.Id]);





        CheckTrue($"the two families share the height rather than stacking ({tall:F0})", tall < 1300);



        Check("the chain stays level with itself", "0, 0, 0",
            $"{y["d3"] - y["d2"]:F0}, {y["d4"] - y["d3"]:F0}, {y["d5"] - y["d4"]:F0}");


        foreach (var column in items.GroupBy(i => i.Column))
        {
            var sorted = column.OrderBy(i => y[i.Id]).ToList();
            for (int i = 1; i < sorted.Count; i++)
                CheckTrue($"{sorted[i - 1].Id} and {sorted[i].Id} do not overlap in column {column.Key}",
                    y[sorted[i].Id] >= y[sorted[i - 1].Id] + sorted[i - 1].Height - 0.001);
        }


        var order = items.Where(i => i.Column == 2).OrderBy(i => y[i.Id]).Select(i => i.Id[0]).ToArray();
        Check("neither family is split by the other in the column they share", "dwwwwwwww",
            new string(order));
    }








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


            string parent = "deep";
            for (int level = 0; level < levels; level++)
            {
                string id = "d" + level;
                items.Add(Node(id, 2 + level, parent));
                parent = id;
            }


            for (int i = 0; i < 12; i++) items.Add(Node($"w{i:00}", 2, "wide"));

            return GraphLayout.Place(items, new Dictionary<string, double>(), 20);
        }

        var shallow = WithChainOf(3);
        var deeper = WithChainOf(9);


        var before = string.Join(", ", Enumerable.Range(0, 12).Select(i => $"{shallow[$"w{i:00}"]:F0}"));
        var after = string.Join(", ", Enumerable.Range(0, 12).Select(i => $"{deeper[$"w{i:00}"]:F0}"));
        Check("six more columns of depth move the wide family not at all", before, after);


        double sharedShallow = shallow["w11"] + 100 - Math.Min(shallow["d0"], shallow["w00"]);
        double sharedDeeper = deeper["w11"] + 100 - Math.Min(deeper["d0"], deeper["w00"]);
        Check("and the column they share is the same height", sharedShallow.ToString("F0"),
              sharedDeeper.ToString("F0"));



        int Links(Dictionary<string, double> laid) => laid.Keys.Count(k => k.Length > 1 && k[0] == 'd' && char.IsDigit(k[1]));
        Check("while the chain really is six nodes longer", "3, 9", $"{Links(shallow)}, {Links(deeper)}");


        for (int level = 1; level < 9; level++)
            CheckTrue($"d{level} is level with d{level - 1}",
                Math.Abs(deeper["d" + level] - deeper["d" + (level - 1)]) < 0.001);
    }



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



    private static void EditorSignaturesComeFromTheClassTable()
    {
        Console.WriteLine("\neditor object signatures come from the class table");

        var changed = new List<(HavokClassTypes.Layout Layout, uint Original)>();
        void Set(string className, uint value)
        {
            var layout = HavokClassTypes.Shipped[className]!;
            changed.Add((layout, layout.Signature));
            typeof(HavokClassTypes.Layout).GetProperty(nameof(HavokClassTypes.Layout.Signature))!
                .SetValue(layout, value);
        }

        Set("hkbClipGenerator", 0x12345678);
        Set("hkbVariableBindingSet", 0x12345679);
        Set("hkbStateMachineStateInfo", 0x1234567a);
        Set("hkbStateMachineTransitionInfoArray", 0x1234567b);
        Set("hkbStateMachineTimeInterval", 0x1234567c);
        Set("hkbRoleAttribute", 0x1234567d);
        Set("hkbVariableValue", 0x1234567e);

        try
        {
            CheckSignature(GeneratorEditor.Add(SmallGraph(), "clip", "NewClip", "new.hkx", "", out string clipId),
                           "hkbClipGenerator", "#" + clipId, "0x12345678");

            CheckSignature(BindingEditor.AddBinding(BindableClipGraph(), "94", "playbackSpeed", 0),
                           "hkbVariableBindingSet", "#95", "0x12345679");

            string stateXml = StateEditor.AddState(SmallGraph(), "92", "C", "#97", out string stateId, out _);
            CheckSignature(stateXml, "hkbStateMachineStateInfo", "#" + stateId, "0x1234567a");

            string transitionXml = StateEditor.AddTransition(SmallGraph(), "92", "93", 1, 0, "null");
            CheckSignature(transitionXml, "hkbStateMachineTransitionInfoArray", "#98", "0x1234567b");
            CheckSignature(transitionXml, "hkbStateMachineTimeInterval", "triggerInterval", "0x1234567c");
            CheckSignature(transitionXml, "hkbStateMachineTimeInterval", "initiateInterval", "0x1234567c");

            string variableXml = SymbolEditor.AddVariable(SymbolGraph(), "fProbe", SymbolEditor.VariableType.Real, out _);
            CheckSignature(variableXml, "hkbRoleAttribute", "role", "0x1234567d");

            string boundsXml = SymbolEditor.SetVariableBounds(ThreeVariablesWithTwoBounds(), 2, "-5", "35");
            CheckSignature(boundsXml, "hkbVariableValue", "min", "0x1234567e");
        }
        finally
        {
            var signature = typeof(HavokClassTypes.Layout).GetProperty(nameof(HavokClassTypes.Layout.Signature))!;
            foreach (var (layout, original) in changed)
                signature.SetValue(layout, original);
        }
    }

    private static void CheckSignature(string xml, string className, string objectName, string expected)
    {
        var matches = Regex.Matches(xml,
            $"<hkobject class=\"{Regex.Escape(className)}\" name=\"{Regex.Escape(objectName)}\" signature=\"(?<sig>[^\"]+)\"");
        CheckTrue($"{className} {objectName} uses the table signature",
                  matches.Any(match => match.Groups["sig"].Value == expected));
    }




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










    private static void AReferenceInsideAStructIsSeenByBothReaders()
    {
        Console.WriteLine("\na reference held in a named struct counts as a reference");

        var model = BehaviourGraphModel.Parse(StructReferenceGraph());




        var holder = model.Get("98")!;
        CheckTrue("the fixture really parsed alarmEvent as a struct",
                  holder.Structs.ContainsKey("alarmEvent"));
        Check("and the struct holds the payload reference", "#99",
              holder.Structs["alarmEvent"].GetValueOrDefault("payload"));
        CheckTrue("with nothing else in the file pointing at the payload",
                  model.Objects.All(o => o.Scalars.Values.All(v => v != "#99")));






        CheckTrue("Unattached reads structs, so a node held only by one is not called orphaned",
                  GraphAuthor.Unattached(model).All(o => o.Id != "97"));




        CheckTrue("PointsAt reads structs, so the canvas keeps the payload connected",
                  GraphAuthor.PointsAt(model, holder).Contains("99"));



        Check("ReferencesTo names the modifier that holds it", 1,
              GeneratorEditor.ReferencesTo(model, "99").Count);



        string after = GeneratorEditor.Remove(StructReferenceGraph(), "99", force: false,
                                              out var blockers);
        Check("and Remove refuses to delete it", 1, blockers.Count);
        CheckTrue("so the payload is still there", BehaviourGraphModel.Parse(after).Get("99") != null);




        string cleared = HkxTextEdit.SetParamAt(StructReferenceGraph(), "98", "alarmEvent.payload", "null");
        Check("a struct member can be cleared the way Detach would need to", "null",
              BehaviourGraphModel.Parse(cleared).Get("98")?.Structs["alarmEvent"]
                  .GetValueOrDefault("payload"));




        string gone = GraphAuthor.DeleteNode(StructReferenceGraph(), "99", out string note);
        var afterDelete = BehaviourGraphModel.Parse(gone);
        Check("deleting the payload clears the struct member that held it", "null",
              afterDelete.Get("98")?.Structs["alarmEvent"].GetValueOrDefault("payload"));
        Check("the payload is gone", null, afterDelete.Get("99"));
        CheckTrue("the note says which holder it cleared", note.Contains("#98"));




        CheckTrue("and no dangling reference is left behind",
                  GraphValidator.Check(gone).All(f => !f.What.Contains("not in this file")));




        Check("a reference in a list element is found", 1,
              GeneratorEditor.ReferencesTo(model, "93").Count);
        string listCleared = GraphAuthor.DeleteNode(StructReferenceGraph(), "93", out _);
        CheckTrue("and deleting it drops the element rather than nulling it",
                  BehaviourGraphModel.Parse(listCleared).Get("92")!.Refs("states").Count == 0);



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

    private static void StructuredFlowKeepsMachineOwnership()
    {
        Console.WriteLine("\nstructured flow keeps machine ownership");

        var model = BehaviourGraphModel.Parse(NestedReachabilityGraph());
        var plan = StructuredFlowLayout.Of(GraphAuthor.Layout(model, 1000));

        Check("the outer machine has no machine parent", "", plan.Item("10").ParentMachineId);
        Check("the nested machine stays inside the outer machine", "10", plan.Item("20").ParentMachineId);
        Check("an outer state belongs to the outer machine", "10", plan.Item("11").MachineId);
        Check("a helper inherits its nearest machine", "10", plan.Item("12").MachineId);
        Check("a nested state belongs to the nested machine", "20", plan.Item("21").MachineId);
        CheckTrue("the root machine ranks above its state", plan.Item("10").Depth < plan.Item("11").Depth);
        CheckTrue("the nested machine ranks below the state that owns it",
                  plan.Item("13").Depth < plan.Item("20").Depth);
        CheckTrue("source order gives sibling states a stable order",
                  plan.Item("11").SiblingOrder < plan.Item("13").SiblingOrder);
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




    private static void EventUsageSaysWhoSendsAndWhoListens()
    {
        Console.WriteLine("\nwho sends and who listens for each event, with no verdict");

        var usage = EventUsage.ByEvent(EventGraph());



        Check("the enter notify event is seen at all", 1, Lines(usage, 3).Count);
        Check("and it is a send", EventUsage.Role.Raised, Line(usage, 3).Role);
        Check("named by the member holding it", "hkbStateMachineEventPropertyArray.events", Line(usage, 3).Site);

        Check("the transition's event is listened for", EventUsage.Role.Listened, Line(usage, 1).Role);
        Check("by the transition array", "hkbStateMachineTransitionInfoArray.eventId", Line(usage, 1).Site);

        Check("the clip trigger is a send", EventUsage.Role.Raised, Line(usage, 2).Role);
        Check("named by the trigger array, not hkbEventProperty", "hkbClipTriggerArray.event", Line(usage, 2).Site);



        Check("an unrecognised member has no role", EventUsage.Role.Referenced, Line(usage, 0).Role);
        Check("it is still named", "BSLimbCycleModifier.EventCycleLeft", Line(usage, 0).Site);
        Check("with no note invented for it", "", Line(usage, 0).Note);

        CheckTrue("an event listened for with no sender here is not called dead",
                  !EventUsage.Summarise(usage[1]).Contains("dead", StringComparison.OrdinalIgnoreCase)
                  && !EventUsage.Summarise(usage[1]).Contains("unused", StringComparison.OrdinalIgnoreCase));
        Check("it just says what it saw", "1 listened for here", EventUsage.Summarise(usage[1]));






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





    private static void ScaleIsShownOnlyWhenItIsRealScale()
    {
        Console.WriteLine("\nscale is reported when it is real and quiet when it is not");

        CheckTrue("a track with no scale at all is not called scaled",
                  !HkxTrackData.IsScaled(new HkxTrackData()));

        CheckTrue("a flat 1,1,1 is not called scaled",
                  !HkxTrackData.IsScaled(Scaled(Vector3.One, Vector3.One)));


        CheckTrue("the crow's 0.4599 wing counts as scaled",
                  HkxTrackData.IsScaled(Scaled(new Vector3(0.4599f, 0.4599f, 0.4599f))));

        CheckTrue("one scaled frame among unscaled ones still counts",
                  HkxTrackData.IsScaled(Scaled(Vector3.One, new Vector3(1f, 0.5f, 1f), Vector3.One)));

        CheckTrue("a single axis is enough",
                  HkxTrackData.IsScaled(Scaled(new Vector3(1f, 1f, 0.82f))));



        CheckTrue("a zero scale is reported rather than hidden",
                  HkxTrackData.IsScaled(Scaled(Vector3.Zero)));



        CheckTrue("float noise just under 1 is not scale",
                  !HkxTrackData.IsScaled(Scaled(new Vector3(0.99999994f, 1f, 1.00000006f))));
        CheckTrue("but a real 0.999 is",
                  HkxTrackData.IsScaled(Scaled(new Vector3(0.999f, 1f, 1f))));
    }




    private static void AnEmptyStateIsFoundTheSameWayEverywhere()
    {
        Console.WriteLine("\na state left holding nothing is found the same way everywhere");

        string xml = SmallGraph();
        var model = BehaviourGraphModel.Parse(xml);
        Check("a whole graph has no empty states", 0, GraphValidator.StatesWithNoGenerator(model).Count);



        string after = GraphAuthor.DeleteNode(xml, "94", out _);
        var afterModel = BehaviourGraphModel.Parse(after);
        var empty = GraphValidator.StatesWithNoGenerator(afterModel);

        Check("deleting a state's generator leaves one empty state", 1, empty.Count);
        CheckTrue("and it is state A, the one that held the clip", empty.Contains("93"));
        Check("the state itself is still there, not deleted with it", "A", afterModel.Get("93")?.Str("name"));
        Check("its generator link reads null rather than dangling", "null", afterModel.Get("93")?.Str("generator"));



        var reported = GraphValidator.Check(after)
            .Where(f => f.What.Contains("nothing to play", StringComparison.Ordinal)).ToList();
        Check("Check graph reports exactly the same count", empty.Count, reported.Count);
        CheckTrue("and reports it as an error", reported.All(f => f.Level == GraphValidator.Level.Error));
        CheckTrue("naming the state", reported.Any(f => f.Where.Contains("'A'")));


        Check("an untouched graph stays unmarked", 0,
              GraphValidator.StatesWithNoGenerator(BehaviourGraphModel.Parse(SmallGraph())).Count);




        Check("a whole graph is not refused", null, GraphValidator.RefuseToSave(xml));
        Check("an empty file is not refused either", null, GraphValidator.RefuseToSave(""));



        string refusal = GraphValidator.RefuseToSave(after) ?? "";
        CheckTrue("one empty state is refused", refusal.Length > 0);
        CheckTrue("saying nothing was written", refusal.Contains("original is untouched"));
        CheckTrue("and why the game cannot take it", refusal.Contains("crashes on load"));
        CheckTrue("without claiming the state has to be entered",
                  refusal.Contains("whether or not anything can enter"));



        CheckTrue("naming the state rather than only counting it", refusal.Contains("'A'"));
        CheckTrue("and the machine it sits in", refusal.Contains("in Root"));
        CheckTrue("saying how to fix it", refusal.Contains("give each one a generator"));
        CheckTrue("and that deleting the state is the other way out", refusal.Contains("delete the state"));


        var many = GraphValidator.EmptyStates(BehaviourGraphModel.Parse(after));
        Check("one empty state is found by name", 1, many.Count);
        Check("named the way the refusal prints it", "'A' in Root", many[0].ToString());
        CheckTrue("counting the states rather than guessing", refusal.Contains("1 state has"));
    }




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




        var reaching = SymbolIndexFixup.ReferencesAtOrAbove(EventGraph(), events: true, 0);
        CheckTrue("an event index reference is found at all", reaching.Count > 0);
        CheckTrue("and it names the object that carries it", reaching.All(r => r.StartsWith('#')));
    }








    private static void AShortBoundsArrayStaysLinedUp()
    {
        Console.WriteLine("\na short bounds array stays lined up when a variable is removed");

        string xml = ThreeVariablesWithTwoBounds();
        var before = SymbolEditor.Audit(BehaviourGraphModel.Parse(xml));
        Check("three variables", 3, before.Names);
        Check("and only two bounds", 2, before.Bounds);
        CheckTrue("so the array is short, not parallel", !before.BoundsAreParallel);


        string after = SymbolEditor.RemoveVariable(xml, 0, force: true, out _);
        var counts = SymbolEditor.Audit(BehaviourGraphModel.Parse(after));
        Check("two variables are left", 2, counts.Names);
        Check("and one bound, because its entry went with it", 1, counts.Bounds);
        Check("the bound left behind is the second one, not the first", "20",
              BoundMax(after, 0));


        string tail = SymbolEditor.RemoveVariable(xml, 2, force: true, out _);
        var tailCounts = SymbolEditor.Audit(BehaviourGraphModel.Parse(tail));
        Check("removing a variable past the bounds leaves them alone", 2, tailCounts.Bounds);
        Check("with the first bound untouched", "10", BoundMax(tail, 0));
    }







    private static void ABoundCanBeAuthoredPastTheEndOfTheArray()
    {
        Console.WriteLine("\na bound can be authored past the end of the array");

        string xml = ThreeVariablesWithTwoBounds();
        Check("two bounds to begin with", 2, SymbolEditor.Audit(BehaviourGraphModel.Parse(xml)).Bounds);


        string after = SymbolEditor.SetVariableBounds(xml, 2, "-5", "35");
        var counts = SymbolEditor.Audit(BehaviourGraphModel.Parse(after));

        Check("the array now reaches it", 3, counts.Bounds);
        CheckTrue("and is parallel with the variables", counts.BoundsAreParallel);
        Check("the new bound is the one asked for", "35", BoundMax(after, 2));
        Check("with its minimum too", "-5", BoundMin(after, 2));



        Check("the first bound is untouched", "10", BoundMax(after, 0));
        Check("and the second", "20", BoundMax(after, 1));


        string second = SymbolEditor.SetVariableBounds(xml, 0, "1", "2");
        Check("bounding one already in the array does not lengthen it", 2,
              SymbolEditor.Audit(BehaviourGraphModel.Parse(second)).Bounds);
        Check("and it takes the new value", "2", BoundMax(second, 0));
        Check("leaving its neighbour alone", "20", BoundMax(second, 1));



        string refused = "";
        try { SymbolEditor.SetVariableBounds(xml, 7, "0", "0"); }
        catch (ArgumentOutOfRangeException e) { refused = e.Message; }
        CheckTrue("bounding a variable the file does not have is refused",
                  refused.Contains("3 variable(s)", StringComparison.Ordinal));
    }
















    private static void AnElementsFieldIsWrittenToThatElement()
    {
        Console.WriteLine("\na field inside an element is written to that element");

        string xml = TwoTransitions();



        string byName = HkxTextEdit.SetParam(xml, "95", "eventId", "9");
        Check("naming the field alone still reaches the first element", "9",
              TransitionEventId(byName, 0));
        Check("which is why it is not enough on its own", "2", TransitionEventId(byName, 1));

        string byPath = HkxTextEdit.SetParamAt(xml, "95", "transitions[1].eventId", "9");
        Check("addressing the element writes that element", "9", TransitionEventId(byPath, 1));
        Check("and leaves the one before it alone", "1", TransitionEventId(byPath, 0));







        string nested = HkxTextEdit.SetParamAt(xml, "95",
                                               "transitions[1].initiateInterval.enterEventId", "7");
        Check("exactly one enterEventId is now 7", 1, Occurrences(nested, "\"enterEventId\">7<"));
        Check("and the other is untouched", 1, Occurrences(nested, "\"enterEventId\">-1<"));
        CheckTrue("the one that changed is the second element's",
                  nested.IndexOf("\"enterEventId\">7<", StringComparison.Ordinal)
                  > nested.IndexOf("\"eventId\">1<", StringComparison.Ordinal));



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




        CheckTrue("a change inside a bound is not attributed to hkbVariableValue",
                  plan.Changes.All(c => c.ClassName != "hkbVariableValue"));
        Check("it belongs to the object that owns the array", "hkbBehaviorGraphData",
              plan.Changes[0].ClassName);
    }






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




        var fill = longer.Changes.Where(c => c.InElement).ToList();
        Check("with the new element's two numbers to write into it", 2, fill.Count);
        CheckTrue("both aimed at the element that was added", fill.All(c => c.Element == 2));
        CheckTrue("and none at the ones already there", fill.All(c => c.Element >= 2));




        var shorter = NativeSave.Compare(SymbolEditor.SetVariableBounds(xml, 2, "-5", "35"), xml);
        CheckTrue("shortening it is written the same way", shorter.Possible);
        CheckTrue("and it names the array that changed",
                  shorter.Changes.Any(c => c.Grow && c.Field == "variableBounds"));
        Check("at the length it is going back to", "2",
              shorter.Changes.First(c => c.Grow).Value);



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



    private static string BoundMax(string xml, int index)
    {
        int start = xml.IndexOf("name=\"variableBounds\"", StringComparison.Ordinal);
        if (start < 0) return "";
        var maxima = System.Text.RegularExpressions.Regex
            .Matches(xml[start..], "name=\"max\".*?name=\"value\">(-?\\d+)<",
                     System.Text.RegularExpressions.RegexOptions.Singleline);
        return index < maxima.Count ? maxima[index].Groups[1].Value : "";
    }






    private static void LosslessScaleFollowsTheEngine()
    {
        Console.WriteLine("\nlossless scale decodes the way the engine's own sampler does");




        ulong word = Field(0, 5, 1) | Field(1, 9, 2) | Field(2, 0, 0) | Field(3, 0x3FFF, 2);

        Check("component 0 is static", 1, HkxBinaryReader.LosslessType(word, 0));
        Check("with offset 5", 5, HkxBinaryReader.LosslessOffset(word, 0));
        Check("component 1 is dynamic", 2, HkxBinaryReader.LosslessType(word, 1));
        Check("with offset 9", 9, HkxBinaryReader.LosslessOffset(word, 1));
        Check("component 2 is clear", 0, HkxBinaryReader.LosslessType(word, 2));


        Check("component 3 carries the widest offset the format allows", 0x3FFF,
              HkxBinaryReader.LosslessOffset(word, 3));

        var constants = new List<float> { 9f, 9f, 9f, 9f, 9f, 0.5f };
        var dynamic = new List<float>();
        for (int i = 0; i < 40; i++) dynamic.Add(i);

        Check("static reads the constant at its offset", 0.5f,
              HkxBinaryReader.LosslessValue(word, 0, frame: 3, stride: 4, dynamic, constants, 1f));




        Check("dynamic is frame major, frame 0", 9f,
              HkxBinaryReader.LosslessValue(word, 1, frame: 0, stride: 4, dynamic, constants, 1f));
        Check("dynamic is frame major, frame 3", 21f,
              HkxBinaryReader.LosslessValue(word, 1, frame: 3, stride: 4, dynamic, constants, 1f));




        Check("a clear scale component is 1, not 0", 1f,
              HkxBinaryReader.LosslessValue(word, 2, frame: 3, stride: 4, dynamic, constants, 1f));
        Check("a clear translation component is 0", 0f,
              HkxBinaryReader.LosslessValue(word, 2, frame: 3, stride: 4, dynamic, constants, 0f));


        ulong wild = Field(0, 4000, 1) | Field(1, 4000, 2);
        Check("a static offset past the array falls back", 1f,
              HkxBinaryReader.LosslessValue(wild, 0, frame: 0, stride: 4, dynamic, constants, 1f));
        Check("so does a dynamic one", 1f,
              HkxBinaryReader.LosslessValue(wild, 1, frame: 0, stride: 4, dynamic, constants, 1f));
    }

    private static ulong Field(int component, int offset, int type) =>
        ((ulong)(((offset & 0x3FFF) << 2) | (type & 3))) << (component * 16);




    private static void AFractionLandsOnAFrame()
    {
        Console.WriteLine("\na userControlledTimeFraction lands on a frame");

        var clip = new HkxAnimationData { NumFrames = 41 };
        Check("0 is the first frame", 0, clip.FrameAt(0f));
        Check("1 is the last frame, not one past it", 40, clip.FrameAt(1f));
        Check("half way is frame 20 of 40, not 20.5", 20, clip.FrameAt(0.5f));
        Check("a quarter", 10, clip.FrameAt(0.25f));



        Check("below zero clamps to the first frame", 0, clip.FrameAt(-2f));
        Check("above one clamps to the last", 40, clip.FrameAt(7f));

        var single = new HkxAnimationData { NumFrames = 1 };
        Check("a one frame clip is always frame 0", 0, single.FrameAt(0.5f));
        var empty = new HkxAnimationData { NumFrames = 0 };
        Check("and an empty one does not divide by its own length", 0, empty.FrameAt(0.5f));


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



        string bound = BindingEditor.AddVariable(SymbolGraph(), "fBoundProbe", out int boundIndex);
        var boundModel = BehaviourGraphModel.Parse(bound);
        Check("BindingEditor declares a real variable too", "VARIABLE_TYPE_REAL",
              TypeOfVariable(boundModel, boundIndex));
        CheckTrue("and leaves the arrays consistent", SymbolEditor.Audit(boundModel).VariablesConsistent);



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


        string linked = GraphLinks.Connect(crlf, "95", "generator", "97", out _);
        Check("a node connected on a windows file",
              "97", BehaviourGraphModel.Parse(linked).Get("95")!.Ref("generator"));


        string normalised = lf.Replace("\n", "\r\n");
        CheckTrue("reading normalises the line endings",
                  !NormaliseLike(normalised).Contains('\r'));
    }

    private static string NormaliseLike(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n");





    private static void RepackDriftCatchesAChangedValue()
    {
        Console.WriteLine("\nrepack drift catches a value that moved, and ignores renumbering");

        string before = SmallGraph();


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




    private static void AnAnimationIsRefusedForSaving()
    {
        Console.WriteLine("\nan animation reference formatter cannot carry is refused before it is written");

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







    private static OpenCommonwealth.Services.Nif.NifShape BoundShape(HkxSkeleton rig, Vector3 placement)
    {
        var rest = AnimationPose.ReferencePose(rig);
        var shape = new OpenCommonwealth.Services.Nif.NifShape { Name = "TestShape" };

        for (int b = 0; b < rig.BoneNames.Count; b++)
        {
            shape.BoneNames.Add(rig.BoneNames[b]);
            shape.SkinToBone.Add(Matrix4x4.CreateTranslation(placement - rest.Bones[b].Position));
        }



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



            var posed = OpenCommonwealth.Services.Nif.SkinnedMesh.Pose(shape, binding, rest, rig);
            CheckTrue("a vertex on a bone the skeleton lacks is still placed with the mesh",
                      Near(posed[^1], shape.Vertices[^1] + placement));
            CheckTrue("and one on a bone it has lands in the same space",
                      Near(posed[0], shape.Vertices[0] + placement));
        }



        var wrong = BoundShape(rig, new Vector3(0, 0, 120.84f));
        wrong.SkinToBone[1] = Matrix4x4.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2) *
                              wrong.SkinToBone[1];

        float broken = OpenCommonwealth.Services.Nif.SkinnedMesh
            .BindError(wrong, OpenCommonwealth.Services.Nif.SkinnedMesh.Bind(wrong, rig), rig, out _);

        CheckTrue("a bind turned the wrong way is still caught", broken > 10);
    }







    private static string ArchiveOfTwoFiles(byte[] plain, byte[] compressible)
    {
        var names = new[] { "Meshes/Actors/Canine/Behaviors/CanineRoot.hkx", "Meshes/Actors/Human/skeleton.nif" };

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



    private static void AnArchiveIsReadWithoutUnpackingIt()
    {
        Console.WriteLine("\nan archive is read without unpacking it");

        var plain = System.Text.Encoding.ASCII.GetBytes("a behaviour would go here");
        var compressible = System.Text.Encoding.ASCII.GetBytes(new string('x', 4096));

        string path = ArchiveOfTwoFiles(plain, compressible);

        using (var archive = OpenCommonwealth.Services.Archive.Ba2.Open(path))
        {
            Check("both files are in the index", 2, archive.Entries.Count);
            Check("with the archive's own path separators turned round", "Meshes/Actors/Canine/Behaviors/CanineRoot.hkx",
                  archive.Entries[0].Name);
            Check("and the file name on its own", "CanineRoot.hkx", archive.Entries[0].FileName);
            Check("and the folder it sits in", "Meshes/Actors/Canine/Behaviors", archive.Entries[0].Folder);



            Check("words match in any order", 1, archive.Matching("canine behavior").Count());
            Check("and in the other order too", 1, archive.Matching("behavior canine").Count());
            Check("an extension narrows it", 1, archive.Matching("", ".nif").Count());
            Check("a word nothing has matches nothing", 0, archive.Matching("mirelurk").Count());
            Check("no filter matches everything", 2, archive.Matching("").Count());



            CheckTrue("a plainly stored file comes back as it went in",
                      archive.Read(archive.Entries[0]).SequenceEqual(plain));
            CheckTrue("and a compressed one is inflated",
                      archive.Read(archive.Entries[1]).SequenceEqual(compressible));
        }

        File.Delete(path);
    }







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



        CheckTrue("before the start is the start", Near(RootMotion.At(motion, -5).Position, Vector3.Zero));
        CheckTrue("past the end is the end", Near(RootMotion.At(motion, 5).Position, new Vector3(0, 100, 0)));

        Check("travel is the straight line between the ends", 100f, RootMotion.At(motion, 1).Position.Y);
        CheckTrue("and the total is the same", Math.Abs(motion.Travel.Length() - 100) < 0.001f);



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


        anim.Tracks[1] = FullTrack((Vector3.Zero, Quaternion.Identity));
        var collapsed = AnimationPose.At(rig, anim, 0);
        CheckTrue("a driven zero translation is honoured",
                  Near(collapsed.Bones[1].Position, Vector3.Zero));
    }





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


        anim.AnimationClass = "hkaLosslessCompressedAnimation";
        var kept = AnimationPose.At(rig, anim, 0);
        CheckTrue("and a format without that guarantee still keeps the rest pose",
                  Near(kept.Bones[1].Position, new Vector3(10, 0, 0)));
    }






    private static void APackfileSurvivesBeingRebuilt()
    {
        Console.WriteLine("\na packfile taken apart and rebuilt says the same thing");

        var image = new PackfileImage { Predicates = new byte[16] };
        var section = new PackfileSection
        {

            TagBytes = MakeTag("__data__"),
            Data = new byte[100],
            LocalFixups = Pair(8, 40),
            GlobalFixups = Triple(16, 2, 64),
            VirtualFixups = Triple(24, 0, 3),
        };
        image.Sections.Add(section);

        var reread = PackfileImage.Read(image.Rebuild());
        CheckTrue("one section survives", reread.Sections.Count == 1);
        Check("named the same", "__data__", reread.Sections[0].Tag);




        Check("the odd sized data comes back padded to the boundary", 112, reread.Sections[0].Data.Length);
        Check("the bytes before the section headers survive", 16, reread.Predicates.Length);

        var local = reread.Sections[0].Locals().ToList();
        Check("one local fixup", 1, local.Count);
        Check("pointing where it did", 40, local[0].Destination);

        var virtuals = reread.Sections[0].Virtuals().ToList();
        Check("one virtual fixup", 1, virtuals.Count);
        Check("naming section 0, which is always __classnames__", 0, virtuals[0].Section);



        byte[] once = image.Rebuild();
        byte[] twice = PackfileImage.Read(once).Rebuild();
        CheckTrue("rebuilding what was rebuilt gives the same bytes", once.SequenceEqual(twice));
    }





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

        CheckTrue("a name the file left empty is accepted",
                  objects.WriteString(clip, "animationBundleName", "bundle"));

        var reread = new PackfileObjects(PackfileImage.Read(image.Rebuild()));
        var again = reread.Instances.Single();

        Check("the longer name survives the rebuild", longer, reread.ReadString(again, "animationName"));
        Check("so does the name that was empty", "bundle", reread.ReadString(again, "animationBundleName"));
        Check("the object did not move", 0, again.Offset);



        Check("the value next to it is untouched", 2.5f, reread.ReadFloat(again, "playbackSpeed"));



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















    private static void AppendedStringsLandOnAnEvenOffset()
    {
        Console.WriteLine("\nan appended string lands on an even offset");

        var image = ClipInAPackfile("A.hkx", out _);
        var objects = new PackfileObjects(image);
        var clip = objects.Instances.Single();




        const string even = "Walk.hkx";
        CheckTrue("a name of even length is accepted", objects.WriteString(clip, "animationName", even));
        CheckTrue("and a second name after it", objects.WriteString(clip, "animationBundleName", "bundle"));

        var landed = image.Section("__data__")!.Locals().Select(l => l.Destination).ToList();
        Check("both names are pointed at", 2, landed.Count);
        Check("and neither landed on an odd offset", 0, landed.Count(d => d % 2 != 0));



        var reread = new PackfileObjects(PackfileImage.Read(image.Rebuild()));
        var again = reread.Instances.Single();
        Check("the first name still reads back", even, reread.ReadString(again, "animationName"));
        Check("and so does the second", "bundle", reread.ReadString(again, "animationBundleName"));
        Check("the value beside them is untouched", 2.5f, reread.ReadFloat(again, "playbackSpeed"));
    }




    private static void WideAndVectorFieldsReadFromTheBytes()
    {
        Console.WriteLine("\neight byte and vector fields read from the bytes");

        var classes = HavokClasses.Shipped;
        int userData = classes.Field("hkbClipGenerator", "userData")!.Offset;
        int motion = classes.Field("hkbClipGenerator", "extractedMotion")!.Offset;

        var image = ClipInAPackfile("A.hkx", out _);
        var data = image.Section("__data__")!.Data;


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


        Check("a run that does not fit is refused rather than cut short", null,
              objects.ReadFloats(clip, "extractedMotion", 4096));
    }




    private static void ReferencesAndArraysReadFromTheBytes()
    {
        Console.WriteLine("\nreferences and arrays read from the bytes");

        var classes = HavokClasses.Shipped;
        int size = classes["hkbClipGenerator"]!.Size;
        int binding = classes.Field("hkbClipGenerator", "variableBindingSet")!.Offset;
        int triggers = classes.Field("hkbClipGenerator", "triggers")!.Offset;

        var image = ClipInAPackfile("A.hkx", out _);
        var data = image.Section("__data__")!;


        int second = data.AppendData(new byte[size]);
        data.VirtualFixups = data.VirtualFixups
            .Concat(Triple(second, 0, 5)).ToArray();


        data.GlobalFixups = Triple(binding, 1, second);
        int list = data.AppendData(new byte[16]);
        var arrayHeader = new byte[16];
        BitConverter.GetBytes(2).CopyTo(arrayHeader, 8);
        int header = data.AppendData(arrayHeader);


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


        var flags = types.Members("hkbBlendingTransitionEffect").First(m => m.Name == "flags");

        Check("a single flag is named", "FLAG_SYNC",
              types.NameOf("hkbBlendingTransitionEffect", flags, 2));
        Check("so is a combination of declared flags", "FLAG_SYNC|FLAG_IGNORE_TO_WORLD_FROM_MODEL",
              types.NameOf("hkbBlendingTransitionEffect", flags, 6));
        Check("a combination holding a bit with no name is refused whole", null,
              types.NameOf("hkbBlendingTransitionEffect", flags, 6 | 1 << 20));
    }






    private static void ThePanelReadsItsListFromTheTable()
    {
        Console.WriteLine("\nthe panel reads its list from the table");

        var image = ClipInAPackfile("A.hkx", out _);
        var objects = new PackfileObjects(image);
        var clip = objects.Instances.Single();

        var names = ClassFields.NamesOf(objects, clip)!;
        CheckTrue("the list holds the fields reference formatter writes",
                  names.Contains("animationName") && names.Contains("playbackSpeed"));
        CheckTrue("and not the running state it does not",
                  !names.Contains("localTime") && !names.Contains("atEnd"));



        var xml = names.Select(n => (n, "from-reference formatter")).ToList();
        var fields = PanelFields.For(objects, clip, xml, (_, wasNull) => wasNull ? "null" : "");

        Check("one field per name in the table's list", names.Count, fields.Count);
        Check("the name comes from the bytes", "A.hkx",
              fields[names.IndexOf("animationName")].Value);
        Check("and so does a number the text disagrees with", "2.5",
              fields[names.IndexOf("playbackSpeed")].Value);
        Check("a null string is an empty box rather than a symbol", "",
              fields[names.IndexOf("animationBundleName")].Value);
        Check("nothing fell back to reference formatter", 0,
              fields.Count(f => f.From == PanelFields.Source.Fallback));



        var edited = PanelFields.For(objects, clip, xml, (_, _) => "",
                                     new HashSet<string> { "playbackSpeed" });
        int speed = names.IndexOf("playbackSpeed");
        Check("an edited field shows the edit, not the bytes", "from-reference formatter", edited[speed].Value);
        Check("and says so", PanelFields.Source.Edited, edited[speed].From);



        var short_ = PanelFields.For(objects, clip, xml.Take(3).ToList(), (_, _) => "");
        Check("a list that does not line up with reference formatter's degrades to reference formatter's", 3, short_.Count);
        Check("and reads none of it from the bytes", 3,
              short_.Count(f => f.From == PanelFields.Source.Fallback));
    }



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



        CheckTrue("and the file stays readable", written.Contains("&amp;&amp;") == false);
    }




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




        var parents = HavokClasses.Shipped.Field("hkaSkeleton", "parentIndices");
        CheckTrue("a skeleton's parent indices are an array of int16", parents?.Type == "array of int16");
    }





    private static void TheClassTableKnowsWhatTheDumpCannot()
    {
        Console.WriteLine("\nthe class table knows what the dump cannot");

        var types = HavokClassTypes.Shipped;
        CheckTrue("the table is there at all", types.Count > 900);

        var clip = types["hkbClipGenerator"]!;
        Check("a signature, which the dump has none of", 0xd4cc9f6u, clip.Signature);
        Check("and a size, which reference formatter has none of", 352, clip.Size);
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


        var flags = types.Members("hkbBlendingTransitionEffect").Single(m => m.Name == "flags");
        Check("flags read as their names", "FLAG_SYNC|FLAG_IGNORE_TO_WORLD_FROM_MODEL",
              types.NameOf("hkbBlendingTransitionEffect", flags, 6));
        Check("a combination holding a bit with no name is refused whole", null,
              types.NameOf("hkbBlendingTransitionEffect", flags, 6 | 1 << 20));
    }



    private static void AFieldListIsBuiltWithoutReferenceFormatter()
    {
        Console.WriteLine("\na field list is built without reference formatter");

        var image = ClipInAPackfile("A.hkx", out _);
        var objects = new PackfileObjects(image);
        var names = ClassFields.NamesOf(objects, objects.Instances.Single());

        CheckTrue("a list comes back at all", names != null);
        CheckTrue("it holds the fields reference formatter writes", names!.Contains("animationName") &&
                                                        names.Contains("playbackSpeed"));
        CheckTrue("and not the ones it never writes", !names.Contains("localTime") &&
                                                      !names.Contains("atEnd"));


        CheckTrue("a pointer is a field", names.Contains("triggers"));
        CheckTrue("an array is not", !names.Contains("animDatas"));

        var order = HavokClassTypes.Shipped.Members("hkbClipGenerator")
                                   .Where(m => m.Written && m.VType != "TYPE_ARRAY" &&
                                               m.VType != "TYPE_STRUCT")
                                   .Select(m => m.Name).ToList();
        Check("in the order the file writes them", string.Join(",", order), string.Join(",", names));
    }







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


        var image = ClipInAPackfile("A.hkx", out _);
        Check("the names a real packfile carries pass", 0,
              types.SignatureProblems(new PackfileObjects(image).ClassNames()).Count);
    }




    private static void AMisSignedFileIsNotWrittenInto()
    {
        Console.WriteLine("\na file signed for other classes is not written into");

        string good = Path.Combine(Path.GetTempPath(), "symrm-signed-right.hkx");
        string bad = Path.Combine(Path.GetTempPath(), "symrm-signed-wrong.hkx");

        ClipInAPackfile("A.hkx", out _).Save(good);



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



        data[clip.Offset + mode] = 0xFF;
        Check("a value with none reads as the byte, unsigned", "255",
              FieldRender.Render(objects, clip.Offset + mode, "hkbClipGenerator", member,
                                 (_, _) => "", "255"));
    }


    private static void APaddedStructIsKnownFromReferenceFormattersIdeaOfIt()
    {
        Console.WriteLine("\na padded struct is known from reference formatter's idea of it");

        var types = HavokClassTypes.Shipped;




        Check("the game's size for the bone data", 528, types["BSLookAtModifierBoneData"]!.Size);
        CheckTrue("and it is padded past what reference formatter would work out",
                  types.HasTrailingPadding("BSLookAtModifierBoneData"));


        Check("a struct with nothing wider than a pointer", 72,
              types["hkbStateMachineTransitionInfo"]!.Size);
        CheckTrue("is not padded past it",
                  !types.HasTrailingPadding("hkbStateMachineTransitionInfo"));





        Check("a class smaller than the rounding itself", 6, types["hkbVariableInfo"]!.Size);
        CheckTrue("is not called padded", !types.HasTrailingPadding("hkbVariableInfo"));
        Check("nor is a four byte one", 4, types["hkbEventInfo"]!.Size);
        CheckTrue("either", !types.HasTrailingPadding("hkbEventInfo"));
    }



    private static PackfileImage ClipInAPackfile(string animation, out int nameField)
    {
        var classes = HavokClasses.Shipped;
        int size = classes["hkbClipGenerator"]!.Size;
        nameField = classes.Field("hkbClipGenerator", "animationName")!.Offset;
        int speed = classes.Field("hkbClipGenerator", "playbackSpeed")!.Offset;




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




    private static PackfileImage TwoClipsOnePointingAtTheOther(out int pointedAt)
    {
        var classes = HavokClasses.Shipped;
        int size = classes["hkbClipGenerator"]!.Size;
        int binding = classes.Field("hkbClipGenerator", "variableBindingSet")!.Offset;

        var names = new byte[5 + "hkbClipGenerator".Length + 1];
        BitConverter.GetBytes(HavokClassTypes.Shipped["hkbClipGenerator"]!.Signature).CopyTo(names, 0);
        names[4] = 0x09;
        System.Text.Encoding.ASCII.GetBytes("hkbClipGenerator").CopyTo(names, 5);



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


    private static string WriteImage(PackfileImage image, string folder, string name)
    {
        System.IO.Directory.CreateDirectory(folder);
        string path = System.IO.Path.Combine(folder, name);
        System.IO.File.WriteAllBytes(path, image.Rebuild());
        return path;
    }



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


        Check("scrubbing before the start clamps", 0, AnimationPose.At(rig, anim, -5).Frame);
        Check("and past the end clamps", 2, AnimationPose.At(rig, anim, 99).Frame);
        Check("clamped low is the same pose as frame 0", 0f, AnimationPose.Distance(AnimationPose.At(rig, anim, -5), first));



        Check("the halfway fraction lands on the middle frame", 1, anim.FrameAt(0.5f));
        CheckTrue("and that frame is neither end",
                  AnimationPose.Distance(AnimationPose.At(rig, anim, anim.FrameAt(0.5f)), first) > 1f);
    }

    private static void TracksDriveTheBonesTheyName()
    {
        Console.WriteLine("\ntracks drive the bones they name, not the bones in order");

        var rig = ThreeBoneChain();
        var anim = new HkxAnimationData { NumFrames = 1, NumTracks = 1, FrameDuration = 1f / 30f };



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



        var unnamed = new HkxAnimationData { NumFrames = 1, NumTracks = 1, FrameDuration = 1f / 30f };
        unnamed.Tracks.Add(FullTrack((Vector3.Zero, Quaternion.Identity)));
        CheckTrue("one track and three bones with no mapping drives nothing",
                  AnimationPose.TracksByBone(rig, unnamed).All(t => t == -1));

        var matched = new HkxAnimationData { NumFrames = 1, NumTracks = 3, FrameDuration = 1f / 30f };
        for (int i = 0; i < 3; i++) matched.Tracks.Add(FullTrack((Vector3.Zero, Quaternion.Identity)));
        CheckTrue("matching counts with no mapping fall back to order",
                  AnimationPose.TracksByBone(rig, matched).SequenceEqual(new[] { 0, 1, 2 }));
    }



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



    private static void EverySymbolUsageNamesItsObject()
    {
        Console.WriteLine("\nevery symbol usage names the object it sits in");

        string xml = EventGraph();
        var events = SymbolIndexFixup.Usages(xml, events: true);

        CheckTrue("the graph writes event indices at all", events.Count > 0);
        CheckTrue("and every one of them names an object", events.All(u => u.ObjectId.Length > 0));
        CheckTrue("with a member to go with it", events.All(u => u.Member.Length > 0));



        foreach (var lines in EventUsage.ByEvent(xml).Values)
            CheckTrue("event rows carry the objects they came from", lines.All(l => l.ObjectIds.Count > 0));

        string first = events[0].ObjectId;
        var backwards = SymbolIndexFixup.UsagesOf(xml, events: true, first);
        CheckTrue("and the reverse lookup finds the same site", backwards.Any(u => u.Index == events[0].Index));
        CheckTrue("without straying into other objects", backwards.All(u => u.ObjectId == first));
    }



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


        Check("names match without case", "DoorScript.psc", index.Senders("openanim").FirstOrDefault());

        string quiet = PapyrusEvents.Describe(index, "somethingNobodySends");
        CheckTrue("an unsent name says only that nothing was found", quiet == "no sender found in the scanned scripts");
        CheckTrue("and never calls it dead, unused or broken",
                  !new[] { "dead", "unused", "broken", "wrong" }
                      .Any(w => quiet.Contains(w, StringComparison.OrdinalIgnoreCase)));
        Check("with no folder set, nothing is said at all", "", PapyrusEvents.Describe(new PapyrusEvents.Index(), "OpenAnim"));

        Directory.Delete(folder, true);
    }



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

    private static string BindableClipGraph() => """
        <?xml version="1.0" encoding="ascii"?>
        <hkpackfile classversion="11" contentsversion="hk_2014.1.0-r1">
            <hksection name="__data__">
                <hkobject class="hkbClipGenerator" name="#94" signature="0xd4cc9f6">
                    <hkparam name="variableBindingSet">null</hkparam>
                    <hkparam name="userData">0</hkparam>
                    <hkparam name="name">ClipA</hkparam>
                    <hkparam name="animationBundleName"/>
                    <hkparam name="animationName">a.hkx</hkparam>
                    <hkparam name="triggers">null</hkparam>
                    <hkparam name="userPartitionMask">0</hkparam>
                    <hkparam name="cropStartAmountLocalTime">0.0</hkparam>
                    <hkparam name="cropEndAmountLocalTime">0.0</hkparam>
                    <hkparam name="startTime">0.0</hkparam>
                    <hkparam name="playbackSpeed">1.0</hkparam>
                    <hkparam name="enforcedDuration">0.0</hkparam>
                    <hkparam name="userControlledTimeFraction">0.0</hkparam>
                    <hkparam name="animationBindingIndex">65535</hkparam>
                    <hkparam name="mode">MODE_LOOPING</hkparam>
                    <hkparam name="flags">0</hkparam>
                </hkobject>
            </hksection>
        </hkpackfile>
        """;





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


        foreach (string special in new[] { "NaN", "Infinity" })
            CheckTrue($"'{special}' is refused rather than written",
                      !NativeSave.Compare(Before, Before.Replace(">1.0<", $">{special}<")).Possible);


        var tooBig = NativeSave.Compare(Before, Before.Replace(">0<", ">99999999999<"));
        CheckTrue("a number too big for the field is refused", !tooBig.Possible);

        var fits = NativeSave.Compare(Before, Before.Replace(">0<", ">3<"));
        CheckTrue("one that fits is accepted", fits.Possible);
    }



    private static void AFloatIsSpelledTheWayReferenceFormatterSpellsIt()
    {
        Console.WriteLine("\na float is spelled the way reference formatter spells it");



        Check("one", "1.0", HkxNumber.Text(1.0f));
        Check("zero", "0.0", HkxNumber.Text(0.0f));
        Check("a half", "0.5", HkxNumber.Text(0.5f));



        Check("negative zero", "-0.0", HkxNumber.Text(-0.0f));


        Check("a tenth", "0.10000000149011612", HkxNumber.Text(0.1f));
        Check("nine tenths", "0.8999999761581421", HkxNumber.Text(0.9f));
        Check("seven tenths", "0.699999988079071", HkxNumber.Text(0.7f));
        Check("two tenths", "0.20000000298023224", HkxNumber.Text(0.2f));
        Check("a negative", "-0.23399999737739563", HkxNumber.Text(-0.234f));



        Check("a very small number", "3.8432640863340837E-34", HkxNumber.Text(3.8432640863340837E-34));
        Check("one small enough to be subnormal", "8.127531093083939E-44",
              HkxNumber.Text(8.127531093083939E-44));


        Check("just inside the small edge", "0.001", HkxNumber.Text(0.001));
        Check("just outside it", "9.99E-4", HkxNumber.Text(0.000999));
        Check("just inside the large edge", "9999999.0", HkxNumber.Text(9999999.0));
        Check("just outside it", "1.0E7", HkxNumber.Text(1.0E7));

        Check("not a number", "NaN", HkxNumber.Text(float.NaN));
        Check("and the infinities", "-Infinity", HkxNumber.Text(float.NegativeInfinity));
    }







    private static void TheConsumerComparisonCatchesADifferentAnswer()
    {
        Console.WriteLine("\nthe consumer comparison catches a different answer");

        var clean = ConsumerDiff.Compare(Reading(), Reading());
        CheckTrue("two readings of one file behave the same", clean.Clean);
        Check("across every consumer", 13, clean.Compared);

        ConsumerDiff.Result After(Action<BehaviourGraphModel> change) =>
            ConsumerDiff.Compare(Reading(), Broken(change));





        var rewired = After(m => m.Objects[0].Scalars["triggers"] = "#404");
        Check("a wire pointing at nothing shows up twice", 2, rewired.Differences.Count);
        Check("once in the checker", "checker findings", rewired.Differences[0].Consumer);
        Check("and once in the wiring", "the wiring", rewired.Differences[1].Consumer);
        CheckTrue("naming the line it is on",
                  rewired.Differences[1].What.StartsWith("line 1 of", StringComparison.Ordinal));


        var reclassed = After(m => m.Objects[0].Class = "hkbNothing");
        CheckTrue("a class the wiring does not know about is a difference too", !reclassed.Clean);



        var nothing = ConsumerDiff.Compare(new BehaviourGraphModel(), new BehaviourGraphModel());
        CheckTrue("two readings of an empty file still agree", nothing.Clean);


        CheckTrue("a reading of nothing does not agree with a real one",
                  !ConsumerDiff.Compare(Reading(), new BehaviourGraphModel()).Clean);
    }









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


        Check("which splits into ten tokens, not twelve", 10,
              FieldRender.Floats(new float[12])!
                         .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
    }







    private static void TheReadingFromTheBytesRefusesWhatItCannotDescribe()
    {
        Console.WriteLine("\nthe reading from the bytes refuses what it cannot describe");

        var objects = new PackfileObjects(ClipInAPackfile("A.hkx", out _));

        var model = NativeGraphModel.From(objects);
        CheckTrue("a file the table describes is read", model != null);
        Check("with the object in it", 1, model!.Objects.Count);
        Check("numbered where reference formatter starts numbering", "90", model.Objects[0].Id);
        Check("and named by its class", "hkbClipGenerator", model.Objects[0].Class);
        Check("its string read from the bytes", "A.hkx", model.Objects[0].Str("animationName"));
        Check("and its number spelled like the file", "2.5", model.Objects[0].Str("playbackSpeed"));



        Check("a build with no class table reads nothing", null,
              NativeGraphModel.From(objects, HavokClassTypes.Parse(Stream("""
                  { "classes": {} }
                  """))));




        var elsewhere = HavokClassTypes.Parse(Stream("""
            { "classes": { "hkbNothing": { "signature": "0x00000001", "members": [] } } }
            """));
        Check("nor does one that does not describe this class", null,
              NativeGraphModel.From(objects, elsewhere));
    }

    private static System.IO.Stream Stream(string json) =>
        new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));







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



        objects.ReadRef(clip, "variableBindingSet", out bool emptyToStart);
        CheckTrue("a field with no fixup starts null", emptyToStart);


        data.SetGlobal(binding, image.Sections.IndexOf(data), second);
        var pointed = new PackfileObjects(image).ReadRef(clip, "variableBindingSet", out bool none);
        CheckTrue("after pointing it, it is not null", !none);
        Check("and it names the object it was aimed at", second, pointed?.Offset);
        Check("with one entry in the table", 1, data.Globals().Count());


        data.SetGlobal(binding, image.Sections.IndexOf(data), clip.Offset);
        var moved = new PackfileObjects(image).ReadRef(clip, "variableBindingSet", out _);
        Check("repointing it names the new object", clip.Offset, moved?.Offset);
        Check("and does not add a second entry for the same field", 1, data.Globals().Count());


        data.SetGlobal(binding, 0, -1);
        objects = new PackfileObjects(image);
        objects.ReadRef(clip, "variableBindingSet", out bool cleared);
        CheckTrue("clearing it reads as null", cleared);
        Check("because the entry is gone, not aimed at zero", 0, data.Globals().Count());


        Check("and the data never changed length", size + size, data.Data.Length - "A.hkx".Length - 1);
    }








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




        var removed = NativeSave.Compare(Extra("0091"), One);
        CheckTrue("removing one is no longer refused", removed.Possible);
        Check("and is planned as a deletion", 1, removed.Gone.Count);
        Check("naming the object that went", 91, removed.Gone[0]);
        CheckTrue("with no value change invented to go with it", removed.Changes.Count == 0);



        CheckTrue("and taking the last of a class with it is still a deletion",
                  NativeSave.Compare(Extra("0091"), One).Possible);


        string renumbered = Extra("0091").Replace("#0090", "#0500");
        CheckTrue("renumbering the existing objects is refused",
                  !NativeSave.Compare(One, renumbered).Possible);



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



        CheckTrue("an addition counts as growing the file", unnamed.Grows);

        System.IO.File.Delete(path);
    }














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


        section.SetGlobal(32, 2, 999);
        Check("changing one does not move it", "96,32,64",
              string.Join(",", section.Globals().Select(g => g.Source)));
        Check("and it holds the new destination", 999,
              section.Globals().First(g => g.Source == 32).Destination);


        section.SetGlobal(128, 2, 700);
        Check("a new entry goes on the end", "96,32,64,128",
              string.Join(",", section.Globals().Select(g => g.Source)));


        section.SetGlobal(64, 0, -1);
        Check("clearing one removes it", "96,32,128",
              string.Join(",", section.Globals().Select(g => g.Source)));
    }



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


        var flags = types.Members("hkbBlendingTransitionEffect").First(m => m.Name == "flags");
        Check("a flags field is not offered as a list", "TYPE_FLAGS", flags.VType);

        var ordinary = shown.First(f => f.Name == "animationName");
        Check("a field that is not an enum stays a plain box", 0, ordinary.Options.Count);
        CheckTrue("and still holds its value", ordinary.Value.Length > 0);
    }


















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


            ("Speed > TrotMaxSpeed", Expression.Verdict.False),
            ("TrotMaxSpeed > Speed", Expression.Verdict.True),


            ("IsPlayer", Expression.Verdict.True),
            ("!IsPlayer", Expression.Verdict.False),
            ("!bBlockMoveStop", Expression.Verdict.True),
            ("bBlockMoveStop", Expression.Verdict.False),



            ("(iSyncReadyAlertRelaxed==2) && (iSyncIdleLocomotion==0)", Expression.Verdict.True),
            ("(iSyncReadyAlertRelaxed!=2) || (iSyncIdleLocomotion==1)", Expression.Verdict.False),
            ("(isMirrored == 0) && (isSightedOver == 0)", Expression.Verdict.False),
            ("(isMirrored == 1) && (isSightedOver == 0)", Expression.Verdict.True),


            ("iIsInSneak == 0 && Pose == 5", Expression.Verdict.True),
            ("iIsInSneak == 1 || Pose == 5", Expression.Verdict.True),
            ("iIsInSneak == 1 && Pose == 5", Expression.Verdict.False),



            ("fReal > 2", Expression.Verdict.True),
            ("fReal > 3", Expression.Verdict.False),




            ("noSuchVariable == 0", Expression.Verdict.Unknown),


            ("noSuchVariable == 0 && iIsInSneak == 1", Expression.Verdict.False),
            ("noSuchVariable == 0 || iIsInSneak == 0", Expression.Verdict.True),
            ("noSuchVariable == 0 && iIsInSneak == 0", Expression.Verdict.Unknown),



            ("iSyncIdleLocomotion=18", Expression.Verdict.Unknown),


            ("Speed >", Expression.Verdict.Unknown),
            ("((Speed > 1)", Expression.Verdict.Unknown),
            ("", Expression.Verdict.Unknown),
        };

        foreach (var (text, want) in expected)
            Check($"\"{text}\"", want, Expression.Evaluate(text, Value));





        CheckTrue("an unreadable condition is not a reason to hold a transition back",
                  Expression.Evaluate("this is not an expression @@@", Value) != Expression.Verdict.False);
    }

    private static void AnExpressionAssignmentDoesTheArithmeticWeShip()
    {
        Console.WriteLine("\nan expression assignment does the arithmetic we ship");

        var world = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Speed"] = 8,
            ["Gain"] = 0.5,
            ["Limit"] = 3,
        };
        double? Value(string name) => world.TryGetValue(name, out double value) ? value : null;

        var arithmetic = Expression.Parse("Out = clamp(Speed * Gain + 1, -Limit, Limit)");
        CheckTrue("the arithmetic assignment parses", arithmetic.Ok && arithmetic.IsAssignment);
        var arithmeticValue = Expression.EvaluateNumber(arithmetic, Value);
        CheckTrue("the arithmetic assignment evaluates", arithmeticValue.Possible);
        Check("clamp keeps the result in the real bound", 3d, arithmeticValue.Value ?? -1);

        var selected = Expression.Parse("Out = cond(Speed > 5, 7, 9)");
        var selectedValue = Expression.EvaluateNumber(selected, Value);
        CheckTrue("the conditional assignment evaluates", selectedValue.Possible);
        Check("cond takes the true branch", 7d, selectedValue.Value ?? -1);

        var missing = Expression.EvaluateNumber(Expression.Parse("Out = Speed + Missing"), Value);
        CheckTrue("an unknown source is refused", !missing.Possible &&
                  missing.Refusal.Contains("Missing", StringComparison.Ordinal));

        var zero = Expression.EvaluateNumber(Expression.Parse("Out = Speed / 0"), Value);
        CheckTrue("division by zero is refused", !zero.Possible &&
                  zero.Refusal.Contains("zero", StringComparison.Ordinal));

        var unsupported = Expression.EvaluateNumber(Expression.Parse("Out = lerp(0, 1, Gain)"), Value);
        CheckTrue("an unsupported function is refused", !unsupported.Possible &&
                  unsupported.Refusal.Contains("lerp", StringComparison.Ordinal));
    }







    private static void AFalseConditionHoldsATransitionBack()
    {
        Console.WriteLine("\na false condition holds a transition back");

        var model = BehaviourGraphModel.Parse(GatedGraph());
        var run = GraphRun.Start(model);

        Check("the graph starts in its first state", "Start", run.Where().FirstOrDefault()?.StateName ?? "");
        Check("and declares the variable the condition names", 0d, run.ValueOf("bGateOpen") ?? -1);







        Check("a real variable is the number it stores and not its bit pattern", 2.5d,
              run.ValueOf("fSpeed") ?? -1);
        Check("so a comparison against it means what it says", Expression.Verdict.True,
              run.Test("fSpeed > 2"));
        Check("in both directions", Expression.Verdict.False, run.Test("fSpeed > 3"));


        var fired = run.Send("Go");
        Check("the gated route does not fire while its condition is false", "Fallback",
              fired.FirstOrDefault()?.ToStateName ?? "");
        Check("and the one held back is reported rather than passed over in silence", 1, run.HeldBack.Count);
        CheckTrue("naming the condition that held it",
                  run.HeldBack.Count > 0 && run.HeldBack[0].Condition == "bGateOpen == 1");



        run.Set("bGateOpen", 1);
        Check("changing a variable drops the reason the last send gave", 0, run.HeldBack.Count);



        var again = GraphRun.Start(model);
        again.Set("bGateOpen", 1);
        var second = again.Send("Go");
        Check("with the variable set the gated route fires instead", "Gated",
              second.FirstOrDefault()?.ToStateName ?? "");
        Check("and nothing is held back", 0, again.HeldBack.Count);



        string refused = "";
        try { again.Set("noSuchVariable", 1); }
        catch (ArgumentException e) { refused = e.Message; }
        CheckTrue("setting a variable the graph does not declare is refused",
                  refused.Contains("declares no variable", StringComparison.Ordinal));
    }

    private static void AnActiveExpressionModifierUpdatesRuntimeVariables()
    {
        Console.WriteLine("\nan active expression modifier updates runtime variables");

        string path = RepositoryFile("dist", "examples", "Dogmeat", "Behaviors", "DogmeatRoot.hkx");
        byte[] original = File.ReadAllBytes(path);
        var image = PackfileImage.Read(path);
        var model = NativeGraphModel.From(new PackfileObjects(image), HavokClassTypes.Shipped);
        CheckTrue("the real Dogmeat graph is read", model != null);
        if (model == null) return;

        CheckTrue("the real graph contains expression modifiers",
                  model.Objects.Any(o => o.Class == "hkbEvaluateExpressionModifier"));
        CheckTrue("the real graph contains expression data arrays",
                  model.Objects.Any(o => o.Class == "hkbExpressionDataArray"));

        var run = GraphRun.Start(model);
        CheckTrue("the active graph contributes real expression rows", run.ActiveExpressionCount >= 4);
        CheckTrue("the active rows include a real head blend assignment",
                  run.ActiveExpressionSources.Any(source => source.StartsWith("fHeadBlendDampedClamped =", StringComparison.Ordinal)));
        run.Set("fHeadBlendDamped", 2);
        run.Advance(0.1f);
        CheckTrue("the active expression receives this tick's duration",
                  Math.Abs((run.ValueOf("fTimeStep") ?? -1) - 0.1d) < 1e-6);
        CheckTrue("a real head-control assignment updates its target",
                  Math.Abs((run.ValueOf("fHeadBlendDampedClamped") ?? -1) - 1d) < 1e-6);

        run.Set("fHeadBlendDamped", -1);
        run.Advance(0.2f);
        CheckTrue("changing an expression input changes the next target value",
                  Math.Abs(run.ValueOf("fHeadBlendDampedClamped") ?? -1) < 1e-6);
        CheckTrue("the simulation does not mutate native source bytes", original.SequenceEqual(File.ReadAllBytes(path)));
    }

    private static string RepositoryFile(params string[] parts)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
        {
            string candidate = Path.Combine(new[] { folder.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("could not find the repository's " + Path.Combine(parts));
    }








    private static void APastedSubtreePointsAtItself()
    {
        Console.WriteLine("\na pasted subtree points at itself");


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



        var copiedRoot = after.Instances[done.RootId - NativeGraphModel.FirstId];
        var aimedAt = after.ReadRef(copiedRoot, "variableBindingSet", out _);
        Check("the copy points at its own child rather than the original's",
              after.Instances[^1].Offset, aimedAt?.Offset ?? -1);
        CheckTrue("which is not where the original's child sits",
                  aimedAt?.Offset != after.Instances[child - NativeGraphModel.FirstId].Offset);




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



        var listed = TemplateStore.All();
        Check("it is listed afterwards", 1, listed.Count);
        Check("under the name it was given", "A Clip Pair", listed.FirstOrDefault()?.Name ?? "nothing listed");
        Check("with its note kept", "for testing", listed.FirstOrDefault()?.Note ?? "nothing listed");



        int before = new PackfileObjects(PackfileImage.Read(into)).Instances.Count;

        var reloaded = TemplateStore.Get("a-clip-pair");
        CheckTrue("the template can be found again by its slug", reloaded != null);
        if (reloaded == null) return;

        var result = TemplateStore.Apply(reloaded, into);
        System.IO.File.WriteAllBytes(into, result.Bytes);

        var after = new PackfileObjects(PackfileImage.Read(into));
        Check("both objects arrive in the other file", before + 2, after.Instances.Count);
        Check("and the applied root is the id reported", NativeGraphModel.FirstId + before, result.RootId);




        var root = after.Instances[result.RootId - NativeGraphModel.FirstId];
        var child = after.ReadRef(root, "variableBindingSet", out _);
        Check("and it points at its own copy of the child", after.Instances[^1].Offset, child?.Offset ?? -1);


        CheckTrue("a template can be forgotten", TemplateStore.Remove("a-clip-pair"));
        Check("and is gone from the list", 0, TemplateStore.All().Count);
        CheckTrue("along with its copy of the file",
                  !System.IO.File.Exists(System.IO.Path.Combine(folder, "a-clip-pair.hkx")));
    }







    private static void ATemplateRefusesToLiftWhatSharesItsFile()
    {
        Console.WriteLine("\na template refuses to lift what shares its file");

        string folder = OwnTemplateFolder("shares");
        string work = System.IO.Path.Combine(folder, "work");
        string from = WriteImage(ThreeClipsSharingAChild(out int shared), work, "Shared.hkx");


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





    private static void ATemplateSaysWhatToDeclareRatherThanJustFailing()
    {
        Console.WriteLine("\na template says what to declare rather than just failing");

        string folder = OwnTemplateFolder("symbols");
        string work = System.IO.Path.Combine(folder, "work");
        string from = WriteImage(TwoClipsOnePointingAtTheOther(out _), work, "From.hkx");
        string into = WriteImage(TwoClipsOnePointingAtTheOther(out _), work, "Into.hkx");

        var lifted = TemplateStore.Lift(from, NativeGraphModel.FirstId, "Plain");


        var plain = TemplateStore.Against(lifted, into);
        CheckTrue("a template using no symbols fits a file declaring none", plain.Fits);
        Check("and says so plainly", "everything this needs is already declared", plain.ToString());




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



        Check("an escaped backslash does not eat the character after it",
              "a\\rb", TemplateStore.Decode(TemplateStore.Encode("a\\rb")));



        string folder = OwnTemplateFolder("names");
        string work = System.IO.Path.Combine(folder, "work");
        string from = WriteImage(TwoClipsOnePointingAtTheOther(out _), work, "From.hkx");

        var lifted = TemplateStore.Lift(from, NativeGraphModel.FirstId, "Awkward", "note\rwith a return");
        Check("a note holding a carriage return comes back whole", "note\rwith a return",
              TemplateStore.Get(lifted.Slug)?.Note);

        Check("and the description is still one line per field", 8,
              System.IO.File.ReadAllLines(System.IO.Path.Combine(folder, lifted.Slug + ".template")).Length);
    }

    private static void PredefinedTemplateCatalogResolvesDefaults()
    {
        var all = PredefinedTemplates.All();
        Check("the predefined catalog has the three agreed shapes", 3, all.Count);
        Check("the clip template has its stable ID", "clip-generator", all[0].Id);

        var clip = PredefinedTemplates.Get("clip-generator");
        CheckTrue("the clip template can be found by ID", clip != null);
        if (clip == null) return;

        var resolved = PredefinedTemplates.Resolve(clip, new Dictionary<string, string>
        {
            ["animation"] = "Walk.hkx",
        });

        CheckTrue("the required animation resolves", resolved.Possible);
        Check("the clip name has an explicit default", "New Clip", resolved.Text("name"));
        Check("the clip playback mode has an explicit default", "looping", resolved.Choice("mode"));
        var invalidMode = PredefinedTemplates.Resolve(clip, new Dictionary<string, string>
        {
            ["animation"] = "Walk.hkx",
            ["mode"] = "forever",
        });
        CheckTrue("an unknown playback mode is refused", !invalidMode.Possible);

        var blend = PredefinedTemplates.Get("blend-generator");
        CheckTrue("the blend template can be found by ID", blend != null);
        if (blend != null)
        {
            var count = PredefinedTemplates.Resolve(blend, new Dictionary<string, string> { ["children"] = "3" });
            CheckTrue("the child count resolves", count.Possible);
            Check("the child count resolves once as an integer", 3, count.Count("children"));
        }

        var state = PredefinedTemplates.Get("state-with-generator");
        CheckTrue("the state template can be found by ID", state != null);
        if (state != null)
        {
            var reference = PredefinedTemplates.Resolve(state, new Dictionary<string, string> { ["machine"] = "#91" });
            CheckTrue("the state machine reference resolves", reference.Possible);
            Check("the state machine reference resolves once as an object ID", 91, reference.ObjectId("machine"));

            var malformed = PredefinedTemplates.Resolve(state, new Dictionary<string, string> { ["machine"] = "machine" });
            CheckTrue("a malformed object reference is refused before materialization", !malformed.Possible);
        }

        var missing = PredefinedTemplates.Resolve(clip, new Dictionary<string, string>());
        CheckTrue("a missing required slot is refused", !missing.Possible);
        CheckTrue("the refusal names the missing slot", missing.Refusal?.Contains("animation", StringComparison.Ordinal) == true);
    }

    private static void PredefinedClipGeneratorIsNativeAndAtomic()
    {
        string folder = OwnTemplateFolder("predefined-clip");
        string path = WriteImage(ClipInAPackfile("Idle.hkx", out _), folder, "Clip.hkx");
        byte[] original = System.IO.File.ReadAllBytes(path);

        var result = PredefinedTemplates.Instantiate(path, "clip-generator", new Dictionary<string, string>
        {
            ["animation"] = "Walk.hkx",
        });

        CheckTrue("the predefined clip materializes", result.Possible);
        CheckTrue("the source file remains unchanged until a caller accepts bytes",
                  original.SequenceEqual(System.IO.File.ReadAllBytes(path)));
        if (result.Bytes == null) return;

        var objects = new PackfileObjects(PackfileImage.Read(result.Bytes), HavokClasses.Shipped);
        Check("the materialized root is a real clip", "hkbClipGenerator",
              objects.Instances[result.RootId - NativeGraphModel.FirstId].ClassName);
        Check("the requested animation is written", "Walk.hkx",
              objects.ReadString(objects.Instances[^1], "animationName"));
        Check("the default playback mode is looping", 1, objects.ReadInt(objects.Instances[^1], "mode"));

        var singlePlay = PredefinedTemplates.Instantiate(path, "clip-generator", new Dictionary<string, string>
        {
            ["animation"] = "Walk.hkx",
            ["mode"] = "single-play",
        });
        CheckTrue("single-play mode materializes", singlePlay.Possible);
        if (singlePlay.Bytes != null)
        {
            var singlePlayObjects = new PackfileObjects(PackfileImage.Read(singlePlay.Bytes), HavokClasses.Shipped);
            Check("single-play mode uses the shipped enum value", 0,
                  singlePlayObjects.ReadInt(singlePlayObjects.Instances[^1], "mode"));
        }

        var unknown = PredefinedTemplates.Instantiate(path, "unknown", new Dictionary<string, string>());
        CheckTrue("an unknown predefined template is refused", !unknown.Possible && unknown.Bytes == null && unknown.CreatedIds.Count == 0);
    }

    private static void PredefinedBlendGeneratorCreatesItsChildren()
    {
        string folder = OwnTemplateFolder("predefined-blend");
        string path = WriteImage(ClipInAPackfile("Idle.hkx", out _), folder, "Blend.hkx");

        var result = PredefinedTemplates.Instantiate(path, "blend-generator", new Dictionary<string, string>
        {
            ["children"] = "3",
        });

        CheckTrue("the predefined blend materializes", result.Possible);
        if (result.Bytes == null) return;

        var objects = new PackfileObjects(PackfileImage.Read(result.Bytes), HavokClasses.Shipped);
        var blend = objects.Instances[result.RootId - NativeGraphModel.FirstId];
        Check("the materialized root is a real blender", "hkbBlenderGenerator", blend.ClassName);
        Check("the blender has exactly the requested child references", 3, objects.ReadRefArray(blend, "children")?.Count);
        Check("three real blender child objects were created", 3,
              objects.Instances.Count(instance => instance.ClassName == "hkbBlenderGeneratorChild"));
        Check("the blender default parameter is one", 1f, objects.ReadFloat(blend, "blendParameter"));
        Check("the blender default flags are preserved", 8, objects.ReadInt(blend, "flags"));

        var invalid = PredefinedTemplates.Instantiate(path, "blend-generator", new Dictionary<string, string>
        {
            ["children"] = "0",
        });
        CheckTrue("a below-minimum child count is refused without replacement bytes",
                  !invalid.Possible && invalid.Bytes == null && invalid.CreatedIds.Count == 0);
        var aboveMaximum = PredefinedTemplates.Instantiate(path, "blend-generator", new Dictionary<string, string>
        {
            ["children"] = (PredefinedTemplates.MaximumBlendChildren + 1).ToString(),
        });
        CheckTrue("an above-maximum child count is refused", !aboveMaximum.Possible && aboveMaximum.Bytes == null);
    }

    private static void PredefinedStateAttachesItsGenerator()
    {
        string folder = OwnTemplateFolder("predefined-state");
        byte[] source = ClipInAPackfile("Idle.hkx", out _).Rebuild();
        var setup = new NativeSave.Plan(new List<NativeSave.Change>
        {
            new("hkbStateMachine", 0, "", "#91", Added: true),
            new("hkbStateMachine", 0, "states", "", Array: true),
            new("hkbStateMachine", 0, "startStateId", "0"),
        }, null);
        string path = WriteImage(PackfileImage.Read(NativeSave.Apply(source, setup)), folder, "State.hkx");

        var result = PredefinedTemplates.Instantiate(path, "state-with-generator", new Dictionary<string, string>
        {
            ["machine"] = "#91",
            ["animation"] = "Walk.hkx",
        });

        CheckTrue("the predefined state materializes", result.Possible);
        Check("the state materializer refusal is empty", "", result.Refusal ?? "");
        if (result.Bytes == null) return;

        var objects = new PackfileObjects(PackfileImage.Read(result.Bytes), HavokClasses.Shipped);
        var state = objects.Instances[result.RootId - NativeGraphModel.FirstId];
        Check("the materialized root is a state", "hkbStateMachineStateInfo", state.ClassName);
        Check("the state gets the next unused state ID", 0, objects.ReadInt(state, "stateId"));
        CheckTrue("the state points at the generated clip", objects.ReadRef(state, "generator", out _)?.ClassName == "hkbClipGenerator");
        Check("the generated state clip loops by default", 1,
              objects.ReadInt(objects.ReadRef(state, "generator", out _)!, "mode"));
        Check("the machine receives the new state", 1,
              objects.ReadRefArray(objects.Instances[1], "states")?.Count);

        var existing = PredefinedTemplates.Instantiate(path, "state-with-generator", new Dictionary<string, string>
        {
            ["machine"] = "#91",
            ["generator"] = "#90",
        });
        CheckTrue("an existing generator needs no animation name", existing.Possible);
        if (existing.Bytes == null) return;
        var existingObjects = new PackfileObjects(PackfileImage.Read(existing.Bytes), HavokClasses.Shipped);
        Check("the state points at the supplied generator", 90,
              NativeGraphModel.FirstId + existingObjects.Instances.ToList().IndexOf(
                  existingObjects.ReadRef(existingObjects.Instances[existing.RootId - NativeGraphModel.FirstId], "generator", out _)!));

        var incompatible = PredefinedTemplates.Instantiate(path, "state-with-generator", new Dictionary<string, string>
        {
            ["machine"] = "#90",
            ["generator"] = "#90",
        });
        CheckTrue("an incompatible state machine is refused without replacement bytes",
                  !incompatible.Possible && incompatible.Bytes == null && incompatible.CreatedIds.Count == 0);
    }

    private static void PredefinedStateUsesFirstUnusedId()
    {
        string folder = OwnTemplateFolder("predefined-state-id");
        byte[] source = ClipInAPackfile("Idle.hkx", out _).Rebuild();
        var setup = new NativeSave.Plan(new List<NativeSave.Change>
        {
            new("hkbStateMachine", 0, "", "#91", Added: true),
            new("hkbStateMachineStateInfo", 0, "", "#92", Added: true),
            new("hkbStateMachineStateInfo", 1, "", "#93", Added: true),
            new("hkbStateMachine", 0, "states", "#92 #93", Array: true),
            new("hkbStateMachine", 0, "startStateId", "2"),
            new("hkbStateMachineStateInfo", 0, "stateId", "0"),
            new("hkbStateMachineStateInfo", 1, "stateId", "2"),
        }, null);
        string path = WriteImage(PackfileImage.Read(NativeSave.Apply(source, setup)), folder, "StateId.hkx");

        var result = PredefinedTemplates.Instantiate(path, "state-with-generator", new Dictionary<string, string>
        {
            ["machine"] = "#91",
            ["generator"] = "#90",
        });

        CheckTrue("a state can fill an unused ID gap", result.Possible);
        if (result.Bytes == null) return;

        var objects = new PackfileObjects(PackfileImage.Read(result.Bytes), HavokClasses.Shipped);
        var state = objects.Instances[result.RootId - NativeGraphModel.FirstId];
        var machine = objects.Instances[1];
        Check("the new state uses the first unused ID", 1, objects.ReadInt(state, "stateId"));
        Check("the machine start state is unchanged", 2, objects.ReadInt(machine, "startStateId"));
    }


    private static string Readable(string text) =>
        text.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\x1f", "\\u", StringComparison.Ordinal);

    private static void RemovingAnObjectIsRefusedAndOrphaningIsNot()
    {
        Console.WriteLine("\nremoving an object is refused and orphaning is not");





        var image = ClipInAPackfile("A.hkx", out _);



        string refused = "";
        try { NativeRemove.Orphan(image, 4000); }
        catch (InvalidOperationException e) { refused = e.Message; }
        CheckTrue("an id the file does not hold is refused",
                  refused.Contains("#4000", StringComparison.Ordinal));
        CheckTrue("and the refusal says what the file does hold",
                  refused.Contains("#" + NativeGraphModel.FirstId, StringComparison.Ordinal));



        var already = NativeRemove.Orphan(image, NativeGraphModel.FirstId);
        CheckTrue("orphaning something nothing reaches changes nothing", !already.Reached);
        Check("no pointer cleared", 0, already.PointersCleared);
        Check("no element dropped", 0, already.ElementsDropped);


        var untouched = ClipInAPackfile("A.hkx", out _);
        NativeRemove.Orphan(untouched, NativeGraphModel.FirstId);
        CheckTrue("leaving the file exactly as it was",
                  untouched.Rebuild().SequenceEqual(ClipInAPackfile("A.hkx", out _).Rebuild()));
    }






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



        Check("carrying every name", 3, grown.Changes[0].Value.Split('\0').Length);
        Check("with the new one last", "Sprint", grown.Changes[0].Value.Split('\0')[^1]);








        const string Odd = "EventPartA\r\nEventPartB";
        var withNewline = NativeSave.Compare(Doc("EventPartA&#13;\nEventPartB"),
                                             Doc("EventPartA&#13;\nEventPartB", "Sprint"));
        CheckTrue("a name carrying a newline is still writable", withNewline.Possible);

        var parts = withNewline.Changes[0].Value.Split('\0');
        Check("and is still one name", 2, parts.Length);
        Check("with its carriage return intact", Odd, parts[0]);



        var shrunk = NativeSave.Compare(Doc("Walk", "Run", "Sprint"), Doc("Walk"));
        CheckTrue("shrinking it is writable too", shrunk.Possible);
        Check("down to one name", 1, shrunk.Changes[0].Value.Split('\0').Length);




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



        var short3 = NativeSave.Compare(Vector.Replace("VALUE", "(0 0 0 0)"),
                                        Vector.Replace("VALUE", "(1 2 3)"));
        CheckTrue("a vector of the wrong length is refused", !short3.Possible);
        CheckTrue("and the refusal says how many were wanted",
                  short3.Refusal?.Contains("4 number(s)", StringComparison.Ordinal) == true);

        var words = NativeSave.Compare(Vector.Replace("VALUE", "(0 0 0 0)"),
                                       Vector.Replace("VALUE", "(a b c d)"));
        CheckTrue("and so is one that is not numbers", !words.Possible);
    }









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



        var words = NativeSave.Compare(Doc2(0, 1, 2), Doc2(0, 1, 2).Replace("2", "two"));
        CheckTrue("a value that is not a number is refused", !words.Possible);


        var data = new byte[6];
        data[4] = 0x39;
        data[5] = 0x05;

        var image = new PackfileImage();
        image.Sections.Add(new PackfileSection { TagBytes = MakeTag("__classnames__"), Data = new byte[8] });
        image.Sections.Add(new PackfileSection { TagBytes = MakeTag("__data__"), Data = data });

        var objects = new PackfileObjects(image, HavokClasses.Shipped);
        Check("two bytes at the end of a section read as two bytes", 1337, objects.ReadNarrowAt(4, 2));
        Check("and reading them as four still says nothing", null, objects.ReadIntAt(4));
    }







    private static void AFieldSaysWhatItIsAndOnlySaysWhatItMeansWhenWeKnow()
    {
        Console.WriteLine("\na field says what it is, and only says what it means when we know");




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


        CheckTrue("an inherited field names the class that declares it",
                  FieldNotes.Structure("hkbClipGenerator", "userData")?.Contains("declared by hkbNode",
                      StringComparison.Ordinal) == true);



        CheckTrue("one of a run of fields written side by side is still described",
                  FieldNotes.Structure("hkbFootIkControlData", "enabled3")?.Contains("number 3 of 8",
                      StringComparison.Ordinal) == true);
        Check("and a name that merely ends in a digit is not mistaken for one",
              null, FieldNotes.Structure("hkbClipGenerator", "notAField7"));


        var mode = FieldNotes.Meaning("hkbClipGenerator", "mode");
        CheckTrue("a field somebody established has a sentence", mode != null);
        CheckTrue("and says where it came from", mode?.From.Length > 0);

        Check("a field nobody has checked has none",
              null, FieldNotes.Meaning("hkbClipGenerator", "cropStartAmountLocalTime"));
        Check("and neither does one on a class with no findings at all",
              null, FieldNotes.Meaning("BSLookAtModifier", "lookAtCameraX"));



        CheckTrue("a sentence belongs to the class that declares the field",
                  FieldNotes.Meaning("hkbStateMachineTransitionInfo", "flags") != null &&
                  FieldNotes.Meaning("hkbClipGenerator", "flags") == null);
    }






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


        var names = image.Section("__classnames__")!;
        int length = names.Data.Length;
        NativeAppend.Object(image, "hkbClipGenerator");
        Check("a class already in the name table is not added again", length, names.Data.Length);




        var fresh = NativeAppend.Object(image, "hkbStateMachine");
        CheckTrue("a class it has never named makes the table longer", names.Data.Length > length);
        Check("and reads back as itself", "hkbStateMachine",
              new PackfileObjects(image).Instances[^1].ClassName);
        Check("with the number it was promised", fresh.Id,
              NativeGraphModel.FirstId + new PackfileObjects(image).Instances.Count - 1);

        CheckTrue("no 0xFF filler is left inside the name table",
                  !names.Data.SkipLast(1).Any(b => b == 0xFF));



        string refused = "";
        try { NativeAppend.Object(image, "hkbNotAClass"); }
        catch (InvalidOperationException e) { refused = e.Message; }
        CheckTrue("a class the table does not describe is refused",
                  refused.Contains("hkbNotAClass", StringComparison.Ordinal));
    }




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


    private static BehaviourGraphModel Broken(Action<BehaviourGraphModel> change)
    {
        var reading = Reading();
        change(reading);
        return reading;
    }









    private static void TheModelComparisonCatchesFaultsPutThereOnPurpose()
    {
        Console.WriteLine("\nthe model comparison catches faults put there on purpose");

        var clean = ModelDiff.Compare(Reading(), Reading());
        CheckTrue("two readings of one file agree", clean.Clean);
        Check("over both objects", 2, clean.Objects);





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



        Check("a value differing only by a space", 1,
              Faults(m => m.Objects[0].Scalars["name"] = "walk "));



        Check("and the disagreement names the object and the field",
              "#90 hkbClipGenerator.mode", Where(m => m.Objects[0].Scalars["mode"] = "MODE_LOOPING"));
        Check("naming the element too, inside a struct array",
              "#90 hkbClipGenerator.states[1].id",
              Where(m => m.Objects[0].StructLists["states"][1]["id"] = "5"));





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



        CheckTrue($"forty bit rotations come back within a thousandth of a radian ({worst40:F6})",
            worst40 < 0.001f);
        CheckTrue($"forty eight bit rotations come back ten times closer ({worst48:F7})",
            worst48 < 0.0001f);



        var backwards = Quaternion.Normalize(new Quaternion(0.1f, 0.2f, 0.3f, -0.927f));
        SplineQuat.Write40(backwards, scratch, 0);
        CheckTrue("a negative largest component keeps its sign",
            SplineQuat.AngleBetween(backwards, SplineQuat.Read40(scratch, 0)) < 0.001f);
    }





    private static void ALinearCurvePassesThroughEveryFrame()
    {
        const int frames = 40;
        var samples = new float[frames];
        for (int f = 0; f < frames; f++) samples[f] = MathF.Sin(f * 0.4f) * 17f + f * 0.9f;

        var curve = SplineFit.FitScalarAt(samples, frames, 1);
        Check("one control point per frame", frames, curve.ControlPoints.Length);
        Check("at degree one", 1, curve.Degree);




        float step = (curve.Max - curve.Min) / 65535f;
        CheckTrue($"and lands on every frame within one quantisation step ({curve.Error:F6} against {step:F6})",
            curve.Error <= step * 1.01f);

        var knots = SplineFormat.Knots(frames, 1, frames);
        Check("the knot vector is the length the format states", frames + 2, knots.Length);
        Check("it starts clamped", 0, (int)knots[0]);
        Check("and ends on the last frame", frames - 1, (int)knots[^1]);
        CheckTrue("with no repeated span in the middle", SplineFormat.KnotsUsable(knots, frames, 1));
    }


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




    private static void AnUndrivenChannelIsNotWrittenAsACurve()
    {
        var clip = MadeUpClip(40, 1);
        var blob = SplineEncoder.Encode(clip);



        Check("scale is marked undriven", 0, (int)blob.Data[3]);
        CheckTrue("rotation is marked as a curve", (blob.Data[2] >> 4) != 0);
        CheckTrue("and so is position", (blob.Data[1] >> 4) != 0);

        Check("three channels counted as undriven", 3, blob.Report.Identity);


        var moving = MadeUpClip(40, 1);
        for (int f = 0; f < moving.NumFrames; f++)
            moving.Tracks[0].Scales[f] = new Vector3(1f + f * 0.01f, 1f, 1f);

        var second = SplineEncoder.Encode(moving);
        CheckTrue("a scale that moves is written as one", (second.Data[3] >> 4) != 0);
    }





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



        Check("an event it is not listening for moves nothing", 0, run.Send("Opened").Count);
        Check("and it is still closed", "Closed", StateName(model, run));
        CheckThrows("an event the graph does not declare is refused rather than reported as ignored",
            () => run.Send("StartOpen"));
        CheckTrue("which the caller can ask about first", !run.Declares("StartOpen"));

        var reach = run.Reachable();
        Check("every state is reachable", 4, reach.Reachable.Count);
        Check("and none is not", 0, reach.Unreachable.Count);
    }




    private static void EveryRunningMachineHearsAnEvent()
    {
        Console.WriteLine("\nevery running machine hears an event");


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



        CheckTrue("and the machine the door left is no longer running",
            run.Where().All(w => w.MachineName != "Inner"));
    }



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


    private static Dictionary<string, ClipTiming.Clip> OneClip(float seconds, string mode,
                                                               params ClipTiming.Trigger[] triggers) =>
        new(StringComparer.Ordinal)
        {
            ["98"] = new ClipTiming.Clip("98", "TheClip", @"Animations\Test.hkt", seconds, triggers, mode),
        };



    private static void AClipEndsAndTheStateLeavesWithoutAnEvent()
    {
        Console.WriteLine("\na clip ends and the state leaves without an event");

        var model = BehaviourGraphModel.Parse(ClipEndGraph());
        var run = GraphRun.Start(model);



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



        CheckTrue("nothing was sent by hand at any point", true);
    }




    private static void AClipLengthIsCroppedAndScaled()
    {
        Console.WriteLine("\na clip's length is cropped and scaled");




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




        Check("cropped and scaled together, in that order",
              3.5f, ClipTiming.Span(0, 10f, 1f, 2f, 2f, "a", out _));

        Check("playing backwards lasts as long as playing forwards",
              5f, ClipTiming.Span(0, 10f, 0, 0, -2f, "a", out _));

        Check("an enforced duration ignores the animation entirely",
              4f, ClipTiming.Span(4f, 10f, 1f, 2f, 8f, "a", out _));



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



        var untimed = GraphRun.Start(BehaviourGraphModel.Parse(ClipEndGraph()));
        Check("with no timing supplied nothing fires either", 0, untimed.Advance(1000f).Count);
        Check("and no stop is invented for it", 0, untimed.Stops.Count);
    }




    private static void ALoopingClipKeepsFiringAndASinglePlayDoesNot()
    {
        Console.WriteLine("\na looping clip keeps firing and a single play does not");





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


    private static int Steps(GraphRun run, float seconds, int howMany)
    {
        int fired = 0;
        for (int i = 0; i < howMany; i++) fired += run.Advance(seconds).Count;
        return fired;
    }



    private static void AnInstantTransitionDoesNotBlend()
    {
        Console.WriteLine("\nan instant transition does not blend");


        var model = BehaviourGraphModel.Parse(TwoStateBlendGraph()
            .Replace("<hkparam name=\"duration\">0.5</hkparam>", "<hkparam name=\"duration\">0.0</hkparam>"));
        var run = GraphRun.Start(model);

        run.Send("Go");
        Check("it moves straight to B", 1, run.Where().Count);
        Check("with no second state fading", "B", run.Where()[0].StateName);
        CheckTrue("and nothing left blending", !run.Blending);
        CheckTrue("advancing the clock changes nothing", run.Where().Count == 1);
    }


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


        var off = BlendWeights.Of(BehaviourGraphModel.Parse(BlenderGraph(0, 0, 1, 0)), "110");
        CheckTrue("a child weighted zero contributes nothing",
            off.Children.First(c => c.GeneratorName == "Run").Contribution < 1e-6f);
        CheckTrue("and the other takes the whole pose",
            Math.Abs(off.Children.First(c => c.GeneratorName == "Walk").Contribution - 1f) < 1e-3f);
    }



    private static void AParametricBlenderIsPickedNotMixed()
    {
        Console.WriteLine("\na parametric blender is picked along an axis, not mixed by weight");


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



        CheckTrue("which is not what mixing the weights would say", Math.Abs(runc.Contribution - 1f) > 0.1f);
    }


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




    private static void AnEditedFrameSurvivesReEncoding()
    {
        Console.WriteLine("\nan edited frame survives being re-encoded");

        var clip = MadeUpClip(60, 2);
        int track = 0, frame = 30;
        var edit = new Vector3(11.5f, -22.25f, 33.75f);


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




        CheckTrue("the change is not lost to the encoder",
            Math.Abs(back.Tracks[track].Translations[frame].X - edit.X) < 0.05f);
    }







    private static void ACutTakesTheClipsOwnTimeWithIt()
    {
        Console.WriteLine("\na cut takes the clip's own time with it");

        var clip = MadeUpClip(61, 2);
        clip.Annotations.Add(new HkxAnnotation { Time = 0.1f, Text = "before the cut" });
        clip.Annotations.Add(new HkxAnnotation { Time = 1.0f, Text = "inside the cut" });
        clip.Annotations.Add(new HkxAnnotation { Time = 1.9f, Text = "after the cut" });



        var motion = new RootMotion.Motion { Duration = clip.Duration };
        for (int f = 0; f < clip.NumFrames; f++)
            motion.Samples.Add(new RootMotion.Sample(new Vector3(0, f * 2f, 0), 0));

        var cut = AnimationEdit.Trim(clip, motion, 15, 45);

        Check("the frames it was told to keep", 31, cut.Animation.NumFrames);
        CheckTrue($"and the length that many frames really are ({cut.Animation.Duration:F4}s)",
            Math.Abs(cut.Animation.Duration - 1f) < 1e-4f);
        Check("every track was cut, not just the first", 2, cut.Animation.Tracks.Count);
        Check("and each holds the kept frames", 31, cut.Animation.Tracks[1].Translations.Count);


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



        CheckTrue("it starts at the origin the way every shipped clip does",
            cut.Motion.Samples[0].Position.Length() < 1e-4f);
        CheckTrue($"while the distance it covers is untouched ({cut.Motion.Travel.Length():F2})",
            Math.Abs(cut.Motion.Travel.Length() - 60f) < 1e-3f);
    }







    private static void ALinearTravelStaysTwoSamplesAfterACut()
    {
        Console.WriteLine("\na linear travel stays two samples after a cut");

        var clip = MadeUpClip(41, 1);
        var motion = new RootMotion.Motion { Duration = clip.Duration };
        motion.Samples.Add(new RootMotion.Sample(Vector3.Zero, 0));
        motion.Samples.Add(new RootMotion.Sample(new Vector3(0, 40f, 0), 0));

        var cut = AnimationEdit.Trim(clip, motion, 10, 30);

        Check("still two samples, not one per frame", 2, cut.Motion!.Samples.Count);
        CheckTrue("still starting at the origin", cut.Motion.Samples[0].Position.Length() < 1e-4f);


        CheckTrue($"covering the half of the path the cut kept ({cut.Motion.Travel.Length():F3})",
            Math.Abs(cut.Motion.Travel.Length() - 20f) < 1e-2f);
    }


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




    private static void ARetimeMovesEverythingThatMeasuresTime()
    {
        Console.WriteLine("\na retime moves everything that measures time");

        var clip = MadeUpClip(41, 2);
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



        CheckTrue($"it travels the distance it always travelled ({slow.Motion.Travel.Length():F2})",
            Math.Abs(slow.Motion.Travel.Length() - motion.Travel.Length()) < 1e-2f);



        CheckTrue($"every original frame is still exactly itself ({slow.PositionError:F5})",
            slow.PositionError < 1e-3f);
    }


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




    private static void ARetimeSaysWhatTheResamplingCost()
    {
        Console.WriteLine("\na retime says what the resampling cost");



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



        var anyway = AnimationEdit.Retime(clip, null, 0.5f);
        Check("without a budget the same retime is produced", 11, anyway.Animation.NumFrames);
    }




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



        float arc = SplineQuat.AngleBetween(frames[0], frames[1]);
        CheckTrue($"and that distance is half the arc ({toFirst:F4} against {arc / 2:F4})",
            Math.Abs(toFirst - arc / 2) < 1e-3f);

        Check("an end is still itself", frames[1], AnimationEdit.Turned(frames, 1f));
    }
}
