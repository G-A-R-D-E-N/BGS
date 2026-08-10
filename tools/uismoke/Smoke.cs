using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using BehaviourStudio.App;

namespace BehaviourStudio.UiSmoke;




public static class Smoke
{
    private static int _failed;
    private static int _ran;

















    private static int Png(string[] args)
    {


        AppBuilder.Configure<HeadlessApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        string file = args[1];
        string output = args.Length > 2 && !args[2].StartsWith("--") ? args[2] : System.IO.Path.ChangeExtension(file, ".png");
        var rest = args.Skip(3).Where(a => !a.StartsWith("--")).ToList();
        double zoom = rest.Count > 0 && double.TryParse(rest[0], out double z) ? z : 0.75;
        string focus = rest.Count > 1 ? rest[1] : "";

        var window = new MainWindow();
        window.Show();
        window.Open(file);
        if (args.Contains("--structured-flow"))
            window.SetGraphLayoutModeForTest(GraphLayoutMode.StructuredFlow);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();



        var tabs = Find<TabControl>(window).First();
        tabs.SelectedIndex = tabs.Items.OfType<TabItem>().ToList()
                                 .FindIndex(t => t.Header?.ToString() == "Graph");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        if (args.Contains("--check"))
        {
            Find<Button>(window).First(b => b.Content?.ToString() == "Check graph")
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        if (args.Contains("--event") && window.RunEvents.Count > 0)
        {
            window.SendEventForTest(window.RunEvents[0]);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }



        bool whole = args.Contains("--window");
        if (args.Contains("--legend")) { window.OpenLegendForTest(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        if (args.Contains("--details"))
        {
            window.SetGraphDrawerOpen(true);
            if (args.Contains("--output")) window.SelectGraphDrawerTab("Output");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        if (args.Contains("--workspace-window"))
        {
            window.OpenWorkspaceForTest();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        var canvas = Find<GraphView>(window).First();
        bool workspaceWindow = args.Contains("--workspace-window");
        var size = workspaceWindow ? new Size(1100, 700) : new Size(1600, 1000);
        Control drawn = workspaceWindow ? window.WorkspaceWindowForTest! : whole ? window : canvas;
        drawn.Measure(size);
        drawn.Arrange(new Rect(size));

        if (focus.Length > 0)
        {



            if (!focus.All(char.IsDigit))
            {
                var model = OpenCommonwealth.Services.Hkx.BehaviourGraphModel.Parse(window.LoadedXml);
                string found = model.Objects
                    .FirstOrDefault(o => string.Equals(o.Str("name"), focus, StringComparison.OrdinalIgnoreCase))
                    ?.Id ?? "";
                if (found.Length == 0)
                {
                    Console.WriteLine($"no object named {focus}");
                    return 1;
                }
                Console.WriteLine($"{focus} is #{found} {model.Get(found)!.Class}");
                focus = found;
            }

            canvas.FocusOn(focus);
            canvas.Highlight(focus);




            window.SelectNode(focus);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }



        if (args.Contains("--expand"))
        {
            foreach (var block in Find<Expander>(window)) block.IsExpanded = true;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }


        if (args.Contains("--fit"))
        {
            if (focus.Length > 0) canvas.FrameRelated(); else canvas.FrameAll();
        }
        else
        {
            canvas.SetZoom(zoom);
        }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();



        drawn.Measure(size);
        drawn.Arrange(new Rect(size));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
        bitmap.Render(drawn);
        bitmap.Save(output);

        var extent = canvas.Extent();
        Console.WriteLine($"        laid out {extent.Wide:0} wide by {extent.Tall:0} tall");
        Console.WriteLine($"{output}: {canvas.DrawnCount} node(s), {canvas.DrawableRouteCount} route(s), " +
                          $"{canvas.StartStateIds.Count} start state(s), zoom {zoom}" +
                          $", layout {canvas.LayoutMode}" +
                          (focus.Length > 0 ? $", focused on #{focus}" : ""));




        var drops = canvas.OwnershipWireDrops().OrderBy(d => d).ToList();
        if (drops.Count > 0)
            Console.WriteLine($"        wires: {drops.Count}, median {drops[drops.Count / 2]:0} tall, " +
                              $"p90 {drops[(int)(drops.Count * 0.9)]:0}, worst {drops[^1]:0}, " +
                              $"{drops.Count(d => d > 2000)} over 2000");
        return 0;
    }

    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--png") return Png(args);

        AppBuilder.Configure<HeadlessApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();

        var window = new MainWindow();
        window.Show();

        Check("the window has a title", "Behaviour Graph Studio", window.Title);

        var tabs = Find<TabControl>(window);
        Check("there is one tab control", 1, tabs.Count);

        var headers = tabs[0].Items.OfType<TabItem>().Select(t => t.Header?.ToString()).ToList();
        Check("tabs", "Tree, Graph, Symbols, Chain, Animation, Playback, Compare", string.Join(", ", headers));



        var canvases = 0;
        var viewports = 0;
        var grids = new System.Collections.Generic.List<HkGrid>();
        var buttons = new System.Collections.Generic.List<string?>();
        for (int i = 0; i < tabs[0].ItemCount; i++)
        {
            tabs[0].SelectedIndex = i;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            canvases += Find<GraphView>(window).Count;
            viewports += Find<SkeletonView>(window).Count;
            grids.AddRange(Find<HkGrid>(window));
            buttons.AddRange(Find<Button>(window).Select(b => b.Content?.ToString()));
        }

        Check("the node canvas exists", 1, canvases);
        Check("the skeleton viewport exists", 1, viewports);
        Check("the tree, symbol, chain, animation, clip and compare grids build without opening tools",
              6, grids.Count);




        Check("collapsed details leave no hidden grids under the canvas", 0, grids.Count(g => !g.IsVisible));
        foreach (string expected in new[]
                 { "Open", "Browse...", "From archive...", "Expand all", "Collapse all", "Check graph", "Save to .hkx", "+ real", "+ event", "Remove", "Set bounds",
                   "Undo", "Redo", "Compare with...", "Check project", "Scripts folder...",
                   "Play", "From selected node", "Fit", "View ▾", "Fit all", "Fit selection", "Create template" })
            CheckTrue($"the {expected} button is there", buttons.Contains(expected));




        {
            tabs[0].SelectedIndex = headers.IndexOf("Graph");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            CheckTrue("the legend stays out of the way until it is asked for", !window.LegendWindowVisible);
            window.OpenLegendForTest();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            CheckTrue("opening Legend creates a separate reference window", window.LegendWindowVisible);

            var said = Find<TextBlock>(window.LegendWindowForTest!).Select(t => t.Text ?? "").ToList();
            foreach (string mark in new[]
                     { "State machine", "State", "Transitions", "Clip", "Blend", "Modifier",
                       "Solid: holds", "Dashed: transition", "Dashed pink: from this state",
                       "any: an event",
                       "Start", "Teal glow: running now", "Red outline", "Amber outline" })
                CheckTrue($"the legend explains {mark}", said.Contains(mark));

            tabs[0].SelectedIndex = 0;
        }


        tabs[0].SelectedIndex = headers.IndexOf("Playback");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Check("the viewport draws nothing before a clip is picked", 0, window.Viewport.DrawnBones);
        CheckTrue("and is not playing", !window.IsPlaying);




        var ticks = Find<CheckBox>(window).Select(c => c.Content?.ToString()).ToList();
        CheckTrue("the reference pose tick is there", ticks.Contains("Reference pose"));
        CheckTrue("and the follow travel tick", ticks.Contains("Follow travel"));

        foreach (var tick in Find<CheckBox>(window))
        {
            tick.IsChecked = true;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        Check("ticking them with nothing loaded still draws nothing", 0, window.Viewport.DrawnBones);

        tabs[0].SelectedIndex = 0;
        CheckTrue("save is disabled until something changes",
            Find<Button>(window).First(b => b.Content?.ToString() == "Save to .hkx").IsEnabled == false);

        foreach (string idle in new[] { "Undo", "Redo" })
            CheckTrue($"{idle} is disabled with nothing loaded",
                Find<Button>(window).First(b => b.Content?.ToString() == idle).IsEnabled == false);



        CheckTrue("the window has no Java setup control",
            !Find<Button>(window).Any(b => b.Content?.ToString() == "Find Java..."));




        foreach (string path in args.Where(System.IO.File.Exists))
        {
            window.Open(path);


            tabs[0].SelectedIndex = headers.IndexOf("Animation");
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



            if (shown.StartsWith("This is a behaviour file", StringComparison.Ordinal))
            {
                tabs[0].SelectedIndex = 2;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                var roles = new[] { "raised here", "listened for here", "referenced here" };
                var said = Find<TextBlock>(window).Select(t => t.Text ?? "")
                    .Where(t => roles.Any(r => t.Contains(r, StringComparison.Ordinal))).ToList();








                CheckTrue($"{name}: the symbols are built from the file itself",
                          window.SymbolGrid.RowCount > 0);
                CheckTrue($"{name}: events say who sends and who listens", said.Count > 0);
                CheckTrue($"{name}: and no row calls an event dead or unused",
                          !said.Any(t => t.Contains("dead", StringComparison.OrdinalIgnoreCase)
                                      || t.Contains("unused", StringComparison.OrdinalIgnoreCase)));

                Console.WriteLine($"        symbols: {window.SymbolGrid.RowCount} rows, " +
                                  $"{said.Count} of them naming a role, e.g. \"{said.FirstOrDefault()}\"" +
                                  (window.LoadedXml.Length == 0 ? "  (native text unavailable)" : ""));
            }




            {
                tabs[0].SelectedIndex = 1;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                int drawn = Find<GraphView>(window).First().DrawnIds.Count;
                Console.WriteLine($"        canvas: {drawn} node(s) drawn");
                CheckTrue($"{name}: the canvas draws the graph", drawn > 0);
            }





            {
                tabs[0].SelectedIndex = 1;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                var navigatorModel = OpenCommonwealth.Services.Hkx.BehaviourGraphModel.Parse(window.LoadedXml);
                CheckTrue($"{name}: graph workspace has no permanent left pane", !window.GraphLeftPanePresent);
                CheckTrue($"{name}: navigator starts hidden with Workspace",
                          !window.WorkspaceVisible);
                window.OpenWorkspaceForTest();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                CheckTrue($"{name}: Workspace opens as a top-level tool window", window.WorkspaceVisible);
                Check("Workspace starts with Machines and Runtime tabs", "Machines, Runtime",
                      string.Join(", ", window.WorkspaceWindowForTest?.TabHeaders ?? Array.Empty<string>()));
                CheckTrue($"{name}: Workspace is a reusable desktop window",
                          window.WorkspaceWindowForTest?.UsesDesktopPresentation == true);
                Check($"{name}: Workspace has exactly one window instance", 1, window.WorkspaceWindowInstances);
                CheckTrue($"{name}: navigator contains only state machines",
                          window.MachineNavigatorIds.Count > 0 &&
                          window.MachineNavigatorIds.All(id =>
                              navigatorModel.Get(id)?.Class == "hkbStateMachine"));
                CheckTrue($"{name}: navigator labels serialize numeric ids",
                          window.MachineNavigatorLabels.Count == window.MachineNavigatorIds.Count &&
                          window.MachineNavigatorLabels.All(text => text.Contains("#", StringComparison.Ordinal)));

                string navigatorMachine = window.MachineNavigatorIds.FirstOrDefault() ?? "";
                if (navigatorMachine.Length > 0)
                {
                    window.SelectMachineForTest(navigatorMachine);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Check($"{name}: Workspace selection reaches the inspector", navigatorMachine,
                          window.SelectedObjectId);
                    Check($"{name}: Workspace selection reaches the canvas", navigatorMachine,
                          window.Canvas.SelectedId);
                    CheckTrue($"{name}: Workspace selection does not enable focus",
                              !window.GraphFocusTreeActive);

                    window.FocusTreeForTest();
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    CheckTrue($"{name}: Focus tree is an explicit action", window.GraphFocusTreeActive);

                    window.ShowFullGraphForTest();
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    CheckTrue($"{name}: Show full graph clears focus mode", !window.GraphFocusTreeActive);

                    window.SetGraphLayoutModeForTest(GraphLayoutMode.StructuredFlow);
                    Check($"{name}: View can select Structured Flow", GraphLayoutMode.StructuredFlow,
                          window.GraphLayoutModeForTest);
                    window.SetGraphLayoutModeForTest(GraphLayoutMode.Freeform);
                    Check($"{name}: View can restore Freeform", GraphLayoutMode.Freeform,
                          window.GraphLayoutModeForTest);
                }

                CheckTrue($"{name}: running machines are marked in the navigator",
                          !window.RunReady || window.MachineNavigatorActiveIds.Count > 0);
                window.FilterMachinesForTest("DefaultRootBehavior");
                Check("Workspace machine filter narrows the real list", 1,
                      window.WorkspaceWindowForTest?.MachineRowCount ?? -1);
                window.FilterMachinesForTest("");
                window.ClearRunForTest();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Check($"{name}: clearing the run clears navigator active markers", 0,
                      window.MachineNavigatorActiveIds.Count);
                window.StartRunForTest();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                CheckTrue($"{name}: the properties pane starts open", window.GraphRightPaneOpen);
                CheckTrue($"{name}: the properties pane is wide enough for its inspector",
                          window.GraphRightPaneWidth >= 360);
                CheckTrue($"{name}: the details drawer starts collapsed", !window.GraphDrawerOpen);
                CheckTrue($"{name}: the drawer default is compact when opened",
                    window.GraphDrawerDefaultHeight <= 110);
                CheckTrue($"{name}: collapsed details do not paint over the graph", !window.GraphDrawerContentsVisible);
                CheckTrue($"{name}: the graph keeps a usable minimum width", window.GraphCenterMinWidth >= 720);
                CheckTrue($"{name}: the canvas host clips graph drawing at the pane boundary",
                          window.GraphCanvasHostClips);
                CheckTrue($"{name}: the properties host clips its own contents",
                          window.GraphPropertiesHostClips);
                CheckTrue($"{name}: Properties rows are constrained to their pane",
                          window.GraphProperties.ContentsFitWidth);
                CheckTrue($"{name}: Properties scroll vertically without horizontal overflow",
                          window.GraphProperties.ScrollsVerticallyOnly);
                CheckTrue($"{name}: the graph toolbar has space above its section labels",
                          window.GraphToolbarTopInset >= 10);
                Check($"{name}: the graph toolbar has deliberate control groups",
                      "View, Edit, Simulation", string.Join(", ", window.GraphToolbarGroups));
                CheckTrue($"{name}: toolbar group labels reserve their own text height",
                          window.GraphToolbarGroupLabelsHaveFixedLineHeight);
                CheckTrue($"{name}: edit tools stay out of the toolbar until requested", !window.GraphEditShelfOpen);
                Check($"{name}: the details drawer has isolated tabs", "Problems, Output",
                      string.Join(", ", window.GraphDrawerTabs));
                CheckTrue($"{name}: Runtime is housed by the Workspace tab",
                          window.WorkspaceWindowForTest?.TabHeaders.Contains("Runtime") == true);

                var diagnosticsButton = Find<Button>(window)
                    .SingleOrDefault(button => button.Content?.ToString() == "Show diagnostics");
                CheckTrue($"{name}: diagnostics has one clear drawer affordance", diagnosticsButton != null);
                CheckTrue($"{name}: no duplicate drawer tab launchers sit above the graph",
                    !Find<Button>(window).Any(button => button.Content?.ToString() is "Problems" or "Output"));
                if (diagnosticsButton != null)
                {
                    diagnosticsButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Check($"{name}: the drawer opens on Problems", "Problems", window.SelectedGraphDrawerTab);
                }

                window.OpenLegendForTest();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                CheckTrue($"{name}: Legend opens independently of Workspace", window.LegendWindowVisible);

                window.SetGraphRightPaneOpen(false);
                CheckTrue($"{name}: the properties pane can collapse", !window.GraphRightPaneOpen);
                window.SetGraphRightPaneOpen(true);
                window.ResizeGraphRightPaneForTest(400);
                CheckTrue($"{name}: the properties pane can reopen and resize",
                          window.GraphRightPaneOpen && window.GraphRightPaneWidth == 400);

                window.SetGraphDrawerOpen(true);
                window.ResizeGraphDrawerForTest(250);
                CheckTrue($"{name}: the details drawer can open and resize",
                          window.GraphDrawerOpen && window.GraphDrawerHeight == 250);

                int workspaceWindows = window.WorkspaceWindowInstances;
                window.CloseWorkspaceForTest();
                CheckTrue($"{name}: closing Workspace leaves the simulation running",
                          !window.WorkspaceVisible && window.RunningCount > 0);
                window.OpenWorkspaceForTest();
                Check($"{name}: reopening Workspace reuses the same window", workspaceWindows,
                      window.WorkspaceWindowInstances);
                CheckTrue($"{name}: reopening Workspace asks the native window to come forward",
                    window.WorkspaceWindowForTest?.PresentationRequests >= 2);

                window.SetGraphDrawerOpen(false);
                window.CloseWorkspaceForTest();
                CheckTrue($"{name}: closing details hides their contents again", !window.GraphDrawerContentsVisible);
            }



            {
                tabs[0].SelectedIndex = 5;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                CheckTrue($"{name}: playback viewport clips mesh drawing", window.PlaybackViewportClips);
            }




            {
                tabs[0].SelectedIndex = 1;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var canvas = Find<GraphView>(window).First();




                string parent = canvas.DrawnIds
                    .Where(id => canvas.OwnedCount(id) >= 3 && canvas.OwnedCount(id) <= canvas.DrawnCount / 4)
                    .OrderByDescending(canvas.OwnedCount)
                    .FirstOrDefault() ?? "";
                if (parent.Length > 0)
                {
                    int was = canvas.DrawnCount;
                    int owned = canvas.OwnedCount(parent);
                    double wasTall = canvas.Extent().Tall;

                    canvas.ToggleCollapse(parent, false);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                    Check($"{name}: folding takes exactly what the node owns off the canvas",
                          was - owned, canvas.DrawnCount);
                    Check($"{name}: and the count it reports is what it is holding", owned,
                          canvas.HiddenCount);
                    CheckTrue($"{name}: the node itself stays, it is what unfolds it",
                              canvas.DrawnIds.Contains(parent));
                    CheckTrue($"{name}: and the room it was taking comes back " +
                              $"({canvas.Extent().Tall:0} against {wasTall:0})",
                              canvas.Extent().Tall < wasTall);

                    canvas.ToggleCollapse(parent, false);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                    Check($"{name}: unfolding brings back exactly what it promised", was,
                          canvas.DrawnCount);
                    Check($"{name}: and nothing is left hidden", 0, canvas.HiddenCount);
                    Check($"{name}: and the canvas is the height it was", wasTall.ToString("F0"),
                          canvas.Extent().Tall.ToString("F0"));
                }
            }



            {
                tabs[0].SelectedIndex = 1;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var canvas = Find<GraphView>(window).First();

                var shared = canvas.DrawnIds.Where(id => canvas.SharedBy(id).Count > 0).ToList();
                Console.WriteLine($"        shared: {shared.Count} node(s) drawn in one of several " +
                                  $"homes, e.g. {string.Join(", ", shared.Take(4).Select(id => "#" + id))}");
                CheckTrue($"{name}: the file shares nodes, so the mark has something to say",
                          shared.Count > 0);

                foreach (string each in shared.Take(50))
                {
                    CheckTrue($"{name}: #{each}'s owner is not listed among its borrowers",
                              !canvas.SharedBy(each).Contains(canvas.OwnerOf(each)));
                    CheckTrue($"{name}: #{each} does not borrow itself",
                              !canvas.SharedBy(each).Contains(each));
                }


                var only = canvas.DrawnIds.FirstOrDefault(id => canvas.SharedBy(id).Count == 0
                                                             && canvas.OwnerOf(id).Length > 0);
                CheckTrue($"{name}: a node with one parent carries no mark", only != null);



                string one = shared[0];
                string tip = canvas.SharedTip(one);
                string ownerName = canvas.NameOf(canvas.OwnerOf(one));

                CheckTrue($"{name}: the tip names the owner as the owner", tip.Contains("(owner)"));
                CheckTrue($"{name}: and names it first",
                          tip.Contains($": {ownerName} (owner)"));
                Check($"{name}: it names every home once", canvas.SharedBy(one).Count + 1,
                      tip.Split(", ").Length);



                string borrower = canvas.SharedBy(one).FirstOrDefault(b => canvas.DrawnIds.Contains(b)) ?? "";
                string branch = borrower.Length > 0 ? canvas.OwnerOf(borrower) : "";

                if (branch.Length > 0 && canvas.DrawnIds.Contains(branch))
                {
                    canvas.ToggleCollapse(branch, false);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                    CheckTrue($"{name}: folding a borrower away leaves the mark on",
                              canvas.SharedBy(one).Count > 0);
                    Check($"{name}: and the tip still names every home, word for word", tip,
                          canvas.SharedTip(one));

                    canvas.ToggleCollapse(branch, false);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Check($"{name}: and unfolding leaves it unchanged", tip, canvas.SharedTip(one));
                }
            }




            {
                tabs[0].SelectedIndex = 1;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var canvas = Find<GraphView>(window).First();

                string parent = canvas.DrawnIds.FirstOrDefault(id => canvas.OwnedCount(id) >= 2) ?? "";
                if (parent.Length > 0)
                {
                    string child = canvas.OwnedIds(parent).First();
                    var wasParent = canvas.PositionOf(parent)!.Value;
                    var wasChild = canvas.PositionOf(child)!.Value;

                    canvas.SelectForTest(new[] { parent, child });
                    Check($"{name}: both are selected", 2, canvas.SelectedIds.Count);
                    Check($"{name}: the primary is the first of them", parent, canvas.SelectedId);


                    var moving = canvas.MovementSet(parent);
                    Check($"{name}: the movement set holds the child once",
                          1, moving.Count(m => m == child));

                    canvas.DragForTest(parent, 40, 25);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                    var nowParent = canvas.PositionOf(parent)!.Value;
                    var nowChild = canvas.PositionOf(child)!.Value;

                    Check($"{name}: the node dragged moved by the drag", "40, 25",
                          $"{nowParent.X - wasParent.X:F0}, {nowParent.Y - wasParent.Y:F0}");
                    Check($"{name}: and the one reached twice moved once, not twice", "40, 25",
                          $"{nowChild.X - wasChild.X:F0}, {nowChild.Y - wasChild.Y:F0}");

                    canvas.SelectForTest(System.Array.Empty<string>());
                }
            }





            {
                tabs[0].SelectedIndex = 1;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var canvas = Find<GraphView>(window).First();

                if (window.RunReady)
                {
                    CheckTrue($"{name}: opening a graph starts it running", window.RunningCount > 0);
                    CheckTrue($"{name}: and lights its active states on the canvas", canvas.ActiveIds.Count > 0);
                    CheckTrue($"{name}: the running list is shown once there is something in it",
                              window.RunningVisible);
                    Console.WriteLine($"        run: {window.RunningCount} machine(s) running, " +
                                      $"{canvas.ActiveIds.Count} state(s) lit, {window.RunEventCount} event(s) to send");





                    if (window.RunEventCount > 0)
                    {
                        var before = canvas.ActiveIds.ToHashSet();
                        bool moved = false;
                        foreach (string ev in window.RunEvents)
                        {
                            window.SendEventForTest(ev);
                            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                            if (!before.SetEquals(canvas.ActiveIds)) { moved = true; break; }
                        }
                        CheckTrue($"{name}: the canvas stays lit after sending events", canvas.ActiveIds.Count > 0);
                        Console.WriteLine($"        run: sending events {(moved ? "moved a state" : "moved nothing, which some graphs do")}");









                        string root = System.IO.Path.GetDirectoryName(
                                          System.IO.Path.GetDirectoryName(path) ?? "") ?? "";
                        bool hasAnimations = root.Length > 0 &&
                                             System.IO.Directory.Exists(System.IO.Path.Combine(root, "Animations"));

                        if (hasAnimations)
                            CheckTrue($"{name}: clips are timed from the animations beside the behaviour",
                                      window.TimedClipCount > 0);

                        Console.WriteLine($"        run: {window.TimedClipCount} clip(s) playing with a " +
                                          "length read from the animation beside the behaviour");




                        if (window.RunBlending)
                        {
                            int steps = 0;
                            while (window.RunBlending && steps < 50) { window.StepForTest(0.1f); steps++; }
                            CheckTrue($"{name}: a transition blend settles as the clock advances", !window.RunBlending);
                            CheckTrue($"{name}: and the canvas is still lit after it settles", canvas.ActiveIds.Count > 0);
                            Console.WriteLine($"        run: a blend settled after {steps} step(s)");
                        }
                    }






                    Console.WriteLine($"        run: {window.RunVariables.Count} variable(s) to set");
                    if (window.RunVariables.Count > 0)
                    {
                        string variable = window.RunVariables[0];
                        window.SetVariableForTest(variable, "7");
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                        CheckTrue($"{name}: setting a variable through the box changes what the run holds",
                                  window.RunValueOf(variable) == 7);



                        window.SetVariableForTest(variable, "not a number");
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        CheckTrue($"{name}: nonsense is refused rather than read as zero",
                                  window.RunValueOf(variable) == 7);
                        CheckTrue($"{name}: and the refusal says which variable was left alone",
                                  window.RunSummary.Contains(variable, StringComparison.Ordinal) &&
                                  window.RunSummary.Contains("not a number", StringComparison.Ordinal));



                        window.SetVariableForTest("noSuchVariableAnywhere", "1");
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        CheckTrue($"{name}: a variable the graph does not declare is not offered",
                                  !window.RunVariables.Contains("noSuchVariableAnywhere"));
                    }



                    CheckTrue($"{name}: transitions held back by a condition are reported, or the line is hidden",
                              window.RunHeldBack > 0 == window.RunHeldBackVisible);



                    var blenderId = OpenCommonwealth.Services.Hkx.BehaviourGraphModel
                        .Parse(window.LoadedXml.Length > 0 ? window.LoadedXml : "")
                        .Objects.FirstOrDefault(o => o.Class == "hkbBlenderGenerator")?.Id;
                    if (window.LoadedXml.Length > 0 && blenderId != null && canvas.DrawnIds.Contains(blenderId))
                    {
                        window.SelectNode(blenderId);
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        var said = Find<TextBlock>(window.GraphProperties).Select(t => t.Text ?? "").ToList();
                        CheckTrue($"{name}: a blender says what it blends",
                                  said.Any(t => t.Contains("what it blends", StringComparison.OrdinalIgnoreCase)));
                        CheckTrue($"{name}: and names its mix",
                                  said.Any(t => t.Contains("Mixes", StringComparison.Ordinal)
                                             || t.Contains("Parametric", StringComparison.Ordinal)));
                        Console.WriteLine($"        run: blender #{blenderId} shows its mix on the panel");
                    }
                }
                else
                {
                    CheckTrue($"{name}: a file with no graph does not pretend to run one",
                              window.RunningCount == 0 && canvas.ActiveIds.Count == 0);
                    Console.WriteLine("        run: not a runnable graph, nothing lit");
                }
            }








            bool isBehaviour = window.LoadedXml.Length > 0 &&
                OpenCommonwealth.Services.Hkx.BehaviourGraphModel.Parse(window.LoadedXml)
                    .Objects.Any(o => o.Class == "hkbStateMachine");
            if (isBehaviour)
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



                    canvas.Activated?.Invoke(node);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    CheckTrue($"{name}: double click still leaves the fields there",
                              Find<TextBox>(window.GraphProperties).Count >= fields.Count);

                    canvas.Highlight(node);
                    Check($"{name}: highlighting one node sticks", node, canvas.HighlightId);
                    canvas.ClearHighlight();
                    Check($"{name}: and clearing it releases the canvas", "", canvas.HighlightId);
                }



                {
                    var model = OpenCommonwealth.Services.Hkx.BehaviourGraphModel.Parse(window.LoadedXml);
                    string machineId = model.Objects
                        .Where(o => o.Class == "hkbStateMachine" && canvas.OwnedCount(o.Id) > 0)
                        .Select(o => o.Id).FirstOrDefault() ?? "";
                    string stateId = model.Objects
                        .Where(o => o.Class == "hkbStateMachineStateInfo" && canvas.OwnerOf(o.Id) == machineId)
                        .Select(o => o.Id).FirstOrDefault() ?? "";
                    string detachedStateId = model.Objects
                        .Where(o => o.Class == "hkbStateMachineStateInfo" && canvas.OwnerOf(o.Id).Length == 0)
                        .Select(o => o.Id).FirstOrDefault() ?? "";
                    string helperId = stateId.Length == 0 ? "" : canvas.OwnedIds(stateId).FirstOrDefault() ?? "";

                    if (machineId.Length > 0 && stateId.Length > 0 && helperId.Length > 0)
                    {
                        var freeformExtent = canvas.Extent();
                        canvas.SetLayoutMode(GraphLayoutMode.StructuredFlow);
                        canvas.FrameAll();
                        canvas.SetZoomForTest(0.75);
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                        Check($"{name}: Structured Flow is selected", GraphLayoutMode.StructuredFlow,
                              canvas.LayoutMode);
                        Check($"{name}: 1600px Structured Flow review starts at overview detail",
                              StructuredFlowDetail.Far, canvas.DetailLevel);
                        CheckTrue($"{name}: overview suppresses state plumbing",
                                  !canvas.IsDrawnAtCurrentDetail(stateId));

                        canvas.SetZoomForTest(0.9);
                        CheckTrue($"{name}: Structured Flow creates machine containers",
                                  canvas.StructuredMachineIds.Contains(machineId));
                        CheckTrue($"{name}: Structured Flow puts a machine above its state",
                                  canvas.PositionOf(machineId)!.Value.Y < canvas.PositionOf(stateId)!.Value.Y);
                        CheckTrue($"{name}: Structured Flow does not expand past Freeform's height",
                                  canvas.Extent().Wide <= freeformExtent.Tall * 1.1);
                        CheckTrue($"{name}: Structured Flow bounds the state in its machine",
                                  canvas.StructuredContainerBounds(machineId) is { } box
                                  && box.Contains(canvas.PositionOf(stateId)!.Value));

                        var nestedPair = model.Objects
                            .Where(machine => machine.Class == "hkbStateMachine")
                            .SelectMany(parent => model.Objects
                                .Where(state => state.Class == "hkbStateMachineStateInfo"
                                                && canvas.OwnerOf(state.Id) == parent.Id)
                                .Select(state => (Parent: parent.Id, State: state.Id,
                                                  Child: model.Follow(state, "generator")?.Id ?? "")))
                            .FirstOrDefault(pair => model.Get(pair.Child)?.Class == "hkbStateMachine");
                        if (nestedPair.Child.Length > 0)
                        {
                            var parentBox = canvas.StructuredContainerBounds(nestedPair.Parent);
                            var childBox = canvas.StructuredContainerBounds(nestedPair.Child);
                            CheckTrue($"{name}: nested machine containers keep separate footprints",
                                      parentBox is { } parent && childBox is { } child
                                      && !parent.Intersects(child));
                            CheckTrue($"{name}: a nested machine sits below its owning state",
                                      canvas.PositionOf(nestedPair.State)!.Value.Y
                                      < canvas.PositionOf(nestedPair.Child)!.Value.Y);
                        }

                        canvas.SetZoomForTest(0.35);
                        Check($"{name}: far Structured Flow detail is selected", StructuredFlowDetail.Far,
                              canvas.DetailLevel);
                        CheckTrue($"{name}: far detail reflows to a compact hierarchy",
                                  canvas.VisibleExtent().Tall < freeformExtent.Tall * 0.35);
                        CheckTrue($"{name}: far detail keeps only top-level machine branches",
                                  canvas.VisibleStructuredMachineIds.Count < canvas.StructuredMachineIds.Count);
                        CheckTrue($"{name}: far detail retains machine tiles",
                                  canvas.IsDrawnAtCurrentDetail(machineId));
                        CheckTrue($"{name}: far detail suppresses helper tiles",
                                  !canvas.IsDrawnAtCurrentDetail(helperId));
                        if (detachedStateId.Length > 0)
                            CheckTrue($"{name}: far detail suppresses detached state plumbing",
                                      !canvas.IsDrawnAtCurrentDetail(detachedStateId));

                        canvas.SetZoomForTest(1.20);
                        Check($"{name}: close Structured Flow detail is selected", StructuredFlowDetail.Close,
                              canvas.DetailLevel);
                        CheckTrue($"{name}: close detail reveals helper tiles",
                                  canvas.IsDrawnAtCurrentDetail(helperId));

                        canvas.SetLayoutMode(GraphLayoutMode.Freeform);
                        canvas.SetZoomForTest(0.9);
                    }
                }




                {
                    var model = OpenCommonwealth.Services.Hkx.BehaviourGraphModel.Parse(window.LoadedXml);
                    string machineId = model.Objects
                        .Where(o => o.Class == "hkbStateMachine")
                        .Select(o => o.Id)
                        .FirstOrDefault(id => canvas.OwnedCount(id) > 0) ?? "";

                    if (machineId.Length > 0)
                    {
                        int drawnBeforeFocus = canvas.DrawnCount;
                        string xmlBeforeFocus = window.LoadedXml;

                        canvas.SelectForTest(new[] { machineId });
                        CheckTrue($"{name}: focus tree accepts a real state machine",
                                  canvas.SetFocusTree(machineId));
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                        CheckTrue($"{name}: focus hides nodes outside its machine tree",
                                  canvas.FocusTreeActive && canvas.DrawnCount < drawnBeforeFocus);
                        CheckTrue($"{name}: focus does not change XML",
                                  string.Equals(xmlBeforeFocus, window.LoadedXml, StringComparison.Ordinal));
                        Check($"{name}: focus records the focused machine", machineId,
                              canvas.FocusTreeRootId);
                        CheckTrue($"{name}: node headers show the serialized object id",
                                  canvas.HeaderTextOf(machineId).EndsWith(" #" + machineId,
                                      StringComparison.Ordinal));

                        string traceSeed = model.Objects
                            .Where(o => o.Class == "hkbClipGenerator" && canvas.DrawnIds.Contains(o.Id))
                            .Select(o => o.Id)
                            .FirstOrDefault()
                            ?? canvas.DrawnIds.FirstOrDefault(id => id != machineId) ?? machineId;

                        canvas.SelectForTest(new[] { traceSeed });
                        CheckTrue($"{name}: static trace accepts a visible selected node",
                                  canvas.Trace(OpenCommonwealth.Services.Hkx.GraphTrace.Direction.Both));
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                        CheckTrue($"{name}: trace keeps the selected seed",
                                  canvas.TraceIds.Contains(traceSeed));
                        CheckTrue($"{name}: focused trace cannot escape visible graph",
                                  canvas.TraceIds.All(id => canvas.DrawnIds.Contains(id)));

                        string unrelated = canvas.DrawnIds.FirstOrDefault(id => !canvas.TraceIds.Contains(id)) ?? "";
                        CheckTrue($"{name}: trace leaves at least one visible node unrelated",
                                  unrelated.Length > 0);
                        if (unrelated.Length > 0)
                            CheckTrue($"{name}: trace dims unrelated visible nodes",
                                      canvas.IsDimmed(unrelated) && canvas.IsTraceDimmed(unrelated));

                        canvas.ClearTrace();
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        Check($"{name}: clearing trace keeps the selection", traceSeed, canvas.SelectedId);
                        CheckTrue($"{name}: clearing trace restores normal emphasis",
                                  !canvas.TraceActive && (unrelated.Length == 0 || !canvas.IsTraceDimmed(unrelated)));

                        canvas.ClearFocusTree();
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        CheckTrue($"{name}: show full graph clears focus mode",
                                  !canvas.FocusTreeActive && canvas.DrawnCount == drawnBeforeFocus);
                    }
                }




                {
                    var model = OpenCommonwealth.Services.Hkx.BehaviourGraphModel.Parse(window.LoadedXml);
                    var routes = OpenCommonwealth.Services.Hkx.StateRoutes.Of(model);

                    Console.WriteLine($"        routes: {canvas.RouteCount} in the file, " +
                                      $"{canvas.DrawableRouteCount} with both ends on the canvas, " +
                                      $"{canvas.NestedRouteCount} nested, " +
                                      $"{canvas.StartStateIds.Count} start state(s)");

                    Check($"{name}: the canvas reads the same routes as the file",
                          routes.Routes.Count, canvas.RouteCount);
                    CheckTrue($"{name}: and there are some to draw", canvas.RouteCount > 0);



                    Check($"{name}: every route has both ends on the canvas",
                          canvas.RouteCount, canvas.DrawableRouteCount);



                    CheckTrue($"{name}: a start state is marked", canvas.StartStateIds.Count > 0);
                    CheckTrue($"{name}: and the node itself knows it is one",
                              canvas.StartStateIds.All(id => !canvas.DrawnIds.Contains(id) || canvas.IsStart(id)));





                    var withWildcards = routes.MachineOfState.Keys
                        .Where(s => canvas.DrawnIds.Contains(s))
                        .FirstOrDefault(s => routes.LeavingState(s).Any(r => r.Wildcard)) ?? "";



                    {
                        canvas.ClearHighlight();
                        int direct = routes.Routes.Count(r => !r.Wildcard &&
                                                              canvas.DrawnIds.Contains(r.FromId) &&
                                                              canvas.DrawnIds.Contains(r.ToId));
                        int marked = canvas.DrawnIds.Count(id => canvas.WildcardsInto(id).Count > 0);
                        int events = canvas.DrawnIds.Sum(id => canvas.WildcardsInto(id).Count);

                        Console.WriteLine($"        wildcards: {marked} state(s) marked, {events} event(s) " +
                                          $"written on them, {canvas.LineCount} line(s) drawn");

                        Check($"{name}: with nothing picked out only direct transitions are lines",
                              direct, canvas.LineCount);
                        CheckTrue($"{name}: and the states a wildcard enters say so on themselves",
                                  marked > 0);



                        var targets = routes.Routes.Where(r => r.Wildcard && canvas.DrawnIds.Contains(r.ToId))
                                                   .Select(r => r.ToId).ToHashSet();
                        CheckTrue($"{name}: every state a wildcard targets is marked",
                                  targets.All(id => canvas.WildcardsInto(id).Count > 0));
                    }

                    if (withWildcards.Length > 0)
                    {
                        var leaving = routes.LeavingState(withWildcards).ToList();
                        int wild = leaving.Count(r => r.Wildcard);

                        Console.WriteLine($"        #{withWildcards}: {leaving.Count} way(s) out, " +
                                          $"{wild} of them the machine's wildcards");

                        CheckTrue($"{name}: a state's ways out include its machine's wildcards", wild > 0);
                        CheckTrue($"{name}: and every one of them leaves that state, not the machine",
                                  leaving.All(r => r.FromId == withWildcards));



                        CheckTrue($"{name}: none of them points back at the state itself",
                                  leaving.All(r => r.ToId != withWildcards));



                        string machineId = routes.MachineOfState[withWildcards];
                        int onMachine = routes.Out.TryGetValue(machineId, out var fromMachine)
                            ? fromMachine.Count(r => r.Wildcard) : 0;
                        CheckTrue($"{name}: rewriting them adds none and drops none",
                                  wild == onMachine || wild == onMachine - 1);
                    }




                    {
                        var extent = canvas.Extent();
                        canvas.ClearHighlight();
                        canvas.FrameAll();
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                        var seen = canvas.VisibleWorld();
                        Console.WriteLine($"        fit all: graph {extent.Wide:0}x{extent.Tall:0}, " +
                                          $"viewport shows {seen.Width:0}x{seen.Height:0}");

                        CheckTrue($"{name}: fit all shows the whole graph across",
                                  seen.Width >= extent.Wide - 1);
                        CheckTrue($"{name}: and the whole of it down",
                                  seen.Height >= extent.Tall - 1);



                        canvas.Highlight(node);
                        canvas.FrameRelated();
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                        var near = canvas.VisibleWorld();
                        CheckTrue($"{name}: fit selection shows less than the whole graph",
                                  near.Width < seen.Width);
                        canvas.ClearHighlight();
                    }



                    var routed = routes.Routes.FirstOrDefault(r => canvas.DrawnIds.Contains(r.FromId) &&
                                                                   canvas.DrawnIds.Contains(r.ToId));
                    if (routed != null)
                    {
                        canvas.Highlight(routed.FromId);
                        CheckTrue($"{name}: highlighting a state keeps what it routes to lit",
                                  !canvas.IsDimmed(routed.ToId));
                        canvas.ClearHighlight();
                    }
                }








                var full = OpenCommonwealth.Services.Hkx.BehaviourGraphModel.Parse(window.LoadedXml);
                string array = OpenCommonwealth.Services.Hkx.HkxTextEdit
                    .IdsOfClass(window.LoadedXml, "hkbStateMachineTransitionInfoArray")
                    .Where(id => canvas.DrawnIds.Contains(id))
                    .OrderByDescending(id => full.Get(id)?.StructLists.GetValueOrDefault("transitions")?.Count ?? 0)
                    .FirstOrDefault() ?? "";

                if (array.Length > 0)
                {
                    var model = full;
                    var summaries = OpenCommonwealth.Services.Hkx.ElementSummary.For(model, array);
                    int flat = OpenCommonwealth.Services.Hkx.HkxTextEdit
                        .ReadParams(window.LoadedXml, array).Count;

                    window.SelectNode(array);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                    var blocks = Find<Expander>(window.GraphProperties);
                    int collapsed = Find<TextBox>(window.GraphProperties).Count;







                    foreach (var block in blocks) block.IsExpanded = true;
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                    var tips = Find<TextBlock>(window.GraphProperties)
                        .Select(t => Avalonia.Controls.ToolTip.GetTip(t) as string)
                        .Where(t => t != null).Select(t => t!).ToList();

                    CheckTrue("a field name carries a tip", tips.Count > 0);
                    CheckTrue("saying where the edit would be written",
                              tips.Any(t => t.StartsWith("transitions[", StringComparison.Ordinal)));
                    CheckTrue("and what the field is",
                              tips.Any(t => t.Contains("a whole number", StringComparison.Ordinal)));

                    var explained = tips.FirstOrDefault(t => t.Contains("Established by:", StringComparison.Ordinal));
                    CheckTrue("a field somebody established says so, and says by what", explained != null);

                    if (explained != null)
                        Console.WriteLine("        a tip reads: " + explained.Replace("\n", " | "));



                    foreach (var block in blocks) block.IsExpanded = false;
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                    Console.WriteLine($"        #{array}: {flat} fields flat, {blocks.Count} element(s), " +
                                      $"{collapsed} box(es) while collapsed");

                    Check($"{name}: one block per element of the array", summaries.Count, blocks.Count);
                    CheckTrue($"{name}: which is fewer things to read than the flat list",
                              blocks.Count < flat);
                    CheckTrue($"{name}: and the boxes stay out of the way until asked for",
                              collapsed < flat);
                    CheckTrue($"{name}: every block starts closed", blocks.All(b => !b.IsExpanded));


                    CheckTrue($"{name}: each block says which event it fires on",
                              blocks.Count == 0 || summaries.Values.All(s => s.Contains("->")));



                    if (blocks.Count > 0)
                    {
                        blocks[0].IsExpanded = true;
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        CheckTrue($"{name}: opening a block shows that element's fields",
                                  Find<TextBox>(window.GraphProperties).Count > collapsed);
                    }
                }




                string boneWeights = OpenCommonwealth.Services.Hkx.HkxTextEdit
                    .IdsOfClass(window.LoadedXml, "hkbBoneWeightArray")
                    .Select(id => new
                    {
                        Id = id,
                        Values = OpenCommonwealth.Services.Hkx.HkxTextEdit
                            .ArrayValues(window.LoadedXml, id, "boneWeights") ?? new List<string>(),
                    })
                    .FirstOrDefault(array => array.Values.Count == 73)?.Id ?? "";
                if (string.Equals(name, "VertibirdBehavior.hkx", StringComparison.OrdinalIgnoreCase))
                    CheckTrue($"{name}: finds the real 73-entry bone weight array", boneWeights.Length > 0);
                if (boneWeights.Length > 0)
                {
                    string field = "boneWeights";
                    var before = OpenCommonwealth.Services.Hkx.HkxTextEdit
                        .ArrayValues(window.LoadedXml, boneWeights, field)!;
                    Check($"{name}: the selected Vertibird bone weight array has 73 entries", 73, before.Count);

                    window.SelectNode(boneWeights);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                    var boxes = Find<TextBox>(window.GraphProperties);
                    var labels = Find<TextBlock>(window.GraphProperties).Select(t => t.Text ?? "").ToList();
                    Check($"{name}: every bone weight has one editable row", before.Count, boxes.Count);

                    string rigPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(path) ?? "", "..", "CharacterAssets", "skeleton.hkx"));
                    if (System.IO.File.Exists(rigPath))
                    {
                        var bones = new OpenCommonwealth.Services.Hkx.HkxBinaryReader().ReadSkeleton(rigPath).BoneNames;
                        CheckTrue($"{name}: a bone weight row resolves the first skeleton name",
                                  bones.Count > 0 && labels.Any(t => t.Contains(bones[0], StringComparison.Ordinal)));
                        CheckTrue($"{name}: a bone weight row resolves a later skeleton name",
                                  bones.Count > 36 && labels.Any(t => t.Contains(bones[36], StringComparison.Ordinal)));
                        Console.WriteLine($"        #{boneWeights}: {before.Count} weights; " +
                                          $"bone 0 = {bones[0]}, bone 36 = {bones[36]}");
                    }
                    else
                    {
                        CheckTrue($"{name}: missing skeleton says why labels are numeric",
                                  labels.Any(t => t.Contains("No skeleton is available", StringComparison.Ordinal)));
                    }

                    string changed = before[0] == "0.125" ? "0.25" : "0.125";
                    var changedValues = before.ToList();
                    changedValues[0] = changed;
                    Check($"{name}: the array writer changes a bone weight", changed,
                          OpenCommonwealth.Services.Hkx.HkxTextEdit
                              .ArrayValues(OpenCommonwealth.Services.Hkx.HkxTextEdit
                                  .SetArrayValues(window.LoadedXml, boneWeights, field, changedValues),
                                  boneWeights, field)![0]);
                    boxes[0].Text = changed;
                    boxes[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(
                        Avalonia.Input.InputElement.LostFocusEvent));
                    window.CommitPendingFields();
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                    window.SelectNode(boneWeights);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    boxes = Find<TextBox>(window.GraphProperties);
                    Check($"{name}: reselecting the array keeps the edited weight", changed, boxes[0].Text ?? "");
                    int objectStart = window.LoadedXml.IndexOf(
                        $"<hkobject class=\"hkbBoneWeightArray\" name=\"#{boneWeights}\"", StringComparison.Ordinal);
                    int valueStart = window.LoadedXml.IndexOf("<hkparam name=\"boneWeights\"", objectStart,
                                                               StringComparison.Ordinal);
                    CheckTrue($"{name}: editing a bone weight changes the loaded document",
                              valueStart >= 0 && window.LoadedXml.IndexOf(changed, valueStart,
                                                                           StringComparison.Ordinal) >= 0);
                    Check($"{name}: reselecting still has every weight row", before.Count, boxes.Count);

                    var scroll = Find<ScrollViewer>(window.GraphProperties).First();
                    scroll.Offset = new Vector(0, scroll.Extent.Height);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    CheckTrue($"{name}: the full bone array scrolls to its final row",
                              scroll.Offset.Y > 0 && boxes[^1].Bounds.Width > 0 && boxes[^1].Bounds.Height > 0);



                    window.Open(path);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                }



                int drawn = canvas.DrawnCount;
                string needle = OpenCommonwealth.Services.Hkx.BehaviourGraphModel
                    .Parse(window.LoadedXml).Get(node)?.Str("name") ?? "";
                if (needle.Length > 0)
                {
                    window.Filter(needle);
                    CheckTrue($"{name}: the filter matches something on the canvas", canvas.MatchCount > 0);
                    CheckTrue($"{name}: and not everything", canvas.MatchCount < drawn);
                    CheckTrue($"{name}: the tree filters to the matches too", window.TreeGrid.RowCount > 0);
                    Console.WriteLine($"        \"{needle}\": {canvas.MatchCount} of {drawn} nodes, " +
                                      $"{window.TreeGrid.RowCount} tree rows");

                    window.Filter("no-such-node-xyzzy");
                    Check($"{name}: nonsense matches nothing rather than everything", 0, canvas.MatchCount);

                    window.Filter("");
                    Check($"{name}: clearing it releases the canvas", 0, canvas.MatchCount);
                    Check($"{name}: and nothing was dropped from the canvas", drawn, canvas.DrawnCount);
                }




                string host = OpenCommonwealth.Services.Hkx.HkxTextEdit
                    .IdsOfClass(window.LoadedXml, "hkbStateMachineStateInfo")
                    .FirstOrDefault(id => canvas.DrawnIds.Contains(id)) ?? "";
                if (host.Length > 0)
                {
                    var before = canvas.DrawnIds.ToHashSet();
                    var dropped = new Point(4321, 765);

                    canvas.AddRequested?.Invoke(host, "generator", dropped);
                    var item = canvas.ContextMenu?.ItemsSource?.OfType<MenuItem>()
                        .FirstOrDefault(m => m.Header?.ToString() == "Add clip");
                    CheckTrue($"{name}: dragging out to empty canvas offers a node to add", item != null);

                    item?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                    string added = canvas.DrawnIds.FirstOrDefault(id => !before.Contains(id)) ?? "";
                    CheckTrue($"{name}: and the node is created", added.Length > 0);
                    Check($"{name}: where it was dropped, not at the end of the graph",
                          dropped, canvas.PositionOf(added) ?? new Point(-1, -1));

                    var owner = OpenCommonwealth.Services.Hkx.BehaviourGraphModel
                        .Parse(window.LoadedXml).Get(host);
                    Check($"{name}: wired into the slot the drag came from", added, owner?.Ref("generator") ?? "");



                    string afterAdd = window.LoadedXml;
                    var save = Find<Button>(window).First(b => b.Content?.ToString() == "Save to .hkx");
                    var undo = Find<Button>(window).First(b => b.Content?.ToString() == "Undo");
                    var redo = Find<Button>(window).First(b => b.Content?.ToString() == "Redo");

                    CheckTrue($"{name}: undo is offered once something has changed", undo.IsEnabled);
                    Click(undo);
                    CheckTrue($"{name}: undo takes the node off the canvas", !canvas.DrawnIds.Contains(added));
                    CheckTrue($"{name}: and the document is back to what was opened",
                              window.LoadedXml.Length > 0 && window.LoadedXml != afterAdd);
                    CheckTrue($"{name}: so there is nothing left to save", !save.IsEnabled);
                    CheckTrue($"{name}: and nothing left to undo", !undo.IsEnabled);

                    CheckTrue($"{name}: redo is offered after an undo", redo.IsEnabled);
                    Click(redo);
                    Check($"{name}: redo puts the document back exactly", afterAdd, window.LoadedXml);
                    CheckTrue($"{name}: and the node is drawn again", canvas.DrawnIds.Contains(added));
                    CheckTrue($"{name}: with the unsaved marker back on", save.IsEnabled);

                    Click(undo);

                    PasteOnACopy(window, path, name);


                    window.Open(path);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();



                    string said = window.CompareLoadedWith(path);
                    if (said.Length == 0)
                        Console.WriteLine("        compare: skipped, the window opened read only");
                    else
                        CheckTrue($"{name}: a file compared with itself reports no difference",
                                  said.Contains("same objects", StringComparison.Ordinal));



                    canvas.Highlight(added);
                    window.Filter("clip");
                    window.Open(path);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Check($"{name}: reopening drops the old highlight", "", canvas.HighlightId);
                    Check($"{name}: and the old filter", 0, canvas.MatchCount);
                    CheckTrue($"{name}: and does not pin nodes where the last file's were",
                              canvas.PositionOf(host) != dropped);
                }
            }




            if (window.AnimationFrameCount > 0)
            {


                tabs[0].SelectedIndex = headers.IndexOf("Animation");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

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




                int unfiltered = window.AnimationGrid.RowCount;
                window.FilterBones("no-such-bone-xyzzy");
                int filtered = window.AnimationGrid.RowCount;
                CheckTrue($"{name}: a filter matching nothing leaves the annotations and one line saying so",
                          filtered < unfiltered && filtered <= window.AnimationAnnotationCount + 1);
                window.FilterBones("");
                Check($"{name}: clearing the filter brings the tracks back", unfiltered, window.AnimationGrid.RowCount);






                CheckTrue($"{name}: nothing is picked before a row is", !window.AnimationEdited);

                if (window.PickFrame(0, 0))
                {
                    CheckTrue($"{name}: picking a frame fills the position box",
                              window.FramePositionText.Length > 0);
                    CheckTrue($"{name}: and the rotation box", window.FrameRotationText.Length > 0);
                    CheckTrue($"{name}: and says which frame it is",
                              window.FrameEditAnswer.Contains("frame 0", StringComparison.Ordinal));

                    window.TypeFramePosition("11.5, -22.25, 33.75");
                    var moved = window.FramePosition(0, 0);
                    CheckTrue($"{name}: typing a position moves that frame",
                              Math.Abs(moved.X - 11.5f) < 0.001f && Math.Abs(moved.Y + 22.25f) < 0.001f &&
                              Math.Abs(moved.Z - 33.75f) < 0.001f);
                    CheckTrue($"{name}: and the file counts as changed", window.AnimationEdited);



                    window.TypeFramePosition("1, 2");
                    var still = window.FramePosition(0, 0);
                    CheckTrue($"{name}: a position short of three numbers is refused",
                              Math.Abs(still.X - 11.5f) < 0.001f);
                    CheckTrue($"{name}: and it says what it wanted",
                              window.FrameEditAnswer.Contains("three numbers", StringComparison.Ordinal));

                    SaveOnACopy(window, path, name);



                    window.Open(path);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                }
            }




            tabs[0].SelectedIndex = headers.IndexOf("Playback");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();




            if (window.LoadedXml.Length > 0)
            {
                tabs[0].SelectedIndex = headers.IndexOf("Tree");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                string fromTree = "";
                foreach (string clip in OpenCommonwealth.Services.Hkx.HkxTextEdit
                             .IdsOfClass(window.LoadedXml, "hkbClipGenerator"))
                {
                    window.SelectFromTree(clip);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    if (window.PoseFrameCount > 0) { fromTree = clip; break; }
                }

                if (fromTree.Length > 0)
                    Console.WriteLine($"        tree selection loaded a pose from #{fromTree}");
                CheckTrue($"{name}: picking a clip in the tree loads the pose too, not only the canvas",
                          fromTree.Length == 0 || window.PoseFrameCount > 0);

                tabs[0].SelectedIndex = headers.IndexOf("Playback");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }




            if (window.PoseFrameCount == 0 && window.LoadedXml.Length > 0)
                foreach (string clip in OpenCommonwealth.Services.Hkx.HkxTextEdit
                             .IdsOfClass(window.LoadedXml, "hkbClipGenerator"))
                {
                    window.SelectNode(clip);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    if (window.PoseFrameCount > 0) break;
                }

            if (window.PoseFrameCount == 0)
            {
                Console.WriteLine($"        playback: {window.PlaybackSummary}");
            }
            else
            {
                int frames = window.PoseFrameCount;
                Console.WriteLine($"        playback: {window.PlaybackSummary}");

                CheckTrue($"{name}: the viewport drew the whole skeleton", window.Viewport.DrawnBones > 1);

                window.ScrubTo(0);
                var atStart = window.PoseNow!;
                window.ScrubTo(frames - 1);
                var atEnd = window.PoseNow!;

                window.ScrubTo(frames / 2);
                var atMiddle = window.PoseNow!;
                window.ScrubTo(frames - 1);

                Check($"{name}: scrubbing to the last frame lands on it", frames - 1, window.PoseFrame);





                CheckTrue($"{name}: and the pose moves as the clip runs",
                          frames < 2 ||
                          OpenCommonwealth.Services.Hkx.AnimationPose.Distance(atStart, atEnd) > 0.01f ||
                          OpenCommonwealth.Services.Hkx.AnimationPose.Distance(atStart, atMiddle) > 0.01f);
                CheckTrue($"{name}: with no bone landing on a NaN",
                          atEnd.Bones.All(b => !float.IsNaN(b.Position.X) && !float.IsNaN(b.Position.Y)
                                                                          && !float.IsNaN(b.Position.Z)));

                window.ScrubTo(-10);
                Check($"{name}: scrubbing before the start clamps", 0, window.PoseFrame);
                window.ScrubTo(frames + 500);
                Check($"{name}: and past the end clamps", frames - 1, window.PoseFrame);


                var play = Find<Button>(window).First(b => b.Content?.ToString() is "Play" or "Pause");
                window.ScrubTo(0);




                ClickOnly(play);
                CheckTrue($"{name}: play starts a clock", window.IsPlaying || frames <= 1);
                ClickOnly(play);
                CheckTrue($"{name}: and pressing it again stops it", !window.IsPlaying);



                CheckTrue($"{name}: scrubbing leaves the document alone",
                          !Find<Button>(window).First(b => b.Content?.ToString() == "Save to .hkx").IsEnabled);
            }
        }






        foreach (string path in args.Where(System.IO.File.Exists))
        {



            if (window.LoadedXml.Length == 0)
            {
                Console.WriteLine("        editing: skipped, the window opened without a text form");
                continue;
            }

            string clip = OpenCommonwealth.Services.Hkx.HkxTextEdit
                .IdsOfClass(window.LoadedXml, "hkbClipGenerator").FirstOrDefault() ?? "";





            bool graph = OpenCommonwealth.Services.Hkx.HkxTextEdit
                .IdsOfClass(window.LoadedXml, "hkbBehaviorGraph").Count > 0;

            if (graph) CheckTrue("a clip to edit was found", clip.Length > 0);

            if (clip.Length == 0)
            {
                Console.WriteLine("        editing: skipped, this file holds no clip generator to type into");
                continue;
            }

            window.SelectNode(clip);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();




            var box = FieldNamed(window.GraphProperties, "playbackSpeed");
            CheckTrue("and a field to type in", box != null);
            if (box == null) continue;

            string typed = "0.375";
            box.Text = typed;
            CheckTrue("typing alone does not reach the document",
                      !window.LoadedXml.Contains(typed, StringComparison.Ordinal));

            window.CommitPendingFields();
            CheckTrue("saving takes what was still being typed with it",
                      window.LoadedXml.Contains(typed, StringComparison.Ordinal));
        }

        StandaloneAnimationFillsTheClipList();
        StandaloneAnimationSkeletonSearchesFromAnimationsRoot();

        ArchiveBrowserBuilds();

        Console.WriteLine($"\n{_ran} checks, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }







    private static void StandaloneAnimationFillsTheClipList()
    {
        const string path = "dist/examples/Dogmeat/Animations/IdleOutroDogmeatWalkForward.hkx";
        if (!System.IO.File.Exists(path))
        {
            Console.WriteLine($"        clip list: skipped, {path} is not here");
            return;
        }

        var window = new MainWindow();
        window.Show();
        window.Open(path);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var tabs = Find<TabControl>(window)[0];
        tabs.SelectedIndex = tabs.Items.OfType<TabItem>().ToList()
                                 .FindIndex(t => t.Header?.ToString() == "Playback");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Check("a standalone animation puts itself in the clip list", 1, window.ClipGrid.RowCount);



        CheckTrue("and the row is picked, so Playback behaves as if a clip had been chosen",
                  window.ClipGrid.HasSelection);



        var said = Find<TextBlock>(window.ClipGrid).Select(t => t.Text ?? "").ToList();
        CheckTrue("and it names the animation that is loaded",
                  said.Contains("IdleOutroDogmeatWalkForward"));
        CheckTrue($"and says how long it runs ({string.Join(" | ", said)})",
                  said.Contains($"11.20s, {window.AnimationFrameCount} frames"));
        CheckTrue("and the file summary calls it an animation",
                  Find<TextBlock>(window).Any(t => (t.Text ?? "").Contains("an animation, not a behaviour",
                                                                  StringComparison.Ordinal)));
    }

    private static void StandaloneAnimationSkeletonSearchesFromAnimationsRoot()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uismoke-animation-root-search");
        OpenCommonwealth.Services.Hkx.HkxTextEdit.ResetDirectory(root);

        string character = System.IO.Path.Combine(root, "Character");
        string assets = System.IO.Path.Combine(character, "CharacterAssets");
        System.IO.Directory.CreateDirectory(assets);
        string skeleton = System.IO.Path.Combine(assets, "skeleton.hkx");
        System.IO.File.WriteAllText(skeleton, "fixture");

        string shallow = System.IO.Path.Combine(character, "Animations", "kziitd", "MyAnimation", "MWOW.hkx");
        string nested = System.IO.Path.Combine(character, "Animations", "kziitd", "MyAnimation", "01", "MWOW.hkx");
        string deep = System.IO.Path.Combine(character, "Animations", "kziitd", "MyAnimation", "01", "a", "b", "c", "MWOW.hkx");
        foreach (string file in new[] { shallow, nested, deep })
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            System.IO.File.WriteAllText(file, "fixture");
        }

        string unrelated = System.IO.Path.Combine(root, "CharacterAssets");
        System.IO.Directory.CreateDirectory(unrelated);
        System.IO.File.WriteAllText(System.IO.Path.Combine(unrelated, "wrong.hkx"), "fixture");
        string outside = System.IO.Path.Combine(root, "Other", "AnimationsElsewhere", "MWOW.hkx");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outside)!);
        System.IO.File.WriteAllText(outside, "fixture");

        Check("a shallow animation finds its character skeleton", assets,
              MainWindow.FindSiblingSkeletonFolder(shallow) ?? "");
        Check("one extra animation folder finds the same skeleton", assets,
              MainWindow.FindSiblingSkeletonFolder(nested) ?? "");
        Check("several extra animation folders find the same skeleton", assets,
              MainWindow.FindSiblingSkeletonFolder(deep) ?? "");
        Check("an unrelated higher CharacterAssets folder is not selected", "",
              MainWindow.FindSiblingSkeletonFolder(outside) ?? "");

        const string sampleAnimation = "dist/examples/Dogmeat/Animations/IdleOutroDogmeatWalkForward.hkx";
        const string sampleSkeleton = "dist/examples/Dogmeat/CharacterAssets/skeleton.hkx";
        if (!System.IO.File.Exists(sampleAnimation) || !System.IO.File.Exists(sampleSkeleton)) return;

        System.IO.File.Copy(sampleSkeleton, skeleton, true);
        System.IO.File.Copy(sampleAnimation, shallow, true);
        System.IO.File.Copy(sampleAnimation, nested, true);
        var window = new MainWindow();
        window.Show();
        window.Open(nested);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        CheckTrue("the extra nested animation has a pose to render", window.PoseNow != null);
    }






















