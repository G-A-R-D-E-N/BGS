using System.Globalization;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.App;

public sealed record EditorControlInfo(string Id, string Window, string Kind, string Label,
    string Value, bool Enabled, string[] Actions, string[] Items, double Width, double Height, int ItemCount);
public sealed record EditorState(string Status, string Snapshot, int Total, EditorControlInfo[] Controls);
public sealed record EditorActionPreview(bool Accepted, bool RequiresApproval, string Status,
    string Code, string Message);

public sealed class AssistantEditor
{
    private readonly MainWindow _owner;
    private readonly Dictionary<Control, string> _ids = new();
    private int _nextId;
    private Dictionary<string, Control> _observed = new();
    private string _snapshot = "";
    private string _observedState = "";
    private readonly Dictionary<Control, Window> _windows = new();
    private Dictionary<Control, string> _observedScenes = new();
    private (Action Apply, Func<string> CurrentState, string State, string Description)? _pending;
    public string PendingId { get; private set; } = "";

    public AssistantEditor(MainWindow owner) => _owner = owner;
    public bool HasPending => _pending is not null;
    public string PendingDescription => _pending?.Description ?? "";
    public void Reject() { _pending = null; PendingId = ""; }

    public Task<EditorState> State(int offset = 0, int limit = 100, string query = "", int itemOffset = 0) => OnUi(() =>
    {
        var controls = Controls().ToArray();
        foreach (var closed in _ids.Keys.Except(controls).ToArray()) _ids.Remove(closed);
        var matching = controls.Where(control => query.Length == 0 ||
            Label(control).Contains(query, StringComparison.OrdinalIgnoreCase) ||
            control.GetType().Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        var page = matching.Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 200)).ToArray();
        _observed = page.ToDictionary(Id);
        _observedScenes = page.Where(control => control is GraphView or SkeletonView).ToDictionary(control => control, SceneStamp);
        _observedState = Fingerprint();
        _snapshot = Guid.NewGuid().ToString("N");
        return new EditorState("ok", _snapshot, matching.Length,
            page.Select(control => Describe(control, Math.Max(0, itemOffset))).ToArray());
    });

    public Task<EditorActionPreview> Preview(string snapshot, string controlId, string action,
        string value = "", double x = 0, double y = 0, double endX = 0, double endY = 0,
        string button = "Left", string modifiers = "None") => OnUi(() =>
    {
        Reject();
        if (snapshot != _snapshot || !_observed.TryGetValue(controlId, out var control) ||
            _observedState != Fingerprint())
            return Refuse("stale_snapshot", "Read editor_state again; the editor changed.");
        if (!control.IsEffectivelyVisible || !Enabled(control))
            return Refuse("unavailable", "This control is hidden or disabled.");
        try
        {
            Action apply = Operation(control, action, value, x, y, endX, endY, button, modifiers);
            bool gesture = action is "pointer" or "hover" or "drag" or "wheel";
            if (gesture && _observedScenes.GetValueOrDefault(control) != SceneStamp(control))
                return Refuse("stale_snapshot", "The scene moved; pause playback/simulation and read editor_state again.");
            string CurrentState() => Fingerprint() + (gesture ? SceneStamp(control) : "");
            string description = $"{WindowOf(control).Title}: {action} {Label(control)}" +
                (value.Length == 0 ? "" : $" = {value}") +
                (action is "pointer" or "hover" or "drag" or "wheel"
                    ? $" at ({x:R}, {y:R}) to ({endX:R}, {endY:R}); {button}; modifiers {modifiers}" :
                    action == "key" ? $"; modifiers {modifiers}" : "");
            _pending = (apply, CurrentState, CurrentState(), description);
            PendingId = Guid.NewGuid().ToString("N");
            return new EditorActionPreview(true, true, "approval_required", "ok", description);
        }
        catch (ArgumentException error) { return Refuse("invalid_argument", error.Message); }
    });

    public Task<EditorActionPreview> File(string snapshot, string windowId, string operation, string path) => OnUi(() =>
    {
        Reject();
        if (snapshot != _snapshot || _observedState != Fingerprint() ||
            !_observed.TryGetValue(windowId, out var control) || control is not Window target)
            return Refuse("stale_snapshot", "Read editor_state again and use a Window control ID.");
        bool rig = target is RigAuthoringWindow;
        if (!Enabled(target) || !target.IsVisible)
            return Refuse("unavailable", "The target window is disabled or closed.");
        if (rig ? operation is not ("skeleton" or "skin" or "ragdoll") : target != _owner ||
            operation is not ("open" or "mesh" or "compare" or "archive" or "scripts" or "game_data" or "mods" or "export_diff_text" or "export_diff_json"))
            return Refuse("invalid_argument", "The file operation is unavailable in this window.");
        try
        {
            path = Path.GetFullPath(path);
            bool folder = operation is "scripts" or "game_data" or "mods";
            bool export = operation.StartsWith("export_diff_", StringComparison.Ordinal);
            if (export && !_owner.AssistantExportPathAllowed(path))
                return Refuse("path_not_allowed", "Assistant exports must stay inside the active project folder.");
            string extension = operation switch
            {
                "skin" or "mesh" => ".nif", "archive" => ".ba2",
                "export_diff_text" => ".txt", "export_diff_json" => ".json", _ => ".hkx",
            };
            if (folder ? !Directory.Exists(path) : export ? !Directory.Exists(Path.GetDirectoryName(path)) : !System.IO.File.Exists(path))
                return Refuse("path_not_found", "The file or destination folder does not exist.");
            if (!folder && !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) &&
                !(operation == "open" && path.EndsWith(".hkt", StringComparison.OrdinalIgnoreCase)))
                return Refuse("invalid_argument", "The path must have the expected asset/export extension.");
            string description = $"{target.Title}: {operation} {path}" + (export && System.IO.File.Exists(path) ? " (overwrite existing file)" : "");
            _pending = (() => _owner.AssistantFile(target, operation, path), Fingerprint, Fingerprint(), description);
            PendingId = Guid.NewGuid().ToString("N");
            return new EditorActionPreview(true, true, "approval_required", "ok", description);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { return Refuse("invalid_argument", "The path is invalid."); }
    });

    public ClipAnimationChangeResult Approve()
    {
        Dispatcher.UIThread.VerifyAccess();
        var pending = _pending;
        Reject();
        if (pending is null) return Result(false, "no_pending_approval", "There is no pending editor action.");
        if (pending.Value.State != pending.Value.CurrentState())
            return Result(false, "stale_approval", "The editor changed; request the action again.");
        try
        {
            pending.Value.Apply();
            return Result(true, "dispatched", "Sent to the editor. Read editor_state for the result; no asset edit or save is implied.");
        }
        catch (Exception error) { return Result(false, "action_failed", error.Message); }
    }

    private ClipAnimationChangeResult Result(bool issued, string code, string message) =>
        new(issued, false, issued ? "dispatched" : "error", code, message,
            _owner.AssistantContextSnapshot.DocumentId, 0, _owner.IsDirty, 0, "", "", "", null);

    private IEnumerable<Window> Windows(Window root)
    {
        if (root is AssistantSettingsWindow or AssistantApprovalWindow) yield break;
        foreach (var child in root.OwnedWindows)
            foreach (var window in Windows(child)) yield return window;
        yield return root;
    }

    internal static Window? ActiveDialog(Window owner) => owner.OwnedWindows
        .Where(window => window.IsVisible && window is not AssistantSettingsWindow and not AssistantApprovalWindow)
        .Select(window => ActiveDialog(window) ?? (window.IsDialog ? window : null))
        .LastOrDefault(window => window is not null);

    private bool Enabled(Control control)
    {
        if (!control.IsEffectivelyEnabled) return false;
        Window? modal = ActiveDialog(_owner);
        if (modal is null) return true;
        for (WindowBase? window = WindowOf(control); window is Window current; window = current.Owner)
            if (current == modal) return true;
        return false;
    }

    private IEnumerable<Control> Controls()
    {
        _windows.Clear();
        foreach (var window in Windows(_owner))
        {
            window.UpdateLayout();
            var descendants = new Control[] { window }.Concat(window.GetVisualDescendants().OfType<Control>()).ToArray();
            var menus = descendants.Select(control => control.ContextMenu).OfType<ContextMenu>().Where(menu => menu.IsOpen);
            var popups = window.GetLogicalDescendants().OfType<Popup>().Where(popup => popup.IsOpen && popup.Child is not null);
            var floating = menus.Cast<Control>().Concat(popups.Select(popup => popup.Child!));
            foreach (var control in descendants.Concat(floating.SelectMany(root =>
                new[] { root }.Concat(root.GetVisualDescendants().OfType<Control>()))).Distinct())
            {
                if (!control.IsEffectivelyVisible || Excluded(control)) continue;
                _windows[control] = window;
                if (Actions(control).Length > 0 || control is TextBlock or TextBox or Button) yield return control;
            }
        }
    }

    private static bool Excluded(Control control) =>
        control.GetVisualAncestors().Prepend(control).Any(parent =>
            parent is AssistantPane or AssistantConversationSidebar or AssistantSettingsWindow or AssistantApprovalWindow or
                TextBox { PasswordChar: not '\0' }) ||
        control.GetLogicalAncestors().Any(parent => parent is AssistantPane or AssistantConversationSidebar or AssistantSettingsWindow) ||
        control is Button button &&
            (button.Content?.ToString() is EditorShell.SettingsGlyph or EditorShell.ChatGlyph);

    private string Id(Control control)
    {
        if (!_ids.TryGetValue(control, out string? id)) _ids[control] = id = "c" + _nextId++;
        return id;
    }

    private Window WindowOf(Control control) => _windows[control];
    private static string Label(Control control) => AutomationProperties.GetName(control) ?? control.Name ??
        (control switch
        {
            Window window => window.Title,
            TextBox text => text.PlaceholderText,
            MenuItem menu => Text(menu.Header),
            TreeViewItem row => Text(row.Header),
            HeaderedContentControl header => Text(header.Header),
            ContentControl content => Text(content.Content),
            TextBlock text => text.Text,
            _ => null,
        }) ?? control.GetType().Name;

    private static string Text(object? value) => value switch
    {
        TextBlock text => text.Text ?? "",
        Control control => string.Join(" ", control.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)),
        _ => value?.ToString() ?? "",
    };

    private static string[] Actions(Control control) => control switch
    {
        Button { Content: "Browse..." or "From archive..." or "Mesh..." or "Compare with..." or
            "Scripts folder..." or "Export text..." or "Export JSON..." or "Open skeleton HKX" or
            "Open skinned NIF" or "Open ragdoll HKX" } => Array.Empty<string>(),
        Window => new[] { "close", "key" },
        TextBox { IsReadOnly: false } => new[] { "text", "key", "focus" },
        ToggleButton => new[] { "check" },
        Button or MenuItem => new[] { "click" },
        TabControl or ComboBox or ListBox or TreeView => new[] { "select" },
        TreeViewItem => new[] { "expand", "select_item" },
        Expander => new[] { "expand" },
        RangeBase => new[] { "number" },
        NumericUpDown => new[] { "number" },
        ScrollViewer => new[] { "scroll" },
        GraphView or SkeletonView => new[] { "pointer", "hover", "drag", "wheel", "key", "focus" },
        _ => Array.Empty<string>(),
    };

    private EditorControlInfo Describe(Control control, int itemOffset) => new(Id(control), WindowOf(control).Title ?? "BGS",
        control.GetType().Name, Label(control), Value(control), Enabled(control),
        Actions(control), control is GraphView graph ? graph.AssistantItems(itemOffset) :
            control is SkeletonView skeleton ? skeleton.AssistantItems(itemOffset) :
            control is ItemsControl items ? items.Items.Cast<object>().Skip(itemOffset).Take(200).Select(ItemText).ToArray() :
            Array.Empty<string>(), control.Bounds.Width, control.Bounds.Height,
            control is ItemsControl list ? list.Items.Count : control is GraphView nodes ? nodes.AssistantItemCount :
                control is SkeletonView pose ? pose.AssistantItemCount : 0);

    private static string ItemText(object item) => item switch
    {
        TreeViewItem row => Text(row.Header),
        MenuItem menu => Text(menu.Header),
        HeaderedContentControl header => Text(header.Header),
        _ => Text(item),
    };
    private static string Value(Control control) => control switch
    {
        TextBox text => text.Text ?? "",
        TextBlock text => text.Text ?? "",
        ToggleButton toggle => toggle.IsChecked?.ToString() ?? "null",
        SelectingItemsControl select => select.SelectedIndex.ToString(CultureInfo.InvariantCulture),
        TreeView tree => ItemText(tree.SelectedItem ?? ""),
        TreeViewItem item => item.IsExpanded.ToString(),
        Expander expander => expander.IsExpanded.ToString(),
        RangeBase range => range.Value.ToString(CultureInfo.InvariantCulture),
        NumericUpDown numeric => numeric.Value?.ToString(CultureInfo.InvariantCulture) ?? "",
        GraphView graph => $"Selected {graph.SelectedId}; visible world {graph.VisibleWorld()}",
        SkeletonView skeleton => $"{skeleton.DrawnBones} bones; {skeleton.DrawnBodies} bodies; {skeleton.DrawnConstraints} constraints; {skeleton.FrameDistanceText}; {skeleton.AssistantCameraStamp}",
        _ => "",
    };

    private string Fingerprint() => JsonSerializer.Serialize(new
    {
        Context = _owner.AssistantContextSnapshot,
        Controls = Controls().Where(control => control is not TextBlock).Select(control => new
        {
            Id = Id(control), Label = Label(control), Value = Value(control), Enabled = Enabled(control),
            Actions = Actions(control),
            Scene = control is GraphView graph ? graph.AssistantInteractionStamp : "",
            Items = control is ItemsControl items ? items.Items.Cast<object>().Select(ItemText).ToArray() : null,
        }).ToArray(),
    });

    private Action Operation(Control control, string action, string value, double x, double y,
        double endX, double endY, string button, string modifiers)
    {
        if (!Actions(control).Contains(action)) throw new ArgumentException("This control does not support that action.");
        switch (action)
        {
            case "click": return () =>
            {
                control.Focus();
                if (control is Button b) ((IInvokeProvider)new ButtonAutomationPeer(b)).Invoke();
                else
                {
                    control.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    control.GetVisualAncestors().OfType<ContextMenu>().FirstOrDefault()?.Close();
                }
            };
            case "text":
                if (value.Length > 16000) throw new ArgumentException("Text is limited to 16000 characters.");
                return () => ((TextBox)control).Text = value;
            case "check":
                if (!bool.TryParse(value, out bool check)) throw new ArgumentException("Use true or false.");
                return () => ((ToggleButton)control).IsChecked = check;
            case "select":
                var items = ((ItemsControl)control).Items;
                if (!int.TryParse(value, out int index) || index < 0 || index >= items.Count)
                    throw new ArgumentException("Use an existing zero-based item index.");
                return () =>
                {
                    if (control is TreeView tree) tree.SelectedItem = items[index];
                    else ((SelectingItemsControl)control).SelectedIndex = index;
                };
            case "expand":
                if (!bool.TryParse(value, out bool expand)) throw new ArgumentException("Use true or false.");
                return () => { if (control is TreeViewItem item) item.IsExpanded = expand; else ((Expander)control).IsExpanded = expand; };
            case "select_item": return () =>
                control.GetVisualAncestors().OfType<TreeView>().First().SelectedItem = control;
            case "number":
                if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number))
                    throw new ArgumentException("Use a finite number.");
                if (control is RangeBase range && (number < (decimal)range.Minimum || number > (decimal)range.Maximum) ||
                    control is NumericUpDown numeric && (number < numeric.Minimum || number > numeric.Maximum))
                    throw new ArgumentException("The number is outside the control's limits.");
                return () => { if (control is RangeBase range) range.Value = (double)number; else ((NumericUpDown)control).Value = number; };
            case "scroll":
                if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0) throw new ArgumentException("Use finite positive offsets.");
                return () => ((ScrollViewer)control).Offset = new Vector(x, y);
            case "focus": return () => control.Focus();
            case "close": return () => ((Window)control).Close();
            case "key":
                if (!Enum.TryParse(value, out Key key) || !Enum.IsDefined(key) || key == Key.None ||
                    !Enum.TryParse(modifiers, out KeyModifiers keys) || !ValidModifiers(keys))
                    throw new ArgumentException("Use a named Avalonia key and modifier.");
                if (key is Key.C or Key.V or Key.X or Key.Insert && keys != KeyModifiers.None)
                    throw new ArgumentException("Clipboard shortcuts are unavailable to the assistant.");
                return () => control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent,
                    Key = key, KeyModifiers = keys });
            default:
                if (action == "wheel" && (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double delta) ||
                    !double.IsFinite(delta) || Math.Abs(delta) > 20))
                    throw new ArgumentException("Wheel delta must be between -20 and 20.");
                if (!Enum.TryParse(button, out MouseButton mouse) || mouse is not (MouseButton.Left or MouseButton.Middle or MouseButton.Right) ||
                    !Enum.TryParse(modifiers, out KeyModifiers mods) || !ValidModifiers(mods))
                    throw new ArgumentException("Use Left, Middle or Right and a named modifier.");
                if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(endX) || !double.IsFinite(endY) ||
                    x < 0 || y < 0 || x >= control.Bounds.Width || y >= control.Bounds.Height ||
                    action == "drag" && (endX < 0 || endY < 0 || endX >= control.Bounds.Width || endY >= control.Bounds.Height))
                    throw new ArgumentException("Pointer coordinates must be inside the viewport.");
                return () => PointerAction(control, action, value, new Point(x, y), new Point(endX, endY), mouse, mods);
        }
    }

    private void PointerAction(Control control, string action, string value, Point start, Point end,
        MouseButton button, KeyModifiers mods)
    {
        var root = WindowOf(control);
        Point At(Point point) => control.TranslatePoint(point, root)!.Value;
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        if (action == "hover")
        {
            control.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, control, pointer, root,
                At(start), 0, new PointerPointProperties(), mods));
            return;
        }
        if (action == "wheel")
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double delta) ||
                !double.IsFinite(delta) || Math.Abs(delta) > 20) throw new ArgumentException("Wheel delta must be between -20 and 20.");
            control.RaiseEvent(new PointerWheelEventArgs(control, pointer, root, At(start), 0, new PointerPointProperties(), mods, new Vector(0, delta)));
            return;
        }
        var raw = button == MouseButton.Left ? RawInputModifiers.LeftMouseButton :
            button == MouseButton.Middle ? RawInputModifiers.MiddleMouseButton : RawInputModifiers.RightMouseButton;
        var down = button == MouseButton.Left ? PointerUpdateKind.LeftButtonPressed :
            button == MouseButton.Middle ? PointerUpdateKind.MiddleButtonPressed : PointerUpdateKind.RightButtonPressed;
        var up = button == MouseButton.Left ? PointerUpdateKind.LeftButtonReleased :
            button == MouseButton.Middle ? PointerUpdateKind.MiddleButtonReleased : PointerUpdateKind.RightButtonReleased;
        try
        {
            control.RaiseEvent(new PointerPressedEventArgs(control, pointer, root, At(start), 0,
                new PointerPointProperties(raw, down), mods, value == "double" ? 2 : 1));
            if (action == "drag") control.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, control,
                pointer, root, At(end), 0, new PointerPointProperties(raw, PointerUpdateKind.Other), mods));
            control.RaiseEvent(new PointerReleasedEventArgs(control, pointer, root, At(action == "drag" ? end : start),
                0, new PointerPointProperties(RawInputModifiers.None, up), mods, button));
        }
        finally { pointer.Capture(null); }
    }

    private static EditorActionPreview Refuse(string code, string message) => new(false, false, "error", code, message);
    private static bool ValidModifiers(KeyModifiers modifiers) =>
        (modifiers & ~(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta)) == 0;
    private static string SceneStamp(Control control) => control switch
    {
        GraphView graph => graph.AssistantInteractionStamp,
        SkeletonView skeleton => skeleton.AssistantInteractionStamp,
        _ => "",
    };
    private static async Task<T> OnUi<T>(Func<T> action) => Dispatcher.UIThread.CheckAccess()
        ? action() : await Dispatcher.UIThread.InvokeAsync(action);
}
