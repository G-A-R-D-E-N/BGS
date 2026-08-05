using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenCommonwealth.Services.Hkx;

// A transition listening for an event that nothing in its own file sends is the ordinary case, not a
// fault: the sender is usually a script. 177 vanilla base scripts call ObjectReference.PlayAnimation,
// which resolves the event by name against the behaviour's own event list.
//
// So this answers the question rather than judging it. It reports which scripts name an event and
// nothing else: no pass, no fail, and silence when no scripts folder has been set.
public static class PapyrusEvents
{
    // The calls that reach a behaviour graph by event name. Every one takes the name as a string
    // literal in practice; a name built at runtime is invisible here and that is a limit worth
    // knowing rather than one worth guessing around.
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
        /// Script file names that name each event, keyed by event name, matched without case because
        /// Papyrus is case insensitive and the graphs are not consistent.
        public readonly Dictionary<string, List<string>> ByEvent =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> Senders(string eventName) =>
            ByEvent.TryGetValue(eventName, out var list) ? list : Array.Empty<string>();

        public override string ToString() => ScriptsRead == 0
            ? "no Papyrus sources were read"
            : $"{ScriptsRead} script{(ScriptsRead == 1 ? "" : "s")} read from {Root}, " +
              $"{ByEvent.Count} event name{(ByEvent.Count == 1 ? "" : "s")} sent between them";
    }

    /// Reads .psc sources under a folder. Compiled .pex is deliberately not parsed: its string table
    /// holds every string the script uses, so matching against it would report a sender for names the
    /// script only prints.
    public static Index Scan(string root)
    {
        var index = new Index { Root = root };
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return index;

        foreach (string file in Directory.EnumerateFiles(root, "*.psc", SearchOption.AllDirectories))
        {
            string text;
            try { text = File.ReadAllText(file, Encoding.UTF8); }
            catch (IOException) { continue; }

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

        foreach (var list in index.ByEvent.Values) list.Sort(StringComparer.OrdinalIgnoreCase);
        return index;
    }

    /// What to show beside an event. Information, not a verdict: a name no script sends is not a
    /// fault, because the engine sends plenty of them itself.
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
