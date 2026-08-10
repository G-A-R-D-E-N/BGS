using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BehaviourStudio.App;

// A running graph has enough independent information that it needs its own surface. This window
// owns only the presentation controls: MainWindow remains the single owner of GraphRun and updates
// these controls whenever the simulation changes.
public sealed class RuntimeWindow : Window
{
    private readonly Grid _sections = new();
    private int _presentationRequests;
    private bool _wasOpened;
    private bool _wasActivated;

    public RuntimeWindow(HkGrid activeMachines, HkGrid stops, HkGrid heldBack, HkGrid eventLog,
                         ComboBox variables, TextBox value, Button setVariable, TextBlock status)
    {
        Title = "Behaviour Graph Studio Runtime";
        Width = 1100;
        Height = 700;
        MinWidth = 760;
        MinHeight = 480;
        Background = Ux.BaseBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        ShowActivated = true;
        ShowInTaskbar = true;

        var heading = new TextBlock
        {
            Text = "SIMULATION RUNTIME",
            Foreground = Ux.TitleBrush,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var header = new DockPanel { Margin = new Thickness(14, 12, 14, 8) };
        DockPanel.SetDock(heading, Dock.Left);
        header.Children.Add(heading);
        header.Children.Add(status);

        var variablesBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(14, 0, 14, 10),
        };
        variablesBar.Children.Add(Ux.SectionTitle("Variables"));
        variablesBar.Children.Add(variables);
        variablesBar.Children.Add(value);
        variablesBar.Children.Add(setVariable);

        _sections.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        _sections.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        _sections.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        _sections.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        Place(activeMachines, 0, 0, new Thickness(14, 0, 5, 5));
        Place(stops, 1, 0, new Thickness(5, 0, 14, 5));
        Place(heldBack, 0, 1, new Thickness(14, 5, 5, 14));
        Place(eventLog, 1, 1, new Thickness(5, 5, 14, 14));

        var content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(variablesBar, Dock.Top);
        content.Children.Add(header);
        content.Children.Add(variablesBar);
        content.Children.Add(_sections);
        Content = content;

        // Closing a tool window only hides it. The simulation remains in MainWindow and another
        // Runtime click restores this same view instead of creating a second consumer.
        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
        Opened += (_, _) => _wasOpened = true;
        Activated += (_, _) => _wasActivated = true;
    }

    public int SectionCount => _sections.Children.Count;
    public int PresentationRequests => _presentationRequests;
    public bool UsesDesktopPresentation => CanResize && ShowActivated && ShowInTaskbar
        && WindowStartupLocation == WindowStartupLocation.CenterOwner && Owner != null;
    public bool WasOpenedAndActivated => _wasOpened && _wasActivated;

    public void Present(Window owner)
    {
        _presentationRequests++;
        if (!IsVisible) Show(owner);
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    public void CloseForTest() => Close();

    private void Place(Control control, int column, int row, Thickness margin)
    {
        control.Margin = margin;
        Grid.SetColumn(control, column);
        Grid.SetRow(control, row);
        _sections.Children.Add(control);
    }
}
