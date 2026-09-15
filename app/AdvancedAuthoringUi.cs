using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.App;

public static class AdvancedAuthoringUi
{
    public static void Attach(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var native = NativeStrip(window.Content as Control);
        if (native?.Child is not Panel row || row.Children.OfType<Button>().Any(b => b.Content?.ToString() == "Structure authoring")) return;

        var open = Ux.Secondary("Structure authoring");
        ToolTip.SetTip(open, "Author verified transition conditions, notify events, and state machines.");
        open.Click += (_, _) => new BehaviourStructureWindow(window).Present();
        row.Children.Insert(Math.Max(0, row.Children.Count - 1), open);
    }

    private static NativeAuthoringStrip? NativeStrip(Control? content)
    {
        var shell = EditorShell.Find(content);
        if (shell != null)
            return shell.Tools.Children.OfType<NativeAuthoringStrip>().FirstOrDefault();
        return content is DockPanel panel
            ? panel.Children.OfType<NativeAuthoringStrip>().FirstOrDefault()
            : null;
    }

    public static BehaviourStructureWindow OpenForTest(MainWindow window)
    {
        var editor = new BehaviourStructureWindow(window);
        editor.Present();
        return editor;
    }
}

public sealed class BehaviourStructureWindow : Window
{
    private sealed record TransitionOption(int ArrayId, int Index, string Label);
    private sealed record ObjectOption(int Id, string Label, string Class);
    private sealed record EventOption(int Id, string Label);

    private readonly MainWindow _owner;
    private readonly ComboBox _transitions = Box();
    private readonly ComboBox _states = Box();
    private readonly ComboBox _events = Box();
    private readonly ComboBox _notifyEvents = Box();
    private readonly ComboBox _generators = Box();
    private readonly ComboBox _parents = Box();
    private readonly TextBox _expression = Ux.Field("expression", 320);
    private readonly TextBox _eventName = Ux.Field("global event name", 220);
    private readonly TextBox _notifyIndex = Ux.Field("index", 70);
    private readonly TextBox _machineName = Ux.Field("machine name", 180);
    private readonly TextBox _firstState = Ux.Field("first state", 150);
    private readonly TextBox _parentField = Ux.Field("field", 150);
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private byte[]? _pendingBytes;

