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
        /// The parent that reached this node first, which is the one that decides where it is drawn.
        /// Empty for a walk root. See GraphOwnership for why there is exactly one.
        public string OwnerId = "";
        public string Name = "";
        public string Animation = "";
        public Rect Bounds;
        public List<GraphLinks.Slot> Slots = new();
        public Color Accent;
        public bool Empty;
        public bool Start;
        public bool Active;
        /// Events that enter this state from any state of its machine, which are shown on the node
        /// rather than drawn as lines. See `WildcardRows`.
        public List<string> Wildcards = new();
        public GraphValidator.Level? Problem;
        public Point InPort => new(Bounds.X - PortRadius, Bounds.Y + HeaderHeight / 2);
        public Point OutPort(int index) =>
            new(Bounds.Right + PortRadius, Bounds.Y + HeaderHeight + RowHeight * (index + 0.5) + 2);
    }

    private readonly Dictionary<string, Node> _nodes = new();
    private readonly List<string> _order = new();
    private readonly Dictionary<string, Point> _placed = new();

    /// Which nodes are folded shut, kept across rebuilds so an edit does not silently unfold the
    /// graph, and cleared when a different file is opened.
    ///
    /// A folded node's family is left out of the layout altogether rather than drawn and skipped.
    /// That is what makes the space come back: the contour a subtree reserved is reserved because it
    /// was measured, so a subtree nobody can see has to not be measured.
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);

    /// How many nodes the file has that the canvas is not showing, which is what a folded branch is
    /// holding.
    private int _placedCount;

    /// Which other nodes point at a node without owning it, and what to call them.
    ///
    /// Derived here rather than asked of GraphOwnership, which answers who owns what and has no
    /// business knowing that the answer is about to be drawn. This is the same fact read for a
    /// different purpose: ownership decides where a shared node goes, and this says that the place
    /// it went is only one of its homes.
    ///
    /// Built over every node the file has rather than only the visible ones, so folding the branch a
    /// node is borrowed from does not quietly make it look exclusive.
    private readonly Dictionary<string, List<string>> _sharedBy = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _nameOf = new(StringComparer.Ordinal);

    public IReadOnlyList<string> SharedBy(string id) =>
        _sharedBy.TryGetValue(id, out var by) ? by : Array.Empty<string>();

    public string OwnerOf(string id) => _own.Owner.TryGetValue(id, out string? owner) ? owner : "";

    /// What a node is called on the canvas, or its id when it has no name.
    public string NameOf(string id) => _nameOf.GetValueOrDefault(id, "#" + id);

    private BehaviourGraphModel? _model;

    /// Who owns what, rebuilt with the nodes. Every question about where a node is placed goes
    /// through this rather than being worked out again, and collapsing and dragging will read the
    /// same answers rather than each deciding for themselves.
    private GraphOwnership.Tree _own = GraphOwnership.Of(Array.Empty<(string, string)>());

    /// Which event moves which state to which state. Held apart from the nodes because it is not
    /// drawn from them: a route joins two states that hold no reference to each other, and the
    /// ownership wires the rest of the canvas draws cannot show one.
    private StateRoutes _routes = new();

    /// Off by default. A shipped behaviour draws a few thousand ownership wires already, and routes
    /// laid over all of them at once is a worse picture rather than a fuller one.
    public bool ShowRoutes { get; set; } = true;

    /// A route's label is only worth the space when it can be read. Below this the routes still draw
    /// and only the words are dropped, so the shape survives zooming out and the clutter does not.
    private const double LabelZoom = 0.55;

    /// How many wildcard events a state lists on itself before the rest become a count. A state
    /// enterable on twenty different events is real, and a node twenty rows taller than its
    /// neighbours stops being a node and becomes a wall.
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

    /// Everything picked. A marquee or ctrl click can pick several, and a drag moves all of them.
    public IReadOnlyList<string> SelectedIds => _selected;

    /// The one the properties panel is looking at, which is the first of the selection.
    ///
    /// Kept because most of what asks about the selection wants exactly one node and always did:
    /// which node to hang a new child off, whose paths to highlight, what to describe. Those are not
    /// selection wide operations that were never written, they are single node operations, so they
    /// go on asking for one node and get the primary.
    public string SelectedId => _selected.Count > 0 ? _selected[0] : "";

    /// Replaces the selection with one node, or clears it.
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

    /// The states the stepper says are live right now, drawn as a bright ring.
    ///
    /// Kept apart from the highlight and the fault marks because it answers a different question:
    /// not what you picked, and not what is wrong, but where the graph actually is. A running
    /// character lights several of these at once, since several machines run at the same time, which
    /// is the thing a static picture cannot show and the reason this exists.
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

        // A state's routes are the reason to pick it out in the first place. Ownership alone answers
        // what a state contains; it says nothing about what enters it or what it leads to, which is
        // the question somebody clicking a state is asking.
        foreach (string id in _routes.Touching(_highlight)) _related.Add(id);
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

    /// Read only, for the window checks.
    public bool IsDimmed(string id) => Dimmed(id);

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

        Select(id);
        var centre = node.Bounds.Center;
        _pan = new Point(Bounds.Width / 2 - centre.X * _zoom, Bounds.Height / 2 - centre.Y * _zoom);
        InvalidateVisual();
        return true;
    }

    /// Everything the canvas remembers is keyed by object id, and the next file numbers its objects
    /// from one as well, so carrying any of it across a load applies it to whichever object happens
    /// to hold that number now.
    public void Reset()
    {
        // The canvas is only refilled when a file has a text form to draw from. Without this, opening
        // something that cannot be unpacked left the previous file's graph on screen.
        _model = null;
        _routes = new StateRoutes();
        _nodes.Clear();
        _order.Clear();
        _placed.Clear();
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
        _zoom = 0.9;
        _pan = new Point(40, 40);
    }

    public void Show(BehaviourGraphModel model)
    {
        _model = model;
        _routes = StateRoutes.Of(model);
        _nodes.Clear();
        _order.Clear();

        // A state left holding nothing is drawn like any other until it is marked. Deleting its
        // generator clears the link rather than refusing, so this is a shape an edit can produce and
        // the game never ships.
        var empty = GraphValidator.StatesWithNoGenerator(model);

        // A running offset per column rather than a row number times this node's own height. Nodes
        // are as tall as their slot count, so multiplying by one node's height overlapped every node
        // shorter than it with the one below.
        // The events that reach a state from any state of its machine, gathered per target. A
        // wildcard is not drawn as a line: there is one per state it could fire from, which is a
        // picture nobody can read, and the useful fact is not the path but that this state is
        // enterable from anywhere and on what. That is a property of the state, so it is written on
        // the state.
        var wildcardsInto = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var route in _routes.Routes.Where(r => r.Wildcard))
        {
            if (!wildcardsInto.TryGetValue(route.ToId, out var events))
                wildcardsInto[route.ToId] = events = new List<string>();
            if (!events.Contains(route.Event)) events.Add(route.Event);
        }

        // Measuring is separate from placing, because a family is centred on its parent and that
        // cannot be worked out until every node's height is known. A node is as tall as its slot
        // count, so this is not a constant anyone can assume.
        var placed = GraphAuthor.Layout(model, MaxNodes);
        _own = GraphOwnership.Of(placed);

        _placedCount = placed.Count;

        _sharedBy.Clear();
        _nameOf.Clear();

        foreach (var (obj, _, _) in placed)
        {
            string name = obj.Str("name");
            _nameOf[obj.Id] = name.Length > 0 ? name : "#" + obj.Id;
        }

        // The same enumeration that decided ownership, not the port list. Ownership can be settled
        // through a reference buried in an array element, a transition's blend effect being the
        // usual one, and the ports never carried those. Counting parents off the ports would mark
        // fewer nodes than the walk actually shared, so the mark would be quietly wrong in exactly
        // the places the picture is hardest to read.
        foreach (var (obj, _, _) in placed)
            foreach (string target in GraphAuthor.PointsAt(model, obj))
            {
                if (target == obj.Id) continue;
                if (!_own.Owner.TryGetValue(target, out string? owner) || owner == obj.Id) continue;

                if (!_sharedBy.TryGetValue(target, out var by))
                    _sharedBy[target] = by = new List<string>();
                if (!by.Contains(obj.Id)) by.Add(obj.Id);
            }

        // A folded branch is left out here rather than drawn and skipped, so the contour it would
        // have reserved is never measured and the space it was holding comes back.
        var showing = placed.Where(p => !_own.Hidden(_collapsed, p.Node.Id)).ToList();

        var measured = new List<GraphLayout.Item>();
        var slotsOf = new Dictionary<string, List<GraphLinks.Slot>>(StringComparer.Ordinal);
        var wildcardsOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var heightOf = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var (obj, column, ownerId) in showing)
        {
            // Ports, not edges. This asks what rows the node shows and how tall it is, which is a
            // different question from what it points at: a reference buried in an array element is a
            // real edge and has no port of its own. GraphAuthor.PointsAt answers the other one.
            var slots = GraphLinks.OutSlots(model, obj);
            var wildcards = wildcardsInto.GetValueOrDefault(obj.Id) ?? new List<string>();
            double height = HeaderHeight + Math.Max(1, slots.Count) * RowHeight
                            + Math.Min(wildcards.Count, WildcardRows) * RowHeight + 8;

            slotsOf[obj.Id] = slots;
            wildcardsOf[obj.Id] = wildcards;
            heightOf[obj.Id] = height;
            measured.Add(new GraphLayout.Item(obj.Id, column, ownerId, height));
        }

        // A node the user has dragged stays exactly where they put it across rebuilds, and blocks,
        // so a family is pushed past it rather than over it.
        var pinned = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, at) in _placed)
            if (heightOf.ContainsKey(id)) pinned[id] = at.Y;

        var y = GraphLayout.Place(measured, pinned, RowGap);

        foreach (var (obj, column, ownerId) in showing)
        {
            double x = _placed.TryGetValue(obj.Id, out var kept) ? kept.X : column * ColumnGap;

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
                Wildcards = wildcardsOf[obj.Id],
                Problem = _problems.TryGetValue(obj.Id, out var level) ? level : null,
                Bounds = new Rect(x, y[obj.Id], NodeWidth, heightOf[obj.Id]),
            };
        }

        // The whole selection is pruned, not just the primary. Folding a branch takes its nodes off
        // the canvas, and a selection still naming them would drag things nobody can see.
        _selected.RemoveAll(id => !_nodes.ContainsKey(id));
        if (_highlight.Length > 0 && !_nodes.ContainsKey(_highlight)) _highlight = "";
        RebuildRelated();
        RebuildMatched();
        InvalidateVisual();
    }

    /// How far each ownership wire has to travel down the canvas, in world units.
    ///
    /// This is the number the layout is judged on and it is worth being able to read rather than
    /// squint at. A wire from a parent to a child it owns should be short: the whole complaint about
    /// the old layout was that these ran the height of the canvas, and a picture at a zoom that fits
    /// the graph on screen is too small to tell whether that stopped.
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

    /// What a node owns, restricted to the nodes actually on the canvas.
    public int OwnedCount(string id) => _own.Under(id).Count(_nodes.ContainsKey);

    public IReadOnlyList<string> OwnedIds(string id) => _own.Under(id).Where(_nodes.ContainsKey).ToList();

    /// How many nodes the folded branches are holding, which is the file's node count less what is
    /// on screen. Read straight off the two counts rather than recomputed, so it cannot disagree
    /// with what was drawn.
    public int HiddenCount => Math.Max(0, _placedCount - _nodes.Count);

    public bool IsCollapsed(string id) => _collapsed.Contains(id);

    /// Selection and drag driven directly, for the window checks. Injected clicks are not reliable
    /// enough on this platform to build a regression on, and what is worth testing is what the
    /// movement set does rather than whether a synthetic click landed on a node.
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

    /// What a drag from this node would move, which is the set the delta is applied to once each.
    public IReadOnlyCollection<string> MovementSet(string id)
    {
        var picked = _selected.Contains(id) ? (IEnumerable<string>)_selected : new[] { id };
        return _own.Moving(picked).Where(_nodes.ContainsKey).ToList();
    }

    /// Folds a node shut or open, and lays the graph out again so the space comes back.
    ///
    /// Deep does every node it owns rather than one level: if any of them is open they all shut,
    /// otherwise they all open, so one gesture always has an obvious result rather than toggling
    /// each independently and leaving a half folded branch.
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

    /// Where the chevron sits, in screen space. Left of the title, inside the header.
    private Rect ChevronRect(Node node)
    {
        var at = ToScreen(node.Bounds.TopLeft);
        return new Rect(at.X + 2 * _zoom, at.Y + 3 * _zoom, 13 * _zoom, 13 * _zoom);
    }

    private bool HasFamily(Node node) => _own.Children(node.Id).Count > 0;

    public int DrawnCount => _nodes.Count;
    public IReadOnlyCollection<string> DrawnIds => _nodes.Keys;

    /// Read only, for the window checks. A route whose two ends are not both on the canvas cannot be
    /// drawn, so the count that matters is the drawable one rather than the file's total.
    public int RouteCount => _routes.Routes.Count;
    public int DrawableRouteCount =>
        _routes.Routes.Count(r => _nodes.ContainsKey(r.FromId) && _nodes.ContainsKey(r.ToId));
    public int NestedRouteCount => _routes.Routes.Count(r => r.IntoId.Length > 0);

    /// Read only, for the window checks. How far the laid out graph runs across and down, which is
    /// the measurement behind folding a tall depth into lanes: a graph that is far taller than it is
    /// wide is a strip somebody has to scroll rather than a picture they can look at.
    /// The slab of the graph the viewport is currently showing, in the graph's own units. What a
    /// fit button claims to have done is only checkable against this.
    public Rect VisibleWorld() =>
        new(ToWorld(new Point(0, 0)), ToWorld(new Point(Bounds.Width, Bounds.Height)));

    public (double Wide, double Tall) Extent()
    {
        if (_nodes.Count == 0) return (0, 0);
        return (_nodes.Values.Max(n => n.Bounds.Right) - _nodes.Values.Min(n => n.Bounds.X),
                _nodes.Values.Max(n => n.Bounds.Bottom) - _nodes.Values.Min(n => n.Bounds.Y));
    }

    /// The events written on a node saying it can be entered from any state of its machine.
    public IReadOnlyList<string> WildcardsInto(string id) =>
        _nodes.TryGetValue(id, out var node) ? node.Wildcards : Array.Empty<string>();

    /// How many routes the canvas would draw as lines right now, which is direct transitions plus,
    /// when a state is picked out, that state's share of its machine's wildcards.
    public int LineCount => RoutesToDraw().Count(r => _nodes.ContainsKey(r.FromId) && _nodes.ContainsKey(r.ToId));
    public IReadOnlyCollection<string> StartStateIds => _routes.StartStates;
    public bool IsStart(string id) => _nodes.TryGetValue(id, out var node) && node.Start;

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
        // crossed by them, which is the whole point of asking for one state at a time. With nothing
        // picked out there is nothing to sit on top of, and a second walk of four thousand nodes is
        // not free, so that case runs the lit pass only.
        bool focused = _highlight.Length > 0 || _needle.Length > 0;
        for (int pass = focused ? 0 : 1; pass < 2; pass++)
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

                        DrawLink(ctx, from, node.Accent, 1.6, lit ? 0.9 : 0.42, into,
                                 cased: lit && focused);
                    }

        if (ShowRoutes) DrawRoutes(ctx);

        if (_wiring is { } w)
            DrawLink(ctx, ToScreen(w.Node.OutPort(w.Slot)), Ux.Accent, 2.2, 0.85, _wireTo);

        foreach (var node in _nodes.Values)
        {
            if (!Dimmed(node.Id)) DrawNode(ctx, node);
            else using (ctx.PushOpacity(0.4)) DrawNode(ctx, node);
        }

        DrawMarquee(ctx);
    }

    /// Which routes get drawn, which depends on whether a state has been picked out.
    ///
    /// Every transition in a state machine runs between two of its states. A wildcard is declared on
    /// the machine rather than on a state, but that is where the rule is written down, not where it
    /// fires from: it fires from every state the machine holds, so from any one of them it is a way
    /// out of that state like any other.
    ///
    /// Drawing that literally, a line from every state to the target, is one line per state per
    /// wildcard, and across the vanilla data that is 41,751 lines against 6,394 transitions. So it is
    /// drawn from the state being asked about instead:
    ///
    /// With a state picked out, its machine's wildcards leave that state, and every line on the
    /// canvas runs between two states, which is what the format means.
    ///
    /// With nothing picked out there is no state to draw them from, and the honest anchor is the
    /// machine that declares them. They sit faint there, saying a fan exists without claiming to
    /// come from any particular state.
    private IEnumerable<StateRoutes.Route> RoutesToDraw()
    {
        // Direct transitions only. A wildcard has no single state to leave, so any line drawn for
        // one is either a lie about where it comes from or one line per state of the machine, which
        // across the vanilla data is 41,751 lines against 6,394 transitions. What matters about a
        // wildcard is that this state can be entered from anywhere and on what event, and that is
        // said on the state itself.
        var direct = _routes.Routes.Where(r => !r.Wildcard);

        if (_highlight.Length == 0 || !_routes.MachineOfState.ContainsKey(_highlight))
            return direct;

        // With a state picked out, its machine's wildcards do have a state to leave: this one. Shown
        // then, and only then, because the question being asked is what can happen from here.
        return direct.Concat(_routes.LeavingState(_highlight).Where(r => r.Wildcard));
    }

    /// The routes, over the ownership wires rather than among them.
    ///
    /// A route joins two states side by side in the same column, so it is drawn between the sides of
    /// the nodes rather than between the ports: a port to port curve between neighbours doubles back
    /// on itself and reads as a wire to somewhere else entirely.
    ///
    /// Labels come last and only when picked out or zoomed in. Half of all machines hold two
    /// transitions and nine in ten hold eight, so most graphs can carry every label at once; the one
    /// vanilla machine with 168 of them cannot, and that is what the gating is for.
    private void DrawRoutes(DrawingContext ctx)
    {
        bool focused = _highlight.Length > 0 || _needle.Length > 0;
        var wanted = new List<(string Text, Point At, Color Colour, bool Lit, bool Wildcard)>();

        foreach (var route in RoutesToDraw())
        {
            if (!_nodes.TryGetValue(route.FromId, out var from)) continue;
            if (!_nodes.TryGetValue(route.ToId, out var to)) continue;

            bool lit = Lit(route.FromId, route.ToId);

            // A wildcard fires from any state, so every one of a machine's wildcards leaves the same
            // node and they fan out across the whole graph. Dogmeat's paired animation machine has
            // twenty five, which drew as a sheet of lines over everything else. They stay on screen,
            // because a machine having them is worth seeing, but they sit back until the machine or
            // one of its targets is picked out.
            // Weight stays the same lit or not. What changes is the outline under it, which is what
            // separates a picked out route from the ones it runs alongside without widening it into
            // them.
            double weight = route.Wildcard && !lit ? 0.9 : 1.4;
            double alpha = route.Wildcard ? (lit ? 0.95 : 0.10) : lit ? 1.0 : 0.28;
            bool cased = lit && focused;

            var a = ToScreen(RouteExit(from, to));
            var b = ToScreen(RouteEntry(to, from));
            if (OffScreen(a, b)) continue;

            var colour = route.Wildcard ? Ux.Wildcard : Ux.RouteColour;
            DrawLink(ctx, a, colour, weight, alpha, b, dashed: true, cased: cased);
            DrawArrowHead(ctx, a, b, colour, alpha);

            // The second hop of a nested transition, which enters a state and picks a state inside
            // it at the same time. Drawn from the state entered to the state chosen within it, so
            // the pair reads as one route with two ends rather than as two unrelated ones.
            if (route.IntoId.Length > 0 && _nodes.TryGetValue(route.IntoId, out var into))
            {
                var c = ToScreen(RouteExit(to, into));
                var d = ToScreen(RouteEntry(into, to));
                DrawLink(ctx, c, colour, weight * 0.8, alpha * 0.8, d, dashed: true, cased: cased);
                DrawArrowHead(ctx, c, d, colour, alpha * 0.8);
            }

            if (!lit || _zoom < LabelZoom) continue;

            // A wildcard's name only appears once something is picked out. Unlit, they are the bulk
            // of the labels and none of them is the one being looked for.
            if (route.Wildcard && !focused) continue;

            wanted.Add((route.Wildcard ? "any: " + route.Event : route.Event,
                        new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2), colour, lit, route.Wildcard));
        }

        DrawLabels(ctx, wanted);
    }

    /// The labels that fit, rather than all of them.
    ///
    /// Routes converge, so their midpoints do too, and drawing every label put text over text in
    /// exactly the busy places where reading one matters most. A label that would land on one
    /// already drawn is dropped instead: the route is still there to follow, and the alternative is
    /// a pile that names neither of them.
    ///
    /// Ordered so the ones worth keeping are placed first. Anything picked out beats anything not,
    /// and a plain route beats a wildcard, whose name is the same at every one of its ends.
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

    // The side of the node a route should leave from, chosen by where the other end is. A route to
    // something above or below leaves the top or the bottom, which is the common case: a machine's
    // states are laid out in one column.
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

    /// A wire between two points. `to` sits after the alpha so the ownership calls that have always
    /// passed three numbers keep reading the way they did.
    ///
    /// Dashed is what tells a route from a wire. They mean different things, a route being an event
    /// the game sends and a wire being one object holding another, and drawing both as solid curves
    /// in different colours left the two reading as one kind of thing.
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
                // A route joins states that usually sit one above the other, where a horizontal bend
                // would loop out sideways and back. Bending along the run keeps it between its ends.
                var lift = new Vector((to.X - from.X) * 0.3, (to.Y - from.Y) * 0.15);
                g.CubicBezierTo(from + lift, to - lift, to);
            }
            else
            {
                g.CubicBezierTo(from + new Vector(bend, 0), to - new Vector(bend, 0), to);
            }
            g.EndFigure(false);
        }

        // An outline under the line rather than a thicker line on top of it.
        //
        // Picking a route out used to mean drawing it fatter, which fails exactly where it is needed:
        // routes converge, and two fat lines running together read as one fat line. A casing gives
        // each its own edge, so lines crossing or running side by side stay countable without any of
        // them taking more room. Drawn in the opposite of the canvas, which is the one colour
        // guaranteed to separate from both the background and every wire on it.
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

    /// The selection box, drawn over everything so it reads as a thing being dragged now rather than
    /// part of the picture.
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

        // A live state gets a bright halo outside its border, brighter and wider than a fault's, so a
        // running graph reads at a glance and lights up as events move it. Drawn even when the node is
        // also faulted, since where the graph is and what is wrong with it are both worth seeing.
        if (node.Active)
            for (int ring = 4; ring >= 1; ring--)
                ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Ux.RouteColour, 0.16 * ring), ring * 2 + 2),
                                  r.Inflate(ring * 2.0), 6, 6);

        var borderColour = node.Active ? Ux.RouteColour : fault ?? node.Accent;
        var edge = new Pen(new SolidColorBrush(borderColour), node.Active ? 3 : fault != null ? 2.5 : selected ? 2 : 1);
        ctx.DrawRectangle(body, edge, r, 4, 4);

        // A second outline one step in, so a node drawn in only one of its homes reads as doubled at
        // a glance. No icon and no header space, so it survives zooming out and does not compete
        // with the wildcard rows.
        //
        // Dimmer again when the node is selected. Which node you picked matters more in the moment
        // than which nodes are borrowed, so a selected shared node has to read as selected first.
        if (_sharedBy.ContainsKey(node.Id))
            ctx.DrawRectangle(null,
                new Pen(new SolidColorBrush(borderColour, selected ? 0.28 : 0.45), 1),
                r.Deflate(3), 3, 3);
        ctx.DrawRectangle(new SolidColorBrush(borderColour, node.Active ? 0.30 : fault != null ? 0.22 : 0.35), null,
            new Rect(r.X, r.Y, r.Width, HeaderHeight * _zoom), 4, 4);

        double scale = _zoom;
        var faultBrush = fault is { } f ? new SolidColorBrush(f) : null;
        string title = node.Name.Length > 0 ? node.Name : node.Class;

        // The title starts clear of the chevron on a node that has one, so folding does not cover
        // the name of the thing being folded.
        bool family = HasFamily(node);
        double titleAt = family ? 18 : 6;

        Draw(ctx, title, r.X + titleAt * scale, r.Y + 4 * scale, 11 * scale,
             faultBrush ?? Ux.TitleBrush, r.Width - (titleAt + 6) * scale);

        if (family)
        {
            bool shut = _collapsed.Contains(node.Id);
            var chevron = ChevronRect(node);
            Draw(ctx, shut ? ">" : "v", chevron.X + 2 * scale, chevron.Y - 1 * scale, 11 * scale,
                 shut ? new SolidColorBrush(node.Accent) : Ux.MutedBrush, chevron.Width);

            // What this fold is holding, which is what clicking it again brings back. Owned
            // descendants only and only those not already held by a fold further up, so the number
            // is a promise rather than a subtree size.
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

        // The state its machine starts in. A machine's states are otherwise identical on the canvas
        // and which one the graph begins in cannot be read off the picture at all: it is a number on
        // the machine, matched against a number on the state, neither of which is drawn.
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

    /// The events that enter this state from any state of its machine, listed on the state.
    ///
    /// This is the wildcard, and it is deliberately not a line. A wildcard fires from every state
    /// the machine holds, so there is no single place for a line to start: drawing one from the
    /// machine says something the format does not, and drawing one from each state is 41,751 lines
    /// across the vanilla data against 6,394 transitions. Neither is readable and neither is the
    /// question. The question is whether this state can be entered from anywhere and on what, which
    /// is a fact about this state, so it is written here.
    private void DrawWildcards(DrawingContext ctx, Node node, Rect r, double scale)
    {
        if (node.Wildcards.Count == 0) return;

        double top = r.Y + (HeaderHeight + RowHeight * Math.Max(1, node.Slots.Count) + 10) * scale;
        int shown = Math.Min(node.Wildcards.Count, WildcardRows);

        for (int i = 0; i < shown; i++)
        {
            // The last row carries the remainder rather than being dropped, so a state with twenty
            // ways in never looks like a state with four.
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

    /// Moves a node, everything picked with it, and everything each of those owns.
    ///
    /// The set is a set because the two overlap all the time. Select a parent and one of its own
    /// children and the child is reached twice, once in its own right and once through its owner,
    /// and applying the delta twice would send it off at double speed and out of its family.
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

    /// Names the parents that borrow the node under the pointer.
    ///
    /// Which ones, not only how many: "shared" on its own leaves somebody hunting the canvas for the
    /// other end. The owner is named too and named first, because the node is sitting where the
    /// owner put it and that is the part the picture is otherwise silent about.
    ///
    /// Only touched when the node under the pointer changes. Setting a tip on every mouse move is a
    /// layout pass per pixel.
    private void Hovering(Node? node)
    {
        string over = node?.Id ?? "";
        if (over == _hovered) return;
        _hovered = over;

        string tip = SharedTip(over);
        ToolTip.SetTip(this, tip.Length > 0 ? tip : null);
    }

    /// What hovering a shared node says, or nothing when the node has one parent.
    ///
    /// The order is fixed rather than whatever an enumeration happens to give: the owner first,
    /// because the node is sitting where the owner put it and that is the part the picture is
    /// otherwise silent about, then the borrowers in the order the walk met them. Nothing here reads
    /// a dictionary in its own order, so the same file gives the same sentence every time.
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
            Select(hit?.Id ?? "");
            Selected?.Invoke(SelectedId);
            AddRequested?.Invoke("", "", world);
            InvalidateVisual();
            return;
        }

        if (props.IsMiddleButtonPressed) { _panning = true; return; }

        // The chevron is checked before selection, so folding a node does not also select it and
        // throw away a selection somebody built up.
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
            // Dragging something outside the selection takes just that node, rather than carrying a
            // selection the user has visibly moved on from.
            else if (!_selected.Contains(node.Id)) Select(node.Id);

            Selected?.Invoke(SelectedId);
            // A second click opens the fields rather than starting a drag, so the node does not
            // shift by a pixel on the way to editing it.
            if (e.ClickCount >= 2) Activated?.Invoke(node.Id);
            else _dragNode = node;
        }
        else
        {
            // Left drag on empty canvas draws a marquee. Panning stays on the middle button, which
            // already worked and is where it lives in every tool of this kind.
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

            // Intersecting rather than wholly inside, so a node hanging off the edge of the view can
            // be caught without zooming out far enough to fit all of it in the box.
            foreach (string id in _order)
                if (_nodes.TryGetValue(id, out var node) && node.Bounds.Intersects(_marquee))
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
            // One at a time on purpose. Deleting an object renumbers every id above it, which is the
            // hazard #19 is about, so a second delete in the same breath would be aimed at whatever
            // moved into the number it remembered. Refused out loud rather than deleting one of
            // twelve and leaving the rest, which is the surprising version of the same limit.
            //
            // The message says what to do as well as why. A refusal that only explains itself leaves
            // somebody stuck holding a selection with no idea what to change about it.
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

    /// Read only, for the headless renderer: a picture has to be taken at a chosen zoom rather than
    /// at whatever the last interaction left behind.
    public void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.15, 3.0);
        InvalidateVisual();
    }

    /// Fit the whole graph in the viewport.
    ///
    /// This used to set a fixed zoom of 0.7 and move the corner into view, which is not fitting
    /// anything: Dogmeat's default behaviour lays out 8,890 by 5,589, so at 0.7 the button put you
    /// in the top left corner of something seven screens across and said it had framed it. The zoom
    /// is worked out from what there is to show.
    public void FrameAll() => Frame(_nodes.Values.Select(n => n.Bounds));

    /// Fit one node and everything it is joined to, which is what somebody asking about a machine
    /// wants: that machine and its states filling the view instead of being a tenth of it.
    public void FrameRelated()
    {
        if (_highlight.Length == 0) { FrameAll(); return; }

        var of = _related.Count > 0 ? _related : new HashSet<string> { _highlight };
        Frame(of.Where(_nodes.ContainsKey).Select(id => _nodes[id].Bounds));
    }

    private void Frame(IEnumerable<Rect> what)
    {
        var boxes = what.ToList();
        if (boxes.Count == 0 || Bounds.Width < 1 || Bounds.Height < 1) return;

        double minX = boxes.Min(b => b.X), minY = boxes.Min(b => b.Y);
        double maxX = boxes.Max(b => b.Right), maxY = boxes.Max(b => b.Bottom);

        const double Margin = 40;
        double wide = Math.Max(1, maxX - minX), tall = Math.Max(1, maxY - minY);

        // The floor is there so the button cannot produce a zoom of zero, not to stop it fitting.
        // It was 0.02, which was enough while the graph was 5,589 tall and not once the layout
        // started placing families beside their parents: on a short viewport the clamp won and "fit
        // all" framed something taller than the view and said it had framed it.
        _zoom = Math.Clamp(Math.Min((Bounds.Width - Margin * 2) / wide,
                                    (Bounds.Height - Margin * 2) / tall), 0.005, 1.5);

        // Centred rather than corner aligned. A graph wider than it is tall leaves a band of empty
        // canvas otherwise, and the thing being looked at sits against one edge of it.
        _pan = new Point(Bounds.Width / 2 - (minX + wide / 2) * _zoom,
                         Bounds.Height / 2 - (minY + tall / 2) * _zoom);
        InvalidateVisual();
    }
}
