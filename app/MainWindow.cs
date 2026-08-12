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
using OpenCommonwealth.Services.Hkx;
using OpenCommonwealth.Services.Nif;

namespace BehaviourStudio.App;

public class MainWindow : Window
{
    private const int MaxTreeRows = 4000;
    private const int FramesPerPage = 300;

    private readonly TextBox _pathField = Ux.Field("absolute path to a .hkx behaviour, character or project file");
    private readonly TextBox _filter = Ux.Field("filter objects by name, class or animation");
    private readonly TextBlock _summary = new() { Foreground = Ux.MutedBrush, FontSize = 12 };
    private readonly TextBlock _status = new() { Foreground = Ux.MutedBrush, FontSize = 12 };
    private readonly Inspector _treeProps = new(340);
    private readonly Inspector _graphProps = new(360);
    private readonly GraphView _graph = new();
    private readonly ColumnDefinition _graphCenterColumn = new(new GridLength(1, GridUnitType.Star)) { MinWidth = 720 };
    private readonly ColumnDefinition _graphRightSplitterColumn = new(new GridLength(6, GridUnitType.Pixel));
    private readonly ColumnDefinition _graphRightColumn = new(new GridLength(380, GridUnitType.Pixel))
        { MinWidth = 360, MaxWidth = 480 };
    private readonly RowDefinition _graphDrawerSplitterRow = new(new GridLength(0, GridUnitType.Pixel));
    private readonly RowDefinition _graphDrawerRow = new(new GridLength(0, GridUnitType.Pixel))
        { MaxHeight = 300 };
    private GridSplitter? _graphRightSplitter;
    private GridSplitter? _graphDrawerSplitter;
    private Control? _graphDrawer;
    private Control? _graphEditShelf;
    private TabControl? _graphDrawerTabs;
    private Border? _graphCanvasHost;
    private Border? _graphPropertiesHost;
    private Border? _playbackViewportHost;
    private readonly List<string> _graphToolbarGroups = new();
    private bool _graphToolbarGroupLabelsHaveFixedLineHeight;
    private WorkspaceWindow? _workspaceWindow;
    private LegendWindow? _legendWindow;
    private int _workspaceWindowInstances;
    private Button? _drawerButton;
    private readonly HkGrid _machineNavigator = new(("Machine", -4), ("ID", 70), ("Run", 62));
    private bool _machineNavigatorRebuilding;
    private readonly List<string> _machineNavigatorIds = new();
    private readonly List<string> _machineNavigatorLabels = new();
    private readonly HashSet<string> _machineNavigatorActiveIds = new(StringComparer.Ordinal);
    private bool _graphRightOpen = true;
    private bool _graphDrawerOpen;
    private double _graphRightWidth = 380;
    private double _graphDrawerHeight = 110;
    private readonly Button _saveButton;
    private readonly Button _undoButton;
    private readonly Button _redoButton;

    private readonly HkGrid _tree = new(("Node", -4), ("Havok class", -3), ("Animation", -4), ("Offset", 90));
    private readonly HkGrid _symbols =
        new(("Kind", 60), ("Index", 55), ("Name", -4), ("Initial value", -2), ("Used by, in this file", -5));
    private readonly HkGrid _chain = new(("Role", 110), ("Declared in the file", -4), ("On disk", 80), ("Notes", -3));
    private readonly HkGrid _clips = new(("Clip", -5), ("Plays", -6));
    private readonly Inspector _clipProps = new(320);
    private readonly HkGrid _animation =
        new(("Bone or track", -4), ("Frame", 70), ("Time", 80), ("Position", -4), ("Rotation", -5),
            ("Scale", -3));
    private readonly TextBlock _animationSummary =
        new() { Foreground = Ux.MetaBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _framePage = new() { Foreground = Ux.MetaBrush, FontSize = 12 };
    private readonly TextBox _boneFilter = Ux.Field("bone", 150);
    private readonly TextBox _fraction = Ux.Field("0.0 to 1.0", 110);
    private readonly TextBlock _fractionAnswer = new() { Foreground = Ux.MetaBrush, FontSize = 12 };
    private readonly TextBox _framePosition = Ux.Field("x, y, z", 170);
    private readonly TextBox _frameRotation = Ux.Field("x, y, z, w", 210);
    private readonly TextBox _frameScale = Ux.Field("x, y, z", 150);
    private readonly TextBlock _frameEditAnswer = new() { Foreground = Ux.MetaBrush, FontSize = 12 };
    private HkxAnimationData? _animationData;
    private HkxSkeleton? _animationSkeleton;
    private int _frameStart;
    private int _aimedFrame = -1;




    private int _editTrack = -1, _editFrame = -1;




    private bool _animationEdited;

    private readonly TextBox _symbolName = Ux.Field("name", 170);
    private readonly TextBox _symbolValue = Ux.Field("value, for a variable", 130);
    private readonly TextBox _symbolMin = Ux.Field("min", 80);
    private readonly TextBox _symbolMax = Ux.Field("max", 80);
    private readonly TextBlock _symbolAudit = new() { Foreground = Ux.MetaBrush, FontSize = 12 };
    private PapyrusEvents.Index _papyrus = new();
    private bool _papyrusScanned;

    private readonly SkeletonView _skeleton = new();
    private readonly TextBlock _playbackSummary =
        new() { Foreground = Ux.MetaBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _frameLabel = new() { Foreground = Ux.MetaBrush, FontSize = 12 };
    private readonly Slider _scrub = new() { Minimum = 0, Maximum = 0, SmallChange = 1, LargeChange = 5 };
    private Button _playButton = Ux.Secondary("Play");
    private HkxSkeleton? _poseSkeleton;
    private HkxAnimationData? _poseAnimation;
    private string _poseSource = "";





    private RootMotion.Motion _poseMotion = new();
    private bool _followTravel;
    private int _poseFrame;
    private bool _scrubbing;
    private DispatcherTimer? _clock;
    private HkxSkeleton? _cachedSkeleton;
    private string _cachedSkeletonFor = "";




    private readonly List<(NifShape Shape, SkinnedMesh.Binding Binding, List<(int From, int To)> Edges)>
        _meshShapes = new();
    private string _meshPath = "";

    private readonly HkGrid _diff =
        new(("Change", 80), ("Havok class", -3), ("Field or name", -3), ("In the open file", -4),
            ("In the other file", -4));
    private readonly TextBlock _diffSummary =
        new() { Foreground = Ux.MetaBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap };

    private readonly HkGrid _problems = new(("", 70), ("Object", -3), ("What is wrong", -7));
    private readonly TextBlock _problemBar = new() { Foreground = Ux.MetaBrush, FontSize = 12, Margin = new Thickness(2, 6, 2, 2) };

    private long _documentStamp;                 // bumped on every document change
    private DocumentSourceStamp? _sourceStamp;
    private long CaptureStamp() => _documentStamp;

    public Func<ProjectChain, IProgress<string>, Task<ProjectCheck.Result>>? ValidateProjectRunner;
    public Func<string, Task<PapyrusEvents.Index>>? PapyrusScanRunner;


    private GraphRun? _run;
    private readonly ComboBox _runEvents = new()
        { MinWidth = 190, MaxWidth = 260, Foreground = Ux.CodeBrush, FontSize = 12 };
    private readonly TextBlock _runSummary = new()
        { Foreground = Ux.MetaBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
          TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 0, 0, 0) };
    private readonly TextBlock _runtimeStatus = new()
        { Foreground = Ux.MetaBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap,
          HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 700 };
    private readonly HkGrid _running = new(("Machine", -4), ("Is in state", -4), ("Weight", 70));
    private readonly HkGrid _runStopsGrid = new(("Stops", -3), ("Why", -6));
    private readonly HkGrid _runHeldBackGrid = new(("Held back", -3), ("Condition", -6));
    private readonly HkGrid _runLog = new(("Event and transition log", -1));
    private readonly TextBox _runOutput = new()
    {
        IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        Background = Ux.CardBrush, Foreground = Ux.MetaBrush, BorderBrush = Ux.BorderBrush,
        BorderThickness = new Thickness(1), Padding = new Thickness(8), FontSize = 12,
    };
    private readonly List<string> _runOutputLines = new();
    private readonly TextBlock _runStops = new()
        { Foreground = Ux.WarnBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap,
          Margin = new Thickness(2, 4, 2, 2) };
    private Button _step = Ux.Secondary("Step 0.1s");







    private readonly ComboBox _runVariables = new()
        { MinWidth = 170, MaxWidth = 230, Foreground = Ux.CodeBrush, FontSize = 12 };
    private readonly TextBox _runValue = new()
        { Width = 80, Foreground = Ux.CodeBrush, FontSize = 12, Watermark = "value" };
    private readonly Button _setRunVariable = Ux.Secondary("Set variable");
    private readonly TextBlock _runHeldBack = new()
        { Foreground = Ux.WarnBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap,
          Margin = new Thickness(2, 4, 2, 2) };




    private static NativePaste.Clip? _clip;
    private readonly ComboBox _pasteInto = new()
        { MinWidth = 210, MaxWidth = 300, Foreground = Ux.CodeBrush, FontSize = 12 };
    private readonly TextBlock _pasteSummary = new()
        { Foreground = Ux.MetaBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
          TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 0, 0, 0) };
    private Button _pasteButton = Ux.Primary("Paste subtree");

    private readonly TextBox _templateName = Ux.Field("template name", 150);
    private readonly ComboBox _templates = new()
        { MinWidth = 210, MaxWidth = 300, Foreground = Ux.CodeBrush, FontSize = 12 };
    private Button _applyTemplate = Ux.Secondary("Apply template");
    private readonly ComboBox _predefinedTemplates = new()
        { MinWidth = 190, MaxWidth = 250, Foreground = Ux.CodeBrush, FontSize = 12 };
    private readonly StackPanel _predefinedSlots = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly Dictionary<string, Control> _predefinedValues = new(StringComparer.Ordinal);
    private readonly Button _applyPredefinedTemplate = Ux.Primary("Create template");

    private readonly Dictionary<int, int> _offsetToIndex = new();
    private HashSet<string> _emptyStates = new();
    private List<string> _objectIds = new();



    private PackfileObjects? _bytes;




    private readonly HashSet<string> _editedFields = new(StringComparer.Ordinal);



    private string _classWarning = "";
    private List<HkxBehaviorParser.BehaviorNode> _objects = new();
    private HkxBehaviorParser.BehaviorNode? _root;

    private string _hkxPath = "";
    private bool _closeApproved;
    private bool _reloading;




    private bool _readOnly;
    private string _readOnlyWhy = "";
    private string _xmlPath = "";
    private string _xmlText = "";
    private ProjectChain? _projectChain;
    private string _selectedId = "";
    private readonly List<Action> _fieldCommits = new();
    private bool _dirty;





    private const int UndoDepth = 100;
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private string _savedXml = "";

    public MainWindow()
    {
        Title = "Behaviour Graph Studio";
        Width = 1500;
        Height = 940;
        Background = Ux.BaseBrush;

        _pathField.Text = Settings.Get("last_path");

        var open = Ux.Primary("Open");
        open.Click += (_, _) => Load();
        var browse = Ux.Secondary("Browse...");
        browse.Click += async (_, _) => await Browse();
        var archive = Ux.Secondary("From archive...");
        archive.Click += async (_, _) => await OpenFromArchive();
        _pathField.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Load(); };

