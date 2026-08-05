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
    private HkxAnimationData? _animationData;
    private HkxSkeleton? _animationSkeleton;
    private int _frameStart;
    private int _aimedFrame = -1;

    private readonly TextBox _symbolName = Ux.Field("name", 170);
    private readonly TextBox _symbolValue = Ux.Field("value, for a variable", 130);
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
    private List<HkxBehaviorParser.BehaviorNode> _objects = new();
    private HkxBehaviorParser.BehaviorNode? _root;

    private string _hkxPath = "";
    private string _xmlPath = "";
    private string _xmlText = "";
    private ProjectChain? _projectChain;
    private string _selectedId = "";
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
                (Bar(_pathField, browse, open), false),
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

        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_problemBar, Dock.Bottom);
        DockPanel.SetDock(_problems, Dock.Bottom);
        DockPanel.SetDock(_graphProps, Dock.Right);
        DockPanel.SetDock(splitter, Dock.Right);
        panel.Children.Add(_problemBar);
        panel.Children.Add(_problems);
        panel.Children.Add(_graphProps);
        panel.Children.Add(splitter);
        panel.Children.Add(Framed(_graph));

        _problems.IsVisible = false;
        _problemBar.IsVisible = false;
        return panel;
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

        panel.Children.Add(_animation);
        return panel;
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
        // A frame aimed at in the last file means nothing in this one.
        _aimedFrame = -1;
        _fractionAnswer.Text = "";

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
                _animation.Add(head, aimed ? "->" : "", f.ToString(), $"{f * anim.FrameDuration:F3}s", pos, rot, scl)
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
                 { _playButton, first, back, forward, last, fit, reference, reload, mesh, clearMesh })
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

        SetPlaybackSummary(
            $"{label}   {animation.NumFrames} frames at {1f / Math.Max(animation.FrameDuration, 0.0001f):F0} fps, " +
            $"{animation.Duration:F2}s   {driven} of {_poseSkeleton!.BoneNames.Count} bones driven   " +
            $"on {_poseSkeleton.Name}", Ux.MetaBrush);
        UpdateFrameLabel();
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

    private void LoadMesh(string path)
    {
        var skeleton = PoseSkeleton();
        if (skeleton == null)
        {
            SetPlaybackSummary("No skeleton is resolved for this file, so a mesh has nothing to hang on.",
                               Ux.BadBrush);
            return;
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
            return;
        }

        if (_meshShapes.Count == 0)
        {
            SetPlaybackSummary($"{Path.GetFileName(path)} holds no drawable shape.", Ux.MutedBrush);
            return;
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
            var model = BehaviourGraphModel.Parse(_xmlText);
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

        LoadMesh(found.Path!);
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

        _clock = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(_poseAnimation.FrameDuration, 1 / 120f, 1)),
        };
        // Looping rather than stopping at the end: nearly every clip in a behaviour graph is a loop,
        // and one that is not still reads better repeating than freezing on its last frame.
        _clock.Tick += (_, _) => ShowFrame(_poseFrame + 1 > _scrub.Maximum ? 0 : _poseFrame + 1, stop: false);
        _clock.Start();
        _playButton.Content = "Pause";
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
        _skeleton.Update(posed);
        UpdateMesh(posed, _poseSkeleton);

        _scrubbing = true;
        _scrub.Value = _poseFrame;
        _scrubbing = false;
        UpdateFrameLabel();
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
        if (java == null || jar == null)
        {
            SetDiffSummary("Comparing needs Java and hkxpack, the same as saving does.", Ux.BadBrush);
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

    private static BehaviourDiff.Result ComputeDiff(string mine, string other, string java, string jar)
    {
        string work = Path.Combine(Path.GetTempPath(), "bgs_compare");
        HkxTextEdit.ResetDirectory(work);
        string xml = HkxTextEdit.Unpack(java, jar, other, work);
        return BehaviourDiff.Compare(RepackCheck.Take(mine), RepackCheck.Take(HkxTextEdit.ReadXml(xml)));
    }

    /// Runs the comparison through the same code the picker feeds, so a check exercises what a person
    /// does rather than a parallel path. Returns what the panel now says.
    public string CompareLoadedWith(string other)
    {
        string? java = HkxTextEdit.FindJava(Settings.Get("java"));
        string? jar = HkxTextEdit.FindHkxPack(Settings.Get("hkxpack"), AppContext.BaseDirectory);
        if (_xmlText.Length == 0 || java == null || jar == null) return "";

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

        if (_xmlText.Length > 0) BuildSymbols(BehaviourGraphModel.Parse(_xmlText));
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
        _xmlText = "";
        _xmlPath = "";
        _selectedId = "";
        _projectChain = null;
        _emptyStates = new HashSet<string>();
        // Object ids start again at #1 in the next file, so anything the canvas remembers by id is
        // about to be applied to a different object entirely.
        _graph.Reset();
        ClearPose();
        ResetHistory();

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

        var classes = new HashSet<string>();
        int clips = 0;
        foreach (var o in _objects)
        {
            classes.Add(o.ClassName);
            if (!string.IsNullOrEmpty(o.AnimationName)) clips++;
        }

        SetSummary($"{Path.GetFileName(path)}   root {root.ClassName}   {_objects.Count} objects   " +
                   $"{classes.Count} classes   {clips} clip references", Ux.TitleBrush);

        RebuildTree();
        Settings.Set("last_path", path);
        Settings.Set("last_folder", Path.GetDirectoryName(path) ?? "");
        PrepareEditing();

        // An animation file parses as a graph of objects too, so it comes down this path rather than
        // the one above. Either way, if the open file holds frames then it is the thing on screen and
        // it is what plays.
        if (_animationData != null) LoadPose(path, Path.GetFileName(path));
    }

    private void PrepareEditing()
    {
        string? java = HkxTextEdit.FindJava(Settings.Get("java"));
        string? jar = HkxTextEdit.FindHkxPack(Settings.Get("hkxpack"), AppContext.BaseDirectory);

        // Naming which one is missing matters more than it looks. Everything except the Tree comes
        // from the unpacked text form, so without these the other four tabs are simply empty, which
        // reads as a broken tool rather than a missing dependency.
        if (java == null || jar == null)
        {
            string missing = java == null && jar == null ? "Java and hkxpack are missing"
                           : java == null ? "Java is missing"
                           : "hkxpack-cli.jar is missing";
            _findJava.IsVisible = java == null;
            SetStatus($"Read only, so the Graph, Symbols, Chain and Animation tabs stay empty: " +
                      $"{missing}. The tree is read straight from the binary and does not need either. " +
                      (java == null ? "Install a Java runtime, or press Find Java if one is already installed somewhere this did not look. " : "") +
                      (jar == null ? $"Put hkxpack-cli.jar in a tools folder beside the program, at {Path.Combine(AppContext.BaseDirectory, "tools")}. " : "") +
                      "Save stays off until then.", Ux.WarnBrush);
            return;
        }

        _findJava.IsVisible = false;

        try
        {
            string work = Path.Combine(Path.GetTempPath(), "bgs_edit", Path.GetFileNameWithoutExtension(_hkxPath));
            HkxTextEdit.ResetDirectory(work);

            _xmlPath = HkxTextEdit.Unpack(java, jar, _hkxPath, work);
            _xmlText = HkxTextEdit.ReadXml(_xmlPath);
            _objectIds = HkxTextEdit.ObjectIds(_xmlText);
            ResetHistory();

            if (_objectIds.Count != _objects.Count)
            {
                _xmlText = "";
                ResetHistory();
                SetStatus($"Read only: object counts disagree ({_objects.Count} binary vs {_objectIds.Count} xml).",
                          Ux.MutedBrush);
                return;
            }

            var model = BehaviourGraphModel.Parse(_xmlText);
            // The tree drew before the text form existed, so the states holding nothing were unknown
            // when it was built. Now they are known, so it is built again.
            _emptyStates = GraphValidator.StatesWithNoGenerator(model);
            RebuildTree();

            _graph.Show(model);
            _graph.FrameAll();
            BuildSymbols(model);
            BuildClipList(model);
            BuildChain(java, jar);
            FindMeshForFile();
            SetStatus($"Editable. {_objectIds.Count} objects mapped, {_graph.DrawnCount} drawn.", Ux.MetaBrush);
        }
        catch (Exception ex)
        {
            _xmlText = "";
            ResetHistory();
            SetStatus("Read only: " + ex.Message.Split('\n')[0], Ux.MutedBrush);
        }
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
        var model = BehaviourGraphModel.Parse(_xmlText);
        ShowProps(objectId, model);
        SetStatus(Describe(model, objectId), Ux.MetaBrush);

        // Selecting a clip is what asks what it plays, so it is what answers it. Quiet when the
        // selection plays nothing, which is most nodes in a graph.
        LoadPoseFromSelection(announce: false);
    }

    private string Describe(string id) => Describe(BehaviourGraphModel.Parse(_xmlText), id);

    private static string Describe(BehaviourGraphModel model, string id)
    {
        var obj = model.Get(id);
        if (obj == null) return "#" + id;
        string name = obj.Str("name");
        return $"#{id} {obj.Class}" + (name.Length > 0 ? $" '{name}'" : "");
    }

    private void ClearProps()
    {
        _treeProps.Clear();
        _graphProps.Clear();
        _clipProps.Clear();
    }

    // Both panels are filled, because which one is on screen depends on the tab and a node can be
    // reached from either. The model is parsed once and handed to both: on a shipped weapon graph
    // that parse is the expensive part of selecting a node.
    private void ShowProps(string objectId) => ShowProps(objectId, BehaviourGraphModel.Parse(_xmlText));

    private void ShowProps(string objectId, BehaviourGraphModel model)
    {
        _selectedId = objectId;
        FillProps(_treeProps, objectId, model);
        FillProps(_graphProps, objectId, model);
        FillProps(_clipProps, objectId, model);
        _clips.SelectByTag(objectId);
    }

    private void FillProps(Inspector panel, string objectId, BehaviourGraphModel model)
    {
        panel.Clear();
        string className = HkxTextEdit.ClassOf(_xmlText, objectId);
        var parameters = HkxTextEdit.ReadParams(_xmlText, objectId);

        var heading = Ux.Label($"#{objectId}   {className}   {parameters.Count} editable fields");
        heading.TextWrapping = TextWrapping.Wrap;
        panel.Add(heading);

        foreach (var p in parameters)
        {
            var field = Ux.Field();
            field.Text = p.Value;

            string name = p.Name;
            string original = p.Value;
            string owner = objectId;
            field.LostFocus += (_, _) => Apply(owner, name, field, original);
            field.KeyDown += (_, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter) Apply(owner, name, field, original);
            };

            var label = Ux.Label(p.Name);
            label.Width = 128;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(label, p.Name);

            var row = new DockPanel();
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            row.Children.Add(field);
            panel.Add(row);
        }

        AddSymbolSection(panel, objectId, model);
        AddBindingSection(panel, objectId, model);
    }

    // The other direction of the usages question: not who touches this symbol, but which symbols this
    // node touches. An index on its own says nothing, so each one is resolved to its declared name.
    private void AddSymbolSection(Inspector panel, string objectId, BehaviourGraphModel model)
    {
        var events = SymbolIndexFixup.UsagesOf(_xmlText, events: true, objectId);
        var variables = SymbolIndexFixup.UsagesOf(_xmlText, events: false, objectId);
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
        var variableSites = _xmlText.Length == 0
            ? new List<SymbolIndexFixup.Usage>()
            : SymbolIndexFixup.Usages(_xmlText, events: false);

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

        var usage = _xmlText.Length == 0
            ? new Dictionary<int, List<EventUsage.Line>>()
            : EventUsage.ByEvent(_xmlText);

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
    private Dictionary<int, List<string>> UsersByIndex(bool events)
    {
        var map = new Dictionary<int, List<string>>();
        if (_xmlText.Length == 0) return map;

        foreach (var reference in SymbolIndexFixup.References(_xmlText, events))
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

        var model = BehaviourGraphModel.Parse(_xmlText);
        var names = variable ? SymbolEditor.VariableNames(model) : SymbolEditor.EventNames(model);
        if (index < 0 || index >= names.Count) return;

        _symbolName.Text = names[index];

        if (!variable) { _symbolValue.Text = ""; return; }
        var types = SymbolEditor.VariableTypes(model);
        var values = SymbolEditor.VariableValues(model);
        _symbolValue.Text = index < values.Count
            ? SymbolEditor.DecodeValue(index < types.Count ? types[index] : SymbolEditor.VariableType.Int32, values[index])
            : "";
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
            var model = BehaviourGraphModel.Parse(_xmlText);
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
        var model = BehaviourGraphModel.Parse(_xmlText);

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

            var model = BehaviourGraphModel.Parse(_xmlText);
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

            var model = BehaviourGraphModel.Parse(_xmlText);
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
            var names = BindingEditor.VariableNames(BehaviourGraphModel.Parse(_xmlText));
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
        var model = BehaviourGraphModel.Parse(_xmlText);
        _graph.Show(model);
        BuildSymbols(model);
        ClearProps();
        ShowProps(objectId);
    }

    private void Apply(string objectId, string paramName, TextBox field, string original)
    {
        if (field.Text == original || _xmlText.Length == 0) return;

        try
        {
            Commit(HkxTextEdit.SetParam(_xmlText, objectId, paramName, field.Text ?? ""));
            SetStatus($"#{objectId}.{paramName} = {field.Text}   (unsaved)", Ux.CodeBrush);
        }
        catch (Exception ex)
        {
            field.Text = original;
            SetStatus(ex.Message.Split('\n')[0], Ux.MutedBrush);
        }
    }

    // hkxpack checks shape, not meaning, so this is the only thing standing between a bad edit and
    // finding out in game.
    private void Validate()
    {
        if (_xmlText.Length == 0) { SetStatus("Nothing loaded to check.", Ux.MutedBrush); return; }

        var findings = GraphValidator.Check(_xmlText, _projectChain);
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
            File.WriteAllBytes(_hkxPath, bytes);

            ResetHistory();
            SetStatus($"Saved {plan.Changes.Count} " +
                      $"change{(plan.Changes.Count == 1 ? "" : "s")} straight into the file, " +
                      $"leaving every other byte as it was. The original is kept as " +
                      $"{Path.GetFileName(backup)}.", Ux.MetaBrush);
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
        if (!_dirty || _xmlText.Length == 0) return;

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
        _saveButton.IsEnabled = _dirty;
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

        var model = BehaviourGraphModel.Parse(_xmlText);
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
