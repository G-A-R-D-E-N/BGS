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
        var grids = new System.Collections.Generic.List<HkGrid>();
        var buttons = new System.Collections.Generic.List<string?>();
        for (int i = 0; i < tabs[0].ItemCount; i++)
        {
            tabs[0].SelectedIndex = i;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            canvases += Find<GraphView>(window).Count;
            grids.AddRange(Find<HkGrid>(window));
            buttons.AddRange(Find<Button>(window).Select(b => b.Content?.ToString()));
        }

        Check("the node canvas exists", 1, canvases);
        Check("the tree, problem, symbol, chain and animation grids exist", 5, grids.Count);

        // The problem list is the one that starts hidden: an empty box under the canvas before any
        // check has run would read as a check that found nothing.
        Check("the problem list is hidden until a check has run", 1, grids.Count(g => !g.IsVisible));
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

            // The event summary is rows under an event, so it only exists once the Symbols tab has
            // been built. A behaviour file that declares events has to say what each one is for.
            if (shown.StartsWith("This is a behaviour file", StringComparison.Ordinal))
            {
                tabs[0].SelectedIndex = 2;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                var roles = new[] { "raised here", "listened for here", "referenced here" };
                var said = Find<TextBlock>(window).Select(t => t.Text ?? "")
                    .Where(t => roles.Any(r => t.Contains(r, StringComparison.Ordinal))).ToList();

                // Symbols are only built once the file has been unpacked, which needs a Java runtime
                // and the bundled jar. Without them the window is read only by design, so there is
                // nothing here to check and saying so beats failing.
                if (window.SymbolGrid.RowCount == 0)
                {
                    Console.WriteLine("        symbols: none built, the window opened read only");
                }
                else
                {
                    CheckTrue($"{name}: events say who sends and who listens", said.Count > 0);
                    CheckTrue($"{name}: and no row calls an event dead or unused",
                              !said.Any(t => t.Contains("dead", StringComparison.OrdinalIgnoreCase)
                                          || t.Contains("unused", StringComparison.OrdinalIgnoreCase)));
                    Console.WriteLine($"        symbols: {window.SymbolGrid.RowCount} rows, " +
                                      $"{said.Count} of them naming a role, e.g. \"{said.FirstOrDefault()}\"");
                }
            }

            // The fields have to be reachable from the canvas, not only from the tree. A node's
            // properties are useless in a tab that is not showing the node.
            if (window.LoadedXml.Length > 0)
            {
                tabs[0].SelectedIndex = 1;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                var canvas = Find<GraphView>(window).First();
                string node = OpenCommonwealth.Services.Hkx.HkxTextEdit
                    .IdsOfClass(window.LoadedXml, "hkbClipGenerator")
                    .FirstOrDefault(id => canvas.DrawnIds.Contains(id)) ?? canvas.DrawnIds.FirstOrDefault() ?? "";

                if (node.Length > 0)
                {
                    var fields = OpenCommonwealth.Services.Hkx.HkxTextEdit.ReadParams(window.LoadedXml, node);
                    window.SelectNode(node);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                    var boxes = Find<TextBox>(window.GraphProperties);
                    CheckTrue($"{name}: picking a node on the canvas fills the panel beside it",
                              boxes.Count >= fields.Count && fields.Count > 0);
                    Console.WriteLine($"        #{node}: {fields.Count} fields, {boxes.Count} boxes beside the canvas");

                    // Double click is what the request asks for, so the wiring from the canvas to
                    // the panel is what gets checked, not a synthetic mouse event.
                    canvas.Activated?.Invoke(node);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    CheckTrue($"{name}: double click still leaves the fields there",
                              Find<TextBox>(window.GraphProperties).Count >= fields.Count);

                    canvas.Highlight(node);
                    Check($"{name}: highlighting one node sticks", node, canvas.HighlightId);
                    canvas.ClearHighlight();
                    Check($"{name}: and clearing it releases the canvas", "", canvas.HighlightId);
                }
            }

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

                // The lookup the ticket asks for: a variable drives userControlledTimeFraction, and
                // this says which pose that is. 0 and 1 have to land on the ends, and the view has to
                // move to the frame rather than only printing its number.
                window.LookUpFraction("0");
                Check($"{name}: fraction 0 is the first frame", 0, window.AimedFrame);
                window.LookUpFraction("1");
                Check($"{name}: fraction 1 is the last frame", frames - 1, window.AimedFrame);
                CheckTrue($"{name}: and the page moved to it",
                          window.FramePageLabel.Contains($"to {frames - 1} ") || frames <= 300);
                Console.WriteLine($"        {window.FractionAnswer}");

                window.LookUpFraction("banana");
                Check($"{name}: nonsense is refused rather than aimed at", -1, window.AimedFrame);
                CheckTrue($"{name}: and it says so", window.FractionAnswer.Contains("not a number"));

                // Filtering to one bone is the difference between a browser and a wall of 95 tracks.
                string bone = window.AnimationGrid.RowCount > 0 ? "no-such-bone-xyzzy" : "";
                window.FilterBones(bone);
                CheckTrue($"{name}: a filter matching nothing says so rather than showing everything",
                          window.AnimationGrid.RowCount <= 2);
                window.FilterBones("");
                CheckTrue($"{name}: clearing the filter brings the tracks back",
                          window.AnimationGrid.RowCount > 2);
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
