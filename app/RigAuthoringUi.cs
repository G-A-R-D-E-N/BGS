using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpenCommonwealth.Services.Hkx;
using OpenCommonwealth.Services.Nif;

namespace BehaviourStudio.App;

public static class RigAuthoringUi
{
    public static void Attach(MainWindow window)
    {
        var shell = EditorShell.Find(window.Content as Control);
        if (shell == null || shell.AnimationTools.Children.OfType<Button>().Any(b => b.Content?.ToString() == "Skeleton / skin authoring")) return;
        var button = Ux.Secondary("Skeleton / skin authoring");
        button.Click += (_, _) => new RigAuthoringWindow(window).Show(window);
        shell.AnimationTools.Children.Add(button);
    }
}

public sealed partial class RigAuthoringWindow : Window
{
    private readonly MainWindow _owner;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private SkeletonDocument? _skeleton;
    private SkinDocument? _skin;
    private readonly ComboBox _rigs = new() { MinWidth = 180 };
    private readonly ComboBox _bones = new() { MinWidth = 230 };
    private readonly TextBox _boneName = Ux.Field("bone name", 230);
    private readonly TextBox _parent = Ux.Field("parent index", 90);
    private readonly TextBox _translation = Ux.Field("X Y Z", 230);
    private readonly TextBox _rotation = Ux.Field("X Y Z W", 230);
    private readonly TextBox _scale = Ux.Field("X Y Z", 230);
    private readonly CheckBox _lockedTranslation = new() { Content = "Lock translation" };
    private readonly SkeletonView _preview = new();
    private bool _closeApproved;
    private bool _deciding;

    public RigAuthoringWindow(MainWindow owner, int selectedTab = 0)
    {
        _owner = owner;
        Title = "BGS Skeleton / Skin Authoring";
        Width = 1050; Height = 760; MinWidth = 760; MinHeight = 600;
        Background = Ux.BaseBrush;
        Foreground = Ux.CodeBrush;
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _boneName.Name = "BoneName";
        _translation.Name = "BoneTranslation";
        _rotation.Name = "BoneRotation";
        _scale.Name = "BoneScale";
        _bones.Name = "BoneSelection";
        var root = new DockPanel { Margin = new Avalonia.Thickness(12), Background = Ux.BaseBrush };
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        root.Children.Add(new TabControl { SelectedIndex = selectedTab, ItemsSource = new[] {
            new TabItem { Header = "Skeleton", Content = SkeletonPanel() },
            new TabItem { Header = "Skin weights", Content = SkinPanel() },
            new TabItem { Header = "Physics simulation", Content = PhysicsPanel() }
        } });
        Content = root;
        _owner.Closing += OwnerClosing;
        Closed += (_, _) => { _owner.Closing -= OwnerClosing; StopPhysics(); };
    }

