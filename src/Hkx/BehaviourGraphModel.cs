using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenCommonwealth.Services.Hkx;

public sealed class HkObject
{
    public string Id = "";
    public string Class = "";
    public readonly Dictionary<string, string> Scalars = new();
    public readonly Dictionary<string, List<string>> Lists = new();
    public readonly Dictionary<string, List<Dictionary<string, string>>> StructLists = new();
    public readonly Dictionary<string, Dictionary<string, string>> Structs = new();

    public string Str(string field) => Scalars.TryGetValue(field, out var v) ? v : "";

    public int Int(string field, int fallback = -1)
        => int.TryParse(Str(field), out var v) ? v : fallback;

    public string? Ref(string field)
    {
        string v = Str(field);
        return v.StartsWith('#') ? v[1..] : null;
    }

    public List<string> Refs(string field)
        => Lists.TryGetValue(field, out var l)
            ? l.Where(x => x.StartsWith('#')).Select(x => x[1..]).ToList()
            : new List<string>();

    public List<string> Strings(string field)
        => Lists.TryGetValue(field, out var l) ? l : new List<string>();
}

public sealed class BehaviourGraphModel
{
    private static readonly Regex ObjectHead =
        new(@"<hkobject class=""(?<cls>[A-Za-z0-9_]+)"" name=""#(?<id>\d+)""", RegexOptions.Compiled);

    private static readonly Regex Tag =
        new(@"<(?<close>/?)(?<kind>hkobject|hkparam|hkcstring)(?<attrs>[^>]*)>(?<inner>[^<]*)", RegexOptions.Compiled);

    public readonly Dictionary<string, HkObject> ById = new();
    public readonly List<HkObject> Objects = new();

    public HkObject? Get(string? id) => id != null && ById.TryGetValue(id, out var o) ? o : null;
    public HkObject? Follow(HkObject? o, string field) => o == null ? null : Get(o.Ref(field));

    public static BehaviourGraphModel Parse(string xml)
    {
        var model = new BehaviourGraphModel();
        var marks = ObjectHead.Matches(xml);

        for (int i = 0; i < marks.Count; i++)
        {
            var obj = new HkObject { Id = marks[i].Groups["id"].Value, Class = marks[i].Groups["cls"].Value };
            int start = marks[i].Index + marks[i].Length;
            int end = i + 1 < marks.Count ? marks[i + 1].Index : xml.Length;
            ParseBody(xml[start..end], obj);
            model.ById[obj.Id] = obj;
            model.Objects.Add(obj);
        }
        return model;
    }

    private static void ParseBody(string body, HkObject obj)
    {
        int depth = 0;
        string current = "";
        string nestedInto = "";
        List<Dictionary<string, string>>? structList = null;
        Dictionary<string, string>? element = null;

        foreach (Match m in Tag.Matches(body))
        {
            string kind = m.Groups["kind"].Value;
            bool closing = m.Groups["close"].Value == "/";
            string attrs = m.Groups["attrs"].Value;
            string inner = m.Groups["inner"].Value.Trim();

            if (kind == "hkobject")
            {
                if (closing)
                {
                    depth--;
                    if (depth == 1) nestedInto = "";
                    if (depth == 0 && element != null && structList != null)
                    {
                        structList.Add(element);
                        element = null;
                    }
                    continue;
                }
                depth++;
                if (depth == 1 && current.Length > 0)
                {
                    bool named = attrs.Contains("name=\"");
                    if (named)
                    {
                        obj.Structs[current] = new Dictionary<string, string>();
                    }
                    else
                    {
                        if (!obj.StructLists.TryGetValue(current, out structList))
                        {
                            structList = new List<Dictionary<string, string>>();
                            obj.StructLists[current] = structList;
                        }
                        element = new Dictionary<string, string>();
                    }
                }
                continue;
            }

            if (kind == "hkcstring")
            {
                if (depth <= 1 && current.Length > 0 && !closing)
                {
                    if (!obj.Lists.TryGetValue(current, out var l))
                    {
                        l = new List<string>();
                        obj.Lists[current] = l;
                    }
                    l.Add(inner);
                }
                continue;
            }

            if (closing) continue;

            var nameMatch = Regex.Match(attrs, @"name=""([^""]+)""");
            if (!nameMatch.Success) continue;
            string name = nameMatch.Groups[1].Value;

            if (depth == 0)
            {
                current = name;
                bool isArray = attrs.Contains("numelements=");
                if (attrs.TrimEnd().EndsWith('/'))
                {
                    if (isArray) obj.Lists[name] = new List<string>();
                    else obj.Scalars[name] = "";
                    current = "";
                    continue;
                }
                if (isArray)
                {
                    var items = inner.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
                    obj.Lists[name] = items;
                }
                else if (inner.Length > 0)
                {
                    obj.Scalars[name] = inner;
                    current = "";
                }
            }
            else if (depth == 1 && element != null)
            {
                element[name] = inner;
                if (inner.Length == 0) nestedInto = name;
            }
            else if (depth == 2 && element != null && nestedInto.Length > 0 && inner.Length > 0)
            {
                if (!element.TryGetValue(nestedInto, out string? have) || have.Length == 0)
                    element[nestedInto] = inner;
            }
            else if (depth == 1 && current.Length > 0 && obj.Structs.TryGetValue(current, out var st))
            {
                st[name] = inner;
            }
        }
    }
}
