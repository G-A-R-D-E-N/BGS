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
    private readonly Button _saveButton;
    private readonly Button _undoButton;
    private readonly Button _redoButton;
    private readonly Button _findJava;

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

    /// Which frame the boxes above are showing, as the track and the frame it came from, or -1 for
    /// neither. A frame is not addressable by anything in the file, so it is addressed by where it
    /// sits, and the row carries that.
    private int _editTrack = -1, _editFrame = -1;

    /// Whether a frame has been changed since the file was opened. Kept apart from the behaviour
    /// graph's own dirty flag: an animation is a different kind of file, saved a different way, and
    /// running the two together would offer to write a clip out of a behaviour.
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

    /// Where the open clip travels, which the drawn pose does not show. Motion is extracted in this
    /// format: a walk plays on the spot and carries its displacement separately, so the bones stay
    /// put no matter how far the clip takes you. Measured rather than assumed: a Dogmeat walk that
    /// travels 1,060 units moves its root bone 0.000 and its centre of mass 0.312.
    private RootMotion.Motion _poseMotion = new();
    private bool _followTravel;
    private int _poseFrame;
    private bool _scrubbing;
    private DispatcherTimer? _clock;
    private HkxSkeleton? _cachedSkeleton;
    private string _cachedSkeletonFor = "";

    // The mesh, its edges worked out once, and which skeleton bone each of its own bones is. All
    // three are per shape and none of them change while scrubbing, so only the vertex positions are
    // recomputed per frame.
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

    private readonly Dictionary<int, int> _offsetToIndex = new();
    private HashSet<string> _emptyStates = new();
    private List<string> _objectIds = new();

    // The open file's own bytes, which is where the properties panel gets its values. Null when the
    // file could not be taken apart, in which case the panel falls back to hkxpack for everything.
    private PackfileObjects? _bytes;

    // Fields changed since the file was read, as "objectId.field". An edit lands in the text form
    // and not in the bytes until it is saved, so for these the text form is the newer of the two and
    // reading the bytes would put the old value back on screen under the person typing.
    private readonly HashSet<string> _editedFields = new(StringComparer.Ordinal);

    // Why the bytes are not being read, when they are not. Kept rather than printed on the spot,
    // because the load says several things after this point and the last one wins the status line.
    private string _classWarning = "";
    private List<HkxBehaviorParser.BehaviorNode> _objects = new();
    private HkxBehaviorParser.BehaviorNode? _root;

    private string _hkxPath = "";

    /// Set when the open file is a copy pulled out of a BA2 rather than a file on disk. The copy is
    /// in a temporary folder, so saving into it would write somewhere the user will never look and
    /// leave the archive untouched, which is worse than refusing.
    private bool _readOnly;
    private string _readOnlyWhy = "";
    private string _xmlPath = "";
    private string _xmlText = "";
    private ProjectChain? _projectChain;
    private string _selectedId = "";
    private readonly List<Action> _fieldCommits = new();
    private bool _dirty;

    // The document is one string, so a step back is a copy of it. Every mutation goes through Commit,
    // which is the only place _xmlText is allowed to change outside a load, so nothing can edit
    // behind the stack's back. Depth is capped because a long session on a seven megabyte weapon
    // behaviour would otherwise keep every version of it alive.
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

        HkxTextEdit.AppDirectory = AppContext.BaseDirectory;
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

        _findJava = Ux.Secondary("Find Java...");
        _findJava.IsVisible = false;
        _findJava.Click += async (_, _) => await PickJava();

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
                (Bar(Ux.Pill(_status), _findJava, _undoButton, _redoButton, checkProject, check, _saveButton), false)),
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

    // The last control in the row is the one that stretches, unless a later one is a button, which
    // is the layout every bar in this window happens to want.
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

    // The problem list sits under the canvas rather than in a dialog, because the point of it is to
    // be read while looking at the node it is about.
    private Control BuildGraphTab()
    {
        _problems.Height = 150;
        _problems.SelectionChanged += OnProblemSelected;

        var splitter = new GridSplitter { Width = 6, Background = Brushes.Transparent };

        // The canvas draws six node colours, three kinds of line and two badges, and none of them
        // says what it means. That is a lot to hold in your head on a graph with eight hundred boxes
        // in it, and the answer is not fewer marks: it is the marks having somewhere to be looked up.
        _legend = BuildLegend();
        _legend.IsVisible = false;

        var legendButton = Ux.Secondary("Legend");
        legendButton.Click += (_, _) =>
        {
            _legend.IsVisible = !_legend.IsVisible;
            legendButton.Content = _legend.IsVisible ? "Hide legend" : "Legend";
        };

        // A behaviour lays out several screens across. Without a way back to the whole of it, being
        // anywhere in particular means being lost, and the answer to a graph feeling overwhelming is
        // usually being able to see all of it rather than there being less of it.
        var fitAll = Ux.Secondary("Fit all");
        fitAll.Click += (_, _) =>
        {
            _graph.ClearHighlight();
            _graph.FrameAll();
        };

        // With a node picked out, its own neighbourhood filling the view. This is the one that makes
        // a big file workable: a machine and its states are a readable picture, and the rest of the
        // file being off screen is the point rather than a loss.
        var fitPicked = Ux.Secondary("Fit selection");
        fitPicked.Click += (_, _) =>
        {
            if (_graph.SelectedId.Length > 0 && _graph.HighlightId.Length == 0)
                HighlightPaths(_graph.SelectedId);
            _graph.FrameRelated();
        };

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var button in new[] { legendButton, fitAll, fitPicked })
        {
            button.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(button, Dock.Left);
            top.Children.Add(button);
        }
        top.Children.Add(new Panel());

        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(_problemBar, Dock.Bottom);
        DockPanel.SetDock(_problems, Dock.Bottom);
        DockPanel.SetDock(_graphProps, Dock.Right);
        DockPanel.SetDock(splitter, Dock.Right);
        DockPanel.SetDock(_legend, Dock.Left);
        panel.Children.Add(top);
        panel.Children.Add(_problemBar);
        panel.Children.Add(_problems);
        panel.Children.Add(_graphProps);
        panel.Children.Add(splitter);
        panel.Children.Add(_legend);
        panel.Children.Add(Framed(_graph));

        _problems.IsVisible = false;
        _problemBar.IsVisible = false;
        return panel;
    }

    private Control _legend = new Panel();

    /// Read only, for the window checks.
    public Control Legend => _legend;

    /// What everything on the canvas means, in the words somebody reading a graph would use.
    ///
    /// Every colour here is asked for by class name rather than written down again, so the legend
    /// cannot come to disagree with the picture. A legend that is wrong is worse than none: it is
    /// read as the answer rather than checked against the thing it describes.
    private Control BuildLegend()
    {
        var body = new StackPanel { Spacing = 4, Width = 284 };

        void Heading(string text)
        {
            var title = Ux.SectionTitle(text);
            title.Margin = new Thickness(0, 10, 0, 2);
            body.Children.Add(title);
        }

        void Swatch(Control mark, string name, string what)
        {
            // Width given rather than inherited. The row is a DockPanel with the swatch docked left,
            // and the words filling what is left of a panel that is itself inside a scroll viewer,
            // which measures its content as though it had all the room in the world. Left to itself
            // the last word of every explanation sat past the edge of the panel.
            var words = new StackPanel { Spacing = 1, Width = 212 };
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

            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            DockPanel.SetDock(mark, Dock.Left);
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

        // Drawn at the size it is on a node rather than the size the row would like. The first
        // version squeezed it into a twelve pixel strip with eight point text, which is the one
        // sample in the legend nobody could read, and the point of a sample is that it is
        // recognisable when you next meet it.
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
            line.Width = 240;
            line.Margin = new Thickness(0, 3, 0, 0);
            body.Children.Add(line);
        }

        return new Border
        {
            Background = Ux.CardBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6, 10, 10),
            Margin = new Thickness(0, 0, 8, 0),
            Child = new ScrollViewer
            {
                Content = body,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
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

    // Read only, for the window checks. Which page is showing and how many rows it drew are the two
    // things a paged view can lie about.
    public HkGrid AnimationGrid => _animation;
    public string FramePageLabel => _framePage.Text ?? "";
    public int AnimationFrameCount => _animationData?.NumFrames ?? 0;
    public int AnimationTrackCount => _animationData?.Tracks.Count ?? 0;
    public int AnimationAnnotationCount => _animationData?.Annotations.Count ?? 0;
    public HkGrid SymbolGrid => _symbols;
    public string FractionAnswer => _fractionAnswer.Text ?? "";
    public int AimedFrame => _aimedFrame;

    // The frame editing boxes, which are only meaningful together: what they hold, whether a frame
    // is picked at all, and whether anything has been changed since the file was opened.
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

    /// Selects through the same handler a click on the canvas uses, so a check exercises the path a
    /// person takes rather than a parallel one.
    public void SelectNode(string objectId) => SelectObjectId(objectId);

    /// The same, from the tree's end: sets the tree's own selection and lets its handler run, rather
    /// than calling what that handler calls. The two used to reach different places.
    public bool SelectFromTree(string objectId)
    {
        int index = _objectIds.IndexOf(objectId);
        if (index < 0) return false;

        foreach (var (offset, at) in _offsetToIndex)
            if (at == index) return _tree.SelectByTag(offset);
        return false;
    }

    /// Drives the fraction lookup the way a person does, through the same field and the same handler,
    /// so a check exercises what the button exercises rather than a parallel path.
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

    /// Picks a frame row the way clicking one does, so the boxes fill through the same handler.
    public bool PickFrame(int track, int frame)
    {
        bool found = _animation.SelectByTag($"f:{track}:{frame}");
        if (found) ShowSelectedFrame();
        return found;
    }

    /// Types a position into the box and presses the button, which is the whole edit path.
    public void TypeFramePosition(string text)
    {
        _framePosition.Text = text;
        SetFrame();
    }

    /// Presses the save button. Only worth driving on a copy: it writes the file.
    public void PressSaveAnimation() => SaveAnimation();

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

        // The question a variable driven clip actually asks: I am about to set the fraction to this,
        // which pose am I asking for. Answering it needs the page to move to that frame, not just a
        // number printed somewhere, so this jumps and marks the row.
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

        // Changing a frame. Nothing here re-encodes a compressed animation, so a saved clip is
        // written out uncompressed, which is a much larger file holding exactly the frames that went
        // into it. That is said on the button rather than discovered afterwards.
        var apply = Ux.Secondary("Set frame");
        apply.Click += (_, _) => SetFrame();
        var write = Ux.Primary("Save uncompressed");
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

    /// Fills the boxes from whichever frame row is selected, so a frame is changed by picking it
    /// rather than by typing where it is.
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

    private static string Triple(System.Numerics.Vector3 v) => $"{F(v.X)}, {F(v.Y)}, {F(v.Z)}";

    private static string F(float value) =>
        value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    /// Writes the boxes back into the frame they came from.
    ///
    /// Only into the decoded animation held in memory. Nothing reaches the file until it is saved,
    /// which is what makes several frames changeable before anything is written.
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

        // A track with no scale of its own prints none, so an empty box means leave it as it was
        // rather than set it to nothing.
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
                                System.Globalization.CultureInfo.InvariantCulture, out values[i]))
                return false;

        return true;
    }

    /// Writes the clip back, uncompressed.
    ///
    /// Nothing here re-encodes a compressed animation, so what goes back is
    /// `hkaInterleavedUncompressedAnimation`: every frame of every track stored as it is. The file
    /// gets much larger and the frames are exact. The clip that was there is left in the file
    /// unreferenced, so nothing already in it moves.
    private void SaveAnimation()
    {
        var anim = _animationData;
        if (anim == null)
        {
            _frameEditAnswer.Text = "This is not an animation file.";
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return;
        }

        if (_readOnly)
        {
            _frameEditAnswer.Text = "Not saved: " + _readOnlyWhy;
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return;
        }

        string? blocked = HkxTextEdit.WhyNotWritable(_hkxPath);
        if (blocked != null)
        {
            _frameEditAnswer.Text = "Cannot save: " + blocked;
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return;
        }

        try
        {
            var written = NativeAnimation.Interleave(_hkxPath, anim);

            string backup = _hkxPath + ".bak";
            if (!File.Exists(backup)) File.Copy(_hkxPath, backup);
            ReplaceFile(_hkxPath, written.Bytes);

            _animationEdited = false;
            string said =
                $"Saved {written.Frames} frame(s) of {written.Tracks} track(s) uncompressed, " +
                $"{written.Grew} bytes larger. The original is kept as {Path.GetFileName(backup)}.";

            // Reloaded first and told afterwards. Opening a file clears these boxes, which is right
            // when a different file is opened and wrong here: saying it before the reload put the
            // message up and wiped it in the same breath, so pressing Save appeared to do nothing.
            Load();
            _frameEditAnswer.Text = said;
            _frameEditAnswer.Foreground = Ux.MetaBrush;
            SetStatus(said, Ux.MetaBrush);
        }
        catch (Exception e)
        {
            _frameEditAnswer.Text = "Not saved, and the original is untouched: " + e.Message;
            _frameEditAnswer.Foreground = Ux.BadBrush;
        }
    }

    /// userControlledTimeFraction is what a bound variable drives, so this is the lookup the clip work
    /// needs: type the fraction, land on the frame, see the transform that plays there.
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
                            System.Globalization.CultureInfo.InvariantCulture, out float fraction))
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

        // Land the page on the frame rather than leaving the reader to find it.
        _frameStart = _aimedFrame / FramesPerPage * FramesPerPage;
        ShowAnimationFrames();
    }

    // A page of frames rather than a cap. The old grid stopped at 300 rows per track and said how
    // many it had dropped, which is honest but leaves the rest of a long animation unreachable.
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

    // An animation is a different kind of file from a behaviour, so this runs on its own rather than
    // hanging off the behaviour parse. A class the reader cannot decode has to say so here, in the
    // panel, and not only on the console: the whole point of making the reader refuse loudly was
    // that somebody using the window finds out.
    private bool BuildAnimation(string path)
    {
        _animation.Clear();
        _animationData = null;
        _animationSkeleton = null;
        _frameStart = 0;
        // A frame aimed at in the last file means nothing in this one, and neither does a frame
        // picked for editing or a change made to one.
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

            // Filtering to one bone is what makes this a browser rather than a wall: a character
            // animation has 95 tracks, and reading one bone's motion means seeing only that bone.
            var head = _animation.Add(null, name, frames.ToString(), "", "", "", scaled ? "scaled" : "")
                                 .Colour(0, Ux.TitleBrush).Colour(1, Ux.DisabledBrush).Colour(5, Ux.BadBrush);
            if (needle.Length == 0) head.Collapse();

            for (int f = _frameStart; f < Math.Min(last, frames); f++)
            {
                string pos = f < track.Translations.Count
                    ? $"{track.Translations[f].X:F3}, {track.Translations[f].Y:F3}, {track.Translations[f].Z:F3}" : "";
                string rot = f < track.Rotations.Count
                    ? $"{track.Rotations[f].X:F4}, {track.Rotations[f].Y:F4}, {track.Rotations[f].Z:F4}, {track.Rotations[f].W:F4}" : "";
                // Only the tracks that carry a scale print one. Filling every row of every animation
                // with 1.000, 1.000, 1.000 would bury the 130 vanilla animations that actually scale.
                string scl = scaled && f < track.Scales.Count
                    ? $"{track.Scales[f].X:F4}, {track.Scales[f].Y:F4}, {track.Scales[f].Z:F4}" : "";
                bool aimed = f == _aimedFrame;
                // Tagged with where it sits, because nothing in the file names a frame. That is what
                // makes a row selectable and therefore changeable.
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

    // The bone names live in the skeleton, not the animation. An animation's annotation tracks are
    // named after bones by convention and are empty in plenty of vanilla files, so the real name
    // comes through transformTrackToBoneIndices.
    private static HkxSkeleton? SiblingSkeleton(string animationPath)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(animationPath)) ?? "");
        for (int up = 0; up < 4 && dir != null; up++, dir = dir.Parent)
        {
            string assets = Path.Combine(dir.FullName, "CharacterAssets");
            if (!Directory.Exists(assets)) continue;

            foreach (string file in Directory.EnumerateFiles(assets, "*.hkx").OrderBy(f => f))
            {
                try { return new HkxBinaryReader().ReadSkeleton(file); }
                catch { /* not a skeleton, try the next */ }
            }
        }
        return null;
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

    // A clip generator names an animation, and until now the only way to know what that animation was
    // was to remember it. Nothing here writes to the document: scrubbing and playing are views of a
    // file on disk, so they take no undo step and cannot make the graph dirty.
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
            // Set from code as well as dragged, so without this every ShowFrame would call itself
            // back through the slider.
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
        panel.Children.Add(WithClipPicker(Framed(_skeleton)));

        // Says that a model is a second, separate step. What plays here is the skeleton, and waiting
        // for a character to appear on its own is waiting for something that never happens.
        SetPlaybackSummary("Open a behaviour and select a clip to see what it plays. That animates " +
                           "the skeleton; use Mesh... to hang a model on it.", Ux.MutedBrush);
        return panel;
    }

    /// The rig this behaviour belongs to. The behaviour file does not name one; the character does,
    /// which is why this comes off the chain rather than off the open file. Cached because selecting
    /// a clip resolves it, and re-reading a 95 bone skeleton off disk on every click is felt.
    private HkxSkeleton? PoseSkeleton()
    {
        if (_cachedSkeletonFor == _hkxPath && _cachedSkeleton != null) return _cachedSkeleton;

        _cachedSkeletonFor = _hkxPath;
        _cachedSkeleton = _projectChain?.Skeleton ?? SiblingSkeleton(_hkxPath);
        return _cachedSkeleton;
    }

    // Selecting a clip is what asks the question, so selecting one is what answers it. Silent when
    // the selection is not a clip, or names an animation that is not on disk: a behaviour is mostly
    // nodes that play nothing, and a message per click would be noise.
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
        _poseSkeleton = PoseSkeleton();

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
            // The rig is still worth drawing: it is the thing the reader is trying to place the
            // animation onto, and an empty box says nothing at all.
            if (_poseSkeleton != null) _skeleton.Show(AnimationPose.ReferencePose(_poseSkeleton));
            _poseAnimation = null;
            _poseSource = "";
            _scrub.Maximum = 0;
            return;
        }

        _poseAnimation = animation;
        _poseSource = animationPath;
        _poseFrame = 0;

        // Read off the file rather than off the decoded tracks, because it is not in them: the
        // displacement lives in its own object and never reaches a bone.
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

        // Travel is said here whether or not it is being drawn, because it is invisible otherwise:
        // the bones stay on the spot, so a clip that takes the character 1,060 units looks exactly
        // like one that goes nowhere until this line says so.
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

    /// Opens a behaviour straight out of a BA2, without unpacking the archive around it.
    ///
    /// Every behaviour in the game is inside Fallout4 - Animations.ba2, and reaching one of them used
    /// to mean writing all 29,716 entries to disk first. Reading the index takes about a second and
    /// touches no file data, so the browser lists the archive itself.
    ///
    /// The chosen file is written to a temporary folder and opened from there, because everything
    /// downstream of here works on a path: the project chain, the animation reader, the mesh, the
    /// validator. What it is not is somewhere to save. The window goes read only, and says so, rather
    /// than letting an edit land in a temporary file the user will never find again.
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
                // Under a folder named for the archive, so two files of the same name out of two
                // archives do not land on top of each other, and the folder is one a person can find
                // if they want the copy afterwards.
                string folder = Path.Combine(Path.GetTempPath(), "BehaviourGraphStudio",
                                             Path.GetFileNameWithoutExtension(archivePath));
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

    // The behaviour chain names no mesh, and neither does the skeleton, so the only honest way to
    // find one today is to be told. Pointing at a .nif is that, and the race record lookup that would
    // do it automatically is a separate job.
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

    /// Returns false when the mesh could not be drawn, having already said why. The caller must not
    /// then overwrite that with a message about the mesh it thinks it loaded.
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
        Settings.Set("last_mesh_folder", Path.GetDirectoryName(path) ?? "");

        int vertices = _meshShapes.Sum(m => m.Shape.Vertices.Count);
        int edges = _meshShapes.Sum(m => m.Edges.Count);

        // A bone the mesh names and the skeleton does not is the failure that shows up as a limb
        // quietly missing, so it is named here rather than left to be noticed.
        var missing = _meshShapes.SelectMany(m => m.Binding.Unmatched).Distinct().ToList();
        float drift = _meshShapes.Max(m => SkinnedMesh.BindError(m.Shape, m.Binding, skeleton));

        string report = $"{Path.GetFileName(path)}   {_meshShapes.Count} shapes, {vertices} vertices, " +
                        $"{edges} edges   drift from the rest pose {drift:F2}";
        if (missing.Count > 0)
            report += $"   {missing.Count} bone{(missing.Count == 1 ? "" : "s")} did not match this " +
                      $"skeleton: {string.Join(", ", missing.Take(6))}" +
                      (missing.Count > 6 ? ", and more" : "") +
                      ". Vertices weighted only to those stay at their rest position.";

        SetPlaybackSummary(report, missing.Count > 0 ? Ux.WarnBrush : Ux.MetaBrush);
        ShowFrame(_poseFrame, stop: false);
        _skeleton.Frame();
        return true;
    }

    /// Writes beside the target and moves the finished file into place. WriteAllBytes truncates
    /// before it writes, so a disk filling up or a mod manager taking the file mid write would leave
    /// the game's own file empty, with nothing but the backup to show it ever had contents.
    private static void ReplaceFile(string path, byte[] bytes)
    {
        string staging = path + ".writing";
        try
        {
            File.WriteAllBytes(staging, bytes);
            File.Move(staging, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(staging)) File.Delete(staging); } catch (IOException) { }
            throw;
        }
    }

    private void ClearMesh()
    {
        _meshShapes.Clear();
        _meshPath = "";
        _skeleton.ShowMesh(null);
    }

    // Only the vertex positions are recomputed per frame. The edge list and the bone matching are
    // worked out when the mesh is loaded and do not change while scrubbing.
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
        // Left behind, the last clip's travel would be reported for the next one, and a stationary
        // clip would be drawn walking down the path of the one before it.
        _poseMotion = new RootMotion.Motion();
        _cachedSkeleton = null;
        _cachedSkeletonFor = "";
        _scrubbing = true;
        _scrub.Maximum = 0;
        _scrub.Value = 0;
        _scrubbing = false;
        _skeleton.Reset();
        _frameLabel.Text = "";
        // Says that a model is a second, separate step. What plays here is the skeleton, and waiting
        // for a character to appear on its own is waiting for something that never happens.
        SetPlaybackSummary("Open a behaviour and select a clip to see what it plays. That animates " +
                           "the skeleton; use Mesh... to hang a model on it.", Ux.MutedBrush);
    }

    // Every clip in the file down the right hand side, with its own properties under it, so a clip
    // can be found, played and changed without leaving playback for the tree or the graph.
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

    // Hangs the character's own model on the skeleton when there is exactly one obvious candidate.
    // Loading it here rather than on the first frame means the viewport is still empty until a clip
    // is picked, which is what a mesh with no pose should look like.
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
    }

    private void TogglePlay()
    {
        if (_poseAnimation == null || _poseAnimation.NumFrames <= 1)
        {
            SetPlaybackSummary("Nothing loaded to play. Select a clip, or press From selected node.", Ux.MutedBrush);
            return;
        }

        if (_clock != null) { Stop(); return; }

        // The selected clip's own playbackSpeed, so changing it here shows the change here. Without
        // this the preview always ran at the animation's native rate and an edited speed looked like
        // an edit that had not taken, when it had and had been saved.
        _clock = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(
                Math.Clamp(_poseAnimation.FrameDuration / SelectedPlaybackSpeed(), 1 / 120f, 4)),
        };
        // Looping rather than stopping at the end: nearly every clip in a behaviour graph is a loop,
        // and one that is not still reads better repeating than freezing on its last frame.
        _clock.Tick += (_, _) => ShowFrame(_poseFrame + 1 > _scrub.Maximum ? 0 : _poseFrame + 1, stop: false);
        _clock.Start();
        _playButton.Content = "Pause";
    }

    /// How fast the selected clip says to play, or full speed when nothing sensible is set. Zero and
    /// negative are treated as full speed rather than as a stopped or reversed preview: the engine
    /// reads them as its own thing, and guessing which would be inventing behaviour.
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

        // Update, not Show: re-fitting on every frame would jump the camera about as the pose's own
        // bounds change under it.
        var posed = AnimationPose.At(_poseSkeleton, _poseAnimation, _poseFrame);
        if (_followTravel) posed = WithTravel(posed);

        _skeleton.Update(posed);
        UpdateMesh(posed, _poseSkeleton);

        _scrubbing = true;
        _scrub.Value = _poseFrame;
        _scrubbing = false;
        UpdateFrameLabel();
    }

    /// The same pose, moved along the path the clip carries.
    ///
    /// Motion is extracted in this format, so a walk plays on the spot and the displacement lives in
    /// its own object. Drawing it means putting the two back together, which is what the game does to
    /// the object rather than to the rig. The turn is about the animation's own up axis rather than
    /// an assumed one, because the file states which axis that is.
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

    /// Read only, for the window checks.
    public SkeletonView Viewport => _skeleton;
    public int PoseFrame => _poseFrame;
    public int PoseFrameCount => _poseAnimation?.NumFrames ?? 0;
    public string PlaybackSummary => _playbackSummary.Text ?? "";
    public bool IsPlaying => _clock != null;

    /// Drives playback through the same handlers the buttons use.
    public void ScrubTo(int frame) => ShowFrame(frame, stop: true);
    public void LoadPoseFrom(string animationPath) => LoadPose(animationPath, Path.GetFileName(animationPath));
    public AnimationPose.Pose? PoseNow =>
        _poseSkeleton == null ? null : AnimationPose.At(_poseSkeleton, _poseAnimation, _poseFrame);

    // Two mods edit one behaviour and the question is what each of them touched. The object walk is
    // the same one the repack guard uses; the only difference is that it is pointed at somebody
    // else's file instead of at this file's own repack.
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

        string? java = HkxTextEdit.FindJava(Settings.Get("java"));
        string? jar = HkxTextEdit.FindHkxPack(Settings.Get("hkxpack"), AppContext.BaseDirectory);

        if (_xmlText.Length == 0)
        {
            SetDiffSummary("Nothing is open to compare against.", Ux.BadBrush);
            return;
        }

        _diff.Clear();
        SetDiffSummary($"Unpacking {Path.GetFileName(other)}...", Ux.MutedBrush);

        BehaviourDiff.Result result;
        try
        {
            string mine = _xmlText;
            result = await Task.Run(() => ComputeDiff(mine, other, java, jar));
        }
        catch (Exception ex)
        {
            SetDiffSummary($"Could not read {Path.GetFileName(other)}: {ex.Message.Split('\n')[0]}", Ux.BadBrush);
            return;
        }

        ShowDiff(Path.GetFileName(other), result);
    }

    /// The other file's text, written from its bytes where the class table describes it and unpacked
    /// with hkxpack where it does not. Same order as opening a file, and for the same reason:
    /// comparing is a reading, and a reading should not need Java.
    private static string TextOf(string path, string? java, string? jar)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var objects = new PackfileObjects(PackfileImage.Read(bytes));

            if (HavokClassTypes.Shipped.SignatureProblems(objects.ClassNames()).Count == 0)
                return NativeXml.From(bytes);
        }
        catch (Exception) { }

        if (java == null || jar == null) return "";

        string work = Path.Combine(Path.GetTempPath(), "bgs_compare");
        HkxTextEdit.ResetDirectory(work);
        return HkxTextEdit.ReadXml(HkxTextEdit.Unpack(java, jar, path, work));
    }

    private static BehaviourDiff.Result ComputeDiff(string mine, string other, string? java, string? jar)
    {
        string theirs = TextOf(other, java, jar);
        if (theirs.Length == 0)
            throw new InvalidOperationException(
                "this file's classes are not ones this build describes, and there is no hkxpack " +
                "to fall back on");

        return BehaviourDiff.Compare(RepackCheck.Take(mine), RepackCheck.Take(theirs));
    }

    /// Runs the comparison through the same code the picker feeds, so a check exercises what a person
    /// does rather than a parallel path. Returns what the panel now says.
    public string CompareLoadedWith(string other)
    {
        string? java = HkxTextEdit.FindJava(Settings.Get("java"));
        string? jar = HkxTextEdit.FindHkxPack(Settings.Get("hkxpack"), AppContext.BaseDirectory);
        if (_xmlText.Length == 0) return "";

        ShowDiff(Path.GetFileName(other), ComputeDiff(_xmlText, other, java, jar));
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

    /// Read only, for the window checks.
    public HkGrid DiffGrid => _diff;

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

    // A transition can listen for an event nothing in its own file sends, which looks broken and
    // usually is not: the sender is a script. Pointing at the Papyrus sources answers it. Optional
    // throughout, and silent when it is not set.
    private async Task PickScriptsFolder()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Where the Papyrus .psc sources are",
            AllowMultiple = false,
        });

        string? folder = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (folder == null) return;

        Settings.Set("scripts", folder);
        _papyrusScanned = true;
        _papyrus = await Task.Run(() => PapyrusEvents.Scan(folder));
        SetStatus(_papyrus.ToString(), _papyrus.ScriptsRead == 0 ? Ux.MutedBrush : Ux.MetaBrush);

        if (_xmlText.Length > 0) BuildSymbols(Model());
    }

    // Opens where the last file came from. Behaviours, characters, skeletons and animations all sit
    // in sibling folders of one project, so the next file wanted is nearly always a couple of clicks
    // from the last one rather than somewhere new.
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

        Settings.Set("last_folder", Path.GetDirectoryName(path) ?? "");
        Open(path);
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

    /// The mesh to draw on this file's skeleton, named from outside because nothing inside a
    /// behaviour, a character or a skeleton names one.
    public void OpenMesh(string nifPath) => LoadMesh(nifPath);

    public int MeshEdges => _skeleton.DrawnEdges;

    private void Load()
    {
        _tree.Clear();
        _clips.Clear();
        ClearProps();
        _offsetToIndex.Clear();
        _objectIds = new List<string>();
        _bytes = null;
        _editedFields.Clear();
        _xmlText = "";
        _xmlPath = "";
        // Cleared with the text. Left behind, the previous file's reading would answer for a file
        // that failed to open.
        _reading = new BehaviourGraphModel();
        _selectedId = "";
        _projectChain = null;
        _emptyStates = new HashSet<string>();
        // Object ids start again at #1 in the next file, so anything the canvas remembers by id is
        // about to be applied to a different object entirely.
        _graph.Reset();
        ClearPose();
        ResetHistory();
        _readOnly = false;
        _readOnlyWhy = "";

        string path = (_pathField.Text ?? "").Trim().Trim('"');
        if (path.Length == 0) { SetSummary("Enter the path to a .hkx file.", Ux.MutedBrush); return; }
        if (!File.Exists(path))
        {
            // Says where a relative path actually landed. Relative to the working directory the app
            // was started in, not to the file box, so the same text works from one place and not
            // another and the bare path in the message looks correct while being wrong.
            string full = Path.GetFullPath(path);
            SetSummary(full == path ? "Not found: " + path
                                    : $"Not found: {path}, which from here means {full}", Ux.BadBrush);
            return;
        }
        if (!HkxBinaryReader.IsFo4Hkx(path))
        {
            SetSummary("Not a Fallout 4 hk_2014.1.0-r1 packfile.", Ux.MutedBrush);
            return;
        }

        // Animations are read on their own path. A behaviour file simply has no animation object in
        // it and the tab says so, but an animation file has no behaviour root, so this has to happen
        // before the root check below rejects it.
        bool isAnimation = BuildAnimation(path);

        var root = HkxBehaviorParser.ParseBehavior(path);
        if (root == null)
        {
            _hkxPath = path;
            Settings.Set("last_path", path);
            Settings.Set("last_folder", Path.GetDirectoryName(path) ?? "");
            SetSummary(isAnimation
                ? $"{Path.GetFileName(path)}   an animation, not a behaviour. See the Animation and Playback tabs."
                : "Parsed as FO4 hkx, but no root object was resolved.", Ux.MutedBrush);
            SetStatus(_animationSummary.Text ?? "", _animationSummary.Foreground ?? Ux.MutedBrush);

            // An animation opened on its own has no clip pointing at it, so the selection path never
            // fires. It is still the thing on screen, so it is what plays.
            if (_animationData != null) LoadPose(path, Path.GetFileName(path));
            return;
        }

        // A behaviour is a different layout entirely and the animation reader refuses it outright,
        // which is true but reads as a fault. Once the behaviour parse has succeeded, say the plain
        // thing instead.
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

        // Not fatal if it fails: the panel falls back to hkxpack field by field, so a file this
        // cannot take apart still shows its values, just none of them from the bytes.
        _classWarning = "";
        try
        {
            var bytes = new PackfileObjects(PackfileImage.Read(path));

            // A packfile stores the signature of every class it names, and a signature is what a
            // class definition is. If the file's disagrees with ours then this build's idea of where
            // a field sits was written for a different version of that class, and reading a value
            // out of it would be quiet nonsense rather than an error. So the bytes are put aside
            // and the panel goes back to reading through hkxpack, which reads the file's own
            // definitions rather than ours.
            var problems = HavokClassTypes.Shipped.SignatureProblems(bytes.ClassNames());
            if (problems.Count > 0)
            {
                _classWarning = $"Read through hkxpack only: {problems[0]}" +
                                (problems.Count > 1 ? $", and {problems.Count - 1} more like it" : "") +
                                ". Values are not read from the bytes when the classes do not match.";
                _bytes = null;
            }
            else _bytes = bytes;
        }
        catch (Exception) { _bytes = null; }

        var classes = new HashSet<string>();
        int clips = 0;
        foreach (var o in _objects)
        {
            classes.Add(o.ClassName);
            if (!string.IsNullOrEmpty(o.AnimationName)) clips++;
        }

        // The class warning goes here rather than on the status line because the status line has
        // four ways out of PrepareEditing and only one of them was carrying it: a file with classes
        // we do not describe *and* no Java present reported the Java and swallowed the rest.
        SetSummary($"{Path.GetFileName(path)}   root {root.ClassName}   {_objects.Count} objects   " +
                   $"{classes.Count} classes   {clips} clip references" +
                   (_classWarning.Length > 0 ? "   —   " + _classWarning : ""),
                   _classWarning.Length > 0 ? Ux.WarnBrush : Ux.TitleBrush);

        RebuildTree();
        Settings.Set("last_path", path);
        Settings.Set("last_folder", Path.GetDirectoryName(path) ?? "");
        PrepareEditing();

        // An animation file parses as a graph of objects too, so it comes down this path rather than
        // the one above. Either way, if the open file holds frames then it is the thing on screen and
        // it is what plays.
        if (_animationData != null) LoadPose(path, Path.GetFileName(path));
    }

    /// The reading everything else works from.
    ///
    /// The text is authoritative while there is any, because an edit is made by rewriting it and the
    /// bytes on disk are then out of date until the file is saved. With no text there is nothing to
    /// be out of date, so the reading taken from the file when it was opened is the answer. That is
    /// what lets the graph, the symbols and the properties fill on a machine with no Java on it.
    private BehaviourGraphModel Model() =>
        _xmlText.Length > 0 ? BehaviourGraphModel.Parse(_xmlText) : _reading;

    private BehaviourGraphModel _reading = new();

    private void PrepareEditing()
    {
        string? java = HkxTextEdit.FindJava(Settings.Get("java"));
        string? jar = HkxTextEdit.FindHkxPack(Settings.Get("hkxpack"), AppContext.BaseDirectory);
        bool text = java != null && jar != null;

        _findJava.IsVisible = java == null;

        // The graph comes out of the file's own bytes now. It used to come out of hkxpack's text,
        // which is why a machine without Java showed a tree and four empty tabs. The two readings
        // were compared field by field and consumer by consumer across every vanilla behaviour and
        // came out the same, so this is the same picture drawn without the dependency.
        var reading = _bytes == null ? null : NativeGraphModel.From(_bytes);

        // The text form is written from the file's own bytes when the class table can describe it,
        // and unpacked with hkxpack when it cannot. That is what takes Java off the editing path as
        // well as the reading one: an edit is made by rewriting this text, so with no text every edit
        // was refused.
        //
        // The two texts were set against each other line by line across every vanilla behaviour. Of
        // the 370 files hkxpack reads correctly, all 370 come out identical, 385,773 lines of them.
        // The other 128 hold a class hkxpack strides wrongly, so its own text is misaligned and there
        // is nothing there to match.
        bool own = false;
        if (_bytes != null && reading != null)
        {
            try
            {
                string work = Path.Combine(Path.GetTempPath(), "bgs_edit",
                                           Path.GetFileNameWithoutExtension(_hkxPath));
                HkxTextEdit.ResetDirectory(work);

                // Written to disk as well as held in memory, because saving a structural change still
                // packs this file back through hkxpack when Java is there to do it.
                _xmlPath = Path.Combine(work, Path.GetFileNameWithoutExtension(_hkxPath) + ".xml");
                // Read off disk rather than from the objects already in hand, because the writer
                // needs the file's header as well as its objects and the file has not changed since
                // it was opened.
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

        if (text && !own)
        {
            try
            {
                string work = Path.Combine(Path.GetTempPath(), "bgs_edit",
                                           Path.GetFileNameWithoutExtension(_hkxPath));
                HkxTextEdit.ResetDirectory(work);

                _xmlPath = HkxTextEdit.Unpack(java!, jar!, _hkxPath, work);
                _xmlText = HkxTextEdit.ReadXml(_xmlPath);
                _objectIds = HkxTextEdit.ObjectIds(_xmlText);

                // Editing works by rewriting the text, so a text form that does not line up with the
                // file is not something to edit through. The picture below still draws.
                if (_objectIds.Count != _objects.Count)
                {
                    _xmlText = "";
                    _objectIds = new List<string>();
                }
            }
            catch (Exception ex)
            {
                _xmlText = "";
                _objectIds = new List<string>();
                if (reading == null)
                {
                    ResetHistory();
                    SetStatus("Read only: " + ex.Message.Split('\n')[0], Ux.MutedBrush);
                    return;
                }
            }
        }

        ResetHistory();

        var model = reading ?? (_xmlText.Length > 0 ? Model() : null);
        if (model == null)
        {
            string missing = java == null && jar == null ? "Java and hkxpack are missing"
                           : java == null ? "Java is missing"
                           : jar == null ? "hkxpack-cli.jar is missing"
                           : "the file's classes are not ones this build describes";

            SetStatus("Read only, so the Graph, Symbols, Chain and Animation tabs stay empty: " +
                      missing + ". The tree is read straight from the binary and does not need " +
                      "either. " +
                      (java == null ? "Install a Java runtime, or press Find Java if one is already installed somewhere this did not look. " : "") +
                      (jar == null ? $"Put hkxpack-cli.jar in a tools folder beside the program, at {Path.Combine(AppContext.BaseDirectory, "tools")}. " : "") +
                      "Save stays off until then.", Ux.WarnBrush);
            return;
        }

        // Both readings number the objects the same way, so either can say which id sits at which
        // position. The text's list is preferred only because editing writes back through it.
        if (_objectIds.Count == 0) _objectIds = model.Objects.Select(o => o.Id).ToList();
        _reading = model;

        // The tree drew before the model existed, so the states holding nothing were unknown when it
        // was built. Now they are known, so it is built again.
        _emptyStates = GraphValidator.StatesWithNoGenerator(model);
        RebuildTree();

        _graph.Show(model);
        _graph.FrameAll();
        BuildSymbols(model);
        BuildClipList(model);
        if (text) BuildChain(java!, jar!);
        FindMeshForFile();

        string source = reading != null ? "read from the file itself" : "read through hkxpack";
        SetStatus(_xmlText.Length > 0
            ? $"Editable. {_objectIds.Count} objects mapped, {_graph.DrawnCount} drawn, {source}."
            : $"{_objectIds.Count} objects mapped, {_graph.DrawnCount} drawn, {source}. " +
              "Editing and saving still need Java and hkxpack-cli.jar.",
            _xmlText.Length > 0 ? Ux.MetaBrush : Ux.WarnBrush);
    }

    private bool IsEmptyState(int offset) =>
        _emptyStates.Count > 0
        && _offsetToIndex.TryGetValue(offset, out int index)
        && index < _objectIds.Count
        && _emptyStates.Contains(_objectIds[index]);

    // The properties panel is not cleared here: the tree is rebuilt on every keystroke in the filter,
    // and the node whose fields are open is usually the reason the filter is being typed.
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

    // The box sits above the tabs and used to drive only the tree, so on the Graph tab typing in it
    // did nothing at all. It filters whichever view is showing now. The canvas dims rather than
    // jumping while you type; Enter is what moves the view onto the first match.
    private void ApplyFilter()
    {
        RebuildTree();
        if (_xmlText.Length == 0) return;

        string needle = (_filter.Text ?? "").Trim();
        _graph.Filter(needle);

        if (needle.Length == 0)
        {
            SetStatus($"Editable. {_objectIds.Count} objects mapped, {_graph.DrawnCount} drawn.", Ux.MetaBrush);
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

    /// Drives the filter through the same handlers typing does.
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
        // Offsets map to xml object ids the same way selection resolves them, so a state the graph
        // marks as empty is the same row the tree marks.
        bool empty = IsEmptyState(node.Offset);

        var row = _tree.Add(parent, repeat ? label + "  (shown above)" : label,
                            empty ? node.ClassName + "  no generator" : node.ClassName,
                            node.AnimationName, "0x" + node.Offset.ToString("X"));
        row.Colour(0, empty ? Ux.BadBrush : parent == null ? Ux.TitleBrush : repeat ? Ux.DisabledBrush : Ux.MetaBrush)
           .Colour(2, empty ? Ux.BadBrush : Ux.CodeBrush).Colour(3, Ux.DisabledBrush).Tag(node.Offset);

        if (repeat) return;
        foreach (var child in node.Children) AddTreeNode(child, row, seen, ref rows);
    }

    // Through the same handler a canvas click uses, not a parallel one. Filling only the properties
    // panel here is what left the tree unable to load a pose, and would leave it behind again the
    // next time selecting a node has to do something else as well.
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

        // One parse for both. On a weapon behaviour a parse is a tenth of a second, and selecting a
        // node was paying for two of them.
        var model = Model();
        ShowProps(objectId, model);
        SetStatus(Describe(model, objectId), Ux.MetaBrush);

        // Selecting a clip is what asks what it plays, so it is what answers it. Quiet when the
        // selection plays nothing, which is most nodes in a graph.
        LoadPoseFromSelection(announce: false);
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

    // Both panels are filled, because which one is on screen depends on the tab and a node can be
    // reached from either. The model is parsed once and handed to both: on a shipped weapon graph
    // that parse is the expensive part of selecting a node.
    private void ShowProps(string objectId) => ShowProps(objectId, Model());

    private void ShowProps(string objectId, BehaviourGraphModel model)
    {
        _selectedId = objectId;
        // One list for all three panels, cleared once here rather than in FillProps, which runs
        // three times and would leave only the last panel's boxes registered.
        _fieldCommits.Clear();
        FillProps(_treeProps, objectId, model);
        FillProps(_graphProps, objectId, model);
        FillProps(_clipProps, objectId, model);
        _clips.SelectByTag(objectId);
    }

    /// The values the panel shows, read from the file's own bytes wherever they can be, and from
    /// hkxpack's text for the fields they cannot: a struct written inline is the only kind left, and
    /// a handful of those should not decide where the other forty values come from.
    private List<PanelFields.Field> PanelValues(string objectId,
                                                IReadOnlyList<HkxTextEdit.Param> parameters)
    {
        var plain = parameters.Select(p => (p.Name, p.Value)).ToList();

        int index = _objectIds.IndexOf(objectId);
        if (_bytes == null || index < 0 || index >= _bytes.Instances.Count)
            return plain.Select(p => new PanelFields.Field(p.Name, p.Value,
                                                          PanelFields.Source.Fallback, p.Value))
                        .ToList();

        // The id the rest of the window is keyed on, for whatever an object points at. Both lists are
        // in file order and the load refuses to go on unless they are the same length, so the
        // position in one is the position in the other.
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
                               (fromXml > 0 ? $", {fromXml} of them read through hkxpack" : ""));
        heading.TextWrapping = TextWrapping.Wrap;
        panel.Add(heading);

        // An array of structs is shown one element at a time rather than as one flat run of boxes.
        // The file writes an element's fields together, so the fields sharing a group arrive
        // together and a run of them is an element. A transition array with five transitions in it
        // is eighty boxes laid out flat, with every name repeated once per element and nothing
        // saying where one transition ends and the next begins.
        var summaries = ElementSummary.For(model, objectId);

        for (int i = 0; i < parameters.Count;)
        {
            string group = parameters[i].Group;
            if (group.Length == 0)
            {
                panel.Add(FieldRow(parameters[i], objectId));
                i++;
                continue;
            }

            int end = i;
            while (end < parameters.Count && parameters[end].Group == group) end++;

            var inside = new StackPanel { Spacing = 6, Margin = new Thickness(8, 4, 0, 4) };
            for (int f = i; f < end; f++) inside.Children.Add(FieldRow(parameters[f], objectId));

            panel.Add(ElementBlock(group, summaries.GetValueOrDefault(group, ""), inside));
            i = end;
        }

        AddSymbolSection(panel, objectId, model);
        AddBindingSection(panel, objectId, model);
    }

    /// One element of an array of structs, behind a line saying what it is.
    ///
    /// Collapsed to start with, because the point is that five transitions read as five lines. The
    /// summary is what the element means rather than what it is called, and an element with no
    /// summary shows its index alone: a made up description would read as a fact about the file.
    private static Control ElementBlock(string group, string summary, Control inside)
    {
        var header = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };

        var index = Ux.Label(group);
        index.Foreground = Ux.MutedBrush;
        header.Children.Add(index);

        if (summary.Length > 0)
        {
            var said = Ux.Label(summary);
            said.Foreground = Ux.CodeBrush;
            said.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(said, summary);
            header.Children.Add(said);
        }

        return new Expander
        {
            Header = header,
            Content = inside,
            IsExpanded = false,
            Padding = new Thickness(0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        };
    }

    private Control FieldRow(PanelFields.Field p, string owner)
    {
        // An enum whose values the class table declares becomes a list rather than a box. The names
        // are the ones the game registers, not ours, and PanelFields only offers them when the value
        // already in the file is one of them, so picking from the list can never be the only way to
        // keep what is there.
        if (p.Options.Count > 0) return EnumRow(p, owner);

        string address = p.Address;
        string original = p.Value;

        var field = Ux.Field();
        field.Text = p.Value;
        // Committing is driven by what the box holds, not by which box has focus. Focus is the
        // usual trigger, but a window closing has no focus change to hang off, and asking every
        // field whether it differs is both simpler and safe: one that has not been touched
        // commits nothing, and one already committed commits nothing twice.
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
        // The name alone does not say which of them this is, and inside an element there are several
        // with the same name, so the tip carries the address the edit will be written to.
        ToolTip.SetTip(label, address);

        var row = new DockPanel();
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        row.Children.Add(field);
        return row;
    }

    // The other direction of the usages question: not who touches this symbol, but which symbols this
    // node touches. An index on its own says nothing, so each one is resolved to its declared name.
    /// One property row whose value is chosen from a list rather than typed.
    ///
    /// Committing is driven by what the control holds, the same way a text box is, so a window
    /// closing with a changed selection still writes it.
    private Control EnumRow(PanelFields.Field p, string owner)
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
        ToolTip.SetTip(label, $"{name}: one of {string.Join(", ", p.Options)}");

        var row = new DockPanel();
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        row.Children.Add(choice);
        return row;
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

        // Stacked rather than one row: the member path alone is wider than this panel, and a row
        // that does not fit gets its left end cut off rather than shrinking.
        var member = Ux.Field("member, e.g. userControlledTimeFraction");
        var variable = Ux.Field("variable name");
        var bind = Ux.Secondary("Bind");
        bind.HorizontalAlignment = HorizontalAlignment.Right;
        bind.Click += (_, _) => AddBinding(objectId, (member.Text ?? "").Trim(), (variable.Text ?? "").Trim());

        panel.Add(member);
        panel.Add(variable);
        panel.Add(bind);
    }

    // Scanned once, on the first build that needs it, rather than at startup: a full Base folder is
    // several thousand files and nobody who never opens the Symbols tab should pay for it.
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

            // Every reader as its own row, so "is this variable used" is answered by looking rather
            // than by reading the whole file, and each answer can be clicked through to the node.
            var sites = variableSites.Where(u => u.Index == i)
                                     .GroupBy(u => (u.ObjectId, u.Owner, u.Member)).ToList();
            if (sites.Count == 0) continue;

            row.Collapse();
            foreach (var site in sites)
                AddUsageRow(row, "reads it", site.Key.ObjectId, site.Key.Owner, site.Key.Member,
                            site.Count(), "");
        }

        // The text form while there is one, because an edit rewrites it and the bytes on disk are
        // out of date until the file is saved. The reading taken when the file was opened otherwise,
        // which is what fills the roles with no Java on the machine.
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

            // Papyrus is reported as information, never as a verdict: the engine sends events itself,
            // so a name no script sends is not evidence of anything.
            if (scripts.Length == 0) continue;
            if (lines == null) row.Collapse();
            _symbols.Add(row, "papyrus", "", scripts, "", "scripts address events by name, not by index")
                    .Colour(0, Ux.MutedBrush).Colour(2, Ux.MetaBrush).Colour(4, Ux.DisabledBrush);
        }

        if (names.Count == 0 && events.Count == 0)
            _symbols.Add(null, "", "", "this graph declares no variables or events").Colour(2, Ux.DisabledBrush);
    }

    // One place that names a symbol, tagged with the object so selecting it goes there. The object's
    // own name is what a reader recognises; the class alone is not, since a graph holds hundreds of
    // the same class.
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

    // This column is what the file references, not everything that uses the symbol. Game code
    // addresses both variables and events by name rather than index, so an empty column means no
    // consumer was found in this file, not that the symbol is dead. The Pip-Boy's iTabSync and
    // iCatSync are the worked example: nothing in the graph reads them, the game writes them and
    // reads them back, and the tab switching itself runs on events.
    // One scan for every index rather than one scan per symbol. A weapon behaviour declares 873
    // symbols against seven megabytes of text, and asking one at a time took about two minutes of
    // the load.
    /// Where every index is written, out of the text while there is any and out of the file's own
    /// bytes when there is not.
    ///
    /// The text is preferred for the same reason the model is: an edit rewrites it, so it is the
    /// current state of the graph and the bytes on disk are behind until the file is saved. The two
    /// walks were compared across every vanilla behaviour, 21,624 usages, and agreed on all of them.
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

    private void BuildChain(string java, string jar)
    {
        _chain.Clear();
        var chain = ProjectChain.Resolve(_hkxPath, java, jar);
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

    // Selecting a row loads it into the edit boxes, so rename and set value act on what is on
    // screen rather than on whatever was typed last.
    private void OnSymbolSelected()
    {
        // A usage row is a place to go, not a symbol to edit. Same jump the problem list uses, so a
        // result found here lands the same way a result found by Check graph does.
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

        // Empty rather than zero where the array stops short of this variable, because the array is
        // allowed to stop short and an unbounded variable is not one bounded to zero.
        var bounds = SymbolEditor.VariableBounds(model);
        _symbolMin.Text = index < bounds.Count ? SymbolEditor.DecodeValue(type, bounds[index].Min) : "";
        _symbolMax.Text = index < bounds.Count ? SymbolEditor.DecodeValue(type, bounds[index].Max) : "";
    }

    /// Gives the selected variable a min and a max, which nothing in the window could do before: the
    /// array could be inherited from vanilla or lost, never authored, so a variable added here never
    /// got a bound at all.
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
        // Usage rows are tagged with an object id and live in the same tree, so the shape of the tag
        // is what separates them, not its length.
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
            // The name is the contract with everything outside the file: the engine's setters take
            // a name and resolve it against variableNames, and Papyrus sends events by name. A
            // rename breaks those callers silently, with no error anywhere.
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

    // A drag out to empty canvas says two things a right click does not: which slot wanted a node,
    // and where. Both are held until the menu is answered, because the menu is what says what kind.
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
            // A drag names the slot, so the node goes into that one rather than whichever slot the
            // parent's class would otherwise be given. Dragging off a clip's triggers and getting a
            // generator hung on something else is not what the wire said.
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

            // Creating the node and wiring it up is one action to whoever did it, so it is one step
            // back, not two.
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

            // Declaring the variable and binding it is one action, so undo takes both back together
            // rather than leaving an orphan variable behind.
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

    /// Commits anything typed into a property field but not yet left, as leaving it would. Saving
    /// calls this first, so a value typed and not blurred is part of what gets written rather than
    /// missing from it. Deliberately not called when the window closes: closing discards the whole
    /// unsaved document anyway, and writing a game file on an accidental close is worse than losing
    /// edits that were never saved.
    public void CommitPendingFields()
    {
        foreach (var commit in _fieldCommits.ToList()) commit();
    }

    private void Apply(string objectId, string address, TextBox field, string original)
    {
        if (field.Text == original || _xmlText.Length == 0) return;
        if (!SetParam(objectId, address, field.Text ?? "")) field.Text = original;
    }

    /// Writes one field and reports whether it took, so a control that has to put itself back on a
    /// refusal can. Shared by the typed boxes and the enum lists rather than written twice.
    ///
    /// `address` is where the field sits rather than what it is called: `transitions[1].eventId` for
    /// one element of an array of structs, and a bare name for a field the object holds directly,
    /// which is the same string it always was. Naming the field alone reached the first one with
    /// that name, so every box below the first element of an array wrote the first element's value.
    private bool SetParam(string objectId, string address, string value)
    {
        if (_xmlText.Length == 0) return false;

        try
        {
            Commit(HkxTextEdit.SetParamAt(_xmlText, objectId, address, value));
            _editedFields.Add(objectId + "." + address);
            SetStatus($"#{objectId}.{address} = {value}   (unsaved)", Ux.CodeBrush);

            // Retimes a preview that is already running, so an edited speed shows up without having
            // to stop and start playback to see it.
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

    // hkxpack checks shape, not meaning, so this is the only thing standing between a bad edit and
    // finding out in game.
    private void Validate()
    {
        if (_xmlText.Length == 0 && _reading.Objects.Count == 0)
        {
            SetStatus("Nothing loaded to check.", Ux.MutedBrush);
            return;
        }

        // Every check runs either way now. With no text the model and the index walk both come from
        // the file's own bytes, so the checker sees the same graph it would have seen through
        // hkxpack rather than a smaller one.
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

    // Most real problems only exist between files, so the same checks run over every behaviour the
    // project owns and report once. Results carry a file, and a node in another file cannot be jumped
    // to on this file's canvas, so the click handler says so rather than silently doing nothing.
    private async Task ValidateProject()
    {
        var chain = _projectChain;
        if (chain == null || chain.Root.Length == 0)
        {
            SetStatus("No project resolved for this file, so there is no chain to check. See the Chain tab.",
                      Ux.MutedBrush);
            return;
        }

        string? java = HkxTextEdit.FindJava(Settings.Get("java"));
        string? jar = HkxTextEdit.FindHkxPack(Settings.Get("hkxpack"), AppContext.BaseDirectory);
        if (java == null || jar == null)
        {
            SetStatus("Checking the project needs Java and hkxpack, the same as saving does.", Ux.BadBrush);
            return;
        }

        _problems.Clear();
        _problems.IsVisible = _problemBar.IsVisible = true;
        _problemBar.Text = "Reading the project...";

        var progress = new Progress<string>(s => SetStatus("Checking " + s, Ux.MutedBrush));
        var result = await Task.Run(() => ProjectCheck.Run(chain, java, jar, s => ((IProgress<string>)progress).Report(s)));

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

    /// Writes the changed values straight into the file's own bytes, leaving everything else exactly
    /// as it was on disk. Returns false when the edit is not one that can be written that way, which
    /// is not a failure: the caller then does it the old way.
    private bool SavedInPlace()
    {
        NativeSave.Plan plan;
        try
        {
            plan = NativeSave.Compare(_savedXml, _xmlText);
        }
        catch (Exception e)
        {
            SetStatus("Could not work out what changed, so nothing was written: " + e.Message, Ux.BadBrush);
            return true;
        }

        if (!plan.Possible || plan.Empty) return false;

        string? blocked = HkxTextEdit.WhyNotWritable(_hkxPath);
        if (blocked != null) { SetStatus("Cannot save: " + blocked, Ux.BadBrush); return true; }

        try
        {
            byte[] bytes = NativeSave.Apply(_hkxPath, plan);

            string backup = _hkxPath + ".bak";
            if (!File.Exists(backup)) File.Copy(_hkxPath, backup);
            ReplaceFile(_hkxPath, bytes);

            ResetHistory();
            SetStatus($"Saved {plan.Changes.Count} " +
                      $"change{(plan.Changes.Count == 1 ? "" : "s")} straight into the file, " +
                      (plan.Grows
                          ? "with anything that grew added on the end so nothing already in it moved. "
                          : "leaving every other byte as it was. ") +
                      $"The original is kept as {Path.GetFileName(backup)}.", Ux.MetaBrush);
            Load();
            return true;
        }
        catch (Exception e)
        {
            SetStatus("Not saved, and the original is untouched: " + e.Message, Ux.BadBrush);
            return true;
        }
    }

    private void Save()
    {
        CommitPendingFields();
        if (!_dirty || _xmlText.Length == 0) return;

        if (_readOnly) { SetStatus("Not saved: " + _readOnlyWhy, Ux.BadBrush); return; }

        // The graph checks apply whichever way the file gets written. The hkxpack round trip warning
        // does not, because writing the bytes in place has no round trip to lose anything in, so it
        // is asked for separately below rather than folded in here.
        string? refusal = GraphValidator.RefuseToSave(_xmlText, includeRepackLosses: false);
        if (refusal != null) { SetStatus(refusal, Ux.BadBrush); return; }

        if (SavedInPlace()) return;

        string? java = HkxTextEdit.FindJava(Settings.Get("java"));
        string? jar = HkxTextEdit.FindHkxPack(Settings.Get("hkxpack"), AppContext.BaseDirectory);
        if (java == null) { SetStatus("Cannot save: no Java runtime found.", Ux.BadBrush); return; }
        if (jar == null) { SetStatus("Cannot save: hkxpack-cli.jar not found.", Ux.BadBrush); return; }

        // Asked before packing rather than after. Packing a weapon behaviour takes seconds, and
        // finding out at the end that the file was read only all along wastes all of them.
        string? blocked = HkxTextEdit.WhyNotWritable(_hkxPath);
        if (blocked != null) { SetStatus("Cannot save: " + blocked, Ux.BadBrush); return; }

        // Only the rebuild loses things, so this is where the warning belongs.
        string? lossy = GraphValidator.RepackWouldLose(_xmlText);
        if (lossy != null) { SetStatus(lossy, Ux.BadBrush); return; }

        try
        {
            File.WriteAllText(_xmlPath, _xmlText);
            string packed = HkxTextEdit.Repack(java, jar, _xmlPath);

            var drift = VerifyRepack(java, jar, packed);
            if (!drift.Clean)
            {
                SetStatus($"Not saved, and the original is untouched: the repack {drift}.", Ux.BadBrush);
                return;
            }

            string backup = _hkxPath + ".bak";
            if (!File.Exists(backup)) File.Copy(_hkxPath, backup);
            File.Copy(packed, _hkxPath, true);

            ResetHistory();
            SetStatus($"Saved. The original is kept as {Path.GetFileName(backup)}.", Ux.MetaBrush);
            Load();
        }
        catch (UnauthorizedAccessException)
        {
            SetStatus("Save failed: " + (HkxTextEdit.WhyNotWritable(_hkxPath) ??
                      "Windows refused the write. Your original is untouched."), Ux.BadBrush);
        }
        catch (Exception ex)
        {
            SetStatus("Save failed: " + ex.Message.Split('\n')[0], Ux.BadBrush);
        }
    }

    // Read the file hkxpack just wrote back out and count what is in it. Done before the original
    // is overwritten, so a repack that silently drops objects costs nothing.
    private RepackCheck.Drift VerifyRepack(string java, string jar, string packed)
    {
        string work = Path.Combine(Path.GetTempPath(), "bgs_verify", Path.GetFileNameWithoutExtension(_hkxPath));
        HkxTextEdit.ResetDirectory(work);

        string xml = HkxTextEdit.Unpack(java, jar, packed, work);
        return RepackCheck.Compare(RepackCheck.Take(_xmlText), RepackCheck.Take(HkxTextEdit.ReadXml(xml)));
    }

    private void OnWindowKey(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        bool control = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
        if (!control) return;
        bool shift = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift);

        if (e.Key == Avalonia.Input.Key.Z && !shift) { Undo(); e.Handled = true; }
        else if (e.Key == Avalonia.Input.Key.Y || (e.Key == Avalonia.Input.Key.Z && shift)) { Redo(); e.Handled = true; }
    }

    /// The only place the loaded document changes outside a load. Every editing path routes here so
    /// the step back is taken before the change lands, and so nothing can mutate around the stack.
    private void Commit(string newXml)
    {
        if (newXml == _xmlText) return;

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

    // Dirty is measured against what was last written rather than latched on, so undoing back past
    // the last save says so instead of claiming there is still something to write.
    private void RefreshDirty()
    {
        _dirty = _xmlText.Length > 0 && _xmlText != _savedXml;

        // A file opened out of an archive can be edited and read, it just cannot be written back
        // where it came from. Greying the button says that before an edit rather than after it, and
        // Save refuses as well, so the answer does not depend on the button being right.
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

    // The tree, the canvas, the symbol table and the properties panel all read from the document, so
    // all four are rebuilt. Leaving any of them showing the old version is worse than not having undo.
    private void AfterHistoryMove(string what)
    {
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

    // Java that autodetection missed. Validated by running it rather than accepted on its name, so a
    // wrong pick says so here instead of at the next save.
    private async Task PickJava()
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Find the Java launcher",
            AllowMultiple = false,
            SuggestedStartLocation = await JavaStartFolder(),
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Java launcher") { Patterns = new[] { "java", "java.exe" } },
                FilePickerFileTypes.All,
            },
        });

        string? path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (path == null) return;

        SetStatus($"Running {Path.GetFileName(path)} -version...", Ux.MutedBrush);
        string? bad = await Task.Run(() => HkxTextEdit.WhyNotJava(path));
        if (bad != null) { SetStatus(bad + " Nothing was changed.", Ux.BadBrush); return; }

        string version = await Task.Run(() => HkxTextEdit.JavaVersion(path));
        Settings.Set("java", path);
        _findJava.IsVisible = false;
        SetStatus($"Java accepted: {version}", Ux.MetaBrush);

        // Read only is lifted by redoing the unpack, not by flipping a flag: the four tabs that were
        // empty are empty because the text form was never produced.
        if (_hkxPath.Length > 0 && _root != null) PrepareEditing();
    }

    private async Task<IStorageFolder?> JavaStartFolder()
    {
        string[] candidates =
        {
            Path.GetDirectoryName(Settings.Get("java")) ?? "",
            Path.Combine(Environment.GetEnvironmentVariable("JAVA_HOME") ?? "", "bin"),
            @"C:\Program Files\Java",
            "/usr/lib/jvm",
        };

        foreach (string dir in candidates)
            if (dir.Length > 0 && Directory.Exists(dir))
                return await StorageProvider.TryGetFolderFromPathAsync(dir);

        return null;
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
