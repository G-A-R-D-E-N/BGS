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
        panel.SetSchemaClass(className);
        var parameters = PanelValues(objectId, HkxTextEdit.ReadParams(_xmlText, objectId));

        int fromXml = parameters.Count(p => p.From == PanelFields.Source.Fallback);
        string fieldKind = panel.SchemaReadOnly ? "read-only" : "editable";
        var heading = Ux.Label($"#{objectId}   {className}   {parameters.Count} {fieldKind} fields" +
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
}
