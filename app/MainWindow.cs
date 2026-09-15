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
    private const int MaxTreeRows = 4000;
    private const int FramesPerPage = 300;

    private readonly TextBox _pathField = Ux.Field("absolute path to a .hkx behaviour, character or project file", 420);
    private readonly TextBox _filter = Ux.Field("filter objects by name, class or animation");
    private readonly TextBox _graphFilter = Ux.Field("filter graph by name, class or animation", 220);
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

    private readonly TabControl _tabs = new() { Padding = new Thickness(0) };
    private EditorShell? _shell;
    private readonly TextBlock _bridgeFile = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _bridgeLastAction = new() { Foreground = Ux.MetaBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly WrapPanel _bridgeRecents = new();
    private readonly Button _bridgeSave = Ux.Primary("Save to .hkx");
    private readonly Button _bridgeUndo = Ux.Secondary("Undo");
    private readonly Button _bridgeRedo = Ux.Secondary("Redo");
    private const int RecentCount = 6;
    private readonly TourOverlay _tour = new();
    private AssistantUi? _assistantUi;
    private readonly List<(Border Card, string Title, string What, string Where)> _tourStations = new();
    private bool _tourStarted;
    private TextBlock? _bridgeTitle;
    private Border? _bridgeFileCard;
    private readonly TextBox _bridgeSearch = Ux.Field("Search the deck — stations and the reference table", 340);
    private readonly TextBlock _bridgeSearchAnswer = new() { Foreground = Ux.MutedBrush, FontSize = 12 };
    private readonly List<(TextBlock Header, List<Border> Cards)> _bridgeGroups = new();
    private readonly List<(TextBlock Name, TextBlock Where)> _bridgeRefRows = new();
    private TextBlock? _bridgeRefHeader;
    private readonly TextBlock _dropHint = new()
    {
        Text = "Drop a .hkx anywhere on the Bridge to open it.",
        Foreground = Ux.MutedBrush,
        FontSize = 11,
    };

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
    private readonly TextBlock _scaleStatus = new() { Foreground = Ux.MetaBrush, FontSize = 12 };
    private Control? _scalePill;
    private readonly Button _clearFrameDistanceButton = Ux.Secondary("Clear measure");
    private readonly TextBlock _frameDistanceStatus = new() { Foreground = Ux.MetaBrush, FontSize = 12 };
    private Control? _frameDistancePill;

    private string _summaryBaseText = "";
    private IBrush _summaryBaseBrush = Ux.MetaBrush;

    private readonly Slider _scrub = new() { Minimum = 0, Maximum = 0, SmallChange = 1, LargeChange = 5 };
    private Button _playButton = Ux.Secondary("Play");
    private readonly Button _dropButton = Ux.Secondary("Drop");
    private readonly DispatcherTimer _dropClock =
        new() { Interval = TimeSpan.FromMilliseconds(16) };
    private HkxSkeleton? _poseSkeleton;
    private HkxAnimationData? _poseAnimation;
    private string _poseSource = "";

    private RootMotion.Motion _poseMotion = new();
    private bool _followTravel;
    private HavokRagdollModel? _ragdoll;
    private string? _ragdollSourceName;
    private string? _ragdollSourceNote;
    private readonly CheckBox _bodiesToggle = new()
    {
        Content = "Bodies",
        Foreground = Ux.MetaBrush,
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly CheckBox _constraintsToggle = new()
    {
        Content = "Constraints",
        Foreground = Ux.MetaBrush,
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly CheckBox _bindingsToggle = new()
    {
        Content = "Bone labels",
        Foreground = Ux.MetaBrush,
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly CheckBox _physicsSkeletonToggle = new()
    {
        Content = "Physics skeleton",
        Foreground = Ux.MetaBrush,
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly CheckBox _framesToggle = new()
    {
        Content = "Body frames",
        Foreground = Ux.MetaBrush,
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly CheckBox _engineerToggle = new()
    {
        Content = "Frames only",
        Foreground = Ux.MetaBrush,
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly PlaybackSession _playback = new();
    private bool _scrubbing;
    private DispatcherTimer? _clock;
    private HkxSkeleton? _cachedSkeleton;
    private string _cachedSkeletonFor = "";

    private readonly List<(NifShape Shape, SkinnedMesh.Binding Binding, List<(int From, int To)> Edges)>
        _meshShapes = new();
    private string _meshPath = "";

    private readonly TextBox _projectSearchText =
        Ux.Field("name, class, event, variable, animation or field", 360);
    private readonly TextBlock _projectSearchSummary =
        new() { Foreground = Ux.MetaBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly HkGrid _projectSearchResults =
        new(("File", -3), ("Kind", 80), ("Object", -3), ("Field", -3), ("Value", -6));
    private readonly List<ProjectSearch.Hit> _projectSearchHits = new();
    private long _projectSearchGeneration;

    private readonly HkGrid _diff =
        new(("Change", 80), ("Havok class", -3), ("Field or name", -3), ("In the open file", -4),
            ("In the other file", -4));
    private readonly TextBlock _diffSummary =
        new() { Foreground = Ux.MetaBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _diffKind = new()
        { MinWidth = 130, MaxWidth = 170, Foreground = Ux.CodeBrush, FontSize = 12 };
    private readonly ComboBox _diffClass = new()
        { MinWidth = 220, MaxWidth = 340, Foreground = Ux.CodeBrush, FontSize = 12 };
    private readonly Button _diffExportText = Ux.Secondary("Export text...");
    private readonly Button _diffExportJson = Ux.Secondary("Export JSON...");
    private BehaviourDiff.Result? _diffResult;
    private string _diffOtherName = "";

    private readonly HkGrid _problems = new(("", 70), ("Object", -3), ("What is wrong", -7));
    private readonly TextBlock _problemBar = new() { Foreground = Ux.MetaBrush, FontSize = 12, Margin = new Thickness(2, 6, 2, 2) };

    private long _documentStamp;
    private DocumentSourceStamp? _sourceStamp;
    private readonly ProjectAnalysisController _analysis;
    private readonly BehaviourCompareSession _compare;
    private long CaptureStamp() => _documentStamp;

    public Func<ProjectChain, IProgress<string>, Task<ProjectCheck.Result>>? ValidateProjectRunner;
    public Func<string, Task<PapyrusEvents.Index>>? PapyrusScanRunner;

    private readonly GraphRunSession _runSession = new();
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
    private GameData? _gameData;
    private readonly TextBox _dataField = Ux.Field("Path to the game Data folder, e.g. .../Fallout 4/Data", 220);
    private readonly TextBlock _dataSummary = new() { Foreground = Ux.MetaBrush, FontSize = 12 };
    private readonly TextBox _modsField = Ux.Field("MO2 mods folder, e.g. .../ModOrganizer (optional)", 220);
    private readonly TextBlock _modsSummary = new() { Foreground = Ux.MetaBrush, FontSize = 12 };
    private readonly TextBox _crashField = Ux.Field("AnimTextData crash hash, e.g. 10448007347639226270", 220);
    private readonly TextBlock _crashSummary = new() { Foreground = Ux.MetaBrush, FontSize = 12 };
    private Border? _crashPanel;
    private readonly StackPanel _crashPanelBody = new() { Spacing = 6 };
    private readonly TextBlock _crashPanelTitle = new() { Foreground = Ux.TitleBrush, FontSize = 13 };
    private readonly TextBlock _sweepSummary = new() { Foreground = Ux.MetaBrush, FontSize = 12 };
    private bool _sweeping;
    private string _selectedId = "";
    private readonly List<Action> _fieldCommits = new();
    private bool _dirty;

    private const int UndoDepth = 100;
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private string _savedXml = "";

    public MainWindow()
    {
        _analysis = new ProjectAnalysisController(() => _documentStamp);
        _compare = new BehaviourCompareSession(() => _documentStamp);
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

        _filter.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) ApplyFilter(); };
        _filter.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) JumpToFirstTreeMatch(); };
        _graphFilter.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) ApplyGraphFilter(); };
        _graphFilter.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) JumpToFirstGraphMatch(); };

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

        _tabs.Items.Add(Tab("Bridge", BuildBridgeTab()));
        _tabs.Items.Add(Tab("Tree", BuildTreeTab()));
        _tabs.Items.Add(Tab("Graph", BuildGraphTab()));
        _tabs.Items.Add(Tab("Symbols", BuildSymbolsTab()));
        _tabs.Items.Add(Tab("Chain", BuildChainTab()));
        _tabs.Items.Add(Tab("Project search", BuildProjectSearchTab()));
        _tabs.Items.Add(Tab("Animation", BuildAnimationTab()));
        _tabs.Items.Add(Tab("Playback", BuildPlaybackTab()));
        _tabs.Items.Add(Tab("Compare", BuildDiffTab()));

        _bridgeSave.IsEnabled = false;
        _bridgeUndo.IsEnabled = false;
        _bridgeRedo.IsEnabled = false;
        _bridgeSave.Click += (_, _) => Save();
        _bridgeUndo.Click += (_, _) => Undo();
        _bridgeRedo.Click += (_, _) => Redo();

        EditorShell.HideStrip(_tabs);
        Avalonia.Input.DragDrop.SetAllowDrop(_tabs, true);
        _tabs.AddHandler(Avalonia.Input.DragDrop.DragEnterEvent, BridgeTabDragOver);
        _tabs.AddHandler(Avalonia.Input.DragDrop.DragOverEvent, BridgeTabDragOver);
        _tabs.AddHandler(Avalonia.Input.DragDrop.DragLeaveEvent, BridgeTabDragLeave);
        _tabs.AddHandler(Avalonia.Input.DragDrop.DropEvent, BridgeTabDrop);
        _tabs.SelectionChanged += (_, _) =>
        {
            if (_tabs.SelectedItem is TabItem tab && tab.Header?.ToString() is { } header && header.Length > 0)
            {
                Settings.TrySet("last_tab", header, out _);
                _shell?.ShowTab(header);
            }
        };

        var command = new StackPanel { Spacing = Ux.Space };
        command.Children.Add(Ux.Group("Open behaviour", Ux.Wrap(_pathField, browse, archive, open)));
        command.Children.Add(Ux.Group("Document actions",
            Ux.Wrap(Ux.Pill(_summary), _undoButton, _redoButton, checkProject, check, _saveButton)));
        _shell = new EditorShell(command, _tabs, Ux.Pill(_status));
        _shell.Navigate += GoToTab;
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        Grid.SetColumn(_shell, 0);
        Grid.SetColumn(_tour, 0);
        root.Children.Add(_shell);
        root.Children.Add(_tour);
        Avalonia.Input.DragDrop.SetAllowDrop(root, true);
        root.AddHandler(Avalonia.Input.DragDrop.DragEnterEvent, BridgeTabDragOver);
        root.AddHandler(Avalonia.Input.DragDrop.DragOverEvent, BridgeTabDragOver);
        root.AddHandler(Avalonia.Input.DragDrop.DragLeaveEvent, BridgeTabDragLeave);
        root.AddHandler(Avalonia.Input.DragDrop.DropEvent, BridgeTabDrop);
        _assistantUi = new AssistantUi(this, root, _shell);
        Content = root;

        if (Settings.Get("last_tab") is { } lastTab && lastTab.Length > 0)
            GoToTab(lastTab);

        Opened += (_, _) =>
        {
            if (!_tourStarted && Settings.Get("tour_done").Length == 0) StartTour();
        };

        SetSummary("No file loaded.", Ux.MutedBrush);
        SetStatus("Open a behaviour file to start.", Ux.MutedBrush);
    }
}
