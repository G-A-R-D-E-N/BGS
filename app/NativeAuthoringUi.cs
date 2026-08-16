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

public static class NativeAuthoringUi
{
    public static void Attach(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.Content is not Control current) return;

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

        var strip = new Border
        {
            Background = Ux.BaseBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 5),
            Child = row,
        };

        var host = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(strip, Dock.Top);
        host.Children.Add(strip);
        host.Children.Add(current);
        window.Content = host;
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

        var remove = Ux.Secondary("Remove selected");
        remove.Click += (_, _) => RemoveSelected();
        var clear = Ux.Secondary("Clear batch");
        clear.Click += (_, _) =>
        {
            _entries.Clear();
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
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(new GridLength(2, GridUnitType.Star)));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(new GridLength(3, GridUnitType.Star)));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Add(body, machineRow, 0, new Thickness(0, 0, 0, 8));
        Add(body, entryRow, 1, new Thickness(0, 0, 0, 8));
        Add(body, queueHeader, 2, new Thickness(0, 4, 0, 5));
        Add(body, _queue, 3, new Thickness(0));
        Add(body, previewHeader, 4, new Thickness(0, 10, 0, 5));
        Add(body, _preview, 5, new Thickness(0));
        Add(body, Ux.Pill(_summary), 6, new Thickness(0, 10, 0, 0));
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
        _pending = null;
        _apply.IsEnabled = false;
        _preview.Clear();
        RefreshQueue();
        _stateName.Text = "";
        _animationName.Text = "";
        _stateName.Focus();
        Say($"Queued {state}. {_entries.Count} animation{(_entries.Count == 1 ? "" : "s")} in this batch.", Ux.MetaBrush);
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
        _pending = null;
        _apply.IsEnabled = false;
        _preview.Clear();
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
            Say($"Verified {_pending.Result.Created.Count} clip/state pair{(_pending.Result.Created.Count == 1 ? "" : "s")}. " +
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
        if (_entries.Count == 0)
            throw new InvalidOperationException("queue at least one animation first");
        if (_machines.SelectedItem is not MachineOption machine)
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

        var result = BatchAnimationBuilder.Build(source, machine.Id, _entries);
        string after = NativeXml.From(result.Bytes);
        var diff = BehaviourDiff.Compare(RepackCheck.Take(before), RepackCheck.Take(after));
        if (diff.Identical)
            throw new InvalidOperationException("the batch produced no document changes");

        return new Pending(sourcePath, before, after, result, diff);
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
        _pending = null;
        _apply.IsEnabled = false;
        Say($"Applied {count} clip/state pair{(count == 1 ? "" : "s")} as one undo step. " +
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