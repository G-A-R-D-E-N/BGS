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
using OpenCommonwealth.Services.Hkx;

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

    private readonly HkGrid _tree = new(("Node", -4), ("Havok class", -3), ("Animation", -4), ("Offset", 90));
    private readonly HkGrid _symbols =
        new(("Kind", 60), ("Index", 55), ("Name", -4), ("Initial value", -2), ("Used by, in this file", -5));
    private readonly HkGrid _chain = new(("Role", 110), ("Declared in the file", -4), ("On disk", 80), ("Notes", -3));
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
        _saveButton = Ux.Primary("Save to .hkx");
        _saveButton.IsEnabled = false;
        _saveButton.Click += (_, _) => Save();

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

        Content = new Border
        {
            Padding = new Thickness(14),
            Child = Rows(
                (Ux.SectionTitle("Havok behaviour file"), false),
                (Bar(_pathField, browse, open), false),
                (Ux.Pill(_summary), false),
                (Bar(_filter, expand, collapse), false),
                (tabs, true),
                (Bar(Ux.Pill(_status), check, _saveButton), false)),
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
    public HkGrid SymbolGrid => _symbols;
    public string FractionAnswer => _fractionAnswer.Text ?? "";
    public int AimedFrame => _aimedFrame;
    public string LoadedXml => _xmlText;
    public Inspector GraphProperties => _graphProps;
    public GraphView Canvas => _graph;

    /// Selects through the same handler a click on the canvas uses, so a check exercises the path a
    /// person takes rather than a parallel one.
    public void SelectNode(string objectId) => SelectObjectId(objectId);

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

        bar.Children.Add(Ux.Pill(_symbolAudit));

        var panel = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        bar.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(bar);
        panel.Children.Add(_symbols);
        return panel;
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

    private void Load()
    {
        _tree.Clear();
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
        SetDirty(false);

        string path = (_pathField.Text ?? "").Trim().Trim('"');
        if (path.Length == 0) { SetSummary("Enter the path to a .hkx file.", Ux.MutedBrush); return; }
        if (!File.Exists(path)) { SetSummary("Not found: " + path, Ux.MutedBrush); return; }
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
                ? $"{Path.GetFileName(path)}   an animation, not a behaviour. See the Animation tab."
                : "Parsed as FO4 hkx, but no root object was resolved.", Ux.MutedBrush);
            SetStatus(_animationSummary.Text ?? "", _animationSummary.Foreground ?? Ux.MutedBrush);
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
            SetStatus($"Read only, so the Graph, Symbols, Chain and Animation tabs stay empty: " +
                      $"{missing}. The tree is read straight from the binary and does not need either. " +
                      (java == null ? "Install a Java runtime. " : "") +
                      (jar == null ? $"Put hkxpack-cli.jar in a tools folder beside the program, at {Path.Combine(AppContext.BaseDirectory, "tools")}. " : "") +
                      "Save stays off until then.", Ux.WarnBrush);
            return;
        }

        try
        {
            string work = Path.Combine(Path.GetTempPath(), "bgs_edit", Path.GetFileNameWithoutExtension(_hkxPath));
            if (Directory.Exists(work)) Directory.Delete(work, true);

            _xmlPath = HkxTextEdit.Unpack(java, jar, _hkxPath, work);
            _xmlText = HkxTextEdit.ReadXml(_xmlPath);
            _objectIds = HkxTextEdit.ObjectIds(_xmlText);

            if (_objectIds.Count != _objects.Count)
            {
                _xmlText = "";
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
            BuildChain(java, jar);
            SetStatus($"Editable. {_objectIds.Count} objects mapped, {_graph.DrawnCount} drawn.", Ux.MetaBrush);
        }
        catch (Exception ex)
        {
            _xmlText = "";
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

    private void OnTreeSelected()
    {
        ClearProps();
        _selectedId = "";
        if (_tree.SelectedTag is not int offset || _xmlText.Length == 0) return;
        if (!_offsetToIndex.TryGetValue(offset, out int index)) return;
        if (index < 0 || index >= _objectIds.Count) return;
        ShowProps(_objectIds[index]);
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

        AddBindingSection(panel, objectId, model);
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

    private void BuildSymbols(BehaviourGraphModel model)
    {
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

        for (int i = 0; i < names.Count; i++)
        {
            var type = i < types.Count ? types[i] : SymbolEditor.VariableType.Int32;
            Paint(_symbols.Add(null, type.ToString().ToLowerInvariant(), i.ToString(), names[i],
                               i < values.Count ? SymbolEditor.DecodeValue(type, values[i]) : "",
                               Users(readers, events: false, i)).Tag($"v:{i}"));
        }

        var usage = _xmlText.Length == 0
            ? new Dictionary<int, List<EventUsage.Line>>()
            : EventUsage.ByEvent(_xmlText);

        for (int i = 0; i < events.Count; i++)
        {
            usage.TryGetValue(i, out var lines);
            var row = Paint(_symbols.Add(null, "event", i.ToString(), events[i], "",
                                         lines is { Count: > 0 } ? EventUsage.Summarise(lines) : Users(listeners, events: true, i)))
                .Tag($"e:{i}");
            if (lines == null) continue;

            row.Collapse();
            foreach (var line in lines)
                _symbols.Add(row, EventUsage.Describe(line.Role), line.Count > 1 ? $"x{line.Count}" : "",
                             line.Site, "", line.Note)
                        .Colour(0, line.Role == EventUsage.Role.Raised ? Ux.MetaBrush : Ux.MutedBrush)
                        .Colour(1, Ux.DisabledBrush).Colour(2, Ux.CodeBrush).Colour(4, Ux.MutedBrush);
        }

        if (names.Count == 0 && events.Count == 0)
            _symbols.Add(null, "", "", "this graph declares no variables or events").Colour(2, Ux.DisabledBrush);
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
        if (_symbols.SelectedTag is not string tag || tag.Length < 3) return false;
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
            _xmlText = edit(_xmlText);
            SetDirty(true);
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
            _xmlText = GraphAuthor.AddNode(_xmlText, kind, name, animation, bySlot ? "" : parentId,
                                           out string newId, out string note);
            SetDirty(true);
            _graph.Place(newId, at);

            if (bySlot)
            {
                try
                {
                    _xmlText = GraphLinks.Connect(_xmlText, parentId, field, newId, out string joined);
                    note = $"created {name}, {joined}";
                }
                catch (Exception ex)
                {
                    note = $"created {name} but left it unattached: {ex.Message.Split('\n')[0]}";
                }
            }

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
            _xmlText = connect
                ? GraphLinks.Connect(_xmlText, fromId, field, toId, out string note)
                : GraphLinks.Disconnect(_xmlText, fromId, field, toId, out note);

            SetDirty(true);
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
            _xmlText = GraphAuthor.DeleteNode(_xmlText, objectId, out string note);
            SetDirty(true);
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

            if (index < 0)
            {
                _xmlText = BindingEditor.AddVariable(_xmlText, variableName, out index);
                SetStatus($"declared variable '{variableName}' at index {index}", Ux.CodeBrush);
            }

            _xmlText = BindingEditor.AddBinding(_xmlText, objectId, memberPath, index);
            SetDirty(true);
            SetStatus($"#{objectId}.{memberPath} driven by {variableName}   (unsaved)", Ux.CodeBrush);
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
            _xmlText = BindingEditor.RemoveBinding(_xmlText, setId, index);
            SetDirty(true);
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
            _xmlText = HkxTextEdit.SetParam(_xmlText, objectId, paramName, field.Text ?? "");
            SetDirty(true);
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

    private void Save()
    {
        if (!_dirty || _xmlText.Length == 0) return;

        string? refusal = GraphValidator.RefuseToSave(_xmlText);
        if (refusal != null) { SetStatus(refusal, Ux.BadBrush); return; }

        string? java = HkxTextEdit.FindJava(Settings.Get("java"));
        string? jar = HkxTextEdit.FindHkxPack(Settings.Get("hkxpack"), AppContext.BaseDirectory);
        if (java == null) { SetStatus("Cannot save: no Java runtime found.", Ux.BadBrush); return; }
        if (jar == null) { SetStatus("Cannot save: hkxpack-cli.jar not found.", Ux.BadBrush); return; }

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

            SetDirty(false);
            SetStatus($"Saved. The original is kept as {Path.GetFileName(backup)}.", Ux.MetaBrush);
            Load();
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
        if (Directory.Exists(work)) Directory.Delete(work, true);

        string xml = HkxTextEdit.Unpack(java, jar, packed, work);
        return RepackCheck.Compare(RepackCheck.Take(_xmlText), RepackCheck.Take(HkxTextEdit.ReadXml(xml)));
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        _saveButton.IsEnabled = dirty;
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
