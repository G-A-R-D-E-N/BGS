using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// What the file does with each event, and nothing about whether that is right.
//
// An event with listeners and no sender in this file is the ordinary case for a working graph, not a
// fault. Papyrus sends events by name through ObjectReference.PlayAnimation, which 177 vanilla base
// scripts call, and the engine sends more of them itself. So this reports who listens and who sends
// here, and leaves the verdict to whoever is reading.
//
// The table below is not recalled. Every class and member in it was enumerated out of the 314 vanilla
// behaviour files under Meshes\Actors, which between them write an event index in exactly 43 places;
// all 43 are listed. Anything outside that set reports as referenced with no role rather than being
// guessed at, which is also what BSLimbCycleModifier gets: its three members carry events, and which
// direction they run in is not readable from the file.
public static class EventUsage
{
    public enum Role
    {
        /// This file sends the event.
        Raised,
        /// This file reacts to the event.
        Listened,
        /// This file writes the event here, and the direction is not established.
        Referenced,
    }

    public readonly record struct Line(Role Role, string Site, string Note, int Count, IReadOnlyList<string> ObjectIds);

    private sealed record Known(Role Role, string Note);

    private static readonly Dictionary<string, Known> Table = new(StringComparer.Ordinal)
    {
        ["hkbStateMachineEventPropertyArray.events"] = new(Role.Raised, "sent when a state is entered or exited"),
        ["hkbClipTriggerArray.event"] = new(Role.Raised, "sent at a point in the clip"),
        ["hkbStateMachine.eventToSendWhenStateOrTransitionChanges"] = new(Role.Raised, "sent when this machine changes state"),
        ["hkbEventRangeDataArray.event"] = new(Role.Raised, "sent when the driving value enters a range"),
        ["BSEventEveryNEventsModifier.eventToSend"] = new(Role.Raised, "sent once every n of the event it counts"),
        ["BSEventOnDeactivateModifier.event"] = new(Role.Raised, "sent when the modifier deactivates"),
        ["hkbTimerModifier.alarmEvent"] = new(Role.Raised, "sent when the timer elapses"),
        ["BSTimerModifier.alarmEvent"] = new(Role.Raised, "sent when the timer elapses"),
        ["BSRandomAlarmModifier.alarmEvent"] = new(Role.Raised, "sent when the random alarm elapses"),
        ["BSLookAtModifier.targetOutOfLimitEvent"] = new(Role.Raised, "sent when the look at target leaves its limits"),
        ["BSRagdollContactListenerModifier.contactEvent"] = new(Role.Raised, "sent on ragdoll contact"),
        ["BSEventOnFalseToTrueModifier.EventToSend1"] = new(Role.Raised, "sent when its condition turns true"),
        ["BSEventOnFalseToTrueModifier.EventToSend2"] = new(Role.Raised, "sent when its condition turns true"),
        ["BSEventOnFalseToTrueModifier.EventToSend3"] = new(Role.Raised, "sent when its condition turns true"),
        ["BSPassByTargetTriggerModifier.triggerEvent"] = new(Role.Raised, "sent when the target is passed"),

        ["hkbStateMachineTransitionInfoArray.eventId"] = new(Role.Listened, "a transition fires on it"),
        ["hkbStateMachineTimeInterval.enterEventId"] = new(Role.Listened, "starts a transition's time window"),
        ["hkbStateMachineTimeInterval.exitEventId"] = new(Role.Listened, "ends a transition's time window"),
        ["hkbStateMachine.returnToPreviousStateEventId"] = new(Role.Listened, "returns the machine to its previous state"),
        ["hkbStateMachine.randomTransitionEventId"] = new(Role.Listened, "sends the machine to a random state"),
        ["hkbStateMachine.transitionToNextHigherStateEventId"] = new(Role.Listened, "steps the machine up a state"),
        ["hkbStateMachine.transitionToNextLowerStateEventId"] = new(Role.Listened, "steps the machine down a state"),
        ["hkbEventDrivenModifier.activateEventId"] = new(Role.Listened, "activates the modifier"),
        ["hkbEventDrivenModifier.deactivateEventId"] = new(Role.Listened, "deactivates the modifier"),
        ["hkbLayer.onEventId"] = new(Role.Listened, "turns the layer on"),
        ["hkbLayer.offEventId"] = new(Role.Listened, "turns the layer off"),
        ["hkbPoseMatchingGenerator.startMatchingEventId"] = new(Role.Listened, "starts pose matching"),
        ["hkbPoseMatchingGenerator.startPlayingEventId"] = new(Role.Listened, "starts playing the matched pose"),
        ["BSEventEveryNEventsModifier.eventToCheckFor"] = new(Role.Listened, "the event it counts"),
        ["BSCyclicBlendTransitionGenerator.TransitionInEvent"] = new(Role.Listened, "blends the generator in"),
        ["BSCyclicBlendTransitionGenerator.TransitionOutEvent"] = new(Role.Listened, "blends the generator out"),
        ["BSCyclicBlendTransitionGenerator.EventToCrossBlend"] = new(Role.Listened, "starts a cross blend"),
        ["BSCyclicBlendTransitionGenerator.EventToFreezeBlendValue"] = new(Role.Listened, "freezes the blend value"),
        ["BSLookAtModifier.snapToTargetEventId"] = new(Role.Listened, "snaps the look at to its target"),
        ["BSLookAtModifier.restoreDefaultRefPosEventId"] = new(Role.Listened, "restores the default reference position"),
        ["BSDirectAtModifier.snapToTargetEventId"] = new(Role.Listened, "snaps the aim to its target"),
        ["BSDirectAtModifier.useCurrentSourceBonePoseEventId"] = new(Role.Listened, "takes the current source bone pose"),
        ["BSLookAtCapturePoseModifier.capturePoseEventId"] = new(Role.Listened, "captures the pose"),
        ["BSDirectAtCapturePoseModifier.capturePoseEventId"] = new(Role.Listened, "captures the pose"),
        ["hkbExpressionDataArray.assignmentEventIndex"] = new(Role.Listened, "runs an expression's assignment"),
    };

