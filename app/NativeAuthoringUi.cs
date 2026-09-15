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

public sealed class NativeAuthoringStrip : Border
{
}

public static class NativeAuthoringUi
{
    public static void Attach(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.Content is not Control current) return;
        if (AlreadyAttached(current)) return;

        var open = Ux.Secondary("Batch authoring");
        ToolTip.SetTip(open, "Create native clip generators and states in one verified batch.");
        open.Click += (_, _) => new BatchAuthoringWindow(window).Present();

        var label = Ux.Label("Native authoring");
        label.Foreground = Ux.MutedBrush;

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        row.Children.Add(label);
        row.Children.Add(open);

        var strip = new NativeAuthoringStrip
        {
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(10, 0, 0, 0),
            Child = row,
        };

        var shell = EditorShell.Find(current);
        if (shell != null)
        {
            shell.Tools.Children.Add(strip);
            return;
        }

        window.Content = null;
        var host = new DockPanel { LastChildFill = true };
        strip.Background = Ux.BaseBrush;
        strip.BorderThickness = new Thickness(0, 0, 0, 1);
        strip.Padding = new Thickness(14, 5);
        DockPanel.SetDock(strip, Dock.Top);
        host.Children.Add(strip);
        host.Children.Add(current);
        window.Content = host;
    }

    private static bool AlreadyAttached(Control current) =>
        EditorShell.Find(current)?.HasTool<NativeAuthoringStrip>() == true
        || current is DockPanel host && host.Children.Any(child => child is NativeAuthoringStrip);

    public static BatchAuthoringWindow OpenBatchAuthoringForTest(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var batch = new BatchAuthoringWindow(window);
        batch.Present();
        return batch;
    }
}

public sealed class BatchAuthoringWindow : Window
{
    private sealed record MachineOption(int Id, string Name)
    {
        public override string ToString() => $"{Name}   #{Id}";
    }

    private sealed record Pending(
        string SourcePath,
        string BeforeXml,
        string AfterXml,
        BatchAnimationBuilder.Result Result,
        IReadOnlyList<NativeVariableBuilder.Created> Variables,
        BehaviourDiff.Result Diff);

    private readonly MainWindow _owner;
    private readonly ComboBox _machines = new()
    {
        MinWidth = 330,
        Foreground = Ux.CodeBrush,
        FontSize = 12,
    };
    private readonly TextBox _stateName = Ux.Field("state name", 170);
    private readonly TextBox _animationName = Ux.Field("animation path/name", 300);
    private readonly TextBox _bindingIndex = Ux.Field("binding", 75);
    private readonly TextBox _playbackSpeed = Ux.Field("speed", 75);
    private readonly HkGrid _queue = new(
        ("State", -3), ("Animation", -6), ("Binding", 70), ("Speed", 70));
    private readonly TextBox _variableName = Ux.Field("variable name", 200);
    private readonly ComboBox _variableType = new()
    {
        MinWidth = 110,
        Foreground = Ux.CodeBrush,
        FontSize = 12,
        ItemsSource = new[] { "int32", "real", "bool" },
        SelectedIndex = 0,
    };
    private readonly TextBox _variableValue = Ux.Field("initial", 90);
    private readonly HkGrid _variables = new(
        ("Variable", -4), ("Type", 90), ("Initial", 110));
    private readonly HkGrid _preview = new(
        ("Change", 75), ("Havok class", -3), ("Field or name", -3), ("Before", -4), ("After", -4));
    private readonly TextBlock _summary = new()
    {
        Foreground = Ux.MutedBrush,
        FontSize = 12,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
    };
    private readonly Button _apply = Ux.Primary("Apply as one undo step");
    private readonly List<BatchAnimationBuilder.Entry> _entries = new();
    private readonly List<NativeVariableBuilder.Entry> _variableEntries = new();
    private Pending? _pending;

