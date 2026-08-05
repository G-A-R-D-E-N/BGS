using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OpenCommonwealth.Services.Hkx;

// Events and variables are addressed by index everywhere except their own name arrays, so removing
// one has to renumber every reference above it.
//
// The field names below were read out of 132 vanilla behaviour files, not recalled: every hkparam
// whose name matched event or variable was listed with its owning class, then split by hand into the
// ones that carry an index and the ones that do not. Two traps that scan exposed:
//
//   BSAssignVariablesModifier.floatVariable1..20 and intVariable1..4 are values, not indices.
//   An hkbEventProperty or hkbEvent object carries its event in a member called plainly "id", so a
//   name-only rule misses roughly a third of the event references in a typical graph.
public static class SymbolIndexFixup
{
    public static readonly HashSet<string> EventIdParams = new(StringComparer.Ordinal)
    {
        "eventId", "enterEventId", "exitEventId", "activateEventId", "deactivateEventId",
        "onEventId", "offEventId", "randomTransitionEventId", "returnToPreviousStateEventId",
        "transitionToNextHigherStateEventId", "transitionToNextLowerStateEventId",
        "snapToTargetEventId", "useCurrentSourceBonePoseEventId", "capturePoseEventId",
        "restoreDefaultRefPosEventId", "startMatchingEventId", "startPlayingEventId",
        "assignmentEventIndex",
    };

    public static readonly HashSet<string> VariableIndexParams = new(StringComparer.Ordinal)
    {
        "variableIndex", "syncVariableIndex", "assignmentVariableIndex",
    };

    // Classes whose "id" member is an event index rather than an object id.
    //
    // hkbStateMachineEventPropertyArray is here because its events array holds the event property
    // struct inline with no class attribute of its own, so the nearest class is the array. Leaving it
    // out hid every state enter and exit notify event: 2804 references across the 314 vanilla
    // behaviour files, none of them renumbered when an event was removed.
    private static readonly HashSet<string> EventCarriers = new(StringComparer.Ordinal)
    {
        "hkbEventProperty", "hkbEvent", "hkbStateMachineEventPropertyArray",
    };

    // Names that look like an index but are structure: a reference, an array, or a mode enum. Listed
    // so the unknown-name guard below stays quiet about them.
    private static readonly HashSet<string> NotAnIndex = new(StringComparer.Ordinal)
    {
        "variableBindingSet", "variableNames", "variableInfos", "variableBounds",
        "variableInitialValues", "variableMode", "wordVariableValues", "quadVariableValues",
        "variantVariableValues", "eventNames", "eventInfos", "eventMode", "eventRanges",
        "eventData", "eventToSendWhenStateOrTransitionChanges", "events", "event",
        "eventToCheckFor", "eventToSend", "alarmEvent", "contactEvent", "targetOutOfLimitEvent",
        "EventToCrossBlend", "EventToFreezeBlendValue", "TransitionInEvent", "TransitionOutEvent",
        "numberOfEventsBeforeSend", "minimumNumberOfEventsBeforeSend", "randomizeNumberOfEvents",
        "defaultEventMode",
    };

    private static readonly Regex Token = new(
        @"<hkobject(?:\s+class=""(?<cls>[^""]+)"")?(?:\s+name=""#(?<id>\d+)"")?[^>]*?(?<selfclose>/)?>" +
        @"|</hkobject>" +
        @"|<hkparam name=""(?<param>[^""]+)"">(?<value>[^<\r\n]*)</hkparam>" +
        @"|<hkparam name=""(?<open>[^""]+)""[^>]*?(?<paramclose>/)?>" +
        @"|</hkparam>",
        RegexOptions.Compiled);

    private sealed class Site
    {
        public int Start;
        public int Length;
        public int Value;
        public string Param = "";
        public string OwnerClass = "";
        public string OwnerId = "";
        public string HolderClass = "";
        public string HolderParam = "";
    }