    public static Role RoleOf(string site) => Table.TryGetValue(site, out var known) ? known.Role : Role.Referenced;

    public static string NoteFor(string site) => Table.TryGetValue(site, out var known) ? known.Note : "";

    /// Every declared event's sites, keyed by event index. Events with no site are absent rather than
    /// empty, so a caller has to decide what to say about them rather than being handed a verdict.
    public static Dictionary<int, List<Line>> ByEvent(string xml)
    {
        var byEvent = new Dictionary<int, List<Line>>();
        foreach (var group in SymbolIndexFixup.Usages(xml, events: true).GroupBy(r => r.Index))
        {
            var lines = group.GroupBy(r => r.ToString())
                .Select(g => new Line(RoleOf(g.Key), g.Key, NoteFor(g.Key), g.Count(),
                                      g.Select(u => u.ObjectId).Where(id => id.Length > 0).Distinct().ToList()))
                .OrderBy(l => l.Role).ThenByDescending(l => l.Count).ThenBy(l => l.Site, StringComparer.Ordinal)
                .ToList();
            byEvent[group.Key] = lines;
        }
        return byEvent;
    }

    public static string Describe(Role role) => role switch
    {
        Role.Raised => "raised here",
        Role.Listened => "listened for here",
        _ => "referenced here",
    };

    /// One line per event for a summary that is read rather than clicked through.
    public static string Summarise(IReadOnlyList<Line> lines)
    {
        if (lines.Count == 0) return "";
        var parts = new List<string>();
        foreach (var role in new[] { Role.Raised, Role.Listened, Role.Referenced })
        {
            int count = lines.Where(l => l.Role == role).Sum(l => l.Count);
            if (count > 0) parts.Add($"{count} {Describe(role)}");
        }
        return string.Join(", ", parts);
    }
}
