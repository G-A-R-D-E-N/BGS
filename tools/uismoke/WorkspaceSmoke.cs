using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BehaviourStudio.App;
using AppHost = BehaviourStudio.App.App;

namespace BehaviourStudio.UiSmoke;

internal static class WorkspaceSmoke
{
    private static int _failed;

    internal static int Run()
    {
        _failed = 0;
        ActivityRailSelectsExistingTabs();
        ProductionChromeLivesInTheShell();
        LayoutAndHandlersRemainConnected();
        StandaloneSampleResolvesAdjacentSkeleton();
        TreeFilterDoesNotSilentlyDimTheGraph();
        HkGridHeaderScrollsWithRows();
        return _failed;
    }

    internal static int LayoutRun()
    {
        _failed = 0;
        LayoutAndHandlersRemainConnected();
        StandaloneSampleResolvesAdjacentSkeleton();
        return _failed;
    }

    private static void StandaloneSampleResolvesAdjacentSkeleton()
    {
        string samples = Path.Combine(AppContext.BaseDirectory, "samples");
        string animation = Path.Combine(samples, "TurretIdleWeapReady.hkx");
        string skeleton = Path.Combine(samples, "TurretMountedSkeleton.hkx");
        if (!File.Exists(animation) || !File.Exists(skeleton)) return;

        Check("the standalone sample finds its adjacent skeleton", skeleton,
              MainWindow.SiblingSkeletonPath(animation) ?? "");

        string mesh = Path.Combine(samples, "Meshes", "Actors", "Turret", "CharacterAssets",
                                   "TurretMounted.nif");
        Check("the standalone sample finds its matching vanilla mesh", mesh,
              OpenCommonwealth.Services.Hkx.MeshLookup.Find(animation, null, skeleton).Path ?? "");
    }

    private static void LayoutAndHandlersRemainConnected()
    {
        Console.WriteLine("\nBGS control groups are owned by their views and keep their handlers");
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var shell = EditorShell.Find(window.Content as Control);
        CheckTrue("the command area groups file and document actions",
            shell != null && Smoke.Find<TextBlock>(shell).Any(text => text.Text == "OPEN BEHAVIOUR") &&
            Smoke.Find<TextBlock>(shell).Any(text => text.Text == "DOCUMENT ACTIONS"));
        CheckTrue("the status footer remains in the shell",
            shell != null && shell.Children.OfType<Border>().Any(child =>
                Grid.GetRow(child) == 3 && child.Child is Border));

        Smoke.SelectTab(window, "Playback");
        foreach (string section in new[] { "PLAYBACK CONTROLS", "VIEWPORT OVERLAYS", "SCENE TOOLS", "TIMELINE" })
            CheckTrue($"Playback has the {section.ToLowerInvariant()} section",
                Smoke.Find<TextBlock>(window).Any(text => text.Text == section));
        Smoke.Click(Smoke.Find<Button>(window).Single(button => button.Content?.ToString() == "Play"));
        CheckTrue("the Playback Play handler still reports the empty state",
            window.PlaybackSummary.StartsWith("Nothing loaded to play", StringComparison.Ordinal));

        Smoke.SelectTab(window, "Animation");
        foreach (string section in new[] { "FRAME NAVIGATION", "FIND A FRAME", "EDIT SELECTED FRAME" })
            CheckTrue($"Animation has the {section.ToLowerInvariant()} section",
                Smoke.Find<TextBlock>(window).Any(text => text.Text == section));

        Smoke.SelectTab(window, "Chain");
        foreach (string section in new[] { "PROJECT SWEEP", "CRASH HASH", "MOD ORGANIZER LAYER", "GAME DATA" })
            CheckTrue($"Chain has the {section.ToLowerInvariant()} section",
                Smoke.Find<TextBlock>(window).Any(text => text.Text == section));

        Smoke.SelectTab(window, "Bridge");
        Smoke.Click(Smoke.Find<Button>(window).First(button => button.Content?.ToString() == "Check graph"));
        Check("the command Check graph handler still runs", "Nothing loaded to check.", window.StatusForTest);
        Smoke.CloseForTest(window);
    }

    private static void Check(string what, object expected, object? actual)
    {
        bool ok = Equals(expected, actual);
        if (!ok) _failed++;
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {what,-46} expected {expected}, got {actual ?? "null"}");
    }

