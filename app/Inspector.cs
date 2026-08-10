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
        Children.Add(new ScrollViewer
        {
            Content = _body,
            Padding = new Thickness(10, 6, 10, 10),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        });
    }

    public StackPanel Body => _body;

    public void Clear() => _body.Children.Clear();

    public void Add(Control control) => _body.Children.Add(control);

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
