using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;

namespace BehaviourStudio.App;

// Legend is occasional reference material, so it gets an independent temporary window rather than
// taking width from the graph or sharing the Workspace navigator surface.
public sealed class LegendWindow : Window
{
    private int _presentationRequests;
    private bool _restored;

    public LegendWindow(Control legend)
    {
        Title = "Behaviour Graph Studio Legend";
        Width = 420;
        Height = 680;
        MinWidth = 300;
        MinHeight = 320;
        Background = Ux.BaseBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        ShowActivated = true;
        ShowInTaskbar = true;
        Content = legend;
        Closing += (_, e) =>
        {
            e.Cancel = true;
            RememberBounds();
            Hide();
        };
    }

    public int PresentationRequests => _presentationRequests;
    public void CloseForTest() => Close();

    public void Present(Window owner)
    {
        _presentationRequests++;
        RestoreBounds();
        if (!IsVisible) Show(owner);
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    private void RestoreBounds()
    {
        if (_restored) return;
        _restored = true;
        if (!double.TryParse(Settings.Get("legend_width"), NumberStyles.Float,
                             CultureInfo.InvariantCulture, out double width) ||
            !double.TryParse(Settings.Get("legend_height"), NumberStyles.Float,
                             CultureInfo.InvariantCulture, out double height) ||
            !int.TryParse(Settings.Get("legend_x"), out int x) ||
            !int.TryParse(Settings.Get("legend_y"), out int y)) return;

        Width = Math.Max(MinWidth, width);
        Height = Math.Max(MinHeight, height);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(x, y);
    }

    private void RememberBounds()
    {
        if (WindowState != WindowState.Normal) return;
        Settings.Set("legend_width", Width.ToString(CultureInfo.InvariantCulture));
        Settings.Set("legend_height", Height.ToString(CultureInfo.InvariantCulture));
        Settings.Set("legend_x", Position.X.ToString(CultureInfo.InvariantCulture));
        Settings.Set("legend_y", Position.Y.ToString(CultureInfo.InvariantCulture));
    }
}
