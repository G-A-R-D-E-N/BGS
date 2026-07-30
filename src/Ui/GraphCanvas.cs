
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio;

// Draws the link being dragged while the add menu is open. It sits above the GraphEdit and ignores
// the mouse, because GraphEdit stops drawing its own preview the moment the button is released.
public partial class LinkPreview : Control
{
    public bool Active;
    public Vector2 From;
    public Vector2 To;

    public override void _Draw()
    {
        if (!Active) return;

        // Same shape as GraphEdit's own connections, so the held line does not look like a
        // different kind of thing from the ones already on the canvas.
        var points = new Vector2[24];
        Vector2 c1 = From + new Vector2((To.X - From.X) * 0.5f, 0);
        Vector2 c2 = To - new Vector2((To.X - From.X) * 0.5f, 0);
        for (int i = 0; i < points.Length; i++)
        {
            double t = i / (double)(points.Length - 1), u = 1 - t;
            points[i] = From * (u * u * u) + c1 * (3 * u * u * t) + c2 * (3 * u * t * t) + To * (t * t * t);
        }

        DrawPolyline(points, Ux.Accent, 2.0f, true);
        DrawCircle(To, 5.0f, Ux.Accent);
    }
}

public partial class GraphCanvas : Control
{
    private const int MaxNodes = 250;
    private const int ColumnWidth = 420;
    private const int RowHeight = 46;
    private const int RowGap = 34;

    private static readonly string[] LinkFields =
    {
        "rootGenerator", "generator", "generators", "states", "transitions", "wildcardTransitions",
        "children", "layers", "modifier", "modifiers", "transition", "condition", "eventPayload",
        "pDefaultGenerator", "pBlenderGenerator", "variableBindingSet", "triggers", "blendSpeed",
        "selectedGeneratorIndex", "startStateIdSelector", "syncVariableIndex", "boneWeights",
    };

    private GraphEdit _graph = null!;
    private Label _notice = null!;
    private readonly Dictionary<string, string> _nodeToId = new();

    public Action<string>? ObjectSelected;
    public Action<string, string, string>? FieldEdited;
    public Action<string, string, string, string>? NodeAdded;
    public Action<string>? NodeDeleted;
    public Action<string, string, string>? LinkRequested;
    public Action<string, string, string>? UnlinkRequested;

    private readonly Dictionary<string, List<GraphLinks.Slot>> _outFields = new();
    private readonly Dictionary<string, Vector2> _positions = new();
    private PanelContainer _menu = null!;
    private VBoxContainer _menuItems = null!;
    private LineEdit _menuFilter = null!;
    private LinkPreview _preview = null!;
    private List<(string Label, Action Run)> _menuAll = new();
    private string _pendingNode = "";
    private int _pendingPort = -1;

    public string SelectedId { get; private set; } = "";

    private LineEdit _newName = null!;
    private LineEdit _newAnimation = null!;
    private Label _parentLabel = null!;

    private BehaviourGraphModel? _model;
    private List<string> _variableNames = new();

    private static readonly string[] AlwaysShow =
    {
        "mode", "playbackSpeed", "userControlledTimeFraction", "cropStartAmountLocalTime",
        "cropEndAmountLocalTime", "startTime", "enable", "weight", "duration", "selectedGeneratorIndex",
        "startStateId", "stateId", "eventId", "toStateId",
    };

    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        var column = new VBoxContainer { AnchorRight = 1, AnchorBottom = 1 };
        column.AddThemeConstantOverride("separation", Ux.Px(6));
        AddChild(column);

        column.AddChild(BuildToolbar());

        _notice = Ux.StatusPill("No file loaded.");
        column.AddChild(_notice);

