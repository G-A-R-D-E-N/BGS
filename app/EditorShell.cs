using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace BehaviourStudio.App;

public sealed class EditorShell : Grid
{
    public const string Home = "Home";
    public const string Graph = "Graph";
    public const string Inspect = "Inspect";
    public const string Animation = "Animation";
    public const string Project = "Project";

    public const string ChatGlyph = "\U0001F4AC";
    public const string SettingsGlyph = "\u2699";

    public static readonly string[] ActivityNames = { Home, Graph, Inspect, Animation, Project };

    private static readonly (string Activity, string[] Tabs)[] Workspaces =
    {
        (Home, new[] { "Bridge" }),
        (Graph, new[] { "Graph" }),
        (Inspect, new[] { "Tree", "Symbols" }),
        (Animation, new[] { "Animation", "Playback" }),
        (Project, new[] { "Project search", "Chain", "Compare" }),
    };

    private readonly Dictionary<string, Button> _rail = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lastTab = new(StringComparer.Ordinal);
    private readonly StackPanel _secondary = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
    };
    private readonly Border _secondaryHost;
    private readonly Button _settingsButton;
    private readonly Button _chatButton;
    private readonly IReadOnlyDictionary<string, Control> _toolbars;
    private readonly StackPanel _animationTools = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly StackPanel _help = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly ScrollViewer _toolbarScroll = new()
    {
        AllowAutoHide = false,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };
    private readonly StackPanel _tools = new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    public EditorShell(Control command, Control workspace, Control status, IReadOnlyDictionary<string, Control> toolbars)
    {
        _toolbars = toolbars;
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var rail = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(0, 0, 12, 0),
        };
        foreach (var (activity, _) in Workspaces)
        {
            var button = new Button
            {
                Content = activity,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(12, 6),
                FontSize = Ux.FontBody,
                CornerRadius = new CornerRadius(Ux.Radius),
                BorderThickness = new Thickness(0),
            };
            string id = activity;
            button.Click += (_, _) => Navigate?.Invoke(TabFor(id));
            _rail[id] = button;
            rail.Children.Add(button);
        }

        _chatButton = RailIcon(ChatGlyph, "Open the assistant chat");
        _chatButton.Click += (_, _) => ChatRequested?.Invoke();

        _settingsButton = RailIcon(SettingsGlyph, "Assistant settings: provider, model and sign-in");
        _settingsButton.Click += (_, _) => SettingsRequested?.Invoke();

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { _chatButton, _settingsButton },
        };
        var commandBand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Ux.Space };
        commandBand.Children.Add(rail);
        commandBand.Children.Add(command);
        commandBand.Children.Add(footer);
        commandBand.Children.Add(_help);
        var commandHost = new Border
        {
            Background = Ux.BaseBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 12, 8),
            Child = new ScrollViewer
            {
                AllowAutoHide = false,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = commandBand,
            },
        };
        Children.Add(commandHost);

        _secondaryHost = new Border
        {
            Padding = new Thickness(12, 8, 12, 0),
            Child = _secondary,
            IsVisible = false,
        };
        SetRow(_secondaryHost, 1);
        Children.Add(_secondaryHost);

        var ribbon = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Ux.Space };
        foreach (var toolbar in _toolbars.Values) ribbon.Children.Add(toolbar);
        ribbon.Children.Add(_tools);
        ribbon.Children.Add(_animationTools);
        _toolbarScroll.Content = ribbon;
        var ribbonHost = new Border
        {
            Name = "WorkspaceToolbar", Background = Ux.CardBrush,
            BorderBrush = Ux.BorderBrush, BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 6),
            Child = _toolbarScroll,
        };
        SetRow(ribbonHost, 2);
        Children.Add(ribbonHost);

        var workspaceHost = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Child = workspace,
        };
        SetRow(workspaceHost, 3);
        Children.Add(workspaceHost);

        var statusHost = new Border
        {
            Background = Ux.RailBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 6, 12, 8),
            Child = status,
        };
        SetRow(statusHost, 4);
        Children.Add(statusHost);

        ShowTab("Bridge");
    }

    public event Action<string>? Navigate;

    public event Action? SettingsRequested;

    public event Action? ChatRequested;

    public Button SettingsButton => _settingsButton;

    public Button ChatButton => _chatButton;

    public string Activity { get; private set; } = Home;

    public IReadOnlyList<string> ActivityIds => ActivityNames;

    public Panel Tools => _tools;
    public Panel AnimationTools => _animationTools;
    public Panel Help => _help;

    public static EditorShell? Find(Control? root)
    {
        switch (root)
        {
            case EditorShell shell:
                return shell;
            case Panel panel:
                foreach (var child in panel.Children)
                {
                    var found = Find(child);
                    if (found != null) return found;
                }
                return null;
            case Decorator decorator:
                return Find(decorator.Child);
            default:
                return null;
        }
    }

    public bool HasTool<T>() where T : Control
    {
        foreach (var child in _tools.Children.Concat(_animationTools.Children).Concat(_help.Children))
        {
            if (child is T) return true;
            if (child is Decorator decorator)
            {
                if (decorator.Child is T) return true;
                if (decorator.Child is Panel inner && inner.Children.OfType<T>().Any()) return true;
            }
            if (child is Panel panel && panel.Children.OfType<T>().Any()) return true;
        }
        return false;
    }

    public void ShowTab(string header)
    {
        var workspace = WorkspaceForTab(header);
        if (workspace == null) return;

        Activity = workspace.Value.Activity;
        _lastTab[Activity] = header;
        PaintRail();
        PaintSecondary(workspace.Value.Tabs, header);
        foreach (var (tab, toolbar) in _toolbars) toolbar.IsVisible = tab == header;
        _tools.IsVisible = Activity == Graph;
        _animationTools.IsVisible = Activity == Animation;
        _toolbarScroll.Offset = default;
    }

    public static void HideStrip(TabControl tabs)
    {
        tabs.Padding = new Thickness(0);
        tabs.Template = new FuncControlTemplate<TabControl>((control, scope) =>
        {
            var presenter = new ContentPresenter
            {
                Name = "PART_SelectedContentHost",
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            var items = new ItemsPresenter
            {
                Name = "PART_ItemsPresenter", Height = 0, ClipToBounds = true,
                IsHitTestVisible = false,
            };
            KeyboardNavigation.SetTabNavigation(items, KeyboardNavigationMode.None);
            scope.Register(presenter.Name, presenter);
            scope.Register(items.Name, items);
            DockPanel.SetDock(items, Dock.Top);
            return new DockPanel { Children = { items, presenter } };
        });
    }

    private static Button RailIcon(string glyph, string tip)
    {
        var button = new Button
        {
            Content = glyph,
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(4, 8),
            CornerRadius = new CornerRadius(Ux.Radius),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Ux.MetaBrush,
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private string TabFor(string activity)
    {
        var workspace = Workspaces.First(item => item.Activity == activity);
        return _lastTab.TryGetValue(activity, out string? tab) ? tab : workspace.Tabs[0];
    }

    private static (string Activity, string[] Tabs)? WorkspaceForTab(string header)
    {
        foreach (var workspace in Workspaces)
            if (workspace.Tabs.Contains(header))
                return workspace;
        return null;
    }

    private void PaintRail()
    {
        foreach (var (id, button) in _rail)
        {
            bool on = id == Activity;
            button.Background = on ? Ux.AccentBrush : Brushes.Transparent;
            button.Foreground = on ? Brushes.White : Ux.MetaBrush;
        }
    }

    private void PaintSecondary(string[] tabs, string selected)
    {
        _secondary.Children.Clear();
        bool many = tabs.Length > 1;
        _secondaryHost.IsVisible = many;
        if (!many) return;

        foreach (string header in tabs)
        {
            bool on = header == selected;
            var button = new Button
            {
                Content = header,
                Padding = new Thickness(10, 4),
                FontSize = Ux.FontSmall,
                CornerRadius = new CornerRadius(Ux.Radius),
                Background = on ? Ux.CardHoverBrush : Ux.CardBrush,
                Foreground = on ? Ux.TitleBrush : Ux.MetaBrush,
                BorderBrush = on ? Ux.AccentBrush : Ux.BorderBrush,
                BorderThickness = new Thickness(1),
            };
            string tab = header;
            button.Click += (_, _) => Navigate?.Invoke(tab);
            _secondary.Children.Add(button);
        }
    }
}