    private static void CheckTrue(string what, bool actual)
    {
        if (!actual) _failed++;
        Console.WriteLine($"  {(actual ? "ok  " : "FAIL")}  {what,-46} expected True, got {actual}");
    }

    private static void ActivityRailSelectsExistingTabs()
    {
        Console.WriteLine("\nthe activity rail selects the existing workspace tabs");
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Check("the rail names the five workspaces",
            "Home, Graph, Inspect, Animation, Project",
            string.Join(", ", window.ActivityIds));
        Check("Home is selected on an empty window", EditorShell.Home, window.SelectedActivity);

        Click(window, "Inspect");
        Check("Inspect opens the Tree tab", "Tree", SelectedHeader(window));
        Check("Inspect is the active workspace", EditorShell.Inspect, window.SelectedActivity);

        Click(window, "Graph");
        Check("Graph opens the Graph tab", "Graph", SelectedHeader(window));

        Click(window, "Animation");
        Check("Animation opens the Animation tab", "Animation", SelectedHeader(window));

        Click(window, "Project");
        Check("Project opens Project search", "Project search", SelectedHeader(window));

        Click(window, "Home");
        Check("Home returns to the Bridge", "Bridge", SelectedHeader(window));

        Click(window, "Inspect");
        var symbols = Smoke.Find<Button>(window).First(b => b.Content?.ToString() == "Symbols");
        symbols.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Check("the Inspect secondary view opens Symbols", "Symbols", SelectedHeader(window));

        Click(window, "Home");
        Click(window, "Inspect");
        Check("Inspect remembers the last secondary view", "Symbols", SelectedHeader(window));

        Click(window, "Home");
        CheckTrue("Bridge copy names Inspect then Tree",
            Smoke.Find<TextBlock>(window).Any(text =>
                (text.Text ?? "").Contains("Inspect", StringComparison.Ordinal)
                && (text.Text ?? "").Contains("Tree", StringComparison.Ordinal)
                && (text.Text ?? "").Contains("left rail", StringComparison.Ordinal)));
        CheckTrue("Bridge copy no longer says Tree tab",
            !Smoke.Find<TextBlock>(window).Any(text => (text.Text ?? "").Contains("Tree tab")));
        CheckTrue("Bridge copy no longer says bottom bar",
            !Smoke.Find<TextBlock>(window).Any(text => (text.Text ?? "").Contains("bottom bar")));

        Smoke.CloseForTest(window);
    }

    private static void ProductionChromeLivesInTheShell()
    {
        Console.WriteLine("\nproduction CreateMainWindow keeps Batch authoring and About in the shell");
        var window = AppHost.CreateMainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var shell = EditorShell.Find(window.Content as Control);
        CheckTrue("CreateMainWindow still hosts an EditorShell", shell != null);
        Check("it attaches one native authoring strip", 1,
            Smoke.Find<NativeAuthoringStrip>(window).Count);
        Check("it attaches one build-info strip", 1,
            Smoke.Find<BuildInfoStrip>(window).Count);
        CheckTrue("Batch authoring is reachable",
            Smoke.Find<Button>(window).Any(b => b.Content?.ToString() == "Batch authoring"));
        CheckTrue("Structure authoring is reachable",
            Smoke.Find<Button>(window).Any(b => b.Content?.ToString() == "Structure authoring"));
        CheckTrue("About is reachable",
            Smoke.Find<Button>(window).Any(b => b.Content?.ToString() == "About"));
        CheckTrue("the window content is not wrapped in a second top strip",
            window.Content is Grid);
        CheckTrue("the native strip lives inside the shell tools",
            shell != null && shell.HasTool<NativeAuthoringStrip>());
        CheckTrue("the build-info strip lives inside the shell tools",
            shell != null && shell.HasTool<BuildInfoStrip>());

        NativeAuthoringUi.Attach(window);
        BuildInfoUi.Attach(window);
        Dispatcher.UIThread.RunJobs();
        Check("a second attach does not stack native strips", 1,
            Smoke.Find<NativeAuthoringStrip>(window).Count);
        Check("a second attach does not stack build-info strips", 1,
            Smoke.Find<BuildInfoStrip>(window).Count);

        Smoke.CloseForTest(window);
    }

