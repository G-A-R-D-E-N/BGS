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

        _pasteButton.IsEnabled = _clip != null && _bytes != null && !_readOnly && _hkxPath.Length > 0;
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

        NativePaste.Clip clip = _clip;
        var committed = GraphMutationTransaction.Commit(
            _hkxPath,
            _sourceStamp,
            source =>
            {
                var written = NativePaste.Paste(source, _hkxPath, clip, attachTo, field);
                return new GraphMutationTransaction.Mutation(
                    written.Bytes, written.RootId, written.Objects, clip.Tree.RootClass, written.Note);
            });
        if (!committed.Committed)
        {
            SetPasteSummary("Nothing pasted, and the file is untouched: " + committed.Message,
                            Ux.BadBrush);
            return;
        }

        string said = committed.Message;
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
        SelectObjectId(committed.Change!.RootId.ToString());
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

        var committed = GraphMutationTransaction.Commit(
            _hkxPath,
            _sourceStamp,
            source =>
            {
                var written = TemplateStore.Apply(template, source, _hkxPath, attachTo, field);
                return new GraphMutationTransaction.Mutation(
                    written.Bytes, written.RootId, written.Objects, template.RootClass,
                    $"Applied '{template.Name}'. {written.Note}");
            });
        if (!committed.Committed)
        {
            SetPasteSummary("Nothing applied, and the file is untouched: " + committed.Message,
                            Ux.BadBrush);
            return;
        }

        string said = committed.Message;
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
        SelectObjectId(committed.Change!.RootId.ToString());
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
        var definition = PredefinedTemplates.Get(id);
        if (definition == null) { SetPasteSummary("That predefined template is no longer there.", Ux.BadBrush); return; }
        if (_readOnly) { SetPasteSummary("Not created: " + _readOnlyWhy, Ux.BadBrush); return; }
        if (_dirty) { SetPasteSummary("Save your other changes first.", Ux.BadBrush); return; }
        string? blocked = HkxTextEdit.WhyNotWritable(_hkxPath);
        if (blocked != null) { SetPasteSummary("Cannot create: " + blocked, Ux.BadBrush); return; }

        var raw = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, control) in _predefinedValues)
            raw[key] = control is ComboBox choice ? choice.SelectedItem as string ?? "" : ((TextBox)control).Text ?? "";

        var committed = GraphMutationTransaction.Commit(
            _hkxPath,
            _sourceStamp,
            source =>
            {
                var made = PredefinedTemplates.Instantiate(source, id, raw);
                if (!made.Possible || made.Bytes == null)
                    throw new InvalidOperationException(made.Refusal ?? "the predefined template could not be created");

                return new GraphMutationTransaction.Mutation(
                    made.Bytes, made.RootId, made.CreatedIds.Count, definition.RootClass, made.Summary);
            });
        if (!committed.Committed)
        {
            SetPasteSummary("Nothing created: " + committed.Message, Ux.BadBrush);
            return;
        }

        string said = committed.Message;
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
        SelectObjectId(committed.Change!.RootId.ToString());
        SetPasteSummary(said, Ux.MetaBrush);
        SetStatus(said, Ux.MetaBrush);
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
}