    /// Where one event index is written, named by the class that holds it and the member it sits in.
    /// A carrier's own class is not the useful name: every clip trigger and every alarm is an
    /// hkbEventProperty, and what separates them is whose member the property is.
    public readonly record struct EventReference(int Index, string Owner, string Member)
    {
        public override string ToString() => $"{Owner}.{Member}";
    }

    // Every place in the file that stores an event or variable index, with the class that owns it.
    private static List<Site> Sites(string xml, bool events, out List<string> unrecognised)
    {
        var found = new List<Site>();
        var unknown = new HashSet<string>(StringComparer.Ordinal);
        var classStack = new List<string>();
        var paramStack = new List<string>();
        var idStack = new List<string>();

        foreach (Match m in Token.Matches(xml))
        {
            if (m.Value.StartsWith("</hkobject", StringComparison.Ordinal))
            {
                if (classStack.Count > 0) classStack.RemoveAt(classStack.Count - 1);
                if (idStack.Count > 0) idStack.RemoveAt(idStack.Count - 1);
                continue;
            }
            if (m.Value.StartsWith("<hkobject", StringComparison.Ordinal))
            {
                if (!m.Groups["selfclose"].Success)
                {
                    classStack.Add(m.Groups["cls"].Success ? m.Groups["cls"].Value : "");
                    idStack.Add(m.Groups["id"].Success ? m.Groups["id"].Value : "");
                }
                continue;
            }
            if (m.Value.StartsWith("</hkparam", StringComparison.Ordinal))
            {
                if (paramStack.Count > 0) paramStack.RemoveAt(paramStack.Count - 1);
                continue;
            }
            if (m.Groups["open"].Success)
            {
                if (!m.Groups["paramclose"].Success) paramStack.Add(m.Groups["open"].Value);
                continue;
            }
            if (!m.Groups["param"].Success) continue;

            string name = m.Groups["param"].Value;
            string owner = "";
            int ownerDepth = -1;
            for (int i = classStack.Count - 1; i >= 0; i--)
                if (classStack[i].Length > 0) { owner = classStack[i]; ownerDepth = i; break; }

            bool isEvent = EventIdParams.Contains(name)
                           || (name == "id" && EventCarriers.Contains(owner));
            bool isVariable = VariableIndexParams.Contains(name);

            if (!isEvent && !isVariable)
            {
                if (LooksLikeAnIndex(name) && !NotAnIndex.Contains(name)) unknown.Add(owner + "." + name);
                continue;
            }
            if (isEvent != events) continue;
            if (!int.TryParse(m.Groups["value"].Value, out int value)) continue;

            // For a carrier the interesting name is one level out: whose member the event sits in.
            string holderClass = owner, holderParam = name;
            if (EventCarriers.Contains(owner))
            {
                holderClass = "";
                for (int i = ownerDepth - 1; i >= 0; i--)
                    if (classStack[i].Length > 0) { holderClass = classStack[i]; break; }
                holderParam = paramStack.Count > 0 ? paramStack[^1] : name;
                // The array is its own holder: the state info that points at it is a separate object.
                if (holderClass.Length == 0 || owner == "hkbStateMachineEventPropertyArray")
                    holderClass = owner;
            }

            string ownerId = "";
            for (int i = idStack.Count - 1; i >= 0; i--)
                if (idStack[i].Length > 0) { ownerId = idStack[i]; break; }

            found.Add(new Site
            {
                OwnerId = ownerId,
                Start = m.Groups["value"].Index,
                Length = m.Groups["value"].Length,
                Value = value,
                Param = name,
                OwnerClass = owner,
                HolderClass = holderClass,
                HolderParam = holderParam,
            });
        }

        unrecognised = new List<string>(unknown);
        unrecognised.Sort(StringComparer.Ordinal);
        return found;
    }

