using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenCommonwealth.Services.Hkx;

public static class PapyrusEvents
{
    private static readonly string[] Callers =
    {
        "PlayAnimation", "PlayAnimationAndWait", "PlaySubGraphAnimation",
        "SendAnimationEvent", "PlayIdle", "PlayIdleWithTarget",
    };

    private static readonly Regex Call = new(
        @"\b(?<call>" + string.Join("|", Callers) + @")\s*\((?<args>[^)]*)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Literal = new("\"(?<text>[^\"]*)\"", RegexOptions.Compiled);

    public sealed class Index
    {
        public int ScriptsRead;
        public string Root = "";

        public readonly Dictionary<string, List<string>> ByEvent =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> Senders(string eventName) =>
            ByEvent.TryGetValue(eventName, out var list) ? list : Array.Empty<string>();

        public override string ToString() => ScriptsRead == 0
            ? "no Papyrus sources were read"
            : $"{ScriptsRead} script{(ScriptsRead == 1 ? "" : "s")} read from {Root}, " +
              $"{ByEvent.Count} event name{(ByEvent.Count == 1 ? "" : "s")} sent between them";
    }

    public static Index Scan(string root)
    {
        var index = new Index { Root = root };
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return index;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        try
        {
            foreach (string file in Directory.EnumerateFiles(root, "*.psc", options))
            {
                string text;
                try { text = File.ReadAllText(file, Encoding.UTF8); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }

                index.ScriptsRead++;
                string name = Path.GetFileName(file);

                foreach (Match call in Call.Matches(text))
                    foreach (Match literal in Literal.Matches(call.Groups["args"].Value))
                    {
                        string sent = literal.Groups["text"].Value.Trim();
                        if (sent.Length == 0) continue;

                        if (!index.ByEvent.TryGetValue(sent, out var senders))
                            index.ByEvent[sent] = senders = new List<string>();
                        if (!senders.Contains(name, StringComparer.OrdinalIgnoreCase)) senders.Add(name);
                    }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }

        foreach (var list in index.ByEvent.Values) list.Sort(StringComparer.OrdinalIgnoreCase);
        return index;
    }

    public static string Describe(Index index, string eventName)
    {
        if (index.ScriptsRead == 0) return "";

        var senders = index.Senders(eventName);
        if (senders.Count == 0) return "no sender found in the scanned scripts";

        string named = string.Join(", ", senders.Take(3));
        return senders.Count > 3
            ? $"sent by {named}, and {senders.Count - 3} more"
            : $"sent by {named}";
    }
}
