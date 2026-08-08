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

        /// Where the number sits in the file's own bytes, and how wide it is. Only the walk over the
        /// bytes fills these in; the walk over the text has no offsets to give, which is why `Start`
        /// and `Length` exist separately and mean a position in the document.
        public int ByteAt = -1;
        public int ByteWidth;

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

    /// The same walk, over the file's own bytes instead of hkxpack's text.
    ///
    /// The graph model cannot answer this. It records one level of nesting and these indices sit
    /// deeper: an event property inside a transition inside a transition array. So this walks the
    /// class table rather than the model, into every inline struct and every element of every struct
    /// array, as far down as the classes go.
    ///
    /// Two things the text form says out loud have to be worked out here instead. hkxpack writes a
    /// class attribute on a struct written under a name and none on an array element, so the owning
    /// class of a field in an array element is the class of the object the array belongs to, not the
    /// element's own. And the value is rendered through the same renderer the rest of the reading
    /// uses, so a number that is spelled a particular way in the text is spelled that way here, and a
    /// field holding a name rather than a number is skipped by both.
    ///
    /// No offsets. Renumbering still edits the text, and there is nothing here to edit.
    private static List<Site> Sites(PackfileObjects objects, HavokClassTypes types, bool events,
                                    out List<string> unrecognised)
    {
        var found = new List<Site>();
        var unknown = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < objects.Instances.Count; i++)
        {
            var instance = objects.Instances[i];
            Walk(objects, types, instance.Offset, instance.ClassName, instance.ClassName,
                 (NativeGraphModel.FirstId + i).ToString(), "", "", events, found, unknown);
        }

        unrecognised = new List<string>(unknown);
        unrecognised.Sort(StringComparer.Ordinal);
        return found;
    }

    /// `walking` is whose members are being read. `owner` is the class the text would name at this
    /// point, and the two are not always the same: hkxpack writes a class attribute on a struct
    /// written under a name and none on an element of an array, so inside an element the nearest
    /// named class is still the one the array belongs to. Reporting the element's own class instead
    /// was the only thing the two walks disagreed about, ten times on Dogmeat.
    private static void Walk(PackfileObjects objects, HavokClassTypes types, int offset,
                             string walking, string owner, string ownerId,
                             string outerClass, string outerParam,
                             bool events, List<Site> found, HashSet<string> unknown)
    {
        foreach (var member in types.Members(walking))
        {
            if (!member.Written) continue;
            int at = offset + member.Offset;

            // A struct written under a name carries that name's class in the text, so it becomes the
            // owner of everything inside it.
            if (member.VType == "TYPE_STRUCT")
            {
                if (member.CType != null && types.Knows(member.CType))
                    Walk(objects, types, at, member.CType, member.CType, ownerId, owner, member.Name,
                         events, found, unknown);
                continue;
            }

            if (member.VType is "TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY")
            {
                // An element carries no class in the text, so the owner does not change. This is why
                // hkbStateMachineEventPropertyArray is listed as a carrier: the nearest named class
                // to an event inside its events array is the array itself.
                if (member.VSub != "TYPE_STRUCT" || member.CType == null || !types.Knows(member.CType))
                    continue;

                var array = objects.ArrayAt(at);
                int stride = types[member.CType]?.Size ?? 0;
                if (array == null || stride <= 0) continue;

                for (int e = 0; e < array.Count; e++)
                    Walk(objects, types, array.At + e * stride, member.CType, owner, ownerId,
                         outerClass, member.Name, events, found, unknown);
                continue;
            }

            for (int e = 0; e < Math.Max(1, member.ArrSize); e++)
            {
                string name = member.ArrSize > 0 ? member.Name + (e + 1) : member.Name;

                bool isEvent = EventIdParams.Contains(name)
                               || (name == "id" && EventCarriers.Contains(owner));
                bool isVariable = VariableIndexParams.Contains(name);

                if (!isEvent && !isVariable)
                {
                    if (LooksLikeAnIndex(name) && !NotAnIndex.Contains(name)) unknown.Add(owner + "." + name);
                    continue;
                }
                if (isEvent != events) continue;

                string? text = FieldRender.Render(objects, at, walking, member, Nothing, null, e,
                                                  types, FieldRender.LikeHkxPack);
                if (text == null || !int.TryParse(FieldRender.Plain(text), out int value)) continue;

                string holderClass = owner, holderParam = name;
                if (EventCarriers.Contains(owner))
                {
                    holderClass = outerClass;
                    holderParam = outerParam.Length > 0 ? outerParam : name;
                    if (holderClass.Length == 0 || owner == "hkbStateMachineEventPropertyArray")
                        holderClass = owner;
                }

                found.Add(new Site
                {
                    OwnerId = ownerId,
                    ByteAt = at + e * Math.Max(1, HavokClassTypes.Width(member.VType)),
                    ByteWidth = HavokClassTypes.Width(member.VType),
                    Value = value,
                    Param = name,
                    OwnerClass = owner,
                    HolderClass = holderClass,
                    HolderParam = holderParam,
                });
            }
        }
    }

    /// References are never followed here, so how one would be spelled does not matter.
    private static readonly FieldRender.Reference Nothing = (_, _) => "";

    public static List<string> UnknownIndexFields(PackfileObjects objects, HavokClassTypes? types = null)
    {
        Sites(objects, types ?? HavokClassTypes.Shipped, true, out var unknown);
        return unknown;
    }

    public static List<EventReference> References(PackfileObjects objects, bool events,
                                                  HavokClassTypes? types = null)
    {
        var found = new List<EventReference>();
        foreach (var site in Sites(objects, types ?? HavokClassTypes.Shipped, events, out _))
            if (site.Value >= 0)
                found.Add(new EventReference(site.Value, site.HolderClass, site.HolderParam));
        return found;
    }

    /// One index, and where its bytes are.
    ///
    /// Everything else here answers "which symbol does this file use", which a renumber can act on
    /// because a renumber edits the text. Copying a subtree between files cannot: the numbers have to
    /// be rewritten in the bytes of the objects that were just copied, and only the offset says which
    /// of two identical numbers belongs to the copy rather than to the original it came from.
    public readonly record struct IndexSite(int At, int Width, int Value, string Owner, string Member)
    {
        public override string ToString() => $"{Owner}.{Member} = {Value} at 0x{At:x}";
    }

    /// Every place in a file's bytes that stores an event or variable index, with its offset.
    ///
    /// The same walk and the same recognition rules as everything else here, on purpose. A second
    /// list of which fields carry an index would go out of date against this one, and the field that
    /// fell out of it would be the one nothing renumbered and nothing remapped.
    public static List<IndexSite> IndexSites(PackfileObjects objects, bool events,
                                             HavokClassTypes? types = null)
    {
        var found = new List<IndexSite>();
        foreach (var site in Sites(objects, types ?? HavokClassTypes.Shipped, events, out _))
            if (site.ByteAt >= 0 && site.ByteWidth > 0)
                found.Add(new IndexSite(site.ByteAt, site.ByteWidth, site.Value,
                                        site.HolderClass, site.HolderParam));
        return found;
    }

    public static List<Usage> Usages(PackfileObjects objects, bool events, HavokClassTypes? types = null)
    {
        var found = new List<Usage>();
        foreach (var site in Sites(objects, types ?? HavokClassTypes.Shipped, events, out _))
            if (site.Value >= 0)
                found.Add(new Usage(site.Value, site.HolderClass, site.HolderParam, site.OwnerId, site.OwnerClass));
        return found;
    }

    public static List<Usage> UsagesOf(PackfileObjects objects, bool events, string objectId,
                                       HavokClassTypes? types = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var found = new List<Usage>();
        foreach (var use in Usages(objects, events, types))
        {
            if (use.ObjectId != objectId) continue;
            if (seen.Add($"{use.Index} {use.Member}")) found.Add(use);
        }
        return found;
    }

    public static List<string> ReferencesAtOrAbove(PackfileObjects objects, bool events, int limit,
                                                   HavokClassTypes? types = null)
    {
        var users = new List<string>();
        foreach (var site in Sites(objects, types ?? HavokClassTypes.Shipped, events, out _))
            if (site.Value >= limit)
                users.Add((site.OwnerId.Length > 0 ? $"#{site.OwnerId} " : "")
                          + $"{site.OwnerClass}.{site.Param} uses index {site.Value}");
        return users;
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
