using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BehaviourStudio.App;

/// <summary>
/// The hard block shown when a file is refused at the open gate. It carries the
/// reason and a pointer to the issue tracker, and asks the main window to re-enable
/// itself when it closes. Shown while the main window is disabled, so it is the
/// only thing the user can act on.
/// </summary>
public sealed class NotBehaviourDialog : Window
{
    public NotBehaviourDialog(string reason, Action dismissed)
    {
        Title = "Can't open this file";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        Background = Ux.BaseBrush;

        var body = Ux.Label(reason);
        body.TextWrapping = TextWrapping.Wrap;
        body.MaxWidth = 430;

        var report = Ux.Label("If you believe this is a mistake, please report it at " +
                              "github.com/NomadsReach/BehaviorGraphStudio/issues");
        report.TextWrapping = TextWrapping.Wrap;
        report.MaxWidth = 430;
        report.Foreground = Ux.MutedBrush;

        var ok = Ux.Primary("OK");
        ok.Click += (_, _) => Close();

        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(18) };
        panel.Children.Add(body);
        panel.Children.Add(report);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { ok },
        });
        Content = panel;

        Closed += (_, _) => dismissed();
    }

    public void CloseForTest() => Close();
}
