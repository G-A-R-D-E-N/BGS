using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace BehaviourStudio.App;

// The properties panel, as a control rather than a loose StackPanel, so the same panel can sit
// beside the tree and beside the canvas. A node's fields are only useful next to the node, and the
// canvas is where the node is.
public sealed class Inspector : DockPanel
{
    private readonly StackPanel _body = new() { Spacing = 6 };
    private readonly Grid _header = new();
    private readonly ScrollViewer _scroll;

    public Inspector(double width)
    {
        MinWidth = width;
        ClipToBounds = true;
        var title = Ux.SectionTitle("Properties");
        _header.Margin = new Thickness(10, 8, 10, 2);
        _header.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        _header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(title, 0);
        _header.Children.Add(title);
        SetDock(_header, Dock.Top);
        Children.Add(_header);
        // Disabled rather than Auto: the panel is narrow and fixed, so anything that does not fit
        // has to wrap or trim. Letting it scroll sideways instead just hid the left of every line.
        _scroll = new ScrollViewer
        {
            Content = _body,
            Padding = new Thickness(10, 6, 10, 10),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            ClipToBounds = true,
        };
        Children.Add(_scroll);
    }

    public StackPanel Body => _body;

    public void Clear() => _body.Children.Clear();

    public void Add(Control control) => _body.Children.Add(control);

    // A field cannot make the inspector wider than its host. The label remains readable enough to
    // identify the member, while a long value is contained by the star column rather than pushing
    // paint out through a scroll content's desired width.
    public Control TwoColumnRow(Control label, Control value, double labelWidth = 128)
    {
        label.ClipToBounds = true;
        value.ClipToBounds = true;
        value.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        value.MinWidth = 0;

        var row = new Grid { ClipToBounds = true };
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(labelWidth)));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        Grid.SetColumn(label, 0);
        Grid.SetColumn(value, 1);
        row.Children.Add(label);
        row.Children.Add(value);
        return row;
    }

    public bool ContentsFitWidth => ClipToBounds && _scroll.ClipToBounds
        && _body.Children.All(control => control.ClipToBounds || control is TextBlock);
    public bool ScrollsVerticallyOnly => _scroll.HorizontalScrollBarVisibility ==
        Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled &&
        _scroll.VerticalScrollBarVisibility == Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;

    public void SetHeaderAction(string text, Action action)
    {
        var button = Ux.Secondary(text);
        button.Margin = new Thickness(16, 0, 0, 0);
        button.Click += (_, _) => action();
        Grid.SetColumn(button, 1);
        _header.Children.Add(button);
    }

    /// Puts the caret in the first value box, which is what a double click on a node is asking for:
    /// the fields, ready to type into, without a second click to find one.
    public void FocusFirstField()
    {
        var box = _body.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault();
        box?.Focus();
    }
}
