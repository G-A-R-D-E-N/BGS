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
    private Control BuildProjectSearchTab()
    {
        _projectSearchResults.SelectionChanged += OnProjectSearchSelected;
        _projectSearchText.KeyDown += async (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) await SearchProject();
        };

        var search = Ux.Primary("Search project");
        search.Click += async (_, _) => await SearchProject();
        var open = Ux.Secondary("Open result");
        open.Click += (_, _) => OpenProjectSearchResult();

        var bar = Bar(_projectSearchText, search, open, Ux.Pill(_projectSearchSummary));
        bar.Margin = new Thickness(0, 0, 0, 8);

        var panel = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        panel.Children.Add(bar);
        panel.Children.Add(_projectSearchResults);

        _projectSearchSummary.Text =
            "Search every behaviour in the resolved project by file, object, class, name, field, " +
            "event, variable or asset reference.";
        _projectSearchSummary.Foreground = Ux.MutedBrush;
        return panel;
    }

    private async Task SearchProject()
    {
        string query = (_projectSearchText.Text ?? "").Trim();
        if (query.Length == 0)
        {
            SetProjectSearchSummary("Type something to search for first.", Ux.MutedBrush);
            return;
        }

        var chain = _projectChain;
        if (chain == null || chain.Root.Length == 0)
        {
            SetProjectSearchSummary(
                "No project is resolved for this file. Open a behaviour that belongs to a character project.",
                Ux.MutedBrush);
            return;
        }

        long generation = ++_projectSearchGeneration;
        long stamp = CaptureStamp();
        _projectSearchResults.Clear();
        _projectSearchHits.Clear();
        SetProjectSearchSummary($"Searching {Path.GetFileName(chain.Root)} for '{query}'...", Ux.MutedBrush);

        ProjectSearch.Result result;
        try
        {
            result = await Task.Run(() => ProjectSearch.Run(chain, query));
        }
        catch (Exception error)
        {
            if (generation != _projectSearchGeneration || stamp != _documentStamp) return;
            SetProjectSearchSummary("Project search failed: " + error.Message.Split('\n')[0], Ux.BadBrush);
            return;
        }

        if (generation != _projectSearchGeneration || stamp != _documentStamp
            || !string.Equals(query, (_projectSearchText.Text ?? "").Trim(), StringComparison.Ordinal))
            return;

        _projectSearchHits.AddRange(result.Hits);
        for (int i = 0; i < _projectSearchHits.Count; i++)
        {
            var hit = _projectSearchHits[i];
            string obj = hit.ObjectId.Length > 0 ? $"#{hit.ObjectId} {hit.ClassName}" : "";
            var row = _projectSearchResults.Add(null, hit.File, hit.Kind, obj, hit.Field, hit.Value).Tag(i);
            row.Colour(0, Ux.TitleBrush)
               .Colour(1, hit.Kind is "event" or "variable" ? Ux.CodeBrush : Ux.MutedBrush)
               .Colour(2, Ux.CodeBrush)
               .Colour(3, Ux.MetaBrush)
               .Colour(4, Ux.MetaBrush);
        }

        foreach (var problem in result.Problems)
            _projectSearchResults.Add(null, problem.File, "unreadable", "", "", problem.Error)
                                 .Colour(0, Ux.TitleBrush).Colour(1, Ux.BadBrush).Colour(4, Ux.BadBrush);

        if (result.Hits.Count == 0 && result.Problems.Count == 0)
            _projectSearchResults.Add(null, "", "", "", "", $"nothing matched '{query}'")
                                 .Colour(4, Ux.MutedBrush);

        SetProjectSearchSummary(
            result + $" in {Path.GetFileName(chain.Root)}.",
            result.Problems.Count > 0 ? Ux.WarnBrush : result.Hits.Count > 0 ? Ux.MetaBrush : Ux.MutedBrush);
    }

    private void OnProjectSearchSelected()
    {
        if (_projectSearchResults.SelectedTag is not int index
            || index < 0 || index >= _projectSearchHits.Count) return;

        var hit = _projectSearchHits[index];
        if (SameProjectSearchPath(hit.Path, _hkxPath) && hit.ObjectId.Length > 0)
        {
            SelectObjectId(hit.ObjectId);
            _graph.FocusOn(hit.ObjectId);
            SetStatus($"{hit.File}: #{hit.ObjectId} {hit.ClassName}.{hit.Field} = {hit.Value}", Ux.MetaBrush);
            return;
        }

        SetStatus($"{hit.File}: {(hit.ObjectId.Length > 0 ? "#" + hit.ObjectId + " " : "")}" +
                  $"{hit.Field} = {hit.Value}. Use Open result to jump to that file.", Ux.MetaBrush);
    }

    private void OpenProjectSearchResult()
    {
        if (_projectSearchResults.SelectedTag is not int index
            || index < 0 || index >= _projectSearchHits.Count)
        {
            SetProjectSearchSummary("Select a search result first.", Ux.MutedBrush);
            return;
        }

        var hit = _projectSearchHits[index];
        Open(hit.Path);
        if (!SameProjectSearchPath(_hkxPath, hit.Path)) return;

        if (hit.ObjectId.Length > 0)
        {
            SelectObjectId(hit.ObjectId);
            _graph.FocusOn(hit.ObjectId);
        }
    }

    private void ClearProjectSearch()
    {
        _projectSearchGeneration++;
        _projectSearchResults.Clear();
        _projectSearchHits.Clear();
        _projectSearchSummary.Text =
            "Search every behaviour in the resolved project by file, object, class, name, field, " +
            "event, variable or asset reference.";
        _projectSearchSummary.Foreground = Ux.MutedBrush;
    }

    private static bool SameProjectSearchPath(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0) return false;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private void SetProjectSearchSummary(string text, IBrush brush)
    {
        _projectSearchSummary.Text = text;
        _projectSearchSummary.Foreground = brush;
    }

    public int ProjectSearchRows => _projectSearchResults.RowCount;
    public string ProjectSearchAnswer => _projectSearchSummary.Text ?? "";

    private Control BuildDiffTab()
    {
        var compare = Ux.Secondary("Compare with...");
        compare.Click += async (_, _) => await CompareWith();

        _diffKind.ItemsSource = new[] { "All changes", "Added", "Removed", "Changed" };
        _diffKind.SelectedIndex = 0;
        _diffKind.SelectionChanged += (_, _) => RefreshDiff();
        _diffClass.ItemsSource = new[] { "All classes" };
        _diffClass.SelectedIndex = 0;
        _diffClass.SelectionChanged += (_, _) => RefreshDiff();
        _diffExportText.IsEnabled = false;
        _diffExportJson.IsEnabled = false;
        _diffExportText.Click += async (_, _) => await ExportDiff(json: false);
        _diffExportJson.Click += async (_, _) => await ExportDiff(json: true);

        var summaryBar = Bar(Ux.Pill(_diffSummary), compare);
        summaryBar.Margin = new Thickness(0, 0, 0, 8);

        var filterBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        filterBar.Children.Add(Ux.Label("Change"));
        filterBar.Children.Add(_diffKind);
        filterBar.Children.Add(Ux.Label("Object class"));
        filterBar.Children.Add(_diffClass);
        filterBar.Children.Add(_diffExportText);
        filterBar.Children.Add(_diffExportJson);
        filterBar.Margin = new Thickness(0, 0, 0, 8);

        var panel = Rows(
            (summaryBar, false),
            (filterBar, false),
            (_diff, true));

        _diffSummary.Text = "Open a behaviour, then pick another copy of it to see what differs.";
        _diffSummary.Foreground = Ux.MutedBrush;
        return panel;
    }

    private async Task CompareWith()
    {
        if (_xmlText.Length == 0)
        {
            SetDiffSummary("Open a behaviour file first. Comparing needs the text form of both sides.", Ux.MutedBrush);
            return;
        }

        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Compare against which file",
            AllowMultiple = false,
            SuggestedStartLocation = await StartFolder(),
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Havok files") { Patterns = new[] { "*.hkx", "*.HKX" } },
                FilePickerFileTypes.All,
            },
        });

        string? other = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (other == null) return;

        if (_xmlText.Length == 0)
        {
            SetDiffSummary("Nothing is open to compare against.", Ux.BadBrush);
            return;
        }

        ClearDiff();
        SetDiffSummary($"Reading {Path.GetFileName(other)}...", Ux.MutedBrush);

        long stamp = CaptureStamp();
        string mine = _xmlText;
        var outcome = await _compare.Compare(mine, other, stamp);
        if (outcome.Stale) return;
        if (outcome.Failed)
        {
            SetDiffSummary($"Could not read {Path.GetFileName(other)}: {outcome.Error}", Ux.BadBrush);
            return;
        }

        ShowDiff(Path.GetFileName(other), outcome.Value!);
    }

    public string CompareLoadedWith(string other)
    {
        if (_xmlText.Length == 0) return "";

        ShowDiff(Path.GetFileName(other), BehaviourCompareSession.CompareNow(_xmlText, other));
        return _diffSummary.Text ?? "";
    }

    private void ShowDiff(string otherName, BehaviourDiff.Result result)
    {
        _diffResult = result;
        _diffOtherName = otherName;
        _diffClass.ItemsSource = new[] { "All classes" }
            .Concat(result.Lines.Select(line => line.Class)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal))
            .ToList();
        _diffClass.SelectedIndex = 0;
        _diffExportText.IsEnabled = true;
        _diffExportJson.IsEnabled = true;
        RefreshDiff();
    }

    private void RefreshDiff()
    {
        if (_diffResult is not { } result)
        {
            _diff.Clear();
            return;
        }

        var filter = SelectedDiffFilter();
        var export = BehaviourCompareSession.CreateExport(result, filter);
        var filtered = BehaviourCompareSession.ApplyFilter(result, filter);
        RenderDiff(_diffOtherName, filtered, export, result.Lines.Count != filtered.Lines.Count);
    }

    private BehaviourCompareSession.BehaviourDiffFilter SelectedDiffFilter()
    {
        BehaviourDiff.Kind? kind = (_diffKind.SelectedItem as string) switch
        {
            "Added" => BehaviourDiff.Kind.Added,
            "Removed" => BehaviourDiff.Kind.Removed,
            "Changed" => BehaviourDiff.Kind.Changed,
            _ => null,
        };
        string objectClass = _diffClass.SelectedItem as string ?? "";
        return new BehaviourCompareSession.BehaviourDiffFilter(
            kind, objectClass == "All classes" ? "" : objectClass);
    }

    private void RenderDiff(
        string otherName,
        BehaviourDiff.Result result,
        BehaviourCompareSession.BehaviourDiffExport export,
        bool filtered)
    {
        _diff.Clear();
        string suffix = filtered ? $" ({export.Differences.Count} shown)" : "";
        string comparison = export.Differences.Count == 0
            ? export.OriginalCount == 0
                ? "the two files hold the same objects with the same values"
                : "no differences match the current filter"
            : result.ToString();
        SetDiffSummary($"{Path.GetFileName(_hkxPath)} against {otherName}: {comparison}{suffix}",
                       export.OriginalCount == 0 ? Ux.MetaBrush : Ux.TitleBrush);

        foreach (var group in new[] { BehaviourDiff.Kind.Changed, BehaviourDiff.Kind.Removed, BehaviourDiff.Kind.Added })
        {
            var lines = result.Lines.Where(l => l.Kind == group).ToList();
            if (lines.Count == 0) continue;

            var head = _diff.Add(null, group.ToString().ToLowerInvariant(), $"{lines.Count}")
                            .Colour(0, group == BehaviourDiff.Kind.Changed ? Ux.WarnBrush : Ux.CodeBrush)
                            .Colour(1, Ux.TitleBrush);
            if (lines.Count > 200) head.Collapse();

            foreach (var line in lines.Take(2000))
                _diff.Add(head, "", line.Class, line.Where, line.Was, line.Now)
                     .Colour(1, Ux.CodeBrush).Colour(2, Ux.TitleBrush)
                     .Colour(3, Ux.MetaBrush).Colour(4, Ux.MetaBrush);

            if (lines.Count > 2000)
                _diff.Add(head, "", $"and {lines.Count - 2000} more").Colour(1, Ux.MutedBrush);
        }

        if (export.OriginalCount == 0)
            _diff.Add(null, "", "no difference", "the two files hold the same objects with the same values")
                 .Colour(2, Ux.MutedBrush);
    }

    private async Task ExportDiff(bool json)
    {
        if (_diffResult is not { } result)
        {
            SetDiffSummary("Compare two files before exporting a diff.", Ux.MutedBrush);
            return;
        }

        var export = BehaviourCompareSession.CreateExport(result, SelectedDiffFilter());
        string extension = json ? ".json" : ".txt";
        string baseName = Path.GetFileNameWithoutExtension(_hkxPath);
        var picked = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = json ? "Export behaviour diff as JSON" : "Export behaviour diff as text",
            SuggestedFileName = (baseName.Length > 0 ? baseName : "behaviour") + "-diff" + extension,
            SuggestedStartLocation = await StartFolder(),
            DefaultExtension = extension[1..],
            FileTypeChoices = new[]
            {
                new FilePickerFileType(json ? "JSON" : "Text") { Patterns = new[] { "*" + extension } },
            },
        });

        string? path = picked?.TryGetLocalPath();
        if (path == null) return;

        try
        {
            string content = json
                ? BehaviourCompareSession.ExportJson(export)
                : BehaviourCompareSession.ExportText(export);
            await File.WriteAllTextAsync(path, content);
            SetDiffSummary($"Exported {export.Differences.Count} differences to {Path.GetFileName(path)}.",
                           Ux.MetaBrush);
        }
        catch (Exception error)
        {
            SetDiffSummary($"Could not export the diff: {error.Message}", Ux.BadBrush);
        }
    }

    private void ClearDiff()
    {
        _diffResult = null;
        _diffOtherName = "";
        _diff.Clear();
        _diffClass.ItemsSource = new[] { "All classes" };
        _diffClass.SelectedIndex = 0;
        _diffExportText.IsEnabled = false;
        _diffExportJson.IsEnabled = false;
    }

    private void SetDiffSummary(string text, IBrush brush)
    {
        _diffSummary.Text = text;
        _diffSummary.Foreground = brush;
    }

    public HkGrid DiffGrid => _diff;

    public HkGrid ClipGrid => _clips;

}
