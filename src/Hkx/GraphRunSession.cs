using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

internal sealed class GraphRunSession
{
    internal enum MessageKind
    {
        Neutral,
        Status,
        Error,
    }

    internal sealed record LogEntry(string Text, string TargetStateId = "");

    internal sealed record View(
        bool Ready,
        bool Blending,
        IReadOnlyList<string> Events,
        IReadOnlyList<string> Variables,
        IReadOnlyList<GraphRun.Active> Active,
        IReadOnlyList<GraphRun.Stop> Stops,
        IReadOnlyList<GraphRun.Blocked> HeldBack,
        IReadOnlyList<LogEntry> Log,
        IReadOnlyList<string> Output,
        int TimedClipCount,
        string Summary,
        MessageKind Kind);

    private const int OutputLimit = 120;

    private GraphRun? _run;
    private readonly List<LogEntry> _log = new();
    private readonly List<string> _output = new();
    private string _summary = "Open a behaviour to run it.";
    private MessageKind _kind = MessageKind.Neutral;

    internal View Current => Snapshot();

    internal View Clear(string summary = "Open a behaviour to run it.")
    {
        _run = null;
        _log.Clear();
        _output.Clear();
        _summary = summary;
        _kind = MessageKind.Neutral;
        return Snapshot();
    }

    internal View Start(
        BehaviourGraphModel model,
        IReadOnlyDictionary<string, ClipTiming.Clip>? timings = null,
        string note = "Started at the graph's root.")
    {
        _run = null;
        _log.Clear();
        _output.Clear();

        if (model.Objects.Count == 0)
            return Clear("Open a behaviour to run it.");

        var run = GraphRun.Start(model);
        if (run.RootId.Length == 0)
            return Clear("This is a project or character file rather than a graph, so there is nothing in it to run.");

        if (timings != null)
        {
            try { run.Time(timings); }
            catch (Exception) { }
        }

        _run = run;
        return Update(note, MessageKind.Status, appendOutput: true);
    }

    internal View Send(string? name)
    {
        if (_run == null)
            return Message("Open a behaviour first.", MessageKind.Neutral);

        if (string.IsNullOrWhiteSpace(name))
            return Message("Choose an event to send.", MessageKind.Neutral);

        IReadOnlyList<GraphRun.Fired> fired;
        try
        {
            fired = _run.Send(name);
        }
        catch (ArgumentException e)
        {
            return Message(e.Message, MessageKind.Error);
        }

        int held = _run.HeldBack.Count;
        _log.Add(new LogEntry("Event: " + name));
        foreach (var move in fired)
            _log.Add(new LogEntry($"Transition: {move.Event} to {move.ToStateName}", move.ToStateId));

        string said = fired.Count == 0
            ? held == 0
                ? $"Sent {name}. Nothing in a running state was listening for it."
                : $"Sent {name}. Something was listening, but {held} transition(s) are held back by a condition."
            : $"Sent {name}. {fired.Count} transition(s) fired." +
              (held > 0 ? $" {held} other(s) held back by a condition." : "");

        return Update(said, MessageKind.Status, appendOutput: true);
    }

    internal string ValueText(string? name)
    {
        if (_run == null || string.IsNullOrEmpty(name)) return "";
        return _run.ValueOf(name) is double value
            ? value.ToString(CultureInfo.InvariantCulture)
            : "";
    }

    internal double? ValueOf(string name) => _run?.ValueOf(name);

    internal View SetVariable(string? name, string? text)
    {
        if (_run == null)
            return Message("Open a behaviour first.", MessageKind.Neutral);

        if (string.IsNullOrWhiteSpace(name))
            return Message("Choose a variable to set.", MessageKind.Neutral);

        if (!double.TryParse(text ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return Message($"'{text}' is not a number, so {name} was not changed.", MessageKind.Error);

        try
        {
            _run.Set(name, value);
        }
        catch (ArgumentException e)
        {
            return Message(e.Message, MessageKind.Error);
        }

        string shown = value.ToString(CultureInfo.InvariantCulture);
        return Update($"{name} is now {shown}. Send an event to see what that changes.",
                      MessageKind.Status, appendOutput: true);
    }

    internal View Advance(float seconds, string? noteOverride = null)
    {
        if (_run == null) return Snapshot();

        var fired = _run.Advance(seconds);
        string note;
        if (noteOverride != null)
        {
            note = noteOverride;
        }
        else
        {
            string shown = seconds.ToString("0.###", CultureInfo.InvariantCulture);
            note = fired.Count > 0
                ? $"Stepped {shown}s. {fired.Count} transition(s) fired because a clip reached " +
                  $"a point in itself: {string.Join(", ", fired.Select(f => f.Event).Distinct())}."
                : _run.Blending
                    ? $"Stepped {shown}s, still blending."
                    : $"Stepped {shown}s, blend finished.";
        }

        return Update(note, MessageKind.Status, appendOutput: true);
    }

    private View Message(string text, MessageKind kind)
    {
        _summary = text;
        _kind = kind;
        return Snapshot();
    }

    private View Update(string note, MessageKind kind, bool appendOutput)
    {
        if (_run == null) return Message(note, kind);
        if (appendOutput) AddOutput(note);

        var active = _run.Where();
        int machines = active.Count(a => !a.Fading);
        string blending = _run.Blending
            ? "  A transition is blending; Step to move it along."
            : "";

        _summary = $"{machines} machine(s) running.  {note}{blending}";
        _kind = kind;
        return Snapshot(active);
    }

    private void AddOutput(string text)
    {
        if (text.Length == 0) return;
        _output.Add(text);
        if (_output.Count > OutputLimit)
            _output.RemoveRange(0, _output.Count - OutputLimit);
    }

    private View Snapshot(IReadOnlyList<GraphRun.Active>? active = null)
    {
        if (_run == null)
        {
            return new View(
                false,
                false,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<GraphRun.Active>(),
                Array.Empty<GraphRun.Stop>(),
                Array.Empty<GraphRun.Blocked>(),
                _log.ToArray(),
                _output.ToArray(),
                0,
                _summary,
                _kind);
        }

        active ??= _run.Where();
        return new View(
            true,
            _run.Blending,
            _run.Events.ToArray(),
            _run.Variables.ToArray(),
            active.ToArray(),
            _run.Stops.ToArray(),
            _run.HeldBack.ToArray(),
            _log.ToArray(),
            _output.ToArray(),
            _run.Playing().Count(p => p.Clip.Known),
            _summary,
            _kind);
    }
}
