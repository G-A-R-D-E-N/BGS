using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OpenCommonwealth.Services.Hkx;











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







    private static readonly HashSet<string> EventCarriers = new(StringComparer.Ordinal)
    {
        "hkbEventProperty", "hkbEvent", "hkbStateMachineEventPropertyArray",
    };



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




        public int ByteAt = -1;
        public int ByteWidth;

        public int Value;
        public string Param = "";
        public string OwnerClass = "";
        public string OwnerId = "";
        public string HolderClass = "";
        public string HolderParam = "";
    }




    public readonly record struct EventReference(int Index, string Owner, string Member)
    {
        public override string ToString() => $"{Owner}.{Member}";
    }


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


            string holderClass = owner, holderParam = name;
            if (EventCarriers.Contains(owner))
            {
                holderClass = "";
                for (int i = ownerDepth - 1; i >= 0; i--)
                    if (classStack[i].Length > 0) { holderClass = classStack[i]; break; }
                holderParam = paramStack.Count > 0 ? paramStack[^1] : name;

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






    private static void Walk(PackfileObjects objects, HavokClassTypes types, int offset,
                             string walking, string owner, string ownerId,
                             string outerClass, string outerParam,
                             bool events, List<Site> found, HashSet<string> unknown)
    {
        foreach (var member in types.Members(walking))
        {
            if (!member.Written) continue;
            int at = offset + member.Offset;



            if (member.VType == "TYPE_STRUCT")
            {
                if (member.CType != null && types.Knows(member.CType))
                    Walk(objects, types, at, member.CType, member.CType, ownerId, owner, member.Name,
                         events, found, unknown);
                continue;
            }

            if (member.VType is "TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY")
            {



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
                                                  types, FieldRender.ReferenceText);
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







    public readonly record struct IndexSite(int At, int Width, int Value, string Owner, string Member)
    {
        public override string ToString() => $"{Owner}.{Member} = {Value} at 0x{At:x}";
    }






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


    public static List<string> ReferencesTo(string xml, bool events, int index)
    {
        var users = new List<string>();
        foreach (var site in Sites(xml, events, out _))
            if (site.Value == index)
                users.Add($"{site.HolderClass}.{site.HolderParam}");
        return users;
    }




    public static List<EventReference> References(string xml, bool events)
    {
        var found = new List<EventReference>();
        foreach (var site in Sites(xml, events, out _))
            if (site.Value >= 0)
                found.Add(new EventReference(site.Value, site.HolderClass, site.HolderParam));
        return found;
    }

    public static List<EventReference> EventReferences(string xml) => References(xml, events: true);



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



    public static List<string> ReferencesAtOrAbove(string xml, bool events, int limit)
    {
        var users = new List<string>();
        foreach (var site in Sites(xml, events, out _))
            if (site.Value >= limit)
                users.Add((site.OwnerId.Length > 0 ? $"#{site.OwnerId} " : "")
                          + $"{site.OwnerClass}.{site.Param} uses index {site.Value}");
        return users;
    }




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