    private static void PasteOnACopy(MainWindow window, string path, string name)
    {
        string copy = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "uismoke-paste-" + System.IO.Path.GetFileName(path));

        try
        {
            System.IO.File.Copy(path, copy, true);
            System.IO.File.Delete(copy + ".bak");
        }
        catch (System.IO.IOException) { return; }

        window.Open(copy);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        string state = OpenCommonwealth.Services.Hkx.HkxTextEdit
            .IdsOfClass(window.LoadedXml, "hkbStateMachineStateInfo").FirstOrDefault() ?? "";
        if (state.Length == 0) return;

        CheckTrue($"{name}: paste is not offered until something has been copied", !window.CanPaste);

        window.SelectNode(state);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        CheckTrue($"{name}: the slot list offers leaving a paste unattached",
                  window.PasteSlots.Contains("(leave it unattached)"));
        CheckTrue($"{name}: and a slot on the selected node to hang it off",
                  window.PasteSlots.Count > 1);

        int was = new OpenCommonwealth.Services.Hkx.PackfileObjects(
            OpenCommonwealth.Services.Hkx.PackfileImage.Read(copy)).Instances.Count;

        window.CopyForTest();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Console.WriteLine("        " + window.ClipSummary);
        CheckTrue($"{name}: copying says what it took", window.ClipSummary.StartsWith("Holding #", StringComparison.Ordinal));
        CheckTrue($"{name}: and paste is offered afterwards", window.CanPaste);

