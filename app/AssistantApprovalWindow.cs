using Avalonia.Controls;
using Avalonia.Layout;

namespace BehaviourStudio.App;

internal sealed class AssistantApprovalWindow : Window
{
    public AssistantApprovalWindow(string description, Action approve, Action reject)
    {
        Title = "BGS assistant action approval";
        Width = 540; Height = 220;
        Background = Ux.BaseBrush; Foreground = Ux.CodeBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var yes = Ux.Primary("Approve action");
        var no = Ux.Secondary("Reject");
        bool decided = false;
        yes.Click += (_, _) => { decided = true; Close(); approve(); };
        no.Click += (_, _) => { decided = true; Close(); reject(); };
        Closed += (_, _) => { if (!decided) reject(); };
        Content = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12, Children =
        {
            new ScrollViewer { Height = 125, Content = new TextBlock { Text = description, TextWrapping = Avalonia.Media.TextWrapping.Wrap } },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { yes, no } },
        } };
    }
}
