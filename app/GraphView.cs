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

// The node canvas, drawn directly rather than built from a toolkit's graph widget. Everything it
// knows about the file comes from the same GraphAuthor and GraphLinks the headless tools use, so
// what it draws and what an edit does cannot drift apart.
public class GraphView : Control
{
    private const double NodeWidth = 250;
    private const double HeaderHeight = 22;
    private const double RowHeight = 15;
    private const double ColumnGap = 320;
    private const double RowGap = 26;
    private const double PortRadius = 5;
    // A weapon behaviour lays out just under 4000 nodes. Drawing 400 of them meant the search could
    // not find a node that is in the file, because it was never on the canvas to find.
    private const int MaxNodes = 4000;

    private sealed class Node
    {
        public string Id = "";
        public string Class = "";
        public string Name = "";
        public string Animation = "";
        public Rect Bounds;
        public List<GraphLinks.Slot> Slots = new();
        public Color Accent;
        public bool Empty;
        public GraphValidator.Level? Problem;
        public Point InPort => new(Bounds.X - PortRadius, Bounds.Y + HeaderHeight / 2);
        public Point OutPort(int index) =>
            new(Bounds.Right + PortRadius, Bounds.Y + HeaderHeight + RowHeight * (index + 0.5) + 2);
    }

    private readonly Dictionary<string, Node> _nodes = new();
    private readonly List<string> _order = new();
    private readonly Dictionary<string, Point> _placed = new();
    private BehaviourGraphModel? _model;

    private double _zoom = 0.9;
    private Point _pan = new(40, 40);
    private Point _lastPointer;
    private bool _panning;
    private Node? _dragNode;
    private (Node Node, int Slot)? _wiring;
    private Point _wireTo;

    public string SelectedId { get; private set; } = "";

    public Action<string>? Selected;
    public Action<string>? Activated;
    public Action<string, string, string>? LinkRequested;
    public Action<string, string, string>? UnlinkRequested;
    public Action<string>? DeleteRequested;
    /// The node and slot the drag came from, empty for a right click, and the point on the canvas
    /// the new node should land on.
    public Action<string, string, Point>? AddRequested;

    public GraphView()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// What the last check found, kept across rebuilds so an edit does not silently clear the marks
    /// while the list beside the canvas still shows them.
    private Dictionary<string, GraphValidator.Level> _problems = new();

    public void Mark(Dictionary<string, GraphValidator.Level> problems)
    {
        _problems = problems;
        foreach (var node in _nodes.Values)
            node.Problem = problems.TryGetValue(node.Id, out var level) ? level : null;
        InvalidateVisual();
    }