        window.PasteForTest("(leave it unattached)");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Console.WriteLine("        " + window.PasteAnswer);
        CheckTrue($"{name}: pasting keeps the file before it as a .bak", System.IO.File.Exists(copy + ".bak"));
        CheckTrue($"{name}: and says what it pasted",
                  window.PasteAnswer.Contains("object(s) copied", StringComparison.Ordinal));

        int now = new OpenCommonwealth.Services.Hkx.PackfileObjects(
            OpenCommonwealth.Services.Hkx.PackfileImage.Read(copy)).Instances.Count;
        CheckTrue($"{name}: and the file on disk holds more objects than it did ({was} to {now})", now > was);

        TemplatesOnACopy(window, copy, name);
    }






    private static void TemplatesOnACopy(MainWindow window, string copy, string name)
    {


        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uismoke-templates");
        if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true);
        OpenCommonwealth.Services.Hkx.TemplateStore.Folder = folder;





        string clip = OpenCommonwealth.Services.Hkx.HkxTextEdit
            .IdsOfClass(window.LoadedXml, "hkbClipGenerator").FirstOrDefault() ?? "";
        if (clip.Length == 0) return;

        window.SelectNode(clip);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        window.SaveTemplateForTest("Smoke Shape");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Console.WriteLine("        " + window.PasteAnswer);




        if (window.TemplateNames.Count == 0)
        {
            CheckTrue($"{name}: a shape that cannot leave its file says so and names what it shares",
                      window.PasteAnswer.Contains("shares", StringComparison.Ordinal) &&
                      window.PasteAnswer.Contains("owns everything below it", StringComparison.Ordinal));
            return;
        }

        CheckTrue($"{name}: keeping a shape puts it in the template list", window.TemplateNames.Count == 1);
        CheckTrue($"{name}: and offers to apply it", window.CanApplyTemplate);

        window.ChooseTemplateForTest(window.TemplateNames[0]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Console.WriteLine("        " + window.PasteAnswer);




        CheckTrue($"{name}: choosing one says whether it fits this file before anything is applied",
                  window.PasteAnswer.Contains("already declared here", StringComparison.Ordinal) ||
                  window.PasteAnswer.Contains("Before this can go in", StringComparison.Ordinal));

        int was = new OpenCommonwealth.Services.Hkx.PackfileObjects(
            OpenCommonwealth.Services.Hkx.PackfileImage.Read(copy)).Instances.Count;

        window.ApplyTemplateForTest();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Console.WriteLine("        " + window.PasteAnswer);

        int now = new OpenCommonwealth.Services.Hkx.PackfileObjects(
            OpenCommonwealth.Services.Hkx.PackfileImage.Read(copy)).Instances.Count;



        if (window.PasteAnswer.StartsWith("Applied", StringComparison.Ordinal))
            CheckTrue($"{name}: applying a template adds objects to the file ({was} to {now})", now > was);
        else
            CheckTrue($"{name}: or says what to declare first and leaves the file alone",
                      window.PasteAnswer.Contains("declare", StringComparison.Ordinal) && now == was);
    }

    private static void SaveOnACopy(MainWindow window, string path, string name)
    {
        string copy = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "uismoke-save-" + System.IO.Path.GetFileName(path));

        try
        {
            System.IO.File.Copy(path, copy, true);
            System.IO.File.Delete(copy + ".bak");
        }
        catch (System.IO.IOException) { return; }

        window.Open(copy);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        if (!window.PickFrame(0, 0)) return;

        window.TypeFramePosition("7.25, -8.5, 9.75");
        window.PressSaveAnimation();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Console.WriteLine("        " + window.FrameEditAnswer);

        CheckTrue($"{name}: saving keeps the original as a .bak", System.IO.File.Exists(copy + ".bak"));
        CheckTrue($"{name}: and says the file was written",
                  window.FrameEditAnswer.StartsWith("Saved ", StringComparison.Ordinal));
        CheckTrue($"{name}: and nothing is left unsaved afterwards", !window.AnimationEdited);



        var before = new OpenCommonwealth.Services.Hkx.HkxBinaryReader().ReadAnimation(path);
        var written = new OpenCommonwealth.Services.Hkx.HkxBinaryReader().ReadAnimation(copy);






        bool wasSpline = before.AnimationClass == "hkaSplineCompressedAnimation";
        Check($"{name}: the saved file keeps its kind where it can be re-encoded",
              wasSpline ? "hkaSplineCompressedAnimation" : "hkaInterleavedUncompressedAnimation",
              written.AnimationClass);

        float editLimit = wasSpline ? 0.05f : 0.001f;
        float elsewhereLimit = wasSpline ? 0.05f : 0.001f;

        var landed = written.Tracks[0].Translations[0];
        float editDrift = (landed - new System.Numerics.Vector3(7.25f, -8.5f, 9.75f)).Length();
        CheckTrue($"{name}: and holds the frame that was typed ({editDrift:F4})", editDrift < editLimit);



        float worst = 0;
        for (int t = 0; t < before.NumTracks; t++)
            for (int f = 0; f < before.NumFrames; f++)
            {
                if (t == 0 && f == 0) continue;
                worst = Math.Max(worst,
                    (before.Tracks[t].Translations[f] - written.Tracks[t].Translations[f]).Length());
            }

        Console.WriteLine($"        saved {written.AnimationClass}, every other frame moved at most {worst:E2}");
        CheckTrue($"{name}: leaving every other frame where it was ({worst:F4})", worst < elsewhereLimit);
    }

    private static void ArchiveBrowserBuilds()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uismoke-archive.ba2");
        WriteArchive(path, new[]
        {
            "Meshes/Actors/Canine/Behaviors/CanineRoot.hkx",
            "Meshes/Actors/Character/Behaviors/Behavior.hkx",
            "Meshes/Actors/Character/CharacterAssets/skeleton.nif",
        });

        using var archive = OpenCommonwealth.Services.Archive.Ba2.Open(path);
        var browser = new ArchiveBrowser(archive, ".hkx");
        browser.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var lists = Find<ListBox>(browser);
        var boxes = Find<TextBox>(browser);
        Check("the browser has a list", 1, lists.Count);
        Check("and a filter box", 1, boxes.Count);



        Check("only the two behaviours are offered", 2, lists[0].ItemCount);

        boxes[0].Text = "canine behaviors";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Check("typing words in any order narrows it", 1, lists[0].ItemCount);

        boxes[0].Text = "mirelurk";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Check("and a word nothing matches empties it", 0, lists[0].ItemCount);
        CheckTrue("with nothing chosen while the list is empty", browser.Chosen == null);

        browser.Close();
        System.IO.File.Delete(path);
    }



    private static void WriteArchive(string path, string[] names)
    {
        using var stream = System.IO.File.Create(path);
        using var writer = new System.IO.BinaryWriter(stream);

        var body = System.Text.Encoding.ASCII.GetBytes("file");
        long at = 24 + 36 * names.Length;
        long nameTableAt = at + body.Length * names.Length;

        writer.Write(new[] { 'B', 'T', 'D', 'X' });
        writer.Write(1u);
        writer.Write(new[] { 'G', 'N', 'R', 'L' });
        writer.Write((uint)names.Length);
        writer.Write((ulong)nameTableAt);

        for (int i = 0; i < names.Length; i++)
        {
            writer.Write(0u); writer.Write(0u); writer.Write(0u); writer.Write(0u);
            writer.Write((ulong)(at + i * body.Length));
            writer.Write(0u);
            writer.Write((uint)body.Length);
            writer.Write(0u);
        }

        foreach (var _ in names) writer.Write(body);

        foreach (string name in names)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(name.Replace('/', '\\'));
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }
    }


    private static TextBox? FieldNamed(Visual panel, string name)
    {
        foreach (var row in Find<Grid>(panel))
        {
            var label = row.Children.OfType<TextBlock>().FirstOrDefault(text => Grid.GetColumn(text) == 0);
            if (label?.Text != name) continue;
            return row.Children.OfType<TextBox>().FirstOrDefault(box => Grid.GetColumn(box) == 1);
        }
        return null;
    }



    private static void ClickOnly(Button button)
    {
        button.Command?.Execute(button.CommandParameter);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
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