    private static void TreeFilterDoesNotSilentlyDimTheGraph()
    {
        Console.WriteLine("\na Tree filter does not silently dim the Graph");
        string path = Path.Combine(Path.GetTempPath(), $"bgs-shell-filter-{Guid.NewGuid():N}.hkx");
        File.WriteAllBytes(path, Smoke.OneMachineBytes());
        var window = new MainWindow();
        window.Show();
        window.Open(path);
        Dispatcher.UIThread.RunJobs();

        Click(window, "Inspect");
        var treeFilter = Smoke.Find<TextBox>(window)
            .First(box => (box.Watermark ?? "").StartsWith("filter objects", StringComparison.Ordinal));
        treeFilter.Text = "hkb";
        Dispatcher.UIThread.RunJobs();
        CheckTrue("the Tree filter finds rows", window.TreeGrid.RowCount > 0);

        Click(window, "Graph");
        Check("Graph is not left matching the hidden Tree filter", 0, window.Canvas.MatchCount);

        var graphFilter = Smoke.Find<TextBox>(window)
            .First(box => (box.Watermark ?? "").StartsWith("filter graph", StringComparison.Ordinal));
        graphFilter.Text = "hkb";
        Dispatcher.UIThread.RunJobs();
        CheckTrue("the Graph find field still filters the canvas", window.Canvas.MatchCount > 0);

        window.Filter("");
        Dispatcher.UIThread.RunJobs();
        Check("clearing Filter() releases the canvas", 0, window.Canvas.MatchCount);

        Smoke.CloseForTest(window);
        File.Delete(path);
    }

    private static void HkGridHeaderScrollsWithRows()
    {
        Console.WriteLine("\nHkGrid headers share horizontal scroll with rows");
        var grid = new HkGrid(("One", 220), ("Two", 220), ("Three", 220));
        for (int i = 0; i < 8; i++)
            grid.Add(null, $"a{i}", $"b{i}", $"c{i}");

        var host = new Window { Width = 260, Height = 240, Content = grid };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        grid.Measure(new Size(260, 220));
        grid.Arrange(new Rect(0, 0, 260, 220));
        Dispatcher.UIThread.RunJobs();

        var scroll = grid.HorizontalScroll;
        CheckTrue("the header is a descendant of the horizontal scroller",
            scroll.GetVisualDescendants().OfType<Grid>().Contains(grid.HeaderGrid));
        CheckTrue("the rows are a descendant of the same scroller",
            scroll.GetVisualDescendants().OfType<TreeView>().Contains(grid.BodyTree));
        CheckTrue("wide pixel columns overflow the viewport",
            scroll.Extent.Width > scroll.Viewport.Width + 1);

        var body = grid.FirstRowGrid;
        CheckTrue("the first body row has a column grid", body != null);
        double gap0 = ColumnGap(grid.HeaderGrid, body!, 0, scroll);
        double gap1 = ColumnGap(grid.HeaderGrid, body!, 1, scroll);
        scroll.Offset = new Vector(90, 0);
        Dispatcher.UIThread.RunJobs();
        Check("the shared scroller keeps the requested offset", 90d, scroll.Offset.X);
        Check("column 0 header/body gap is unchanged after scroll",
            Math.Round(gap0, 1), Math.Round(ColumnGap(grid.HeaderGrid, body!, 0, scroll), 1));
        Check("column 1 header/body gap is unchanged after scroll",
            Math.Round(gap1, 1), Math.Round(ColumnGap(grid.HeaderGrid, body!, 1, scroll), 1));
        host.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static double ColumnGap(Grid header, Grid body, int column, Visual relative)
    {
        var head = header.Children[column];
        var cell = body.Children[column];
        var from = head.TranslatePoint(new Point(0, 0), relative) ?? default;
        var to = cell.TranslatePoint(new Point(0, 0), relative) ?? default;
        return to.X - from.X;
    }

    private static void Click(MainWindow window, string activity)
    {
        Smoke.Find<Button>(window).First(b => b.Content?.ToString() == activity)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static string SelectedHeader(MainWindow window)
    {
        var tabs = Smoke.Find<TabControl>(window)
            .First(control => control.Items.OfType<TabItem>()
                .Any(tab => tab.Header?.ToString() == "Bridge"));
        return tabs.SelectedItem is TabItem tab ? tab.Header?.ToString() ?? "" : "";
    }
}