    /// One state at a time. A shipped graph draws a few hundred wires over each other and reading a
    /// single state's routes off that is the thing the canvas is worst at, so everything not touching
    /// the chosen node is dimmed rather than hidden: the shape of the rest stays visible as context.
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
    }

    /// The filter box, applied to the canvas rather than only to the tree. Non-matching nodes dim
    /// instead of disappearing, because a node's place in the graph is most of what it tells you and
    /// a filtered canvas with holes in it says nothing about where the match sits.
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

    private bool Dimmed(string id) =>
        (_highlight.Length > 0 && !_related.Contains(id))
        || (_needle.Length > 0 && !_matched.Contains(id));

    // A wire touching a match stays lit, because where a match connects is the question being asked.
    private bool Lit(string fromId, string toId)
    {
        if (_highlight.Length > 0 && fromId != _highlight && toId != _highlight) return false;
        if (_needle.Length > 0 && !_matched.Contains(fromId) && !_matched.Contains(toId)) return false;
        return true;
    }

    /// Select a node and bring it under the viewport centre, which is the whole point of clicking a
    /// row in the problem list: the node is usually off screen when it is the one that is wrong.
    public bool FocusOn(string id)
    {
        if (!_nodes.TryGetValue(id, out var node)) return false;

        SelectedId = id;
        var centre = node.Bounds.Center;
        _pan = new Point(Bounds.Width / 2 - centre.X * _zoom, Bounds.Height / 2 - centre.Y * _zoom);
        InvalidateVisual();
        return true;
    }

    public void Show(BehaviourGraphModel model)
    {
        _model = model;
        _nodes.Clear();
        _order.Clear();

        // A state left holding nothing is drawn like any other until it is marked. Deleting its
        // generator clears the link rather than refusing, so this is a shape an edit can produce and
        // the game never ships.
        var empty = GraphValidator.StatesWithNoGenerator(model);

        // A running offset per column rather than a row number times this node's own height. Nodes
        // are as tall as their slot count, so multiplying by one node's height overlapped every node
        // shorter than it with the one below.
        var nextY = new Dictionary<int, double>();
        foreach (var (obj, column) in GraphAuthor.Layout(model, MaxNodes))
        {
            var slots = GraphLinks.OutSlots(model, obj);
            double height = HeaderHeight + Math.Max(1, slots.Count) * RowHeight + 8;

            // A node the user has dragged stays where they put it across rebuilds.
            nextY.TryGetValue(column, out double y);
            Point at;
            if (_placed.TryGetValue(obj.Id, out var kept))
            {
                at = kept;
            }
            else
            {
                at = new Point(column * ColumnGap, y);
                nextY[column] = y + height + RowGap;
            }

            _order.Add(obj.Id);
            _nodes[obj.Id] = new Node
            {
                Id = obj.Id,
                Class = obj.Class,
                Name = obj.Str("name"),
                Animation = obj.Str("animationName"),
                Slots = slots,
                Accent = Ux.ForClass(obj.Class),
                Empty = empty.Contains(obj.Id),
                Problem = _problems.TryGetValue(obj.Id, out var level) ? level : null,
                Bounds = new Rect(at.X, at.Y, NodeWidth, height),
            };
        }

        if (SelectedId.Length > 0 && !_nodes.ContainsKey(SelectedId)) SelectedId = "";
        if (_highlight.Length > 0 && !_nodes.ContainsKey(_highlight)) _highlight = "";
        RebuildRelated();
        RebuildMatched();
        InvalidateVisual();
    }

    public int DrawnCount => _nodes.Count;
    public IReadOnlyCollection<string> DrawnIds => _nodes.Keys;

    public Point? PositionOf(string id) => _nodes.TryGetValue(id, out var node) ? node.Bounds.TopLeft : null;

    /// Pins a node to a point before the canvas is rebuilt. A new node is otherwise laid out by its
    /// depth from the root, which puts it in a column of its own at the far end of the graph rather
    /// than under the cursor that asked for it.
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

        // Two passes so the highlighted wires sit on top of the dimmed ones rather than being
        // crossed by them, which is the whole point of asking for one state at a time.
        for (int pass = 0; pass < 2; pass++)
            foreach (var node in _nodes.Values)
                for (int i = 0; i < node.Slots.Count; i++)
                    foreach (string target in node.Slots[i].Targets)
                    {
                        if (!_nodes.TryGetValue(target, out var to)) continue;
                        bool lit = Lit(node.Id, target);
                        if (lit != (pass == 1)) continue;

                        var from = ToScreen(node.OutPort(i));
                        var into = ToScreen(to.InPort);
                        if (OffScreen(from, into)) continue;

                        DrawLink(ctx, from, into, node.Accent,
                                 lit && (_highlight.Length > 0 || _needle.Length > 0) ? 2.6 : 1.6,
                                 lit ? 0.85 : 0.42);
                    }

        if (_wiring is { } w)
            DrawLink(ctx, ToScreen(w.Node.OutPort(w.Slot)), _wireTo, Ux.Accent, 2.2, 0.85);

        foreach (var node in _nodes.Values)
        {
            if (!Dimmed(node.Id)) DrawNode(ctx, node);
            else using (ctx.PushOpacity(0.4)) DrawNode(ctx, node);
        }
    }

    // A weapon graph holds a few thousand wires and only a handful are on screen. The curve stays
    // inside its endpoints' box widened by the bend, so a box test is enough to drop the rest.
    private bool OffScreen(Point from, Point to)
    {
        double margin = Math.Max(40, Math.Abs(to.X - from.X) * 0.45) + 10;
        return Math.Max(from.X, to.X) + margin < 0
            || Math.Min(from.X, to.X) - margin > Bounds.Width
            || Math.Max(from.Y, to.Y) < 0
            || Math.Min(from.Y, to.Y) > Bounds.Height;
    }

    private void DrawLink(DrawingContext ctx, Point from, Point to, Color colour, double width, double alpha)
    {
        double bend = Math.Max(40, Math.Abs(to.X - from.X) * 0.45);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(from, false);
            g.CubicBezierTo(from + new Vector(bend, 0), to - new Vector(bend, 0), to);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(colour, alpha), width), geometry);
    }

    private void DrawNode(DrawingContext ctx, Node node)
    {
        var r = new Rect(ToScreen(node.Bounds.TopLeft), new Size(node.Bounds.Width * _zoom, node.Bounds.Height * _zoom));
        if (!r.Intersects(new Rect(Bounds.Size))) return;

        bool selected = node.Id == SelectedId;
        var body = new SolidColorBrush(selected ? Ux.CardHover : Ux.Card);

        // A node the check faulted is outlined in its level's colour rather than its class colour,
        // and gets a soft halo outside the border so it is findable while zoomed out, where a one
        // pixel edge is a pixel.
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

        var edge = new Pen(new SolidColorBrush(fault ?? node.Accent), fault != null ? 2.5 : selected ? 2 : 1);
        ctx.DrawRectangle(body, edge, r, 4, 4);
        ctx.DrawRectangle(new SolidColorBrush(fault ?? node.Accent, fault != null ? 0.22 : 0.35), null,
            new Rect(r.X, r.Y, r.Width, HeaderHeight * _zoom), 4, 4);

        double scale = _zoom;
        var faultBrush = fault is { } f ? new SolidColorBrush(f) : null;
        string title = node.Name.Length > 0 ? node.Name : node.Class;
        Draw(ctx, title, r.X + 6 * scale, r.Y + 4 * scale, 11 * scale,
             faultBrush ?? Ux.TitleBrush, r.Width - 12 * scale);
        Draw(ctx, node.Empty ? node.Class + "  nothing to play" : node.Class,
             r.X + 6 * scale, r.Y + (HeaderHeight + 1) * scale, 9 * scale,
             faultBrush ?? new SolidColorBrush(node.Accent), r.Width - 12 * scale);

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
    }

    private static void Draw(DrawingContext ctx, string text, double x, double y, double size,
                             IBrush brush, double maxWidth, bool rightAlign = false)
    {
        if (size < 4) return;
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                          Typeface.Default, size, brush) { MaxTextWidth = Math.Max(10, maxWidth) };
        ctx.DrawText(formatted, new Point(rightAlign ? x + maxWidth - formatted.Width : x, y));
    }

    private Node? NodeAt(Point world) =>
        _nodes.Values.LastOrDefault(n => n.Bounds.Contains(world));

    private (Node Node, int Slot)? PortAt(Point world)
    {
        foreach (var node in _nodes.Values)
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
            SelectedId = hit?.Id ?? "";
            Selected?.Invoke(SelectedId);
            AddRequested?.Invoke("", "", world);
            InvalidateVisual();
            return;
        }

        if (props.IsMiddleButtonPressed) { _panning = true; return; }

        var port = PortAt(world);
        if (port != null) { _wiring = port; _wireTo = screen; return; }

        var node = NodeAt(world);
        if (node != null)
        {
            SelectedId = node.Id;
            Selected?.Invoke(node.Id);
            // A second click opens the fields rather than starting a drag, so the node does not
            // shift by a pixel on the way to editing it.
            if (e.ClickCount >= 2) Activated?.Invoke(node.Id);
            else _dragNode = node;
        }
        else
        {
            SelectedId = "";
            _panning = true;
            Selected?.Invoke("");
        }
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var screen = e.GetPosition(this);
        var delta = screen - _lastPointer;
        _lastPointer = screen;

        if (_wiring != null) { _wireTo = screen; InvalidateVisual(); return; }

        if (_dragNode != null)
        {
            _dragNode.Bounds = _dragNode.Bounds.WithX(_dragNode.Bounds.X + delta.X / _zoom)
                                               .WithY(_dragNode.Bounds.Y + delta.Y / _zoom);
            _placed[_dragNode.Id] = _dragNode.Bounds.TopLeft;
            InvalidateVisual();
            return;
        }

        if (_panning) { _pan += delta; InvalidateVisual(); }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_wiring is { } w)
        {
            var target = NodeAt(ToWorld(e.GetPosition(this)));
            var slot = w.Node.Slots[w.Slot];

            if (target != null && target.Id != w.Node.Id)
            {
                // Refuse the pairing here rather than writing it and reporting it afterwards.
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
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.12 : 1 / 1.12), 0.15, 3.0);
        var after = ToWorld(e.GetPosition(this));
        _pan += (after - before) * _zoom;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Delete && SelectedId.Length > 0)
        {
            DeleteRequested?.Invoke(SelectedId);
            e.Handled = true;
        }

        if (e.Key == Key.Escape && _highlight.Length > 0)
        {
            ClearHighlight();
            e.Handled = true;
        }
    }

    public void FrameAll()
    {
        if (_nodes.Count == 0) return;
        double minX = _nodes.Values.Min(n => n.Bounds.X), minY = _nodes.Values.Min(n => n.Bounds.Y);
        _zoom = 0.7;
        _pan = new Point(40 - minX * _zoom, 40 - minY * _zoom);
        InvalidateVisual();
    }
}
