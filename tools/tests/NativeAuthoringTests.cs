using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class NativeAuthoringTests
{
    [Fact]
    public void BatchBuilderCreatesNativeClipsAndStates()
    {
        byte[] source = Source("hkbStateMachine");

        var result = BatchAnimationBuilder.Build(
            source,
            NativeGraphModel.FirstId,
            new[]
            {
                new BatchAnimationBuilder.Entry("Idle", "Animations\\Idle.hkx"),
                new BatchAnimationBuilder.Entry("Walk", "Animations\\Walk.hkx", 7, 1.25f),
            });

        var model = Model(result.Bytes);
        var machine = model.Get(NativeGraphModel.FirstId.ToString());
        Assert.NotNull(machine);

        var states = StateEditor.States(model, machine!.Id);
        Assert.Equal(2, states.Count);
        Assert.Equal(new[] { "Idle", "Walk" }, states.Select(s => s.Name).ToArray());
        Assert.Equal(new[] { 0, 1 }, states.Select(s => s.StateId).ToArray());

        var clips = model.Objects.Where(o => o.Class == "hkbClipGenerator").ToList();
        Assert.Equal(2, clips.Count);
        Assert.Equal("Animations\\Idle.hkx", clips[0].Str("animationName"));
        Assert.Equal("Animations\\Walk.hkx", clips[1].Str("animationName"));
        Assert.Equal("7", clips[1].Str("animationBindingIndex"));
        Assert.Equal("#" + clips[0].Id, states[0].GeneratorRef);
        Assert.Equal("#" + clips[1].Id, states[1].GeneratorRef);
        Assert.Equal(2, result.Created.Count);
    }

    [Fact]
    public void AuthoringSessionAddsEventAndTransitionWithoutXmlMutation()
    {
        byte[] source = Source(
            "hkbStateMachine",
            "hkbBehaviorGraphStringData",
            "hkbBehaviorGraphData");

        var session = new BehaviourAuthoringSession(source);
        int machineId = NativeGraphModel.FirstId;
        int eventId = session.AddEvent("StartWalk");
        var idle = session.AddClip("Idle", "Animations\\Idle.hkx");
        var walk = session.AddClip("Walk", "Animations\\Walk.hkx");
        var idleState = session.AddState(machineId, "Idle", idle.Id);
        var walkState = session.AddState(machineId, "Walk", walk.Id);
        session.AddTransition(machineId, idleState.ObjectId, walkState.ObjectId, eventId);

        var result = session.Build();
        var model = Model(result.Bytes);

        Assert.Equal(new[] { "StartWalk" }, SymbolEditor.EventNames(model));
        var transitions = StateEditor.Transitions(model, machineId.ToString());
        var transition = Assert.Single(transitions);
        Assert.Equal(idleState.StateId, transition.FromStateId);
        Assert.Equal(walkState.StateId, transition.ToStateId);
        Assert.Equal(eventId, transition.EventId);
        Assert.DoesNotContain(result.Findings, finding => finding.BlocksSave);
    }

    [Fact]
    public void NativePlanRejectsWrongFieldKindsBeforeWriting()
    {
        byte[] source = Source("hkbStateMachine");
        var plan = new NativeAuthoringPlan(source);

        var clip = plan.AddObject("hkbClipGenerator");
        Assert.Throws<InvalidOperationException>(() => plan.SetReference(clip.Id, "name", NativeGraphModel.FirstId));
        Assert.Throws<InvalidOperationException>(() => plan.SetPointerArray(clip.Id, "name", new[] { NativeGraphModel.FirstId }));
    }

    [Fact]
    public void StateGeneratorMustActuallyBeAGenerator()
    {
        byte[] source = Source("hkbStateMachine", "hkbBehaviorGraphData");
        var session = new BehaviourAuthoringSession(source);

        var error = Assert.Throws<ArgumentException>(() =>
            session.AddState(NativeGraphModel.FirstId, "Bad", NativeGraphModel.FirstId + 1));

        Assert.Contains("hkbGenerator", error.Message);
        Assert.Equal(2, Model(session.Build().Bytes).Objects.Count);
    }

    [Fact]
    public void TransitionEffectMustActuallyBeATransitionEffect()
    {
        byte[] source = Source("hkbStateMachine", "hkbBehaviorGraphData");
        var session = new BehaviourAuthoringSession(source);
        int machineId = NativeGraphModel.FirstId;
        var idle = session.AddClip("Idle", "Animations\\Idle.hkx");
        var walk = session.AddClip("Walk", "Animations\\Walk.hkx");
        var idleState = session.AddState(machineId, "Idle", idle.Id);
        var walkState = session.AddState(machineId, "Walk", walk.Id);

        var error = Assert.Throws<ArgumentException>(() =>
            session.AddTransition(machineId, idleState.ObjectId, walkState.ObjectId, -1,
                                  NativeGraphModel.FirstId + 1));

        Assert.Contains("hkbTransitionEffect", error.Message);
    }

    [Fact]
    public void TransitionRefusesUndeclaredEventIndex()
    {
        byte[] source = Source("hkbStateMachine");
        var session = new BehaviourAuthoringSession(source);
        int machineId = NativeGraphModel.FirstId;
        var idle = session.AddClip("Idle", "Animations\\Idle.hkx");
        var walk = session.AddClip("Walk", "Animations\\Walk.hkx");
        var idleState = session.AddState(machineId, "Idle", idle.Id);
        var walkState = session.AddState(machineId, "Walk", walk.Id);

        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            session.AddTransition(machineId, idleState.ObjectId, walkState.ObjectId, 0));

        Assert.Contains("not declared", error.Message);
    }

    [Fact]
    public void DuplicateEventNamesAreRefusedCaseInsensitively()
    {
        byte[] source = Source(
            "hkbStateMachine",
            "hkbBehaviorGraphStringData",
            "hkbBehaviorGraphData");
        var session = new BehaviourAuthoringSession(source);

        Assert.Equal(0, session.AddEvent("Ping"));
        var error = Assert.Throws<ArgumentException>(() => session.AddEvent("ping"));

        Assert.Contains("already exists", error.Message);
    }

    [Fact]
    public void StateIdsFollowTheExistingMaxPlusOnePolicy()
    {
        byte[] source = Source("hkbStateMachine");
        var seed = new BehaviourAuthoringSession(source);
        int machineId = NativeGraphModel.FirstId;
        var a = seed.AddClip("A", "Animations\\A.hkx");
        var b = seed.AddClip("B", "Animations\\B.hkx");
        _ = seed.AddState(machineId, "A", a.Id);
        var second = seed.AddState(machineId, "B", b.Id);
        byte[] twoStates = seed.Build().Bytes;

        var makeGap = new NativeAuthoringPlan(twoStates);
        makeGap.SetInt(second.ObjectId, "stateId", 2);
        byte[] gapped = makeGap.Apply().Bytes;

        var session = new BehaviourAuthoringSession(gapped);
        var c = session.AddClip("C", "Animations\\C.hkx");
        var created = session.AddState(machineId, "C", c.Id);

        Assert.Equal(3, created.StateId);
        var states = StateEditor.States(Model(session.Build().Bytes), machineId.ToString());
        Assert.Equal(new[] { 0, 2, 3 }, states.Select(state => state.StateId).ToArray());
    }

    [Fact]
    public void AddsTransitionToATransitionArrayThatAlreadyHasOne()
    {
        byte[] source = Source(
            "hkbStateMachine",
            "hkbBehaviorGraphStringData",
            "hkbBehaviorGraphData");
        int machineId = NativeGraphModel.FirstId;

        var seed = new BehaviourAuthoringSession(source);
        int startWalk = seed.AddEvent("StartWalk");
        int startRun = seed.AddEvent("StartRun");
        var idle = seed.AddClip("Idle", "Animations\\Idle.hkx");
        var walk = seed.AddClip("Walk", "Animations\\Walk.hkx");
        var run = seed.AddClip("Run", "Animations\\Run.hkx");
        var idleState = seed.AddState(machineId, "Idle", idle.Id);
        var walkState = seed.AddState(machineId, "Walk", walk.Id);
        var runState = seed.AddState(machineId, "Run", run.Id);
        seed.AddTransition(machineId, idleState.ObjectId, walkState.ObjectId, startWalk);
        byte[] withOne = seed.Build().Bytes;

        var session = new BehaviourAuthoringSession(withOne);
        session.AddTransition(machineId, idleState.ObjectId, runState.ObjectId, startRun);
        byte[] withTwo = session.Build().Bytes;

        var transitions = StateEditor.Transitions(Model(withTwo), machineId.ToString());
        Assert.Equal(2, transitions.Count);
        Assert.Contains(transitions, transition =>
            transition.FromStateId == idleState.StateId && transition.ToStateId == walkState.StateId &&
            transition.EventId == startWalk);
        Assert.Contains(transitions, transition =>
            transition.FromStateId == idleState.StateId && transition.ToStateId == runState.StateId &&
            transition.EventId == startRun);
    }

    [Fact]
    public void TwoOwnersSharingOneTransitionArrayDoNotOverwriteEachOther()
    {
        byte[] source = Source(
            "hkbStateMachine",
            "hkbBehaviorGraphStringData",
            "hkbBehaviorGraphData");
        int machineId = NativeGraphModel.FirstId;

        var seed = new BehaviourAuthoringSession(source);
        int go = seed.AddEvent("Go");
        int stop = seed.AddEvent("Stop");
        var a = seed.AddClip("A", "Animations\\A.hkx");
        var b = seed.AddClip("B", "Animations\\B.hkx");
        var stateA = seed.AddState(machineId, "A", a.Id);
        var stateB = seed.AddState(machineId, "B", b.Id);
        seed.AddTransition(machineId, stateA.ObjectId, stateB.ObjectId, go);
        byte[] withOne = seed.Build().Bytes;

        // Point state B's transitions at the SAME array object as state A. This shared
        // ownership is unusual but structurally legal; build it directly so the fixture
        // itself is not gated on validation.
        int arrayId = int.Parse(
            Model(withOne).Get(stateA.ObjectId.ToString())!.Ref("transitions")!,
            System.Globalization.CultureInfo.InvariantCulture);
        var rewire = new NativeAuthoringPlan(withOne);
        rewire.SetReference(stateB.ObjectId, "transitions", arrayId);
        byte[] shared = NativeSave.Apply(withOne, rewire.ToSavePlan());

        // Add a transition through each owner. The second add (through B) resolves to the
        // already-advanced array; before the fix it reset the running index back to the
        // source count and planned on top of the transition added through A.
        var session = new BehaviourAuthoringSession(shared);
        session.AddTransition(machineId, stateA.ObjectId, stateB.ObjectId, stop);
        session.AddTransition(machineId, stateB.ObjectId, stateA.ObjectId, stop);
        byte[] withThree = session.Build().Bytes;

        var array = Model(withThree).Get(arrayId.ToString())!;
        Assert.True(array.StructLists.TryGetValue("transitions", out var rows));
        Assert.Equal(3, rows!.Count);
    }

    private static BehaviourGraphModel Model(byte[] bytes)
    {
        var objects = new PackfileObjects(PackfileImage.Read(bytes), HavokClasses.Shipped);
        return NativeGraphModel.From(objects) ?? throw new InvalidOperationException("test file could not be modeled");
    }

    [Fact]
    public void SelectorChoosesBetweenGeneratorsNatively()
    {
        var session = new BehaviourAuthoringSession(Source("hkbStateMachine"));
        var idle = session.AddClip("Idle", "Animations\\Idle.hkx");
        var walk = session.AddClip("Walk", "Animations\\Walk.hkx");
        var selector = session.AddManualSelector("Move", new[] { idle.Id, walk.Id }, 1);
        session.AddState(NativeGraphModel.FirstId, "Move", selector.Id);

        var model = Model(session.Build().Bytes);
        var built = model.Get(selector.Id.ToString())!;
        Assert.Equal("hkbManualSelectorGenerator", built.Class);
        Assert.Equal("Move", built.Str("name"));
        Assert.Equal(new[] { idle.Id.ToString(), walk.Id.ToString() }, built.Refs("generators").ToArray());
        Assert.Equal(1, built.Int("selectedGeneratorIndex"));
    }

    [Fact]
    public void SelectorRefusesAnIndexOutsideItsGenerators()
    {
        var session = new BehaviourAuthoringSession(Source("hkbStateMachine"));
        var idle = session.AddClip("Idle", "Animations\\Idle.hkx");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            session.AddManualSelector("Move", new[] { idle.Id }, 1));
        Assert.Throws<ArgumentException>(() =>
            session.AddManualSelector("Move", Array.Empty<int>()));
    }

    [Fact]
    public void SelectorRefusesSomethingThatIsNotAGenerator()
    {
        var session = new BehaviourAuthoringSession(Source(
            "hkbStateMachine", "hkbBehaviorGraphStringData"));
        var reference = session.AddBehaviorReference("Sub", "Behaviors\\Sub.hkx");

        // a behaviour reference and a state machine are both generators, so both are allowed
        Assert.NotNull(session.AddManualSelector("Move",
            new[] { reference.Id, NativeGraphModel.FirstId }));

        // the graph's string data is not
        Assert.Throws<ArgumentException>(() =>
            session.AddManualSelector("Bad", new[] { NativeGraphModel.FirstId + 1 }));
    }

    [Fact]
    public void BehaviorReferenceCarriesOnlyTheNameItPlays()
    {
        var session = new BehaviourAuthoringSession(Source("hkbStateMachine"));
        var reference = session.AddBehaviorReference("Sub", "Behaviors\\Sub.hkx");
        session.AddState(NativeGraphModel.FirstId, "Sub", reference.Id);

        var built = Model(session.Build().Bytes).Get(reference.Id.ToString())!;
        Assert.Equal("hkbBehaviorReferenceGenerator", built.Class);
        Assert.Equal("Behaviors\\Sub.hkx", built.Str("behaviorName"));
    }

    [Fact]
    public void CloningAStateSharesItsGeneratorAndTakesTheNextStateId()
    {
        var session = new BehaviourAuthoringSession(Source("hkbStateMachine"));
        var clip = session.AddClip("Idle", "Animations\\Idle.hkx");
        var first = session.AddState(NativeGraphModel.FirstId, "Idle", clip.Id);
        var clone = session.CloneState(NativeGraphModel.FirstId, first.ObjectId, "IdleAgain");

        Assert.NotEqual(first.ObjectId, clone.ObjectId);
        Assert.Equal(first.StateId + 1, clone.StateId);

        var model = Model(session.Build().Bytes);
        var states = StateEditor.States(model, NativeGraphModel.FirstId.ToString());
        Assert.Equal(new[] { "Idle", "IdleAgain" }, states.Select(s => s.Name).ToArray());
        Assert.Equal(states[0].GeneratorRef, states[1].GeneratorRef);
    }

    [Fact]
    public void RemovingAStateAlsoDropsTheTransitionsAimedAtIt()
    {
        var session = new BehaviourAuthoringSession(Source(
            "hkbStateMachine", "hkbBehaviorGraphStringData", "hkbBehaviorGraphData"));
        int machine = NativeGraphModel.FirstId;
        int go = session.AddEvent("Go");

        var idleClip = session.AddClip("Idle", "Animations\\Idle.hkx");
        var walkClip = session.AddClip("Walk", "Animations\\Walk.hkx");
        var idle = session.AddState(machine, "Idle", idleClip.Id);
        var walk = session.AddState(machine, "Walk", walkClip.Id);
        session.AddTransition(machine, idle.ObjectId, walk.ObjectId, go);

        var removal = session.RemoveState(machine, walk.ObjectId);
        Assert.Equal(walk.ObjectId, removal.StateObjectId);
        Assert.Equal(1, removal.TransitionsDropped);

        var model = Model(session.Build().Bytes);
        var states = StateEditor.States(model, machine.ToString());
        Assert.Equal(new[] { "Idle" }, states.Select(s => s.Name).ToArray());

        string? array = model.Get(idle.ObjectId.ToString())?.Ref("transitions");
        var rows = array == null ? null : model.Get(array)?.StructLists.GetValueOrDefault("transitions");
        Assert.True(rows == null || rows.Count == 0,
                    "the transition aimed at the removed state is still there");
    }

    [Fact]
    public void RemovingAStateRefusesToEmptyTheMachine()
    {
        var session = new BehaviourAuthoringSession(Source("hkbStateMachine"));
        var clip = session.AddClip("Idle", "Animations\\Idle.hkx");
        var only = session.AddState(NativeGraphModel.FirstId, "Idle", clip.Id);

        Assert.Throws<InvalidOperationException>(() =>
            session.RemoveState(NativeGraphModel.FirstId, only.ObjectId));
    }

    [Fact]
    public void ExpressionConditionIsAssignedAndSurvivesReparse()
    {
        var session = new BehaviourAuthoringSession(Source(
            "hkbStateMachine", "hkbBehaviorGraphStringData", "hkbBehaviorGraphData"));
        int machine = NativeGraphModel.FirstId;
        int go = session.AddEvent("Go");
        var idle = session.AddClip("Idle", "Animations\\Idle.hkx");
        var walk = session.AddClip("Walk", "Animations\\Walk.hkx");
        var from = session.AddState(machine, "Idle", idle.Id);
        var to = session.AddState(machine, "Walk", walk.Id);
        var condition = session.AddExpressionCondition("bGateOpen > 0");
        session.AddTransition(machine, from.ObjectId, to.ObjectId, go, conditionId: condition.Id);

        var model = Model(session.Build().Bytes);
        var route = Assert.Single(StateRoutes.Of(model).Routes);
        Assert.Equal(condition.Id.ToString(), route.ConditionId);
        Assert.Equal("hkbExpressionCondition", model.Get(route.ConditionId)!.Class);
        Assert.True(GraphAuthor.IsNode("hkbExpressionCondition"));
        Assert.Contains(GraphAuthor.Layout(model, 100), item => item.Node.Id == condition.Id.ToString());
        Assert.Contains("bGateOpen > 0", ElementSummary.For(model, StateEditor.Transitions(model, machine.ToString())[0].ArrayId)
            .Values.Single());
    }

    [Fact]
    public void TransitionConditionCanBeReplacedAndCleared()
    {
        var source = Source("hkbStateMachine", "hkbBehaviorGraphStringData", "hkbBehaviorGraphData");
        var seed = new BehaviourAuthoringSession(source);
        int machine = NativeGraphModel.FirstId;
        int go = seed.AddEvent("Go");
        var idle = seed.AddClip("Idle", "Animations\\Idle.hkx");
        var walk = seed.AddClip("Walk", "Animations\\Walk.hkx");
        var from = seed.AddState(machine, "Idle", idle.Id);
        var to = seed.AddState(machine, "Walk", walk.Id);
        var first = seed.AddExpressionCondition("bGateOpen > 0");
        var transition = seed.AddTransition(machine, from.ObjectId, to.ObjectId, go, conditionId: first.Id);
        var withFirst = seed.Build().Bytes;

        var replace = new BehaviourAuthoringSession(withFirst);
        var second = replace.AddExpressionCondition("bGateOpen == 1");
        replace.SetTransitionCondition(transition.ArrayObjectId, transition.Index, second.Id);
        var withSecond = replace.Build().Bytes;
        var replaced = Assert.Single(StateRoutes.Of(Model(withSecond)).Routes);
        Assert.Equal(second.Id.ToString(), replaced.ConditionId);

        var clear = new BehaviourAuthoringSession(withSecond);
        clear.SetTransitionCondition(transition.ArrayObjectId, transition.Index, null);
        Assert.Empty(StateRoutes.Of(Model(clear.Build().Bytes)).Routes.Single().ConditionId);
    }

    [Fact]
    public void SharedConditionRemainsOneGraphObject()
    {
        var session = new BehaviourAuthoringSession(Source(
            "hkbStateMachine", "hkbBehaviorGraphStringData", "hkbBehaviorGraphData"));
        int machine = NativeGraphModel.FirstId;
        int go = session.AddEvent("Go");
        var idle = session.AddClip("Idle", "Animations\\Idle.hkx");
        var walk = session.AddClip("Walk", "Animations\\Walk.hkx");
        var run = session.AddClip("Run", "Animations\\Run.hkx");
        var from = session.AddState(machine, "Idle", idle.Id);
        var walkState = session.AddState(machine, "Walk", walk.Id);
        var runState = session.AddState(machine, "Run", run.Id);
        var condition = session.AddExpressionCondition("bGateOpen > 0");
        session.AddTransition(machine, from.ObjectId, walkState.ObjectId, go, conditionId: condition.Id);
        session.AddTransition(machine, from.ObjectId, runState.ObjectId, go, conditionId: condition.Id);

        var model = Model(session.Build().Bytes);
        Assert.Single(model.Objects, o => o.Class == "hkbExpressionCondition");
        Assert.Equal(2, StateRoutes.Of(model).Routes.Count(route => route.ConditionId == condition.Id.ToString()));
        Assert.Single(GraphAuthor.Layout(model, 100), item => item.Node.Id == condition.Id.ToString());
    }

    [Fact]
    public void NotifyArraysAppendCreateAndRemoveWithoutReordering()
    {
        byte[] source = Source(
            "hkbStateMachine", "hkbBehaviorGraphStringData", "hkbBehaviorGraphData");
        var seed = new BehaviourAuthoringSession(source);
        int machine = NativeGraphModel.FirstId;
        int enter = seed.AddEvent("Enter");
        int exit = seed.AddEvent("Exit");
        int third = seed.AddEvent("Third");
        var clip = seed.AddClip("Idle", "Animations\\Idle.hkx");
        var state = seed.AddState(machine, "Idle", clip.Id);
        seed.AddNotifyEvent(state.ObjectId, BehaviourAuthoringSession.NotifyPhase.Enter, enter);
        seed.AddNotifyEvent(state.ObjectId, BehaviourAuthoringSession.NotifyPhase.Enter, exit);
        seed.AddNotifyEvent(state.ObjectId, BehaviourAuthoringSession.NotifyPhase.Exit, enter);
        var first = seed.Build().Bytes;

        var append = new BehaviourAuthoringSession(first);
        append.AddNotifyEvent(state.ObjectId, BehaviourAuthoringSession.NotifyPhase.Enter, third);
        var second = append.Build().Bytes;
        var model = Model(second);
        var rebuilt = model.Get(state.ObjectId.ToString())!;
        var enterArray = model.Get(rebuilt.Ref("enterNotifyEvents"))!;
        var exitArray = model.Get(rebuilt.Ref("exitNotifyEvents"))!;
        Assert.Equal(new[] { enter, exit, third }, NotifyIds(enterArray));
        Assert.Equal(new[] { enter }, NotifyIds(exitArray));
        var drawn = GraphAuthor.Layout(model, 100).Select(item => item.Node.Id).ToHashSet();
        Assert.Contains(enterArray.Id, drawn);
        Assert.Contains(exitArray.Id, drawn);

        var remove = new BehaviourAuthoringSession(second);
        Assert.Equal(exit, remove.RemoveNotifyEvent(state.ObjectId,
            BehaviourAuthoringSession.NotifyPhase.Enter, 1));
        var final = Model(remove.Build().Bytes);
        Assert.Equal(new[] { enter, third }, NotifyIds(final.Get(final.Get(state.ObjectId.ToString())!
            .Ref("enterNotifyEvents"))!));
    }

    [Fact]
    public void NewStateMachineHasProvenDefaultsAndCanAttachToGraph()
    {
        var session = new BehaviourAuthoringSession(Source("hkbBehaviorGraph"));
        var clip = session.AddClip("Idle", "Animations\\Idle.hkx");
        var machine = session.AddStateMachine("NewMachine", "Idle", clip.Id);
        session.AttachGenerator(NativeGraphModel.FirstId, "rootGenerator", machine.Id);

        var model = Model(session.Build().Bytes);
        var built = model.Get(machine.Id.ToString())!;
        Assert.Equal("NewMachine", built.Str("name"));
        Assert.Equal(0, built.Int("startStateId"));
        Assert.Equal("true", built.Str("wrapAroundStateId"));
        Assert.Equal(32, built.Int("maxSimultaneousTransitions"));
        Assert.Equal("START_STATE_MODE_DEFAULT", built.Str("startStateMode"));
        Assert.Single(StateEditor.States(model, machine.Id.ToString()));
        Assert.Equal("#" + machine.Id, model.Get(NativeGraphModel.FirstId.ToString())!.Str("rootGenerator"));
    }

    [Fact]
    public void NotifyEventRequiresADeclaredGlobalEvent()
    {
        var session = new BehaviourAuthoringSession(Source("hkbStateMachine"));
        var clip = session.AddClip("Idle", "Animations\\Idle.hkx");
        var state = session.AddState(NativeGraphModel.FirstId, "Idle", clip.Id);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.AddNotifyEvent(
            state.ObjectId, BehaviourAuthoringSession.NotifyPhase.Enter, 0));
    }

    [Fact]
    public void SharedNotifyArrayKeepsAppendsFromBothOwners()
    {
        var fixture = SharedNotifySource();
        var session = new BehaviourAuthoringSession(fixture.Bytes);
        session.AddNotifyEvent(fixture.StateA, BehaviourAuthoringSession.NotifyPhase.Enter, fixture.Events[1]);
        session.AddNotifyEvent(fixture.StateB, BehaviourAuthoringSession.NotifyPhase.Enter, fixture.Events[2]);

        var model = Model(session.Build().Bytes);
        var stateA = model.Get(fixture.StateA.ToString())!;
        var stateB = model.Get(fixture.StateB.ToString())!;
        Assert.Equal(stateA.Ref("enterNotifyEvents"), stateB.Ref("enterNotifyEvents"));
        Assert.Equal(fixture.ArrayId.ToString(), stateA.Ref("enterNotifyEvents"));
        Assert.Equal(new[] { fixture.Events[0], fixture.Events[1], fixture.Events[2] },
            NotifyIds(model.Get(stateA.Ref("enterNotifyEvents"))!));
    }

    [Fact]
    public void SharedNotifyArrayKeepsAppendWhenOtherOwnerRemoves()
    {
        var fixture = SharedNotifySource();
        var session = new BehaviourAuthoringSession(fixture.Bytes);
        session.AddNotifyEvent(fixture.StateA, BehaviourAuthoringSession.NotifyPhase.Enter, fixture.Events[1]);
        Assert.Equal(fixture.Events[0], session.RemoveNotifyEvent(
            fixture.StateB, BehaviourAuthoringSession.NotifyPhase.Enter, 0));

        var model = Model(session.Build().Bytes);
        var stateA = model.Get(fixture.StateA.ToString())!;
        var stateB = model.Get(fixture.StateB.ToString())!;
        Assert.Equal(stateA.Ref("enterNotifyEvents"), stateB.Ref("enterNotifyEvents"));
        Assert.Equal(new[] { fixture.Events[1] }, NotifyIds(model.Get(stateA.Ref("enterNotifyEvents"))!));
    }

    [Fact]
    public void NotifyMutationRefusesUnreadablePayload()
    {
        var session = new BehaviourAuthoringSession(Source(
            "hkbStateMachine", "hkbBehaviorGraphStringData", "hkbBehaviorGraphData"));
        int machine = NativeGraphModel.FirstId;
        int eventId = session.AddEvent("Enter");
        var clip = session.AddClip("Idle", "Animations\\Idle.hkx");
        var state = session.AddState(machine, "Idle", clip.Id);
        session.AddNotifyEvent(state.ObjectId, BehaviourAuthoringSession.NotifyPhase.Enter, eventId);
        var valid = session.Build().Bytes;
        var model = Model(valid);
        var array = model.Get(model.Get(state.ObjectId.ToString())!.Ref("enterNotifyEvents"))!;
        var malformed = NativeSave.Apply(valid, new NativeSave.Plan(new List<NativeSave.Change>
        {
            new("hkbStateMachineEventPropertyArray", int.Parse(array.Id) - NativeGraphModel.FirstId,
                "events", clip.Reference, Element: 0, Member: "payload", Id: int.Parse(array.Id)),
        }, null));
        var before = malformed.ToArray();

        var edit = new BehaviourAuthoringSession(malformed);
        Assert.Throws<ArgumentException>(() => edit.AddNotifyEvent(
            state.ObjectId, BehaviourAuthoringSession.NotifyPhase.Enter, eventId));
        Assert.Equal(before, malformed);
    }

    private sealed record SharedNotifyFixture(byte[] Bytes, int StateA, int StateB, int ArrayId, int[] Events);

    private static SharedNotifyFixture SharedNotifySource()
    {
        var seed = new BehaviourAuthoringSession(Source(
            "hkbStateMachine", "hkbBehaviorGraphStringData", "hkbBehaviorGraphData"));
        int machine = NativeGraphModel.FirstId;
        int first = seed.AddEvent("First");
        int second = seed.AddEvent("Second");
        int third = seed.AddEvent("Third");
        var clipA = seed.AddClip("A", "Animations\\A.hkx");
        var clipB = seed.AddClip("B", "Animations\\B.hkx");
        var stateA = seed.AddState(machine, "A", clipA.Id);
        var stateB = seed.AddState(machine, "B", clipB.Id);
        seed.AddNotifyEvent(stateA.ObjectId, BehaviourAuthoringSession.NotifyPhase.Enter, first);
        var oneOwner = seed.Build().Bytes;

        var oneModel = Model(oneOwner);
        int arrayId = int.Parse(oneModel.Get(stateA.ObjectId.ToString())!.Ref("enterNotifyEvents")!);
        var rewire = new NativeAuthoringPlan(oneOwner);
        rewire.SetReference(stateB.ObjectId, "enterNotifyEvents", arrayId);
        return new SharedNotifyFixture(rewire.Apply().Bytes, stateA.ObjectId, stateB.ObjectId, arrayId,
            new[] { first, second, third });
    }

    private static int[] NotifyIds(HkObject array) => array.StructLists["events"]
        .Select(row => int.Parse(row["id"]))
        .ToArray();

    private static byte[] Source(params string[] classes)
    {
        var image = new PackfileImage();
        image.Sections.Add(new PackfileSection { TagBytes = Tag("__classnames__") });
        image.Sections.Add(new PackfileSection { TagBytes = Tag("__data__") });

        foreach (string className in classes) NativeAppend.Object(image, className);
        FixupOrder.Reorder(image);
        return image.Rebuild();
    }

    private static byte[] Tag(string name)
    {
        var bytes = new byte[20];
        Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        return bytes;
    }
}
