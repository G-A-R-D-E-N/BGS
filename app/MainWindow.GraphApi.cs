using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCommonwealth.Services.Archive;
using OpenCommonwealth.Services.Hkx;
using OpenCommonwealth.Services.Nif;
using OpenCommonwealth.Services;

namespace BehaviourStudio.App;

public partial class MainWindow : Window
{
    private Control BuildLegend()
    {

        var body = new StackPanel { Spacing = 4 };

        void Heading(string text)
        {
            var title = Ux.SectionTitle(text);
            title.Margin = new Thickness(0, 10, 0, 2);
            body.Children.Add(title);
        }

        void Swatch(Control mark, string name, string what)
        {
            var words = new StackPanel { Spacing = 1 };
            var title = Ux.Label(name);
            title.Foreground = Ux.TitleBrush;
            words.Children.Add(title);

            var said = Ux.Label(what);
            said.Foreground = Ux.MutedBrush;
            said.FontSize = 11;
            said.TextWrapping = TextWrapping.Wrap;
            words.Children.Add(said);

            mark.Margin = new Thickness(0, 3, 8, 0);
            mark.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;

            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            Grid.SetColumn(mark, 0);
            Grid.SetColumn(words, 1);
            row.Children.Add(mark);
            row.Children.Add(words);
            body.Children.Add(row);
        }

        Control Box(string className) => new Border
        {
            Width = 20,
            Height = 12,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Ux.ForClass(className), 0.35),
            BorderBrush = new SolidColorBrush(Ux.ForClass(className)),
            BorderThickness = new Thickness(1.5),
        };

        Control Wire(Color colour, bool dashed) => new Avalonia.Controls.Shapes.Line
        {
            StartPoint = new Point(0, 6),
            EndPoint = new Point(20, 6),
            Stroke = new SolidColorBrush(colour),
            StrokeThickness = 2,
            StrokeDashArray = dashed ? new Avalonia.Collections.AvaloniaList<double> { 3, 2 } : null,
            Width = 20,
            Height = 12,
        };

        Heading("Boxes");
        Swatch(Box("hkbStateMachine"), "State machine",
               "A set of states with one of them active at a time.");
        Swatch(Box("hkbStateMachineStateInfo"), "State",
               "One of those. Holds whatever plays while it is the active one.");
        Swatch(Box("hkbStateMachineTransitionInfoArray"), "Transitions",
               "The list of ways out of a state. Click it to read them one at a time.");
        Swatch(Box("hkbClipGenerator"), "Clip",
               "Plays one animation file.");
        Swatch(Box("hkbBlenderGenerator"), "Blend",
               "Mixes several animations together by weight.");
        Swatch(Box("hkbModifierGenerator"), "Modifier",
               "Changes the pose after it has been made.");

        Heading("Lines");
        Swatch(Wire(Ux.ForClass("hkbStateMachine"), false), "Solid: holds",
               "The box at one end contains the box at the other. This is shape, not behaviour.");
        Swatch(Wire(Ux.RouteColour, true), "Dashed: transition",
               "Send the event written on it and the machine moves along the arrow. This is the " +
               "thing you cannot read anywhere else.");
        Swatch(Wire(Ux.Wildcard, true), "Dashed pink: from this state",
               "A wildcard, shown leaving the one state you highlighted. Only appears while a state " +
               "is picked out, because that is the only time it has one place to start.");

        Heading("Marks");
        Swatch(new TextBlock
        {
            Text = "any:",
            FontSize = 11,
            Foreground = new SolidColorBrush(Ux.Wildcard),
        }, "any: an event", "This state can be entered from any state of its machine, on that event. " +
                            "Written on the state rather than drawn as a line, because a wildcard " +
                            "fires from every state and so has no one place a line could start.");

