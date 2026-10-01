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

}