    public BatchAuthoringWindow(MainWindow owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Title = "BGS Native Batch Authoring";
        Width = 1080;
        Height = 720;
        MinWidth = 820;
        MinHeight = 520;
        Background = Ux.BaseBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;

        _bindingIndex.Text = "-1";
        _playbackSpeed.Text = "1";

        var refresh = Ux.Secondary("Refresh machines");
        refresh.Click += (_, _) => RefreshMachines();

        var machineRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        machineRow.Children.Add(Ux.Label("State machine"));
        machineRow.Children.Add(_machines);
        machineRow.Children.Add(refresh);

        var add = Ux.Primary("Add to batch");
        add.Click += (_, _) => AddEntry();
        _animationName.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) AddEntry();
        };

        var entryRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        entryRow.Children.Add(_stateName);
        entryRow.Children.Add(_animationName);
        entryRow.Children.Add(_bindingIndex);
        entryRow.Children.Add(_playbackSpeed);
        entryRow.Children.Add(add);

        var addVariable = Ux.Primary("Add variable");
        addVariable.Click += (_, _) => AddVariable();
        _variableValue.Text = "0";
        _variableName.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) AddVariable();
        };

        var variableRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        variableRow.Children.Add(Ux.Label("Variable"));
        variableRow.Children.Add(_variableName);
        variableRow.Children.Add(_variableType);
        variableRow.Children.Add(_variableValue);
        variableRow.Children.Add(addVariable);

        var removeVariable = Ux.Secondary("Remove variable");
        removeVariable.Click += (_, _) => RemoveVariable();
        variableRow.Children.Add(removeVariable);

        var remove = Ux.Secondary("Remove selected");
        remove.Click += (_, _) => RemoveSelected();
        var clear = Ux.Secondary("Clear batch");
        clear.Click += (_, _) =>
        {
            _entries.Clear();
            _variableEntries.Clear();
            RefreshVariables();
            _pending = null;
            _apply.IsEnabled = false;
            RefreshQueue();
            _preview.Clear();
            Say("Batch cleared.", Ux.MutedBrush);
        };
        var preview = Ux.Secondary("Preview verified result");
        preview.Click += (_, _) => Preview();
        _apply.Click += (_, _) => Apply();
        _apply.IsEnabled = false;

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(remove);
        actions.Children.Add(clear);
        actions.Children.Add(preview);
        actions.Children.Add(_apply);

        var queueHeader = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(actions, Dock.Right);
        queueHeader.Children.Add(actions);
        queueHeader.Children.Add(Ux.SectionTitle("Batch"));

        var previewHeader = new DockPanel { LastChildFill = true };
        previewHeader.Children.Add(Ux.SectionTitle("Verified preview"));

        var body = new Grid { Margin = new Thickness(14) };
        for (int i = 0; i < 10; i++) body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions[4] = new RowDefinition(new GridLength(2, GridUnitType.Star));
        body.RowDefinitions[6] = new RowDefinition(new GridLength(1, GridUnitType.Star));
        body.RowDefinitions[8] = new RowDefinition(new GridLength(3, GridUnitType.Star));

        Add(body, machineRow, 0, new Thickness(0, 0, 0, 8));
        Add(body, entryRow, 1, new Thickness(0, 0, 0, 8));
        Add(body, variableRow, 2, new Thickness(0, 0, 0, 8));
        Add(body, queueHeader, 3, new Thickness(0, 4, 0, 5));
        Add(body, _queue, 4, new Thickness(0));
        Add(body, Ux.SectionTitle("Variables"), 5, new Thickness(0, 10, 0, 5));
        Add(body, _variables, 6, new Thickness(0));
        Add(body, previewHeader, 7, new Thickness(0, 10, 0, 5));
        Add(body, _preview, 8, new Thickness(0));
        Add(body, Ux.Pill(_summary), 9, new Thickness(0, 10, 0, 0));
        Content = body;

        Opened += (_, _) => RefreshMachines();
        Say("Choose a state machine, queue animations, then preview. Preview runs the native writer, byte verification, and graph validation without touching the file on disk.", Ux.MutedBrush);
    }

    public void Present()
    {
        if (!IsVisible) Show(_owner);
        Activate();
        Focus();
    }

    public IReadOnlyList<string> MachineNamesForTest =>
        _machines.ItemsSource is IEnumerable<MachineOption> options
            ? options.Select(option => option.Name).ToList()
            : Array.Empty<string>();

    public int QueuedForTest => _entries.Count;
    public bool HasPendingPreviewForTest => _pending != null;
    public bool ApplyOfferedForTest => _apply.IsEnabled;
    public string SummaryForTest => _summary.Text ?? "";

    public void RefreshMachinesForTest() => RefreshMachines();

    public void QueueForTest(string state, string animation)
    {
        _stateName.Text = state;
        _animationName.Text = animation;
        AddEntry();
    }

    public int QueuedVariablesForTest => _variableEntries.Count;

    public void QueueVariableForTest(string name, string type, string value)
    {
        _variableName.Text = name;
        _variableType.SelectedItem = type;
        _variableValue.Text = value;
        AddVariable();
    }

    public void PreviewForTest() => Preview();

    public void ApplyForTest() => Apply();

    private static void Add(Grid grid, Control control, int row, Thickness margin)
    {
        control.Margin = margin;
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    private void RefreshMachines()
    {
        string xml = _owner.LoadedXml;
        var options = xml.Length == 0
            ? new List<MachineOption>()
            : BehaviourGraphModel.Parse(xml).Objects
                .Where(o => o.Class == "hkbStateMachine")
                .Select(o => new MachineOption(
                    int.Parse(o.Id, CultureInfo.InvariantCulture),
                    o.Str("name").Length > 0 ? o.Str("name") : "hkbStateMachine"))
                .ToList();

        int? selected = (_machines.SelectedItem as MachineOption)?.Id;
        _machines.ItemsSource = options;
        _machines.SelectedItem = selected.HasValue
            ? options.FirstOrDefault(option => option.Id == selected.Value)
            : options.FirstOrDefault();

        if (options.Count == 0)
            Say("Open an editable behaviour containing an hkbStateMachine first.", Ux.MutedBrush);
    }

    private void AddEntry()
    {
        string state = (_stateName.Text ?? "").Trim();
        string animation = (_animationName.Text ?? "").Trim();
        if (state.Length == 0 || animation.Length == 0)
        {
            Say("State name and animation name are required.", Ux.BadBrush);
            return;
        }

        if (_entries.Any(entry => entry.Name.Equals(state, StringComparison.OrdinalIgnoreCase)))
        {
            Say($"State '{state}' is already in this batch.", Ux.BadBrush);
            return;
        }

        if (!int.TryParse((_bindingIndex.Text ?? "").Trim(), NumberStyles.Integer,
                          CultureInfo.InvariantCulture, out int binding))
        {
            Say("Binding index must be an integer. Use -1 for no animation binding.", Ux.BadBrush);
            return;
        }

        if (!float.TryParse((_playbackSpeed.Text ?? "").Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float speed) ||
            speed <= 0 || float.IsNaN(speed) || float.IsInfinity(speed))
        {
            Say("Playback speed must be a finite number greater than zero.", Ux.BadBrush);
            return;
        }

        _entries.Add(new BatchAnimationBuilder.Entry(state, animation, binding, speed));
        Invalidate();
        RefreshQueue();
        _stateName.Text = "";
        _animationName.Text = "";
        _stateName.Focus();
        Say($"Queued {state}. {_entries.Count} animation{(_entries.Count == 1 ? "" : "s")} in this batch.", Ux.MetaBrush);
    }

    private void AddVariable()
    {
        string name = (_variableName.Text ?? "").Trim();
        if (name.Length == 0)
        {
            Say("A variable needs a name.", Ux.BadBrush);
            return;
        }

        if (_variableEntries.Any(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            Say($"Variable '{name}' is already in this batch.", Ux.BadBrush);
            return;
        }

        var type = (_variableType.SelectedItem as string) switch
        {
            "real" => SymbolEditor.VariableType.Real,
            "bool" => SymbolEditor.VariableType.Bool,
            _ => SymbolEditor.VariableType.Int32,
        };

        string value = (_variableValue.Text ?? "").Trim();
        if (value.Length == 0) value = type == SymbolEditor.VariableType.Bool ? "false" : "0";
        if (!Parses(type, value))
        {
            Say($"'{value}' is not a valid {_variableType.SelectedItem} value.", Ux.BadBrush);
            return;
        }

        _variableEntries.Add(new NativeVariableBuilder.Entry(name, type, value));
        Invalidate();
        RefreshVariables();
        _variableName.Text = "";
        _variableName.Focus();
        Say($"Queued variable {name}. {_variableEntries.Count} variable{(_variableEntries.Count == 1 ? "" : "s")} in this batch.",
            Ux.MetaBrush);
    }

    private static bool Parses(SymbolEditor.VariableType type, string value) => type switch
    {
        SymbolEditor.VariableType.Int32 =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        SymbolEditor.VariableType.Real =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float real) &&
            !float.IsNaN(real) && !float.IsInfinity(real),
        SymbolEditor.VariableType.Bool => bool.TryParse(value, out _),
        _ => false,
    };

    private void RemoveVariable()
    {
        if (_variables.SelectedTag is not int index || index < 0 || index >= _variableEntries.Count)
        {
            Say("Select a queued variable first.", Ux.MutedBrush);
            return;
        }

        string name = _variableEntries[index].Name;
        _variableEntries.RemoveAt(index);
        Invalidate();
        RefreshVariables();
        Say($"Removed variable {name} from the batch.", Ux.MetaBrush);
    }

    private void RefreshVariables()
    {
        _variables.Clear();
        for (int i = 0; i < _variableEntries.Count; i++)
        {
            var entry = _variableEntries[i];
            _variables.Add(null, entry.Name, entry.Type.ToString().ToLowerInvariant(), entry.InitialValue)
                      .Tag(i)
                      .Colour(0, Ux.TitleBrush)
                      .Colour(1, Ux.CodeBrush)
                      .Colour(2, Ux.MetaBrush);
        }
    }

    private void Invalidate()
    {
        _pending = null;
        _apply.IsEnabled = false;
        _preview.Clear();
    }

    private void RemoveSelected()
    {
        if (_queue.SelectedTag is not int index || index < 0 || index >= _entries.Count)
        {
            Say("Select a queued animation first.", Ux.MutedBrush);
            return;
        }

        string name = _entries[index].Name;
        _entries.RemoveAt(index);
        Invalidate();
        RefreshQueue();
        Say($"Removed {name} from the batch.", Ux.MetaBrush);
    }

    private void RefreshQueue()
    {
        _queue.Clear();
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            _queue.Add(null, entry.Name, entry.AnimationName,
                       entry.BindingIndex.ToString(CultureInfo.InvariantCulture),
                       entry.PlaybackSpeed.ToString("0.###", CultureInfo.InvariantCulture))
                  .Tag(i)
                  .Colour(0, Ux.TitleBrush)
                  .Colour(1, Ux.CodeBrush)
                  .Colour(2, Ux.MetaBrush)
                  .Colour(3, Ux.MetaBrush);
        }
    }

    private void Preview()
    {
        try
        {
            _pending = BuildPreview();
            FillPreview(_pending.Diff);
            LoadMainComparePreview(_pending.Result.Bytes);
            _apply.IsEnabled = true;

            int warnings = _pending.Result.Findings.Count(finding =>
                finding.Level == GraphValidator.Level.Warning);
            Say($"Verified {_pending.Result.Created.Count} clip/state pair{(_pending.Result.Created.Count == 1 ? "" : "s")}" +
                (_pending.Variables.Count > 0
                     ? $" and {_pending.Variables.Count} variable{(_pending.Variables.Count == 1 ? "" : "s")}"
                     : "") + ". " +
                $"Diff: {_pending.Diff}. {warnings} graph warning{(warnings == 1 ? "" : "s")}. " +
                "Nothing has been written. The same preview is loaded in the main Compare tab.",
                warnings > 0 ? Ux.WarnBrush : Ux.MetaBrush);
        }
        catch (Exception error)
        {
            _pending = null;
            _apply.IsEnabled = false;
            _preview.Clear();
            Say("Preview refused: " + error.Message.Split('\n')[0], Ux.BadBrush);
        }
    }

    private Pending BuildPreview()
    {
        if (_entries.Count == 0 && _variableEntries.Count == 0)
            throw new InvalidOperationException("queue at least one animation or variable first");
        if (_entries.Count > 0 && _machines.SelectedItem is not MachineOption)
            throw new InvalidOperationException("choose a state machine first");
        if (_owner.LoadedXml.Length == 0)
            throw new InvalidOperationException("open an editable behaviour first");
        if (_owner.IsDirty)
            throw new InvalidOperationException("save or undo the current edits before starting a native batch");

        string path = _owner.PathFieldForTest;
        if (path.Length == 0 || !File.Exists(path))
            throw new InvalidOperationException("the open behaviour file is no longer available on disk");

        string sourcePath = Path.GetFullPath(path);
        byte[] source = InputFilePolicy.ReadHkx(sourcePath);
        string before = NativeXml.From(source);
        if (!string.Equals(before, _owner.LoadedXml, StringComparison.Ordinal))
            throw new InvalidOperationException("the open document no longer matches the file on disk; reload it before authoring");

        byte[] carried = source;
        IReadOnlyList<NativeVariableBuilder.Created> variables = Array.Empty<NativeVariableBuilder.Created>();
        var findings = new List<GraphValidator.Finding>();
        if (_variableEntries.Count > 0)
        {
            var built = NativeVariableBuilder.Build(carried, _variableEntries);
            carried = built.Bytes;
            variables = built.Created;
            findings.AddRange(built.Findings);
        }

        BatchAnimationBuilder.Result result;
        if (_entries.Count > 0)
        {
            var machine = (MachineOption)_machines.SelectedItem!;
            result = BatchAnimationBuilder.Build(carried, machine.Id, _entries);
        }
        else
        {
            result = new BatchAnimationBuilder.Result(
                carried, Array.Empty<BatchAnimationBuilder.Created>(), findings.ToList());
        }

        findings.AddRange(result.Findings);
        string after = NativeXml.From(result.Bytes);
        var diff = BehaviourDiff.Compare(RepackCheck.Take(before), RepackCheck.Take(after));
        if (diff.Identical)
            throw new InvalidOperationException("the batch produced no document changes");

        return new Pending(sourcePath, before, after,
                           result with { Findings = findings }, variables, diff);
    }

    private void Apply()
    {
        if (_pending == null)
        {
            Say("Preview the batch first.", Ux.MutedBrush);
            return;
        }

        if (_owner.IsDirty || !SamePath(_owner.PathFieldForTest, _pending.SourcePath) ||
            !string.Equals(_owner.LoadedXml, _pending.BeforeXml, StringComparison.Ordinal))
        {
            _pending = null;
            _apply.IsEnabled = false;
            Say("The open document or file changed after preview. Preview the batch again.", Ux.BadBrush);
            return;
        }

        string focus = _pending.Result.Created.LastOrDefault()?.State.ObjectId.ToString(CultureInfo.InvariantCulture) ?? "";
        _owner.SetXmlForTest(_pending.AfterXml);
        var model = BehaviourGraphModel.Parse(_pending.AfterXml);
        _owner.Canvas.Show(model);
        _owner.Canvas.FrameAll();
        if (focus.Length > 0)
        {
            _owner.Canvas.FocusOn(focus);
            _owner.SelectNode(focus);
        }

        int count = _pending.Result.Created.Count;
        int variables = _pending.Variables.Count;
        _pending = null;
        _apply.IsEnabled = false;
        Say($"Applied {count} clip/state pair{(count == 1 ? "" : "s")}" +
            (variables > 0 ? $" and {variables} variable{(variables == 1 ? "" : "s")}" : "") +
            " as one undo step. " +
            "The main document is unsaved. Use Save to write through the normal verified save transaction, or Undo to remove the whole batch.",
            Ux.MetaBrush);
    }

    private static bool SamePath(string current, string expected)
    {
        if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(expected)) return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(current),
                Path.GetFullPath(expected),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private void FillPreview(BehaviourDiff.Result diff)
    {
        _preview.Clear();
        foreach (var group in new[] { BehaviourDiff.Kind.Changed, BehaviourDiff.Kind.Removed, BehaviourDiff.Kind.Added })
        {
            var lines = diff.Lines.Where(line => line.Kind == group).ToList();
            if (lines.Count == 0) continue;

            var head = _preview.Add(null, group.ToString().ToLowerInvariant(), $"{lines.Count}")
                               .Colour(0, group == BehaviourDiff.Kind.Changed ? Ux.WarnBrush : Ux.CodeBrush)
                               .Colour(1, Ux.TitleBrush);
            foreach (var line in lines.Take(2000))
                _preview.Add(head, "", line.Class, line.Where, line.Was, line.Now)
                        .Colour(1, Ux.CodeBrush)
                        .Colour(2, Ux.TitleBrush)
                        .Colour(3, Ux.MetaBrush)
                        .Colour(4, Ux.MetaBrush);
        }
    }

    private void LoadMainComparePreview(byte[] bytes)
    {
        string folder = Path.Combine(Path.GetTempPath(), "BehaviourGraphStudio", "authoring-preview");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".hkx");
        try
        {
            File.WriteAllBytes(path, bytes);
            _owner.CompareLoadedWith(path);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private void Say(string text, Avalonia.Media.IBrush brush)
    {
        _summary.Text = text;
        _summary.Foreground = brush;
    }
}