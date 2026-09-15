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
        if (needle.Length == 0)
        {
            SetStatus($"Editable. {_objectIds.Count} objects mapped, {_graph.DrawnCount} drawn." +
                      (_graph.DrawingTruncated
                          ? $" Drawing stops at the first {GraphView.MaxNodes} objects."
                          : ""), Ux.MetaBrush);
            return;
        }

        int hits = _tree.RowCount;
        SetStatus(hits == 0
            ? $"Nothing in the tree matches \"{needle}\"."
            : $"{hits} tree row{(hits == 1 ? "" : "s")} match \"{needle}\". Press Enter to go to the first one.",
            hits == 0 ? Ux.MutedBrush : Ux.MetaBrush);
    }

    private void ApplyGraphFilter()
    {
        string needle = (_graphFilter.Text ?? "").Trim();
        _graph.Filter(needle);
        if (_xmlText.Length == 0) return;

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
            ? $"Nothing on the graph matches \"{needle}\"."
            : $"{hits} node{(hits == 1 ? "" : "s")} match \"{needle}\", the rest are dimmed. " +
              "Press Enter to go to the first one.",
            hits == 0 ? Ux.MutedBrush : Ux.MetaBrush);
    }

    private void JumpToFirstTreeMatch()
    {
        string needle = (_filter.Text ?? "").Trim();
        if (needle.Length == 0) return;
        foreach (var node in _objects)
        {
            if (!Matches(node, needle)) continue;
            if (_offsetToIndex.TryGetValue(node.Offset, out int index)
                && index >= 0 && index < _objectIds.Count)
            {
                SelectFromTree(_objectIds[index]);
                return;
            }
        }
    }

    private void JumpToFirstGraphMatch()
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
        _graphFilter.Text = needle;
        ApplyGraphFilter();
    }

    public HkGrid TreeGrid => _tree;
    public HkGrid ChainGrid => _chain;
    public string GameDataSummary => _dataSummary.Text ?? "";
    public TextBox CrashHashField => _crashField;
    public string CrashHashSummary => _crashSummary.Text ?? "";
    public void ResolveCrashHashForTest() => ResolveCrashHash();
    public bool CrashPanelVisible => _crashPanel?.IsVisible == true;
    public string CrashPanelTitle => _crashPanelTitle.Text ?? "";
    public string CrashPanelBodyText => string.Join("\n", FindPanelTexts(_crashPanelBody));
    public TextBox ModsField => _modsField;
    public string ModsSummary => _modsSummary.Text ?? "";
    public void ApplyModsForTest() => ApplyMods();
    public string SweepSummary => _sweepSummary.Text ?? "";
    public void RunSweepForTest()
    {
        var data = _gameData;
        if (data == null) return;
        RenderSweep(OpenCommonwealth.Services.Archive.SubgraphIndex.Sweep(data));
    }
    private static IEnumerable<string> FindPanelTexts(Visual root)
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is TextBlock t && t.Text != null) yield return t.Text;
            foreach (string s in FindPanelTexts(child)) yield return s;
        }
    }

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
}
