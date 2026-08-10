using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace BehaviourStudio.App;




public sealed class WorkspaceWindow : Window
{
    private readonly TabControl _tabs = new();
    private readonly TextBox _filter = Ux.Field("filter machines by name or #id");
    private readonly HkGrid _machines;
    private readonly Action<string> _applyFilter;
    private int _presentationRequests;
    private bool _opened;
    private bool _activated;
    private bool _restored;

    public WorkspaceWindow(HkGrid machines, Control runtime, Action<string> applyFilter)
    {
        _machines = machines;
        _applyFilter = applyFilter;
        Title = "Behaviour Graph Studio Workspace";
        Width = 540;
        Height = 720;
        MinWidth = 360;
        MinHeight = 360;
        Background = Ux.BaseBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        ShowActivated = true;
        ShowInTaskbar = true;

        _filter.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) _applyFilter(_filter.Text ?? "");
        };

        var machineBody = new DockPanel { LastChildFill = true, Margin = new Thickness(12) };
        DockPanel.SetDock(_filter, Dock.Top);
        _filter.Margin = new Thickness(0, 0, 0, 8);
        machineBody.Children.Add(_filter);
        machineBody.Children.Add(_machines);

        _tabs.Items.Add(Tab("Machines", machineBody));
        _tabs.Items.Add(Tab("Runtime", runtime));
        Content = _tabs;

        Closing += (_, e) =>
        {
            e.Cancel = true;
            RememberBounds();
            Hide();
        };
        Opened += (_, _) => _opened = true;
        Activated += (_, _) => _activated = true;
    }

    public IReadOnlyList<string> TabHeaders => _tabs.Items.OfType<TabItem>()
        .Select(tab => tab.Header?.ToString() ?? "").ToList();
    public int MachineRowCount => _machines.RowCount;
    public string MachineFilterText => _filter.Text ?? "";
    public int PresentationRequests => _presentationRequests;
    public bool UsesDesktopPresentation => CanResize && ShowActivated && ShowInTaskbar && Owner != null;
    public bool WasOpenedAndActivated => _opened && _activated;

    public void FilterMachinesForTest(string text) => _filter.Text = text;
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

    private static TabItem Tab(string header, Control content) => new()
    {
        Header = header,
        Content = content,
        Foreground = Ux.MetaBrush,
        FontSize = 12,
    };

    private void RestoreBounds()
    {
        if (_restored) return;
        _restored = true;
        if (!double.TryParse(Settings.Get("workspace_width"), NumberStyles.Float,
                             CultureInfo.InvariantCulture, out double width) ||
            !double.TryParse(Settings.Get("workspace_height"), NumberStyles.Float,
                             CultureInfo.InvariantCulture, out double height) ||
            !int.TryParse(Settings.Get("workspace_x"), out int x) ||
            !int.TryParse(Settings.Get("workspace_y"), out int y)) return;

        Width = Math.Max(MinWidth, width);
        Height = Math.Max(MinHeight, height);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(x, y);
    }

    private void RememberBounds()
    {
        if (WindowState != WindowState.Normal) return;
        Settings.Set("workspace_width", Width.ToString(CultureInfo.InvariantCulture));
        Settings.Set("workspace_height", Height.ToString(CultureInfo.InvariantCulture));
        Settings.Set("workspace_x", Position.X.ToString(CultureInfo.InvariantCulture));
        Settings.Set("workspace_y", Position.Y.ToString(CultureInfo.InvariantCulture));
    }
}
