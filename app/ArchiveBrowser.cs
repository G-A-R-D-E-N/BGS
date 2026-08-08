using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using OpenCommonwealth.Services.Archive;

namespace BehaviourStudio.App;

// Picking one file out of a BA2 without unpacking the archive.
//
// Every behaviour in the game lives inside Fallout4 - Animations.ba2, which holds 29,716 entries.
// Reaching one of them used to mean writing the other 29,715 to disk first. Reading the index is
// about a second and touches none of the file data, so the list below is the archive itself rather
// than a folder somebody prepared earlier.
//
// Read only, which is what an archive is. Nothing here writes back into it.
public sealed class ArchiveBrowser : Window
{
    private readonly Ba2 _archive;
    private readonly ListBox _list = new() { Background = Ux.CardBrush, MaxHeight = 520 };
    private readonly TextBox _filter = Ux.Field();
    private readonly TextBlock _count = Ux.Label("");

    /// The entry the user settled on, or null if they closed the window without choosing.
    public Ba2.Entry? Chosen { get; private set; }

    public ArchiveBrowser(Ba2 archive, string extension)
    {
        _archive = archive;
        Title = "Open from " + System.IO.Path.GetFileName(archive.Path);
        Width = 900;
        Height = 640;
        Background = Ux.BaseBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _filter.Watermark = "words to look for, in any order, such as: dogmeat behavior";

        // Filtering on every keystroke rather than on Enter. 29,716 string comparisons is nothing,
        // and a list that narrows as you type is how you find a file whose exact path you do not
        // remember, which is the whole case for this window.
        _filter.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) Fill(extension);
        };

        _filter.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && _list.ItemCount > 0) { _list.SelectedIndex = 0; _list.Focus(); }
            if (e.Key == Key.Enter) Accept();
        };

        _list.DoubleTapped += (_, _) => Accept();
        _list.KeyDown += (_, e) => { if (e.Key == Key.Enter) Accept(); };

        var openIt = Ux.Primary("Open");
        openIt.Click += (_, _) => Accept();

        var cancel = Ux.Secondary("Cancel");
        cancel.Click += (_, _) => Close();

        Content = new Border
        {
            Padding = new Thickness(14),
            Child = new DockPanel
            {
                Children =
                {
                    Top(Ux.SectionTitle($"{archive.Entries.Count} files in this archive")),
                    Top(_filter),
                    Top(_count),
                    Bottom(new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 10, 0, 0),
                        Children = { cancel, openIt },
                    }),
                    _list,
                },
            },
        };

        Fill(extension);
        Opened += (_, _) => _filter.Focus();
    }

    private static Control Top(Control c)
    {
        DockPanel.SetDock(c, Dock.Top);
        c.Margin = new Thickness(0, 0, 0, 8);
        return c;
    }

    private static Control Bottom(Control c)
    {
        DockPanel.SetDock(c, Dock.Bottom);
        return c;
    }

    // A cap rather than the whole archive, because an empty filter matches 29,716 rows and Avalonia
    // draws every one of them into the visual tree. The count above the list always says the real
    // number, so a capped list never reads as a complete one.
    private const int Shown = 400;

    private void Fill(string extension)
    {
        var found = _archive.Matching(_filter.Text ?? "", extension).ToList();

        _list.ItemsSource = found.Take(Shown).Select(e => e.Name).ToList();
        _tail = found;

        _count.Text = found.Count == 0
            ? "nothing matches"
            : found.Count > Shown
                ? $"{found.Count} match, showing the first {Shown}. Narrow it to see the rest."
                : $"{found.Count} match";
    }

    private List<Ba2.Entry> _tail = new();

    private void Accept()
    {
        int at = _list.SelectedIndex;
        if (at < 0 && _tail.Count > 0 && (_filter.Text ?? "").Length > 0) at = 0;
        if (at < 0 || at >= _tail.Count) return;

        Chosen = _tail[at];
        Close();
    }
}
