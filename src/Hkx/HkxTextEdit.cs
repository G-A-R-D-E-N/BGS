using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OpenCommonwealth.Services;

namespace OpenCommonwealth.Services.Hkx;

public static class HkxTextEdit
{
    public sealed class Param
    {
        public string Name = "";
        public string Value = "";
    }

    private static readonly Regex ObjectHead =
        new(@"<hkobject class=""(?<cls>[A-Za-z0-9_]+)"" name=""#(?<id>\d+)""", RegexOptions.Compiled);

    private static readonly Regex SimpleParam =
        new(@"^(?<indent>[ \t]*)<hkparam name=""(?<name>[^""]+)""(?:\s*/>|>(?<value>[^<\r\n]*)</hkparam>)[ \t]*\r?$",
            RegexOptions.Compiled | RegexOptions.Multiline);

    public static void ResetDirectory(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                Directory.CreateDirectory(path);
                return;
            }
            catch (Exception e) when (attempt < 4 && (e is IOException or UnauthorizedAccessException))
            {
                System.Threading.Thread.Sleep(150);
            }
        }
    }

    public static string? WhyNotWritable(string path)
    {
        try
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
                return $"{Path.GetFileName(path)} is marked read only. Clear it in the file's " +
                       "Properties, or run  attrib -r  on it, and save again.";

            using (File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { }
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return $"Windows will not let this program write {Path.GetFileName(path)}. It is either " +
                   "read only or owned by another account; check the file's Properties.";
        }
        catch (IOException)
        {
            return $"{Path.GetFileName(path)} is open in another program. Close Fallout 4, the mod " +
                   "manager, or whatever else is holding it, and save again.";
        }
    }

    public static string ReadXml(string path) =>
        File.ReadAllText(path).Replace("\r\n", "\n").Replace("\r", "\n");

    public static string TextOf(string hkxPath)
    {
        try
        {
            var bytes = InputFilePolicy.ReadHkx(hkxPath);
            string ours = NativeXml.From(bytes);
            int objects = new PackfileObjects(PackfileImage.Read(bytes)).Instances.Count;

            if (ours.Length > 0 && ObjectIds(ours).Count == objects) return ours;
        }
        catch (Exception)
        {

        }

        return "";
    }

    public static List<string> ObjectIds(string xmlText)
    {
        var ids = new List<string>();
        foreach (Match m in ObjectHead.Matches(xmlText))
            ids.Add(m.Groups["id"].Value);
        return ids;
    }

    public static (int start, int length) ObjectBlock(string xmlText, string id)
    {
        var matches = ObjectHead.Matches(xmlText);
        for (int i = 0; i < matches.Count; i++)
        {
            if (matches[i].Groups["id"].Value != id) continue;
            int start = matches[i].Index;

            if (i + 1 < matches.Count) return (start, matches[i + 1].Index - start);

            int closed = xmlText.LastIndexOf("</hkobject>", StringComparison.Ordinal);
            if (closed < start) return (start, xmlText.Length - start);

            int end = closed + "</hkobject>".Length;

            while (end < xmlText.Length && char.IsWhiteSpace(xmlText[end])) end++;

            return (start, end - start);
        }
        return (-1, 0);
    }

    public static List<Param> ReadParams(string xmlText, string id)
    {
        var result = new List<Param>();
        var (start, length) = ObjectBlock(xmlText, id);
        if (start < 0) return result;

        string block = xmlText.Substring(start, length);
        foreach (Match m in SimpleParam.Matches(block))
            result.Add(new Param
            {
                Name = m.Groups["name"].Value,
                Value = Decode(m.Groups["value"].Value),
            });
        return result;
    }

    public static List<string>? ArrayValues(string xmlText, string id, string paramName)
    {
        var (start, length) = ObjectBlock(xmlText, id);
        if (start < 0) return null;
        string block = xmlText.Substring(start, length);
        Match param = PlainArrayParam(block, paramName);
        if (!param.Success) return null;
        if (param.Groups["self"].Success) return new List<string>();

        string body = param.Groups["body"].Value;
        if (body.Contains('<')) return null;
        return Decode(body).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public static string SetArrayValues(string xmlText, string id, string paramName,
                                        IReadOnlyList<string> values)
    {
        var (start, length) = ObjectBlock(xmlText, id);
        if (start < 0) throw new ArgumentException($"object #{id} not found");
        string block = xmlText.Substring(start, length);
        Match param = PlainArrayParam(block, paramName);
        if (!param.Success)
            throw new ArgumentException($"#{id} has no array parameter named {paramName}");

        string oldBody = param.Groups["self"].Success ? "" : param.Groups["body"].Value;
        if (oldBody.Contains('<'))
            throw new ArgumentException($"#{id}.{paramName} is an array of nested objects");

        string opening = $"<hkparam name=\"{paramName}\"" + param.Groups["attrs"].Value + ">";
        opening = Regex.Replace(opening, @"numelements=""\d+""", $"numelements=\"{values.Count}\"");
        string replacement = values.Count == 0
            ? $"<hkparam name=\"{paramName}\" numelements=\"0\"/>"
            : opening + EscapeXml(string.Join(" ", values)) + "</hkparam>";

        string rewritten = block[..param.Index] + replacement + block[(param.Index + param.Length)..];
        return xmlText[..start] + rewritten + xmlText[(start + length)..];
    }

    private static Match PlainArrayParam(string block, string name) =>
        Regex.Match(block,
                    $@"<hkparam\s+name=""{Regex.Escape(name)}""(?<attrs>[^>]*)>(?<body>.*?)</hkparam>|" +
                    $@"<hkparam\s+name=""{Regex.Escape(name)}""(?<attrs>[^>]*)/(?<self>)>",
                    RegexOptions.Singleline);

    private static string Decode(string value) => System.Net.WebUtility.HtmlDecode(value);

    public static string EscapeXml(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static string SetParam(string xmlText, string id, string paramName, string newValue)
    {
        var (start, length) = ObjectBlock(xmlText, id);
        if (start < 0) throw new ArgumentException($"object #{id} not found");

        string block = xmlText.Substring(start, length);
        bool replaced = false;

        string updated = SimpleParam.Replace(block, m =>
        {
            if (replaced || m.Groups["name"].Value != paramName) return m.Value;
            replaced = true;
            string body = newValue.Length == 0
                ? $"<hkparam name=\"{paramName}\"/>"
                : $"<hkparam name=\"{paramName}\">{EscapeXml(newValue)}</hkparam>";
            return m.Groups["indent"].Value + body;
        });

        if (!replaced) throw new ArgumentException($"#{id} has no simple parameter named {paramName}");

        return xmlText.Substring(0, start) + updated + xmlText.Substring(start + length);
    }

    public static string SetParamAt(string xmlText, string id, string path, string newValue)
    {
        var (start, length) = ObjectBlock(xmlText, id);
        if (start < 0) throw new ArgumentException($"object #{id} not found");

        string block = xmlText.Substring(start, length);

        var self = TopLevel(block, 0, block.Length)
            .Find(p => p.Kind == "hkobject");
        if (self.Kind == null) throw new ArgumentException($"#{id} has no body to write into");

        var segments = path.Split('.');
        int from = self.InnerStart, to = self.InnerEnd;

        for (int i = 0; i < segments.Length - 1; i++)
        {
            var (name, index) = Segment(segments[i]);
            var param = Find(block, from, to, name)
                ?? throw new ArgumentException($"#{id} has no {name} at {Sofar(segments, i)}");

            if (index < 0)
            {

                var inline = TopLevel(block, param.InnerStart, param.InnerEnd)
                    .Find(p => p.Kind == "hkobject");
                if (inline.Kind == null)
                    throw new ArgumentException($"#{id}.{Sofar(segments, i)} holds no struct to walk into");
                (from, to) = (inline.InnerStart, inline.InnerEnd);
                continue;
            }

            var elements = TopLevel(block, param.InnerStart, param.InnerEnd)
                .FindAll(p => p.Kind == "hkobject");
            if (index >= elements.Count)
                throw new ArgumentException(
                    $"#{id}.{name} has {elements.Count} element{(elements.Count == 1 ? "" : "s")}, " +
                    $"so there is no [{index}]");
            (from, to) = (elements[index].InnerStart, elements[index].InnerEnd);
        }

        var (last, lastIndex) = Segment(segments[^1]);
        if (lastIndex >= 0)
            throw new ArgumentException($"{path} names an array, not a value inside one");

        var target = Find(block, from, to, last)
            ?? throw new ArgumentException($"#{id} has no {last} at {path}");

        string body = newValue.Length == 0
            ? $"<hkparam name=\"{last}\"/>"
            : $"<hkparam name=\"{last}\">{EscapeXml(newValue)}</hkparam>";

        string rewritten = block[..target.Start] + body + block[target.End..];
        return xmlText[..start] + rewritten + xmlText[(start + length)..];
    }

    private static (string Name, int Index) Segment(string segment)
    {
        int bracket = segment.IndexOf('[');
        if (bracket < 0) return (segment, -1);

        string inside = segment[(bracket + 1)..].TrimEnd(']');
        if (!int.TryParse(inside, out int index) || index < 0)
            throw new ArgumentException($"{segment} does not name an element");

        return (segment[..bracket], index);
    }

    private static string Sofar(string[] segments, int upto) => string.Join('.', segments[..(upto + 1)]);

    private static Piece? Find(string text, int from, int to, string name)
    {
        foreach (var piece in TopLevel(text, from, to))
            if (piece.Kind == "hkparam" && piece.Name == name) return piece;
        return null;
    }

    private readonly record struct Piece(string Kind, string Name, int Start, int End,
                                         int InnerStart, int InnerEnd);

    private static readonly Regex AnyTag =
        new(@"<(?<close>/?)(?<kind>hkparam|hkobject)(?<attrs>[^>]*)>", RegexOptions.Compiled);

    private static List<Piece> TopLevel(string text, int from, int to)
    {
        var pieces = new List<Piece>();
        int depth = 0;
        string openKind = "", openName = "";
        int openStart = 0, openInner = 0;

        foreach (Match m in AnyTag.Matches(text, from))
        {
            if (m.Index >= to) break;

            string kind = m.Groups["kind"].Value;
            string attrs = m.Groups["attrs"].Value;
            bool closing = m.Groups["close"].Value == "/";
            bool selfClosing = attrs.TrimEnd().EndsWith('/');

            if (closing)
            {
                depth--;
                if (depth == 0)
                    pieces.Add(new Piece(openKind, openName, openStart, m.Index + m.Length,
                                         openInner, m.Index));
                continue;
            }

            if (selfClosing)
            {

                if (depth == 0)
                    pieces.Add(new Piece(kind, NameOf(attrs), m.Index, m.Index + m.Length,
                                         m.Index + m.Length, m.Index + m.Length));
                continue;
            }

            if (depth == 0)
            {
                openKind = kind;
                openName = NameOf(attrs);
                openStart = m.Index;
                openInner = m.Index + m.Length;
            }
            depth++;
        }

        return pieces;
    }

    private static string NameOf(string attrs)
    {
        var m = Regex.Match(attrs, @"name=""([^""]*)""");
        return m.Success ? m.Groups[1].Value : "";
    }

    public static string ClassOf(string xmlText, string id)
    {
        foreach (Match m in ObjectHead.Matches(xmlText))
            if (m.Groups["id"].Value == id) return m.Groups["cls"].Value;
        return "";
    }

    public static List<string> IdsOfClass(string xmlText, string className)
    {
        var ids = new List<string>();
        foreach (Match m in ObjectHead.Matches(xmlText))
            if (m.Groups["cls"].Value == className) ids.Add(m.Groups["id"].Value);
        return ids;
    }

    public static string AddObject(string xmlText, string className, string signature,
                                   string innerXml, out string newId)
    {
        int highest = 0;
        foreach (Match m in ObjectHead.Matches(xmlText))
            if (int.TryParse(m.Groups["id"].Value, out int n) && n > highest) highest = n;
        newId = (highest + 1).ToString();

        int close = xmlText.LastIndexOf("</hksection>", StringComparison.Ordinal);
        if (close < 0) throw new InvalidOperationException("no </hksection> in this file");

        string block =
            $"        <hkobject class=\"{className}\" name=\"#{newId}\" signature=\"{signature}\">\n" +
            innerXml.TrimEnd('\n') + "\n" +
            "        </hkobject>\n";

        return xmlText.Substring(0, close) + block + xmlText.Substring(close);
    }

    public static string ArrayAppend(string xmlText, string id, string paramName, string elementXml)
    {
        var (start, length) = ObjectBlock(xmlText, id);
        if (start < 0) throw new ArgumentException($"object #{id} not found");
        string block = xmlText.Substring(start, length);

        var empty = new Regex($"<hkparam name=\"{Regex.Escape(paramName)}\" numelements=\"0\"\\s*/>");
        var mEmpty = empty.Match(block);
        if (mEmpty.Success)
        {
            string replacement =
                $"<hkparam name=\"{paramName}\" numelements=\"1\">\n{elementXml.TrimEnd('\n')}\n            </hkparam>";
            block = block.Remove(mEmpty.Index, mEmpty.Length).Insert(mEmpty.Index, replacement);
            return xmlText.Substring(0, start) + block + xmlText.Substring(start + length);
        }

        var open = new Regex($"<hkparam name=\"{Regex.Escape(paramName)}\" numelements=\"(?<n>\\d+)\">");
        var mOpen = open.Match(block);
        if (!mOpen.Success) throw new ArgumentException($"#{id} has no array parameter named {paramName}");

        int count = int.Parse(mOpen.Groups["n"].Value);
        int endTag = ArrayBodyEnd(block, mOpen.Index + mOpen.Length);
        if (endTag < 0) throw new InvalidOperationException($"#{id}.{paramName} is not closed");

        block = block.Insert(endTag, elementXml.TrimEnd('\n') + "\n            ");
        block = open.Replace(block, $"<hkparam name=\"{paramName}\" numelements=\"{count + 1}\">", 1);

        return xmlText.Substring(0, start) + block + xmlText.Substring(start + length);
    }

    public static string ArrayInsertAt(string xmlText, string id, string paramName, int index, string elementXml)
    {
        var (start, length) = ObjectBlock(xmlText, id);
        if (start < 0) throw new ArgumentException($"object #{id} not found");
        string block = xmlText.Substring(start, length);

        var open = new Regex($"<hkparam name=\"{Regex.Escape(paramName)}\" numelements=\"(?<n>\\d+)\">");
        var mOpen = open.Match(block);
        if (!mOpen.Success)
        {
            if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
            return ArrayAppend(xmlText, id, paramName, elementXml);
        }

        int count = int.Parse(mOpen.Groups["n"].Value);
        if (index < 0 || index > count) throw new ArgumentOutOfRangeException(nameof(index));

        int bodyStart = mOpen.Index + mOpen.Length;
        int bodyEnd = ArrayBodyEnd(block, bodyStart);
        if (bodyEnd < 0) throw new InvalidOperationException($"#{id}.{paramName} is not closed");

        var elements = SplitElements(block.Substring(bodyStart, bodyEnd - bodyStart));
        if (elements.Count != count)
            throw new InvalidOperationException(
                $"#{id}.{paramName} says {count} elements but {elements.Count} were found; refusing to edit");

        elements.Insert(index, elementXml.TrimEnd('\n'));
        string newBody = "\n" + string.Join("\n", elements) + "\n            ";

        block = block.Remove(bodyStart, bodyEnd - bodyStart).Insert(bodyStart, newBody);
        block = open.Replace(block, $"<hkparam name=\"{paramName}\" numelements=\"{count + 1}\">", 1);

        return xmlText.Substring(0, start) + block + xmlText.Substring(start + length);
    }

    public static string ArrayRemoveAt(string xmlText, string id, string paramName, int index)
    {
        var (start, length) = ObjectBlock(xmlText, id);
        if (start < 0) throw new ArgumentException($"object #{id} not found");
        string block = xmlText.Substring(start, length);

        var open = new Regex($"<hkparam name=\"{Regex.Escape(paramName)}\" numelements=\"(?<n>\\d+)\">");
        var mOpen = open.Match(block);
        if (!mOpen.Success) throw new ArgumentException($"#{id} has no populated array named {paramName}");

        int count = int.Parse(mOpen.Groups["n"].Value);
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));

        int bodyStart = mOpen.Index + mOpen.Length;
        int bodyEnd = ArrayBodyEnd(block, bodyStart);
        if (bodyEnd < 0) throw new InvalidOperationException($"#{id}.{paramName} is not closed");
        string body = block.Substring(bodyStart, bodyEnd - bodyStart);

        var elements = SplitElements(body);
        if (elements.Count != count)
            throw new InvalidOperationException(
                $"#{id}.{paramName} says {count} elements but {elements.Count} were found; refusing to edit");

        elements.RemoveAt(index);
        string newBody = elements.Count == 0 ? "\n            " : "\n" + string.Join("\n", elements) + "\n            ";

        block = block.Remove(bodyStart, bodyEnd - bodyStart).Insert(bodyStart, newBody);
        block = open.Replace(block, $"<hkparam name=\"{paramName}\" numelements=\"{count - 1}\">", 1);

        return xmlText.Substring(0, start) + block + xmlText.Substring(start + length);
    }

    private static int ArrayBodyEnd(string block, int bodyStart)
    {
        var tag = new Regex(@"<hkparam\b[^>]*?(?<selfclose>/)?>|</hkparam>");
        int depth = 0;
        foreach (Match m in tag.Matches(block, bodyStart))
        {
            if (m.Value.StartsWith("</"))
            {
                if (depth == 0) return m.Index;
                depth--;
            }
            else if (!m.Groups["selfclose"].Success)
            {
                depth++;
            }
        }
        return -1;
    }

    private static List<string> SplitElements(string body)
    {
        var result = new List<string>();
        int depth = 0, from = -1;
        var tag = new Regex(@"<(/?)hkobject\b[^>]*>");
        foreach (Match m in tag.Matches(body))
        {
            bool closing = m.Groups[1].Value == "/";
            if (!closing)
            {
                if (depth == 0) from = m.Index;
                depth++;
            }
            else
            {
                depth--;
                if (depth == 0 && from >= 0)
                {
                    result.Add(body.Substring(from, m.Index + m.Length - from));
                    from = -1;
                }
            }
        }
        if (result.Count > 0) return result;

        foreach (Match m in Regex.Matches(body, @"<hkcstring>.*?</hkcstring>", RegexOptions.Singleline))
            result.Add(m.Value);
        if (result.Count > 0) return result;

        foreach (Match m in Regex.Matches(body, @"(#\d+|null)"))
            result.Add("                " + m.Value);
        return result;
    }

}
