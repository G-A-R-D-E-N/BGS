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

    private static BehaviourGraphModel Model(byte[] bytes)
    {
        var objects = new PackfileObjects(PackfileImage.Read(bytes), HavokClasses.Shipped);
        return NativeGraphModel.From(objects) ?? throw new InvalidOperationException("test file could not be modeled");
    }

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