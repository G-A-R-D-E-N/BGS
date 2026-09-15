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
    private Control BuildGraphTab()
    {
        _problems.SelectionChanged += OnProblemSelected;

        var fitAll = Ux.Secondary("Fit all");
        fitAll.Click += (_, _) =>
        {
            _graph.ClearHighlight();
            _graph.FrameAll();
        };

        var fitPicked = Ux.Secondary("Fit selection");
        fitPicked.Click += (_, _) =>
        {
            if (_graph.SelectedId.Length > 0 && _graph.HighlightId.Length == 0)
                HighlightPaths(_graph.SelectedId);
            _graph.FrameRelated();
        };

        Control view = BuildViewMenu(fitAll, fitPicked);
        _graphToolbarGroups.Clear();
        _graphToolbarGroupLabelsHaveFixedLineHeight = false;
        view = GraphToolbarGroup("View", view, fitAll, fitPicked);
        var runBar = BuildRunControls();
        var pasteBar = BuildPasteControls();
        _graphEditShelf = Framed(pasteBar);
        _graphEditShelf.IsVisible = false;
        var edit = Ux.Secondary("Edit tools");
        edit.Click += (_, _) => SetGraphEditShelfOpen(!_graphEditShelf.IsVisible);
        var editGroup = GraphToolbarGroup("Edit", edit);
        var simulation = GraphToolbarGroup("Simulation", runBar);

        var toolbarLeft = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        toolbarLeft.Children.Add(view);
        toolbarLeft.Children.Add(editGroup);
        toolbarLeft.Children.Add(simulation);
        toolbarLeft.Children.Add(_graphFilter);
        var toolbar = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(toolbarLeft, Dock.Left);
        toolbar.Children.Add(toolbarLeft);
        toolbar.Children.Add(_runSummary);
        var toolbarHost = new Border
        {
            Padding = new Thickness(0, 10, 0, 8),
            Child = toolbar,
        };

        _graphRightSplitter = new GridSplitter { Width = 6, Background = Ux.BorderBrush,
            ResizeDirection = GridResizeDirection.Columns };

        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(_graphCenterColumn);
        workspace.ColumnDefinitions.Add(_graphRightSplitterColumn);
        workspace.ColumnDefinitions.Add(_graphRightColumn);
        var canvasLayer = new Grid();
        canvasLayer.Children.Add(_graph);
        canvasLayer.Children.Add(BuildCrashPanel());
        _graphCanvasHost = Framed(canvasLayer);
        _graphCanvasHost.ClipToBounds = true;
        Grid.SetColumn(_graphCanvasHost, 0);
        Grid.SetColumn(_graphRightSplitter, 1);
        _graphProps.SetHeaderAction("Collapse", () => SetGraphRightPaneOpen(false));
        _graphPropertiesHost = Framed(_graphProps);
        _graphPropertiesHost.ClipToBounds = true;
        Grid.SetColumn(_graphPropertiesHost, 2);
        workspace.Children.Add(_graphCanvasHost);
        workspace.Children.Add(_graphRightSplitter);
        workspace.Children.Add(_graphPropertiesHost);

        var problems = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_problemBar, Dock.Top);
        problems.Children.Add(_problemBar);
        problems.Children.Add(_problems);

        _graphDrawerTabs = new TabControl { Padding = new Thickness(6, 0, 6, 0) };
        _graphDrawerTabs.Items.Add(Tab("Problems", problems));
        _graphDrawerTabs.Items.Add(Tab("Output", _runOutput));

        var drawer = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6), IsVisible = false,
            ClipToBounds = true };
        _graphDrawer = drawer;
        drawer.Children.Add(_graphDrawerTabs);

        _drawerButton = Ux.Secondary("Show diagnostics");
        _drawerButton.Click += (_, _) => SetGraphDrawerOpen(!_graphDrawerOpen);
        ToolTip.SetTip(_drawerButton, "Show validation findings and diagnostic output below the graph.");
        var drawerBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            Margin = new Thickness(0, 8, 0, 0),
        };
        drawerBar.Children.Add(_drawerButton);

        _graphDrawerSplitter = new GridSplitter { Height = 6, Background = Ux.BorderBrush,
            ResizeDirection = GridResizeDirection.Rows, IsVisible = false };

        var graphWorkspace = new Grid();
        graphWorkspace.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        graphWorkspace.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        graphWorkspace.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        graphWorkspace.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        graphWorkspace.RowDefinitions.Add(_graphDrawerSplitterRow);
        graphWorkspace.RowDefinitions.Add(_graphDrawerRow);
        Grid.SetRow(toolbarHost, 0);
        Grid.SetRow(_graphEditShelf, 1);
        Grid.SetRow(workspace, 2);
        Grid.SetRow(drawerBar, 3);
        Grid.SetRow(_graphDrawerSplitter, 4);
        Grid.SetRow(drawer, 5);
        graphWorkspace.Children.Add(toolbarHost);
        graphWorkspace.Children.Add(_graphEditShelf);
        graphWorkspace.Children.Add(workspace);
        graphWorkspace.Children.Add(drawerBar);
        graphWorkspace.Children.Add(_graphDrawerSplitter);
        graphWorkspace.Children.Add(drawer);

        _problems.IsVisible = false;
        _problemBar.IsVisible = false;
        _running.IsVisible = false;
        return graphWorkspace;
    }

    private Button BuildViewMenu(Control fitAll, Control fitPicked)
    {
        var button = Ux.Secondary("View ▾");
        var menu = new ContextMenu();
        var workspace = ViewItem("Workspace", OpenWorkspaceWindow);
        var properties = ViewItem("Properties", () => SetGraphRightPaneOpen(!_graphRightOpen));
        var problems = ViewItem("Problems", () => OpenGraphDrawer("Problems"));
        var output = ViewItem("Output", () => OpenGraphDrawer("Output"));
        var legend = ViewItem("Legend", OpenLegendWindow);
        var focus = ViewItem("Focus tree", FocusSelectedMachine);
        var full = ViewItem("Show full graph", ShowFullGraph);
        var freeform = ViewItem("Freeform", () => SetGraphLayoutMode(GraphLayoutMode.Freeform));
        var structured = ViewItem("Structured Flow", () => SetGraphLayoutMode(GraphLayoutMode.StructuredFlow));
        var upstream = ViewItem("Trace upstream", () => TraceSelected(GraphTrace.Direction.Upstream));
        var downstream = ViewItem("Trace downstream", () => TraceSelected(GraphTrace.Direction.Downstream));
        var both = ViewItem("Trace both", () => TraceSelected(GraphTrace.Direction.Both));
        var clear = ViewItem("Clear trace", _graph.ClearTrace);
        foreach (var item in new MenuItem[]
                 { workspace, properties, problems, output, legend, freeform, structured, focus, full,
                   upstream, downstream, both, clear })
            menu.Items.Add(item);

        button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            workspace.Header = WorkspaceVisible ? "Workspace   Open" : "Workspace   Closed";
            legend.Header = LegendWindowVisible ? "Legend   Open" : "Legend   Closed";
            properties.Header = _graphRightOpen ? "Properties   Open" : "Properties   Closed";
            freeform.Header = _graph.LayoutMode == GraphLayoutMode.Freeform ? "✓ Freeform" : "Freeform";
            structured.Header = _graph.LayoutMode == GraphLayoutMode.StructuredFlow
                ? "✓ Structured Flow" : "Structured Flow";
            menu.Open(button);
        };
        ToolTip.SetTip(button, "Open workspace tools, panels, graph focus, tracing, and reference material.");
        return button;
    }

    private static MenuItem ViewItem(string label, Action action)
    {
        var item = new MenuItem { Header = label };
        item.Click += (_, _) => action();
        return item;
    }

    private void OpenGraphDrawer(string tab)
    {
        SetGraphDrawerOpen(true);
        SelectGraphDrawerTab(tab);
    }

    private void SetGraphLayoutMode(GraphLayoutMode mode)
    {
        _graph.SetLayoutMode(mode);
        SetStatus(mode == GraphLayoutMode.StructuredFlow
            ? "Structured Flow shows the state-machine hierarchy."
            : "Freeform shows the raw object dependency graph.", Ux.MetaBrush);
    }

    private Control BuildWorkspaceRuntimeTab()
    {
        var variables = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(14, 0, 14, 10),
        };
        variables.Children.Add(Ux.SectionTitle("Variables"));
        variables.Children.Add(_runVariables);
        variables.Children.Add(_runValue);
        variables.Children.Add(_setRunVariable);

        var sections = new Grid();
        sections.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        sections.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        sections.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        sections.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        PlaceRuntime(_running, sections, 0, 0, new Thickness(14, 0, 5, 5));
        PlaceRuntime(_runStopsGrid, sections, 1, 0, new Thickness(5, 0, 14, 5));
        PlaceRuntime(_runHeldBackGrid, sections, 0, 1, new Thickness(14, 5, 5, 14));
        PlaceRuntime(_runLog, sections, 1, 1, new Thickness(5, 5, 14, 14));

        var content = new DockPanel { LastChildFill = true };
        var header = new DockPanel { Margin = new Thickness(14, 12, 14, 8) };
        header.Children.Add(Ux.SectionTitle("Simulation runtime"));
        header.Children.Add(_runtimeStatus);
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(variables, Dock.Top);
        content.Children.Add(header);
        content.Children.Add(variables);
        content.Children.Add(sections);
        return content;
    }

    private static void PlaceRuntime(Control control, Grid grid, int column, int row, Thickness margin)
    {
        control.Margin = margin;
        Grid.SetColumn(control, column);
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    private Control BuildRunControls()
    {
        var send = Ux.Primary("Send");
        send.Click += (_, _) => SendRunEvent();

        var restart = Ux.Secondary("Restart");
        ToolTip.SetTip(restart, "Put the graph back in the state it starts in.");
        restart.Click += (_, _) => StartRun("Back at the start.");

        _step = Ux.Secondary("Step 0.1s");
        ToolTip.SetTip(_step, "Advance time so a transition in progress blends further.");
        _step.Click += (_, _) => RenderRun(_runSession.Advance(0.1f));

        _running.SelectionChanged += () =>
        {
            if (_running.SelectedTag is string id && id.Length > 0 && _graph.FocusOn(id))
                SelectObjectId(id);
        };

        var label = Ux.Label("Event");
        label.FontSize = 11;
        label.Margin = new Thickness(2, 0, 0, 0);

        _runValue.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) SetRunVariable(); };
        _runVariables.SelectionChanged += (_, _) => ShowRunVariable();
        ToolTip.SetTip(_setRunVariable, "Change a simulation variable before sending the next event.");
        _setRunVariable.Click += (_, _) => SetRunVariable();

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        foreach (var control in new Control[] { label, _runEvents, send, _step, restart })
            left.Children.Add(control);

        SetRunSummary("Open a behaviour, then send it an event to watch which state goes active.",
            Ux.MutedBrush);
        return left;
    }

    private Control BuildPasteControls()
    {
        var copy = Ux.Secondary("Copy subtree");
        ToolTip.SetTip(copy, "Take the selected node and everything it owns, ready to paste.");
        copy.Click += (_, _) => CopySubtree();

        _pasteButton = Ux.Primary("Paste subtree");
        ToolTip.SetTip(_pasteButton,
            "Put a fresh copy into this file and save it. The file is kept as .bak first.");
        _pasteButton.Click += (_, _) => PasteSubtree();
        _pasteButton.IsEnabled = false;

        var label = new TextBlock
        {
            Text = "Attach to",
            Foreground = Ux.MetaBrush,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        var save = Ux.Secondary("Save as template");
        ToolTip.SetTip(save, "Keep the selected node and everything it owns, so it can be used again " +
                             "in another file later.");
        save.Click += (_, _) => SaveTemplate();

        _applyTemplate.Click += (_, _) => ApplyStoredTemplate();
        _applyTemplate.IsEnabled = false;
        ToolTip.SetTip(_applyTemplate,
            "Put a kept shape into this file and save it. The file is kept as .bak first.");

        _templates.SelectionChanged += (_, _) => DescribeTemplate();
        _predefinedTemplates.ItemsSource = PredefinedTemplates.All().Select(template => template.Id).ToList();
        _predefinedTemplates.SelectionChanged += (_, _) => RefreshPredefinedTemplateEditors();
        _applyPredefinedTemplate.Click += (_, _) => ApplyPredefinedTemplate();
        if (_predefinedTemplates.Items.Count > 0) _predefinedTemplates.SelectedIndex = 0;

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var control in new Control[] { copy, label, _pasteInto, _pasteButton,
                                                _templateName, save, _templates, _applyTemplate })
            left.Children.Add(control);

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
        DockPanel.SetDock(left, Dock.Left);
        bar.Children.Add(left);
        bar.Children.Add(_pasteSummary);

        var predefined = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                                          Margin = new Thickness(0, 6, 0, 0) };
        predefined.Children.Add(new TextBlock { Text = "Predefined", Foreground = Ux.MetaBrush,
                                                FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        predefined.Children.Add(_predefinedTemplates);
        predefined.Children.Add(_predefinedSlots);
        predefined.Children.Add(_applyPredefinedTemplate);

        var all = new StackPanel();
        all.Children.Add(bar);
        all.Children.Add(predefined);

        RefreshPasteSlots();
        RefreshTemplates();
        _applyPredefinedTemplate.IsEnabled = _bytes != null && !_readOnly;
        RefreshPredefinedTemplateEditors();
        return all;
    }

}
