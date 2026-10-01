using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace BehaviourStudio.App;

public sealed class AssistantModelPicker : Border
{
    private const double MaximumListHeight = 320;

    private readonly TextBox _search = new() { PlaceholderText = "Search models" };
    private readonly WrapPanel _agents = new();
    private readonly StackPanel _rows = new() { Spacing = 1 };
    private readonly ScrollViewer _list;
    private readonly List<(string Agent, string Label, Border Chip)> _agentChips = new();
    private readonly TextBlock _empty = new()
    {
        Text = "No models match that search.",
        Foreground = Ux.MutedBrush,
        FontSize = Ux.FontSmall,
        Margin = new Thickness(6, 8, 6, 8),
    };
    private readonly List<CodexModel> _visible = new();
    private IReadOnlyList<CodexModel> _models = Array.Empty<CodexModel>();
    private string _selected = "";
    private string _agent = "";
    private string _query = "";
    private string _agentFilter = "";
    private int _highlight = -1;

    public AssistantModelPicker()
    {
        Background = Ux.CardBrush;
        BorderBrush = Ux.BorderBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(Ux.Radius);
        Padding = new Thickness(8);
        MinWidth = 320;

        _search.FontSize = Ux.FontSmall;
        _search.KeyDown += OnSearchKeyDown;
        _search.TextChanged += (_, _) =>
        {
            string text = _search.Text ?? "";
            if (text == _query) return;
            _query = text;
            Rebuild();
        };

        var hint = new TextBlock
        {
            Text = "Up/Down navigate \u00B7 Enter select \u00B7 Esc close",
            Foreground = Ux.MutedBrush,
            FontSize = Ux.FontSmall,
            Margin = new Thickness(6, 6, 6, 0),
        };

        _list = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = MaximumListHeight,
            Content = _rows,
        };

        Child = new StackPanel
        {
            Spacing = 4,
            Children = { _search, _agents, _list, _empty, hint },
        };

