using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BehaviourStudio.App;

public sealed class BuildInfoStrip : Border
{
}

public static class BuildInfoUi
{
    public static void Attach(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.Content is not Control current) return;
        if (AlreadyAttached(current)) return;

        var summary = new TextBlock
        {
            Text = $"v{BuildInfo.Version} · build {BuildInfo.Build}",
            Foreground = Ux.MutedBrush,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var about = Ux.Secondary("About");
        ToolTip.SetTip(about, "Show copyable version and build information for bug reports.");
        about.Click += (_, _) => new AboutWindow(window).Present();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        row.Children.Add(summary);
        row.Children.Add(about);

        var strip = new BuildInfoStrip
        {
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(10, 0, 0, 0),
            Child = row,
        };

        var editor = EditorShell.Find(current);
        if (editor != null)
        {
            var native = editor.Tools.Children.OfType<NativeAuthoringStrip>().FirstOrDefault();
            if (native?.Child is Panel nativeRow)
            {
                nativeRow.Children.Add(strip);
                return;
            }
            editor.Tools.Children.Add(strip);
            return;
        }

        if (current is DockPanel shell)
        {
            var native = shell.Children.OfType<NativeAuthoringStrip>().FirstOrDefault();
            if (native?.Child is Panel nativeRow)
            {
                nativeRow.Children.Add(strip);
                return;
            }

            if (shell.LastChildFill && shell.Children.Count > 0)
            {
                strip.Background = Ux.BaseBrush;
                strip.BorderThickness = new Thickness(0, 0, 0, 1);
                strip.Padding = new Thickness(14, 4);
                DockPanel.SetDock(strip, Dock.Top);
                shell.Children.Insert(shell.Children.Count - 1, strip);
                return;
            }
        }

        strip.Background = Ux.BaseBrush;
        strip.BorderThickness = new Thickness(0, 0, 0, 1);
        strip.Padding = new Thickness(14, 4);
        DockPanel.SetDock(strip, Dock.Top);
        window.Content = null;
        var host = new DockPanel { LastChildFill = true };
        host.Children.Add(strip);
        host.Children.Add(current);
        window.Content = host;
    }

    private static bool AlreadyAttached(Control current)
    {
        if (EditorShell.Find(current)?.HasTool<BuildInfoStrip>() == true) return true;
        if (current is not DockPanel shell) return false;
        if (shell.Children.Any(child => child is BuildInfoStrip)) return true;
        return shell.Children.OfType<NativeAuthoringStrip>().Any(native =>
            native.Child is Panel row && row.Children.Any(child => child is BuildInfoStrip));
    }

    public static AboutWindow OpenAboutForTest(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var about = new AboutWindow(window);
        about.Present();
        return about;
    }
}

public sealed class AboutWindow : Window
{
    private readonly Window _owner;

    public AboutWindow(Window owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Title = "About Behaviour Graph Studio";
        Width = 560;
        Height = 210;
        MinWidth = 480;
        MinHeight = 190;
        Background = Ux.BaseBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var report = new TextBox
        {
            Text = BuildInfo.Report,
            IsReadOnly = true,
            Foreground = Ux.CodeBrush,
            Background = Ux.CardBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var close = Ux.Secondary("Close");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Click += (_, _) => Close();

        var body = new StackPanel { Spacing = 10, Margin = new Thickness(16) };
        body.Children.Add(new TextBlock
        {
            Text = "Behaviour Graph Studio",
            Foreground = Ux.TitleBrush,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
        });
        body.Children.Add(new TextBlock
        {
            Text = "Select and copy the build line below when filing a bug report.",
            Foreground = Ux.MetaBrush,
            FontSize = 12,
        });
        body.Children.Add(report);
        body.Children.Add(close);
        Content = body;
    }

    public string BuildReport => BuildInfo.Report;

    public void Present()
    {
        if (!IsVisible) Show(_owner);
        Activate();
        Focus();
    }
}
