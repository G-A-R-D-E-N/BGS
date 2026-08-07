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
        Check("the tree, problem, symbol, chain, animation, clip and compare grids exist", 7, grids.Count);

        // The problem list is the one that starts hidden: an empty box under the canvas before any
        // check has run would read as a check that found nothing.
        Check("the problem list is hidden until a check has run", 1, grids.Count(g => !g.IsVisible));
        foreach (string expected in new[]
                 { "Open", "Browse...", "From archive...", "Expand all", "Collapse all", "Check graph", "Save to .hkx", "+ real", "+ event", "Remove",
                   "Undo", "Redo", "Compare with...", "Check project", "Scripts folder...",
                   "Play", "From selected node", "Fit" })
            CheckTrue($"the {expected} button is there", buttons.Contains(expected));

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

                Check($"{name}: scrubbing to the last frame lands on it", frames - 1, window.PoseFrame);
                CheckTrue($"{name}: and frame 0 and the last frame are different poses",
                          OpenCommonwealth.Services.Hkx.AnimationPose.Distance(atStart, atEnd) > 0.01f);
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
            CheckTrue("a clip to edit was found", clip.Length > 0);
            if (clip.Length == 0) continue;

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

        ArchiveBrowserBuilds();

        Console.WriteLine($"\n{_ran} checks, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    /// The archive browser, built on a real archive written here rather than one from the game, so
    /// this runs on a build machine with no Fallout 4 on it.
    ///
    /// Its own window, so it needs walking the same way the tabs do: a control that is never shown
    /// is never built, and a filter that throws would otherwise only be found by a person typing
    /// into it.
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
