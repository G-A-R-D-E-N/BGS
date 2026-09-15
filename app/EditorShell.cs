using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    private readonly WrapPanel _tools = new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    public EditorShell(Control command, Control workspace, Control status)
    {
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(76)));
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var rail = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(6, 10, 6, 10),
        };
        foreach (var (activity, _) in Workspaces)
        {
            var button = new Button
            {
                Content = activity,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(4, 10),
                FontSize = Ux.FontSmall,
                CornerRadius = new CornerRadius(Ux.Radius),
                BorderThickness = new Thickness(0),
            };
            string id = activity;
            button.Click += (_, _) => Navigate?.Invoke(TabFor(id));
            _rail[id] = button;
            rail.Children.Add(button);
        }

        var railHost = new Border
        {
            Background = Ux.RailBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = rail,
        };
        SetRowSpan(railHost, 4);
        Children.Add(railHost);

        var commandBand = new StackPanel { Spacing = Ux.Space };
        commandBand.Children.Add(command);
        commandBand.Children.Add(_tools);
        var commandHost = new Border
        {
            Background = Ux.BaseBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 12, 8),
            Child = commandBand,
        };
        SetColumn(commandHost, 1);
        Children.Add(commandHost);

        _secondaryHost = new Border
        {
            Padding = new Thickness(12, 8, 12, 0),
            Child = _secondary,
            IsVisible = false,
        };
        SetColumn(_secondaryHost, 1);
        SetRow(_secondaryHost, 1);
        Children.Add(_secondaryHost);

        var workspaceHost = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Child = workspace,
        };
        SetColumn(workspaceHost, 1);
        SetRow(workspaceHost, 2);
        Children.Add(workspaceHost);

        var statusHost = new Border
        {
            Background = Ux.RailBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 6, 12, 8),
            Child = status,
        };
        SetColumn(statusHost, 1);
        SetRow(statusHost, 3);
        Children.Add(statusHost);

        ShowTab("Bridge");
    }

    public event Action<string>? Navigate;

    public string Activity { get; private set; } = Home;

    public IReadOnlyList<string> ActivityIds => ActivityNames;

    public Panel Tools => _tools;

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
        foreach (var child in _tools.Children)
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
    }

    public static void HideStrip(TabControl tabs)
    {
        tabs.Padding = new Thickness(0);
        void Hide()
        {
            foreach (var child in tabs.GetVisualChildren())
                if (child is TabStrip strip)
                    strip.IsVisible = false;
        }

        tabs.AttachedToVisualTree += (_, _) => Hide();
        int attempts = 0;
        EventHandler? once = null;
        once = (_, _) =>
        {
            Hide();
            attempts++;
            if (attempts > 8 || tabs.GetVisualChildren().OfType<TabStrip>().Any(strip => !strip.IsVisible))
                tabs.LayoutUpdated -= once;
        };
        tabs.LayoutUpdated += once;
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
