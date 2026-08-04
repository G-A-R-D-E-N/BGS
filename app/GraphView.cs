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

    private sealed class Node
    {
        public string Id = "";
        public string Class = "";
        public string Name = "";
        public Rect Bounds;
        public List<GraphLinks.Slot> Slots = new();
        public Color Accent;
        public bool Empty;
        public Point InPort => new(Bounds.X - PortRadius, Bounds.Y + HeaderHeight / 2);
        public Point OutPort(int index) =>
            new(Bounds.Right + PortRadius, Bounds.Y + HeaderHeight + RowHeight * (index + 0.5) + 2);
    }

    private readonly Dictionary<string, Node> _nodes = new();
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
    public Action<string, string, string>? LinkRequested;
    public Action<string, string, string>? UnlinkRequested;
    public Action<string>? DeleteRequested;
    public Action<string, Point>? AddRequested;

    public GraphView()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public void Show(BehaviourGraphModel model)
    {
        _model = model;
        _nodes.Clear();

        // A state left holding nothing is drawn like any other until it is marked. Deleting its
        // generator clears the link rather than refusing, so this is a shape an edit can produce and
        // the game never ships.
        var empty = GraphValidator.StatesWithNoGenerator(model);

        var byColumn = new Dictionary<int, int>();
        foreach (var (obj, column) in GraphAuthor.Layout(model, 400))
        {
            byColumn.TryGetValue(column, out int row);
            byColumn[column] = row + 1;

            var slots = GraphLinks.OutSlots(model, obj);
            double height = HeaderHeight + Math.Max(1, slots.Count) * RowHeight + 8;

            // A node the user has dragged stays where they put it across rebuilds.
            Point at = _placed.TryGetValue(obj.Id, out var kept)
                ? kept
                : new Point(column * ColumnGap, row * (height + RowGap));

            _nodes[obj.Id] = new Node
            {
                Id = obj.Id,
                Class = obj.Class,
                Name = obj.Str("name"),
                Slots = slots,
                Accent = Ux.ForClass(obj.Class),
                Empty = empty.Contains(obj.Id),
                Bounds = new Rect(at.X, at.Y, NodeWidth, height),
            };
        }

        if (SelectedId.Length > 0 && !_nodes.ContainsKey(SelectedId)) SelectedId = "";
        InvalidateVisual();
    }

    public int DrawnCount => _nodes.Count;

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

        foreach (var node in _nodes.Values)
            for (int i = 0; i < node.Slots.Count; i++)
                foreach (string target in node.Slots[i].Targets)
                    if (_nodes.TryGetValue(target, out var to))
                        DrawLink(ctx, ToScreen(node.OutPort(i)), ToScreen(to.InPort), node.Accent, 1.6);

        if (_wiring is { } w)
            DrawLink(ctx, ToScreen(w.Node.OutPort(w.Slot)), _wireTo, Ux.Accent, 2.2);

        foreach (var node in _nodes.Values) DrawNode(ctx, node);
    }

    private void DrawLink(DrawingContext ctx, Point from, Point to, Color colour, double width)
    {
        double bend = Math.Max(40, Math.Abs(to.X - from.X) * 0.45);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(from, false);
            g.CubicBezierTo(from + new Vector(bend, 0), to - new Vector(bend, 0), to);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(colour, 0.85), width), geometry);
    }

    private void DrawNode(DrawingContext ctx, Node node)
    {
        var r = new Rect(ToScreen(node.Bounds.TopLeft), new Size(node.Bounds.Width * _zoom, node.Bounds.Height * _zoom));
        if (!r.Intersects(new Rect(Bounds.Size))) return;

        bool selected = node.Id == SelectedId;
        var body = new SolidColorBrush(selected ? Ux.CardHover : Ux.Card);
        var edge = new Pen(new SolidColorBrush(node.Accent), selected ? 2 : 1);

        ctx.DrawRectangle(body, edge, r, 4, 4);
        ctx.DrawRectangle(new SolidColorBrush(node.Accent, 0.35), null,
            new Rect(r.X, r.Y, r.Width, HeaderHeight * _zoom), 4, 4);

        double scale = _zoom;
        string title = node.Name.Length > 0 ? node.Name : node.Class;
        Draw(ctx, title, r.X + 6 * scale, r.Y + 4 * scale, 11 * scale,
             node.Empty ? Ux.BadBrush : Ux.TitleBrush, r.Width - 12 * scale);
        Draw(ctx, node.Empty ? node.Class + "  no generator" : node.Class,
             r.X + 6 * scale, r.Y + (HeaderHeight + 1) * scale, 9 * scale,
             node.Empty ? Ux.BadBrush : new SolidColorBrush(node.Accent), r.Width - 12 * scale);

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
            AddRequested?.Invoke(SelectedId, world);
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
            _dragNode = node;
            Selected?.Invoke(node.Id);
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
                AddRequested?.Invoke(w.Node.Id + "" + slot.Field, ToWorld(e.GetPosition(this)));
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
