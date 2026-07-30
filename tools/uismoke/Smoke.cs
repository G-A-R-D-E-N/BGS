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

    public static int Main()
    {
        AppBuilder.Configure<HeadlessApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();

        var window = new MainWindow();
        window.Show();

        Check("the window has a title", "Behaviour Graph Studio", window.Title);

        var tabs = Find<TabControl>(window);
        Check("there is one tab control", 1, tabs.Count);

        var headers = tabs[0].Items.OfType<TabItem>().Select(t => t.Header?.ToString()).ToList();
        Check("tabs", "Tree, Graph, Symbols, Chain", string.Join(", ", headers));

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
        Check("the tree, symbol and chain grids exist", 3, grids);
        foreach (string expected in new[]
                 { "Open", "Expand all", "Collapse all", "Check graph", "Save to .hkx", "+ real", "+ event", "Remove" })
            CheckTrue($"the {expected} button is there", buttons.Contains(expected));

        tabs[0].SelectedIndex = 0;
        CheckTrue("save is disabled until something changes",
            Find<Button>(window).First(b => b.Content?.ToString() == "Save to .hkx").IsEnabled == false);

        Console.WriteLine($"\n{_ran} checks, {_failed} failed");
        return _failed == 0 ? 0 : 1;
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
