using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BehaviourStudio.App;

// A column grid built on TreeView, used for the object tree, the symbol table and the project
// chain. Rows are built as containers directly rather than through templates and bindings, because
// every row here is written once and then read, and a binding layer would only add somewhere for
// the two to disagree.
public sealed class HkGrid : Border
{
    private readonly TreeView _tree = new() { Background = Brushes.Transparent };
    private readonly (string Title, double Width)[] _columns;
    private readonly Grid _header;

    public HkGrid(params (string Title, double Width)[] columns)
    {
        _columns = columns;
        Background = Ux.CardBrush;
        BorderBrush = Ux.BorderBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);

        _header = Columns();
        _header.Margin = new Thickness(6, 4, 6, 4);
        for (int i = 0; i < columns.Length; i++)
        {
            var title = new TextBlock
            {
                Text = columns[i].Title,
                Foreground = Ux.MutedBrush,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
            };
            Grid.SetColumn(title, i);
            _header.Children.Add(title);
        }

        _tree.SelectionChanged += (_, _) => SelectionChanged?.Invoke();

        var stack = new DockPanel();
        var rule = new Border { Height = 1, Background = Ux.BorderBrush };
        DockPanel.SetDock(_header, Dock.Top);
        DockPanel.SetDock(rule, Dock.Top);
        stack.Children.Add(_header);
        stack.Children.Add(rule);
        stack.Children.Add(new ScrollViewer
        {
            Content = _tree,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });
        Child = stack;
    }

    public event Action? SelectionChanged;

    public object? SelectedTag => (_tree.SelectedItem as TreeViewItem)?.Tag;

    /// Every row in the grid, nested ones included. A window over a long list can then be checked
    /// for holding what it claims to rather than taken on trust.
    public int RowCount { get; private set; }

    public void Clear()
    {
        _tree.ItemsSource = null;
        _tree.Items.Clear();
        RowCount = 0;
    }

    public HkRow Add(HkRow? parent, params string[] cells)
    {
        var row = new HkRow(Columns(), cells, _columns.Length, parent?.Depth + 1 ?? 0);
        if (parent == null) _tree.Items.Add(row.Item);
        else parent.Item.Items.Add(row.Item);
        RowCount++;
        return row;
    }

    /// Selects the first row carrying this tag, expanding whatever it sits under. Lets a check drive
    /// selection the way a click does rather than calling the handler behind it.
    public bool SelectByTag(object tag)
    {
        foreach (var item in _tree.Items)
            if (Select(item as TreeViewItem, tag)) return true;
        return false;
    }

    private bool Select(TreeViewItem? item, object tag)
    {
        if (item == null) return false;
        if (item.Tag != null && item.Tag.Equals(tag))
        {
            _tree.SelectedItem = item;
            return true;
        }

        foreach (var child in item.Items)
            if (Select(child as TreeViewItem, tag))
            {
                item.IsExpanded = true;
                return true;
            }
        return false;
    }

    public void SetAllExpanded(bool expanded)
    {
        foreach (var item in _tree.Items) Walk(item as TreeViewItem, expanded);
    }

    private static void Walk(TreeViewItem? item, bool expanded)
    {
        if (item == null) return;
        item.IsExpanded = expanded;
        foreach (var child in item.Items) Walk(child as TreeViewItem, expanded);
    }

    private Grid Columns()
    {
        var grid = new Grid();
        foreach (var (_, width) in _columns)
            grid.ColumnDefinitions.Add(new ColumnDefinition(
                width < 0 ? new GridLength(-width, GridUnitType.Star) : new GridLength(width)));
        return grid;
    }
}

public sealed class HkRow
{
    public readonly TreeViewItem Item;
    private readonly TextBlock[] _cells;
    public readonly int Depth;

    internal HkRow(Grid layout, string[] cells, int columns, int depth)
    {
        Depth = depth;
        _cells = new TextBlock[columns];

        for (int i = 0; i < columns; i++)
        {
            var text = new TextBlock
            {
                Text = i < cells.Length ? cells[i] : "",
                Foreground = Ux.MetaBrush,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(text, i);
            layout.Children.Add(text);
            _cells[i] = text;
        }

        // TreeView already indents by depth. The first column's own left padding is left at zero so
        // the indent it applies is the only one, otherwise deep trees drift off the right edge.
        Item = new TreeViewItem { Header = layout, IsExpanded = depth < 2, Padding = new Thickness(0, 1) };
    }

    public HkRow Colour(int column, IBrush brush)
    {
        if (column < _cells.Length) _cells[column].Foreground = brush;
        return this;
    }

    public HkRow Tag(object tag)
    {
        Item.Tag = tag;
        return this;
    }

    public HkRow Collapse()
    {
        Item.IsExpanded = false;
        return this;
    }

    public string Text(int column) => column < _cells.Length ? _cells[column].Text ?? "" : "";
}
