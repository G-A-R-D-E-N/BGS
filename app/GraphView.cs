using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.App;

public enum GraphLayoutMode
{
    Freeform,
    StructuredFlow,
}

public enum StructuredFlowDetail
{
    Far,
    Medium,
    Close,
}

public partial class GraphView : Control
{
    private const double NodeWidth = 250;
    private const double HeaderHeight = 22;
    private const double RowHeight = 15;
    private const double ColumnGap = 320;
    private const double RowGap = 26;
    private const double PortRadius = 5;

    public const int MaxNodes = 4000;

    private sealed class Node
    {
        public string Id = "";
        public string Class = "";

        public string OwnerId = "";
        public string Name = "";
        public string Animation = "";
        public Rect Bounds;
        public List<GraphLinks.Slot> Slots = new();
        public Color Accent;
        public bool Empty;
        public bool Start;
        public bool Active;

        public List<string> Wildcards = new();
        public GraphValidator.Level? Problem;
        public Point InPort => new(Bounds.X - PortRadius, Bounds.Y + HeaderHeight / 2);
        public Point OutPort(int index) =>
            new(Bounds.Right + PortRadius, Bounds.Y + HeaderHeight + RowHeight * (index + 0.5) + 2);
    }

    private readonly Dictionary<string, Node> _nodes = new();
    private readonly List<string> _order = new();
    private readonly Dictionary<string, Point> _placed = new();
    private readonly Dictionary<string, Rect> _structuredContainers = new(StringComparer.Ordinal);
    private StructuredFlowLayout.Plan? _structuredPlan;
    private GraphLayoutMode _layoutMode;

    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);

    private int _placedCount;
    private bool _truncated;

    private readonly Dictionary<string, List<string>> _sharedBy = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _nameOf = new(StringComparer.Ordinal);

    public IReadOnlyList<string> SharedBy(string id) =>
        _sharedBy.TryGetValue(id, out var by) ? by : Array.Empty<string>();

    public string OwnerOf(string id) => _own.Owner.TryGetValue(id, out string? owner) ? owner : "";

    public string NameOf(string id) => _nameOf.GetValueOrDefault(id, "#" + id);

    private BehaviourGraphModel? _model;

    private GraphOwnership.Tree _own = GraphOwnership.Of(Array.Empty<(string, string)>());

    private StateRoutes _routes = new();
    private GraphTrace.GraphTraceMap? _trace;

    public bool ShowRoutes { get; set; } = true;

    private const double LabelZoom = 0.55;

    private const int WildcardRows = 4;

    private double _zoom = 0.9;
    private Point _pan = new(40, 40);
    private Point _lastPointer;
    private bool _dragChanged;
    private bool _panning;
    private Node? _dragNode;
    private Point? _marqueeFrom;
    private Rect _marquee;
    private (Node Node, int Slot)? _wiring;
    private Point _wireTo;

    private readonly List<string> _selected = new();
    private string _focusTreeRootId = "";
    private readonly HashSet<string> _traceIds = new(StringComparer.Ordinal);

    public IReadOnlyList<string> SelectedIds => _selected;

    public GraphLayoutMode LayoutMode => _layoutMode;
    public StructuredFlowDetail DetailLevel => CurrentDetail();
    public IReadOnlyCollection<string> StructuredMachineIds => _structuredPlan == null
        ? Array.Empty<string>()
        : _structuredPlan.Machines.Where(m => _nodes.ContainsKey(m.Id)).Select(m => m.Id).ToList();
    public IReadOnlyCollection<string> VisibleStructuredMachineIds => StructuredMachineIds
        .Where(IsDrawnAtCurrentDetail).ToList();

    public Rect? StructuredContainerBounds(string machineId) =>
        _structuredContainers.TryGetValue(machineId, out var bounds) ? bounds : null;

    public string SelectedId => _selected.Count > 0 ? _selected[0] : "";

    private void Select(string id)
    {
        _selected.Clear();
        if (id.Length > 0) _selected.Add(id);
    }

    public Action<string>? Selected;
    public Action<string>? Activated;
    public Action<string, string, string>? LinkRequested;
    public Action<string, string, string>? UnlinkRequested;
    public Action<string>? DeleteRequested;
    public event Action? LayoutChanged;

    public Action<string, string, Point>? AddRequested;

    public GraphView()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    private Dictionary<string, GraphValidator.Level> _problems = new();

    public void Mark(Dictionary<string, GraphValidator.Level> problems)
    {
        _problems = problems;
        foreach (var node in _nodes.Values)
            node.Problem = problems.TryGetValue(node.Id, out var level) ? level : null;
        InvalidateVisual();
    }

    private readonly HashSet<string> _active = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> ActiveIds => _active;

    public void ShowActive(IEnumerable<string> stateIds)
    {
        _active.Clear();
        foreach (string id in stateIds) _active.Add(id);
        foreach (var node in _nodes.Values) node.Active = _active.Contains(node.Id);
        InvalidateVisual();
    }

    public void ClearActive()
    {
        _active.Clear();
        foreach (var node in _nodes.Values) node.Active = false;
        InvalidateVisual();
    }

    private string _highlight = "";
    private readonly HashSet<string> _related = new();

    public string HighlightId => _highlight;

    public void Highlight(string id)
    {
        _highlight = _nodes.ContainsKey(id) ? id : "";
        RebuildRelated();
        InvalidateVisual();
    }

    public void ClearHighlight()
    {
        _highlight = "";
        _related.Clear();
        InvalidateVisual();
    }

    private void RebuildRelated()
    {
        _related.Clear();
        if (_highlight.Length == 0) return;

        _related.Add(_highlight);
        foreach (var node in _nodes.Values)
            foreach (var slot in node.Slots)
                foreach (string target in slot.Targets)
                {
                    if (node.Id == _highlight) _related.Add(target);
                    else if (target == _highlight) _related.Add(node.Id);
                }

        foreach (string id in _routes.Touching(_highlight)) _related.Add(id);
    }

    private string _needle = "";
    private readonly HashSet<string> _matched = new();

    public int MatchCount => _matched.Count;
    public string FirstMatch => _order.FirstOrDefault(_matched.Contains) ?? "";

    public void Filter(string needle)
    {
        _needle = needle.Trim();
        RebuildMatched();
        InvalidateVisual();
    }

    private void RebuildMatched()
    {
        _matched.Clear();
        if (_needle.Length == 0) return;

        foreach (var node in _nodes.Values)
            if (node.Name.Contains(_needle, StringComparison.OrdinalIgnoreCase)
                || node.Class.Contains(_needle, StringComparison.OrdinalIgnoreCase)
                || node.Animation.Contains(_needle, StringComparison.OrdinalIgnoreCase))
                _matched.Add(node.Id);
    }

    public bool IsDimmed(string id) => Dimmed(id);

    private bool Dimmed(string id) =>
        IsTraceDimmed(id)
        || (_highlight.Length > 0 && !_related.Contains(id))
        || (_needle.Length > 0 && !_matched.Contains(id));

    public bool IsTraceDimmed(string id) =>
        _traceIds.Count > 0 && !_traceIds.Contains(id);

    private bool Lit(string fromId, string toId)
    {
        if (_traceIds.Count > 0 && (!_traceIds.Contains(fromId) || !_traceIds.Contains(toId))) return false;
        if (_highlight.Length > 0 && fromId != _highlight && toId != _highlight) return false;
        if (_needle.Length > 0 && !_matched.Contains(fromId) && !_matched.Contains(toId)) return false;
        return true;
    }

    public bool FocusOn(string id)
    {
        if (!_nodes.TryGetValue(id, out var node)) return false;

        Select(id);
        var centre = node.Bounds.Center;
        _pan = new Point(Bounds.Width / 2 - centre.X * _zoom, Bounds.Height / 2 - centre.Y * _zoom);
        InvalidateVisual();
        return true;
    }

    public void Reset()
    {

        _model = null;
        _routes = new StateRoutes();
        _trace = null;
        _nodes.Clear();
        _order.Clear();
        _placed.Clear();
        _structuredContainers.Clear();
        _structuredPlan = null;
        _collapsed.Clear();
        _placedCount = 0;
        _sharedBy.Clear();
        _nameOf.Clear();
        _problems.Clear();
        _highlight = "";
        _related.Clear();
        _needle = "";
        _matched.Clear();
        _active.Clear();
        _selected.Clear();
        _focusTreeRootId = "";
        _traceIds.Clear();
        _zoom = 0.9;
        _pan = new Point(40, 40);
    }

    public void SetLayoutMode(GraphLayoutMode mode)
    {
        if (_layoutMode == mode) return;
        _layoutMode = mode;
        if (_model != null) Show(_model);
        FrameAll();
    }

    public void SetZoomForTest(double zoom) => SetZoom(zoom);

    public void Show(BehaviourGraphModel model)
    {
        _model = model;
        _routes = StateRoutes.Of(model);
        _trace = GraphTrace.Of(model, _routes);
        _nodes.Clear();
        _order.Clear();

        var empty = GraphValidator.StatesWithNoGenerator(model);

        var wildcardsInto = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var route in _routes.Routes.Where(r => r.Wildcard))
        {
            if (!wildcardsInto.TryGetValue(route.ToId, out var events))
                wildcardsInto[route.ToId] = events = new List<string>();
            if (!events.Contains(route.Event)) events.Add(route.Event);
        }

        var placed = GraphAuthor.Layout(model, MaxNodes, out bool truncated);
        _truncated = truncated;
        _own = GraphOwnership.Of(placed);
        _structuredPlan = StructuredFlowLayout.Of(placed);
        _structuredContainers.Clear();

        _placedCount = placed.Count;

        HashSet<string>? focusedIds = null;
        if (_focusTreeRootId.Length > 0)
        {
            var focused = model.Get(_focusTreeRootId);
            if (focused == null || focused.Class != "hkbStateMachine" || !_own.Owner.ContainsKey(_focusTreeRootId))
            {
                _focusTreeRootId = "";
                _traceIds.Clear();
            }
            else
            {
                focusedIds = _own.Under(_focusTreeRootId).Append(_focusTreeRootId)
                    .ToHashSet(StringComparer.Ordinal);
            }
        }

        _sharedBy.Clear();
        _nameOf.Clear();

        foreach (var (obj, _, _) in placed)
        {
            string name = obj.Str("name");
            _nameOf[obj.Id] = name.Length > 0 ? name : "#" + obj.Id;
        }

        foreach (var (obj, _, _) in placed)
            foreach (string target in GraphAuthor.PointsAt(model, obj))
            {
                if (target == obj.Id) continue;
                if (!_own.Owner.TryGetValue(target, out string? owner) || owner == obj.Id) continue;

                if (!_sharedBy.TryGetValue(target, out var by))
                    _sharedBy[target] = by = new List<string>();
                if (!by.Contains(obj.Id)) by.Add(obj.Id);
            }

        var showing = placed
            .Where(p => !_own.Hidden(_collapsed, p.Node.Id)
                        && (focusedIds == null || focusedIds.Contains(p.Node.Id)))
            .ToList();

        var measured = new List<GraphLayout.Item>();
        var slotsOf = new Dictionary<string, List<GraphLinks.Slot>>(StringComparer.Ordinal);
        var wildcardsOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var heightOf = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var (obj, column, ownerId) in showing)
        {

            var slots = GraphLinks.OutSlots(model, obj);
            var wildcards = wildcardsInto.GetValueOrDefault(obj.Id) ?? new List<string>();
            double height = HeaderHeight + Math.Max(1, slots.Count) * RowHeight
                            + Math.Min(wildcards.Count, WildcardRows) * RowHeight + 8;

            slotsOf[obj.Id] = slots;
            wildcardsOf[obj.Id] = wildcards;
            heightOf[obj.Id] = height;
            measured.Add(new GraphLayout.Item(obj.Id, column, ownerId, height));
        }

        var pinned = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, at) in _placed)
            if (heightOf.ContainsKey(id)) pinned[id] = at.Y;

        var freeformY = GraphLayout.Place(measured, pinned, RowGap);
        var structuredAt = _layoutMode == GraphLayoutMode.StructuredFlow
            ? StructuredPositions(showing, heightOf)
            : new Dictionary<string, Point>(StringComparer.Ordinal);

        foreach (var (obj, column, ownerId) in showing)
        {
            Point at = _layoutMode == GraphLayoutMode.StructuredFlow
                ? structuredAt[obj.Id]
                : _placed.TryGetValue(obj.Id, out var kept)
                    ? kept
                    : new Point(column * ColumnGap, freeformY[obj.Id]);

            _order.Add(obj.Id);
            _nodes[obj.Id] = new Node
            {
                Id = obj.Id,
                Class = obj.Class,
                OwnerId = ownerId,
                Name = obj.Str("name"),
                Animation = obj.Str("animationName"),
                Slots = slotsOf[obj.Id],
                Accent = Ux.ForClass(obj.Class),
                Empty = empty.Contains(obj.Id),
                Start = _routes.StartStates.Contains(obj.Id),
                Active = _active.Contains(obj.Id),
                Wildcards = wildcardsOf[obj.Id],
                Problem = _problems.TryGetValue(obj.Id, out var level) ? level : null,
                Bounds = new Rect(at.X, at.Y, NodeWidth, heightOf[obj.Id]),
            };
        }

        if (_layoutMode == GraphLayoutMode.StructuredFlow) BuildStructuredContainers();

        _selected.RemoveAll(id => !_nodes.ContainsKey(id));
        if (_highlight.Length > 0 && !_nodes.ContainsKey(_highlight)) _highlight = "";
        if (_traceIds.Count > 0)
        {
            _traceIds.RemoveWhere(id => !_nodes.ContainsKey(id));
            if (_traceIds.Count == 0) _traceIds.Clear();
        }
        RebuildRelated();
        RebuildMatched();
        InvalidateVisual();
    }

    private Dictionary<string, Point> StructuredPositions(
        IReadOnlyList<(HkObject Node, int Column, string OwnerId)> showing,
        IReadOnlyDictionary<string, double> heightOf)
    {
        var showingIds = showing.Select(p => p.Node.Id).ToHashSet(StringComparer.Ordinal);
        var at = new Dictionary<string, Point>(StringComparer.Ordinal);
        if (_structuredPlan == null) return at;

        var structural = showing.Where(p => IsDrawnAtCurrentDetail(p.Node.Id)
                                            && _structuredPlan.Item(p.Node.Id).Kind
            is StructuredFlowLayout.NodeKind.Root
            or StructuredFlowLayout.NodeKind.Machine
            or StructuredFlowLayout.NodeKind.State).ToList();

        const int Columns = 5;
        const double ColumnWidth = NodeWidth + 54;
        double nextY = 0;
        foreach (var rank in structural.GroupBy(p => _structuredPlan.Item(p.Node.Id).Depth)
                                       .OrderBy(group => group.Key))
        {
            var row = rank.OrderBy(p => _structuredPlan.Item(p.Node.Id).SiblingOrder)
                          .ThenBy(p => p.Node.Id, StringComparer.Ordinal).ToList();
            double height = row.Max(p => heightOf[p.Node.Id]);
            for (int index = 0; index < row.Count; index++)
                at[row[index].Node.Id] = new Point(index % Columns * ColumnWidth,
                                                    nextY + index / Columns * (height + 36));
            nextY += ((row.Count + Columns - 1) / Columns) * (height + 36) + 86;
        }

        foreach (var (node, _, _) in showing.Where(p => !at.ContainsKey(p.Node.Id)
                                                        && _structuredPlan.Item(p.Node.Id).Kind
                                                           is not StructuredFlowLayout.NodeKind.Helper))
        {
            string anchor = _structuredPlan.Item(node.Id).StructuralAncestorIds
                .LastOrDefault(id => at.ContainsKey(id)) ?? at.Keys.FirstOrDefault() ?? "";
            at[node.Id] = anchor.Length > 0 ? at[anchor] : default;
        }

        var helperNumber = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (node, _, _) in showing.Where(p => !at.ContainsKey(p.Node.Id)))
        {
            var item = _structuredPlan.Item(node.Id);
            string anchor = item.StructuralAncestorIds.LastOrDefault(id => at.ContainsKey(id)) ?? "";
            if (anchor.Length == 0) anchor = at.Keys.FirstOrDefault() ?? "";
            if (anchor.Length == 0) { at[node.Id] = default; continue; }

            int index = helperNumber.GetValueOrDefault(anchor);
            helperNumber[anchor] = index + 1;
            var parent = at[anchor];
            at[node.Id] = new Point(parent.X + NodeWidth + 48 + index / 4 * 94,
                                    parent.Y + index % 4 * 82);
        }
        return at;
    }

    private void BuildStructuredContainers()
    {
        _structuredContainers.Clear();
        if (_structuredPlan == null) return;

        foreach (var machine in _structuredPlan.Machines.OrderByDescending(m => m.Depth))
        {
            var members = _nodes.Values.Where(node => InStructuredMachine(node.Id, machine.Id)
                                                       && IsDrawnAtCurrentDetail(node.Id))
                                      .Select(node => node.Bounds).ToList();
            if (members.Count == 0) continue;

            double left = members.Min(b => b.Left), top = members.Min(b => b.Top);
            double right = members.Max(b => b.Right), bottom = members.Max(b => b.Bottom);
            _structuredContainers[machine.Id] = new Rect(left - 24, top - 34,
                                                          right - left + 48, bottom - top + 58);
        }
    }

    private bool InStructuredMachine(string id, string machineId)
    {
        if (_structuredPlan == null || !_structuredPlan.Items.TryGetValue(id, out var item)) return false;
        return item.MachineId == machineId;
    }

    private StructuredFlowDetail CurrentDetail() => _layoutMode == GraphLayoutMode.Freeform
        ? StructuredFlowDetail.Close
        : _zoom < 0.80 ? StructuredFlowDetail.Far
        : _zoom < 1.05 ? StructuredFlowDetail.Medium
        : StructuredFlowDetail.Close;

    public bool IsDrawnAtCurrentDetail(string id)
    {
        if (_layoutMode == GraphLayoutMode.Freeform) return _nodes.ContainsKey(id);
        if (_structuredPlan == null || !_structuredPlan.Items.TryGetValue(id, out var item)) return false;
        if (item.Kind == StructuredFlowLayout.NodeKind.Root) return true;
        if (item.Kind == StructuredFlowLayout.NodeKind.Machine)
        {
            if (CurrentDetail() != StructuredFlowDetail.Far) return true;
            return item.ParentMachineId.Length == 0 || _structuredPlan.Machines
                .Any(root => root.ParentMachineId.Length == 0 && root.Id == item.ParentMachineId);
        }
        if (CurrentDetail() == StructuredFlowDetail.Close) return true;
        if (item.Kind == StructuredFlowLayout.NodeKind.State) return CurrentDetail() != StructuredFlowDetail.Far;
        return CurrentDetail() == StructuredFlowDetail.Medium &&
               (_traceIds.Contains(id) || item.StructuralAncestorIds.Any(_selected.Contains));
    }

    public bool SetFocusTree(string machineId)
    {
        if (_model == null) return false;
        var machine = _model.Get(machineId);
        if (machine == null || machine.Class != "hkbStateMachine" || !_own.Owner.ContainsKey(machineId))
            return false;

        _focusTreeRootId = machineId;
        ClearTrace();
        Show(_model);
        FrameAll();
        return true;
    }

    public void ClearFocusTree()
    {
        if (_focusTreeRootId.Length == 0) return;
        _focusTreeRootId = "";
        ClearTrace();
        if (_model != null) Show(_model);
        FrameAll();
    }

    public bool FocusTreeActive => _focusTreeRootId.Length > 0;
    public string FocusTreeRootId => _focusTreeRootId;

    public bool Trace(GraphTrace.Direction direction)
    {
        if (_trace == null || SelectedId.Length == 0 || !_nodes.ContainsKey(SelectedId)) return false;

        var visible = _nodes.Keys.ToHashSet(StringComparer.Ordinal);
        var found = _trace.Reachable(SelectedId, direction, visible);
        if (found.Count == 0) return false;

        _traceIds.Clear();
        foreach (string id in found) _traceIds.Add(id);
        Frame(_traceIds.Where(_nodes.ContainsKey).Select(id => _nodes[id].Bounds));
        InvalidateVisual();
        return true;
    }

    public void ClearTrace()
    {
        if (_traceIds.Count == 0) return;
        _traceIds.Clear();
        InvalidateVisual();
    }

    public bool TraceActive => _traceIds.Count > 0;
    public IReadOnlyCollection<string> TraceIds => _traceIds;

    public string HeaderTextOf(string id)
    {
        if (_nodes.TryGetValue(id, out var node)) return HeaderText(node);
        var obj = _model?.Get(id);
        if (obj == null) return "";
        string title = obj.Str("name");
        if (title.Length == 0) title = obj.Class;
        return title + " #" + id;
    }

    private static string HeaderText(Node node)
    {
        string title = node.Name.Length > 0 ? node.Name : node.Class;
        return title + " #" + node.Id;
    }

    public IEnumerable<double> OwnershipWireDrops()
    {
        foreach (var node in _nodes.Values)
        {
            if (node.OwnerId.Length == 0) continue;
            if (!_nodes.TryGetValue(node.OwnerId, out var owner)) continue;

            yield return Math.Abs((node.Bounds.Y + node.Bounds.Height / 2)
                                  - (owner.Bounds.Y + owner.Bounds.Height / 2));
        }
    }

    public IReadOnlyCollection<string> Collapsed => _collapsed;

    public int OwnedCount(string id) => _own.Under(id).Count(_nodes.ContainsKey);

    public IReadOnlyList<string> OwnedIds(string id) => _own.Under(id).Where(_nodes.ContainsKey).ToList();

    public int HiddenCount => Math.Max(0, _placedCount - _nodes.Count);

    public bool IsCollapsed(string id) => _collapsed.Contains(id);

    public void SelectForTest(IEnumerable<string> ids)
    {
        _selected.Clear();
        foreach (string id in ids) if (_nodes.ContainsKey(id)) _selected.Add(id);
        InvalidateVisual();
    }

    public void DragForTest(string id, double byX, double byY)
    {
        if (_nodes.TryGetValue(id, out var from) && Move(from, byX, byY)
            && _layoutMode == GraphLayoutMode.Freeform) LayoutChanged?.Invoke();
        InvalidateVisual();
    }

    public void RestoreFreeformPositions(IReadOnlyDictionary<string, Settings.LayoutPoint> positions)
    {
        _placed.Clear();
        foreach (var (id, at) in positions)
            if (double.IsFinite(at.X) && double.IsFinite(at.Y))
                _placed[id] = new Point(at.X, at.Y);
    }

    public IReadOnlyDictionary<string, Settings.LayoutPoint> SnapshotFreeformPositions() =>
        _placed.Where(pair => double.IsFinite(pair.Value.X) && double.IsFinite(pair.Value.Y))
               .ToDictionary(pair => pair.Key,
                   pair => new Settings.LayoutPoint(pair.Value.X, pair.Value.Y), StringComparer.Ordinal);

    public IReadOnlyCollection<string> MovementSet(string id)
    {
        var picked = _selected.Contains(id) ? (IEnumerable<string>)_selected : new[] { id };
        return _own.Moving(picked).Where(_nodes.ContainsKey).ToList();
    }

    public void ToggleCollapse(string id, bool deep)
    {
        if (!_own.Owner.ContainsKey(id)) return;

        if (!deep)
        {
            if (!_collapsed.Add(id)) _collapsed.Remove(id);
        }
        else
        {
            var family = _own.Under(id).Append(id).Where(n => _own.Children(n).Count > 0).ToList();
            bool anyOpen = family.Any(n => !_collapsed.Contains(n));

            foreach (string node in family)
                if (anyOpen) _collapsed.Add(node); else _collapsed.Remove(node);
        }

        if (_model != null) Show(_model);
    }

    private Rect ChevronRect(Node node)
    {
        var at = ToScreen(node.Bounds.TopLeft);
        return new Rect(at.X + 2 * _zoom, at.Y + 3 * _zoom, 13 * _zoom, 13 * _zoom);
    }

    private bool HasFamily(Node node) => _own.Children(node.Id).Count > 0;

    public int DrawnCount => _nodes.Count;
    public bool DrawingTruncated => _truncated;
    public IReadOnlyCollection<string> DrawnIds => _nodes.Keys;

    public int RouteCount => _routes.Routes.Count;
    public int DrawableRouteCount =>
        _routes.Routes.Count(r => _nodes.ContainsKey(r.FromId) && _nodes.ContainsKey(r.ToId));
    public int NestedRouteCount => _routes.Routes.Count(r => r.IntoId.Length > 0);

    public Rect VisibleWorld() =>
        new(ToWorld(new Point(0, 0)), ToWorld(new Point(Bounds.Width, Bounds.Height)));

    public (double Wide, double Tall) Extent()
    {
        return ExtentOf(_nodes.Values);
    }

    public (double Wide, double Tall) VisibleExtent() =>
        ExtentOf(_nodes.Values.Where(n => IsDrawnAtCurrentDetail(n.Id)));

    private static (double Wide, double Tall) ExtentOf(IEnumerable<Node> nodes)
    {
        var list = nodes.ToList();
        if (list.Count == 0) return (0, 0);
        return (list.Max(n => n.Bounds.Right) - list.Min(n => n.Bounds.X),
                list.Max(n => n.Bounds.Bottom) - list.Min(n => n.Bounds.Y));
    }

    public IReadOnlyList<string> WildcardsInto(string id) =>
        _nodes.TryGetValue(id, out var node) ? node.Wildcards : Array.Empty<string>();

    public int LineCount => RoutesToDraw().Count(r => _nodes.ContainsKey(r.FromId) && _nodes.ContainsKey(r.ToId));
    public IReadOnlyCollection<string> StartStateIds => _routes.StartStates;
    public bool IsStart(string id) => _nodes.TryGetValue(id, out var node) && node.Start;

    public Point? PositionOf(string id) => _nodes.TryGetValue(id, out var node) ? node.Bounds.TopLeft : null;

    public void Place(string id, Point at)
    {
        _placed[id] = at;
        if (_nodes.TryGetValue(id, out var node))
            node.Bounds = node.Bounds.WithX(at.X).WithY(at.Y);
        InvalidateVisual();
    }

    private Point ToWorld(Point screen) => new((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);
    private Point ToScreen(Point world) => new(world.X * _zoom + _pan.X, world.Y * _zoom + _pan.Y);

}