    private static bool LooksLikeAnIndex(string name) =>
        name.EndsWith("EventId", StringComparison.Ordinal)
        || name.EndsWith("EventIndex", StringComparison.Ordinal)
        || name.EndsWith("VariableIndex", StringComparison.Ordinal);

    public static List<string> UnknownIndexFields(string xml)
    {
        Sites(xml, true, out var unknown);
        return unknown;
    }

    // Objects that point at exactly this index, described well enough to act on.
    public static List<string> ReferencesTo(string xml, bool events, int index)
    {
        var users = new List<string>();
        foreach (var site in Sites(xml, events, out _))
            if (site.Value == index)
                users.Add($"{site.HolderClass}.{site.HolderParam}");
        return users;
    }

    /// Every index the file writes, in one pass. Asking per symbol rescans the whole file once per
    /// symbol: a weapon behaviour declares 142 variables and 731 events against seven megabytes of
    /// text, which is around two minutes of scanning to answer a question one pass answers.
    public static List<EventReference> References(string xml, bool events)
    {
        var found = new List<EventReference>();
        foreach (var site in Sites(xml, events, out _))
            if (site.Value >= 0)
                found.Add(new EventReference(site.Value, site.HolderClass, site.HolderParam));
        return found;
    }

    public static List<EventReference> EventReferences(string xml) => References(xml, events: true);

    /// One site, with the object it lives in, so a list of usages can be clicked through to the node
    /// rather than only read. Same walk as References; the difference is that this keeps the id.
    public readonly record struct Usage(int Index, string Owner, string Member, string ObjectId, string OwnerClass)
    {
        public override string ToString() => $"{Owner}.{Member}";
    }

    public static List<Usage> Usages(string xml, bool events)
    {
        var found = new List<Usage>();
        foreach (var site in Sites(xml, events, out _))
            if (site.Value >= 0)
                found.Add(new Usage(site.Value, site.HolderClass, site.HolderParam, site.OwnerId, site.OwnerClass));
        return found;
    }

    /// Every symbol one object touches, for reading the relationship from the node's end. The same
    /// index can be written more than once by one object, so repeats are folded here rather than
    /// listed.
    public static List<Usage> UsagesOf(string xml, bool events, string objectId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var found = new List<Usage>();
        foreach (var use in Usages(xml, events))
        {
            if (use.ObjectId != objectId) continue;
            if (seen.Add($"{use.Index} {use.Member}")) found.Add(use);
        }
        return found;
    }

    // Anything addressing a symbol the graph does not declare. -1 is the format's "none" and is not
    // an overrun.
    public static List<string> ReferencesAtOrAbove(string xml, bool events, int limit)
    {
        var users = new List<string>();
        foreach (var site in Sites(xml, events, out _))
            if (site.Value >= limit)
                users.Add((site.OwnerId.Length > 0 ? $"#{site.OwnerId} " : "")
                          + $"{site.OwnerClass}.{site.Param} uses index {site.Value}");
        return users;
    }

    // Decrements every index above the removed one. Refuses if the file carries an index field this
    // does not recognise, because renumbering around an unknown field is how a graph ends up quietly
    // playing the wrong animation.
    public static string ShiftDown(string xml, bool events, int removedIndex, out int rewritten)
    {
        var sites = Sites(xml, events, out var unknown);
        if (unknown.Count > 0)
            throw new InvalidOperationException(
                "this file carries index fields that are not in the known table, so renumbering is not safe: "
                + string.Join(", ", unknown));

        rewritten = 0;
        var edits = new List<Site>();
        foreach (var site in sites)
            if (site.Value > removedIndex) edits.Add(site);

        // Back to front, so an earlier edit cannot move a later one's offset.
        edits.Sort((a, b) => b.Start.CompareTo(a.Start));
        foreach (var site in edits)
        {
            string replacement = (site.Value - 1).ToString();
            xml = xml.Remove(site.Start, site.Length).Insert(site.Start, replacement);
            rewritten++;
        }
        return xml;
    }
}
