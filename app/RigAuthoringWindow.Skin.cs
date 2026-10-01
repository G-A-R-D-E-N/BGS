using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenCommonwealth.Services.Hkx;
using OpenCommonwealth.Services.Nif;

namespace BehaviourStudio.App;

public sealed partial class RigAuthoringWindow
{
    private readonly ComboBox _shapes = new() { MinWidth = 230 };
    private readonly NumericUpDown _vertex = new() { Minimum = 0, Increment = 1, FormatString = "0", Width = 100 };
    private readonly ComboBox[] _influenceBones = Enumerable.Range(0, SkinEdit.Slots).Select(_ => new ComboBox { MinWidth = 230 }).ToArray();
    private readonly TextBox[] _weights = Enumerable.Range(0, SkinEdit.Slots).Select(_ => Ux.Field("weight", 100)).ToArray();
    private readonly ComboBox _copyFrom = new() { MinWidth = 200 };
    private readonly ComboBox _copyTo = new() { MinWidth = 200 };
    private readonly TextBox _prune = Ux.Field("minimum", 100);
    private readonly ComboBox _axis = new() { ItemsSource = new[] { "X", "Y", "Z" }, SelectedIndex = 0, Width = 70 };
    private readonly CheckBox _positive = new() { Content = "Copy positive to negative", IsChecked = true };
    private readonly TextBox _tolerance = Ux.Field("tolerance", 100);
    private bool _loadingSkin;
    private readonly SkeletonView _skinPreview = new();

    private Control SkinPanel()
    {
        _prune.Text = "0.01";
        _tolerance.Text = "0.05";
        _vertex.Name = "SkinVertex";
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Row(Button("Open skinned NIF", () => _ = Browse(false)), _shapes,
            Button("Undo skin", () => Run(() => { _skin?.Undo(); RefreshSkin(); })),
            Button("Redo skin", () => Run(() => { _skin?.Redo(); RefreshSkin(); })),
            Button("Save skin", () => Run(() => { RequiredSkin().Save(); Say("Skin saved; original retained as .bak."); }))));
        panel.Children.Add(new TextBlock { Text = "Fallout 4 vertex weights only. Saving preserves geometry, bindings and other bytes; weights use the file's half precision.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Row(Ux.Label("Vertex"), _vertex));
        for (int slot = 0; slot < SkinEdit.Slots; slot++)
        {
            _weights[slot].Name = "SkinWeight" + slot;
            _influenceBones[slot].Name = "SkinBone" + slot;
            panel.Children.Add(Row(Ux.Label("Influence " + (slot + 1)), _influenceBones[slot], _weights[slot]));
        }
        panel.Children.Add(Row(Button("Apply influences", () => EditSkin(shape => {
            var influences = Enumerable.Range(0, SkinEdit.Slots).Select(i =>
                new SkinEdit.Influence(_influenceBones[i].SelectedIndex, Values(_weights[i], 1)[0])).ToArray();
            SkinEdit.SetInfluences(shape, VertexIndex(), influences);
        })), Button("Normalize shape weights", () => EditSkin(shape => SkinEdit.Normalise(shape))),
            Ux.Label("Prune below"), _prune, Button("Prune shape weights", () => EditSkin(shape => SkinEdit.Prune(shape, Values(_prune, 1)[0])))));
        panel.Children.Add(Row(Ux.Label("Copy bone weights"), _copyFrom, Ux.Label("to"), _copyTo,
            Button("Copy weights", () => EditSkin(shape => SkinEdit.CopyWeights(shape, _copyFrom.SelectedIndex, _copyTo.SelectedIndex)))));
        panel.Children.Add(Row(Ux.Label("Mirror plane"), _axis, _positive, Ux.Label("Vertex tolerance"), _tolerance,
            Button("Mirror weights", () => {
                string report = "";
                if (EditSkin(shape => report = SkinEdit.MirrorWeights(shape, _axis.SelectedIndex,
                    _positive.IsChecked == true, Values(_tolerance, 1)[0]).ToString()))
                    Say(report + ". Save to write matched edits.");
            })));
        panel.Children.Add(Button("Validate skin", () => Run(() => Say(Findings(SkinValidator.Check(CurrentShape()).Select(f => f.ToString()))))));
        _shapes.SelectionChanged += (_, _) => RefreshSkin();
        _vertex.ValueChanged += (_, _) => LoadInfluences();
        return AuthoringPanel(panel, _skinPreview);
    }

    public void OpenSkin(string path)
    {
        if (_skin?.Dirty == true) throw new InvalidOperationException("Save or discard the current skin before opening another.");
        var document = new SkinDocument(path);
        if (!document.Shapes.Any(SkinEdit.Editable)) throw new NotSupportedException("This file has no editable vertex skin payload.");
        _skin = document;
        _shapes.ItemsSource = document.Shapes.Select((shape, i) => $"{i}: {shape}").ToArray();
        _shapes.SelectedIndex = document.Shapes.Select((shape, i) => (shape, i)).First(pair => SkinEdit.Editable(pair.shape)).i;
        RefreshSkin();
        Say("Skin opened. Edits stay in memory until Save.");
    }

    private void RefreshSkin()
    {
        if (_loadingSkin || _skin == null || _shapes.SelectedIndex < 0) return;
        _loadingSkin = true;
        try
        {
            NifShape shape = CurrentShape();
            _skinPreview.ShowMesh(SkinnedMesh.Edges(shape).Select(edge => (shape.Vertices[edge.From], shape.Vertices[edge.To])).ToArray());
            _skinPreview.Frame();
            _vertex.Maximum = Math.Max(0, shape.Vertices.Count - 1);
            _vertex.Value = Math.Clamp(_vertex.Value ?? 0, 0, _vertex.Maximum);
            string[] names = shape.BoneNames.Select((name, i) => $"{i}: {name}").ToArray();
            foreach (var box in _influenceBones) box.ItemsSource = names;
            _copyFrom.ItemsSource = names;
            _copyTo.ItemsSource = names;
            _copyFrom.SelectedIndex = names.Length > 0 ? 0 : -1;
            _copyTo.SelectedIndex = names.Length > 1 ? 1 : _copyFrom.SelectedIndex;
        }
        finally { _loadingSkin = false; }
        LoadInfluences();
    }

    private void LoadInfluences()
    {
        if (_loadingSkin || _skin == null || _shapes.SelectedIndex < 0) return;
        NifShape shape = CurrentShape();
        if (!SkinEdit.Editable(shape)) { Say("This shape is not skinned with a supported vertex payload."); return; }
        int vertex = VertexIndex();
        for (int slot = 0; slot < SkinEdit.Slots; slot++)
        {
            int index = vertex * SkinEdit.Slots + slot;
            _influenceBones[slot].SelectedIndex = shape.BoneIndices[index];
            _weights[slot].Text = shape.BoneWeights[index].ToString("R", CultureInfo.InvariantCulture);
        }
    }

    private bool EditSkin(Action<NifShape> edit) => Run(() => {
        RequiredSkin().Edit(_shapes.SelectedIndex, edit);
        RefreshSkin();
        Say("Skin edit verified in memory. Save to write it.");
    });

    private SkinDocument RequiredSkin() => _skin ?? throw new InvalidOperationException("Open a skinned NIF first.");
    private NifShape CurrentShape() => RequiredSkin().Shapes[_shapes.SelectedIndex];
    private int VertexIndex()
    {
        decimal value = _vertex.Value ?? 0;
        if (value != decimal.Truncate(value)) throw new ArgumentException("Choose a whole vertex index.");
        return checked((int)value);
    }
}
