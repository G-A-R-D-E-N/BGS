using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCommonwealth.Services.Archive;
using OpenCommonwealth.Services.Hkx;
using OpenCommonwealth.Services.Nif;
using OpenCommonwealth.Services;

namespace BehaviourStudio.App;

public partial class MainWindow : Window
{
    private static TabItem Tab(string header, Control content) => new()
    {
        Header = header,
        Content = content,
        Foreground = Ux.MetaBrush,
        FontSize = 12,
    };

    private static Border Framed(Control content) => new()
    {
        Background = Ux.CardBrush,
        BorderBrush = Ux.BorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Child = content,
    };

    private void GoToTab(string header)
    {
        foreach (var item in _tabs.Items)
            if (item is TabItem tab && tab.Header?.ToString() == header)
            {
                _tabs.SelectedItem = tab;
                _shell?.ShowTab(header);
                break;
            }
    }

    private void StartTour()
    {
        if (_tourStarted) return;
        _tourStarted = true;
        _bridgeSearch.Text = "";
        GoToTab("Bridge");

        var steps = new List<(Control Target, string Title, string Desc)>();
        if (_bridgeTitle != null)
            steps.Add((_bridgeTitle, "The Bridge",
                "Everything in the studio lives on this one deck. Open a file, jump to any " +
                "tool, or look up where something is — nothing hides in a menu you have to " +
                "remember."));
        if (_bridgeFileCard != null)
            steps.Add((_bridgeFileCard, "Current file",
                "Your open file and its state live here — open, check, save, undo and redo " +
                "without hunting for them."));
        foreach (var (card, title, what, _) in _tourStations)
            steps.Add((card, title,
                what + " Press Next to keep walking the deck, or Skip to jump straight in."));

        _tour.Start(steps, MarkTourDone);
        SetStatus("The tour is showing you around. Press Next to keep going, Skip to stop.", Ux.MetaBrush);
    }

    private static void MarkTourDone() => Settings.TrySet("tour_done", "1", out _);

    private void ApplyBridgeSearch()
    {
        string needle = (_bridgeSearch.Text ?? "").Trim();
        bool searching = needle.Length > 0;

        int stationShown = 0;
        foreach (var (header, cards) in _bridgeGroups)
        {
            bool any = false;
            foreach (var card in cards)
            {
                bool match = MatchBridgeCard(card, needle);
                card.IsVisible = match;
                if (match)
                {
                    any = true;
                    stationShown++;
                }
            }
            header.IsVisible = !searching || any;
        }

        int refShown = 0;
        foreach (var (name, where) in _bridgeRefRows)
        {
            bool match = !searching
                || Contains(name.Text, needle)
                || Contains(where.Text, needle);
            name.IsVisible = match;
            where.IsVisible = match;
            if (match) refShown++;
        }
        if (_bridgeRefHeader != null) _bridgeRefHeader.IsVisible = !searching || refShown > 0;

        if (searching && stationShown == 0 && refShown == 0)
            _bridgeSearchAnswer.Text = $"Nothing matches \"{needle}\".";
        else
            _bridgeSearchAnswer.Text = "";
        _bridgeSearchAnswer.IsVisible = _bridgeSearchAnswer.Text.Length > 0;
    }

    private bool MatchBridgeCard(Border card, string needle)
    {
        if (needle.Length == 0) return true;
        foreach (var (candidate, title, what, where) in _tourStations)
            if (candidate == card)
                return Contains(title, needle) || Contains(what, needle) || Contains(where, needle);
        return false;
    }

