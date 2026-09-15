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
    private void AddWeaponGaps(ProjectChain chain)
    {
        if (chain.Data == null || _reading == null) return;

        var gaps = GraphValidator.Check(_reading, chain)
                                 .Where(f => f.Where == "weapon subgraph")
                                 .ToList();
        if (gaps.Count == 0) return;

        var head = _chain.Add(null, "weapon clips",
                              $"{gaps.Count} per-weapon gap{(gaps.Count == 1 ? "" : "s")} in this behaviour")
                         .Colour(0, Ux.MutedBrush).Colour(1, Ux.WarnBrush);
        foreach (var f in gaps)
            _chain.Add(head, "weapon subgraph", f.What)
                  .Colour(1, f.Level == GraphValidator.Level.Error ? Ux.BadBrush : Ux.WarnBrush);
    }

    private void AddChainAnimations(ProjectChain chain)
    {
        if (chain.Animations.Count == 0) return;

        var head = _chain.Add(null, "animations", $"{chain.Animations.Count} declared by the character")
                         .Colour(0, Ux.MutedBrush).Colour(1, Ux.TitleBrush).Collapse();
        int loose = 0, archived = 0;
        foreach (string anim in chain.Animations)
        {
            string source = chain.AnimationSources.TryGetValue(anim, out string? s) ? s ?? "" : "";
            if (source == "loose") loose++;
            else if (source.Length > 0) archived++;

            var row = _chain.Add(head, "", anim, source.Length > 0 ? source : "", "")
                             .Colour(1, Ux.CodeBrush);
            if (source.Length > 0) row.Colour(2, Ux.MetaBrush);
        }
        if (chain.Data != null)
            _chain.Add(head, "sources", $"{loose} loose, {archived} inside .ba2 archives")
                   .Colour(0, Ux.MutedBrush).Colour(1, Ux.MetaBrush);
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

}
