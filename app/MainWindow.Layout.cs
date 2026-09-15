using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BehaviourStudio.App;

public partial class MainWindow : Window
{
    private Border GraphToolbarGroup(string name, params Control[] controls)
    {
        var label = new TextBlock
        {
            Text = name.ToUpperInvariant(),
            Foreground = Ux.MutedBrush,
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            LineHeight = 14,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        var tag = new Border
        {
            Background = Ux.BaseBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(8, 2),
            Child = label,
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(tag);
        foreach (var control in controls)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            if (control is Button or ComboBox) control.MinHeight = 28;
            row.Children.Add(control);
        }

        _graphToolbarGroups.Add(name);
        _graphToolbarGroupLabelsHaveFixedLineHeight = true;
        return new Border
        {
            Background = Ux.CardBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(1, 1, 6, 1),
            Child = row,
        };
    }

    private static Control Bar(params Control[] controls)
    {
        var panel = new DockPanel { LastChildFill = true };
        for (int i = controls.Length - 1; i >= 1; i--)
        {
            controls[i].Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(controls[i], Dock.Right);
            panel.Children.Add(controls[i]);
        }
        panel.Children.Add(controls[0]);
        return panel;
    }

    private static Control Rows(params (Control Control, bool Fill)[] rows)
    {
        var grid = new Grid();
        for (int i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(rows[i].Fill ? GridLength.Star : GridLength.Auto));
            rows[i].Control.Margin = new Thickness(0, i == 0 ? 0 : 10, 0, 0);
            Grid.SetRow(rows[i].Control, i);
            grid.Children.Add(rows[i].Control);
        }
        return grid;
    }

    private static void AddTop(DockPanel panel, Control control)
    {
        control.Margin = new Thickness(0, 0, 0, Ux.Space);
        DockPanel.SetDock(control, Dock.Top);
        panel.Children.Add(control);
    }

    private static ScrollViewer ControlStrip(params Control[] controls)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var control in controls) strip.Children.Add(control);
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            Content = strip,
        };
    }
}