        Swatch(new Border
        {
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Ux.Good),
            Padding = new Thickness(5, 2),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = "start",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = Ux.BaseBrush,
            },
        }, "Start", "The state its machine begins in. One per machine, at the top right of the box.");

        Swatch(new Border
        {
            Width = 20,
            Height = 12,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Ux.RouteColour, 0.30),
            BorderBrush = new SolidColorBrush(Ux.RouteColour),
            BorderThickness = new Thickness(2),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        }, "Teal glow: running now",
           "The state a machine is in right now, while the graph is being stepped. Send an event " +
           "with the box above the canvas and watch these move. Several light at once, because " +
           "several machines run at the same time.");

        Swatch(Wire(Ux.Bad, false), "Red outline",
               "Check graph found something wrong here. The list under the canvas says what.");
        Swatch(Wire(Ux.Warn, false), "Amber outline",
               "Check graph found something worth a look, but not an error.");

        Heading("Getting around");
        foreach (string tip in new[]
                 {
                     "Right click a state, then Highlight the paths of, to see every way out of it: " +
                     "its own transitions and its machine's wildcards, all leaving that state. " +
                     "Escape clears it.",
                     "Labels appear where there is room for them. Zoom in with the wheel to see more.",
                     "Drag with the middle button to move around. Double click a box to edit its fields.",
                 })
        {
            var line = Ux.Label(tip);
            line.Foreground = Ux.MetaBrush;
            line.FontSize = 11;
            line.TextWrapping = TextWrapping.Wrap;
            line.Margin = new Thickness(0, 3, 0, 0);
            body.Children.Add(line);
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new Border
            {
                Padding = new Thickness(12, 4, 12, 12),
                Child = body,
            },
        };
    }

    private void OnProblemSelected()
    {
        if (_problems.SelectedTag is not string id || id.Length == 0) return;
        if (_graph.FocusOn(id)) SelectObjectId(id);
        else SetStatus("That one is not drawn on the canvas.", Ux.MutedBrush);
    }

    private Control BuildTreeTab()
    {
        var expand = Ux.Secondary("Expand all");
        expand.Click += (_, _) => _tree.SetAllExpanded(true);
        var collapse = Ux.Secondary("Collapse all");
        collapse.Click += (_, _) => _tree.SetAllExpanded(false);

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3, GridUnitType.Star)));
        split.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        split.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(340, GridUnitType.Pixel)));

        var splitter = new GridSplitter { Width = 6, Background = Brushes.Transparent };
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(_treeProps, 2);
        split.Children.Add(_tree);
        split.Children.Add(splitter);
        split.Children.Add(_treeProps);
        return Rows((Bar(_filter, expand, collapse), false), (split, true));
    }

    public IReadOnlyList<string> ActivityIds => EditorShell.ActivityNames;
    public string SelectedActivity => _shell?.Activity ?? "";

    public HkGrid AnimationGrid => _animation;
    public string FramePageLabel => _framePage.Text ?? "";
    public int AnimationFrameCount => _animationData?.NumFrames ?? 0;
    public int AnimationTrackCount => _animationData?.Tracks.Count ?? 0;
    public int AnimationAnnotationCount => _animationData?.Annotations.Count ?? 0;
    public HkGrid SymbolGrid => _symbols;
    public string FractionAnswer => _fractionAnswer.Text ?? "";
    public int AimedFrame => _aimedFrame;

    public string FrameEditAnswer => _frameEditAnswer.Text ?? "";
    public string FramePositionText => _framePosition.Text ?? "";
    public string FrameRotationText => _frameRotation.Text ?? "";
    public bool AnimationEdited => _animationEdited;
    public System.Numerics.Vector3 FramePosition(int track, int frame) =>
        _animationData != null && track < _animationData.Tracks.Count &&
        frame < _animationData.Tracks[track].Translations.Count
            ? _animationData.Tracks[track].Translations[frame]
            : default;
    public string LoadedXml => _xmlText;
    public Inspector GraphProperties => _graphProps;
    public GraphView Canvas => _graph;
    public GraphLayoutMode GraphLayoutModeForTest => _graph.LayoutMode;

    public bool GraphLeftPanePresent => false;
    public bool GraphRightPaneOpen => _graphRightOpen;
    public bool GraphDrawerOpen => _graphDrawerOpen;
    public double GraphRightPaneWidth => _graphRightColumn.Width.Value;
    public double GraphDrawerHeight => _graphDrawerRow.Height.Value;
    public double GraphDrawerDefaultHeight => _graphDrawerHeight;
    public double GraphCenterMinWidth => _graphCenterColumn.MinWidth;
    public bool GraphDrawerContentsVisible => _graphDrawer?.IsVisible ?? false;
    public bool GraphEditShelfOpen => _graphEditShelf?.IsVisible ?? false;
    public bool GraphCanvasHostClips => _graphCanvasHost?.ClipToBounds ?? false;
    public bool GraphPropertiesHostClips => _graphPropertiesHost?.ClipToBounds ?? false;
    public IReadOnlyList<string> MachineNavigatorIds => _machineNavigatorIds;
    public IReadOnlyList<string> MachineNavigatorLabels => _machineNavigatorLabels;
    public IReadOnlyCollection<string> MachineNavigatorActiveIds => _machineNavigatorActiveIds;
    public bool GraphFocusTreeActive => _graph.FocusTreeActive;
    public string SelectedObjectId => _selectedId;
    public double GraphToolbarTopInset => 10;
    public IReadOnlyList<string> GraphToolbarGroups => _graphToolbarGroups;
    public bool GraphToolbarGroupLabelsHaveFixedLineHeight => _graphToolbarGroupLabelsHaveFixedLineHeight;
    public bool PlaybackViewportClips => (_playbackViewportHost?.ClipToBounds ?? false) && _skeleton.ClipToBounds;
    public IReadOnlyList<string> GraphDrawerTabs => _graphDrawerTabs?.Items.OfType<TabItem>()
        .Select(tab => tab.Header?.ToString() ?? "").ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
    public string SelectedGraphDrawerTab => _graphDrawerTabs?.SelectedItem is TabItem tab
        ? tab.Header?.ToString() ?? "" : "";
    public bool WorkspaceVisible => _workspaceWindow?.IsVisible ?? false;
    public bool WorkspaceRuntimeVisible => WorkspaceVisible;
    public int WorkspaceWindowInstances => _workspaceWindowInstances;
    public WorkspaceWindow? WorkspaceWindowForTest => _workspaceWindow;
    public bool LegendWindowVisible => _legendWindow?.IsVisible ?? false;
    public LegendWindow? LegendWindowForTest => _legendWindow;

    public void SetGraphEditShelfOpen(bool open)
    {
        if (_graphEditShelf != null) _graphEditShelf.IsVisible = open;
    }

    public void SelectGraphDrawerTab(string header)
    {
        if (_graphDrawerTabs == null) return;
        _graphDrawerTabs.SelectedItem = _graphDrawerTabs.Items.OfType<TabItem>()
            .FirstOrDefault(tab => tab.Header?.ToString() == header);
    }

    public void SetGraphRightPaneOpen(bool open)
    {
        _graphRightOpen = open;
        _graphProps.IsVisible = open;
        _graphRightColumn.MinWidth = open ? 360 : 0;
        _graphRightColumn.Width = Pixels(open ? _graphRightWidth : 0);
        _graphRightSplitterColumn.Width = Pixels(open ? 6 : 0);
        if (_graphRightSplitter != null) _graphRightSplitter.IsVisible = open;
    }

    public void SetGraphDrawerOpen(bool open)
    {
        _graphDrawerOpen = open;
        _graphDrawerRow.MinHeight = open ? 80 : 0;
        _graphDrawerRow.Height = Pixels(open ? _graphDrawerHeight : 0);
        _graphDrawerSplitterRow.Height = Pixels(open ? 6 : 0);
        if (_graphDrawer != null) _graphDrawer.IsVisible = open;
        if (_graphDrawerSplitter != null) _graphDrawerSplitter.IsVisible = open;
        if (_drawerButton != null) _drawerButton.Content = open ? "Hide diagnostics" : "Show diagnostics";
    }

    public void ResizeGraphRightPaneForTest(double width)
    {
        _graphRightWidth = Math.Clamp(width, 360, 480);
        if (_graphRightOpen) _graphRightColumn.Width = Pixels(_graphRightWidth);
    }

    public void ResizeGraphDrawerForTest(double height)
    {
        _graphDrawerHeight = Math.Clamp(height, 80, 300);
        if (_graphDrawerOpen) _graphDrawerRow.Height = Pixels(_graphDrawerHeight);
    }

    public bool SelectMachineForTest(string machineId) => _machineNavigator.SelectByTag(machineId);

    public void FilterMachinesForTest(string text)
    {
        OpenWorkspaceWindow();
        _workspaceWindow?.FilterMachinesForTest(text);
    }

    public void FocusTreeForTest() => FocusSelectedMachine();

    public void ShowFullGraphForTest() => ShowFullGraph();

    public void SetGraphLayoutModeForTest(GraphLayoutMode mode) => SetGraphLayoutMode(mode);

    public void ClearRunForTest()
    {
        _runEvents.ItemsSource = null;
        _runVariables.ItemsSource = null;
        RenderRun(_runSession.Clear());
    }

    public void StartRunForTest() => StartRun();

    private static GridLength Pixels(double value) => new(value, GridUnitType.Pixel);

    private void OpenWorkspaceWindow()
    {
        if (_workspaceWindow == null)
        {
            _machineNavigator.SelectionChanged += OnMachineNavigatorSelected;
            _workspaceWindow = new WorkspaceWindow(_machineNavigator, BuildWorkspaceRuntimeTab(),
                                                     FilterMachines);
            _workspaceWindowInstances++;
        }
        _workspaceWindow.Present(this);
    }

    private void OpenLegendWindow()
    {
        _legendWindow ??= new LegendWindow(BuildLegend());
        _legendWindow.Present(this);
    }

    public void OpenWorkspaceForTest() => OpenWorkspaceWindow();

    public void CloseWorkspaceForTest() => _workspaceWindow?.CloseForTest();

    public void OpenLegendForTest() => OpenLegendWindow();

    public bool RunReady => _runSession.Current.Ready;
    public bool RunBlending => _runSession.Current.Blending;
    public int RunEventCount => _runSession.Current.Events.Count;
    public IReadOnlyList<string> RunEvents => _runSession.Current.Events;

    public void StepForTest(float seconds) =>
        RenderRun(_runSession.Advance(seconds, "stepped"));

    public int TimedClipCount => _runSession.Current.TimedClipCount;
    public int RunningCount => _running.RowCount;
    public bool RunningVisible => _running.IsVisible;

    public void SendEventForTest(string name)
    {
        _runEvents.SelectedItem = name;
        SendRunEvent();
    }

    public IReadOnlyList<string> RunVariables => _runSession.Current.Variables;
    public int RunHeldBack => _runSession.Current.HeldBack.Count;
    public bool RunHeldBackVisible => _runHeldBackGrid.RowCount > 0;
    public string RunHeldBackText => _runHeldBack.Text ?? "";
    public string RunSummary => _runSummary.Text ?? "";
    public double? RunValueOf(string name) => _runSession.ValueOf(name);

    public void SetVariableForTest(string name, string value)
    {
        _runVariables.SelectedItem = name;
        _runValue.Text = value;
        SetRunVariable();
    }

    public void SelectNode(string objectId) => SelectObjectId(objectId);

    public bool SelectFromTree(string objectId)
    {
        int index = _objectIds.IndexOf(objectId);
        if (index < 0) return false;

        foreach (var (offset, at) in _offsetToIndex)
            if (at == index) return _tree.SelectByTag(offset);
        return false;
    }

    public void LookUpFraction(string typed)
    {
        _fraction.Text = typed;
        AimAtFraction();
    }

    public void FilterBones(string needle)
    {
        _boneFilter.Text = needle;
        ShowAnimationFrames();
    }

    public bool PickFrame(int track, int frame)
    {
        bool found = _animation.SelectByTag($"f:{track}:{frame}");
        if (found) ShowSelectedFrame();
        return found;
    }

    public void TypeFramePosition(string text)
    {
        _framePosition.Text = text;
        SetFrame();
    }

    public void PressSaveAnimation() => SaveAnimation();

    public bool SaveCurrentForTest() => SaveCurrent();

    public void SaveForTest() => Save();
    public Func<Exception>? ReloadFaultForTest;
    public Func<Exception>? VerifyFaultForTest;
    public Func<DiscardChoice>? DiscardDecision;
    public void SetXmlForTest(string xml) => Commit(xml);

    public int ProblemCount => _problems.RowCount;

    public Task ValidateProjectForTest() => ValidateProject();
    public Task ScanPapyrusForTest(string folder) => ScanPapyrusFolder(folder, null);
    public void MarkAnimationEditedForTest() => _animationEdited = true;
    public bool IsDirty => _dirty;
    public int UndoStepsForTest => _undo.Count;
    public int RedoStepsForTest => _redo.Count;
    public void UndoForTest() => Undo();
    public string StatusForTest => _status.Text ?? "";

    public string ScaleStatusForTest => _scaleStatus.Text ?? "";

    public bool ScaleStatusVisibleForTest => _scalePill?.IsVisible == true;

    public string FrameDistanceStatusForTest => _frameDistanceStatus.Text ?? "";

    public bool FrameDistanceVisibleForTest => _frameDistancePill?.IsVisible == true;

    private void UpdateScaleStatus()
    {
        if (_scalePill == null) return;
        string text = _skeleton.ScaleStatusText;
        _scaleStatus.Text = text;
        _scaleStatus.Foreground = Ux.MetaBrush;
        _scalePill.IsVisible = text.Length > 0;
    }

    private void ShowFrameDistance(string? text)
    {
        _frameDistanceStatus.Text = text ?? "";
        _frameDistanceStatus.Foreground = Ux.MetaBrush;
        if (_frameDistancePill != null) _frameDistancePill.IsVisible = text?.Length > 0;
        _clearFrameDistanceButton.IsEnabled = text?.Length > 0;
    }
    public string PathFieldForTest => _pathField.Text ?? "";

}