    private Control SkeletonPanel()
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Row(Button("Open skeleton HKX", () => _ = Browse(true)), _rigs,
            Button("Undo skeleton", () => Run(() => { _skeleton?.Undo(); RefreshSkeleton(); })),
            Button("Redo skeleton", () => Run(() => { _skeleton?.Redo(); RefreshSkeleton(); })),
            Button("Save skeleton", () => Run(() => { RequiredSkeleton().Save(); Say("Skeleton saved; original retained as .bak."); }))));
        panel.Children.Add(new TextBlock { Text = "Edit names, reference transforms and parents while preserving bone indices. Reordering or changing bone count is refused when dependent assets cannot be remapped.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Row(_bones, Ux.Label("Name"), _boneName));
        panel.Children.Add(Row(Ux.Label("Parent (-1 = root)"), _parent, _lockedTranslation));
        panel.Children.Add(Row(Ux.Label("Translation"), _translation, Ux.Label("Scale"), _scale));
        panel.Children.Add(Row(Ux.Label("Quaternion"), _rotation));
        panel.Children.Add(Row(Button("Apply bone", () => Run(ApplyBone)),
            Button("Reparent bone", () => EditSkeleton(edit => edit.Reparent(BoneIndex(), int.Parse(_parent.Text ?? "", CultureInfo.InvariantCulture)))),
            Button("Validate skeleton", () => Run(() => Say(Findings(SkeletonValidator.Check(CurrentSkeleton()).Select(f => f.ToString())))))));
        panel.Children.Add(Row(Button("Add bone", () => EditSkeleton(edit => edit.AddBone(_boneName.Text ?? "", BoneIndex()))),
            Button("Remove bone", () => EditSkeleton(edit => edit.RemoveBone(BoneIndex()))),
            Button("Inspect mirror pairs", () => Run(() => {
                var pairing = SkeletonMirror.Of(CurrentSkeleton());
                Say(pairing + "; " + SkeletonMirror.MirrorAxis(CurrentSkeleton(), pairing));
            }))));
        _rigs.SelectionChanged += (_, _) => RefreshBones();
        _bones.SelectionChanged += (_, _) => LoadBone();
        return AuthoringPanel(panel, _preview);
    }

    public void OpenSkeleton(string path)
    {
        if (_skeleton?.Dirty == true) throw new InvalidOperationException("Save or discard the current skeleton before opening another.");
        _skeleton = new SkeletonDocument(path);
        RefreshSkeleton();
        _preview.Frame();
        Say("Skeleton opened. Edits stay in memory until Save.");
    }

    private void RefreshSkeleton()
    {
        int selected = _rigs.SelectedIndex;
        _rigs.ItemsSource = _skeleton?.Skeletons.Select((s, i) => $"{i}: {s.Name}").ToArray();
        _rigs.SelectedIndex = _skeleton == null ? -1 : Math.Clamp(selected, 0, _skeleton.Skeletons.Count - 1);
        RefreshBones();
    }

    private void RefreshBones()
    {
        if (_skeleton == null || _rigs.SelectedIndex < 0) return;
        int selected = _bones.SelectedIndex;
        HkxSkeleton skeleton = CurrentSkeleton();
        _bones.ItemsSource = skeleton.BoneNames.Select((name, i) => $"{i}: {name}").ToArray();
        _bones.SelectedIndex = Math.Clamp(selected, 0, skeleton.BoneNames.Count - 1);
        _preview.Show(AnimationPose.ReferencePose(skeleton));
        LoadBone();
    }

    private void LoadBone()
    {
        if (_skeleton == null || _rigs.SelectedIndex < 0 || _bones.SelectedIndex < 0) return;
        HkxSkeleton skeleton = CurrentSkeleton();
        int index = BoneIndex();
        HkxBonePose pose = skeleton.ReferencePose[index];
        _boneName.Text = skeleton.BoneNames[index];
        _parent.Text = skeleton.ParentIndices[index].ToString(CultureInfo.InvariantCulture);
        _lockedTranslation.IsChecked = skeleton.LockTranslation[index];
        _translation.Text = Text(pose.Translation.X, pose.Translation.Y, pose.Translation.Z);
        _rotation.Text = Text(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W);
        _scale.Text = Text(pose.Scale.X, pose.Scale.Y, pose.Scale.Z);
    }

    private void ApplyBone()
    {
        float[] t = Values(_translation, 3), q = Values(_rotation, 4), s = Values(_scale, 3);
        EditSkeleton(edit => {
            edit.Rename(BoneIndex(), _boneName.Text ?? "");
            edit.Skeleton.LockTranslation[BoneIndex()] = _lockedTranslation.IsChecked == true;
            edit.SetPose(BoneIndex(), new HkxBonePose(new Vector3(t[0], t[1], t[2]),
                new Quaternion(q[0], q[1], q[2], q[3]), new Vector3(s[0], s[1], s[2])));
        });
    }

    private void EditSkeleton(Action<SkeletonEdit> edit) => Run(() => {
        RequiredSkeleton().Edit(_rigs.SelectedIndex, edit);
        RefreshBones();
        Say("Skeleton edit verified in memory. Save to write it.");
    });

    private SkeletonDocument RequiredSkeleton() => _skeleton ?? throw new InvalidOperationException("Open a skeleton first.");
    private HkxSkeleton CurrentSkeleton() => RequiredSkeleton().Skeletons[_rigs.SelectedIndex];
    private int BoneIndex() => _bones.SelectedIndex >= 0 ? _bones.SelectedIndex : throw new InvalidOperationException("Choose a bone.");

    private async Task Browse(bool skeleton)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
                Title = skeleton ? "Open skeleton HKX" : "Open skinned NIF", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType(skeleton ? "Havok" : "NIF") {
                    Patterns = new[] { skeleton ? "*.hkx" : "*.nif" } } }
            });
            string? path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path == null) return;
            bool dirty = skeleton ? _skeleton?.Dirty == true : _skin?.Dirty == true;
            if (dirty)
            {
                var choice = await new DiscardDialog("open another asset").ShowDialog<DiscardChoice>(this);
                if (choice == DiscardChoice.Cancel) return;
                if (choice == DiscardChoice.Save) { if (skeleton) _skeleton!.Save(); else _skin!.Save(); }
                else { if (skeleton) _skeleton = null; else _skin = null; }
            }
            if (skeleton) OpenSkeleton(path); else OpenSkin(path);
        }
        catch (Exception error) { Say(error.Message); }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _closeApproved || !Dirty) return;
        e.Cancel = true;
        _ = DecideClose(false);
    }

    private void OwnerClosing(object? sender, WindowClosingEventArgs e)
    {
        if (e.Cancel || _closeApproved || !Dirty) return;
        e.Cancel = true;
        _ = DecideClose(true);
    }

    private bool Dirty => _skeleton?.Dirty == true || _skin?.Dirty == true;

    private async Task DecideClose(bool owner)
    {
        if (_deciding) return;
        _deciding = true;
        try
        {
            var choice = await new DiscardDialog("close with unsaved rig edits").ShowDialog<DiscardChoice>(this);
            if (choice == DiscardChoice.Cancel) return;
            if (choice == DiscardChoice.Save) { _skeleton?.Save(); _skin?.Save(); }
            _closeApproved = true;
            if (owner) _owner.Close(); else Close();
        }
        catch (Exception error) { Say(error.Message); }
        finally { _deciding = false; }
    }

    private static Button Button(string title, Action action)
    {
        var button = Ux.Secondary(title);
        button.Click += (_, _) => action();
        return button;
    }

    private static Control AuthoringPanel(StackPanel settings, SkeletonView preview)
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto,*") };
        root.RowDefinitions[0].MaxHeight = 360;
        root.Children.Add(new ScrollViewer { Content = settings });
        var fit = Button("Fit preview", preview.Frame);
        fit.HorizontalAlignment = HorizontalAlignment.Left;
        fit.Margin = new Avalonia.Thickness(0, 6, 0, 6);
        Grid.SetRow(fit, 1);
        root.Children.Add(fit);
        Grid.SetRow(preview, 2);
        root.Children.Add(preview);
        return root;
    }

    private static WrapPanel Row(params Control[] controls)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (Control control in controls) { control.Margin = new Avalonia.Thickness(0, 0, 8, 8); row.Children.Add(control); }
        return row;
    }

    private bool Run(Action action) { try { action(); return true; } catch (Exception error) { Say("Refused: " + error.Message); return false; } }
    private void Say(string message) => _status.Text = message;
    private static string Findings(System.Collections.Generic.IEnumerable<string> findings) => string.Join("\n", findings.DefaultIfEmpty("Validation passed."));
    private static string Text(params float[] values) => string.Join(" ", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
    private static float[] Values(TextBox box, int count)
    {
        float[] result = (box.Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        if (result.Length != count || result.Any(v => !float.IsFinite(v)))
            throw new ArgumentException($"Enter {count} finite numbers.");
        return result;
    }
}
