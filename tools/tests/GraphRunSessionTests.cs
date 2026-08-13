using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class GraphRunSessionTests
{
    [Fact]
    public void StartExposesTheInitialRuntimeState()
    {
        var session = new GraphRunSession();

        var view = session.Start(Model(), note: "started");

        Assert.True(view.Ready);
        Assert.False(view.Blending);
        Assert.Equal(new[] { "Go" }, view.Events);
        Assert.Equal(new[] { "Speed" }, view.Variables);
        Assert.Single(view.Active);
        Assert.Equal("Idle", view.Active[0].StateName);
        Assert.Equal(1d, session.ValueOf("Speed"));
        Assert.Equal(new[] { "started" }, view.Output);
        Assert.Equal("1 machine(s) running.  started", view.Summary);
        Assert.Equal(GraphRunSession.MessageKind.Status, view.Kind);
    }

    [Fact]
    public void SendingAnEventMovesTheStateAndRecordsTheRunLog()
    {
        var session = new GraphRunSession();
        session.Start(Model());

        var view = session.Send("Go");

        Assert.Single(view.Active);
        Assert.Equal("Running", view.Active[0].StateName);
        Assert.Equal(2, view.Log.Count);
        Assert.Equal("Event: Go", view.Log[0].Text);
        Assert.Equal("Transition: Go to Running", view.Log[1].Text);
        Assert.Equal("4", view.Log[1].TargetStateId);
        Assert.Contains("1 transition(s) fired", view.Summary);
        Assert.EndsWith("Sent Go. 1 transition(s) fired.", view.Output[^1]);
    }

    [Fact]
    public void MissingAndUnknownEventsDoNotMutateTheRun()
    {
        var session = new GraphRunSession();
        var started = session.Start(Model());

        var missing = session.Send(null);
        Assert.Equal("Choose an event to send.", missing.Summary);
        Assert.Equal(GraphRunSession.MessageKind.Neutral, missing.Kind);
        Assert.Equal(started.Output, missing.Output);
        Assert.Empty(missing.Log);
        Assert.Equal("Idle", missing.Active.Single().StateName);

        var unknown = session.Send("Missing");
        Assert.Equal(GraphRunSession.MessageKind.Error, unknown.Kind);
        Assert.Contains("declares no event called 'Missing'", unknown.Summary);
        Assert.Equal(started.Output, unknown.Output);
        Assert.Empty(unknown.Log);
        Assert.Equal("Idle", unknown.Active.Single().StateName);
    }

    [Fact]
    public void RuntimeVariablesUseInvariantParsingAndPreserveTheOldValueOnFailure()
    {
        var session = new GraphRunSession();
        session.Start(Model());

        var changed = session.SetVariable("Speed", "2.5");
        Assert.Equal(2.5d, session.ValueOf("Speed"));
        Assert.Contains("Speed is now 2.5", changed.Summary);
        int outputBeforeFailure = changed.Output.Count;

        var refused = session.SetVariable("Speed", "not-a-number");
        Assert.Equal(GraphRunSession.MessageKind.Error, refused.Kind);
        Assert.Contains("was not changed", refused.Summary);
        Assert.Equal(2.5d, session.ValueOf("Speed"));
        Assert.Equal(outputBeforeFailure, refused.Output.Count);
    }

    [Fact]
    public void AdvancingTimeReportsBlendProgressAndCompletion()
    {
        var session = new GraphRunSession();
        session.Start(Model(blending: true));

        var sent = session.Send("Go");
        Assert.True(sent.Blending);
        Assert.Contains("A transition is blending", sent.Summary);
        Assert.Equal(2, sent.Active.Count);

        var halfway = session.Advance(0.1f);
        Assert.True(halfway.Blending);
        Assert.Contains("still blending", halfway.Summary);

        var finished = session.Advance(0.1f);
        Assert.False(finished.Blending);
        Assert.Contains("blend finished", finished.Summary);
        Assert.Single(finished.Active);
        Assert.Equal("Running", finished.Active[0].StateName);
    }

    [Fact]
    public void StartingAnUnsupportedDocumentClearsThePreviousRuntime()
    {
        var session = new GraphRunSession();
        session.Start(Model());
        session.Send("Go");

        var noRoot = new BehaviourGraphModel();
        Add(noRoot, "99", "hkbCharacterData");
        var view = session.Start(noRoot);

        Assert.False(view.Ready);
        Assert.Empty(view.Events);
        Assert.Empty(view.Variables);
        Assert.Empty(view.Active);
        Assert.Empty(view.Log);
        Assert.Empty(view.Output);
        Assert.Contains("project or character file", view.Summary);

        view = session.Start(new BehaviourGraphModel());
        Assert.False(view.Ready);
        Assert.Empty(view.Log);
        Assert.Empty(view.Output);
        Assert.Equal("Open a behaviour to run it.", view.Summary);
    }

    [Fact]
    public void OutputRetainsOnlyTheNewestOneHundredTwentyEntries()
    {
        var session = new GraphRunSession();
        session.Start(Model());

        GraphRunSession.View view = session.Current;
        for (int i = 0; i < 130; i++)
            view = session.SetVariable("Speed", i.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(120, view.Output.Count);
        Assert.Contains("Speed is now 10", view.Output[0]);
        Assert.Contains("Speed is now 129", view.Output[^1]);
    }

    private static BehaviourGraphModel Model(bool blending = false)
    {
        var model = new BehaviourGraphModel();

        var graph = Add(model, "1", "hkbBehaviorGraph");
        graph.Scalars["rootGenerator"] = "#2";

        var machine = Add(model, "2", "hkbStateMachine");
        machine.Scalars["name"] = "Locomotion";
        machine.Scalars["startStateId"] = "0";
        machine.Lists["states"] = new List<string> { "#3", "#4" };

        var idle = Add(model, "3", "hkbStateMachineStateInfo");
        idle.Scalars["stateId"] = "0";
        idle.Scalars["name"] = "Idle";
        idle.Scalars["generator"] = "#5";
        idle.Scalars["transitions"] = "#6";

        var running = Add(model, "4", "hkbStateMachineStateInfo");
        running.Scalars["stateId"] = "1";
        running.Scalars["name"] = "Running";
        running.Scalars["generator"] = "#7";
        running.Scalars["transitions"] = "null";

        var idleClip = Add(model, "5", "hkbClipGenerator");
        idleClip.Scalars["name"] = "IdleClip";
        var runClip = Add(model, "7", "hkbClipGenerator");
        runClip.Scalars["name"] = "RunClip";

        var transitions = Add(model, "6", "hkbStateMachineTransitionInfoArray");
        transitions.StructLists["transitions"] = new List<Dictionary<string, string>>
        {
            new()
            {
                ["eventId"] = "0",
                ["toStateId"] = "1",
                ["toNestedStateId"] = "0",
                ["priority"] = "0",
                ["flags"] = "0",
                ["condition"] = "null",
                ["transition"] = blending ? "#8" : "null",
            },
        };

        if (blending)
        {
            var effect = Add(model, "8", "hkbBlendingTransitionEffect");
            effect.Scalars["duration"] = "0.2";
        }

        var strings = Add(model, "20", "hkbBehaviorGraphStringData");
        strings.Lists["eventNames"] = new List<string> { "Go" };
        strings.Lists["variableNames"] = new List<string> { "Speed" };

        var data = Add(model, "21", "hkbBehaviorGraphData");
        data.StructLists["variableInfos"] = new List<Dictionary<string, string>>
        {
            new() { ["type"] = "VARIABLE_TYPE_REAL" },
        };

        var values = Add(model, "22", "hkbVariableValueSet");
        values.StructLists["wordVariableValues"] = new List<Dictionary<string, string>>
        {
            new() { ["value"] = BitConverter.SingleToInt32Bits(1f).ToString() },
        };

        return model;
    }

    private static HkObject Add(BehaviourGraphModel model, string id, string className)
    {
        var value = new HkObject { Id = id, Class = className };
        model.ById[id] = value;
        model.Objects.Add(value);
        return value;
    }
}