        var expand = Ux.Secondary("Expand all");
        expand.Click += (_, _) => _tree.SetAllExpanded(true);
        var collapse = Ux.Secondary("Collapse all");
        collapse.Click += (_, _) => _tree.SetAllExpanded(false);
        _filter.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) ApplyFilter(); };
        _filter.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) JumpToFirstMatch(); };

        var check = Ux.Secondary("Check graph");
        check.Click += (_, _) => Validate();
        var checkProject = Ux.Secondary("Check project");
        checkProject.Click += async (_, _) => await ValidateProject();
        _saveButton = Ux.Primary("Save to .hkx");
        _saveButton.IsEnabled = false;
        _saveButton.Click += (_, _) => Save();

        _undoButton = Ux.Secondary("Undo");
        _undoButton.IsEnabled = false;
        _undoButton.Click += (_, _) => Undo();
        ToolTip.SetTip(_undoButton, "Ctrl+Z");
        _redoButton = Ux.Secondary("Redo");
        _redoButton.IsEnabled = false;
        _redoButton.Click += (_, _) => Redo();
        ToolTip.SetTip(_redoButton, "Ctrl+Y");

        KeyDown += OnWindowKey;

        _tree.SelectionChanged += OnTreeSelected;
        _symbols.SelectionChanged += OnSymbolSelected;

        _graph.Selected += SelectObjectId;
        _graph.Activated += id => { SelectObjectId(id); _graphProps.FocusFirstField(); };
        _graph.LinkRequested += (from, field, to) => Relink(from, field, to, connect: true);
        _graph.UnlinkRequested += (from, field, to) => Relink(from, field, to, connect: false);
        _graph.DeleteRequested += DeleteNode;
        _graph.Refused += message => SetStatus(message, Ux.MutedBrush);
        _graph.AddRequested += ShowAddMenu;
        _graph.LayoutChanged += SaveCurrentGraphLayout;

        var tabs = new TabControl { Padding = new Thickness(0, 8, 0, 0) };
        tabs.Items.Add(Tab("Tree", BuildTreeTab()));
        tabs.Items.Add(Tab("Graph", BuildGraphTab()));
        tabs.Items.Add(Tab("Symbols", BuildSymbolsTab()));
        tabs.Items.Add(Tab("Chain", _chain));
        tabs.Items.Add(Tab("Animation", BuildAnimationTab()));
        tabs.Items.Add(Tab("Playback", BuildPlaybackTab()));
        tabs.Items.Add(Tab("Compare", BuildDiffTab()));

        Content = new Border
        {
            Padding = new Thickness(14),
            Child = Rows(
                (Ux.SectionTitle("Havok behaviour file"), false),
                (Bar(_pathField, browse, archive, open), false),
                (Ux.Pill(_summary), false),
                (Bar(_filter, expand, collapse), false),
                (tabs, true),
                (Bar(Ux.Pill(_status), _undoButton, _redoButton, checkProject, check, _saveButton), false)),
        };

        SetSummary("No file loaded.", Ux.MutedBrush);
        SetStatus("Open a behaviour file to start.", Ux.MutedBrush);
    }

    private static TabItem Tab(string header, Control content) => new()
    {
        Header = header,
        Content = content,
        Foreground = Ux.MetaBrush,
        FontSize = 12,
    };

    private static Border Framed(Control content) => new()
    {
        Background = Ux.CardBrush,
        BorderBrush = Ux.BorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Child = content,
    };

    private Border GraphToolbarGroup(string name, params Control[] controls)
    {
        var label = new TextBlock
        {
            Text = name.ToUpperInvariant(),
            Foreground = Ux.MutedBrush,
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            LineHeight = 14,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        var tag = new Border
        {
            Background = Ux.BaseBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(8, 2),
            Child = label,
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(tag);
        foreach (var control in controls)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            if (control is Button or ComboBox) control.MinHeight = 28;
            row.Children.Add(control);
        }

        _graphToolbarGroups.Add(name);
        _graphToolbarGroupLabelsHaveFixedLineHeight = true;
        return new Border
        {
            Background = Ux.CardBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(1, 1, 6, 1),
            Child = row,
        };
    }



    private static Control Bar(params Control[] controls)
    {
        var panel = new DockPanel { LastChildFill = true };
        for (int i = controls.Length - 1; i >= 1; i--)
        {
            controls[i].Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(controls[i], Dock.Right);
            panel.Children.Add(controls[i]);
        }
        panel.Children.Add(controls[0]);
        return panel;
    }

    private static Control Rows(params (Control Control, bool Fill)[] rows)
    {
        var grid = new Grid();
        for (int i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(rows[i].Fill ? GridLength.Star : GridLength.Auto));
            rows[i].Control.Margin = new Thickness(0, i == 0 ? 0 : 10, 0, 0);
            Grid.SetRow(rows[i].Control, i);
            grid.Children.Add(rows[i].Control);
        }
        return grid;
    }




    private Control BuildGraphTab()
    {
        _problems.SelectionChanged += OnProblemSelected;




        var fitAll = Ux.Secondary("Fit all");
        fitAll.Click += (_, _) =>
        {
            _graph.ClearHighlight();
            _graph.FrameAll();
        };




        var fitPicked = Ux.Secondary("Fit selection");
        fitPicked.Click += (_, _) =>
        {
            if (_graph.SelectedId.Length > 0 && _graph.HighlightId.Length == 0)
                HighlightPaths(_graph.SelectedId);
            _graph.FrameRelated();
        };

        Control view = BuildViewMenu(fitAll, fitPicked);
        _graphToolbarGroups.Clear();
        _graphToolbarGroupLabelsHaveFixedLineHeight = false;
        view = GraphToolbarGroup("View", view, fitAll, fitPicked);
        var runBar = BuildRunControls();
        var pasteBar = BuildPasteControls();
        _graphEditShelf = Framed(pasteBar);
        _graphEditShelf.IsVisible = false;
        var edit = Ux.Secondary("Edit tools");
        edit.Click += (_, _) => SetGraphEditShelfOpen(!_graphEditShelf.IsVisible);
        var editGroup = GraphToolbarGroup("Edit", edit);
        var simulation = GraphToolbarGroup("Simulation", runBar);

        var toolbarLeft = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        toolbarLeft.Children.Add(view);
        toolbarLeft.Children.Add(editGroup);
        toolbarLeft.Children.Add(simulation);
        var toolbar = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(toolbarLeft, Dock.Left);
        toolbar.Children.Add(toolbarLeft);
        toolbar.Children.Add(_runSummary);
        var toolbarHost = new Border
        {
            Padding = new Thickness(0, 10, 0, 8),
            Child = toolbar,
        };

        _graphRightSplitter = new GridSplitter { Width = 6, Background = Ux.BorderBrush,
            ResizeDirection = GridResizeDirection.Columns };

        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(_graphCenterColumn);
        workspace.ColumnDefinitions.Add(_graphRightSplitterColumn);
        workspace.ColumnDefinitions.Add(_graphRightColumn);
        _graphCanvasHost = Framed(_graph);
        _graphCanvasHost.ClipToBounds = true;
        Grid.SetColumn(_graphCanvasHost, 0);
        Grid.SetColumn(_graphRightSplitter, 1);
        _graphProps.SetHeaderAction("Collapse", () => SetGraphRightPaneOpen(false));
        _graphPropertiesHost = Framed(_graphProps);
        _graphPropertiesHost.ClipToBounds = true;
        Grid.SetColumn(_graphPropertiesHost, 2);
        workspace.Children.Add(_graphCanvasHost);
        workspace.Children.Add(_graphRightSplitter);
        workspace.Children.Add(_graphPropertiesHost);

        var problems = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_problemBar, Dock.Top);
        problems.Children.Add(_problemBar);
        problems.Children.Add(_problems);

        _graphDrawerTabs = new TabControl { Padding = new Thickness(6, 0, 6, 0) };
        _graphDrawerTabs.Items.Add(Tab("Problems", problems));
        _graphDrawerTabs.Items.Add(Tab("Output", _runOutput));

        var drawer = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6), IsVisible = false,
            ClipToBounds = true };
        _graphDrawer = drawer;
        drawer.Children.Add(_graphDrawerTabs);

        _drawerButton = Ux.Secondary("Show diagnostics");
        _drawerButton.Click += (_, _) => SetGraphDrawerOpen(!_graphDrawerOpen);
        ToolTip.SetTip(_drawerButton, "Show validation findings and diagnostic output below the graph.");
        var drawerBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            Margin = new Thickness(0, 8, 0, 0),
        };
        drawerBar.Children.Add(_drawerButton);

        _graphDrawerSplitter = new GridSplitter { Height = 6, Background = Ux.BorderBrush,
            ResizeDirection = GridResizeDirection.Rows, IsVisible = false };

        var graphWorkspace = new Grid();
        graphWorkspace.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        graphWorkspace.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        graphWorkspace.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        graphWorkspace.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        graphWorkspace.RowDefinitions.Add(_graphDrawerSplitterRow);
        graphWorkspace.RowDefinitions.Add(_graphDrawerRow);
        Grid.SetRow(toolbarHost, 0);
        Grid.SetRow(_graphEditShelf, 1);
        Grid.SetRow(workspace, 2);
        Grid.SetRow(drawerBar, 3);
        Grid.SetRow(_graphDrawerSplitter, 4);
        Grid.SetRow(drawer, 5);
        graphWorkspace.Children.Add(toolbarHost);
        graphWorkspace.Children.Add(_graphEditShelf);
        graphWorkspace.Children.Add(workspace);
        graphWorkspace.Children.Add(drawerBar);
        graphWorkspace.Children.Add(_graphDrawerSplitter);
        graphWorkspace.Children.Add(drawer);

        _problems.IsVisible = false;
        _problemBar.IsVisible = false;
        _running.IsVisible = false;
        return graphWorkspace;
    }




    private Button BuildViewMenu(Control fitAll, Control fitPicked)
    {
        var button = Ux.Secondary("View ▾");
        var menu = new ContextMenu();
        var workspace = ViewItem("Workspace", OpenWorkspaceWindow);
        var properties = ViewItem("Properties", () => SetGraphRightPaneOpen(!_graphRightOpen));
        var problems = ViewItem("Problems", () => OpenGraphDrawer("Problems"));
        var output = ViewItem("Output", () => OpenGraphDrawer("Output"));
        var legend = ViewItem("Legend", OpenLegendWindow);
        var focus = ViewItem("Focus tree", FocusSelectedMachine);
        var full = ViewItem("Show full graph", ShowFullGraph);
        var freeform = ViewItem("Freeform", () => SetGraphLayoutMode(GraphLayoutMode.Freeform));
        var structured = ViewItem("Structured Flow", () => SetGraphLayoutMode(GraphLayoutMode.StructuredFlow));
        var upstream = ViewItem("Trace upstream", () => TraceSelected(GraphTrace.Direction.Upstream));
        var downstream = ViewItem("Trace downstream", () => TraceSelected(GraphTrace.Direction.Downstream));
        var both = ViewItem("Trace both", () => TraceSelected(GraphTrace.Direction.Both));
        var clear = ViewItem("Clear trace", _graph.ClearTrace);
        foreach (var item in new MenuItem[]
                 { workspace, properties, problems, output, legend, freeform, structured, focus, full,
                   upstream, downstream, both, clear })
            menu.Items.Add(item);

        button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            workspace.Header = WorkspaceVisible ? "Workspace   Open" : "Workspace   Closed";
            legend.Header = LegendWindowVisible ? "Legend   Open" : "Legend   Closed";
            properties.Header = _graphRightOpen ? "Properties   Open" : "Properties   Closed";
            freeform.Header = _graph.LayoutMode == GraphLayoutMode.Freeform ? "✓ Freeform" : "Freeform";
            structured.Header = _graph.LayoutMode == GraphLayoutMode.StructuredFlow
                ? "✓ Structured Flow" : "Structured Flow";
            menu.Open(button);
        };
        ToolTip.SetTip(button, "Open workspace tools, panels, graph focus, tracing, and reference material.");
        return button;
    }

    private static MenuItem ViewItem(string label, Action action)
    {
        var item = new MenuItem { Header = label };
        item.Click += (_, _) => action();
        return item;
    }

    private void OpenGraphDrawer(string tab)
    {
        SetGraphDrawerOpen(true);
        SelectGraphDrawerTab(tab);
    }

    private void SetGraphLayoutMode(GraphLayoutMode mode)
    {
        _graph.SetLayoutMode(mode);
        SetStatus(mode == GraphLayoutMode.StructuredFlow
            ? "Structured Flow shows the state-machine hierarchy."
            : "Freeform shows the raw object dependency graph.", Ux.MetaBrush);
    }

    private Control BuildWorkspaceRuntimeTab()
    {
        var variables = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(14, 0, 14, 10),
        };
        variables.Children.Add(Ux.SectionTitle("Variables"));
        variables.Children.Add(_runVariables);
        variables.Children.Add(_runValue);
        variables.Children.Add(_setRunVariable);

        var sections = new Grid();
        sections.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        sections.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        sections.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        sections.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        PlaceRuntime(_running, sections, 0, 0, new Thickness(14, 0, 5, 5));
        PlaceRuntime(_runStopsGrid, sections, 1, 0, new Thickness(5, 0, 14, 5));
        PlaceRuntime(_runHeldBackGrid, sections, 0, 1, new Thickness(14, 5, 5, 14));
        PlaceRuntime(_runLog, sections, 1, 1, new Thickness(5, 5, 14, 14));

        var content = new DockPanel { LastChildFill = true };
        var header = new DockPanel { Margin = new Thickness(14, 12, 14, 8) };
        header.Children.Add(Ux.SectionTitle("Simulation runtime"));
        header.Children.Add(_runtimeStatus);
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(variables, Dock.Top);
        content.Children.Add(header);
        content.Children.Add(variables);
        content.Children.Add(sections);
        return content;
    }

    private static void PlaceRuntime(Control control, Grid grid, int column, int row, Thickness margin)
    {
        control.Margin = margin;
        Grid.SetColumn(control, column);
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }








    private Control BuildRunControls()
    {
        var send = Ux.Primary("Send");
        send.Click += (_, _) => SendRunEvent();

        var restart = Ux.Secondary("Restart");
        ToolTip.SetTip(restart, "Put the graph back in the state it starts in.");
        restart.Click += (_, _) => StartRun("Back at the start.");




        _step = Ux.Secondary("Step 0.1s");
        ToolTip.SetTip(_step, "Advance time so a transition in progress blends further.");
        _step.Click += (_, _) =>
        {
            if (_run == null) return;





            var byTime = _run.Advance(0.1f);
            if (byTime.Count > 0)
            {
                RefreshRun($"Stepped 0.1s. {byTime.Count} transition(s) fired because a clip reached " +
                           $"a point in itself: {string.Join(", ", byTime.Select(f => f.Event).Distinct())}.");
                return;
            }

            RefreshRun(_run.Blending ? "Stepped 0.1s, still blending." : "Stepped 0.1s, blend finished.");
        };




        _running.SelectionChanged += () =>
        {
            if (_running.SelectedTag is string id && id.Length > 0 && _graph.FocusOn(id))
                SelectObjectId(id);
        };

        var label = Ux.Label("Event");
        label.FontSize = 11;
        label.Margin = new Thickness(2, 0, 0, 0);



        _runValue.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) SetRunVariable(); };
        _runVariables.SelectionChanged += (_, _) => ShowRunVariable();
        ToolTip.SetTip(_setRunVariable, "Change a simulation variable before sending the next event.");
        _setRunVariable.Click += (_, _) => SetRunVariable();

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        foreach (var control in new Control[] { label, _runEvents, send, _step, restart })
            left.Children.Add(control);

        SetRunSummary("Open a behaviour, then send it an event to watch which state goes active.",
            Ux.MutedBrush);
        return left;
    }











    private Control BuildPasteControls()
    {
        var copy = Ux.Secondary("Copy subtree");
        ToolTip.SetTip(copy, "Take the selected node and everything it owns, ready to paste.");
        copy.Click += (_, _) => CopySubtree();

        _pasteButton = Ux.Primary("Paste subtree");
        ToolTip.SetTip(_pasteButton,
            "Put a fresh copy into this file and save it. The file is kept as .bak first.");
        _pasteButton.Click += (_, _) => PasteSubtree();
        _pasteButton.IsEnabled = false;

        var label = new TextBlock
        {
            Text = "Attach to",
            Foreground = Ux.MetaBrush,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };





        var save = Ux.Secondary("Save as template");
        ToolTip.SetTip(save, "Keep the selected node and everything it owns, so it can be used again " +
                             "in another file later.");
        save.Click += (_, _) => SaveTemplate();

        _applyTemplate.Click += (_, _) => ApplyStoredTemplate();
        _applyTemplate.IsEnabled = false;
        ToolTip.SetTip(_applyTemplate,
            "Put a kept shape into this file and save it. The file is kept as .bak first.");




        _templates.SelectionChanged += (_, _) => DescribeTemplate();
        _predefinedTemplates.ItemsSource = PredefinedTemplates.All().Select(template => template.Id).ToList();
        _predefinedTemplates.SelectionChanged += (_, _) => RefreshPredefinedTemplateEditors();
        _applyPredefinedTemplate.Click += (_, _) => ApplyPredefinedTemplate();
        if (_predefinedTemplates.Items.Count > 0) _predefinedTemplates.SelectedIndex = 0;

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var control in new Control[] { copy, label, _pasteInto, _pasteButton,
                                                _templateName, save, _templates, _applyTemplate })
            left.Children.Add(control);

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
        DockPanel.SetDock(left, Dock.Left);
        bar.Children.Add(left);
        bar.Children.Add(_pasteSummary);

        var predefined = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                                          Margin = new Thickness(0, 6, 0, 0) };
        predefined.Children.Add(new TextBlock { Text = "Predefined", Foreground = Ux.MetaBrush,
                                                FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        predefined.Children.Add(_predefinedTemplates);
        predefined.Children.Add(_predefinedSlots);
        predefined.Children.Add(_applyPredefinedTemplate);

        var all = new StackPanel();
        all.Children.Add(bar);
        all.Children.Add(predefined);

        RefreshPasteSlots();
        RefreshTemplates();
        _applyPredefinedTemplate.IsEnabled = _bytes != null && !_readOnly;
        RefreshPredefinedTemplateEditors();
        return all;
    }







    private void RefreshPasteSlots()
    {
        var slots = new List<string> { Unattached };

        if (_bytes != null && _selectedId.Length > 0
            && int.TryParse(_selectedId, out int id)
            && id - NativeGraphModel.FirstId >= 0
            && id - NativeGraphModel.FirstId < _bytes.Instances.Count)
        {
            var instance = _bytes.Instances[id - NativeGraphModel.FirstId];
            foreach (var member in HavokClassTypes.Shipped.Members(instance.ClassName))
            {
                if (!member.Written) continue;
                bool one = member.VType == "TYPE_POINTER";
                bool many = member.VType is "TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY"
                            && member.VSub == "TYPE_POINTER";
                if (one || many) slots.Add($"#{_selectedId}.{member.Name}" + (many ? "[]" : ""));
            }
        }

        string chosen = _pasteInto.SelectedItem as string ?? Unattached;
        _pasteInto.ItemsSource = slots;
        _pasteInto.SelectedItem = slots.Contains(chosen) ? chosen : Unattached;

        _pasteButton.IsEnabled = _clip != null && !_readOnly && _hkxPath.Length > 0;
        _applyPredefinedTemplate.IsEnabled = _bytes != null && !_readOnly;
        if (_clip != null && _pasteSummary.Text?.Length == 0) SetPasteSummary(Held(_clip), Ux.MetaBrush);
    }

    private const string Unattached = "(leave it unattached)";

    private static string Held(NativePaste.Clip clip)
    {
        var tree = clip.Tree;
        string what = $"Holding #{tree.RootId} {tree.RootClass} from " +
                      $"{Path.GetFileName(clip.Path)}: {tree.Ids.Count} object(s)";
        if (tree.Shared.Count > 0) what += $", {tree.Shared.Count} shared with the rest of that file";
        if (tree.Events.Count > 0) what += $", {tree.Events.Count} event(s)";
        if (tree.Variables.Count > 0) what += $", {tree.Variables.Count} variable(s)";
        return what + ".";
    }

    private void CopySubtree()
    {
        if (_hkxPath.Length == 0 || _selectedId.Length == 0)
        {
            SetPasteSummary("Pick a node on the canvas or in the tree first.", Ux.MutedBrush);
            return;
        }

        if (!int.TryParse(_selectedId, out int id))
        {
            SetPasteSummary($"#{_selectedId} is not an object this file numbers, so there is nothing " +
                            "to copy.", Ux.BadBrush);
            return;
        }

        try
        {
            _clip = NativePaste.Copy(_hkxPath, id);
            SetPasteSummary(Held(_clip) + " Open the file to paste it into, or paste it here.",
                            Ux.MetaBrush);
        }
        catch (Exception e)
        {
            SetPasteSummary("Nothing copied: " + e.Message, Ux.BadBrush);
        }

        RefreshPasteSlots();
    }

    private void PasteSubtree()
    {
        if (_clip == null) { SetPasteSummary("Nothing has been copied yet.", Ux.MutedBrush); return; }
        if (_readOnly) { SetPasteSummary("Not pasted: " + _readOnlyWhy, Ux.BadBrush); return; }



        if (_dirty)
        {
            SetPasteSummary("Save your other changes first. Pasting writes the file and reads it " +
                            "back, which would lose them.", Ux.BadBrush);
            return;
        }

        string? blocked = HkxTextEdit.WhyNotWritable(_hkxPath);
        if (blocked != null) { SetPasteSummary("Cannot paste: " + blocked, Ux.BadBrush); return; }

        int attachTo = -1;
        string field = "";
        if (_pasteInto.SelectedItem as string is { } slot && slot != Unattached)
        {
            int dot = slot.IndexOf('.');
            attachTo = int.Parse(slot[1..dot]);
            field = slot[(dot + 1)..].TrimEnd('[', ']');
        }

        NativePaste.Result written;
        try
        {
            written = NativePaste.Paste(_hkxPath, _clip, attachTo, field);

            FileSafety.Backup(_hkxPath);
            FileSafety.Replace(_hkxPath, written.Bytes);
        }
        catch (Exception e)
        {
            SetPasteSummary("Nothing pasted, and the file is untouched: " + e.Message, Ux.BadBrush);
            return;
        }

        string said = written.Note + $" The file before this is kept as {Path.GetFileName(_hkxPath + ".bak")}.";

        try
        {
            Load();
        }
        catch (Exception e)
        {
            SetPasteSummary("The file was pasted into, but the editor could not reload it: " +
                            e.Message, Ux.BadBrush);
            return;
        }
        SelectObjectId(written.RootId.ToString());
        SetPasteSummary(said, Ux.MetaBrush);
        SetStatus(said, Ux.MetaBrush);
    }







    private void SaveTemplate()
    {
        if (_bytes == null || _hkxPath.Length == 0)
        {
            SetPasteSummary("Open a behaviour first.", Ux.MutedBrush);
            return;
        }

        if (_selectedId.Length == 0 || !int.TryParse(_selectedId, out int id))
        {
            SetPasteSummary("Pick a node on the canvas or in the tree first.", Ux.MutedBrush);
            return;
        }

        string name = _templateName.Text?.Trim() ?? "";
        if (name.Length == 0)
        {
            SetPasteSummary("Give the template a name in the box first, so it can be told apart from " +
                            "the others later.", Ux.MutedBrush);
            return;
        }

        try
        {
            var kept = TemplateStore.Lift(_hkxPath, id, name, $"from #{id} of {Path.GetFileName(_hkxPath)}");
            _templateName.Text = "";
            RefreshTemplates();
            _templates.SelectedItem = kept.Slug;

            SetPasteSummary($"Kept '{kept.Name}': {kept.Objects} object(s) from #{id}. " +
                            (kept.Events.Count + kept.Variables.Count > 0
                                ? $"It uses {kept.Events.Count + kept.Variables.Count} symbol(s) by name, so a " +
                                  "file it goes into has to declare them."
                                : "It uses no events or variables, so it fits anywhere."),
                            Ux.MetaBrush);
        }
        catch (Exception e)
        {
            SetPasteSummary("Nothing kept: " + e.Message, Ux.BadBrush);
        }
    }


    private void ApplyStoredTemplate()
    {
        if (_templates.SelectedItem as string is not { } slug || slug.Length == 0)
        {
            SetPasteSummary("Pick a template first.", Ux.MutedBrush);
            return;
        }

        var template = TemplateStore.Get(slug);
        if (template == null) { SetPasteSummary("That template is no longer there.", Ux.BadBrush); return; }

        if (_readOnly) { SetPasteSummary("Not applied: " + _readOnlyWhy, Ux.BadBrush); return; }



        if (_dirty)
        {
            SetPasteSummary("Save your other changes first. Applying a template writes the file and " +
                            "reads it back, which would lose them.", Ux.BadBrush);
            return;
        }

        string? blocked = HkxTextEdit.WhyNotWritable(_hkxPath);
        if (blocked != null) { SetPasteSummary("Cannot apply: " + blocked, Ux.BadBrush); return; }

        int attachTo = -1;
        string field = "";
        if (_pasteInto.SelectedItem as string is { } slot && slot != Unattached)
        {
            int dot = slot.IndexOf('.');
            attachTo = int.Parse(slot[1..dot]);
            field = slot[(dot + 1)..].TrimEnd('[', ']');
        }

        NativePaste.Result written;
        try
        {
            written = TemplateStore.Apply(template, _hkxPath, attachTo, field);

            FileSafety.Backup(_hkxPath);
            FileSafety.Replace(_hkxPath, written.Bytes);
        }
        catch (Exception e)
        {
            SetPasteSummary("Nothing applied, and the file is untouched: " + e.Message, Ux.BadBrush);
            return;
        }

        string said = $"Applied '{template.Name}'. {written.Note} " +
                      $"The file before this is kept as {Path.GetFileName(_hkxPath + ".bak")}.";

        try
        {
            Load();
        }
        catch (Exception e)
        {
            SetPasteSummary("The template was written, but the editor could not reload the file: " +
                            e.Message, Ux.BadBrush);
            return;
        }
        SelectObjectId(written.RootId.ToString());
        SetPasteSummary(said, Ux.MetaBrush);
        SetStatus(said, Ux.MetaBrush);
    }


    private void RefreshTemplates()
    {
        var kept = TemplateStore.All();
        _templates.ItemsSource = kept.Select(t => t.Slug).ToList();
        _applyTemplate.IsEnabled = kept.Count > 0 && _bytes != null;
        if (kept.Count > 0 && _templates.SelectedItem == null) _templates.SelectedIndex = 0;
    }







    private void DescribeTemplate()
    {
        if (_templates.SelectedItem as string is not { } slug) return;

        var template = TemplateStore.Get(slug);
        if (template == null) return;

        if (_bytes == null)
        {
            SetPasteSummary($"'{template.Name}': {template.Objects} object(s). Open a behaviour to put " +
                            "it into one.", Ux.MutedBrush);
            return;
        }

        try
        {
            var fit = TemplateStore.Against(template, _bytes);
            SetPasteSummary($"'{template.Name}': {template.Objects} object(s) from {template.FromFile}. " +
                            (fit.Fits ? "Everything it needs is already declared here."
                                      : "Before this can go in, " + fit + " on the symbols tab."),
                            fit.Fits ? Ux.MetaBrush : Ux.WarnBrush);
        }
        catch (Exception e)
        {
            SetPasteSummary("Could not tell whether that fits: " + e.Message, Ux.BadBrush);
        }
    }

    private void RefreshPredefinedTemplateEditors()
    {
        _predefinedSlots.Children.Clear();
        _predefinedValues.Clear();
        _applyPredefinedTemplate.IsEnabled = _bytes != null && !_readOnly;

        if (_predefinedTemplates.SelectedItem as string is not { } id) return;
        var template = PredefinedTemplates.Get(id);
        if (template == null) return;

        foreach (var slot in template.Slots)
        {
            Control control;
            if (slot.Kind == PredefinedTemplates.SlotKind.Choice)
            {
                var choices = new ComboBox { MinWidth = 120, Foreground = Ux.CodeBrush, FontSize = 12,
                                             ItemsSource = slot.Choices?.ToList() ?? new List<string>() };
                choices.SelectedItem = slot.DefaultValue;
                control = choices;
            }
            else
            {
                control = Ux.Field(slot.DisplayName + (slot.Required ? " *" : ""), 130);
                ((TextBox)control).Text = slot.DefaultValue;
            }
            ToolTip.SetTip(control, slot.Description);
            _predefinedValues[slot.Key] = control;
            _predefinedSlots.Children.Add(control);
        }

        SetPasteSummary($"{template.DisplayName}: {template.Description}", Ux.MetaBrush);
    }

    private void ApplyPredefinedTemplate()
    {
        if (_predefinedTemplates.SelectedItem as string is not { } id) return;
        if (_readOnly) { SetPasteSummary("Not created: " + _readOnlyWhy, Ux.BadBrush); return; }
        if (_dirty) { SetPasteSummary("Save your other changes first.", Ux.BadBrush); return; }
        string? blocked = HkxTextEdit.WhyNotWritable(_hkxPath);
        if (blocked != null) { SetPasteSummary("Cannot create: " + blocked, Ux.BadBrush); return; }

        var raw = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, control) in _predefinedValues)
            raw[key] = control is ComboBox choice ? choice.SelectedItem as string ?? "" : ((TextBox)control).Text ?? "";

        var result = PredefinedTemplates.Instantiate(_hkxPath, id, raw);
        if (!result.Possible || result.Bytes == null)
        {
            SetPasteSummary("Nothing created: " + result.Refusal, Ux.BadBrush);
            return;
        }

        try
        {
            FileSafety.Backup(_hkxPath);
            FileSafety.Replace(_hkxPath, result.Bytes);
        }
        catch (Exception e)
        {
            SetPasteSummary("Nothing was created: the file could not be replaced. " + e.Message, Ux.BadBrush);
            return;
        }

        try
        {
            Load();
        }
        catch (Exception e)
        {
            SetPasteSummary("The template was written, but the editor could not reload the file. " +
                            e.Message, Ux.BadBrush);
            return;
        }
        SelectObjectId(result.RootId.ToString());
        SetPasteSummary(result.Summary + $" The file before this is kept as {Path.GetFileName(_hkxPath + ".bak")}.",
                        Ux.MetaBrush);
    }

    private void SetPasteSummary(string text, IBrush brush)
    {
        _pasteSummary.Text = text;
        _pasteSummary.Foreground = brush;
    }


    public IReadOnlyList<string> TemplateNames =>
        (_templates.ItemsSource as IEnumerable<string>)?.ToList() ?? new List<string>();
    public bool CanApplyTemplate => _applyTemplate.IsEnabled;
    public bool CanCreatePredefinedTemplate => _applyPredefinedTemplate.IsEnabled;
    public void SaveTemplateForTest(string name)
    {
        _templateName.Text = name;
        SaveTemplate();
    }
    public void ChooseTemplateForTest(string slug)
    {
        if (!TemplateNames.Contains(slug)) return;




        _templates.SelectedItem = slug;
        DescribeTemplate();
    }
    public void ApplyTemplateForTest() => ApplyStoredTemplate();


    public string ClipSummary => _clip == null ? "" : Held(_clip);
    public IReadOnlyList<string> PasteSlots =>
        (_pasteInto.ItemsSource as IEnumerable<string>)?.ToList() ?? new List<string>();
    public string PasteAnswer => _pasteSummary.Text ?? "";
    public bool CanPaste => _pasteButton.IsEnabled;
    public void CopyForTest() => CopySubtree();
    public void PasteForTest(string slot)
    {
        if (PasteSlots.Contains(slot)) _pasteInto.SelectedItem = slot;
        PasteSubtree();
    }


    private void StartRun(string note = "Started at the graph's root.")
    {
        _graph.ClearActive();
        _running.Clear();
        _runStopsGrid.Clear();
        _runHeldBackGrid.Clear();
        _runLog.Clear();
        _runOutputLines.Clear();
        _runOutput.Text = "";
        _running.IsVisible = false;
        _step.IsEnabled = false;
        SetMachineNavigatorActive(Array.Empty<string>());

        var model = Model();
        if (model.Objects.Count == 0)
        {
            _run = null;
            _runEvents.ItemsSource = null;
            _runVariables.ItemsSource = null;
            SetRunSummary("Open a behaviour to run it.", Ux.MutedBrush);
            return;
        }

        _run = GraphRun.Start(model);
        if (_run.RootId.Length == 0)
        {
            _run = null;
            _runEvents.ItemsSource = null;
            _runVariables.ItemsSource = null;
            SetRunSummary("This is a project or character file rather than a graph, so there is " +
                          "nothing in it to run.", Ux.MutedBrush);
            return;
        }




        if (_bytes != null && _hkxPath.Length > 0)
        {
            try
            {
                _run.Time(ClipTiming.All(_bytes, SymbolEditor.EventNames(model),
                                         ClipTiming.FromDisk(_hkxPath)));
            }
            catch (Exception)
            {


            }
        }

        _runEvents.ItemsSource = _run.Events;
        _runVariables.ItemsSource = _run.Variables;
        if (_run.Variables.Count > 0) _runVariables.SelectedIndex = 0;
        ShowRunVariable();
        if (_run.Events.Count > 0) _runEvents.SelectedIndex = 0;
        RefreshRun(note);
    }

    private void SendRunEvent()
    {
        if (_run == null)
        {
            SetRunSummary("Open a behaviour first.", Ux.MutedBrush);
            return;
        }

        if (_runEvents.SelectedItem is not string name || name.Length == 0)
        {
            SetRunSummary("Choose an event to send.", Ux.MutedBrush);
            return;
        }

        var fired = _run.Send(name);
        int held = _run.HeldBack.Count;

        _runLog.Add(null, "Event: " + name);
        foreach (var move in fired)
            _runLog.Add(null, $"Transition: {move.Event} to {move.ToStateName}").Tag(move.ToStateId);




        string said = fired.Count == 0
            ? held == 0
              ? $"Sent {name}. Nothing in a running state was listening for it."
              : $"Sent {name}. Something was listening, but {held} transition(s) are held back by a condition."
            : $"Sent {name}. {fired.Count} transition(s) fired." +
              (held > 0 ? $" {held} other(s) held back by a condition." : "");

        RefreshRun(said);
    }


    private void ShowRunVariable()
    {
        if (_run == null || _runVariables.SelectedItem is not string name) return;
        _runValue.Text = _run.ValueOf(name) is double value
            ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "";
    }

    private void SetRunVariable()
    {
        if (_run == null) { SetRunSummary("Open a behaviour first.", Ux.MutedBrush); return; }

        if (_runVariables.SelectedItem is not string name || name.Length == 0)
        {
            SetRunSummary("Choose a variable to set.", Ux.MutedBrush);
            return;
        }

        if (!double.TryParse(_runValue.Text ?? "", System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out double value))
        {
            SetRunSummary($"'{_runValue.Text}' is not a number, so {name} was not changed.", Ux.BadBrush);
            return;
        }

        try
        {
            _run.Set(name, value);
            RefreshRun($"{name} is now {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}. " +
                       "Send an event to see what that changes.");
        }
        catch (ArgumentException e)
        {
            SetRunSummary(e.Message, Ux.BadBrush);
        }
    }


    private void RefreshRun(string note)
    {
        if (_run == null) return;

        var here = _run.Where();
        _graph.ShowActive(here.Select(a => a.StateId));
        SetMachineNavigatorActive(here.Where(a => !a.Fading).Select(a => a.MachineId));
        AddRunOutput(note);

        _running.Clear();
        foreach (var active in here)
        {
            string machine = active.MachineName.Length > 0 ? active.MachineName : "#" + active.MachineId;
            if (active.Fading) machine = "leaving " + machine;
            var row = _running.Add(null,
                machine,
                active.StateName.Length > 0 ? active.StateName : "#" + active.StateId,
                $"{active.Weight * 100:F0}%")
                .Tag(active.StateId);



            if (active.Fading) row.Colour(0, Ux.MutedBrush).Colour(1, Ux.MutedBrush).Colour(2, Ux.MutedBrush);
        }
        _running.IsVisible = true;



        _step.IsEnabled = _run.Blending;

        _runStopsGrid.Clear();
        foreach (var stop in _run.Stops)
            _runStopsGrid.Add(null, stop.ClassName, stop.Why).Tag(stop.ObjectId);



        _runHeldBackGrid.Clear();
        foreach (var held in _run.HeldBack)
            _runHeldBackGrid.Add(null, held.Event + " to " +
                                   (held.ToStateName.Length > 0 ? held.ToStateName : "#" + held.ToStateId),
                                   held.Condition).Tag(held.ToStateId);

        int machines = here.Count(a => !a.Fading);
        string blending = _run.Blending ? "  A transition is blending; Step to move it along." : "";
        SetRunSummary($"{machines} machine(s) running.  {note}{blending}", Ux.MetaBrush);
    }

    private void SetRunSummary(string text, IBrush brush)
    {
        _runSummary.Text = text;
        _runSummary.Foreground = brush;
        _runtimeStatus.Text = text;
        _runtimeStatus.Foreground = brush;
    }

    private void AddRunOutput(string text)
    {
        if (text.Length == 0) return;
        _runOutputLines.Add(text);
        if (_runOutputLines.Count > 120) _runOutputLines.RemoveRange(0, _runOutputLines.Count - 120);
        _runOutput.Text = string.Join(Environment.NewLine, _runOutputLines);
    }






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
        return split;
    }



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
        _run = null;
        _graph.ClearActive();
        _running.Clear();
        _running.IsVisible = false;
        SetMachineNavigatorActive(Array.Empty<string>());
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


    public bool RunReady => _run != null;
    public bool RunBlending => _run?.Blending ?? false;
    public int RunEventCount => _run?.Events.Count ?? 0;
    public IReadOnlyList<string> RunEvents => _run?.Events ?? Array.Empty<string>();


    public void StepForTest(float seconds) { _run?.Advance(seconds); RefreshRun("stepped"); }



    public int TimedClipCount => _run?.Playing().Count(p => p.Clip.Known) ?? 0;
    public int RunningCount => _running.RowCount;
    public bool RunningVisible => _running.IsVisible;


    public void SendEventForTest(string name)
    {
        _runEvents.SelectedItem = name;
        SendRunEvent();
    }

    public IReadOnlyList<string> RunVariables =>
        (_runVariables.ItemsSource as IEnumerable<string>)?.ToList() ?? new List<string>();
    public int RunHeldBack => _run?.HeldBack.Count ?? 0;
    public bool RunHeldBackVisible => _runHeldBackGrid.RowCount > 0;
    public string RunHeldBackText => _runHeldBack.Text ?? "";
    public string RunSummary => _runSummary.Text ?? "";
    public double? RunValueOf(string name) => _run?.ValueOf(name);


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
    public string StatusForTest => _status.Text ?? "";
    public string PathFieldForTest => _pathField.Text ?? "";

    private Control BuildAnimationTab()
    {
        var earlier = Ux.Secondary("Earlier frames");
        earlier.Click += (_, _) => PageFrames(-FramesPerPage);
        var later = Ux.Secondary("Later frames");
        later.Click += (_, _) => PageFrames(FramesPerPage);
        var first = Ux.Secondary("First");
        first.Click += (_, _) => PageFrames(int.MinValue);
        var last = Ux.Secondary("Last");
        last.Click += (_, _) => PageFrames(int.MaxValue);

        var panel = new DockPanel();
        var header = Ux.Pill(_animationSummary);
        header.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);

        var bar = Bar(Ux.Pill(_framePage), first, earlier, later, last);
        bar.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(bar, Dock.Top);
        panel.Children.Add(bar);




        var aim = Ux.Secondary("Find frame");
        aim.Click += (_, _) => AimAtFraction();
        _fraction.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) AimAtFraction(); };
        _boneFilter.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) ShowAnimationFrames();
        };

        var tools = Bar(Ux.Pill(_fractionAnswer), _boneFilter, _fraction, aim);
        tools.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(tools, Dock.Top);
        panel.Children.Add(tools);





        var apply = Ux.Secondary("Set frame");
        apply.Click += (_, _) => SetFrame();
        var write = Ux.Primary("Save changes");
        write.Click += (_, _) => SaveAnimation();

        foreach (var box in new[] { _framePosition, _frameRotation, _frameScale })
            box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) SetFrame(); };

        _animation.SelectionChanged += ShowSelectedFrame;

        var editing = Bar(_framePosition, _frameRotation, _frameScale, apply, write,
                          Ux.Pill(_frameEditAnswer));
        editing.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(editing, Dock.Top);
        panel.Children.Add(editing);

        panel.Children.Add(_animation);
        return panel;
    }



    private void ShowSelectedFrame()
    {
        var anim = _animationData;
        _editTrack = _editFrame = -1;

        if (anim == null || _animation.SelectedTag is not string tag) { Clear(); return; }

        var parts = tag.Split(':');
        if (parts.Length != 3 || parts[0] != "f" ||
            !int.TryParse(parts[1], out int track) || !int.TryParse(parts[2], out int frame))
        {
            Clear();
            return;
        }

        if (track >= anim.Tracks.Count || frame >= anim.Tracks[track].Translations.Count) { Clear(); return; }

        _editTrack = track;
        _editFrame = frame;

        var data = anim.Tracks[track];
        _framePosition.Text = Triple(data.Translations[frame]);
        _frameRotation.Text = frame < data.Rotations.Count
            ? $"{F(data.Rotations[frame].X)}, {F(data.Rotations[frame].Y)}, " +
              $"{F(data.Rotations[frame].Z)}, {F(data.Rotations[frame].W)}"
            : "";
        _frameScale.Text = frame < data.Scales.Count ? Triple(data.Scales[frame]) : "";

        _frameEditAnswer.Text = $"{TrackName(anim, _animationSkeleton, track)}, frame {frame}";
        _frameEditAnswer.Foreground = Ux.MetaBrush;

        void Clear()
        {
            _framePosition.Text = _frameRotation.Text = _frameScale.Text = "";
            _frameEditAnswer.Text = "Pick a frame row to change it.";
            _frameEditAnswer.Foreground = Ux.MutedBrush;
        }
    }

    internal static string TempDirKey(string path)
    {
        string full = Path.GetFullPath(path);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(full));
        return Path.GetFileNameWithoutExtension(full) + "-" + Convert.ToHexString(hash)[..12];
    }

    private static bool ContainsNonFinite(string? text)
    {
        var parts = (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
            if (float.TryParse(part.Trim(), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float f) &&
                (float.IsNaN(f) || float.IsInfinity(f)))
                return true;
        return false;
    }

    private static string Triple(System.Numerics.Vector3 v) => $"{F(v.X)}, {F(v.Y)}, {F(v.Z)}";

    private static string F(float value) =>
        value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);





    private void SetFrame()
    {
        var anim = _animationData;
        if (anim == null || _editTrack < 0)
        {
            _frameEditAnswer.Text = "Pick a frame row first.";
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return;
        }

        var track = anim.Tracks[_editTrack];

        if (ContainsNonFinite(_framePosition.Text) || ContainsNonFinite(_frameRotation.Text) ||
            ContainsNonFinite(_frameScale.Text))
        {
            _frameEditAnswer.Text = "NaN and Infinity are not allowed here — every number must be finite.";
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return;
        }

        if (!Numbers(_framePosition.Text, 3, out float[] position) ||
            !Numbers(_frameRotation.Text, 4, out float[] rotation) ||
            (_frameScale.Text?.Trim().Length > 0 && !Numbers(_frameScale.Text, 3, out _)))
        {
            _frameEditAnswer.Text = "Position takes three numbers and rotation four, separated by commas.";
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return;
        }

        track.Translations[_editFrame] = new System.Numerics.Vector3(position[0], position[1], position[2]);

        if (_editFrame < track.Rotations.Count)
            track.Rotations[_editFrame] =
                new System.Numerics.Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);



        if (Numbers(_frameScale.Text, 3, out float[] scale) && _editFrame < track.Scales.Count)
            track.Scales[_editFrame] = new System.Numerics.Vector3(scale[0], scale[1], scale[2]);

        _animationEdited = true;
        _frameEditAnswer.Text = $"{TrackName(anim, _animationSkeleton, _editTrack)}, frame {_editFrame} " +
                                "changed   (unsaved)";
        _frameEditAnswer.Foreground = Ux.CodeBrush;

        int track_ = _editTrack, frame_ = _editFrame;
        ShowAnimationFrames();
        _animation.SelectByTag($"f:{track_}:{frame_}");
    }

    private static bool Numbers(string? text, int wanted, out float[] values)
    {
        values = new float[wanted];
        var parts = (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != wanted) return false;

        for (int i = 0; i < wanted; i++)
            if (!float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out values[i]) ||
                float.IsNaN(values[i]) || float.IsInfinity(values[i]))
                return false;

        return true;
    }








    private bool SaveAnimation()
    {
        var anim = _animationData;
        if (anim == null)
        {
            _frameEditAnswer.Text = "This is not an animation file.";
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return false;
        }

        if (_readOnly)
        {
            _frameEditAnswer.Text = "Not saved: " + _readOnlyWhy;
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return false;
        }

        if (_sourceStamp is { } sourceStamp && !sourceStamp.Matches(_hkxPath, out string externalChange))
        {
            _frameEditAnswer.Text = "Not saved: " + externalChange;
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return false;
        }

        string? blocked = HkxTextEdit.WhyNotWritable(_hkxPath);
        if (blocked != null)
        {
            _frameEditAnswer.Text = "Cannot save: " + blocked;
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return false;
        }

        bool asSpline;
        NativeAnimation.Result written;
        try
        {
            asSpline = anim.AnimationClass == NativeAnimation.SplineClass;
            written = asSpline
                ? NativeAnimation.Recompress(_hkxPath, anim)
                : NativeAnimation.Interleave(_hkxPath, anim);
        }
        catch (Exception e)
        {
            _frameEditAnswer.Text = "Not saved, and the original is untouched: " + e.Message;
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return false;
        }

        try
        {
            VerifyAnimation(written, asSpline);
        }
        catch (Exception e)
        {
            _frameEditAnswer.Text = "The rebuilt animation failed verification, so nothing was written: " + e.Message;
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return false;
        }

        try
        {
            FileSafety.Backup(_hkxPath);
            FileSafety.Replace(_hkxPath, written.Bytes);
        }
        catch (Exception e)
        {
            _frameEditAnswer.Text = "Not saved: the file could not be written: " + e.Message;
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return false;
        }

        _animationEdited = false;

        string size = written.Grew >= 0 ? $"{written.Grew} bytes larger" : $"{-written.Grew} bytes smaller";
        string said =
            $"Saved {written.Frames} frame(s) of {written.Tracks} track(s) " +
            (asSpline ? "spline compressed" : "uncompressed") +
            $", {size}. The original is kept as {Path.GetFileName(_hkxPath + ".bak")}.";

        try
        {
            Load();
        }
        catch (Exception e)
        {
            _frameEditAnswer.Text = "The file was saved, but the editor could not reload it: " + e.Message;
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return false;
        }

        _frameEditAnswer.Text = said;
        _frameEditAnswer.Foreground = Ux.MetaBrush;
        SetStatus(said, Ux.MetaBrush);
        return true;
    }

    private void VerifyAnimation(NativeAnimation.Result written, bool asSpline)
    {
        var rebuilt = new HkxBinaryReader().ParseHkx(written.Bytes);
        if (rebuilt.HasUnsupportedAnimation)
            throw new InvalidDataException(
                $"the rebuilt file decodes as {rebuilt.AnimationClass}, which is not supported");

        var objects = new PackfileObjects(PackfileImage.Read(written.Bytes));
        var mismatched = HavokClassTypes.Shipped.SignatureProblems(objects.ClassNames());
        if (mismatched.Count > 0)
            throw new InvalidDataException("rebuilt class signatures do not match: " + mismatched[0]);

        string expectedClass = asSpline ? NativeAnimation.SplineClass : NativeAnimation.InterleavedClass;
        if (rebuilt.AnimationClass != expectedClass)
            throw new InvalidDataException(
                $"rebuilt decodes as {rebuilt.AnimationClass}, expected {expectedClass}");
        if (rebuilt.NumTracks != written.Tracks)
            throw new InvalidDataException(
                $"rebuilt decodes to {rebuilt.NumTracks} track(s), expected {written.Tracks}");
        if (rebuilt.NumFrames != written.Frames)
            throw new InvalidDataException(
                $"rebuilt decodes to {rebuilt.NumFrames} frame(s), expected {written.Frames}");

        var anim = _animationData;
        if (anim == null) return;
        if (Math.Abs(rebuilt.Duration - anim.Duration) > 1e-3f)
            throw new InvalidDataException(
                $"rebuilt duration {rebuilt.Duration} differs from the edited {anim.Duration}");

        if (_editTrack >= 0 && _editFrame >= 0 && _editTrack < rebuilt.Tracks.Count &&
            _editFrame < rebuilt.Tracks[_editTrack].Translations.Count)
        {
            var wanted = anim.Tracks[_editTrack].Translations[_editFrame];
            var landed = rebuilt.Tracks[_editTrack].Translations[_editFrame];
            float drift = (landed - wanted).Length();
            float limit = asSpline ? 0.05f : 0.001f;
            if (drift > limit)
                throw new InvalidDataException(
                    $"the edited frame did not survive re-encoding (drift {drift})");
        }
    }



    private void AimAtFraction()
    {
        var anim = _animationData;
        if (anim == null || anim.NumFrames <= 0)
        {
            _fractionAnswer.Text = "Open an animation first.";
            _fractionAnswer.Foreground = Ux.MutedBrush;
            return;
        }

        string typed = (_fraction.Text ?? "").Trim();
        if (!float.TryParse(typed, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float fraction) ||
            float.IsNaN(fraction) || float.IsInfinity(fraction))
        {
            _fractionAnswer.Text = $"\"{typed}\" is not a number between 0 and 1.";
            _fractionAnswer.Foreground = Ux.BadBrush;
            _aimedFrame = -1;
            ShowAnimationFrames();
            return;
        }

        _aimedFrame = anim.FrameAt(fraction);
        string clamped = fraction is < 0f or > 1f ? $", clamped from {fraction:0.###}" : "";
        _fractionAnswer.Text =
            $"userControlledTimeFraction {Math.Clamp(fraction, 0f, 1f):0.###} is frame {_aimedFrame} " +
            $"of {Math.Max(anim.NumFrames - 1, 0)}, at {_aimedFrame * anim.FrameDuration:F3}s{clamped}";
        _fractionAnswer.Foreground = Ux.MetaBrush;


        _frameStart = _aimedFrame / FramesPerPage * FramesPerPage;
        ShowAnimationFrames();
    }



    private void PageFrames(int by)
    {
        if (_animationData == null) return;
        int frames = _animationData.NumFrames;
        int lastStart = Math.Max(0, ((frames - 1) / FramesPerPage) * FramesPerPage);

        _frameStart = by switch
        {
            int.MinValue => 0,
            int.MaxValue => lastStart,
            _ => Math.Clamp(_frameStart + by, 0, lastStart),
        };
        ShowAnimationFrames();
    }





    private bool BuildAnimation(string path)
    {
        _animation.Clear();
        _animationData = null;
        _animationSkeleton = null;
        _frameStart = 0;


        _aimedFrame = -1;
        _editTrack = _editFrame = -1;
        _animationEdited = false;
        _fractionAnswer.Text = "";
        _framePosition.Text = _frameRotation.Text = _frameScale.Text = "";
        _frameEditAnswer.Text = "";

        HkxAnimationData anim;
        try
        {
            if (!new HkxBinaryReader().TryReadAnimation(path, out anim))
            {
                _animationSummary.Text =
                    $"Unsupported: {anim.AnimationClass} (decode not implemented yet). " +
                    $"Only {HkxAnimationData.SupportedAnimationClasses} are read, so there is no frame data to show.";
                _animationSummary.Foreground = Ux.BadBrush;
                _animation.Add(null, anim.AnimationClass, "", "", "no frame data was read from this file", "")
                          .Colour(0, Ux.BadBrush).Colour(3, Ux.MutedBrush);
                return true;
            }
        }
        catch (Exception ex)
        {
            _animationSummary.Text = "Could not read this file as an animation: " + ex.Message.Split('\n')[0];
            _animationSummary.Foreground = Ux.BadBrush;
            return false;
        }

        bool anyFrames = anim.Tracks.Any(t => t.Rotations.Count > 0 || t.Translations.Count > 0);
        if (!anyFrames || anim.NumFrames <= 0)
        {
            _animationSummary.Text = anim.AnimationClass.Length == 0
                ? "This file holds no animation."
                : $"{anim.AnimationClass} is present but decoded to no frames, so the file is an empty container.";
            _animationSummary.Foreground = Ux.MutedBrush;
            return anim.AnimationClass.Length > 0;
        }

        _animationData = anim;
        _animationSkeleton = SiblingSkeleton(path);
        _frameStart = 0;
        ShowAnimationFrames();
        return true;
    }

    private void ShowAnimationFrames()
    {
        _animation.Clear();
        var anim = _animationData;
        if (anim == null) { _framePage.Text = ""; return; }

        var skeleton = _animationSkeleton;
        _animationSummary.Text =
            $"{anim.AnimationClass}   {anim.GetSummary()}" +
            (skeleton != null ? $"   bones named from a sibling skeleton of {skeleton.BoneNames.Count}" : "   no sibling skeleton, tracks are numbered");
        _animationSummary.Foreground = Ux.MetaBrush;

        int last = Math.Min(_frameStart + FramesPerPage, anim.NumFrames);
        _framePage.Text = anim.NumFrames <= FramesPerPage
            ? $"all {anim.NumFrames} frames"
            : $"frames {_frameStart} to {last - 1} of {anim.NumFrames}";

        foreach (var note in anim.Annotations)
            _animation.Add(null, "annotation", "", $"{note.Time:F3}s", note.Text, "", "").Colour(0, Ux.MutedBrush);

        string needle = (_boneFilter.Text ?? "").Trim();
        int shown = 0;

        for (int t = 0; t < anim.Tracks.Count; t++)
        {
            string name = TrackName(anim, skeleton, t);
            if (needle.Length > 0 && name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
            shown++;

            var track = anim.Tracks[t];
            int frames = Math.Max(Math.Max(track.Translations.Count, track.Rotations.Count), track.Scales.Count);
            bool scaled = HkxTrackData.IsScaled(track);



            var head = _animation.Add(null, name, frames.ToString(), "", "", "", scaled ? "scaled" : "")
                                 .Colour(0, Ux.TitleBrush).Colour(1, Ux.DisabledBrush).Colour(5, Ux.BadBrush);
            if (needle.Length == 0) head.Collapse();

            for (int f = _frameStart; f < Math.Min(last, frames); f++)
            {
                string pos = f < track.Translations.Count
                    ? $"{track.Translations[f].X:F3}, {track.Translations[f].Y:F3}, {track.Translations[f].Z:F3}" : "";
                string rot = f < track.Rotations.Count
                    ? $"{track.Rotations[f].X:F4}, {track.Rotations[f].Y:F4}, {track.Rotations[f].Z:F4}, {track.Rotations[f].W:F4}" : "";


                string scl = scaled && f < track.Scales.Count
                    ? $"{track.Scales[f].X:F4}, {track.Scales[f].Y:F4}, {track.Scales[f].Z:F4}" : "";
                bool aimed = f == _aimedFrame;


                _animation.Add(head, aimed ? "->" : "", f.ToString(), $"{f * anim.FrameDuration:F3}s", pos, rot, scl)
                          .Tag($"f:{t}:{f}")
                          .Colour(0, Ux.AccentBrush)
                          .Colour(1, aimed ? Ux.AccentBrush : Ux.DisabledBrush).Colour(2, Ux.MutedBrush)
                          .Colour(3, Ux.CodeBrush).Colour(4, Ux.MetaBrush).Colour(5, Ux.BadBrush);
            }
        }

        if (shown == 0 && anim.Tracks.Count > 0)
            _animation.Add(null, $"no track matches \"{needle}\"", anim.Tracks.Count + " in the file")
                      .Colour(0, Ux.MutedBrush).Colour(1, Ux.DisabledBrush);
    }




    private static HkxSkeleton? SiblingSkeleton(string primaryPath, string? fallbackPath = null)
    {
        string? assets = FindPoseSkeletonFolder(primaryPath, fallbackPath);
        if (assets == null) return null;

        foreach (string file in Directory.EnumerateFiles(assets, "*.hkx").OrderBy(f => f))
        {
            try { return new HkxBinaryReader().ReadSkeleton(file); }
            catch { }
        }
        return null;
    }




    internal static string? FindSiblingSkeletonFolder(string animationPath)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(animationPath)) ?? "");
        while (dir != null)
        {
            if (dir.Name.Equals("Animations", StringComparison.OrdinalIgnoreCase))
            {
                var characterRoot = dir.Parent;
                if (characterRoot == null) return null;

                string assets = Path.Combine(characterRoot.FullName, "CharacterAssets");
                return Directory.Exists(assets) ? assets : null;
            }
            dir = dir.Parent;
        }
        return null;
    }

    internal static string? FindPoseSkeletonFolder(string primaryPath, string? fallbackPath = null)
    {
        string? primary = FindSiblingSkeletonFolder(primaryPath);
        return primary ?? (string.IsNullOrWhiteSpace(fallbackPath)
            ? null
            : FindSiblingSkeletonFolder(fallbackPath));
    }

    private static string TrackName(HkxAnimationData anim, HkxSkeleton? skeleton, int track)
    {
        if (skeleton != null && track < anim.TrackToBoneIndices.Count)
        {
            int bone = anim.TrackToBoneIndices[track];
            if (bone >= 0 && bone < skeleton.BoneNames.Count) return skeleton.BoneNames[bone];
        }

        string annotation = track < anim.BoneNames.Count ? anim.BoneNames[track] : "";
        return annotation.Length > 0 ? annotation : $"track {track}";
    }




    private Control BuildPlaybackTab()
    {
        _playButton.Click += (_, _) => TogglePlay();

        var first = Ux.Secondary("|<");
        first.Click += (_, _) => ShowFrame(0, stop: true);
        var back = Ux.Secondary("<");
        back.Click += (_, _) => ShowFrame(_poseFrame - 1, stop: true);
        var forward = Ux.Secondary(">");
        forward.Click += (_, _) => ShowFrame(_poseFrame + 1, stop: true);
        var last = Ux.Secondary(">|");
        last.Click += (_, _) => ShowFrame(int.MaxValue, stop: true);

        var fit = Ux.Secondary("Fit");
        fit.Click += (_, _) => _skeleton.Frame();

        var reference = new CheckBox
        {
            Content = "Reference pose",
            Foreground = Ux.MetaBrush,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        reference.IsCheckedChanged += (_, _) =>
        {
            _skeleton.ShowReference = reference.IsChecked == true;
            _skeleton.InvalidateVisual();
        };

        var travel = new CheckBox
        {
            Content = "Follow travel",
            Foreground = Ux.MetaBrush,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(travel, "Move the character along the path the clip carries, instead of " +
                               "playing it on the spot the way the file stores it.");
        travel.IsCheckedChanged += (_, _) =>
        {
            _followTravel = travel.IsChecked == true;
            ShowFrame(_poseFrame, stop: false);
        };

        var reload = Ux.Secondary("From selected node");
        reload.Click += (_, _) => LoadPoseFromSelection(announce: true);

        var mesh = Ux.Secondary("Mesh...");
        mesh.Click += async (_, _) => await PickMesh();
        ToolTip.SetTip(mesh, "A .nif to draw on this skeleton");
        var clearMesh = Ux.Secondary("No mesh");
        clearMesh.Click += (_, _) => { ClearMesh(); ShowFrame(_poseFrame, stop: false); };

        _scrub.PropertyChanged += (_, e) =>
        {


            if (e.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty || _scrubbing) return;
            ShowFrame((int)Math.Round(_scrub.Value), stop: true);
        };

        _skeleton.BoneHovered += _ => _skeleton.InvalidateVisual();

        var transport = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var control in new Control[]
                 { _playButton, first, back, forward, last, fit, reference, travel, reload, mesh, clearMesh })
            transport.Children.Add(control);

        var bar = Bar(Ux.Pill(_playbackSummary), transport);
        bar.Margin = new Thickness(0, 0, 0, 8);

        var scrubRow = Bar(_scrub, Ux.Pill(_frameLabel));
        scrubRow.Margin = new Thickness(0, 8, 0, 0);

        var panel = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(scrubRow, Dock.Bottom);
        panel.Children.Add(bar);
        panel.Children.Add(scrubRow);
        _skeleton.ClipToBounds = true;
        _playbackViewportHost = Framed(_skeleton);
        _playbackViewportHost.ClipToBounds = true;
        panel.Children.Add(WithClipPicker(_playbackViewportHost));



        SetPlaybackSummary("Open a behaviour and select a clip to see what it plays. That animates " +
                           "the skeleton; use Mesh... to hang a model on it.", Ux.MutedBrush);
        return panel;
    }




    private HkxSkeleton? PoseSkeleton(string? animationPath = null)
    {
        if (_cachedSkeletonFor == _hkxPath && _cachedSkeleton != null) return _cachedSkeleton;

        _cachedSkeletonFor = _hkxPath;
        _cachedSkeleton = _projectChain?.Skeleton ?? SiblingSkeleton(_hkxPath, animationPath);
        return _cachedSkeleton;
    }




    private void LoadPoseFromSelection(bool announce)
    {
        if (_xmlText.Length == 0 || _selectedId.Length == 0)
        {
            if (announce) SetPlaybackSummary("Select a clip generator in the graph or the tree first.", Ux.MutedBrush);
            return;
        }

        string animation = "";
        foreach (var p in HkxTextEdit.ReadParams(_xmlText, _selectedId))
            if (p.Name == "animationName") animation = p.Value.Trim();

        if (animation.Length == 0)
        {
            if (announce)
                SetPlaybackSummary($"{Describe(_selectedId)} names no animation, so there is nothing to play.",
                                   Ux.MutedBrush);
            return;
        }

        string root = _projectChain?.Root ?? Path.GetDirectoryName(Path.GetFullPath(_hkxPath)) ?? "";
        string path = ProjectChain.ResolvePath(root, animation);
        if (!File.Exists(path))
        {
            if (announce)
                SetPlaybackSummary($"'{animation}' is not on disk under {root}, so it cannot be played. " +
                                   "Check graph reports the same thing.", Ux.BadBrush);
            return;
        }

        LoadPose(path, animation);
    }

    private void LoadPose(string animationPath, string label)
    {
        if (_poseSource == animationPath) return;

        Stop();
        _poseSkeleton = PoseSkeleton(animationPath);

        HkxAnimationData animation;
        try
        {
            if (!new HkxBinaryReader().TryReadAnimation(animationPath, out animation))
            {
                SetPlaybackSummary($"{label}: {animation.AnimationClass} is not decoded, so it cannot be drawn.",
                                   Ux.BadBrush);
                ClearPose();
                return;
            }
        }
        catch (Exception ex)
        {
            SetPlaybackSummary($"Could not read {label}: {ex.Message.Split('\n')[0]}", Ux.BadBrush);
            ClearPose();
            return;
        }

        string? refusal = AnimationPose.WhyNotPosable(_poseSkeleton, animation);
        if (refusal != null)
        {
            SetPlaybackSummary($"{label}: {refusal}", Ux.WarnBrush);


            if (_poseSkeleton != null) _skeleton.Show(AnimationPose.ReferencePose(_poseSkeleton));
            _poseAnimation = null;
            _poseSource = "";
            _scrub.Maximum = 0;
            return;
        }

        _poseAnimation = animation;
        _poseSource = animationPath;
        _poseFrame = 0;



        try { _poseMotion = RootMotion.Read(animationPath); }
        catch { _poseMotion = new RootMotion.Motion(); }

        var reference = AnimationPose.ReferencePose(_poseSkeleton!);
        var opening = AnimationPose.At(_poseSkeleton!, animation, 0);
        _skeleton.Show(opening, reference);
        UpdateMesh(opening, _poseSkeleton!);

        _scrubbing = true;
        _scrub.Maximum = Math.Max(0, animation.NumFrames - 1);
        _scrub.Value = 0;
        _scrubbing = false;

        int driven = 0;
        foreach (int track in AnimationPose.TracksByBone(_poseSkeleton!, animation)) if (track >= 0) driven++;




        string travelled = _poseMotion.Any
            ? $"   travels {_poseMotion.Travel.Length():F0} units" +
              (Math.Abs(_poseMotion.Turn) > 0.02f
                  ? $" and turns {_poseMotion.Turn * 180 / MathF.PI:F0} degrees"
                  : "")
            : "   stays on the spot";

        SetPlaybackSummary(
            $"{label}   {animation.NumFrames} frames at {1f / Math.Max(animation.FrameDuration, 0.0001f):F0} fps, " +
            $"{animation.Duration:F2}s   {driven} of {_poseSkeleton!.BoneNames.Count} bones driven   " +
            $"on {_poseSkeleton.Name}{travelled}", Ux.MetaBrush);
        UpdateFrameLabel();
    }











    private async Task OpenFromArchive()
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Which archive to look in",
            AllowMultiple = false,
            SuggestedStartLocation = await StartFolder(),
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Bethesda archives") { Patterns = new[] { "*.ba2", "*.BA2" } },
                FilePickerFileTypes.All,
            },
        });

        string? archivePath = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (archivePath == null) return;

        OpenCommonwealth.Services.Archive.Ba2 archive;
        try
        {
            archive = OpenCommonwealth.Services.Archive.Ba2.Open(archivePath);
        }
        catch (Exception e)
        {
            SetStatus("That archive could not be read: " + e.Message, Ux.BadBrush);
            return;
        }

        using (archive)
        {
            var browser = new ArchiveBrowser(archive, ".hkx");
            await browser.ShowDialog(this);
            if (browser.Chosen is not { } entry) return;

            try
            {



                string folder = Path.Combine(Path.GetTempPath(), "BehaviourGraphStudio",
                                             TempDirKey(archivePath));
                Directory.CreateDirectory(folder);

                string copy = Path.Combine(folder, entry.Name.Replace('/', '_'));
                File.WriteAllBytes(copy, archive.Read(entry));

                _pathField.Text = copy;
                Load();

                _readOnly = true;
                _readOnlyWhy = $"{entry.FileName} came out of {Path.GetFileName(archivePath)}, and " +
                               "nothing here writes back into an archive. Save a copy somewhere of " +
                               "your own and open that to edit it.";
                SetStatus($"Opened {entry.Name} from {Path.GetFileName(archivePath)}, read only. " +
                          $"The copy is at {copy}", Ux.MetaBrush);
            }
            catch (Exception e)
            {
                SetStatus($"Could not open {entry.FileName} from the archive: " + e.Message, Ux.BadBrush);
            }
        }
    }




    private async Task PickMesh()
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Which mesh to draw on this skeleton",
            AllowMultiple = false,
            SuggestedStartLocation = await StartFolder(),
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Meshes") { Patterns = new[] { "*.nif", "*.NIF" } },
                FilePickerFileTypes.All,
            },
        });

        string? path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (path != null) LoadMesh(path);
    }



    private bool LoadMesh(string path)
    {
        var skeleton = PoseSkeleton();
        if (skeleton == null)
        {
            SetPlaybackSummary("No skeleton is resolved for this file, so a mesh has nothing to hang on.",
                               Ux.BadBrush);
            return false;
        }

        ClearMesh();
        try
        {
            var nif = NifFile.Read(path);
            foreach (var shape in NifGeometry.Shapes(nif))
            {
                var binding = SkinnedMesh.Bind(shape, skeleton);
                _meshShapes.Add((shape, binding, SkinnedMesh.Edges(shape)));
            }
        }
        catch (Exception ex)
        {
            ClearMesh();
            SetPlaybackSummary($"Could not read {Path.GetFileName(path)}: {ex.Message.Split('\n')[0]}",
                               Ux.BadBrush);
            return false;
        }

        if (_meshShapes.Count == 0)
        {
            SetPlaybackSummary($"{Path.GetFileName(path)} holds no drawable shape.", Ux.MutedBrush);
            return false;
        }

        _meshPath = path;
        string? settingsWarning = RememberSetting("last_mesh_folder", Path.GetDirectoryName(path) ?? "",
                                                  "The mesh loaded");

        int vertices = _meshShapes.Sum(m => m.Shape.Vertices.Count);
        int edges = _meshShapes.Sum(m => m.Edges.Count);



        var missing = _meshShapes.SelectMany(m => m.Binding.Unmatched).Distinct().ToList();
        float drift = _meshShapes.Max(m => SkinnedMesh.BindError(m.Shape, m.Binding, skeleton));

        string report = $"{Path.GetFileName(path)}   {_meshShapes.Count} shapes, {vertices} vertices, " +
                        $"{edges} edges   drift from the rest pose {drift:F2}";
        if (missing.Count > 0)
            report += $"   {missing.Count} bone{(missing.Count == 1 ? "" : "s")} did not match this " +
                      $"skeleton: {string.Join(", ", missing.Take(6))}" +
                      (missing.Count > 6 ? ", and more" : "") +
                      ". Vertices weighted only to those stay at their rest position.";

        SetPlaybackSummary(settingsWarning ?? report,
                           settingsWarning == null && missing.Count == 0 ? Ux.MetaBrush : Ux.WarnBrush);
        ShowFrame(_poseFrame, stop: false);
        _skeleton.Frame();
        return true;
    }

    private void ClearMesh()
    {
        _meshShapes.Clear();
        _meshPath = "";
        _skeleton.ShowMesh(null);
    }



    private void UpdateMesh(AnimationPose.Pose pose, HkxSkeleton skeleton)
    {
        if (_meshShapes.Count == 0) { _skeleton.ShowMesh(null); return; }

        int total = _meshShapes.Sum(m => m.Edges.Count);
        var segments = new (System.Numerics.Vector3, System.Numerics.Vector3)[total];

        int at = 0;
        foreach (var (shape, binding, edges) in _meshShapes)
        {
            var posed = SkinnedMesh.Pose(shape, binding, pose, skeleton);
            foreach (var (from, to) in edges)
                segments[at++] = (posed[from], posed[to]);
        }

        _skeleton.ShowMesh(segments);
    }

    private void ClearPose()
    {
        Stop();
        _poseAnimation = null;
        _poseSource = "";
        _poseFrame = 0;


        _poseMotion = new RootMotion.Motion();
        _cachedSkeleton = null;
        _cachedSkeletonFor = "";
        _scrubbing = true;
        _scrub.Maximum = 0;
        _scrub.Value = 0;
        _scrubbing = false;
        _skeleton.Reset();
        _frameLabel.Text = "";


        SetPlaybackSummary("Open a behaviour and select a clip to see what it plays. That animates " +
                           "the skeleton; use Mesh... to hang a model on it.", Ux.MutedBrush);
    }



    private Control WithClipPicker(Control viewport)
    {
        _clips.SelectionChanged += () =>
        {
            if (_clips.SelectedTag is not string id || id == _selectedId) return;
            var model = Model();
            ShowProps(id, model);
            LoadPoseFromSelection(announce: true);
        };

        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition(new GridLength(2, GridUnitType.Star)));
        right.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        right.RowDefinitions.Add(new RowDefinition(new GridLength(3, GridUnitType.Star)));

        var horizontal = new GridSplitter { Height = 6, Background = Brushes.Transparent };
        Grid.SetRow(horizontal, 1);
        Grid.SetRow(_clipProps, 2);
        right.Children.Add(_clips);
        right.Children.Add(horizontal);
        right.Children.Add(_clipProps);

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3, GridUnitType.Star)));
        split.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        split.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(360, GridUnitType.Pixel)));

        var splitter = new GridSplitter { Width = 6, Background = Brushes.Transparent };
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(right, 2);
        split.Children.Add(viewport);
        split.Children.Add(splitter);
        split.Children.Add(right);
        return split;
    }




    private void FindMeshForFile()
    {
        var found = MeshLookup.Find(_hkxPath, _projectChain?.Root, _projectChain?.SkeletonPath);
        if (!found.Found)
        {
            SetPlaybackSummary("Select a clip to see what it plays. " + found.Reason, Ux.MutedBrush);
            return;
        }

        if (!LoadMesh(found.Path!)) return;

        SetPlaybackSummary($"Select a clip to see what it plays, on {Path.GetFileName(found.Path!)}.",
                           Ux.MutedBrush);
    }



    private void BuildClipList(BehaviourGraphModel model)
    {
        _clips.Clear();
        foreach (var clip in model.Objects.Where(o => o.Class == "hkbClipGenerator"))
        {
            string animation = clip.Str("animationName");
            _clips.Add(null, clip.Str("name"), animation.Length > 0 ? animation : "nothing")
                  .Colour(0, Ux.TitleBrush)
                  .Colour(1, animation.Length > 0 ? Ux.CodeBrush : Ux.MutedBrush)
                  .Tag(clip.Id);
        }

        if (_clips.RowCount == 0) ShowLoneAnimation();
    }














    private void ShowLoneAnimation()
    {
        if (_animationData is not { NumFrames: > 0 }) return;



        _clips.Add(null, Path.GetFileNameWithoutExtension(_hkxPath),
                   $"{_animationData.Duration:F2}s, {_animationData.NumFrames} frames")
              .Colour(0, Ux.TitleBrush)
              .Colour(1, Ux.CodeBrush);

        _clips.SelectFirst();
    }

    private void TogglePlay()
    {
        if (_poseAnimation == null || _poseAnimation.NumFrames <= 1)
        {
            SetPlaybackSummary("Nothing loaded to play. Select a clip, or press From selected node.", Ux.MutedBrush);
            return;
        }

        if (_clock != null) { Stop(); return; }




        _clock = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(
                Math.Clamp(_poseAnimation.FrameDuration / SelectedPlaybackSpeed(), 1 / 120f, 4)),
        };


        _clock.Tick += (_, _) => ShowFrame(_poseFrame + 1 > _scrub.Maximum ? 0 : _poseFrame + 1, stop: false);
        _clock.Start();
        _playButton.Content = "Pause";
    }




    private float SelectedPlaybackSpeed()
    {
        if (_xmlText.Length == 0 || _selectedId.Length == 0) return 1f;

        foreach (var p in HkxTextEdit.ReadParams(_xmlText, _selectedId))
            if (p.Name == "playbackSpeed" && float.TryParse(p.Value, out float speed))
                return speed > 0f ? speed : 1f;

        return 1f;
    }

    private void Stop()
    {
        _clock?.Stop();
        _clock = null;
        _playButton.Content = "Play";
    }

    private void ShowFrame(int frame, bool stop)
    {
        if (stop) Stop();
        if (_poseAnimation == null || _poseSkeleton == null) return;

        _poseFrame = Math.Clamp(frame, 0, Math.Max(0, _poseAnimation.NumFrames - 1));



        var posed = AnimationPose.At(_poseSkeleton, _poseAnimation, _poseFrame);
        if (_followTravel) posed = WithTravel(posed);

        _skeleton.Update(posed);
        UpdateMesh(posed, _poseSkeleton);

        _scrubbing = true;
        _scrub.Value = _poseFrame;
        _scrubbing = false;
        UpdateFrameLabel();
    }







    private AnimationPose.Pose WithTravel(AnimationPose.Pose pose)
    {
        if (!_poseMotion.Any || _poseAnimation == null) return pose;

        float fraction = _poseAnimation.NumFrames > 1
            ? (float)_poseFrame / (_poseAnimation.NumFrames - 1)
            : 0f;

        var at = RootMotion.At(_poseMotion, fraction);
        var turn = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.Normalize(_poseMotion.Up), at.TurnRadians);

        var moved = new AnimationPose.Pose { Frame = pose.Frame, Time = pose.Time };
        moved.Links.AddRange(pose.Links);

        foreach (var bone in pose.Bones)
        {
            var position = System.Numerics.Vector3.Transform(bone.Position, turn) + at.Position;
            moved.Bones.Add(bone with { Position = position, Rotation = turn * bone.Rotation });
            moved.Min = System.Numerics.Vector3.Min(moved.Min, position);
            moved.Max = System.Numerics.Vector3.Max(moved.Max, position);
        }

        return moved;
    }

    private void UpdateFrameLabel()
    {
        var animation = _poseAnimation;
        _frameLabel.Text = animation == null
            ? ""
            : $"frame {_poseFrame} of {Math.Max(animation.NumFrames - 1, 0)}   " +
              $"{_poseFrame * animation.FrameDuration:F3}s   " +
              $"fraction {(animation.NumFrames > 1 ? (float)_poseFrame / (animation.NumFrames - 1) : 0f):0.###}";
    }

    private void SetPlaybackSummary(string text, IBrush brush)
    {
        _playbackSummary.Text = text;
        _playbackSummary.Foreground = brush;
    }


    public SkeletonView Viewport => _skeleton;
    public int PoseFrame => _poseFrame;
    public int PoseFrameCount => _poseAnimation?.NumFrames ?? 0;
    public string PlaybackSummary => _playbackSummary.Text ?? "";
    public bool IsPlaying => _clock != null;


    public void ScrubTo(int frame) => ShowFrame(frame, stop: true);
    public void LoadPoseFrom(string animationPath) => LoadPose(animationPath, Path.GetFileName(animationPath));
    public AnimationPose.Pose? PoseNow =>
        _poseSkeleton == null ? null : AnimationPose.At(_poseSkeleton, _poseAnimation, _poseFrame);




    private Control BuildDiffTab()
    {
        var compare = Ux.Secondary("Compare with...");
        compare.Click += async (_, _) => await CompareWith();

        var bar = Bar(Ux.Pill(_diffSummary), compare);
        bar.Margin = new Thickness(0, 0, 0, 8);

        var panel = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        panel.Children.Add(bar);
        panel.Children.Add(_diff);

        _diffSummary.Text = "Open a behaviour, then pick another copy of it to see what differs.";
        _diffSummary.Foreground = Ux.MutedBrush;
        return panel;
    }

    private async Task CompareWith()
    {
        if (_xmlText.Length == 0)
        {
            SetDiffSummary("Open a behaviour file first. Comparing needs the text form of both sides.", Ux.MutedBrush);
            return;
        }

        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Compare against which file",
            AllowMultiple = false,
            SuggestedStartLocation = await StartFolder(),
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Havok files") { Patterns = new[] { "*.hkx", "*.HKX" } },
                FilePickerFileTypes.All,
            },
        });

        string? other = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (other == null) return;

        if (_xmlText.Length == 0)
        {
            SetDiffSummary("Nothing is open to compare against.", Ux.BadBrush);
            return;
        }

        _diff.Clear();
        SetDiffSummary($"Reading {Path.GetFileName(other)}...", Ux.MutedBrush);

        BehaviourDiff.Result result;
        try
        {
            long stamp = CaptureStamp();
            string mine = _xmlText;
            result = await Task.Run(() => ComputeDiff(mine, other));
            if (stamp != _documentStamp) return;  // document or revision moved on; discard
        }
        catch (Exception ex)
        {
            SetDiffSummary($"Could not read {Path.GetFileName(other)}: {ex.Message.Split('\n')[0]}", Ux.BadBrush);
            return;
        }

        ShowDiff(Path.GetFileName(other), result);
    }




    private static string TextOf(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var objects = new PackfileObjects(PackfileImage.Read(bytes));

            if (HavokClassTypes.Shipped.SignatureProblems(objects.ClassNames()).Count == 0)
                return NativeXml.From(bytes);
        }
        catch (Exception) { }

        return "";
    }

    private static BehaviourDiff.Result ComputeDiff(string mine, string other)
    {
        string theirs = TextOf(other);
        if (theirs.Length == 0)
            throw new InvalidOperationException(
                "this file's classes are not ones this build describes");

        return BehaviourDiff.Compare(RepackCheck.Take(mine), RepackCheck.Take(theirs));
    }



    public string CompareLoadedWith(string other)
    {
        if (_xmlText.Length == 0) return "";

        ShowDiff(Path.GetFileName(other), ComputeDiff(_xmlText, other));
        return _diffSummary.Text ?? "";
    }

    private void ShowDiff(string otherName, BehaviourDiff.Result result)
    {
        _diff.Clear();
        SetDiffSummary($"{Path.GetFileName(_hkxPath)} against {otherName}: {result}",
                       result.Identical ? Ux.MetaBrush : Ux.TitleBrush);

        foreach (var group in new[] { BehaviourDiff.Kind.Changed, BehaviourDiff.Kind.Removed, BehaviourDiff.Kind.Added })
        {
            var lines = result.Lines.Where(l => l.Kind == group).ToList();
            if (lines.Count == 0) continue;

            var head = _diff.Add(null, group.ToString().ToLowerInvariant(), $"{lines.Count}")
                            .Colour(0, group == BehaviourDiff.Kind.Changed ? Ux.WarnBrush : Ux.CodeBrush)
                            .Colour(1, Ux.TitleBrush);
            if (lines.Count > 200) head.Collapse();

            foreach (var line in lines.Take(2000))
                _diff.Add(head, "", line.Class, line.Where, line.Was, line.Now)
                     .Colour(1, Ux.CodeBrush).Colour(2, Ux.TitleBrush)
                     .Colour(3, Ux.MetaBrush).Colour(4, Ux.MetaBrush);

            if (lines.Count > 2000)
                _diff.Add(head, "", $"and {lines.Count - 2000} more").Colour(1, Ux.MutedBrush);
        }

        if (result.Identical)
            _diff.Add(null, "", "no difference", "the two files hold the same objects with the same values")
                 .Colour(2, Ux.MutedBrush);
    }

    private void SetDiffSummary(string text, IBrush brush)
    {
        _diffSummary.Text = text;
        _diffSummary.Foreground = brush;
    }


    public HkGrid DiffGrid => _diff;



    public HkGrid ClipGrid => _clips;

    private Control BuildSymbolsTab()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        bar.Children.Add(_symbolName);
        bar.Children.Add(_symbolValue);
        bar.Children.Add(_symbolMin);
        bar.Children.Add(_symbolMax);

        foreach (var (label, type) in new (string, SymbolEditor.VariableType)[]
                 {
                     ("+ real", SymbolEditor.VariableType.Real),
                     ("+ int", SymbolEditor.VariableType.Int32),
                     ("+ bool", SymbolEditor.VariableType.Bool),
                 })
        {
            var captured = type;
            var button = Ux.Secondary(label);
            button.Click += (_, _) => AddSymbolVariable(captured);
            bar.Children.Add(button);
        }

        foreach (var (label, action) in new (string, Action)[]
                 {
                     ("+ event", AddSymbolEvent),
                     ("Rename", RenameSymbol),
                     ("Set value", SetSymbolValue),
                     ("Set bounds", SetSymbolBounds),
                     ("Remove", RemoveSymbol),
                 })
        {
            var captured = action;
            var button = Ux.Secondary(label);
            button.Click += (_, _) => captured();
            bar.Children.Add(button);
        }

        var papyrus = Ux.Secondary("Scripts folder...");
        papyrus.Click += async (_, _) => await PickScriptsFolder();
        ToolTip.SetTip(papyrus, "A folder of .psc sources, to show which scripts send each event");
        bar.Children.Add(papyrus);

        bar.Children.Add(Ux.Pill(_symbolAudit));

        var panel = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        bar.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(bar);
        panel.Children.Add(_symbols);
        return panel;
    }




    private async Task PickScriptsFolder()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Where the Papyrus .psc sources are",
            AllowMultiple = false,
        });

        string? folder = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (folder == null) return;

        string? settingsWarning = RememberSetting("scripts", folder, "The scripts folder was selected");
        await ScanPapyrusFolder(folder, settingsWarning);
    }

    private async Task ScanPapyrusFolder(string folder, string? settingsWarning)
    {
        _papyrusScanned = true;
        long stamp = CaptureStamp();
        try
        {
            _papyrus = PapyrusScanRunner != null
                ? await PapyrusScanRunner(folder)
                : await Task.Run(() => PapyrusEvents.Scan(folder));
        }
        catch (Exception e)
        {
            if (stamp != _documentStamp) return;
            Console.Error.WriteLine($"Scripts scan failed: {e}");
            SetStatus("Scripts scan failed. The Papyrus sources could not be read.", Ux.BadBrush);
            return;
        }
        if (stamp != _documentStamp) return;      // document or revision moved on; discard
        SetStatus(settingsWarning ?? _papyrus.ToString(),
                  settingsWarning == null && _papyrus.ScriptsRead > 0 ? Ux.MetaBrush : Ux.MutedBrush);

        if (_xmlText.Length > 0) BuildSymbols(Model());
    }




    private async Task Browse()
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a Havok file",
            AllowMultiple = false,
            SuggestedStartLocation = await StartFolder(),
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Havok files")
                {
                    Patterns = new[] { "*.hkx", "*.HKX", "*.hkt", "*.HKT" },
                },
                FilePickerFileTypes.All,
            },
        });

        string? path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (path == null) return;

        Open(path);
        if (RememberSetting("last_folder", Path.GetDirectoryName(path) ?? "", "The file opened") is { } warning)
            SetStatus(warning, Ux.WarnBrush);
    }

    private async Task<IStorageFolder?> StartFolder()
    {
        string[] candidates =
        {
            Settings.Get("last_folder"),
            Path.GetDirectoryName(_pathField.Text ?? "") ?? "",
        };

        foreach (string dir in candidates)
            if (dir.Length > 0 && Directory.Exists(dir))
                return await StorageProvider.TryGetFolderFromPathAsync(dir);

        return null;
    }

    public void Open(string path)
    {
        _pathField.Text = path;
        Load();
    }

    private static string? RememberSetting(string key, string value, string action)
    {
        return Settings.TrySet(key, value, out string failure)
            ? null
            : $"{action}, but the preference was not saved: {failure}";
    }

    private void SaveCurrentGraphLayout()
    {
        if (_hkxPath.Length == 0 || _graph.LayoutMode != GraphLayoutMode.Freeform) return;
        if (!Settings.TrySetGraphLayout(_hkxPath, _graph.SnapshotFreeformPositions(), out string failure))
            SetStatus($"Could not save graph layout: {failure}", Ux.WarnBrush);
    }



    public void OpenMesh(string nifPath) => LoadMesh(nifPath);

    public int MeshEdges => _skeleton.DrawnEdges;

    public static string? RefuseReason(string path)
    {
        string name = Path.GetFileName(path);
        bool looksHavok = name.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase) ||
                          name.EndsWith(".hkt", StringComparison.OrdinalIgnoreCase);
        if (!looksHavok)
            return $"{name} does not look like a Havok behaviour file. " +
                   "Behaviour Graph Studio opens Fallout 4 .hkx behaviour files.";
        if (!HkxBinaryReader.IsFo4Hkx(path))
            return $"{name} is not a Fallout 4 hk_2014.1.0-r1 packfile.";
        return null;
    }

    private bool SavePendingChanges()
    {
        if (_animationEdited && !_dirty) return SaveAnimation();
        return SaveCurrent();
    }

    private bool ConfirmDiscard(string what)
    {
        CommitPendingFields();
        if (_reloading) return true;
        if (!_dirty && !_animationEdited) return true;
        return (DiscardDecision ?? (() => ShowDiscardDialog(what)))() switch
        {
            DiscardChoice.Discard => true,
            DiscardChoice.Save => SavePendingChanges(),
            _ => false,
        };
    }

    private DiscardChoice ShowDiscardDialog(string what)
    {
        var dialog = new DiscardDialog(what);
        dialog.ShowDialog(this);
        while (dialog.IsVisible)
        {
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(10);
        }
        return dialog.Choice;
    }

    private async Task<DiscardChoice> ShowDiscardDialogAsync(string what)
    {
        var dialog = new DiscardDialog(what);
        return await dialog.ShowDialog<DiscardChoice>(this);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _closeApproved) return;

        if (!_dirty && !_animationEdited)
        {
            _closeApproved = true;
            return;
        }

        e.Cancel = true;
        _ = CloseAfterDecision();
    }

    private async Task CloseAfterDecision()
    {
        DiscardChoice choice;
        if (DiscardDecision is { } decide) choice = decide();
        else choice = await ShowDiscardDialogAsync("close the window");

        bool proceed = choice switch
        {
            DiscardChoice.Discard => true,
            DiscardChoice.Save => SavePendingChanges(),
            _ => false,
        };
        if (proceed)
        {
            _closeApproved = true;
            Close();
        }
    }

    private void Load()
    {
        string path = (_pathField.Text ?? "").Trim().Trim('"');
        if (path.Length == 0) { SetSummary("Enter the path to a .hkx file.", Ux.MutedBrush); return; }
        if (!File.Exists(path))
        {
            string full = Path.GetFullPath(path);
            SetSummary(full == path ? "Not found: " + path
                                    : $"Not found: {path}, which from here means {full}", Ux.BadBrush);
            if (_hkxPath.Length > 0) _pathField.Text = _hkxPath;
            return;
        }
        if (RefuseReason(path) is { } reason)
        {
            SetSummary(reason, Ux.BadBrush);
            if (_hkxPath.Length > 0) _pathField.Text = _hkxPath;
            return;
        }

        if (!ConfirmDiscard("open this file"))
        {
            if (_hkxPath.Length > 0) _pathField.Text = _hkxPath;
            return;
        }

        DocumentSourceStamp sourceStamp;
        try
        {
            sourceStamp = DocumentSourceStamp.Capture(path);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            SetSummary("Could not read the file consistently enough to open it: " + e.Message.Split('\n')[0],
                       Ux.BadBrush);
            return;
        }

        _documentStamp++;
        _sourceStamp = sourceStamp;
        _tree.Clear();
        _clips.Clear();
        ClearProps();
        _offsetToIndex.Clear();
        _objectIds = new List<string>();
        _bytes = null;
        _editedFields.Clear();
        _xmlText = "";
        _xmlPath = "";


        _reading = new BehaviourGraphModel();
        _selectedId = "";
        _projectChain = null;
        _emptyStates = new HashSet<string>();


        _graph.Reset();
        BuildMachineNavigator(new BehaviourGraphModel());
        SetMachineNavigatorActive(Array.Empty<string>());
        ClearPose();
        ResetHistory();
        _readOnly = false;
        _readOnlyWhy = "";




        bool isAnimation = BuildAnimation(path);

        var root = HkxBehaviorParser.ParseBehavior(path);
        if (root == null)
        {
            _hkxPath = path;
            string? rootSettingsWarning = RememberSetting("last_path", path, "The file opened") ??
                                          RememberSetting("last_folder", Path.GetDirectoryName(path) ?? "",
                                                          "The file opened");
            SetSummary(isAnimation
                ? $"{Path.GetFileName(path)}   an animation, not a behaviour. See the Animation and Playback tabs."
                : "Parsed as FO4 hkx, but no root object was resolved.", Ux.MutedBrush);
            SetStatus(rootSettingsWarning ?? _animationSummary.Text ?? "",
                      rootSettingsWarning == null ? _animationSummary.Foreground ?? Ux.MutedBrush : Ux.WarnBrush);





            BuildClipList(new BehaviourGraphModel());



            if (_animationData != null) LoadPose(path, Path.GetFileName(path));
            return;
        }




        if (!isAnimation)
        {
            _animationSummary.Text = "This is a behaviour file. It holds no animation.";
            _animationSummary.Foreground = Ux.MutedBrush;
            _animation.Clear();
            _animationData = null;
        }

        _hkxPath = path;
        _root = root;
        _objects = new List<HkxBehaviorParser.BehaviorNode>(HkxBehaviorParser.LastObjects);
        for (int i = 0; i < _objects.Count; i++) _offsetToIndex[_objects[i].Offset] = i;



        _classWarning = "";
        try
        {
            var bytes = new PackfileObjects(PackfileImage.Read(path));







            var problems = HavokClassTypes.Shipped.SignatureProblems(bytes.ClassNames());
            if (problems.Count > 0)
            {
                _classWarning = $"Unsupported class signature: {problems[0]}" +
                                (problems.Count > 1 ? $", and {problems.Count - 1} more like it" : "") +
                                ". Values are not read from the bytes when the classes do not match.";
                _bytes = null;
            }
            else _bytes = bytes;
        }
        catch (Exception) { _bytes = null; }



        RefreshTemplates();
        _applyPredefinedTemplate.IsEnabled = _bytes != null && !_readOnly;

        var classes = new HashSet<string>();
        int clips = 0;
        foreach (var o in _objects)
        {
            classes.Add(o.ClassName);
            if (!string.IsNullOrEmpty(o.AnimationName)) clips++;
        }




        SetSummary(isAnimation
                       ? $"{Path.GetFileName(path)}   an animation, not a behaviour. See the Animation and Playback tabs."
                       : $"{Path.GetFileName(path)}   root {root.ClassName}   {_objects.Count} objects   " +
                         $"{classes.Count} classes   {clips} clip references" +
                         (_classWarning.Length > 0 ? "   —   " + _classWarning : ""),
                   isAnimation ? Ux.MutedBrush : _classWarning.Length > 0 ? Ux.WarnBrush : Ux.TitleBrush);

        RebuildTree();
        string? settingsWarning = RememberSetting("last_path", path, "The file opened") ??
                                  RememberSetting("last_folder", Path.GetDirectoryName(path) ?? "", "The file opened");
        PrepareEditing();




        if (_animationData != null) LoadPose(path, Path.GetFileName(path));
        if (settingsWarning != null) SetStatus(settingsWarning, Ux.WarnBrush);
    }







    private BehaviourGraphModel Model() =>
        _xmlText.Length > 0 ? BehaviourGraphModel.Parse(_xmlText) : _reading;

    private BehaviourGraphModel _reading = new();

    private void PrepareEditing()
    {
        var reading = _bytes == null ? null : NativeGraphModel.From(_bytes);









        bool own = false;
        if (_bytes != null && reading != null)
        {
            try
            {
                string work = Path.Combine(Path.GetTempPath(), "bgs_edit", TempDirKey(_hkxPath));
                HkxTextEdit.ResetDirectory(work);



                _xmlPath = Path.Combine(work, Path.GetFileNameWithoutExtension(_hkxPath) + ".xml");



                _xmlText = NativeXml.From(File.ReadAllBytes(_hkxPath));
                File.WriteAllText(_xmlPath, _xmlText);

                _objectIds = HkxTextEdit.ObjectIds(_xmlText);
                own = _objectIds.Count == _objects.Count;

                if (!own)
                {
                    _xmlText = "";
                    _objectIds = new List<string>();
                }
            }
            catch
            {
                _xmlText = "";
                _objectIds = new List<string>();
            }
        }

        ResetHistory();

        var model = reading ?? (_xmlText.Length > 0 ? Model() : null);
        if (model == null)
        {
            SetStatus("Read only, so the Graph, Symbols, Chain and Animation tabs stay empty: " +
                      "this file holds a class this build cannot describe. The tree is read straight " +
                      "from the binary. Save stays off for this file.", Ux.WarnBrush);
            return;
        }



        if (_objectIds.Count == 0) _objectIds = model.Objects.Select(o => o.Id).ToList();
        _reading = model;



        _emptyStates = GraphValidator.StatesWithNoGenerator(model);
        RebuildTree();

        _graph.RestoreFreeformPositions(Settings.GetGraphLayout(_hkxPath));
        _graph.Show(model);
        _graph.FrameAll();
        BuildMachineNavigator(model);
        BuildSymbols(model);
        BuildClipList(model);
        BuildChain();
        FindMeshForFile();
        StartRun();

        string source = reading != null ? "read from the file itself" : "read by the internal developer fallback";
        SetStatus(_xmlText.Length > 0
            ? $"Editable. {_objectIds.Count} objects mapped, {_graph.DrawnCount} drawn, {source}."
            : $"{_objectIds.Count} objects mapped, {_graph.DrawnCount} drawn, {source}. " +
              "This file holds a class this build cannot describe, so it is read only.",
            _xmlText.Length > 0 ? Ux.MetaBrush : Ux.WarnBrush);
    }

    private bool IsEmptyState(int offset) =>
        _emptyStates.Count > 0
        && _offsetToIndex.TryGetValue(offset, out int index)
        && index < _objectIds.Count
        && _emptyStates.Contains(_objectIds[index]);

    private void BuildMachineNavigator(BehaviourGraphModel model)
    {
        string selected = _machineNavigator.SelectedTag as string ?? "";
        _machineNavigatorRebuilding = true;
        try
        {
            _machineNavigator.Clear();
            _machineNavigatorIds.Clear();
            _machineNavigatorLabels.Clear();

            string filter = _workspaceWindow?.MachineFilterText ?? "";
            foreach (var machine in model.Objects.Where(o => o.Class == "hkbStateMachine"))
            {
                string name = machine.Str("name");
                if (name.Length == 0) name = "hkbStateMachine";
                string id = "#" + machine.Id;
                if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                    !id.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                bool active = _machineNavigatorActiveIds.Contains(machine.Id);

                _machineNavigatorIds.Add(machine.Id);
                _machineNavigatorLabels.Add(name + " " + id);

                var row = _machineNavigator.Add(null, name, id, active ? "running" : "").Tag(machine.Id);
                var activeBrush = new SolidColorBrush(Ux.Good);
                row.Colour(0, active ? activeBrush : Ux.TitleBrush)
                   .Colour(1, Ux.CodeBrush)
                   .Colour(2, active ? activeBrush : Ux.MutedBrush);
            }

            if (selected.Length > 0) _machineNavigator.SelectByTag(selected);
        }
        finally
        {
            _machineNavigatorRebuilding = false;
        }
    }

    private void FilterMachines(string text)
    {
        if (_reading.Objects.Count > 0) BuildMachineNavigator(_reading);
    }

    private void SetMachineNavigatorActive(IEnumerable<string> activeMachineIds)
    {
        _machineNavigatorActiveIds.Clear();
        foreach (string id in activeMachineIds) _machineNavigatorActiveIds.Add(id);
        if (_reading.Objects.Count > 0) BuildMachineNavigator(_reading);
    }

    private void OnMachineNavigatorSelected()
    {
        if (_machineNavigatorRebuilding) return;
        if (_machineNavigator.SelectedTag is not string id || id.Length == 0) return;
        if (_graph.FocusOn(id)) SelectObjectId(id);
        else SelectObjectId(id);
    }

    private string SelectedMachineId()
    {
        if (_machineNavigator.SelectedTag is string machineId && machineId.Length > 0)
            return machineId;
        var selected = Model().Get(_selectedId);
        return selected?.Class == "hkbStateMachine" ? _selectedId : "";
    }

    private void FocusSelectedMachine()
    {
        string machineId = SelectedMachineId();
        if (machineId.Length == 0)
        {
            SetStatus("Choose a machine before focusing its tree.", Ux.MutedBrush);
            return;
        }

        if (_graph.SetFocusTree(machineId))
            SetStatus($"Focused machine #{machineId}. Use Show full graph to clear the focus.", Ux.MetaBrush);
    }

    private void ShowFullGraph()
    {
        _graph.ClearFocusTree();
        SetStatus("Showing the full graph.", Ux.MetaBrush);
    }

    private void TraceSelected(GraphTrace.Direction direction)
    {
        if (_graph.Trace(direction))
        {
            SetStatus($"Traced {direction.ToString().ToLowerInvariant()} dependencies for #{_graph.SelectedId}.",
                      Ux.MetaBrush);
            return;
        }

        SetStatus("Select a visible graph node before tracing.", Ux.MutedBrush);
    }



    private void RebuildTree()
    {
        _tree.Clear();
        if (_root == null) return;

        string needle = (_filter.Text ?? "").Trim();
        if (needle.Length == 0)
        {
            var seen = new HashSet<int>();
            int rows = 0;
            AddTreeNode(_root, null, seen, ref rows);
            return;
        }

        var head = _tree.Add(null, $"matches for \"{needle}\"").Colour(0, Ux.TitleBrush);
        int hits = 0;
        foreach (var o in _objects)
        {
            if (!Matches(o, needle)) continue;
            _tree.Add(head, string.IsNullOrEmpty(o.NodeName) ? o.ClassName : o.NodeName,
                            o.ClassName, o.AnimationName, "0x" + o.Offset.ToString("X"))
                 .Colour(2, Ux.CodeBrush).Colour(3, Ux.DisabledBrush).Tag(o.Offset);
            if (++hits >= 2000) break;
        }
    }




    private void ApplyFilter()
    {
        RebuildTree();
        if (_xmlText.Length == 0) return;

        string needle = (_filter.Text ?? "").Trim();
        _graph.Filter(needle);

        if (needle.Length == 0)
        {
            SetStatus($"Editable. {_objectIds.Count} objects mapped, {_graph.DrawnCount} drawn." +
                      (_graph.DrawingTruncated
                          ? $" Drawing stops at the first {GraphView.MaxNodes} objects."
                          : ""), Ux.MetaBrush);
            return;
        }

        int hits = _graph.MatchCount;
        SetStatus(hits == 0
            ? $"Nothing matches \"{needle}\"."
            : $"{hits} node{(hits == 1 ? "" : "s")} match \"{needle}\", the rest are dimmed. " +
              "Press Enter to go to the first one.",
            hits == 0 ? Ux.MutedBrush : Ux.MetaBrush);
    }

    private void JumpToFirstMatch()
    {
        string first = _graph.FirstMatch;
        if (first.Length == 0) return;

        _graph.FocusOn(first);
        SelectObjectId(first);
    }


    public void Filter(string needle)
    {
        _filter.Text = needle;
        ApplyFilter();
    }

    public HkGrid TreeGrid => _tree;

    private static bool Matches(HkxBehaviorParser.BehaviorNode o, string needle) =>
        o.ClassName.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || o.NodeName.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || o.AnimationName.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private void AddTreeNode(HkxBehaviorParser.BehaviorNode node, HkRow? parent, HashSet<int> seen, ref int rows)
    {
        if (rows >= MaxTreeRows) return;
        rows++;

        bool repeat = !seen.Add(node.Offset);
        string label = string.IsNullOrEmpty(node.NodeName) ? node.ClassName : node.NodeName;


        bool empty = IsEmptyState(node.Offset);

        var row = _tree.Add(parent, repeat ? label + "  (shown above)" : label,
                            empty ? node.ClassName + "  no generator" : node.ClassName,
                            node.AnimationName, "0x" + node.Offset.ToString("X"));
        row.Colour(0, empty ? Ux.BadBrush : parent == null ? Ux.TitleBrush : repeat ? Ux.DisabledBrush : Ux.MetaBrush)
           .Colour(2, empty ? Ux.BadBrush : Ux.CodeBrush).Colour(3, Ux.DisabledBrush).Tag(node.Offset);

        if (repeat) return;
        foreach (var child in node.Children) AddTreeNode(child, row, seen, ref rows);
    }




    private void OnTreeSelected()
    {
        ClearProps();
        _selectedId = "";
        if (_tree.SelectedTag is not int offset || _xmlText.Length == 0) return;
        if (!_offsetToIndex.TryGetValue(offset, out int index)) return;
        if (index < 0 || index >= _objectIds.Count) return;
        SelectObjectId(_objectIds[index]);
    }

    private void SelectObjectId(string objectId)
    {
        ClearProps();
        _selectedId = "";
        if (objectId.Length == 0 || _xmlText.Length == 0) return;



        var model = Model();
        ShowProps(objectId, model);
        SetStatus(Describe(model, objectId), Ux.MetaBrush);



        LoadPoseFromSelection(announce: false);



        RefreshPasteSlots();
    }

    private string Describe(string id) => Describe(Model(), id);

    private static string Describe(BehaviourGraphModel model, string id)
    {
        var obj = model.Get(id);
        if (obj == null) return "#" + id;
        string name = obj.Str("name");
        return $"#{id} {obj.Class}" + (name.Length > 0 ? $" '{name}'" : "");
    }

    private void ClearProps()
    {
        _fieldCommits.Clear();
        _treeProps.Clear();
        _graphProps.Clear();
        _clipProps.Clear();
    }




    private void ShowProps(string objectId) => ShowProps(objectId, Model());

    private void ShowProps(string objectId, BehaviourGraphModel model)
    {
        _selectedId = objectId;


        _fieldCommits.Clear();
        FillProps(_treeProps, objectId, model);
        FillProps(_graphProps, objectId, model);
        FillProps(_clipProps, objectId, model);
        _clips.SelectByTag(objectId);
    }




    private List<PanelFields.Field> PanelValues(string objectId,
                                                IReadOnlyList<HkxTextEdit.Param> parameters)
    {
        var plain = parameters.Select(p => (p.Name, p.Value)).ToList();

        int index = _objectIds.IndexOf(objectId);
        if (_bytes == null || index < 0 || index >= _bytes.Instances.Count)
            return plain.Select(p => new PanelFields.Field(p.Name, p.Value,
                                                          PanelFields.Source.Fallback, p.Value))
                        .ToList();




        string Reference(PackfileObjects.Instance? target, bool wasNull)
        {
            if (wasNull) return "null";
            if (target == null) return "";
            int at = _bytes.IndexOf(target);
            return at >= 0 && at < _objectIds.Count ? "#" + _objectIds[at] : "";
        }

        var edited = new HashSet<string>(
            _editedFields.Where(f => f.StartsWith(objectId + ".", StringComparison.Ordinal))
                         .Select(f => f[(objectId.Length + 1)..]), StringComparer.Ordinal);

        return PanelFields.For(_bytes, _bytes.Instances[index], plain, Reference, edited);
    }

    private void FillProps(Inspector panel, string objectId, BehaviourGraphModel model)
    {
        panel.Clear();
        string className = HkxTextEdit.ClassOf(_xmlText, objectId);
        var parameters = PanelValues(objectId, HkxTextEdit.ReadParams(_xmlText, objectId));

        int fromXml = parameters.Count(p => p.From == PanelFields.Source.Fallback);
        var heading = Ux.Label($"#{objectId}   {className}   {parameters.Count} editable fields" +
                               (fromXml > 0 ? $", {fromXml} from fallback metadata" : ""));
        heading.TextWrapping = TextWrapping.Wrap;
        panel.Add(heading);

        if (AddBoneArraySection(panel, objectId, className))
            return;






        var summaries = ElementSummary.For(model, objectId);

        for (int i = 0; i < parameters.Count;)
        {
            string group = parameters[i].Group;
            if (group.Length == 0)
            {
                panel.Add(FieldRow(panel, parameters[i], objectId));
                i++;
                continue;
            }

            int end = i;
            while (end < parameters.Count && parameters[end].Group == group) end++;

            var inside = new StackPanel { Spacing = 6, Margin = new Thickness(8, 4, 0, 4), ClipToBounds = true };
            for (int f = i; f < end; f++) inside.Children.Add(FieldRow(panel, parameters[f], objectId));

            panel.Add(ElementBlock(group, summaries.GetValueOrDefault(group, ""), inside));
            i = end;
        }

        AddSymbolSection(panel, objectId, model);
        AddBindingSection(panel, objectId, model);
        AddBlendSection(panel, objectId, model, className);
    }





    private bool AddBoneArraySection(Inspector panel, string objectId, string className)
    {
        bool weights = className == "hkbBoneWeightArray";
        bool indices = className == "hkbBoneIndexArray";
        if (!weights && !indices) return false;

        string field = weights ? "boneWeights" : "boneIndices";
        var values = HkxTextEdit.ArrayValues(_xmlText, objectId, field);
        if (values == null) return false;
        var skeleton = PoseSkeleton();
        panel.Add(Ux.SectionTitle(weights ? "bone weights" : "bone indices"));

        if (skeleton == null)
        {
            var unavailable = Ux.Label("No skeleton is available, so values remain numeric.");
            unavailable.TextWrapping = TextWrapping.Wrap;
            unavailable.Foreground = Ux.WarnBrush;
            panel.Add(unavailable);
        }

        for (int i = 0; i < values.Count; i++)
        {
            int row = i;
            int bone = row;
            if (indices) int.TryParse(values[row], out bone);

            string name = skeleton != null && bone >= 0 && bone < skeleton.BoneNames.Count
                ? skeleton.BoneNames[bone]
                : skeleton == null ? "bone name unavailable" : $"bone {bone} is outside this skeleton";

            var label = Ux.Label($"{row}  {name}");
            label.Width = 188;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(label, weights
                ? $"weight for skeleton bone {row}: {name}"
                : $"entry {row} names skeleton bone {bone}: {name}");

            var value = Ux.Field();
            value.Text = values[row];
            string original = values[row];
            void Commit()
            {
                string now = value.Text ?? original;
                if (now == original) return;

                string before = values[row];
                values[row] = now;
                if (SetArrayValues(objectId, field, values)) original = now;
                else { values[row] = before; value.Text = original; }
            }

            _fieldCommits.Add(Commit);
            value.LostFocus += (_, _) => Commit();
            value.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Commit(); };

            panel.Add(panel.TwoColumnRow(label, value, 188));
        }

        return true;
    }

    private bool SetArrayValues(string objectId, string field, IReadOnlyList<string> values)
    {
        if (_xmlText.Length == 0) return false;
        try
        {
            Commit(HkxTextEdit.SetArrayValues(_xmlText, objectId, field, values));
            _editedFields.Add(objectId + "." + field);
            SetStatus($"#{objectId}.{field} = {values.Count} value(s)   (unsaved)", Ux.CodeBrush);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message.Split('\n')[0], Ux.MutedBrush);
            return false;
        }
    }







    private void AddBlendSection(Inspector panel, string objectId, BehaviourGraphModel model, string className)
    {
        if (className != "hkbBlenderGenerator") return;

        BlendWeights.Result blend;
        try { blend = BlendWeights.Of(model, objectId); }
        catch (Exception) { return; }

        panel.Add(Ux.SectionTitle("what it blends"));

        string head = blend.Mode switch
        {
            BlendWeights.Mode.Mix => "Mixes every child by weight.",
            BlendWeights.Mode.Parametric => $"Parametric on {blend.Parameter} = {blend.ParameterValue:F3}.",
            _ => $"Parametric, driven by the variable {blend.Parameter}, so the mix is set at runtime.",
        };
        var headLabel = Ux.Label(head);
        headLabel.TextWrapping = TextWrapping.Wrap;
        headLabel.Foreground = blend.Resolved ? Ux.MetaBrush : Ux.WarnBrush;
        panel.Add(headLabel);

        foreach (var child in blend.Children)
        {
            string who = child.GeneratorName.Length > 0 ? child.GeneratorName : "#" + child.GeneratorId;
            string share = child.WeightDriven
                ? $"driven by {child.WeightDriver}"
                : blend.Mode == BlendWeights.Mode.Mix
                    ? $"{child.Contribution * 100:F0}%"
                    : blend.Mode == BlendWeights.Mode.Parametric
                        ? $"at {child.Weight:F2}, {child.Contribution * 100:F0}% now"
                        : $"at {child.Weight:F2}";

            var text = Ux.Label($"{who}   {share}");
            text.TextWrapping = TextWrapping.Wrap;
            if (child.WeightDriven) text.Foreground = Ux.WarnBrush;
            panel.Add(text);
        }
    }






    private static Control ElementBlock(string group, string summary, Control inside)
    {
        var header = new Grid { ClipToBounds = true };
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        header.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        var index = Ux.Label(group);
        index.Foreground = Ux.MutedBrush;
        index.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(index, 0);
        header.Children.Add(index);

        if (summary.Length > 0)
        {
            var said = Ux.Label(summary);
            said.Foreground = Ux.CodeBrush;
            said.TextTrimming = TextTrimming.CharacterEllipsis;
            said.ClipToBounds = true;
            Grid.SetColumn(said, 1);
            ToolTip.SetTip(said, summary);
            header.Children.Add(said);
        }

        return new Expander
        {
            Header = header,
            Content = inside,
            IsExpanded = false,
            Padding = new Thickness(0),
            ClipToBounds = true,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        };
    }

    private Control FieldRow(Inspector panel, PanelFields.Field p, string owner)
    {




        if (p.Options.Count > 0) return EnumRow(panel, p, owner);

        string address = p.Address;
        string original = p.Value;

        var field = Ux.Field();
        field.Text = p.Value;




        void Commit()
        {
            if (field.Text == original) return;
            Apply(owner, address, field, original);
            original = field.Text ?? original;
        }

        _fieldCommits.Add(Commit);
        field.LostFocus += (_, _) => Commit();
        field.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) Commit();
        };

        var label = Ux.Label(p.Name);
        label.Width = 128;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(label, Tip(p));

        return panel.TwoColumnRow(label, field);
    }


















    private static string Tip(PanelFields.Field p)
    {
        var lines = new List<string> { p.Address };

        if (p.Owner.Length > 0 && FieldNotes.Structure(p.Owner, p.Name) is { } structure)
            lines.Add(structure);

        if (p.Options.Count > 0)
            lines.Add("one of: " + string.Join(", ", p.Options));

        if (p.Owner.Length > 0 && FieldNotes.Meaning(p.Owner, p.Name) is { } note)
            lines.Add("\n" + note.Says + "\n\nEstablished by: " + note.From);

        return string.Join("\n", lines);
    }

    private Control EnumRow(Inspector panel, PanelFields.Field p, string owner)
    {
        var choice = new ComboBox
        {
            ItemsSource = p.Options,
            SelectedItem = p.Value,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Foreground = Ux.CodeBrush,
        };

        string name = p.Name;
        string address = p.Address;
        string original = p.Value;

        void Commit()
        {
            string now = choice.SelectedItem as string ?? original;
            if (now == original) return;

            if (SetParam(owner, address, now)) original = now;
            else choice.SelectedItem = original;
        }

        _fieldCommits.Add(Commit);
        choice.SelectionChanged += (_, _) => Commit();

        var label = Ux.Label(name);
        label.Width = 128;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(label, Tip(p));

        return panel.TwoColumnRow(label, choice);
    }

    private void AddSymbolSection(Inspector panel, string objectId, BehaviourGraphModel model)
    {
        var events = UsagesOf(true, objectId);
        var variables = UsagesOf(false, objectId);
        if (events.Count == 0 && variables.Count == 0) return;

        var eventNames = SymbolEditor.EventNames(model);
        var variableNames = SymbolEditor.VariableNames(model);
        panel.Add(Ux.SectionTitle("symbols this node touches"));

        foreach (var (use, names, kind) in
                 events.Select(u => (u, eventNames, "event"))
                       .Concat(variables.Select(u => (u, variableNames, "variable"))))
        {
            string name = use.Index >= 0 && use.Index < names.Count
                ? names[use.Index]
                : $"index {use.Index}, which this graph does not declare";

            var text = Ux.Label($"{use.Member} -> {kind} {name}");
            text.TextWrapping = TextWrapping.Wrap;
            if (use.Index >= names.Count) text.Foreground = Ux.BadBrush;
            panel.Add(text);
        }
    }

    private void AddBindingSection(Inspector panel, string objectId, BehaviourGraphModel model)
    {
        var owner = model.Get(objectId);
        if (owner == null) return;

        var names = BindingEditor.VariableNames(model);
        panel.Add(Ux.SectionTitle("variable bindings"));

        foreach (var b in BindingEditor.BindingsOf(model, owner))
        {
            string varName = b.VariableIndex >= 0 && b.VariableIndex < names.Count
                ? names[b.VariableIndex]
                : "index " + b.VariableIndex;

            string setId = b.SetId;
            int index = b.Index;
            var remove = Ux.Secondary("Remove");
            remove.Click += (_, _) => RemoveBinding(setId, index, objectId);

            var text = Ux.Label($"{b.MemberPath} <- {varName}");
            text.TextWrapping = TextWrapping.Wrap;

            var row = new DockPanel();
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(text);
            panel.Add(row);
        }



        var member = Ux.Field("member, e.g. userControlledTimeFraction");
        var variable = Ux.Field("variable name");
        var bind = Ux.Secondary("Bind");
        bind.HorizontalAlignment = HorizontalAlignment.Right;
        bind.Click += (_, _) => AddBinding(objectId, (member.Text ?? "").Trim(), (variable.Text ?? "").Trim());

        panel.Add(member);
        panel.Add(variable);
        panel.Add(bind);
    }



    private void EnsurePapyrus()
    {
        if (_papyrusScanned) return;
        _papyrusScanned = true;

        string folder = Settings.Get("scripts");
        if (folder.Length > 0) _papyrus = PapyrusEvents.Scan(folder);
    }

    private void BuildSymbols(BehaviourGraphModel model)
    {
        EnsurePapyrus();
        _symbols.Clear();

        var names = SymbolEditor.VariableNames(model);
        var types = SymbolEditor.VariableTypes(model);
        var values = SymbolEditor.VariableValues(model);
        var events = SymbolEditor.EventNames(model);

        var counts = SymbolEditor.Audit(model);
        _symbolAudit.Text = counts.ToString();
        _symbolAudit.Foreground = counts.VariablesConsistent && counts.EventsConsistent
            ? Ux.MetaBrush : Ux.BadBrush;

        var readers = UsersByIndex(events: false);
        var listeners = UsersByIndex(events: true);
        var variableSites = Usages(events: false);

        for (int i = 0; i < names.Count; i++)
        {
            var type = i < types.Count ? types[i] : SymbolEditor.VariableType.Int32;
            var row = Paint(_symbols.Add(null, type.ToString().ToLowerInvariant(), i.ToString(), names[i],
                                         i < values.Count ? SymbolEditor.DecodeValue(type, values[i]) : "",
                                         Users(readers, events: false, i)).Tag($"v:{i}"));



            var sites = variableSites.Where(u => u.Index == i)
                                     .GroupBy(u => (u.ObjectId, u.Owner, u.Member)).ToList();
            if (sites.Count == 0) continue;

            row.Collapse();
            foreach (var site in sites)
                AddUsageRow(row, "reads it", site.Key.ObjectId, site.Key.Owner, site.Key.Member,
                            site.Count(), "");
        }




        var usage = _xmlText.Length > 0 ? EventUsage.ByEvent(_xmlText)
                  : _bytes != null ? EventUsage.ByEvent(_bytes)
                  : new Dictionary<int, List<EventUsage.Line>>();

        for (int i = 0; i < events.Count; i++)
        {
            usage.TryGetValue(i, out var lines);
            string scripts = PapyrusEvents.Describe(_papyrus, events[i]);
            string summary = lines is { Count: > 0 } ? EventUsage.Summarise(lines) : Users(listeners, events: true, i);
            var row = Paint(_symbols.Add(null, "event", i.ToString(), events[i], "",
                                         scripts.Length > 0 ? $"{summary}; {scripts}" : summary))
                .Tag($"e:{i}");

            if (lines != null)
            {
                row.Collapse();
                foreach (var line in lines)
                {
                    string what = EventUsage.Describe(line.Role);
                    if (line.ObjectIds.Count == 0)
                    {
                        _symbols.Add(row, what, line.Count > 1 ? $"x{line.Count}" : "", line.Site, "", line.Note)
                                .Colour(0, line.Role == EventUsage.Role.Raised ? Ux.MetaBrush : Ux.MutedBrush)
                                .Colour(1, Ux.DisabledBrush).Colour(2, Ux.CodeBrush).Colour(4, Ux.MutedBrush);
                        continue;
                    }

                    foreach (string id in line.ObjectIds)
                        AddUsageRow(row, what, id, line.Site, "", 0, line.Note);
                }
            }



            if (scripts.Length == 0) continue;
            if (lines == null) row.Collapse();
            _symbols.Add(row, "papyrus", "", scripts, "", "scripts address events by name, not by index")
                    .Colour(0, Ux.MutedBrush).Colour(2, Ux.MetaBrush).Colour(4, Ux.DisabledBrush);
        }

        if (names.Count == 0 && events.Count == 0)
            _symbols.Add(null, "", "", "this graph declares no variables or events").Colour(2, Ux.DisabledBrush);
    }




    private void AddUsageRow(HkRow parent, string what, string objectId, string owner, string member,
                             int count, string note)
    {
        string where = member.Length > 0 ? $"{owner}.{member}" : owner;
        string named = objectId.Length > 0 ? $"#{objectId} {NameOf(objectId)}" : "";

        _symbols.Add(parent, what, count > 1 ? $"x{count}" : "", where, named, note)
                .Colour(0, Ux.MutedBrush).Colour(1, Ux.DisabledBrush).Colour(2, Ux.CodeBrush)
                .Colour(3, Ux.TitleBrush).Colour(4, Ux.MutedBrush)
                .Tag(objectId.Length > 0 ? "#" + objectId : "");
    }

    private string NameOf(string objectId)
    {
        foreach (var p in HkxTextEdit.ReadParams(_xmlText, objectId))
            if (p.Name == "name" && p.Value.Length > 0) return p.Value;
        return HkxTextEdit.ClassOf(_xmlText, objectId);
    }

    private static HkRow Paint(HkRow row) => row
        .Colour(0, Ux.MutedBrush).Colour(1, Ux.DisabledBrush).Colour(2, Ux.TitleBrush).Colour(3, Ux.CodeBrush)
        .Colour(4, row.Text(4).StartsWith("nothing") ? Ux.DisabledBrush : Ux.MetaBrush);















    private List<SymbolIndexFixup.Usage> Usages(bool events) =>
        _xmlText.Length > 0 ? SymbolIndexFixup.Usages(_xmlText, events)
        : _bytes != null ? SymbolIndexFixup.Usages(_bytes, events)
        : new List<SymbolIndexFixup.Usage>();

    private List<SymbolIndexFixup.Usage> UsagesOf(bool events, string objectId) =>
        _xmlText.Length > 0 ? SymbolIndexFixup.UsagesOf(_xmlText, events, objectId)
        : _bytes != null ? SymbolIndexFixup.UsagesOf(_bytes, events, objectId)
        : new List<SymbolIndexFixup.Usage>();

    private Dictionary<int, List<string>> UsersByIndex(bool events)
    {
        var map = new Dictionary<int, List<string>>();
        var references = _xmlText.Length > 0 ? SymbolIndexFixup.References(_xmlText, events)
                       : _bytes != null ? SymbolIndexFixup.References(_bytes, events)
                       : new List<SymbolIndexFixup.EventReference>();

        foreach (var reference in references)
        {
            if (!map.TryGetValue(reference.Index, out var list))
                map[reference.Index] = list = new List<string>();
            list.Add(reference.ToString());
        }
        return map;
    }

    private string Users(Dictionary<int, List<string>> map, bool events, int index)
    {
        if (_xmlText.Length == 0) return "";
        var users = map.TryGetValue(index, out var found) ? found : new List<string>();
        if (users.Count == 0)
            return events
                ? "nothing in this file listens for it; game code and scripts can still send it by name"
                : "nothing in this file reads it; game code can still set and read it by name";

        return string.Join(", ", users.GroupBy(u => u).OrderByDescending(g => g.Count())
            .Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key).Take(4));
    }

    private void BuildChain()
    {
        _chain.Clear();
        var chain = ProjectChain.Resolve(_hkxPath);
        _projectChain = chain;

        foreach (var link in chain.Links)
            _chain.Add(null, link.Role, link.Declared, link.Exists ? "found" : "MISSING", link.Note)
                  .Colour(0, Ux.MutedBrush).Colour(1, Ux.TitleBrush)
                  .Colour(2, link.Exists ? Ux.MetaBrush : Ux.BadBrush);

        AddChainGroup("animations", $"{chain.Animations.Count} declared by the character", chain.Animations, Ux.CodeBrush);
        AddChainGroup("bones", $"{chain.Bones.Count} in the skeleton", chain.Bones, Ux.MetaBrush);

        foreach (string problem in chain.Problems)
            _chain.Add(null, "problem", problem).Colour(0, Ux.BadBrush).Colour(1, Ux.BadBrush);
    }

    private void AddChainGroup(string role, string summary, List<string> values, IBrush colour)
    {
        if (values.Count == 0) return;
        var head = _chain.Add(null, role, summary).Colour(0, Ux.MutedBrush).Colour(1, Ux.TitleBrush).Collapse();
        foreach (string v in values) _chain.Add(head, "", v).Colour(1, colour);
    }



    private void OnSymbolSelected()
    {


        if (_symbols.SelectedTag is string jump && jump.StartsWith('#'))
        {
            string id = jump[1..];
            SelectObjectId(id);
            if (!_graph.FocusOn(id))
                SetStatus($"{Describe(id)} is not drawn on the canvas; its fields are in the panel.", Ux.MutedBrush);
            return;
        }

        if (!SelectedSymbol(out bool variable, out int index)) return;

        var model = Model();
        var names = variable ? SymbolEditor.VariableNames(model) : SymbolEditor.EventNames(model);
        if (index < 0 || index >= names.Count) return;

        _symbolName.Text = names[index];

        if (!variable) { _symbolValue.Text = ""; _symbolMin.Text = ""; _symbolMax.Text = ""; return; }
        var types = SymbolEditor.VariableTypes(model);
        var values = SymbolEditor.VariableValues(model);
        var type = index < types.Count ? types[index] : SymbolEditor.VariableType.Int32;

        _symbolValue.Text = index < values.Count
            ? SymbolEditor.DecodeValue(type, values[index])
            : "";



        var bounds = SymbolEditor.VariableBounds(model);
        _symbolMin.Text = index < bounds.Count ? SymbolEditor.DecodeValue(type, bounds[index].Min) : "";
        _symbolMax.Text = index < bounds.Count ? SymbolEditor.DecodeValue(type, bounds[index].Max) : "";
    }




    private void SetSymbolBounds()
    {
        if (!SelectedSymbol(out bool variable, out int index) || !variable)
        {
            SetStatus("pick a variable row; events have no bounds.", Ux.MutedBrush);
            return;
        }

        EditSymbols(xml =>
        {
            var types = SymbolEditor.VariableTypes(BehaviourGraphModel.Parse(xml));
            var type = index < types.Count ? types[index] : SymbolEditor.VariableType.Int32;

            string min = (_symbolMin.Text ?? "").Trim();
            string max = (_symbolMax.Text ?? "").Trim();
            if (min.Length == 0) min = "0";
            if (max.Length == 0) max = "0";

            xml = SymbolEditor.SetVariableBounds(xml, index, SymbolEditor.EncodeValue(type, min),
                                                 SymbolEditor.EncodeValue(type, max));

            SetStatus($"variable {index} is bounded {min} to {max}   (unsaved)", Ux.CodeBrush);
            return xml;
        });
    }

    private bool SelectedSymbol(out bool variable, out int index)
    {
        variable = false;
        index = -1;


        if (_symbols.SelectedTag is not string tag || tag.Length < 3 || tag[1] != ':') return false;
        variable = tag[0] == 'v';
        return int.TryParse(tag[2..], out index);
    }

    private void AddSymbolVariable(SymbolEditor.VariableType type) => EditSymbols(xml =>
    {
        string name = (_symbolName.Text ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("give the variable a name first");
        xml = SymbolEditor.AddVariable(xml, name, type, out int index);

        string value = (_symbolValue.Text ?? "").Trim();
        if (value.Length > 0)
            xml = SymbolEditor.SetVariableValue(xml, index, SymbolEditor.EncodeValue(type, value));

        SetStatus($"declared {type.ToString().ToLowerInvariant()} variable '{name}' at index {index}   (unsaved)",
                  Ux.CodeBrush);
        return xml;
    });

    private void AddSymbolEvent() => EditSymbols(xml =>
    {
        string name = (_symbolName.Text ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("give the event a name first");
        xml = SymbolEditor.AddEvent(xml, name, out int index);
        SetStatus($"declared event '{name}' at index {index}   (unsaved)", Ux.CodeBrush);
        return xml;
    });

    private void RenameSymbol()
    {
        if (!SelectedSymbol(out bool variable, out int index)) { SetStatus("pick a row first.", Ux.MutedBrush); return; }
        EditSymbols(xml =>
        {
            string name = (_symbolName.Text ?? "").Trim();
            if (name.Length == 0) throw new ArgumentException("type the new name first");
            xml = SymbolEditor.Rename(xml, variable, index, name);



            SetStatus($"renamed {(variable ? "variable" : "event")} {index} to '{name}'. " +
                      "Game code and scripts address it by name, so anything outside this file that " +
                      "used the old name now silently does nothing.   (unsaved)", Ux.CodeBrush);
            return xml;
        });
    }

    private void SetSymbolValue()
    {
        if (!SelectedSymbol(out bool variable, out int index) || !variable)
        {
            SetStatus("pick a variable row; events have no value.", Ux.MutedBrush);
            return;
        }

        EditSymbols(xml =>
        {
            var types = SymbolEditor.VariableTypes(BehaviourGraphModel.Parse(xml));
            var type = index < types.Count ? types[index] : SymbolEditor.VariableType.Int32;
            string value = (_symbolValue.Text ?? "").Trim();
            xml = SymbolEditor.SetVariableValue(xml, index, SymbolEditor.EncodeValue(type, value));
            SetStatus($"variable {index} starts at {value}   (unsaved)", Ux.CodeBrush);
            return xml;
        });
    }

    private void RemoveSymbol()
    {
        if (!SelectedSymbol(out bool variable, out int index)) { SetStatus("pick a row first.", Ux.MutedBrush); return; }
        EditSymbols(xml =>
        {
            string what = variable ? "variable" : "event";
            xml = variable
                ? SymbolEditor.RemoveVariable(xml, index, force: false, out var blockers)
                : SymbolEditor.RemoveEvent(xml, index, force: false, out blockers);

            if (blockers.Count > 0)
                throw new InvalidOperationException(
                    $"{blockers.Count} references still point at {what} {index}: " +
                    string.Join(", ", blockers.Distinct().Take(3)));

            SetStatus($"removed {what} {index}, every index above it moved down   (unsaved)", Ux.CodeBrush);
            return xml;
        });
    }

    private void EditSymbols(Func<string, string> edit)
    {
        if (_xmlText.Length == 0) { SetStatus("Read only: no text form loaded.", Ux.MutedBrush); return; }

        try
        {
            Commit(edit(_xmlText));
            var model = Model();
            _graph.Show(model);
            BuildSymbols(model);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message.Split('\n')[0], Ux.MutedBrush);
        }
    }



    private void ShowAddMenu(string fromId, string field, Point at)
    {
        if (_xmlText.Length == 0) return;

        string parent = fromId.Length > 0 ? fromId : _graph.SelectedId;
        var items = new List<Control>();
        var model = Model();

        if (_graph.SelectedId.Length > 0)
        {
            string id = _graph.SelectedId;
            var highlight = new MenuItem { Header = "Highlight the paths of " + Describe(model, id) };
            highlight.Click += (_, _) => HighlightPaths(id);
            items.Add(highlight);
        }

        if (_graph.HighlightId.Length > 0)
        {
            var clear = new MenuItem { Header = "Clear the highlight" };
            clear.Click += (_, _) => HighlightPaths("");
            items.Add(clear);
        }

        if (items.Count > 0) items.Add(new Separator());

        foreach (string kind in GraphAuthor.Kinds)
        {
            string captured = kind;
            var item = new MenuItem { Header = "Add " + captured };
            item.Click += (_, _) => AddNode(captured, captured + "_new", "", parent, field, at);
            items.Add(item);
        }

        if (_graph.SelectedId.Length > 0)
        {
            string id = _graph.SelectedId;
            var delete = new MenuItem { Header = "Delete " + Describe(model, id) };
            delete.Click += (_, _) => DeleteNode(id);
            items.Add(delete);
        }

        _graph.ContextMenu = new ContextMenu { ItemsSource = items };
        _graph.ContextMenu.Open(_graph);
    }

    private void HighlightPaths(string objectId)
    {
        if (objectId.Length == 0)
        {
            _graph.ClearHighlight();
            SetStatus("Highlight cleared.", Ux.MutedBrush);
            return;
        }

        _graph.Highlight(objectId);
        SetStatus($"Showing only what {Describe(objectId)} is wired to. Escape, or right click, to clear.",
                  Ux.MetaBrush);
    }

    private void AddNode(string kind, string name, string animation, string parentId, string field, Point at)
    {
        if (_xmlText.Length == 0) { SetStatus("Read only: no text form loaded.", Ux.MutedBrush); return; }

        try
        {



            bool bySlot = field.Length > 0 && parentId.Length > 0;
            string xml = GraphAuthor.AddNode(_xmlText, kind, name, animation, bySlot ? "" : parentId,
                                             out string newId, out string note);
            _graph.Place(newId, at);

            if (bySlot)
            {
                try
                {
                    xml = GraphLinks.Connect(xml, parentId, field, newId, out string joined);
                    note = $"created {name}, {joined}";
                }
                catch (Exception ex)
                {
                    note = $"created {name} but left it unattached: {ex.Message.Split('\n')[0]}";
                }
            }



            Commit(xml);
            SetStatus(note + $"   (#{newId}, unsaved)", Ux.CodeBrush);
            RefreshAfterEdit(newId);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message.Split('\n')[0], Ux.MutedBrush);
        }
    }

    private void Relink(string fromId, string field, string toId, bool connect)
    {
        if (_xmlText.Length == 0) { SetStatus("Read only: no text form loaded.", Ux.MutedBrush); return; }

        try
        {
            Commit(connect
                ? GraphLinks.Connect(_xmlText, fromId, field, toId, out string note)
                : GraphLinks.Disconnect(_xmlText, fromId, field, toId, out note));

            SetStatus(note + "   (unsaved)", Ux.CodeBrush);

            var model = Model();
            _graph.Show(model);
            BuildSymbols(model);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message.Split('\n')[0], Ux.MutedBrush);
        }
    }

    private void DeleteNode(string objectId)
    {
        if (objectId.Length == 0) { SetStatus("select a node in the graph first.", Ux.MutedBrush); return; }
        if (_xmlText.Length == 0) { SetStatus("Read only: no text form loaded.", Ux.MutedBrush); return; }

        try
        {
            Commit(GraphAuthor.DeleteNode(_xmlText, objectId, out string note));
            SetStatus(note + "   (unsaved)", Ux.CodeBrush);

            var model = Model();
            _graph.Show(model);
            BuildSymbols(model);
            ClearProps();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message.Split('\n')[0], Ux.MutedBrush);
        }
    }

    private void AddBinding(string objectId, string memberPath, string variableName)
    {
        try
        {
            var names = BindingEditor.VariableNames(Model());
            int index = names.FindIndex(n => n.Equals(variableName, StringComparison.OrdinalIgnoreCase));

            string xml = _xmlText;
            string declared = "";
            if (index < 0)
            {
                xml = BindingEditor.AddVariable(xml, variableName, out index);
                declared = $"declared variable '{variableName}' at index {index}, and ";
            }



            Commit(BindingEditor.AddBinding(xml, objectId, memberPath, index));
            SetStatus($"{declared}#{objectId}.{memberPath} driven by {variableName}   (unsaved)", Ux.CodeBrush);
            RefreshAfterEdit(objectId);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message.Split('\n')[0], Ux.MutedBrush);
        }
    }

    private void RemoveBinding(string setId, int index, string objectId)
    {
        try
        {
            Commit(BindingEditor.RemoveBinding(_xmlText, setId, index));
            SetStatus($"removed binding {index} from #{setId}   (unsaved)", Ux.CodeBrush);
            RefreshAfterEdit(objectId);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message.Split('\n')[0], Ux.MutedBrush);
        }
    }

    private void RefreshAfterEdit(string objectId)
    {
        var model = Model();
        _graph.Show(model);
        BuildSymbols(model);
        ClearProps();
        ShowProps(objectId);
    }






    public void CommitPendingFields()
    {
        foreach (var commit in _fieldCommits.ToList()) commit();
    }

    private void Apply(string objectId, string address, TextBox field, string original)
    {
        if (field.Text == original || _xmlText.Length == 0) return;
        if (!SetParam(objectId, address, field.Text ?? "")) field.Text = original;
    }








    private bool SetParam(string objectId, string address, string value)
    {
        if (_xmlText.Length == 0) return false;

        try
        {
            Commit(HkxTextEdit.SetParamAt(_xmlText, objectId, address, value));
            _editedFields.Add(objectId + "." + address);
            SetStatus($"#{objectId}.{address} = {value}   (unsaved)", Ux.CodeBrush);



            if (address == "playbackSpeed" && objectId == _selectedId && _clock != null)
                _clock.Interval = TimeSpan.FromSeconds(
                    Math.Clamp(_poseAnimation!.FrameDuration / SelectedPlaybackSpeed(), 1 / 120f, 4));

            return true;
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message.Split('\n')[0], Ux.MutedBrush);
            return false;
        }
    }



    private void Validate()
    {
        if (_xmlText.Length == 0 && _reading.Objects.Count == 0)
        {
            SetStatus("Nothing loaded to check.", Ux.MutedBrush);
            return;
        }




        var findings = _xmlText.Length > 0
            ? GraphValidator.Check(_xmlText, _projectChain)
            : GraphValidator.Check(_reading, _projectChain, _bytes);
        var errors = findings.Where(f => f.Level == GraphValidator.Level.Error).ToList();
        var warnings = findings.Where(f => f.Level == GraphValidator.Level.Warning).ToList();

        foreach (var f in findings) Console.WriteLine("check  " + f);

        _graph.Mark(GraphValidator.ByObject(findings));

        _problems.Clear();
        foreach (var f in errors.Concat(warnings))
        {
            bool error = f.Level == GraphValidator.Level.Error;
            _problems.Add(null, error ? "error" : "warning", f.Where, f.What)
                      .Colour(0, error ? Ux.BadBrush : Ux.WarnBrush)
                      .Colour(1, Ux.CodeBrush)
                      .Colour(2, Ux.MetaBrush)
                      .Tag(f.ObjectId);
        }

        bool any = findings.Count > 0;
        _problems.IsVisible = any;
        _problemBar.IsVisible = any;
        _problemBar.Text = any
            ? $"{errors.Count} error{(errors.Count == 1 ? "" : "s")}, " +
              $"{warnings.Count} warning{(warnings.Count == 1 ? "" : "s")}. " +
              "Click one to jump to it on the canvas. Errors are outlined red, warnings amber."
            : "";

        if (!any)
        {
            SetStatus("Checked: nothing wrong found. That is not a promise the game will load it.", Ux.MetaBrush);
            return;
        }

        SetStatus($"{errors.Count} errors, {warnings.Count} warnings. " +
                  $"First: {(errors.Count > 0 ? errors[0] : warnings[0])}",
                  errors.Count > 0 ? Ux.BadBrush : Ux.MutedBrush);
    }




    private async Task ValidateProject()
    {
        var chain = _projectChain;
        if (chain == null || chain.Root.Length == 0)
        {
            SetStatus("No project resolved for this file, so there is no chain to check. See the Chain tab.",
                      Ux.MutedBrush);
            return;
        }





        _problems.Clear();
        _problems.IsVisible = _problemBar.IsVisible = true;
        _problemBar.Text = "Reading the project...";

        long stamp = CaptureStamp();
        var progress = new Progress<string>(s =>
        {
            if (stamp == _documentStamp) SetStatus("Checking " + s, Ux.MutedBrush);
        });

        ProjectCheck.Result result;
        try
        {
            result = ValidateProjectRunner != null
                ? await ValidateProjectRunner(chain, progress)
                : await Task.Run(() => ProjectCheck.Run(
                    chain, s => ((IProgress<string>)progress).Report(s)));
        }
        catch (Exception e)
        {
            if (stamp != _documentStamp) return;
            Console.Error.WriteLine($"Project check failed: {e}");
            _problemBar.Text = "Project check failed.";
            SetStatus("Project check failed. The project files could not be read.", Ux.BadBrush);
            return;
        }

        if (stamp != _documentStamp) return;      // document or revision moved on; discard

        foreach (var file in result.Files.Where(f => f.Error.Length > 0 || f.Findings.Count > 0))
        {
            var head = _problems.Add(null, file.Error.Length > 0 ? "unread" : $"{file.Errors}e {file.Warnings}w",
                                     file.Name, file.Error.Length > 0 ? file.Error : "")
                                .Colour(0, file.Error.Length > 0 || file.Errors > 0 ? Ux.BadBrush : Ux.WarnBrush)
                                .Colour(1, Ux.TitleBrush);
            if (file.Findings.Count > 30) head.Collapse();

            foreach (var f in file.Findings.OrderBy(f => f.Level))
                _problems.Add(head, f.Level == GraphValidator.Level.Error ? "error" : "warning", f.Where, f.What)
                         .Colour(0, f.Level == GraphValidator.Level.Error ? Ux.BadBrush : Ux.WarnBrush)
                         .Colour(1, Ux.CodeBrush).Colour(2, Ux.MetaBrush)
                         .Tag(file.Path == _hkxPath ? f.ObjectId : "");
        }

        if (result.Files.All(f => f.Error.Length == 0 && f.Findings.Count == 0))
            _problems.Add(null, "", "nothing wrong found", "across every behaviour in this project")
                     .Colour(2, Ux.MutedBrush);

        _problemBar.Text = result + ". Only findings in the open file can be jumped to on the canvas.";
        SetStatus("Project checked. " + result, result.Errors > 0 ? Ux.BadBrush : Ux.MetaBrush);
    }




    private bool SavedInPlace() => SaveCurrent();

    private bool SaveCurrent()
    {
        if (_readOnly) { SetStatus("Not saved: " + _readOnlyWhy, Ux.BadBrush); return false; }

        if (_sourceStamp is { } sourceStamp && !sourceStamp.Matches(_hkxPath, out string externalChange))
        {
            SetStatus("Not saved: " + externalChange, Ux.BadBrush);
            return false;
        }

        NativeSave.Plan plan;
        try
        {
            plan = NativeSave.Compare(_savedXml, _xmlText);
        }
        catch (Exception e)
        {
            SetStatus("Could not work out what changed, so nothing was written: " + e.Message, Ux.BadBrush);
            return false;
        }

        if (!plan.Possible)
        {
            SetStatus(plan.Refusal ?? "native save does not support this edit yet", Ux.BadBrush);
            return false;
        }
        if (plan.Empty) { SetStatus("Nothing to save.", Ux.MutedBrush); return false; }

        string? blocked = HkxTextEdit.WhyNotWritable(_hkxPath);
        if (blocked != null) { SetStatus("Cannot save: " + blocked, Ux.BadBrush); return false; }

        byte[] bytes;
        try
        {
            bytes = NativeSave.Apply(_hkxPath, plan);
        }
        catch (Exception e)
        {
            SetStatus("Not saved, and the original is untouched: " + e.Message, Ux.BadBrush);
            return false;
        }

        try
        {
            SaveVerifier.Verify(File.ReadAllBytes(_hkxPath), bytes, plan);
            if (VerifyFaultForTest is { } verifyFault) throw verifyFault();
        }
        catch (Exception e)
        {
            SetStatus("The rebuilt file failed verification, so nothing was written: " + e.Message,
                      Ux.BadBrush);
            return false;
        }

        try
        {
            FileSafety.Backup(_hkxPath);
            FileSafety.Replace(_hkxPath, bytes);
        }
        catch (Exception e)
        {
            SetStatus("Not saved: the file could not be written: " + e.Message, Ux.BadBrush);
            return false;
        }

        ResetHistory();

        string how = plan.Gone.Count > 0
            ? $"and took out {plan.Gone.Count} object{(plan.Gone.Count == 1 ? "" : "s")}, " +
              "so the file was laid out again and everything after them has moved. Object " +
              "numbers above the ones deleted have changed. "
            : plan.Grows
                ? "with anything that grew added on the end so nothing already in it moved. "
                : "leaving every other byte as it was. ";

        SetStatus($"Saved {plan.Changes.Count} " +
                  $"change{(plan.Changes.Count == 1 ? "" : "s")} straight into the file, " + how +
                  $"The original is kept as {Path.GetFileName(_hkxPath + ".bak")}.", Ux.MetaBrush);

        try
        {
            _reloading = true;
            Load();
            if (ReloadFaultForTest is { } fault) throw fault();
        }
        catch (Exception e)
        {
            SetStatus("The file was saved, but the editor could not reload it: " + e.Message, Ux.BadBrush);
            return false;
        }
        finally
        {
            _reloading = false;
        }
        return true;
    }

    private void Save()
    {
        CommitPendingFields();
        if (!_dirty || _xmlText.Length == 0) return;

        if (_readOnly) { SetStatus("Not saved: " + _readOnlyWhy, Ux.BadBrush); return; }




        string? refusal = GraphValidator.SaveRefusal(_xmlText, _savedXml, includeRepackLosses: false);
        if (refusal != null) { SetStatus(refusal, Ux.BadBrush); return; }

        SavedInPlace();
    }

    private void OnWindowKey(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        bool control = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
        if (!control) return;
        bool shift = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift);

        if (e.Key == Avalonia.Input.Key.Z && !shift) { Undo(); e.Handled = true; }
        else if (e.Key == Avalonia.Input.Key.Y || (e.Key == Avalonia.Input.Key.Z && shift)) { Redo(); e.Handled = true; }
    }



    private void Commit(string newXml)
    {
        if (newXml == _xmlText) return;

        _documentStamp++;
        _undo.Add(_xmlText);
        if (_undo.Count > UndoDepth) _undo.RemoveAt(0);
        _redo.Clear();
        _xmlText = newXml;
        RefreshDirty();
    }

    private void ResetHistory()
    {
        _undo.Clear();
        _redo.Clear();
        _savedXml = _xmlText;
        RefreshDirty();
    }



    private void RefreshDirty()
    {
        _dirty = _xmlText.Length > 0 && _xmlText != _savedXml;




        _saveButton.IsEnabled = _dirty && !_readOnly;
        if (_readOnly) ToolTip.SetTip(_saveButton, _readOnlyWhy);
        _undoButton.IsEnabled = _undo.Count > 0;
        _redoButton.IsEnabled = _redo.Count > 0;
    }

    private void Undo()
    {
        if (_undo.Count == 0) { SetStatus("Nothing to undo.", Ux.MutedBrush); return; }
        _redo.Add(_xmlText);
        _xmlText = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        AfterHistoryMove("Undone");
    }

    private void Redo()
    {
        if (_redo.Count == 0) { SetStatus("Nothing to redo.", Ux.MutedBrush); return; }
        _undo.Add(_xmlText);
        _xmlText = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        AfterHistoryMove("Redone");
    }



    private void AfterHistoryMove(string what)
    {
        _documentStamp++;
        RefreshDirty();

        var model = Model();
        _objectIds = HkxTextEdit.ObjectIds(_xmlText);
        _emptyStates = GraphValidator.StatesWithNoGenerator(model);
        RebuildTree();
        _graph.Show(model);
        BuildSymbols(model);
        ClearProps();
        if (_selectedId.Length > 0 && model.Get(_selectedId) != null) ShowProps(_selectedId, model);

        SetStatus($"{what}. {_undo.Count} step{(_undo.Count == 1 ? "" : "s")} back, " +
                  $"{_redo.Count} forward." + (_dirty ? "   (unsaved)" : "   this now matches the file on disk"),
                  Ux.MetaBrush);
    }



    private void SetSummary(string text, IBrush brush)
    {
        _summary.Text = text;
        _summary.Foreground = brush;
    }

    private void SetStatus(string text, IBrush brush)
    {
        _status.Text = text;
        _status.Foreground = brush;
    }
}
