using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BehaviourStudio.App;

public enum DiscardChoice { Cancel, Discard, Save }

public sealed class DiscardDialog : Window
{
    public DiscardChoice Choice { get; private set; } = DiscardChoice.Cancel;

    public DiscardDialog(string what)
    {
        Title = "Discard unsaved work?";
        Width = 440;
        Height = 190;
        MinWidth = 380;
        MinHeight = 160;
        Background = Ux.BaseBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowActivated = true;

        var header = new TextBlock
        {
            Text = $"The document has unsaved work. Save it before you {what}?",
            Foreground = Ux.TitleBrush,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var hint = new TextBlock
        {
            Text = "Anything you do not save will be lost.",
            Foreground = Ux.MutedBrush,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        };

        var save = Ux.Primary("Save and continue");
        var discard = Ux.Secondary("Discard changes");
        var cancel = Ux.Secondary("Cancel");

        save.Click += (_, _) => Complete(DiscardChoice.Save);
        discard.Click += (_, _) => Complete(DiscardChoice.Discard);
        cancel.Click += (_, _) => Complete(DiscardChoice.Cancel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { save, discard, cancel },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children = { header, hint, buttons },
        };
    }

    private void Complete(DiscardChoice choice)
    {
        Choice = choice;
        Close(choice);
    }
}