        _graph = new GraphEdit
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            ShowGrid = true,
            MinimapEnabled = false,
            RightDisconnects = true,
        };
        _graph.AddThemeStyleboxOverride("panel", Ux.Fill(Ux.Base, Ux.Border, 1, 4));
        _graph.NodeSelected += OnNodeSelected;
        _graph.NodeDeselected += _ => SetSelection("");
        _graph.ConnectionRequest += OnConnectionRequest;
        _graph.DisconnectionRequest += OnDisconnectionRequest;
        _graph.ConnectionToEmpty += OnConnectionToEmpty;
        _graph.GuiInput += OnGraphInput;
        _graph.EndNodeMove += RememberPositions;
        column.AddChild(_graph);

        BuildMenu();
    }

    // A plain panel rather than a PopupMenu. Every Popup derived control is backed by its own OS
    // window that hides itself on focus loss, which makes it unusable over a canvas the user is
    // still dragging on.
    private void BuildMenu()
    {
        _preview = new LinkPreview { MouseFilter = MouseFilterEnum.Ignore };
        _preview.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_preview);

        _menu = new PanelContainer { Visible = false, ZIndex = 100 };
        _menu.AddThemeStyleboxOverride("panel", Ux.Fill(Ux.Card, Ux.Accent, 1, 4));
        _menu.CustomMinimumSize = new Vector2(Ux.Px(240), 0);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", Ux.Px(4));
        _menu.AddChild(box);

        _menuFilter = Ux.Field("type to filter");
        _menuFilter.TextChanged += _ => FillMenu();
        _menuFilter.TextSubmitted += _ => RunFirstMatch();
        box.AddChild(_menuFilter);

        _menuItems = new VBoxContainer();
        _menuItems.AddThemeConstantOverride("separation", Ux.Px(2));
        box.AddChild(_menuItems);

        AddChild(_menu);
    }

    private void RememberPositions()
    {
        foreach (var node in _graph.GetChildren().OfType<GraphNode>())
            if (_nodeToId.TryGetValue(node.Name, out string id))
                _positions[id] = node.PositionOffset;
    }

    private void HideMenu()
    {
        _menu.Visible = false;
        _preview.Active = false;
        _preview.QueueRedraw();
        _pendingNode = "";
        _pendingPort = -1;
    }

    // The line from the port the drag started at stays on screen while the menu is open and while
    // the filter is being typed into, the way it does in a blueprint editor. It is anchored to the
    // release point rather than following the cursor, so it does not chase the menu.
    public override void _Process(double delta)
    {
        if (!_preview.Active || _pendingPort < 0) return;

        var node = _graph.GetNodeOrNull<GraphNode>(_pendingNode);
        if (node == null) { HideMenu(); return; }

        Vector2 port = node.GetOutputPortPosition(_pendingPort) * _graph.Zoom;
        Vector2 inGraph = node.PositionOffset * _graph.Zoom - _graph.ScrollOffset + port;
        _preview.From = inGraph + _graph.GlobalPosition - GlobalPosition;
        _preview.QueueRedraw();
    }

    private void ShowMenu(string heading, List<(string Label, Action Run)> items)
    {
        _menuAll = items;
        _menuFilter.Text = "";

        foreach (var child in _menuItems.GetChildren())
        {
            _menuItems.RemoveChild(child);
            child.QueueFree();
        }

        var title = Ux.FieldLabel(heading);
        title.AddThemeColorOverride("font_color", Ux.TextMuted);
        _menuItems.AddChild(title);

        FillMenu();
        _menu.Position = GetLocalMousePosition();
        _menu.Visible = true;
        _menuFilter.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void FillMenu()
    {
        foreach (var child in _menuItems.GetChildren().Skip(1))
        {
            _menuItems.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var (label, run) in Matching())
        {
            var button = Ux.SecondaryButton(label);
            button.Alignment = HorizontalAlignment.Left;
            var captured = run;
            button.Pressed += () => { HideMenu(); captured(); };
            _menuItems.AddChild(button);
        }
    }

    private IEnumerable<(string Label, Action Run)> Matching()
    {
        string needle = _menuFilter.Text.Trim();
        return needle.Length == 0
            ? _menuAll
            : _menuAll.Where(i => i.Label.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private void RunFirstMatch()
    {
        var first = Matching().FirstOrDefault();
        if (first.Run == null) return;
        HideMenu();
        first.Run();
    }

    private void ShowCanvasMenu(string attachToId, string attachField)
    {
        var items = new List<(string, Action)>();
        string heading = attachToId.Length > 0
            ? $"add and connect to #{attachToId}.{attachField}"
            : "add a node";

        foreach (string kind in GraphAuthor.Kinds)
        {
            string captured = kind;
            items.Add(("New " + captured, () =>
            {
                string name = _newName.Text.Trim();
                if (name.Length == 0) name = captured + "_new";
                NodeAdded?.Invoke(captured, name, _newAnimation.Text.Trim(), attachToId);
            }));
        }

        ShowMenu(heading, items);
    }

    private void ShowNodeMenu(string objectId)
    {
        var obj = _model?.Get(objectId);
        string label = obj == null ? "#" + objectId : $"#{objectId} {obj.Class}";
        var items = new List<(string, Action)>();

        foreach (string kind in GraphAuthor.Kinds)
        {
            string captured = kind;
            items.Add(($"Add {captured} here", () =>
            {
                string name = _newName.Text.Trim();
                if (name.Length == 0) name = captured + "_new";
                NodeAdded?.Invoke(captured, name, _newAnimation.Text.Trim(), objectId);
            }));
        }

        if (obj != null)
            foreach (var slot in GraphLinks.OutSlots(_model!, obj).Where(s => s.Targets.Count > 0))
            {
                var captured = slot;
                items.Add(($"Clear {captured.Field}", () =>
                {
                    foreach (string target in captured.Targets.ToList())
                        UnlinkRequested?.Invoke(objectId, captured.Field, target);
                }));
            }

        items.Add(("Delete this node", () => NodeDeleted?.Invoke(objectId)));
        ShowMenu(label, items);
    }

    // A row of buttons rather than a dropdown: five kinds is not enough to justify a popup, and a
    // popup is one more thing that can steal focus away from the canvas.
    private HBoxContainer BuildToolbar()
    {
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", Ux.Px(6));

        _newName = Ux.Field("new node name");
        _newName.CustomMinimumSize = new Vector2(Ux.Px(150), 0);
        bar.AddChild(_newName);

        _newAnimation = Ux.Field("animation, for a clip");
        _newAnimation.CustomMinimumSize = new Vector2(Ux.Px(170), 0);
        bar.AddChild(_newAnimation);

        foreach (string kind in GraphAuthor.Kinds)
        {
            var button = Ux.SecondaryButton("+ " + kind);
            string captured = kind;
            button.Pressed += () => RequestAdd(captured);
            bar.AddChild(button);
        }

        var remove = Ux.SecondaryButton("Delete");
        remove.Pressed += () => NodeDeleted?.Invoke(SelectedId);
        bar.AddChild(remove);

        _parentLabel = Ux.FieldLabel("nothing selected, a new node will be left unattached");
        _parentLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bar.AddChild(_parentLabel);

        return bar;
    }

    private void RequestAdd(string kind)
    {
        string name = _newName.Text.Trim();
        if (name.Length == 0) name = kind + "_new";
        NodeAdded?.Invoke(kind, name, _newAnimation.Text.Trim(), SelectedId);
    }

    private void SetSelection(string id)
    {
        SelectedId = id;
        if (_parentLabel == null) return;

        if (id.Length == 0)
        {
            _parentLabel.Text = "nothing selected, a new node will be left unattached";
            return;
        }

        string cls = _model?.Get(id)?.Class ?? "";
        string slot = GraphAuthor.AttachmentFor(cls);
        _parentLabel.Text = slot.Length > 0
            ? $"#{id} {cls}: a new node becomes {slot}"
            : $"#{id} {cls} has no generator slot, a new node will be left unattached";
    }

    public void Clear()
    {
        foreach (var child in _graph.GetChildren().OfType<GraphNode>().ToList())
        {
            _graph.RemoveChild(child);
            child.QueueFree();
        }
        _graph.ClearConnections();
        _nodeToId.Clear();
        _outFields.Clear();
        if (_menu != null) HideMenu();
    }

    public void ShowMessage(string text)
    {
        Clear();
        _notice.Text = text;
        _notice.AddThemeColorOverride("font_color", Ux.TextMuted);
    }

    public void Build(BehaviourGraphModel model)
    {
        Clear();
        _model = model;
        _variableNames = model.Objects
            .FirstOrDefault(o => o.Class == "hkbBehaviorGraphStringData")?.Strings("variableNames")
            ?? new List<string>();

        var root = model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraph")
                   ?? model.Objects.FirstOrDefault(o => o.Class == "hkbStateMachine")
                   ?? model.Objects.FirstOrDefault();

        if (root == null)
        {
            ShowMessage("Nothing to draw: no root object in this file.");
            return;
        }

        var edges = new Dictionary<string, List<(string Field, string Target)>>();
        var depth = new Dictionary<string, int> { [root.Id] = 0 };
        var order = new List<HkObject> { root };
        var queue = new Queue<HkObject>();
        queue.Enqueue(root);

        while (queue.Count > 0 && order.Count < MaxNodes)
        {
            var current = queue.Dequeue();
            var outgoing = new List<(string, string)>();

            foreach (var field in LinkFields)
            {
                foreach (string target in TargetsOf(current, field))
                {
                    if (model.Get(target) == null) continue;
                    outgoing.Add((field, target));
                    if (depth.ContainsKey(target)) continue;
                    depth[target] = depth[current.Id] + 1;
                    var next = model.Get(target)!;
                    order.Add(next);
                    queue.Enqueue(next);
                }
            }

            edges[current.Id] = outgoing;
        }

        // The walk above only reaches nodes something points at, so a node that was just created has
        // nothing pointing at it and would be invisible: the tool would look like it did nothing.
        // They go in a column past the deepest real one.
        int orphanColumn = depth.Values.DefaultIfEmpty(0).Max() + 1;
        foreach (var orphan in GraphAuthor.Unattached(model))
        {
            if (order.Count >= MaxNodes) break;
            if (depth.ContainsKey(orphan.Id)) continue;
            depth[orphan.Id] = orphanColumn;
            order.Add(orphan);
            edges[orphan.Id] = new List<(string, string)>();
        }

        int drawn = 0;
        var byDepth = new Dictionary<int, int>();

        foreach (var obj in order)
        {
            var node = MakeNode(obj, edges.TryGetValue(obj.Id, out var outs) ? outs : new List<(string, string)>());
            int d = depth[obj.Id];
            byDepth.TryGetValue(d, out int row);
            byDepth[d] = row + 1;

            // A node the user has dragged keeps where they put it. Without this every edit
            // reshuffles the whole canvas back to the computed layout and loses their arrangement.
            node.PositionOffset = _positions.TryGetValue(obj.Id, out var placed)
                ? placed
                : new Vector2(d * ColumnWidth, row * (RowHeight + RowGap) * 2);
            _graph.AddChild(node);
            _nodeToId[node.Name] = obj.Id;
            drawn++;
        }

        foreach (var obj in order)
        {
            string fromName = NodeName(obj.Id);
            if (!_outFields.TryGetValue(fromName, out var fields)) continue;

            for (int port = 0; port < fields.Count; port++)
            {
                foreach (string target in fields[port].Targets)
                {
                    string toName = NodeName(target);
                    if (_graph.GetNodeOrNull<GraphNode>(toName) == null) continue;
                    _graph.ConnectNode(fromName, port, toName, 0);
                }
            }
        }

        string capped = order.Count >= MaxNodes
            ? $"  (stopped at {MaxNodes} nodes, this graph is larger)"
            : "";
        int orphans = depth.Count(kv => kv.Value == orphanColumn);
        string unattached = orphans > 0 ? $",  {orphans} unattached in the last column" : "";

        _notice.Text = $"{drawn} nodes, {edges.Values.Sum(e => e.Count)} links, " +
                       $"{orphanColumn} levels deep{unattached}{capped}";
        _notice.AddThemeColorOverride("font_color", Ux.TextTitle);

        SetSelection(_nodeToId.ContainsValue(SelectedId) ? SelectedId : "");
    }

    private static IEnumerable<string> TargetsOf(HkObject obj, string field)
    {
        string scalar = obj.Str(field);
        if (scalar.StartsWith('#')) yield return scalar[1..];

        foreach (string item in obj.Refs(field)) yield return item;

        if (obj.StructLists.TryGetValue(field, out var rows))
            foreach (var row in rows)
                foreach (var kv in row)
                    if (kv.Value.StartsWith('#'))
                        yield return kv.Value[1..];
    }

    private static string NodeName(string id) => "obj_" + id;

    private IEnumerable<string> BindingsOf(HkObject obj)
    {
        var set = _model?.Follow(obj, "variableBindingSet");
        if (set == null || !set.StructLists.TryGetValue("bindings", out var rows)) yield break;

        foreach (var row in rows)
        {
            row.TryGetValue("memberPath", out string? path);
            row.TryGetValue("variableIndex", out string? index);
            row.TryGetValue("bindingType", out string? kind);

            string variable = index != null && int.TryParse(index, out int i)
                              && i >= 0 && i < _variableNames.Count
                ? _variableNames[i]
                : $"index {index}";

            string scope = kind != null && kind.Contains("CHARACTER_PROPERTY") ? " (character property)" : "";
            yield return $"{path} driven by {variable}{scope}";
        }
    }

    private HBoxContainer EditRow(string objectId, string field, string value)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", Ux.Px(6));

        var label = new Label { Text = field };
        label.AddThemeColorOverride("font_color", Ux.TextMeta);
        label.AddThemeFontSizeOverride("font_size", Ux.Px(11));
        box.AddChild(label);

        var edit = Ux.Field();
        edit.Text = value;
        edit.CustomMinimumSize = new Vector2(Ux.Px(110), 0);
        edit.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        string original = value;
        void Commit()
        {
            if (edit.Text == original) return;
            FieldEdited?.Invoke(objectId, field, edit.Text);
            original = edit.Text;
        }
        edit.TextSubmitted += _ => Commit();
        edit.FocusExited += Commit;
        box.AddChild(edit);

        return box;
    }

    private GraphNode MakeNode(HkObject obj, List<(string Field, string Target)> outs)
    {
        string label = obj.Str("name");
        if (string.IsNullOrEmpty(label)) label = obj.Class;

        var node = new GraphNode
        {
            Name = NodeName(obj.Id),
            Title = label,
            Draggable = true,
            CustomMinimumSize = new Vector2(Ux.Px(300), 0),
        };

        var accent = ColourFor(obj.Class);
        node.AddThemeStyleboxOverride("panel", Ux.Fill(Ux.Card, Ux.Border, 1, 4));
        node.AddThemeStyleboxOverride("titlebar", Ux.Fill(accent.Darkened(0.55f), accent, 1, 4));

        // GraphNode swaps to panel_selected/titlebar_selected on selection; without these the
        // default theme's translucent styles show through instead of the node body.
        node.AddThemeStyleboxOverride("panel_selected", Ux.Fill(Ux.CardHover, accent, 2, 4));
        node.AddThemeStyleboxOverride("panel_focus", Ux.Fill(Ux.CardHover, accent, 2, 4));
        node.AddThemeStyleboxOverride("titlebar_selected", Ux.Fill(accent.Darkened(0.3f), accent, 2, 4));

        var header = new Label { Text = obj.Class };
        header.AddThemeColorOverride("font_color", accent);
        node.AddChild(header);
        node.SetSlot(0, true, 0, accent, false, 0, accent);

        string animation = obj.Str("animationName");
        if (!string.IsNullOrEmpty(animation))
        {
            var anim = new Label { Text = animation };
            anim.AddThemeColorOverride("font_color", Ux.TextCode);
            node.AddChild(anim);
        }

        foreach (var binding in BindingsOf(obj))
        {
            var row = new Label { Text = binding };
            row.AddThemeColorOverride("font_color", Color.FromHtml("D29922"));
            node.AddChild(row);
        }

        foreach (string field in AlwaysShow)
        {
            if (!obj.Scalars.TryGetValue(field, out string? value)) continue;
            if (value.StartsWith('#')) continue;
            node.AddChild(EditRow(obj.Id, field, value));
        }

        // A port per link the class is allowed to have, not per link it already has, so an empty
        // generator field can still be dragged from. The port index Godot reports is the position
        // among enabled right hand slots, which is why the fields are recorded in the same order.
        var fields = _model == null ? new List<GraphLinks.Slot>() : GraphLinks.OutSlots(_model, obj);
        _outFields[node.Name] = fields;

        int slot = node.GetChildCount();
        foreach (var link in fields)
        {
            string filled = link.Targets.Count switch
            {
                0 => "",
                1 => "  " + (_model?.Get(link.Targets[0])?.Str("name") ?? "#" + link.Targets[0]),
                _ => $"  {link.Targets.Count} linked",
            };

            var row = new Label
            {
                Text = (link.Array ? link.Field + " []" : link.Field) + filled,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            row.AddThemeColorOverride("font_color", link.Targets.Count > 0 ? Ux.TextMeta : Ux.TextDisabled);
            node.AddChild(row);
            node.SetSlot(slot, false, 0, accent, true, 0, accent);
            slot++;
        }

        node.GuiInput += e => OnNodeInput(e, obj.Id);
        return node;
    }

    private void OnNodeInput(InputEvent e, string objectId)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) return;
        SetSelection(objectId);
        ObjectSelected?.Invoke(objectId);
        ShowNodeMenu(objectId);
    }

    private void OnGraphInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true } click) return;
        if (click.ButtonIndex == MouseButton.Right) ShowCanvasMenu("", "");
        else HideMenu();
    }

    // Dropping a dragged link on empty canvas is the blueprint gesture for "make me something to
    // connect this to", so the menu that opens attaches whatever it creates to that port.
    private void OnConnectionToEmpty(StringName fromNode, long fromPort, Vector2 releasePosition)
    {
        string from = fromNode.ToString();
        if (!_nodeToId.TryGetValue(from, out string id)) return;
        if (!_outFields.TryGetValue(from, out var fields) || fromPort >= fields.Count) return;

        _pendingNode = from;
        _pendingPort = (int)fromPort;
        _preview.To = releasePosition + _graph.GlobalPosition - GlobalPosition;
        _preview.Active = true;

        ShowCanvasMenu(id, fields[(int)fromPort].Field);
    }

    private void OnConnectionRequest(StringName fromNode, long fromPort, StringName toNode, long toPort)
    {
        string from = fromNode.ToString(), to = toNode.ToString();
        if (!_nodeToId.TryGetValue(from, out string fromId)) return;
        if (!_nodeToId.TryGetValue(to, out string toId)) return;
        if (!_outFields.TryGetValue(from, out var fields) || fromPort >= fields.Count) return;

        LinkRequested?.Invoke(fromId, fields[(int)fromPort].Field, toId);
    }

    private void OnDisconnectionRequest(StringName fromNode, long fromPort, StringName toNode, long toPort)
    {
        string from = fromNode.ToString(), to = toNode.ToString();
        if (!_nodeToId.TryGetValue(from, out string fromId)) return;
        if (!_nodeToId.TryGetValue(to, out string toId)) return;
        if (!_outFields.TryGetValue(from, out var fields) || fromPort >= fields.Count) return;

        UnlinkRequested?.Invoke(fromId, fields[(int)fromPort].Field, toId);
    }

    private static Color ColourFor(string cls)
    {
        if (cls.Contains("StateMachine")) return Ux.Accent;
        if (cls.Contains("ClipGenerator")) return Color.FromHtml("3FB950");
        if (cls.Contains("Blender") || cls.Contains("Layer") || cls.Contains("Selector")) return Color.FromHtml("D29922");
        if (cls.Contains("Transition")) return Color.FromHtml("A371F7");
        if (cls.Contains("Modifier")) return Color.FromHtml("DB6D28");
        return Ux.TextMeta;
    }

    private void OnNodeSelected(Node node)
    {
        if (node is not GraphNode g || !_nodeToId.TryGetValue(g.Name, out string id)) return;
        SetSelection(id);
        ObjectSelected?.Invoke(id);
    }
}

