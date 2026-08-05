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

    public Inspector(double width)
    {
        MinWidth = width;
        var title = Ux.SectionTitle("Properties");
        SetDock(title, Dock.Top);
        Children.Add(title);
        // Disabled rather than Auto: the panel is narrow and fixed, so anything that does not fit
        // has to wrap or trim. Letting it scroll sideways instead just hid the left of every line.
        Children.Add(new ScrollViewer
        {
            Content = _body,
            Padding = new Thickness(0, 6, 8, 0),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        });
    }

    public StackPanel Body => _body;

    public void Clear() => _body.Children.Clear();

    public void Add(Control control) => _body.Children.Add(control);

    /// Puts the caret in the first value box, which is what a double click on a node is asking for:
    /// the fields, ready to type into, without a second click to find one.
    public void FocusFirstField()
    {
        var box = _body.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault();
        box?.Focus();
    }
}
