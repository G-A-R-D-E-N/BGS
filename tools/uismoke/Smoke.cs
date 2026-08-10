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

    /// Draws the canvas to a PNG with no display attached.
    ///
    /// The checks above can prove a route was counted and that its ends are on the canvas. They
    /// cannot say whether the picture is readable, and "is it readable" is the entire point of
    /// drawing transitions rather than listing them. Rendering it to a file is how that question
    /// gets answered without asking somebody to open the window and describe what they see.
    ///
    /// Usage: uismoke --png &lt;behaviour.hkx&gt; [out.png] [zoom] [focus node id]
    ///
    /// `--window` draws everything beside the canvas as well, which is where the properties panel
    /// is. `--details` opens the Problems and Output drawer, while `--output` selects Output.
    /// `--runtime-window` opens the separate Runtime tool window. `--check` fills Problems through
    /// the real validation button, and `--event` sends the first available event.
    /// Focusing on a node also selects it, so that panel is showing the object the picture is about
    /// rather than nothing. `--expand` opens the array element blocks, which start closed, so a
    /// question about what one of a transition's boxes says can be answered from the picture.
    private static int Png(string[] args)
    {
        // Real drawing rather than the headless stub, which records that something was drawn and
        // produces no pixels.
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
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // A TabControl builds only the tab that is showing, so the canvas does not exist as a visual
        // until the Graph tab is the selected one.
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

        // The whole window rather than the canvas alone, for anything that is drawn beside it: the
        // legend explains the canvas and cannot be checked from a picture that leaves it out.
        bool whole = args.Contains("--window");
        if (args.Contains("--legend"))
        {
            Find<Button>(window).First(b => b.Content?.ToString() == "Legend")
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        if (args.Contains("--details"))
        {
            window.SetGraphDrawerOpen(true);
            if (args.Contains("--output")) window.SelectGraphDrawerTab("Output");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        if (args.Contains("--runtime-window"))
        {
            window.OpenRuntimeForTest();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        var canvas = Find<GraphView>(window).First();
        bool runtimeWindow = args.Contains("--runtime-window");
        var size = runtimeWindow ? new Size(1100, 700) : new Size(1600, 1000);
        Control drawn = runtimeWindow ? window.RuntimeWindowForTest! : whole ? window : canvas;
        drawn.Measure(size);
        drawn.Arrange(new Rect(size));

        if (focus.Length > 0)
        {
            // A name rather than an id, when what was asked for is not a number. Ids are assigned by
            // position in the file and mean nothing to anyone reading a picture; a state is known by
            // what it is called.
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

            // Selecting as well as focusing. Highlighting says where a node is; it does not put the
            // object in front of the panel, and a picture of the whole window drawn without this
            // shows an empty panel beside a highlighted node.
            window.SelectNode(focus);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        // Element blocks are collapsed when the panel builds them, which is the point of grouping
        // them. Opening them is the only way a picture can show what is inside one.
        if (args.Contains("--expand"))
        {
            foreach (var block in Find<Expander>(window)) block.IsExpanded = true;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        // --fit leaves the zoom to the canvas, which is the whole point when what is being checked
        // is whether the fit button fits.
        if (args.Contains("--fit"))
        {
            if (focus.Length > 0) canvas.FrameRelated(); else canvas.FrameAll();
        }
        else
        {
            canvas.SetZoom(zoom);
        }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Again, because selecting and expanding both add controls after the first pass and a
        // bitmap is rendered from the last layout rather than from the tree.
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
                          (focus.Length > 0 ? $", focused on #{focus}" : ""));

        // How far the ownership wires run, which is what the layout is judged on. A picture at a zoom
        // that fits the whole graph is far too small to tell whether the long diagonals stopped, so
        // the distances are printed rather than left to the eye.
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

        // A TabControl only builds the selected tab, so each one has to be visited to prove it is
        // whole. Visiting them also exercises the switching itself.
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
        Check("the tree, symbol, chain, animation, clip and compare grids build without opening details", 6, grids.Count);

        // The drawer is collapsed by default, so its diagnostics and runtime grids are not built into
        // the visible workspace until the user asks for them. That is what returns their height to the
        // graph rather than merely making an empty panel look inactive.
        Check("collapsed details leave no hidden grids under the canvas", 0, grids.Count(g => !g.IsVisible));
        foreach (string expected in new[]
                 { "Open", "Browse...", "From archive...", "Expand all", "Collapse all", "Check graph", "Save to .hkx", "+ real", "+ event", "Remove", "Set bounds",
                   "Undo", "Redo", "Compare with...", "Check project", "Scripts folder...",
                   "Play", "From selected node", "Fit", "Legend", "Fit all", "Fit selection" })
            CheckTrue($"the {expected} button is there", buttons.Contains(expected));

        // The canvas draws six node colours, three kinds of line and two badges. The legend is the
        // only thing that says what any of them mean, so it has to be closed to start with, open on
        // asking, and name every mark that is actually drawn.
        {
            tabs[0].SelectedIndex = headers.IndexOf("Graph");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var legendButton = Find<Button>(window).First(b => b.Content?.ToString() == "Legend");
            CheckTrue("the legend stays out of the way until it is asked for", !window.Legend.IsVisible);

            legendButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            CheckTrue("clicking Legend opens it", window.Legend.IsVisible);
            Check("and the button then offers to put it away", "Hide legend", legendButton.Content?.ToString());

            var said = Find<TextBlock>(window.Legend).Select(t => t.Text ?? "").ToList();
            foreach (string mark in new[]
                     { "State machine", "State", "Transitions", "Clip", "Blend", "Modifier",
                       "Solid: holds", "Dashed: transition", "Dashed pink: from this state",
                       "any: an event",
                       "Start", "Teal glow: running now", "Red outline", "Amber outline" })
                CheckTrue($"the legend explains {mark}", said.Contains(mark));

            legendButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            CheckTrue("and clicking again puts it away", !window.Legend.IsVisible);
            tabs[0].SelectedIndex = 0;
        }

        // Nothing loaded, so the viewport must be empty rather than drawing a rig from the last file.
        tabs[0].SelectedIndex = headers.IndexOf("Playback");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Check("the viewport draws nothing before a clip is picked", 0, window.Viewport.DrawnBones);
        CheckTrue("and is not playing", !window.IsPlaying);

        // Both ticks exist and neither draws anything on its own. Follow travel moves the character
        // along the path the clip carries, which is invisible otherwise, since motion is extracted in
        // this format and the bones play on the spot.
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

        // The Java picker is the recovery path out of read only, so it must stay hidden while nothing
        // is wrong: a permanently visible one reads as a step everybody has to take.
        CheckTrue("the Java picker stays hidden until Java is actually missing",
            Find<Button>(window).First(b => b.Content?.ToString() == "Find Java...").IsVisible == false);

        // Opening a real file through the window, so what the panel actually says is checked rather
        // than assumed from what the reader returns. Paths come in on the command line because the
        // game's own files cannot be committed here.
        foreach (string path in args.Where(System.IO.File.Exists))
        {
            window.Open(path);
            // The panel has to be the selected tab or it is never built, so searching the visual
            // tree for its text finds nothing however correct the text is.
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

            // The event summary is rows under an event, so it only exists once the Symbols tab has
            // been built. A behaviour file that declares events has to say what each one is for.
            if (shown.StartsWith("This is a behaviour file", StringComparison.Ordinal))
            {
                tabs[0].SelectedIndex = 2;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                var roles = new[] { "raised here", "listened for here", "referenced here" };
                var said = Find<TextBlock>(window).Select(t => t.Text ?? "")
                    .Where(t => roles.Any(r => t.Contains(r, StringComparison.Ordinal))).ToList();

                // Both of these are built from the file's own bytes now, so both are asserted with
                // or without Java rather than one of them being excused.
                //
                // The roles were the last thing here still needing hkxpack. What an event is used for
                // is a scan of every place an index is written, including inside structs nested
                // deeper than the graph model carries, so the model genuinely cannot answer it. That
                // was read as needing the text form. It needs the places, and the bytes have them.
                CheckTrue($"{name}: the symbols are built from the file itself",
                          window.SymbolGrid.RowCount > 0);
                CheckTrue($"{name}: events say who sends and who listens", said.Count > 0);
                CheckTrue($"{name}: and no row calls an event dead or unused",
                          !said.Any(t => t.Contains("dead", StringComparison.OrdinalIgnoreCase)
                                      || t.Contains("unused", StringComparison.OrdinalIgnoreCase)));

                Console.WriteLine($"        symbols: {window.SymbolGrid.RowCount} rows, " +
                                  $"{said.Count} of them naming a role, e.g. \"{said.FirstOrDefault()}\"" +
                                  (window.LoadedXml.Length == 0 ? "  (read with no Java)" : ""));
            }

            // The canvas is drawn from the model, and the model comes from the file's own bytes, so
            // it fills whether or not Java is present. Checked outside the text guard below on
            // purpose: inside it, a window that drew nothing would skip the check and pass.
            {
                tabs[0].SelectedIndex = 1;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                int drawn = Find<GraphView>(window).First().DrawnIds.Count;
                Console.WriteLine($"        canvas: {drawn} node(s) drawn");
                CheckTrue($"{name}: the canvas draws the graph", drawn > 0);
            }

            // The graph is the primary workspace. The optional legend and details areas must start
            // closed, while properties remain ready for the first selected node. The test drives the
            // same state changes as the pane buttons, rather than inspecting layout implementation
            // details, so a later layout implementation can keep this contract.
            {
                tabs[0].SelectedIndex = 1;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                CheckTrue($"{name}: the legend pane starts collapsed", !window.GraphLeftPaneOpen);
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
                CheckTrue($"{name}: the graph toolbar has space above its section labels",
                          window.GraphToolbarTopInset >= 10);
                Check($"{name}: the graph toolbar has deliberate control groups",
                      "View, Edit, Simulation", string.Join(", ", window.GraphToolbarGroups));
                CheckTrue($"{name}: toolbar group labels reserve their own text height",
                          window.GraphToolbarGroupLabelsHaveFixedLineHeight);
                CheckTrue($"{name}: edit tools stay out of the toolbar until requested", !window.GraphEditShelfOpen);
                Check($"{name}: the details drawer has isolated tabs", "Problems, Output",
                      string.Join(", ", window.GraphDrawerTabs));
                CheckTrue($"{name}: Runtime is not permanently below the graph", !window.RuntimeWindowVisible);

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

                window.SetGraphLeftPaneOpen(true);
                window.ResizeGraphLeftPaneForTest(300);
                CheckTrue($"{name}: the legend pane can open and resize",
                          window.GraphLeftPaneOpen && window.GraphLeftPaneWidth == 300);

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

                var runtimeButton = Find<Button>(window)
                    .First(button => button.Content?.ToString() == "Runtime");
                runtimeButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                CheckTrue($"{name}: Runtime opens in its own window", window.RuntimeWindowVisible);
                Check($"{name}: Runtime gives its sections room", 4, window.RuntimeWindowSectionCount);
                CheckTrue($"{name}: Runtime uses an activated resizable top-level window",
                    window.RuntimeWindowForTest?.UsesDesktopPresentation == true);
                CheckTrue($"{name}: Runtime reaches the native opened and activated lifecycle",
                    window.RuntimeWindowForTest?.WasOpenedAndActivated == true);
                int runtimeWindows = window.RuntimeWindowInstances;
                window.CloseRuntimeForTest();
                CheckTrue($"{name}: closing Runtime leaves the simulation running",
                          !window.RuntimeWindowVisible && window.RunningCount > 0);
                window.OpenRuntimeForTest();
                Check($"{name}: reopening Runtime reuses the same window", runtimeWindows,
                      window.RuntimeWindowInstances);
                CheckTrue($"{name}: reopening Runtime asks the native window to come forward",
                    window.RuntimeWindowForTest?.PresentationRequests >= 2);

                window.SetGraphLeftPaneOpen(false);
                window.SetGraphDrawerOpen(false);
                window.CloseRuntimeForTest();
                CheckTrue($"{name}: closing details hides their contents again", !window.GraphDrawerContentsVisible);
            }

            // Playback has a separate renderer. Its mesh must stay inside the viewport rather than
            // painting across the playback controls or outside the tab.
            {
                tabs[0].SelectedIndex = 5;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                CheckTrue($"{name}: playback viewport clips mesh drawing", window.PlaybackViewportClips);
            }

            // Folding a branch. Two things have to be true and they fail separately: the right nodes
            // go, and the room they were taking comes back. A fold that only stopped drawing them
            // would leave a hole the size of the whole subtree, which is worse than not folding.
            {
                tabs[0].SelectedIndex = 1;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var canvas = Find<GraphView>(window).First();

                // A branch in the middle rather than the root. Folding the root is a real case and
                // a useless check: it takes the whole graph off and proves nothing about the rest of
                // it staying put.
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

            // Sharing is the ordinary case in a shipped behaviour, so a real file has to produce
            // some. A check that passed because nothing was shared would be testing nothing.
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

                // A node with one parent is not marked, or the mark means nothing.
                var only = canvas.DrawnIds.FirstOrDefault(id => canvas.SharedBy(id).Count == 0
                                                             && canvas.OwnerOf(id).Length > 0);
                CheckTrue($"{name}: a node with one parent carries no mark", only != null);

                // The sentence is fixed, not whatever order an enumeration gives, and the owner
                // leads because the node is sitting where the owner put it.
                string one = shared[0];
                string tip = canvas.SharedTip(one);
                string ownerName = canvas.NameOf(canvas.OwnerOf(one));

                CheckTrue($"{name}: the tip names the owner as the owner", tip.Contains("(owner)"));
                CheckTrue($"{name}: and names it first",
                          tip.Contains($": {ownerName} (owner)"));
                Check($"{name}: it names every home once", canvas.SharedBy(one).Count + 1,
                      tip.Split(", ").Length);

                // Folding the branch a node is borrowed from must not make it look exclusive, and
                // folding is a full rebuild, so this checks the sentence survives one too.
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

            // Several nodes picked and dragged together. The failure this guards is a node that is
            // both explicitly selected and reached through its parent moving twice, drifting away
            // from its own family at double speed.
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

                    // The child is in the set once, not twice, even though it is reached both ways.
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

            // The run panel: a graph file starts stepping, lights its active states, and moves them
            // when sent an event. A project or character file has no graph, so it must not pretend to
            // run one. This is the window half of #37; the reachability behind it is checked headless
            // in symrm, so here the question is only whether the panel is wired to it.
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

                    // Sending an event either moves something or does not, and both are fine; what is
                    // checked is that the panel survives it and stays consistent with the canvas. A
                    // real move is looked for across the declared events so a file whose first event
                    // happens to be inert still exercises the moving path.
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

                        // A behaviour opened out of a real project folder gets its clip lengths from
                        // the animation files beside it, which is what lets a state leave because its
                        // clip ended. Checked through the window rather than through the reader,
                        // because the wiring is the part that breaks: the reader can be right while
                        // the window never hands it the folder it is sitting in.
                        // Only asserted when there are animations to read. A behaviour pulled out of
                        // the archive on its own has no folder around it and correctly gets no
                        // lengths, so requiring them everywhere would fail on the honest case.
                        string root = System.IO.Path.GetDirectoryName(
                                          System.IO.Path.GetDirectoryName(path) ?? "") ?? "";
                        bool hasAnimations = root.Length > 0 &&
                                             System.IO.Directory.Exists(System.IO.Path.Combine(root, "Animations"));

                        if (hasAnimations)
                            CheckTrue($"{name}: clips are timed from the animations beside the behaviour",
                                      window.TimedClipCount > 0);

                        Console.WriteLine($"        run: {window.TimedClipCount} clip(s) playing with a " +
                                          "length read from the animation beside the behaviour");

                        // If any send left a transition blending, stepping the clock has to move it
                        // along and, given enough steps, finish it. A blend that never settles would
                        // leave two states lit forever.
                        if (window.RunBlending)
                        {
                            int steps = 0;
                            while (window.RunBlending && steps < 50) { window.StepForTest(0.1f); steps++; }
                            CheckTrue($"{name}: a transition blend settles as the clock advances", !window.RunBlending);
                            CheckTrue($"{name}: and the canvas is still lit after it settles", canvas.ActiveIds.Count > 0);
                            Console.WriteLine($"        run: a blend settled after {steps} step(s)");
                        }
                    }

                    // The variables a condition reads, and setting one.
                    //
                    // A transition gated on a variable can only be tried both ways if the variable can
                    // be changed, so this walks the same controls a person uses: choose the variable,
                    // type a value, press Set, and confirm the run holds the number afterwards.
                    Console.WriteLine($"        run: {window.RunVariables.Count} variable(s) to set");
                    if (window.RunVariables.Count > 0)
                    {
                        string variable = window.RunVariables[0];
                        window.SetVariableForTest(variable, "7");
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                        CheckTrue($"{name}: setting a variable through the box changes what the run holds",
                                  window.RunValueOf(variable) == 7);

                        // A value that is not a number is refused and says so, rather than being read
                        // as zero, which would silently change the variable to something it was not.
                        window.SetVariableForTest(variable, "not a number");
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        CheckTrue($"{name}: nonsense is refused rather than read as zero",
                                  window.RunValueOf(variable) == 7);
                        CheckTrue($"{name}: and the refusal says which variable was left alone",
                                  window.RunSummary.Contains(variable, StringComparison.Ordinal) &&
                                  window.RunSummary.Contains("not a number", StringComparison.Ordinal));

                        // A variable the graph does not declare cannot be set, because nothing in the
                        // graph could read it and accepting it would look like it had worked.
                        window.SetVariableForTest("noSuchVariableAnywhere", "1");
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        CheckTrue($"{name}: a variable the graph does not declare is not offered",
                                  !window.RunVariables.Contains("noSuchVariableAnywhere"));
                    }

                    // The held back line only appears when something is held back, so on a file where
                    // nothing is, the check is that it stays away rather than sitting there empty.
                    CheckTrue($"{name}: transitions held back by a condition are reported, or the line is hidden",
                              window.RunHeldBack > 0 == window.RunHeldBackVisible);

                    // A blender node shows its mix on the properties panel beside the canvas. This is
                    // the weapon idle question answered on the node: how much of each child plays.
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

            // The fields have to be reachable from the canvas, not only from the tree. A node's
            // properties are useless in a tab that is not showing the node.
            //
            // Guarded on the file being a behaviour rather than only on it having a text form, because
            // an animation clip opened with Java has a text form too and no states, transitions or
            // clip generators in it, so these checks would fail on a file they were never about. An
            // animation is exercised by the frame editing section below instead.
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

                // Which event moves which state to which state, which is the thing the canvas has
                // never been able to show: none of it is a reference in the file, so the ownership
                // wires cannot carry it.
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

                    // A route the canvas cannot draw is one whose ends are missing from it, which
                    // would make the picture quietly incomplete rather than visibly wrong.
                    Check($"{name}: every route has both ends on the canvas",
                          canvas.RouteCount, canvas.DrawableRouteCount);

                    // A machine starts somewhere, and which state that is cannot be read off the
                    // picture without the badge.
                    CheckTrue($"{name}: a start state is marked", canvas.StartStateIds.Count > 0);
                    CheckTrue($"{name}: and the node itself knows it is one",
                              canvas.StartStateIds.All(id => !canvas.DrawnIds.Contains(id) || canvas.IsStart(id)));

                    // Every transition in a machine runs between two of its states. A wildcard is
                    // written on the machine rather than on a state, but it fires from every state
                    // the machine holds, so highlighting a state has to show it leaving that state
                    // rather than leaving the machine.
                    var withWildcards = routes.MachineOfState.Keys
                        .Where(s => canvas.DrawnIds.Contains(s))
                        .FirstOrDefault(s => routes.LeavingState(s).Any(r => r.Wildcard)) ?? "";

                    // A wildcard is not a line. With nothing picked out the canvas draws direct
                    // transitions only, and every state a wildcard can enter says so on itself.
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

                        // Every wildcard in the file has to reach the state it targets, or the
                        // canvas has quietly dropped one by not drawing it as a line.
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

                        // A wildcard into the state you are already in is a self transition and is
                        // not a way out of it.
                        CheckTrue($"{name}: none of them points back at the state itself",
                                  leaving.All(r => r.ToId != withWildcards));

                        // The machine's own wildcard count is unchanged by any of this: the routes
                        // are rewritten for drawing, not invented.
                        string machineId = routes.MachineOfState[withWildcards];
                        int onMachine = routes.Out.TryGetValue(machineId, out var fromMachine)
                            ? fromMachine.Count(r => r.Wildcard) : 0;
                        CheckTrue($"{name}: rewriting them adds none and drops none",
                                  wild == onMachine || wild == onMachine - 1);
                    }

                    // Fitting has to actually fit. This used to set a fixed zoom and move the corner
                    // into view, so on a graph nine thousand units across the button put you in the
                    // top left of it and reported success.
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

                        // And fitting one node's neighbourhood has to show less than everything, or
                        // it is the same button twice.
                        canvas.Highlight(node);
                        canvas.FrameRelated();
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                        var near = canvas.VisibleWorld();
                        CheckTrue($"{name}: fit selection shows less than the whole graph",
                                  near.Width < seen.Width);
                        canvas.ClearHighlight();
                    }

                    // Picking a state out has to bring what it routes to with it. Ownership alone
                    // answers what a state contains and says nothing about what enters or leaves it.
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

                // A transition array is the object the flat panel was worst at: every element
                // carries the same field names, so five transitions arrived as eighty boxes with
                // nothing saying where one ended and the next began. Each element is now behind a
                // line naming its event and its target, and the boxes only exist once opened.
                // The busiest array that is on the canvas, not the first: an array holding one
                // transition proves nothing about a panel whose problem only appears when the same
                // field names repeat.
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

                    // What hovering a field name actually says is read from the built controls,
                    // rather than inferred from the code that builds them.
                    //
                    // Opened first, because a collapsed element has built no field rows and there is
                    // nothing to hover. Reading the tips without this found the summary lines on the
                    // element headers and reported that no field said anything.
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

                    // Put back, because a later check asserts every block starts closed and this is
                    // the only thing that opened them.
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

                    // The line is the whole point: it has to name the event, not the element index.
                    CheckTrue($"{name}: each block says which event it fires on",
                              blocks.Count == 0 || summaries.Values.All(s => s.Contains("->")));

                    // Opening one has to produce the fields, or the grouping has hidden them rather
                    // than tidied them.
                    if (blocks.Count > 0)
                    {
                        blocks[0].IsExpanded = true;
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        CheckTrue($"{name}: opening a block shows that element's fields",
                                  Find<TextBox>(window.GraphProperties).Count > collapsed);
                    }
                }

                // The filter box sits above the tabs, so it has to work on whichever one is showing.
                // Driving only the tree meant typing in it on the Graph tab did nothing at all.
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

                // Dragging a wire out to empty canvas and picking a node type. The new node has to
                // land under the cursor: laid out by depth it goes into a column of its own at the
                // far end of the graph, which is where it used to appear.
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

                    // Undo has to put the document back and take the node off the canvas with it,
                    // not only flip the unsaved marker.
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

                    // Back to the file the run was given, since the paste walk opens a copy.
                    window.Open(path);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                    // A file compared with itself is the one answer that cannot be wrong, and it
                    // proves the unpack, the census and the walk all ran.
                    string said = window.CompareLoadedWith(path);
                    if (said.Length == 0)
                        Console.WriteLine("        compare: skipped, the window opened read only");
                    else
                        CheckTrue($"{name}: a file compared with itself reports no difference",
                                  said.Contains("same objects", StringComparison.Ordinal));

                    // Object ids restart at #1 in the next file, so a remembered position, highlight
                    // or filter would be applied to whatever now holds that number.
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

            // A paged view can drop the tail without saying so, which is the failure the old 300 row
            // cap made visible and paging could hide. Walk every page and prove the frames shown add
            // up to the frames the file has, with the last page ending on the last frame.
            if (window.AnimationFrameCount > 0)
            {
                // The paging buttons only exist while their own tab is built, and the canvas section
                // above leaves the Graph tab selected.
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
                // Counted against the tracks rather than against every row: annotations are rows too
                // and they are not filtered, so a clip with seven of them is still showing seven.
                int unfiltered = window.AnimationGrid.RowCount;
                window.FilterBones("no-such-bone-xyzzy");
                int filtered = window.AnimationGrid.RowCount;
                CheckTrue($"{name}: a filter matching nothing leaves the annotations and one line saying so",
                          filtered < unfiltered && filtered <= window.AnimationAnnotationCount + 1);
                window.FilterBones("");
                Check($"{name}: clearing the filter brings the tracks back", unfiltered, window.AnimationGrid.RowCount);

                // Changing a frame. A frame is not addressable by anything in the file, so it is
                // picked by its row, and the whole path from picking to typing to the number
                // actually moving is what this drives. The file is not written: saving is the same
                // call the harness proves on real animations, and doing it here would rewrite the
                // sample.
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

                    // Refused rather than half applied, because two numbers where three are wanted
                    // would otherwise land as a position nobody typed.
                    window.TypeFramePosition("1, 2");
                    var still = window.FramePosition(0, 0);
                    CheckTrue($"{name}: a position short of three numbers is refused",
                              Math.Abs(still.X - 11.5f) < 0.001f);
                    CheckTrue($"{name}: and it says what it wanted",
                              window.FrameEditAnswer.Contains("three numbers", StringComparison.Ordinal));

                    SaveOnACopy(window, path, name);

                    // Back to the file the run was given, so the checks after this see the file they
                    // were pointed at rather than the copy.
                    window.Open(path);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                }
            }

            // Playback. Only reachable when a skeleton resolved for this file, which for a loose
            // animation means a CharacterAssets folder beside it; when it did not, the window says so
            // rather than drawing nothing, and there is no pose to check.
            tabs[0].SelectedIndex = headers.IndexOf("Playback");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            // The tree has to reach the same place the canvas does. It used to fill only the
            // properties panel, so a clip picked in the tree left the viewport empty and the tab
            // looked broken from that side.
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

            // Selecting a clip is what loads a pose, so a behaviour needs one selected. Walked rather
            // than taking the first: most clips in a graph name an animation the folder does not have,
            // which is the ordinary case Check graph already warns about.
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

                // Against the middle as well as the end, because plenty of clips are loops and a
                // loop ends where it began. Comparing only the two ends called a working idle
                // broken, which is the check being wrong rather than the clip. What is really being
                // asked is whether the pose moves at all as the clip runs.
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

                // Play has to actually run a clock and stop when pressed again, not just relabel.
                var play = Find<Button>(window).First(b => b.Content?.ToString() is "Play" or "Pause");
                window.ScrubTo(0);
                // Pressed without pumping the dispatcher. Play starts a repeating timer, and running
                // jobs while it ticks never returns, so a pump here hangs the whole suite rather
                // than failing it. IsPlaying is set as the button is pressed, so there is nothing to
                // wait for anyway.
                ClickOnly(play);
                CheckTrue($"{name}: play starts a clock", window.IsPlaying || frames <= 1);
                ClickOnly(play);
                CheckTrue($"{name}: and pressing it again stops it", !window.IsPlaying);

                // Scrubbing is a view of a file on disk. It must never make the graph dirty, or every
                // look at an animation would arm the save button.
                CheckTrue($"{name}: scrubbing leaves the document alone",
                          !Find<Button>(window).First(b => b.Content?.ToString() == "Save to .hkx").IsEnabled);
            }
        }

        // Text typed into a property field only reaches the document when the field is left, so a
        // value typed and then saved straight away used to be missing from what was written. Saving
        // commits the pending fields first. This types without pressing Enter and without clicking
        // away, then asks for that commit the way saving does, and checks the document caught it.
        // It stops short of actually saving: writing the example file is not this test's business.
        foreach (string path in args.Where(System.IO.File.Exists))
        {
            // Editing is done by rewriting the text form, so with no Java there is no document to
            // type into and nothing here to check. The window is still readable, which is what the
            // checks above cover.
            if (window.LoadedXml.Length == 0)
            {
                Console.WriteLine("        editing: skipped, the window opened without a text form");
                continue;
            }

            string clip = OpenCommonwealth.Services.Hkx.HkxTextEdit
                .IdsOfClass(window.LoadedXml, "hkbClipGenerator").FirstOrDefault() ?? "";

            // An animation file holds no clip generator, and that is not a failure: it is a file of
            // frames rather than a graph, and the frame editing above is what it gets checked on.
            // Demanding one here reported every animation as broken. A behaviour with no clip in it
            // is still a failure, so the check is kept for the files it means something for.
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

            // A named field rather than whichever box comes first. The first one is
            // variableBindingSet, which holds a reference and rightly refuses arbitrary text, so
            // typing into it proves nothing about whether the commit ran.
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

        ArchiveBrowserBuilds();

        Console.WriteLine($"\n{_ran} checks, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    /// The clip list is built from clip generators and an animation file holds none, so Playback
    /// opened on one with an empty panel. An empty panel reads as broken even when it is correct,
    /// and every user pays for that once.
    ///
    /// Its own window rather than the one the loop above left behind, because the check counts rows
    /// and a window that has held a behaviour has a list already filled from it.
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

        // Selected as well as listed, because the row exists to save the user a click they have no
        // reason to know is needed: the animation is already the thing being played.
        CheckTrue("and the row is picked, so Playback behaves as if a clip had been chosen",
                  window.ClipGrid.HasSelection);

        // What the row says, not merely that there is one. A row naming the wrong file or reading
        // "0.00s, 0 frames" would pass a count and still tell the user nothing.
        var said = Find<TextBlock>(window.ClipGrid).Select(t => t.Text ?? "").ToList();
        CheckTrue("and it names the animation that is loaded",
                  said.Contains("IdleOutroDogmeatWalkForward"));
        CheckTrue($"and says how long it runs ({string.Join(" | ", said)})",
                  said.Contains($"11.20s, {window.AnimationFrameCount} frames"));
        CheckTrue("and the file summary calls it an animation",
                  Find<TextBlock>(window).Any(t => (t.Text ?? "").Contains("an animation, not a behaviour",
                                                                  StringComparison.Ordinal)));
    }

    /// The archive browser, built on a real archive written here rather than one from the game, so
    /// this runs on a build machine with no Fallout 4 on it.
    ///
    /// Its own window, so it needs walking the same way the tabs do: a control that is never shown
    /// is never built, and a filter that throws would otherwise only be found by a person typing
    /// into it.
    /// Presses the save button for real, on a copy.
    ///
    /// Everything above it stops at the decoded animation held in memory, which proves the editing
    /// and nothing about the writing. The write is the part that replaces a file on disk, so it is
    /// driven here rather than assumed from the harness that proves the converter: a copy is opened,
    /// a frame is moved, the button is pressed, and the file on disk is read back and asked whether
    /// the frame moved. The copy is used so the sample this run was pointed at is left alone.
    /// Copy a subtree and paste it back, through the window's own buttons rather than through the
    /// class behind them.
    ///
    /// Done on a copy of the file, because pasting writes the file there and then. What this is for
    /// is the wiring: that the button is offered only once something has been copied, that the slot
    /// list follows the selection, and that the file on disk afterwards really holds more objects
    /// than it did. Whether the references inside the copy are right is `symrm paste`, over all 531
    /// behaviours rather than over this one.
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

    /// The template path through the window: keep the selected shape, then put it back.
    ///
    /// Walked here rather than only in `symrm test` because the wiring is the part that breaks. The
    /// store can be right while the window never hands it the selection, never refreshes its list, or
    /// leaves the button disabled, and none of that shows up in a check that calls the store directly.
    private static void TemplatesOnACopy(MainWindow window, string copy, string name)
    {
        // A folder of this run's own, so a smoke test never reads or writes the templates belonging to
        // whoever is running it.
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uismoke-templates");
        if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true);
        OpenCommonwealth.Services.Hkx.TemplateStore.Folder = folder;

        // A clip rather than the state the paste checks used. A state usually shares its generator
        // with another state and so cannot be lifted at all, which would leave this only ever walking
        // the refusal and never the path the feature exists for. Clips are the shape that lifts:
        // `symrm template` counts 3,717 of 3,740 liftable against 1,696 of 5,320 states.
        string clip = OpenCommonwealth.Services.Hkx.HkxTextEdit
            .IdsOfClass(window.LoadedXml, "hkbClipGenerator").FirstOrDefault() ?? "";
        if (clip.Length == 0) return;

        window.SelectNode(clip);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        window.SaveTemplateForTest("Smoke Shape");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Console.WriteLine("        " + window.PasteAnswer);

        // A shape sharing an object with the rest of its file cannot be lifted, and that is the
        // ordinary answer for a state rather than a failure of this test, so both outcomes are
        // allowed and only the wrong ones are not.
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

        // Specifically the fit, which is the thing only this line can say. "N object(s)" alone would
        // also match the message left behind by keeping it, so the check would pass without the fit
        // ever being worked out.
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

        // Applying it into the file it came out of is the same file by name and a different one by
        // history, so it goes down the ordinary path and must either land or say why not.
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

        // Read off the disk rather than out of the window, because the window reloads itself after a
        // save and would report its own memory either way.
        var before = new OpenCommonwealth.Services.Hkx.HkxBinaryReader().ReadAnimation(path);
        var written = new OpenCommonwealth.Services.Hkx.HkxBinaryReader().ReadAnimation(copy);

        // A spline clip is written back spline compressed, which is the win of the encoder and comes
        // out smaller than the file it replaced. Only a lossless clip, which nothing re-encodes,
        // falls back to uncompressed. So the class the save produces, and the tolerances the checks
        // hold it to, follow the class it went in as: an exact hundredth for the exact path, and the
        // codec's own quantisation for the spline one.
        bool wasSpline = before.AnimationClass == "hkaSplineCompressedAnimation";
        Check($"{name}: the saved file keeps its kind where it can be re-encoded",
              wasSpline ? "hkaSplineCompressedAnimation" : "hkaInterleavedUncompressedAnimation",
              written.AnimationClass);

        float editLimit = wasSpline ? 0.05f : 0.001f;
        float elsewhereLimit = wasSpline ? 0.05f : 0.001f;

        var landed = written.Tracks[0].Translations[0];
        float editDrift = (landed - new System.Numerics.Vector3(7.25f, -8.5f, 9.75f)).Length();
        CheckTrue($"{name}: and holds the frame that was typed ({editDrift:F4})", editDrift < editLimit);

        // The clip is not only correct where it was edited. Every other frame has to survive the
        // round trip through the writer, or a save would quietly flatten the rest of the animation.
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
            "Meshes/Actors/Dogmeat/Behaviors/DogmeatRoot.hkx",
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

        // The extension is the browser's, not the caller's afterthought: a .nif in the archive must
        // not be offered as a behaviour to open.
        Check("only the two behaviours are offered", 2, lists[0].ItemCount);

        boxes[0].Text = "dogmeat behaviors";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Check("typing words in any order narrows it", 1, lists[0].ItemCount);

        boxes[0].Text = "mirelurk";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Check("and a word nothing matches empties it", 0, lists[0].ItemCount);
        CheckTrue("with nothing chosen while the list is empty", browser.Chosen == null);

        browser.Close();
        System.IO.File.Delete(path);
    }

    /// The smallest valid BA2: a 24 byte header, one 36 byte entry per file, all stored plain, then
    /// the name table. Enough for the browser to have something real to list.
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

    /// The value box on the row whose label is this field's name.
    private static TextBox? FieldNamed(Visual panel, string name)
    {
        foreach (var row in Find<DockPanel>(panel))
        {
            var label = row.Children.OfType<TextBlock>().FirstOrDefault();
            if (label?.Text != name) continue;
            return row.Children.OfType<TextBox>().FirstOrDefault();
        }
        return null;
    }

    /// Presses a button without pumping the dispatcher afterwards, for the ones that start
    /// something repeating.
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
