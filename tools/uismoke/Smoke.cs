using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using BehaviourStudio.App;

namespace BehaviourStudio.UiSmoke;

// Builds the real window on a headless display and walks it, so "the window still opens" is
// something a build runner can prove rather than something someone has to look at. It cannot judge
// how the window looks; it can prove every part of it was constructed and is reachable.
public static class Smoke
{
    private static int _failed;
    private static int _ran;

    public static int Main(string[] args)
    {
        AppBuilder.Configure<HeadlessApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();

        var window = new MainWindow();
        window.Show();

        Check("the window has a title", "Behaviour Graph Studio", window.Title);

        var tabs = Find<TabControl>(window);
        Check("there is one tab control", 1, tabs.Count);

        var headers = tabs[0].Items.OfType<TabItem>().Select(t => t.Header?.ToString()).ToList();
        Check("tabs", "Tree, Graph, Symbols, Chain, Animation", string.Join(", ", headers));

        // A TabControl only builds the selected tab, so each one has to be visited to prove it is
        // whole. Visiting them also exercises the switching itself.
        var canvases = 0;
        var grids = 0;
        var buttons = new System.Collections.Generic.List<string?>();
        for (int i = 0; i < tabs[0].ItemCount; i++)
        {
            tabs[0].SelectedIndex = i;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            canvases += Find<GraphView>(window).Count;
            grids += Find<HkGrid>(window).Count;
            buttons.AddRange(Find<Button>(window).Select(b => b.Content?.ToString()));
        }

        Check("the node canvas exists", 1, canvases);
        Check("the tree, symbol, chain and animation grids exist", 4, grids);
        foreach (string expected in new[]
                 { "Open", "Browse...", "Expand all", "Collapse all", "Check graph", "Save to .hkx", "+ real", "+ event", "Remove" })
            CheckTrue($"the {expected} button is there", buttons.Contains(expected));

        tabs[0].SelectedIndex = 0;
        CheckTrue("save is disabled until something changes",
            Find<Button>(window).First(b => b.Content?.ToString() == "Save to .hkx").IsEnabled == false);

        // Opening a real file through the window, so what the panel actually says is checked rather
        // than assumed from what the reader returns. Paths come in on the command line because the
        // game's own files cannot be committed here.
        foreach (string path in args.Where(System.IO.File.Exists))
        {
            window.Open(path);
            // The panel has to be the selected tab or it is never built, so searching the visual
            // tree for its text finds nothing however correct the text is.
            tabs[0].SelectedIndex = tabs[0].ItemCount - 1;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            string name = System.IO.Path.GetFileName(path);
            var texts = Find<TextBlock>(window).Select(t => t.Text ?? "").ToList();
            string shown = texts.FirstOrDefault(t => t.StartsWith("Unsupported:", StringComparison.Ordinal)
                                                  || OpenCommonwealth.Services.Hkx.HkxAnimationData.DecodedAnimationClasses
                                                         .Any(c => t.Contains(c, StringComparison.Ordinal))
                                                  || t.StartsWith("This file holds no animation", StringComparison.Ordinal)
                                                  || t.StartsWith("Could not read this file as an animation", StringComparison.Ordinal)
                                                  || t.StartsWith("This is a behaviour file", StringComparison.Ordinal)) ?? "";

            CheckTrue($"{name}: the animation panel says something", shown.Length > 0);
            Console.WriteLine($"        {shown}");

            // A paged view can drop the tail without saying so, which is the failure the old 300 row
            // cap made visible and paging could hide. Walk every page and prove the frames shown add
            // up to the frames the file has, with the last page ending on the last frame.
            if (window.AnimationFrameCount > 0)
            {
                var button = Find<Button>(window)
                    .Where(b => !string.IsNullOrEmpty(b.Content?.ToString()))
                    .GroupBy(b => b.Content!.ToString()!)
                    .ToDictionary(g => g.Key, g => g.First());
                int frames = window.AnimationFrameCount, tracks = window.AnimationTrackCount;

                Click(button["First"]);
                int firstRows = window.AnimationGrid.RowCount;
                string firstLabel = window.FramePageLabel;

                int seen = 0, pages = 0;
                string label = "";
                for (int guard = 0; guard < 100; guard++)
                {
                    pages++;
                    seen += (window.AnimationGrid.RowCount - tracks) / Math.Max(tracks, 1);
                    label = window.FramePageLabel;
                    if (label.Contains($"of {frames}") && label.Contains($"to {frames - 1} ")) break;
                    if (frames <= 300) break;
                    Click(button["Later frames"]);
                    if (window.FramePageLabel == label) break;
                }

                Console.WriteLine($"        {frames} frames, {tracks} tracks, {pages} page(s): " +
                                  $"first \"{firstLabel}\" {firstRows} rows, last \"{label}\" {window.AnimationGrid.RowCount} rows");
                Check($"{name}: frames shown across all pages", frames, seen);

                Click(button["Last"]);
                CheckTrue($"{name}: the last page ends on frame {frames - 1}",
                          window.FramePageLabel.Contains($"to {frames - 1} ") || frames <= 300);
            }
        }

        Console.WriteLine($"\n{_ran} checks, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static void Click(Button button)
    {
        button.Command?.Execute(button.CommandParameter);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static System.Collections.Generic.List<T> Find<T>(Visual root) where T : Visual
    {
        var found = new System.Collections.Generic.List<T>();
        var stack = new System.Collections.Generic.Stack<Visual>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is T match) found.Add(match);
            foreach (var child in current.GetVisualChildren()) stack.Push(child);
        }
        return found;
    }

    private static void Check(string what, object expected, object? actual)
    {
        _ran++;
        bool ok = Equals(expected, actual);
        if (!ok) _failed++;
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {what,-46} expected {expected}, got {actual ?? "null"}");
    }

    private static void CheckTrue(string what, bool value)
    {
        _ran++;
        if (!value) _failed++;
        Console.WriteLine($"  {(value ? "ok  " : "FAIL")}  {what}");
    }
}

public class HeadlessApp : Application
{
    public override void Initialize() => Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
}