        AddHandler(KeyDownEvent, OnPickerKeyDown, RoutingStrategies.Tunnel);
    }

    public event Action<CodexModel>? Chosen;
    public event Action? Dismissed;

    public string Query => _query;
    public string SelectedModel => _models
        .FirstOrDefault(model => string.Equals(Identity(model), _selected, StringComparison.Ordinal))
        ?.WireModel ?? "";
    public IReadOnlyList<string> VisibleModels => _visible.Select(model => model.WireModel).ToArray();
    internal IReadOnlyList<CodexModel> VisibleModelItems => _visible.ToArray();
    public IReadOnlyList<string> Agents =>
        _agentChips.Where(chip => chip.Agent.Length > 0).Select(chip => chip.Agent).ToArray();
    public IReadOnlyList<string> FilterLabels => _agentChips.Select(chip => chip.Label).ToArray();
    public string AgentFilter => _agentFilter;
    public int HighlightedIndex => _highlight;
    public TextBox Search => _search;
    public ScrollViewer ModelList => _list;

    public void SetModels(IReadOnlyList<CodexModel> models, string agent, string selected)
    {
        _models = models ?? Array.Empty<CodexModel>();
        _agent = agent ?? "";
        CodexModel? chosen = _models.FirstOrDefault(model =>
            string.Equals(AssistantModelCatalog.AgentOf(model), _agent, StringComparison.Ordinal) &&
            string.Equals(model.WireModel, selected ?? "", StringComparison.Ordinal));
        _selected = chosen is null ? "" : Identity(chosen);
        RebuildAgents();
        Rebuild();
    }

    public void SetQuery(string text)
    {
        _query = text ?? "";
        _search.Text = _query;
        Rebuild();
    }

    public void SetAgentFilter(string agent)
    {
        string resolved = agent ?? "";
        if (resolved.Length > 0 &&
            !_models.Any(model => string.Equals(AssistantModelCatalog.AgentOf(model), resolved, StringComparison.Ordinal)))
            resolved = "";
        _agentFilter = resolved;
        Rebuild();
    }

    public void FocusSearch() => _search.Focus();

    internal void InvokeFavorite(string identity)
    {
        AssistantModelPrefs.ToggleFavorite(identity);
        Rebuild();
    }

    internal bool FavoriteRow(int index)
    {
        if (index < 0 || index >= _visible.Count) return false;
        InvokeFavorite(Identity(_visible[index]));
        return true;
    }

    public bool Choose(string wireModel)
    {
        CodexModel? model = _models.FirstOrDefault(candidate =>
                string.Equals(candidate.WireModel, wireModel, StringComparison.Ordinal) &&
                string.Equals(AssistantModelCatalog.AgentOf(candidate), _agent, StringComparison.Ordinal))
            ?? _models.FirstOrDefault(candidate =>
                string.Equals(candidate.WireModel, wireModel, StringComparison.Ordinal));
        return model is not null && ChooseModel(model);
    }

    internal bool ChooseVisible(int index)
    {
        if (index < 0 || index >= _visible.Count) return false;
        return ChooseModel(_visible[index]);
    }

    internal bool ChooseModel(CodexModel model)
    {
        AssistantModelPrefs.Remember(Identity(model));
        _selected = Identity(model);
        Rebuild();
        Chosen?.Invoke(model);
        return true;
    }

    private static string Identity(CodexModel model) => AssistantModelCatalog.Identity(model);

    internal static string ContextLabel(long context) => context switch
    {
        >= 1_000_000 => (context / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (context / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K",
        > 0 => context.ToString(CultureInfo.InvariantCulture),
        _ => "",
    };

    private void RebuildAgents()
    {
        _agents.Children.Clear();
        _agentChips.Clear();
        IReadOnlyList<string> agents = AssistantModelCatalog.AgentsInOrder(_models);
        if (agents.Count <= 1)
        {
            _agents.IsVisible = false;
            return;
        }
        _agents.IsVisible = true;
        AddAgentChip("", "All");
        foreach (string agent in agents) AddAgentChip(agent, agent);
    }

    private void AddAgentChip(string agent, string label)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = Ux.FontSmall,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 3, 8, 3),
        };
        var chip = new Border
        {
            Child = text,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Ux.Radius),
            Margin = new Thickness(0, 0, 4, 4),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        chip.PointerPressed += (_, args) =>
        {
            args.Handled = true;
            SetAgentFilter(agent);
        };
        _agentChips.Add((agent, label, chip));
        _agents.Children.Add(chip);
    }

    private void PaintAgentChips()
    {
        foreach ((string agent, string _, Border chip) in _agentChips)
        {
            bool on = string.Equals(agent, _agentFilter, StringComparison.Ordinal);
            chip.Background = on ? Ux.AccentBrush : Ux.BaseBrush;
            chip.BorderBrush = on ? Ux.AccentBrush : Ux.BorderBrush;
            if (chip.Child is TextBlock text) text.Foreground = on ? Brushes.White : Ux.MetaBrush;
        }
    }

    private void Rebuild()
    {
        _rows.Children.Clear();
        _visible.Clear();
        _highlight = -1;
        PaintAgentChips();

        var matching = new List<CodexModel>();
        foreach (CodexModel model in _models)
            if (Matches(model)) matching.Add(model);

        _empty.IsVisible = matching.Count == 0;

        var placed = new HashSet<string>(StringComparer.Ordinal);
        AddSection("Favorites", matching, AssistantModelPrefs.Favorites(), placed);
        AddSection("Recent", matching, AssistantModelPrefs.Recents(), placed);

        foreach (string agent in AssistantModelCatalog.AgentsInOrder(matching))
        {
            List<CodexModel> available = matching
                .Where(model => !placed.Contains(Identity(model)) &&
                                string.Equals(AssistantModelCatalog.AgentOf(model), agent, StringComparison.Ordinal))
                .ToList();
            foreach (IGrouping<string, CodexModel> group in available.GroupBy(
                         model => model.Group.Length > 0 ? model.Group : agent))
            {
                AddSection(
                    AssistantModelCatalog.SectionTitle(agent, group.Key),
                    group.ToList(),
                    null,
                    placed);
            }
        }

        if (_highlight < 0 && _visible.Count > 0) _highlight = 0;
        PaintHighlight();
    }

    private void AddSection(
        string title, IReadOnlyList<CodexModel> matching, IReadOnlyList<string>? order, HashSet<string> placed)
    {
        List<CodexModel> rows;
        if (order is null)
        {
            rows = matching.Where(model => !placed.Contains(Identity(model))).ToList();
        }
        else
        {
            List<string> ordering = order.ToList();
            rows = matching
                .Where(model => ordering.Contains(Identity(model), StringComparer.Ordinal))
                .OrderBy(model => ordering.IndexOf(Identity(model)))
                .Where(model => !placed.Contains(Identity(model)))
                .ToList();
        }
        if (rows.Count == 0) return;

        _rows.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            Foreground = Ux.MutedBrush,
            FontSize = Ux.FontSmall,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(6, 8, 6, 2),
        });

        foreach (CodexModel model in rows)
        {
            placed.Add(Identity(model));
            _visible.Add(model);
            _rows.Children.Add(BuildRow(model, _visible.Count - 1));
        }
    }

    private Control BuildRow(CodexModel model, int index)
    {
        var name = new TextBlock
        {
            Text = model.DisplayName,
            Foreground = Ux.TitleBrush,
            FontSize = Ux.FontSmall,
            FontWeight = Identity(model) == _selected ? FontWeight.SemiBold : FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var meta = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        string context = ContextLabel(model.ContextLimit);
        if (context.Length > 0) meta.Children.Add(Chip(context));
        if (model.Reasoning) meta.Children.Add(Chip("Thinking"));

        bool favorite = AssistantModelPrefs.IsFavorite(Identity(model));
        var starText = new TextBlock
        {
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0),
            Text = favorite ? "\u2605" : "\u2606",
            Foreground = favorite ? Ux.WarnBrush : Ux.MutedBrush,
        };
        var star = new Border
        {
            Child = starText,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(star, "Favourite this model");
        star.PointerPressed += (_, args) =>
        {
            args.Handled = true;
            AssistantModelPrefs.ToggleFavorite(Identity(model));
            Rebuild();
        };
        star.PointerReleased += (_, args) => args.Handled = true;

        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(meta, Dock.Right);
        DockPanel.SetDock(star, Dock.Right);
        layout.Children.Add(star);
        layout.Children.Add(meta);
        layout.Children.Add(name);

        var row = new Border
        {
            Child = layout,
            Background = RowBrush(index == _highlight || Identity(model) == _selected),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(Ux.Radius),
            Padding = new Thickness(6, 4, 6, 4),
            Tag = index,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        row.PointerPressed += (_, args) =>
        {
            args.Handled = true;
            ChooseVisible(index);
        };
        return row;
    }

    private static IBrush RowBrush(bool on) => on ? Ux.CardHoverBrush : Brushes.Transparent;

    private static TextBlock Chip(string text) => new()
    {
        Text = text,
        Foreground = Ux.CodeBrush,
        FontSize = Ux.FontSmall,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void OnPickerKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape) return;
        args.Handled = true;
        Dismissed?.Invoke();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs args)
    {
        switch (args.Key)
        {
            case Key.Down:
                Move(1);
                args.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                args.Handled = true;
                break;
            case Key.Enter:
                if (_highlight >= 0 && _highlight < _visible.Count) ChooseVisible(_highlight);
                args.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        if (_visible.Count == 0) return;
        _highlight = (_highlight + delta + _visible.Count) % _visible.Count;
        PaintHighlight();
    }

    private void PaintHighlight()
    {
        for (int index = 0; index < _rows.Children.Count; index++)
        {
            if (_rows.Children[index] is not Border row || row.Tag is not int slot) continue;
            row.Background = RowBrush(slot == _highlight || Identity(_visible[slot]) == _selected);
        }
    }

    private bool Matches(CodexModel model)
    {
        if (_agentFilter.Length > 0 &&
            !string.Equals(AssistantModelCatalog.AgentOf(model), _agentFilter, StringComparison.Ordinal))
            return false;
        if (_query.Length == 0) return true;
        return model.DisplayName.Contains(_query, StringComparison.OrdinalIgnoreCase) ||
               model.WireModel.Contains(_query, StringComparison.OrdinalIgnoreCase);
    }
}