    private static bool Contains(string? text, string needle) =>
        (text ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool HasDroppedFiles(Avalonia.Input.IDataTransfer data) =>
        DroppedPaths(data).Count > 0;

    private void BridgeDragOver(object? sender, Avalonia.Input.DragEventArgs e)
    {
        bool files = HasDroppedFiles(e.DataTransfer);
        e.DragEffects = files ? Avalonia.Input.DragDropEffects.Copy : Avalonia.Input.DragDropEffects.None;
        e.Handled = true;
        if (files) ShowDropHint(FirstDroppedFileName(e.DataTransfer));
        else HideDropHint();
    }

    private void BridgeTabDragOver(object? sender, Avalonia.Input.DragEventArgs e)
    {
        if (_tabs.SelectedItem is TabItem { Header: "Bridge" })
            BridgeDragOver(sender, e);
    }

    private void BridgeTabDragLeave(object? sender, Avalonia.Input.DragEventArgs e)
    {
        HideDropHint();
    }

    private static List<string> DroppedPaths(Avalonia.Input.IDataTransfer data)
    {
        var files = Avalonia.Input.DataTransferExtensions.TryGetFiles(data);
        if (files is not { Length: > 0 } && Avalonia.Input.DataTransferExtensions.TryGetFile(data) is { } file)
            files = new[] { file };
        return (files ?? Array.Empty<IStorageItem>())
            .Select(item => item.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .ToList();
    }

    private void BridgeDrop(object? sender, Avalonia.Input.DragEventArgs e)
    {
        HideDropHint();
        if (!HasDroppedFiles(e.DataTransfer)) return;

        var files = DroppedPaths(e.DataTransfer);
        if (files.Count == 0) return;
        e.Handled = true;

        BridgeOpenPath(files[0]);
        if (files.Count > 1)
            SetStatus($"Dropped {files.Count} files — opened the first. Open the rest from the " +
                      "path bar or the recent row.", Ux.MetaBrush);
    }

    private void BridgeTabDrop(object? sender, Avalonia.Input.DragEventArgs e)
    {
        if (_tabs.SelectedItem is TabItem { Header: "Bridge" })
            BridgeDrop(sender, e);
    }

    private static string? FirstDroppedFileName(Avalonia.Input.IDataTransfer data)
    {
        try
        {
            return DroppedPaths(data).FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ShowDropHint(string? fileName)
    {
        _dropHint.Text = fileName is { Length: > 0 }
            ? $"Drop to open {Path.GetFileName(fileName)}"
            : "Drop to open";
        _dropHint.Foreground = Ux.AccentBrush;
        if (_bridgeFileCard != null) _bridgeFileCard.BorderBrush = Ux.AccentBrush;
    }

    private void HideDropHint()
    {
        _dropHint.Text = "Drop a .hkx anywhere on the Bridge to open it.";
        _dropHint.Foreground = Ux.MutedBrush;
        if (_bridgeFileCard != null) _bridgeFileCard.BorderBrush = Ux.BorderBrush;
    }

    public string BridgeDropHintText => _dropHint.Text ?? "";
    public void ShowDropHintForTest(string fileName) => ShowDropHint(fileName);
    public void HideDropHintForTest() => HideDropHint();
    public void DropFileForTest(string path) => BridgeOpenPath(path);

    public bool TourOverlayVisible => _tour.IsActive;

    private static void RememberRecent(string path)
    {
        var recents = new List<string>(RecentCount + 1) { path };
        for (int i = 0; i < RecentCount; i++)
        {
            string value = Settings.Get("recent." + i);
            if (value.Length > 0 && value != path && !recents.Contains(value)) recents.Add(value);
        }
        if (recents.Count > RecentCount) recents.RemoveRange(RecentCount, recents.Count - RecentCount);
        for (int i = 0; i < recents.Count; i++) Settings.TrySet("recent." + i, recents[i], out _);
    }

    private void RefreshRecents()
    {
        _bridgeRecents.Children.Clear();
        bool any = false;
        for (int i = 0; i < RecentCount; i++)
        {
            string path = Settings.Get("recent." + i);
            if (path.Length == 0) continue;
            any = true;
            var button = Ux.Secondary(Path.GetFileName(path));
            ToolTip.SetTip(button, path);
            button.MaxWidth = 280;
            button.Margin = new Thickness(0, 0, 6, 0);
            button.Click += (_, _) => BridgeOpenPath(path);
            _bridgeRecents.Children.Add(button);
        }
        if (!any)
        {
            var none = Ux.Label("No files opened yet. Your last few will appear here so you can jump " +
                                "straight back in.");
            none.Foreground = Ux.MutedBrush;
            _bridgeRecents.Children.Add(none);
        }
    }

    private void BridgeOpenPath(string path)
    {
        _pathField.Text = path;
        Load();
    }

    private static Border BridgeCard(string title, string what, string where,
                                     params (string Label, Action Go)[] actions)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Ux.TitleBrush,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
        });
        stack.Children.Add(new TextBlock
        {
            Text = what,
            Foreground = Ux.MetaBrush,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Where: " + where,
            Foreground = Ux.MutedBrush,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        for (int i = 0; i < actions.Length; i++)
        {
            var button = i == 0 ? Ux.Primary(actions[i].Label) : Ux.Secondary(actions[i].Label);
            var go = actions[i].Go;
            button.Click += (_, _) => go();
            buttons.Children.Add(button);
        }
        stack.Children.Add(buttons);

        return new Border
        {
            Background = Ux.CardBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12),
            Child = stack,
        };
    }

    private Border BuildBridgeFileCard()
    {
        var open = Ux.Primary("Open...");
        open.Click += (_, _) => Load();
        var browse = Ux.Secondary("Browse...");
        browse.Click += async (_, _) => await Browse();
        var archive = Ux.Secondary("From archive...");
        archive.Click += async (_, _) => await OpenFromArchive();
        var check = Ux.Secondary("Check graph");
        check.Click += (_, _) => Validate();
        var checkProject = Ux.Secondary("Check project");
        checkProject.Click += async (_, _) => await ValidateProject();

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var button in new Control[]
                 { open, browse, archive, check, checkProject, _bridgeUndo, _bridgeRedo, _bridgeSave })
            actions.Children.Add(button);

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(Ux.SectionTitle("Current file"));
        stack.Children.Add(_bridgeFile);
        stack.Children.Add(_bridgeLastAction);
        stack.Children.Add(actions);
        stack.Children.Add(_dropHint);

        return new Border
        {
            Background = Ux.CardBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12),
            Child = stack,
        };
    }
}
