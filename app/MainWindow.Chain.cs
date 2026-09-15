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
    private Control BuildChainTab()
    {
        _dataField.Text = Settings.Get("gameDataFolder");
        _dataField.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) ApplyGameData();
        };
        var browse = Ux.Secondary("Browse...");
        browse.Click += async (_, _) => await PickGameDataFolder();

        _dataSummary.Text = "no game data attached";
        var bar = Ux.Group("Game data", Ux.Wrap(_dataField, browse), Ux.Wrap(Ux.Pill(_dataSummary)));

        _modsField.Text = Settings.Get("gameModsFolder");
        _modsField.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) ApplyMods();
        };
        var modsBrowse = Ux.Secondary("Browse...");
        modsBrowse.Click += async (_, _) => await PickModsFolder();
        ToolTip.SetTip(modsBrowse, "The Mod Organizer 2 instance root (holds mods/, profiles/, overwrite/)");
        _modsSummary.Text = "no mods layered";
        var modsBar = Ux.Group("Mod Organizer layer", Ux.Wrap(_modsField, modsBrowse),
            Ux.Wrap(Ux.Pill(_modsSummary)));

        _crashField.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) ResolveCrashHash();
        };
        var resolve = Ux.Secondary("Resolve");
        resolve.Click += (_, _) => ResolveCrashHash();
        _crashSummary.Text = "paste a hash and press Resolve";
        var crashBar = Ux.Group("Crash hash", Ux.Wrap(_crashField, resolve), Ux.Wrap(Ux.Pill(_crashSummary)));

        var sweep = Ux.Secondary("Sweep all subgraphs");
        sweep.Click += (_, _) => RunSweep();
        ToolTip.SetTip(sweep, "Run the whole-load-order per-weapon check across every AnimationFileData manifest");
        _sweepSummary.Text = "one-click whole-load-order per-weapon check";
        var sweepBar = Ux.Group("Project sweep", Ux.Wrap(sweep), Ux.Wrap(Ux.Pill(_sweepSummary)));

        var panel = new DockPanel();
        AddTop(panel, sweepBar);
        AddTop(panel, crashBar);
        AddTop(panel, modsBar);
        AddTop(panel, bar);
        panel.Children.Add(_chain);
        return panel;
    }

    private async Task PickGameDataFolder()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "The game Data folder (holds the .ba2 archives)",
            AllowMultiple = false,
        });

        string? folder = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (folder == null) return;
        _dataField.Text = folder;
        ApplyGameData();
    }

    private void ApplyGameData()
    {
        string folder = (_dataField.Text ?? "").Trim();
        if (folder.Length == 0)
        {
            Settings.TrySet("gameDataFolder", "", out _);
            _dataSummary.Text = "no game data attached";
        }
        else if (!Directory.Exists(folder))
        {
            _dataSummary.Text = "that folder is not there";
            return;
        }
        else
        {
            Settings.TrySet("gameDataFolder", folder, out _);
            _dataSummary.Text = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        }

        if (_hkxPath.Length > 0) BuildChain();
    }

    private async Task PickModsFolder()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "The Mod Organizer 2 instance root (holds mods/, profiles/, overwrite/)",
            AllowMultiple = false,
        });

        string? folder = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (folder == null) return;
        _modsField.Text = folder;
        ApplyMods();
    }

    private void ApplyMods()
    {
        string folder = (_modsField.Text ?? "").Trim();
        if (folder.Length == 0)
        {
            Settings.TrySet("gameModsFolder", "", out _);
            _modsSummary.Text = "no mods layered";
        }
        else if (!Directory.Exists(folder))
        {
            _modsSummary.Text = "that folder is not there";
            return;
        }
        else
        {
            Settings.TrySet("gameModsFolder", folder, out _);
        }

        if (_hkxPath.Length > 0) BuildChain();
    }

    private void ResolveCrashHash()
    {
        string input = (_crashField.Text ?? "").Trim();
        if (input.Length == 0) return;

        var data = _gameData;
        if (data == null)
        {
            _crashSummary.Text = "set a Game Data folder first";
            return;
        }

        ulong? id = OpenCommonwealth.Services.Archive.SubgraphIndex.ExtractSubgraphHash(input);
        if (id == null)
        {
            _crashSummary.Text = "no AnimTextData hash in that input";
            return;
        }

        var index = OpenCommonwealth.Services.Archive.SubgraphIndex.Discover(data);
        var sub = index.Find(id.Value);
        var off = index.FindOffsetData(id.Value);
        if (sub == null && off == null)
        {
            _crashSummary.Text = $"{id.Value}: no manifest or offset data in the game data";
            return;
        }

        if (_crashResolved && _hkxPath.Length > 0) BuildChain();
        _crashResolved = true;

        string primary = sub != null ? sub.PrimaryBehavior : off!.FirstPathHint ?? "";
        _crashSummary.Text = $"{id.Value} -> {Path.GetFileName(primary)}";
        var head = _chain.Add(null, "crash hash", $"{id.Value} -> {primary}")
                         .Colour(0, Ux.MutedBrush).Colour(1, Ux.TitleBrush);

        int present = 0, total = 0;
        if (sub != null)
        {
            foreach (string behavior in sub.BehaviorPaths.Take(4))
                _chain.Add(head, "behavior", behavior).Colour(1, Ux.CodeBrush);

            total = sub.AnimationPaths.Count;
            present = sub.AnimationPaths.Count(path =>
                data.ContainsAnimation(Path.Combine(data.DataFolder, "Meshes"), path));
            _chain.Add(head, "animations", $"{present} present, {total - present} missing")
                   .Colour(1, present == total ? Ux.MetaBrush : Ux.BadBrush);
        }
        if (off != null)
            _chain.Add(head, "offset data", $"exists ({off.Bytes} bytes), hint: {off.FirstPathHint}")
                   .Colour(1, Ux.MetaBrush);

        var behaviorPaths = new List<string>();
        if (sub != null) behaviorPaths.AddRange(sub.BehaviorPaths);
        if (off?.FirstPathHint != null &&
            !behaviorPaths.Contains(off.FirstPathHint, StringComparer.OrdinalIgnoreCase))
            behaviorPaths.Add(off.FirstPathHint);

        var (weaponSubgraph, gaps) = OpenCommonwealth.Services.Archive.SubgraphIndex.WeaponGapFindings(data, behaviorPaths);
        if (!weaponSubgraph)
        {
            _chain.Add(head, "per-weapon", "not a weapon subgraph").Colour(1, Ux.MetaBrush);
            ShowCrashPanel(id.Value, primary, behaviorPaths, present, total,
                           new List<GraphValidator.Finding>());
        }
        else if (gaps.Count == 0)
        {
            _chain.Add(head, "per-weapon", "every clip resolves for every weapon type").Colour(1, Ux.MetaBrush);
            ShowCrashPanel(id.Value, primary, behaviorPaths, present, total,
                           new List<GraphValidator.Finding>());
        }
        else
        {
            foreach (var f in gaps.Take(6))
                _chain.Add(head, "per-weapon", f.What).Colour(1, Ux.WarnBrush);
            ShowCrashPanel(id.Value, primary, behaviorPaths, present, total, gaps);
        }
    }

    private async void RunSweep()
    {
        var data = _gameData;
        if (data == null)
        {
            _sweepSummary.Text = "set a Game Data folder first";
            return;
        }
        if (_sweeping) return;
        _sweeping = true;
        _sweepSummary.Text = "sweeping every subgraph…";
        try
        {
            var result = await Task.Run(() =>
                OpenCommonwealth.Services.Archive.SubgraphIndex.Sweep(data,
                    n => Dispatcher.UIThread.Post(() => _sweepSummary.Text = n)));
            RenderSweep(result);
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            _sweepSummary.Text = "the sweep could not read the game data";
        }
        finally
        {
            _sweeping = false;
        }
    }

    private void RenderSweep(OpenCommonwealth.Services.Archive.SubgraphIndex.SweepResult result)
    {
        _sweepSummary.Text = result.Failures.Count == 0
            ? $"{result.ManifestCount} manifests, {result.WeaponSubgraphsChecked} weapon subgraphs, all clean"
            : $"{result.Failures.Count} subgraph(s) with per-weapon gaps across {result.ManifestCount} manifests";

        var head = _chain.Add(null, "sweep",
            $"{result.ManifestCount} manifests, {result.ArchiveCount} archives" +
            (result.ModRootCount > 0 ? $", {result.ModRootCount} mod roots" : ""),
            $"{result.WeaponSubgraphsChecked} weapon subgraphs checked, {result.Failures.Count} with gaps")
            .Colour(0, Ux.MutedBrush).Colour(1, Ux.TitleBrush)
            .Colour(2, result.Failures.Count == 0 ? Ux.MetaBrush : Ux.BadBrush);

        foreach (var fail in result.Failures.Take(20))
        {
            var row = _chain.Add(head, "FAIL", $"{fail.Id} ({Path.GetFileName(fail.Behavior)})",
                                 $"{fail.Gaps.Count} gap(s)")
                .Colour(0, Ux.BadBrush).Colour(1, Ux.WarnBrush).Colour(2, Ux.BadBrush);
            foreach (var f in fail.Gaps.Take(6))
                _chain.Add(row, "per-weapon", f.What).Colour(1, Ux.WarnBrush);
            if (fail.Gaps.Count > 6)
                _chain.Add(row, "per-weapon", $"... and {fail.Gaps.Count - 6} more")
                       .Colour(1, Ux.MutedBrush);
        }
        if (result.Failures.Count > 20)
            _chain.Add(head, "sweep", $"... and {result.Failures.Count - 20} more failing subgraphs")
                   .Colour(1, Ux.MutedBrush);
    }

    private Border BuildCrashPanel()
    {
        var close = Ux.Secondary("Close");
        close.Click += (_, _) => _crashPanel!.IsVisible = false;

        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(_crashPanelTitle);
        head.Children.Add(close);

        var root = new StackPanel { Spacing = 6 };
        root.Children.Add(head);
        root.Children.Add(new ScrollViewer
        {
            MaxHeight = 280,
            Content = _crashPanelBody,
        });

        return _crashPanel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(240, Ux.Card.R, Ux.Card.G, Ux.Card.B)),
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            MaxWidth = 480,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 10, 10, 0),
            IsVisible = false,
            Child = root,
        };
    }

    private void ShowCrashPanel(ulong id, string primary, List<string> behaviors,
                                int present, int total, List<GraphValidator.Finding> gaps)
    {
        _crashPanelBody.Children.Clear();
        _crashPanelTitle.Text = $"crash {id} -> {Path.GetFileName(primary)}";

        var lines = new StackPanel { Spacing = 2 };
        foreach (string behavior in behaviors.Take(4))
            lines.Children.Add(new TextBlock
            {
                Text = behavior,
                Foreground = Ux.CodeBrush,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        lines.Children.Add(new TextBlock
        {
            Text = $"{present} of {total} animations present",
            Foreground = present == total ? Ux.MetaBrush : Ux.BadBrush,
            FontSize = 11,
        });

        if (gaps.Count == 0)
        {
            lines.Children.Add(new TextBlock
            {
                Text = "every clip resolves for every weapon type",
                Foreground = Ux.MetaBrush,
                FontSize = 11,
            });
        }
        else
        {
            lines.Children.Add(new TextBlock
            {
                Text = $"{gaps.Count} missing clip(s) — jump to each on the graph",
                Foreground = Ux.WarnBrush,
                FontSize = 11,
            });
            foreach (var gap in gaps.Take(6))
                lines.Children.Add(CrashGapRow(gap));
        }

        _crashPanelBody.Children.Add(lines);
        _crashPanel!.IsVisible = true;
    }

    private Control CrashGapRow(GraphValidator.Finding gap)
    {
        var text = new TextBlock
        {
            Text = gap.What,
            Foreground = Ux.WarnBrush,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 330,
        };
        var jump = Ux.Secondary(gap.ObjectId.Length > 0 ? "Jump" : "—");
        jump.IsEnabled = gap.ObjectId.Length > 0;
        jump.Tag = gap.ObjectId;
        jump.Click += (_, _) => JumpToClip(gap.ObjectId);
        ToolTip.SetTip(jump, gap.What);
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(jump, Dock.Right);
        row.Children.Add(jump);
        row.Children.Add(text);
        return row;
    }

    private void JumpToClip(string objectId)
    {
        if (objectId.Length == 0) return;
        GoToTab("Graph");
        if (_graph.FocusOn(objectId))
        {
            SelectObjectId(objectId);
            HighlightPaths(objectId);
        }
        else
        {
            SetStatus($"clip #{objectId} is not on the current graph", Ux.MutedBrush);
        }
    }

    private bool _crashResolved;

    private void BuildChain()
    {
        _chain.Clear();
        _gameData?.Dispose();
        _gameData = null;

        GameData? data = null;
        string folder = Settings.Get("gameDataFolder");
        if (folder.Length > 0 && Directory.Exists(folder))
        {
            try
            {
                bool modded = false;
                string mods = Settings.Get("gameModsFolder");
                if (mods.Length > 0 && Directory.Exists(mods))
                {
                    string? profile = GameData.ModlistProfile(mods);
                    if (profile != null || File.Exists(Path.Combine(mods, "modlist.txt")))
                    {
                        data = _gameData = GameData.DiscoverModded(folder, mods, profile);
                        modded = true;
                        _modsSummary.Text = profile != null
                            ? $"{data.ModRoots.Count} mod root(s), profile {profile}"
                            : $"{data.ModRoots.Count} mod root(s)";
                    }
                    else
                    {
                        _modsSummary.Text = "no modlist.txt under that folder";
                    }
                }
                else if (mods.Length > 0)
                {
                    _modsSummary.Text = "the saved mods folder is not there now";
                }
                else
                {
                    _modsSummary.Text = "no mods layered";
                }

                if (data == null)
                    data = _gameData = GameData.Discover(folder);

                string summary = $"{data.ArchivePaths.Count} .ba2 archive(s)";
                if (data.PluginsPath != null)
                    summary += $", ordered by {Path.GetFileName(data.PluginsPath)}";
                if (modded) summary += " + mods";
                _dataSummary.Text = summary;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _dataSummary.Text = "game data could not be read";
            }
        }
        else if (folder.Length > 0)
        {
            _dataSummary.Text = "the saved folder is not there now";
        }

        var chain = ProjectChain.Resolve(_hkxPath, data: data);
        _projectChain = chain;

        if (data != null)
            _chain.Add(null, "game data", folder, $"{data.ArchivePaths.Count} .ba2 archive(s)",
                       data.PluginsPath != null ? Path.GetFileName(data.PluginsPath) : "")
                  .Colour(0, Ux.MutedBrush).Colour(1, Ux.TitleBrush).Colour(2, Ux.MetaBrush);

        foreach (var link in chain.Links)
            _chain.Add(null, link.Role, link.Declared, link.Exists ? "found" : "MISSING", link.Note)
                  .Colour(0, Ux.MutedBrush).Colour(1, Ux.TitleBrush)
                  .Colour(2, link.Exists ? Ux.MetaBrush : Ux.BadBrush);

        AddChainAnimations(chain);
        AddChainGroup("bones", $"{chain.Bones.Count} in the skeleton", chain.Bones, Ux.MetaBrush);

        foreach (string problem in chain.Problems)
            _chain.Add(null, "problem", problem).Colour(0, Ux.BadBrush).Colour(1, Ux.BadBrush);

        AddWeaponGaps(chain);
        FindMeshForFile();
    }

}