    public BehaviourStructureWindow(MainWindow owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Title = "BGS Structure Authoring";
        Width = 1050;
        Height = 720;
        MinWidth = 820;
        MinHeight = 560;
        Background = Ux.BaseBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _expression.Text = "bGateOpen > 0";
        _notifyIndex.Text = "0";
        _firstState.Text = "State 0";
        _parentField.Text = "rootGenerator";

        var conditionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        conditionRow.Children.Add(Ux.Label("Transition"));
        conditionRow.Children.Add(_transitions);
        conditionRow.Children.Add(_expression);
        conditionRow.Children.Add(Button("Create / assign condition", () => Apply(session =>
        {
            var option = Required<TransitionOption>(_transitions, "transition");
            var created = session.AddExpressionCondition(_expression.Text ?? "");
            session.SetTransitionCondition(option.ArrayId, option.Index, created.Id);
        })));
        conditionRow.Children.Add(Button("Clear condition", () => Apply(session =>
        {
            var option = Required<TransitionOption>(_transitions, "transition");
            session.SetTransitionCondition(option.ArrayId, option.Index, null);
        })));

        var eventRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        eventRow.Children.Add(Ux.Label("Global event"));
        eventRow.Children.Add(_eventName);
        eventRow.Children.Add(Button("Create event", () => Apply(session => session.AddEvent(_eventName.Text ?? ""))));
        eventRow.Children.Add(_events);

        var notifyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        notifyRow.Children.Add(Ux.Label("State"));
        notifyRow.Children.Add(_states);
        notifyRow.Children.Add(Ux.Label("Event"));
        notifyRow.Children.Add(_notifyEvents);
        notifyRow.Children.Add(Button("Add enter", () => AddNotify(BehaviourAuthoringSession.NotifyPhase.Enter)));
        notifyRow.Children.Add(Button("Add exit", () => AddNotify(BehaviourAuthoringSession.NotifyPhase.Exit)));
        notifyRow.Children.Add(_notifyIndex);
        notifyRow.Children.Add(Button("Remove enter", () => RemoveNotify(BehaviourAuthoringSession.NotifyPhase.Enter)));
        notifyRow.Children.Add(Button("Remove exit", () => RemoveNotify(BehaviourAuthoringSession.NotifyPhase.Exit)));

        var machineRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        machineRow.Children.Add(Ux.Label("New machine"));
        machineRow.Children.Add(_machineName);
        machineRow.Children.Add(_firstState);
        machineRow.Children.Add(Ux.Label("generator"));
        machineRow.Children.Add(_generators);
        machineRow.Children.Add(Ux.Label("parent"));
        machineRow.Children.Add(_parents);
        machineRow.Children.Add(_parentField);
        machineRow.Children.Add(Button("Create and attach", CreateMachine));

        var body = new StackPanel { Spacing = 10, Margin = new Thickness(16) };
        body.Children.Add(Ux.SectionTitle("Verified structure authoring"));
        body.Children.Add(new TextBlock
        {
            Text = "Each action runs the native writer, byte verification, and graph validation, then leaves one undoable edit in the main window. Save unsupported payloads and unknown layouts are refused.",
            Foreground = Ux.MetaBrush,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        body.Children.Add(Ux.SectionTitle("Transition conditions"));
        body.Children.Add(conditionRow);
        body.Children.Add(Ux.SectionTitle("Global event definitions"));
        body.Children.Add(eventRow);
        body.Children.Add(Ux.SectionTitle("State enter / exit notify arrays"));
        body.Children.Add(notifyRow);
        body.Children.Add(Ux.SectionTitle("State machines"));
        body.Children.Add(machineRow);
        body.Children.Add(Ux.Pill(_status));
        Content = new ScrollViewer { Content = body };
        Opened += (_, _) => Refresh();
    }

    public void Present()
    {
        if (!IsVisible) Show(_owner);
        Activate();
    }

    public string StatusForTest => _status.Text ?? "";

    public void CreateExpressionConditionForTest(string expression)
    {
        _expression.Text = expression;
        Apply(session =>
        {
            var option = Required<TransitionOption>(_transitions, "transition");
            var created = session.AddExpressionCondition(expression);
            session.SetTransitionCondition(option.ArrayId, option.Index, created.Id);
        });
    }

    public void AddEnterNotifyForTest(int eventIndex)
    {
        _notifyEvents.SelectedIndex = eventIndex;
        AddNotify(BehaviourAuthoringSession.NotifyPhase.Enter);
    }

    public void AddExitNotifyForTest(int eventIndex)
    {
        _notifyEvents.SelectedIndex = eventIndex;
        AddNotify(BehaviourAuthoringSession.NotifyPhase.Exit);
    }

    public void RemoveEnterNotifyForTest(int index)
    {
        _notifyIndex.Text = index.ToString(CultureInfo.InvariantCulture);
        RemoveNotify(BehaviourAuthoringSession.NotifyPhase.Enter);
    }

    public void RemoveExitNotifyForTest(int index)
    {
        _notifyIndex.Text = index.ToString(CultureInfo.InvariantCulture);
        RemoveNotify(BehaviourAuthoringSession.NotifyPhase.Exit);
    }

    public void CreateStateMachineForTest(string machineName, string firstStateName)
    {
        _machineName.Text = machineName;
        _firstState.Text = firstStateName;
        _generators.SelectedItem = _generators.Items.OfType<ObjectOption>()
            .First(option => option.Class.EndsWith("Generator", StringComparison.Ordinal));
        _parents.SelectedItem = _parents.Items.OfType<ObjectOption>()
            .First(option => option.Class == "hkbBehaviorGraph");
        CreateMachine();
    }

    private void AddNotify(BehaviourAuthoringSession.NotifyPhase phase)
    {
        Apply(session =>
        {
            var state = Required<ObjectOption>(_states, "state");
            var ev = Required<EventOption>(_notifyEvents, "event");
            session.AddNotifyEvent(state.Id, phase, ev.Id);
        });
    }

    private void RemoveNotify(BehaviourAuthoringSession.NotifyPhase phase)
    {
        if (!int.TryParse(_notifyIndex.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            Say("The notify index must be an integer.", Ux.BadBrush);
            return;
        }
        Apply(session => session.RemoveNotifyEvent(Required<ObjectOption>(_states, "state").Id, phase, index));
    }

    private void CreateMachine()
    {
        Apply(session =>
        {
            var generator = Required<ObjectOption>(_generators, "generator");
            var machine = session.AddStateMachine(_machineName.Text ?? "", _firstState.Text ?? "", generator.Id);
            var parent = Required<ObjectOption>(_parents, "parent");
            if (parent.Class == "hkbStateMachine")
                session.AddState(parent.Id, _machineName.Text ?? "", machine.Id);
            else
                session.AttachGenerator(parent.Id, _parentField.Text ?? "", machine.Id);
        });
    }

    private void Apply(Action<BehaviourAuthoringSession> edit)
    {
        try
        {
            if (_owner.LoadedXml.Length == 0) throw new InvalidOperationException("open an editable behaviour first");
            string path = _owner.PathFieldForTest;
            if (path.Length == 0 || !File.Exists(path)) throw new InvalidOperationException("the open behaviour file is unavailable");
            byte[] source;
            if (_pendingBytes != null && string.Equals(NativeXml.From(_pendingBytes), _owner.LoadedXml, StringComparison.Ordinal))
                source = _pendingBytes;
            else
            {
                if (_owner.IsDirty) throw new InvalidOperationException("the open document changed outside this authoring window");
                source = InputFilePolicy.ReadHkx(path);
                if (!string.Equals(NativeXml.From(source), _owner.LoadedXml, StringComparison.Ordinal))
                    throw new InvalidOperationException("the open document no longer matches the file on disk");
            }

            var session = new BehaviourAuthoringSession(source);
            edit(session);
            var result = session.Build();
            string xml = NativeXml.From(result.Bytes);
            _pendingBytes = result.Bytes;
            _owner.SetXmlForTest(xml);
            _owner.Canvas.Show(BehaviourGraphModel.Parse(xml));
            _owner.Canvas.FrameAll();
            Refresh();
            int warnings = result.Findings.Count(finding => finding.Level == GraphValidator.Level.Warning);
            Say($"Verified edit applied as one undo step. {warnings} graph warning{(warnings == 1 ? "" : "s")}. Save in the main window to write it.",
                warnings == 0 ? Ux.MetaBrush : Ux.WarnBrush);
        }
        catch (Exception error)
        {
            Say("Edit refused: " + error.Message.Split('\n')[0], Ux.BadBrush);
        }
    }

    private void Refresh()
    {
        var model = BehaviourGraphModel.Parse(_owner.LoadedXml);
        _transitions.ItemsSource = model.Objects.Where(o => o.Class == "hkbStateMachine")
            .SelectMany(machine => StateEditor.Transitions(model, machine.Id)
                .Select(row => new TransitionOption(int.Parse(row.ArrayId), row.Index,
                    $"#{row.ArrayId}[{row.Index}] {row.EventId} -> {row.ToStateId}"))).ToList();
        _states.ItemsSource = model.Objects.Where(o => o.Class == "hkbStateMachineStateInfo")
            .Select(o => new ObjectOption(int.Parse(o.Id), $"{o.Str("name")} #{o.Id}", o.Class)).ToList();
        var events = SymbolEditor.EventNames(model).Select((name, id) =>
            new EventOption(id, $"{id}: {name}")).ToList();
        _events.ItemsSource = events;
        _notifyEvents.ItemsSource = events.ToList();
        _generators.ItemsSource = model.Objects.Where(o => o.Class.EndsWith("Generator", StringComparison.Ordinal) ||
                o.Class == "hkbStateMachine")
            .Select(o => new ObjectOption(int.Parse(o.Id), $"{o.Str("name")} #{o.Id}", o.Class)).ToList();
        _parents.ItemsSource = model.Objects.Where(o => o.Class is "hkbBehaviorGraph" or "hkbStateMachine" or
            "hkbStateMachineStateInfo" or "hkbModifierGenerator")
            .Select(o => new ObjectOption(int.Parse(o.Id), $"{o.Class} #{o.Id}", o.Class)).ToList();
        SelectFirst(_transitions);
        SelectFirst(_states);
        SelectFirst(_events);
        SelectFirst(_notifyEvents);
        SelectFirst(_generators);
        SelectFirst(_parents);
    }

    private static void SelectFirst(ComboBox box)
    {
        if (box.SelectedItem == null && box.ItemCount > 0) box.SelectedIndex = 0;
    }

    private static ComboBox Box() => new() { MinWidth = 160, Foreground = Ux.CodeBrush, FontSize = 12 };

    private static Button Button(string text, Action action)
    {
        var button = Ux.Secondary(text);
        button.Click += (_, _) => action();
        return button;
    }

    private static T Required<T>(ComboBox box, string name) where T : class
        => box.SelectedItem as T ?? throw new InvalidOperationException($"choose a {name} first");

    private void Say(string text, Avalonia.Media.IBrush brush)
    {
        _status.Text = text;
        _status.Foreground = brush;
    }
}
