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




public class GraphView : Control
{
    private const double NodeWidth = 250;
    private const double HeaderHeight = 22;
    private const double RowHeight = 15;
    private const double ColumnGap = 320;
    private const double RowGap = 26;
    private const double PortRadius = 5;


    private const int MaxNodes = 4000;

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




        var placed = GraphAuthor.Layout(model, MaxNodes);
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
        if (_nodes.TryGetValue(id, out var from)) Move(from, byX, byY);
        InvalidateVisual();
    }


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

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Ux.BaseBrush, new Rect(Bounds.Size));

        var grid = new Pen(new SolidColorBrush(Color.Parse("#1E1E1E")), 1);
        for (double x = _pan.X % (60 * _zoom); x < Bounds.Width; x += 60 * _zoom)
            ctx.DrawLine(grid, new Point(x, 0), new Point(x, Bounds.Height));
        for (double y = _pan.Y % (60 * _zoom); y < Bounds.Height; y += 60 * _zoom)
            ctx.DrawLine(grid, new Point(0, y), new Point(Bounds.Width, y));

        if (_model == null) return;

        if (_layoutMode == GraphLayoutMode.StructuredFlow) DrawStructuredContainers(ctx);





        bool focused = _highlight.Length > 0 || _needle.Length > 0 || _traceIds.Count > 0;
        for (int pass = focused ? 0 : 1; pass < 2; pass++)
            foreach (var node in _nodes.Values)
                for (int i = 0; i < node.Slots.Count; i++)
                    foreach (string target in node.Slots[i].Targets)
                    {
                        if (!IsDrawnAtCurrentDetail(node.Id) || !IsDrawnAtCurrentDetail(target)) continue;
                        if (!_nodes.TryGetValue(target, out var to)) continue;
                        bool lit = Lit(node.Id, target);
                        if (lit != (pass == 1)) continue;

                        var from = ToScreen(node.OutPort(i));
                        var into = ToScreen(to.InPort);
                        if (OffScreen(from, into)) continue;

                        DrawLink(ctx, from, node.Accent, 1.6, lit ? 0.9 : 0.42, into,
                                 cased: lit && focused);
                    }

        if (ShowRoutes) DrawRoutes(ctx);

        if (_wiring is { } w)
            DrawLink(ctx, ToScreen(w.Node.OutPort(w.Slot)), Ux.Accent, 2.2, 0.85, _wireTo);

        foreach (var node in _nodes.Values)
        {
            if (!IsDrawnAtCurrentDetail(node.Id)) continue;
            if (!Dimmed(node.Id)) DrawNode(ctx, node);
            else using (ctx.PushOpacity(0.4)) DrawNode(ctx, node);
        }

        DrawMarquee(ctx);
    }

    private void DrawStructuredContainers(DrawingContext ctx)
    {
        if (_structuredPlan == null) return;

        foreach (var machine in _structuredPlan.Machines.OrderBy(m => m.Depth))
        {
            if (!_structuredContainers.TryGetValue(machine.Id, out var world)) continue;
            var box = new Rect(ToScreen(world.TopLeft), new Size(world.Width * _zoom, world.Height * _zoom));
            if (!box.Intersects(new Rect(Bounds.Size))) continue;

            bool selected = _selected.Contains(machine.Id);
            var accent = _nodes.TryGetValue(machine.Id, out var node) ? node.Accent : Ux.RouteColour;
            ctx.DrawRectangle(new SolidColorBrush(accent, selected ? 0.13 : 0.07),
                              new Pen(new SolidColorBrush(accent, selected ? 0.92 : 0.68), selected ? 2.3 : 1.6),
                              box, 10, 10);
            Draw(ctx, HeaderTextOf(machine.Id), box.X + 10 * _zoom, box.Y + 7 * _zoom,
                 Math.Max(9, 11 * _zoom), new SolidColorBrush(accent), box.Width - 20 * _zoom);
        }
    }


















    private IEnumerable<StateRoutes.Route> RoutesToDraw()
    {





        var direct = _routes.Routes.Where(r => !r.Wildcard);

        if (_highlight.Length == 0 || !_routes.MachineOfState.ContainsKey(_highlight))
            return direct;



        return direct.Concat(_routes.LeavingState(_highlight).Where(r => r.Wildcard));
    }










    private void DrawRoutes(DrawingContext ctx)
    {
        bool focused = _highlight.Length > 0 || _needle.Length > 0 || _traceIds.Count > 0;
        var wanted = new List<(string Text, Point At, Color Colour, bool Lit, bool Wildcard)>();

        foreach (var route in RoutesToDraw())
        {
            if (!_nodes.TryGetValue(route.FromId, out var from)) continue;
            if (!_nodes.TryGetValue(route.ToId, out var to)) continue;
            if (!IsDrawnAtCurrentDetail(route.FromId) || !IsDrawnAtCurrentDetail(route.ToId)) continue;

            bool lit = Lit(route.FromId, route.ToId);









            double weight = route.Wildcard && !lit ? 0.9 : 1.4;
            double alpha = route.Wildcard ? (lit ? 0.95 : 0.10) : lit ? 1.0 : 0.28;
            bool cased = lit && focused;

            var a = ToScreen(RouteExit(from, to));
            var b = ToScreen(RouteEntry(to, from));
            if (OffScreen(a, b)) continue;

            var colour = route.Wildcard ? Ux.Wildcard : Ux.RouteColour;
            DrawLink(ctx, a, colour, weight, alpha, b, dashed: true, cased: cased);
            DrawArrowHead(ctx, a, b, colour, alpha);




            if (route.IntoId.Length > 0 && _nodes.TryGetValue(route.IntoId, out var into))
            {
                var c = ToScreen(RouteExit(to, into));
                var d = ToScreen(RouteEntry(into, to));
                DrawLink(ctx, c, colour, weight * 0.8, alpha * 0.8, d, dashed: true, cased: cased);
                DrawArrowHead(ctx, c, d, colour, alpha * 0.8);
            }

            if (!lit || _zoom < LabelZoom) continue;



            if (route.Wildcard && !focused) continue;

            wanted.Add((route.Wildcard ? "any: " + route.Event : route.Event,
                        new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2), colour, lit, route.Wildcard));
        }

        DrawLabels(ctx, wanted);
    }










    private void DrawLabels(DrawingContext ctx,
                            List<(string Text, Point At, Color Colour, bool Lit, bool Wildcard)> wanted)
    {
        var taken = new List<Rect>();

        foreach (var label in wanted.OrderByDescending(l => l.Lit).ThenBy(l => l.Wildcard))
        {
            var formatted = new FormattedText(label.Text, CultureInfo.InvariantCulture,
                                              FlowDirection.LeftToRight, Typeface.Default,
                                              Math.Max(8, 10 * _zoom), new SolidColorBrush(label.Colour));

            var box = new Rect(label.At.X - formatted.Width / 2 - 3, label.At.Y - formatted.Height / 2 - 1,
                               formatted.Width + 6, formatted.Height + 2);

            if (box.Right < 0 || box.X > Bounds.Width || box.Bottom < 0 || box.Y > Bounds.Height) continue;
            if (taken.Any(t => t.Intersects(box))) continue;

            taken.Add(box);
            ctx.DrawRectangle(new SolidColorBrush(Ux.Base, 0.85), null, box, 3, 3);
            ctx.DrawText(formatted, new Point(box.X + 3, box.Y + 1));
        }
    }




    private static Point RouteExit(Node from, Node to) =>
        to.Bounds.Center.Y < from.Bounds.Y ? new Point(from.Bounds.Center.X, from.Bounds.Y)
        : to.Bounds.Center.Y > from.Bounds.Bottom ? new Point(from.Bounds.Center.X, from.Bounds.Bottom)
        : new Point(to.Bounds.Center.X < from.Bounds.X ? from.Bounds.X : from.Bounds.Right,
                    from.Bounds.Center.Y);

    private static Point RouteEntry(Node to, Node from) => RouteExit(to, from);

    private void DrawArrowHead(DrawingContext ctx, Point from, Point to, Color colour, double alpha)
    {
        double angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
        double size = Math.Max(4, 7 * _zoom);

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(to, true);
            g.LineTo(to - new Vector(Math.Cos(angle - 0.4) * size, Math.Sin(angle - 0.4) * size));
            g.LineTo(to - new Vector(Math.Cos(angle + 0.4) * size, Math.Sin(angle + 0.4) * size));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(new SolidColorBrush(colour, alpha), null, geometry);
    }



    private bool OffScreen(Point from, Point to)
    {
        double margin = Math.Max(40, Math.Abs(to.X - from.X) * 0.45) + 10;
        return Math.Max(from.X, to.X) + margin < 0
            || Math.Min(from.X, to.X) - margin > Bounds.Width
            || Math.Max(from.Y, to.Y) < 0
            || Math.Min(from.Y, to.Y) > Bounds.Height;
    }







    private void DrawLink(DrawingContext ctx, Point from, Color colour, double width, double alpha,
                          Point to, bool dashed = false, bool cased = false)
    {
        double bend = Math.Max(40, Math.Abs(to.X - from.X) * 0.45);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(from, false);
            if (dashed)
            {


                var lift = new Vector((to.X - from.X) * 0.3, (to.Y - from.Y) * 0.15);
                g.CubicBezierTo(from + lift, to - lift, to);
            }
            else
            {
                g.CubicBezierTo(from + new Vector(bend, 0), to - new Vector(bend, 0), to);
            }
            g.EndFigure(false);
        }








        if (cased)
        {
            var casing = new Pen(new SolidColorBrush(Ux.Casing, 0.95), width + 2.6)
            {
                LineCap = PenLineCap.Round,
            };
            ctx.DrawGeometry(null, casing, geometry);
        }

        var pen = new Pen(new SolidColorBrush(colour, alpha), width);
        if (dashed) pen.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);
        ctx.DrawGeometry(null, pen, geometry);
    }



    private void DrawMarquee(DrawingContext ctx)
    {
        if (_marquee.Width < 1 && _marquee.Height < 1) return;

        var box = new Rect(ToScreen(_marquee.TopLeft),
                           new Size(_marquee.Width * _zoom, _marquee.Height * _zoom));

        ctx.DrawRectangle(new SolidColorBrush(Ux.RouteColour, 0.10),
                          new Pen(new SolidColorBrush(Ux.RouteColour, 0.7), 1), box);
    }

    private void DrawNode(DrawingContext ctx, Node node)
    {
        var r = new Rect(ToScreen(node.Bounds.TopLeft), new Size(node.Bounds.Width * _zoom, node.Bounds.Height * _zoom));
        if (!r.Intersects(new Rect(Bounds.Size))) return;

        bool selected = _selected.Contains(node.Id);
        var body = new SolidColorBrush(selected ? Ux.CardHover : Ux.Card);




        Color? fault = node.Problem switch
        {
            GraphValidator.Level.Error => Ux.Bad,
            GraphValidator.Level.Warning => Ux.Warn,
            _ => node.Empty ? Ux.Bad : null,
        };

        if (fault is { } colour)
            for (int ring = 3; ring >= 1; ring--)
                ctx.DrawRectangle(null, new Pen(new SolidColorBrush(colour, 0.10 * ring), ring * 2 + 1),
                                  r.Inflate(ring * 1.5), 5, 5);




        if (node.Active)
            for (int ring = 4; ring >= 1; ring--)
                ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Ux.RouteColour, 0.16 * ring), ring * 2 + 2),
                                  r.Inflate(ring * 2.0), 6, 6);

        var borderColour = node.Active ? Ux.RouteColour : fault ?? node.Accent;
        var edge = new Pen(new SolidColorBrush(borderColour), node.Active ? 3 : fault != null ? 2.5 : selected ? 2 : 1);
        ctx.DrawRectangle(body, edge, r, 4, 4);







        if (_sharedBy.ContainsKey(node.Id))
            ctx.DrawRectangle(null,
                new Pen(new SolidColorBrush(borderColour, selected ? 0.28 : 0.45), 1),
                r.Deflate(3), 3, 3);
        ctx.DrawRectangle(new SolidColorBrush(borderColour, node.Active ? 0.30 : fault != null ? 0.22 : 0.35), null,
            new Rect(r.X, r.Y, r.Width, HeaderHeight * _zoom), 4, 4);

        double scale = _zoom;
        var faultBrush = fault is { } f ? new SolidColorBrush(f) : null;
        string title = node.Name.Length > 0 ? node.Name : node.Class;
        string chipText = "#" + node.Id;



        bool family = HasFamily(node);
        double titleAt = family ? 18 : 6;
        double chipWidth = Math.Clamp((chipText.Length * 6 + 10) * scale, 24 * scale, 58 * scale);
        var chip = new Rect(r.Right - (chipWidth + 5 * scale), r.Y + 4 * scale,
                            chipWidth, 13 * scale);

        Draw(ctx, title, r.X + titleAt * scale, r.Y + 4 * scale, 11 * scale,
             faultBrush ?? Ux.TitleBrush, r.Width - (titleAt + 14) * scale - chipWidth);
        ctx.DrawRectangle(new SolidColorBrush(Ux.Base, 0.38), null, chip, 3, 3);
        Draw(ctx, chipText, chip.X + 3 * scale, chip.Y + 1 * scale, 8 * scale,
             Ux.MetaBrush, chip.Width - 6 * scale);

        if (family)
        {
            bool shut = _collapsed.Contains(node.Id);
            var chevron = ChevronRect(node);
            Draw(ctx, shut ? ">" : "v", chevron.X + 2 * scale, chevron.Y - 1 * scale, 11 * scale,
                 shut ? new SolidColorBrush(node.Accent) : Ux.MutedBrush, chevron.Width);




            if (shut)
            {
                int held = _own.HiddenBy(_collapsed, node.Id);
                var badge = new Rect(r.Right - 66 * scale, r.Bottom - 15 * scale, 62 * scale, 13 * scale);
                ctx.DrawRectangle(new SolidColorBrush(node.Accent, 0.30), null, badge, 3, 3);
                Draw(ctx, $"+{held} hidden", badge.X + 4 * scale, badge.Y + 1 * scale, 8 * scale,
                     Ux.TitleBrush, badge.Width - 6 * scale);
            }
        }
        Draw(ctx, node.Empty ? node.Class + "  nothing to play" : node.Class,
             r.X + 6 * scale, r.Y + (HeaderHeight + 1) * scale, 9 * scale,
             faultBrush ?? new SolidColorBrush(node.Accent), r.Width - 12 * scale);




        if (node.Start)
        {
            var badge = new Rect(r.Right - 30 * scale, r.Y - 7 * scale, 28 * scale, 13 * scale);
            ctx.DrawRectangle(new SolidColorBrush(Ux.Good), null, badge, 3, 3);
            Draw(ctx, "start", badge.X + 3 * scale, badge.Y + 1 * scale, 8 * scale,
                 Ux.BaseBrush, badge.Width - 4 * scale);
        }

        ctx.DrawEllipse(new SolidColorBrush(node.Accent), null, ToScreen(node.InPort), PortRadius * scale, PortRadius * scale);

        for (int i = 0; i < node.Slots.Count; i++)
        {
            var slot = node.Slots[i];
            string label = slot.Array ? slot.Field + " []" : slot.Field;
            if (slot.Targets.Count > 0) label += "  " + slot.Targets.Count;

            double y = r.Y + (HeaderHeight + RowHeight * i + 10) * scale;
            Draw(ctx, label, r.X + 6 * scale, y, 9 * scale,
                 slot.Targets.Count > 0 ? Ux.MetaBrush : Ux.MutedBrush, r.Width - 16 * scale, true);

            var port = ToScreen(node.OutPort(i));
            var fill = slot.Targets.Count > 0 ? new SolidColorBrush(node.Accent) : Ux.BorderBrush;
            ctx.DrawEllipse(fill, new Pen(new SolidColorBrush(node.Accent), 1), port, PortRadius * scale, PortRadius * scale);
        }

        DrawWildcards(ctx, node, r, scale);
    }









    private void DrawWildcards(DrawingContext ctx, Node node, Rect r, double scale)
    {
        if (node.Wildcards.Count == 0) return;

        double top = r.Y + (HeaderHeight + RowHeight * Math.Max(1, node.Slots.Count) + 10) * scale;
        int shown = Math.Min(node.Wildcards.Count, WildcardRows);

        for (int i = 0; i < shown; i++)
        {


            bool last = i == shown - 1 && node.Wildcards.Count > shown;
            string text = last
                ? $"any: {node.Wildcards[i]}  +{node.Wildcards.Count - shown + 1} more"
                : "any: " + node.Wildcards[i];

            Draw(ctx, text, r.X + 6 * scale, top + RowHeight * i * scale, 9 * scale,
                 new SolidColorBrush(Ux.Wildcard), r.Width - 12 * scale);
        }
    }

    private static void Draw(DrawingContext ctx, string text, double x, double y, double size,
                             IBrush brush, double maxWidth, bool rightAlign = false)
    {
        if (size < 4) return;
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                          Typeface.Default, size, brush) { MaxTextWidth = Math.Max(10, maxWidth) };
        ctx.DrawText(formatted, new Point(rightAlign ? x + maxWidth - formatted.Width : x, y));
    }






    private void Move(Node from, double byX, double byY)
    {
        var picked = _selected.Contains(from.Id) ? (IEnumerable<string>)_selected : new[] { from.Id };

        foreach (string id in _own.Moving(picked))
        {
            if (!_nodes.TryGetValue(id, out var node)) continue;
            node.Bounds = node.Bounds.WithX(node.Bounds.X + byX).WithY(node.Bounds.Y + byY);
            _placed[id] = node.Bounds.TopLeft;
        }
    }









    private void Hovering(Node? node)
    {
        string over = node?.Id ?? "";
        if (over == _hovered) return;
        _hovered = over;

        string tip = SharedTip(over);
        ToolTip.SetTip(this, tip.Length > 0 ? tip : null);
    }







    public string SharedTip(string id)
    {
        if (id.Length == 0) return "";

        var borrowers = SharedBy(id);
        if (borrowers.Count == 0) return "";

        string owner = OwnerOf(id);
        var homes = new List<string>();
        if (owner.Length > 0) homes.Add(_nameOf.GetValueOrDefault(owner, "#" + owner) + " (owner)");
        foreach (string by in borrowers) homes.Add(_nameOf.GetValueOrDefault(by, "#" + by));

        return $"Shared by {homes.Count} parents: {string.Join(", ", homes)}";
    }

    private string _hovered = "";

    private Node? NodeAt(Point world) =>
        _nodes.Values.LastOrDefault(n => IsDrawnAtCurrentDetail(n.Id) && n.Bounds.Contains(world));

    private (Node Node, int Slot)? PortAt(Point world)
    {
        foreach (var node in _nodes.Values)
            if (IsDrawnAtCurrentDetail(node.Id))
            for (int i = 0; i < node.Slots.Count; i++)
                if (Distance(node.OutPort(i), world) < PortRadius * 2.5)
                    return (node, i);
        return null;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var screen = e.GetPosition(this);
        var world = ToWorld(screen);
        var props = e.GetCurrentPoint(this).Properties;
        _lastPointer = screen;

        if (props.IsRightButtonPressed)
        {
            var hit = NodeAt(world);
            Select(hit?.Id ?? "");
            Selected?.Invoke(SelectedId);
            AddRequested?.Invoke("", "", world);
            InvalidateVisual();
            return;
        }

        if (props.IsMiddleButtonPressed) { _panning = true; return; }



        foreach (var candidate in _nodes.Values)
        {
            if (!HasFamily(candidate) || !ChevronRect(candidate).Contains(screen)) continue;
            ToggleCollapse(candidate.Id, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            return;
        }

        var port = PortAt(world);
        if (port != null) { _wiring = port; _wireTo = screen; return; }

        var node = NodeAt(world);
        if (node != null)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (!_selected.Remove(node.Id)) _selected.Add(node.Id);
            }


            else if (!_selected.Contains(node.Id)) Select(node.Id);

            Selected?.Invoke(SelectedId);


            if (e.ClickCount >= 2) Activated?.Invoke(node.Id);
            else _dragNode = node;
        }
        else
        {


            _selected.Clear();
            _marqueeFrom = world;
            _marquee = new Rect(world, world);
            Selected?.Invoke("");
        }
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var screen = e.GetPosition(this);
        var delta = screen - _lastPointer;
        _lastPointer = screen;

        Hovering(NodeAt(ToWorld(screen)));

        if (_wiring != null) { _wireTo = screen; InvalidateVisual(); return; }

        if (_marqueeFrom is { } from)
        {
            _marquee = new Rect(from, ToWorld(screen));
            InvalidateVisual();
            return;
        }

        if (_dragNode != null)
        {
            Move(_dragNode, delta.X / _zoom, delta.Y / _zoom);
            InvalidateVisual();
            return;
        }

        if (_panning) { _pan += delta; InvalidateVisual(); }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_marqueeFrom != null)
        {
            _marqueeFrom = null;



            foreach (string id in _order)
                if (_nodes.TryGetValue(id, out var node) && IsDrawnAtCurrentDetail(id)
                                                    && node.Bounds.Intersects(_marquee))
                    _selected.Add(id);

            Selected?.Invoke(SelectedId);
            _marquee = default;
            _dragNode = null;
            InvalidateVisual();
            return;
        }

        if (_wiring is { } w)
        {
            var target = NodeAt(ToWorld(e.GetPosition(this)));
            var slot = w.Node.Slots[w.Slot];

            if (target != null && target.Id != w.Node.Id)
            {

                int from = GraphLinks.Accepts(slot.Field), to = GraphLinks.FamilyOf(target.Class);
                if (from == to || GraphLinks.ValidPairs.Contains((from, to)))
                    LinkRequested?.Invoke(w.Node.Id, slot.Field, target.Id);
                else
                    Refused?.Invoke($"a {slot.Field} slot will not take a {target.Class}");
            }
            else if (target == null)
            {
                AddRequested?.Invoke(w.Node.Id, slot.Field, ToWorld(e.GetPosition(this)));
            }
        }

        _wiring = null;
        _dragNode = null;
        _panning = false;
        InvalidateVisual();
    }

    public Action<string>? Refused;

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var before = ToWorld(e.GetPosition(this));
        var detail = CurrentDetail();
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.12 : 1 / 1.12), 0.15, 3.0);
        var after = ToWorld(e.GetPosition(this));
        _pan += (after - before) * _zoom;
        ReflowForDetail(detail);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Delete && SelectedId.Length > 0)
        {







            if (_selected.Count > 1)
                Refused?.Invoke($"{_selected.Count} nodes are selected. Deleting is one at a time, " +
                                "because taking an object out renumbers the ones above it. " +
                                "Click one node, or click empty canvas to clear the selection, " +
                                "then delete.");
            else DeleteRequested?.Invoke(SelectedId);

            e.Handled = true;
        }

        if (e.Key == Key.Escape && _highlight.Length > 0)
        {
            ClearHighlight();
            e.Handled = true;
        }
    }



    public void SetZoom(double zoom)
    {
        var detail = CurrentDetail();
        _zoom = Math.Clamp(zoom, 0.15, 3.0);
        ReflowForDetail(detail);
        InvalidateVisual();
    }







    public void FrameAll()
    {
        var detail = CurrentDetail();
        Frame(_nodes.Values.Where(n => IsDrawnAtCurrentDetail(n.Id)).Select(n => n.Bounds));
        if (_layoutMode == GraphLayoutMode.StructuredFlow && detail != CurrentDetail())
            Frame(_nodes.Values.Where(n => IsDrawnAtCurrentDetail(n.Id)).Select(n => n.Bounds));
    }



    public void FrameRelated()
    {
        if (_highlight.Length == 0) { FrameAll(); return; }

        var of = _related.Count > 0 ? _related : new HashSet<string> { _highlight };
        Frame(of.Where(id => _nodes.ContainsKey(id) && IsDrawnAtCurrentDetail(id)).Select(id => _nodes[id].Bounds));
    }

    private void Frame(IEnumerable<Rect> what)
    {
        var boxes = what.ToList();
        if (boxes.Count == 0 || Bounds.Width < 1 || Bounds.Height < 1) return;
        var detail = CurrentDetail();

        double minX = boxes.Min(b => b.X), minY = boxes.Min(b => b.Y);
        double maxX = boxes.Max(b => b.Right), maxY = boxes.Max(b => b.Bottom);

        const double Margin = 40;
        double wide = Math.Max(1, maxX - minX), tall = Math.Max(1, maxY - minY);





        _zoom = Math.Clamp(Math.Min((Bounds.Width - Margin * 2) / wide,
                                    (Bounds.Height - Margin * 2) / tall), 0.005, 1.5);
        ReflowForDetail(detail);



        _pan = new Point(Bounds.Width / 2 - (minX + wide / 2) * _zoom,
                         Bounds.Height / 2 - (minY + tall / 2) * _zoom);
        InvalidateVisual();
    }

    private void ReflowForDetail(StructuredFlowDetail before)
    {
        if (_layoutMode != GraphLayoutMode.StructuredFlow) return;
        if (before != CurrentDetail() && _model != null) Show(_model);
        else BuildStructuredContainers();
    }
}
