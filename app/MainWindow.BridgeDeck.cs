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
    private Control BuildBridgeReference()
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(210, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        int row = 0;
        void RefRow(string name, string where)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var left = new TextBlock
            {
                Text = name,
                Foreground = Ux.MetaBrush,
                FontSize = 12,
                Margin = new Thickness(2, 3, 12, 3),
            };
            var right = new TextBlock
            {
                Text = where,
                Foreground = Ux.MutedBrush,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 3, 2, 3),
            };
            Grid.SetRow(left, row);
            Grid.SetColumn(left, 0);
            Grid.SetRow(right, row);
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);
            _bridgeRefRows.Add((left, right));
            row++;
        }

        RefRow("Open a file", "The command bar path field — or the Current file card above.");
        RefRow("Browse the file", "Inspect → Tree.");
        RefRow("See the graph", "Graph on the left rail.");
        RefRow("Edit an object's fields", "Pick it in Inspect → Tree or Graph; the pane on the right shows its fields.");
        RefRow("Events and variables", "Inspect → Symbols.");
        RefRow("Undo / Redo", "The command bar (Ctrl+Z / Ctrl+Y), or the Current file card above.");
        RefRow("Save", "The command bar, or the Current file card above.");
        RefRow("Check the graph", "The command bar, or the Verify section above.");
        RefRow("Problems and Output", "Graph → Show diagnostics, or the Verify section above.");
        RefRow("Copy, paste and templates", "Graph → Edit tools.");
        RefRow("Run the simulation", "The Graph toolbar, or the Workspace window → Runtime tab.");
        RefRow("List of machines", "Graph → View ▾ → Workspace.");
        RefRow("Animation keyframes", "Animation on the left rail.");
        RefRow("Playback, skeleton, mesh", "Animation → Playback.");
        RefRow("Compare two files", "Project → Compare.");
        RefRow("What the colours mean", "Graph → View ▾ → Legend.");
        RefRow("Focus one machine, trace dependencies", "Graph → View ▾.");

        return new Border
        {
            Background = Ux.CardBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12),
            Child = grid,
        };
    }

    private Control BuildBridgeTab()
    {
        var body = new StackPanel { Spacing = 12 };

        _bridgeTitle = new TextBlock
        {
            Text = "The Bridge",
            Foreground = Ux.TitleBrush,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
        };
        var takeTour = Ux.Secondary("Take the tour");
        ToolTip.SetTip(takeTour, "Walk the deck one station at a time, with everything else dimmed.");
        takeTour.Click += (_, _) => StartTour();
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        header.Children.Add(_bridgeTitle);
        header.Children.Add(takeTour);
        body.Children.Add(header);
        body.Children.Add(new TextBlock
        {
            Text = "Every part of the studio in one place. Open a file, jump to any tool, or look up " +
                   "where something lives — nothing is hidden in a menu you have to remember.",
            Foreground = Ux.MetaBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });

        _bridgeFileCard = BuildBridgeFileCard();
        body.Children.Add(_bridgeFileCard);

        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        searchRow.Children.Add(_bridgeSearch);
        searchRow.Children.Add(_bridgeSearchAnswer);
        _bridgeSearch.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) ApplyBridgeSearch();
        };
        _bridgeSearch.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape && (_bridgeSearch.Text ?? "").Length > 0)
            {
                _bridgeSearch.Text = "";
                e.Handled = true;
            }
        };
        body.Children.Add(searchRow);

        var currentStations = new WrapPanel();
        TextBlock Section(string name)
        {
            var section = Ux.SectionTitle(name);
            section.Margin = new Thickness(1, 8, 0, 0);
            body.Children.Add(section);
            currentStations = new WrapPanel { ItemWidth = 350 };
            body.Children.Add(currentStations);
            _bridgeGroups.Add((section, new List<Border>()));
            return section;
        }

        void Station(string title, string what, string where, params (string Label, Action Go)[] actions)
        {
            var card = BridgeCard(title, what, where, actions);
            currentStations.Children.Add(card);
            _tourStations.Add((card, title, what, where));
            _bridgeGroups[^1].Cards.Add(card);
        }

        Section("Open and inspect");
        Station("Tree", "Browse the whole file as a tree of objects, classes and clips.",
                "Inspect → Tree on the left rail.",
                ("Go to Tree", () => GoToTab("Tree")));
        Station("Graph", "The state machine drawn as boxes and arrows, with the picked object's " +
                         "fields editable on the right.",
                "Graph on the left rail.",
                ("Go to Graph", () => GoToTab("Graph")),
                ("Workspace window", OpenWorkspaceWindow),
                ("Legend", OpenLegendWindow));
        Station("Properties", "Inspect and change the fields of the object you picked.",
                "the pane on the right of Graph and Inspect → Tree.",
                ("Go to Tree", () => GoToTab("Tree")),
                ("Go to Graph", () => GoToTab("Graph")));

        Section("Edit");
        Station("Symbols", "The events and variables this file declares — who raises them and who " +
                           "listens.",
                "Inspect → Symbols on the left rail.",
                ("Go to Symbols", () => GoToTab("Symbols")));
        Station("Chain", "Where this file sits in a project chain — what it depends on and what " +
                         "depends on it.",
                "Project → Chain on the left rail.",
                ("Go to Chain", () => GoToTab("Chain")));
        Station("Copy, paste & templates", "Move a subtree into another file, or save a shape to " +
                                          "reuse later.",
                "Graph → Edit tools.",
                ("Open Edit tools", () =>
                {
                    GoToTab("Graph");
                    SetGraphEditShelfOpen(true);
                }));

        Section("Verify");
        Station("Check graph", "Find broken references, missing objects and suspicious values in the " +
                               "open file. Findings land in the Problems list.",
                "the command bar Check graph button, or this card.",
                ("Run check now", Validate));
        Station("Check project", "Check a whole mod folder — every behaviour file, project chain and " +
                                 "animation reference.",
                "the command bar Check project button.",
                ("Run check now", async () => await ValidateProject()));
        Station("Diagnostics", "The Problems and Output lists that sit under the graph canvas.",
                "Graph → Show diagnostics.",
                ("Open Problems", () =>
                {
                    GoToTab("Graph");
                    OpenGraphDrawer("Problems");
                }),
                ("Open Output", () =>
                {
                    GoToTab("Graph");
                    OpenGraphDrawer("Output");
                }));

        Section("Preview");
        Station("Animation", "Read the keyframes of the loaded animation, filter by bone, and edit " +
                             "a frame.",
                "Animation on the left rail.",
                ("Go to Animation", () => GoToTab("Animation")));
        Station("Playback", "Pose the skeleton or mesh and scrub through the animation in time.",
                "Animation → Playback on the left rail.",
                ("Go to Playback", () => GoToTab("Playback")));
        Station("Simulation", "Run the graph and send it events to watch which state goes active.",
                "the Simulation controls on the Graph toolbar, or the Runtime tab of the Workspace " +
                "window.",
                ("Open Workspace", OpenWorkspaceWindow));

        Section("Reference");
        Station("Legend", "What every box, line and mark on the canvas means.",
                "Graph → View ▾ → Legend.",
                ("Open legend", OpenLegendWindow));

        _bridgeRefHeader = Section("Where everything is");
        body.Children.Add(BuildBridgeReference());

        Section("Jump back in");
        body.Children.Add(_bridgeRecents);
        RefreshRecents();

        var viewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new Border
            {
                Padding = new Thickness(2, 0, 10, 16),
                Child = body,
            },
        };
        Avalonia.Input.DragDrop.SetAllowDrop(viewer, true);
        viewer.AddHandler(Avalonia.Input.DragDrop.DragEnterEvent, BridgeDragOver);
        viewer.AddHandler(Avalonia.Input.DragDrop.DragOverEvent, BridgeDragOver);
        viewer.AddHandler(Avalonia.Input.DragDrop.DragLeaveEvent, (_, _) => HideDropHint());
        viewer.AddHandler(Avalonia.Input.DragDrop.DropEvent, BridgeDrop);
        return viewer;
    }
}
