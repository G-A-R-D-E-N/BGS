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

public partial class GraphView
{
    private bool Move(Node from, double byX, double byY)
    {
        if (byX == 0 && byY == 0) return false;
        var picked = _selected.Contains(from.Id) ? (IEnumerable<string>)_selected : new[] { from.Id };
        bool moved = false;

        foreach (string id in _own.Moving(picked))
        {
            if (!_nodes.TryGetValue(id, out var node)) continue;
            node.Bounds = node.Bounds.WithX(node.Bounds.X + byX).WithY(node.Bounds.Y + byY);
            _placed[id] = node.Bounds.TopLeft;
            moved = true;
        }
        return moved;
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
        _dragChanged = false;
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
            if (Move(_dragNode, delta.X / _zoom, delta.Y / _zoom)) _dragChanged = true;
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
        if (_dragChanged && _layoutMode == GraphLayoutMode.Freeform) LayoutChanged?.Invoke();
        _dragChanged = false;
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
