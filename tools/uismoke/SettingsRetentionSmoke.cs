using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using BehaviourStudio.App;

namespace BehaviourStudio.UiSmoke;

internal static class SettingsRetentionSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Directory.CreateTempSubdirectory("bgs-settings-retention").FullName;
        string settingsPath = Path.Combine(root, "settings.cfg");
        string? previousPath = Settings.SettingsPathForTest;
        Func<string, string[]> previousReader = Settings.ReadAllLinesForTest;

        try
        {
            Settings.SettingsPathForTest = settingsPath;
            Settings.ReadAllLinesForTest = File.ReadAllLines;
            Settings.Set("ordinary-preference", "survives");

            var point = new Dictionary<string, Settings.LayoutPoint>(StringComparer.Ordinal)
            {
                ["#1"] = new Settings.LayoutPoint(10, 20),
            };

            var paths = Enumerable.Range(0, Settings.MaxRememberedGraphLayouts)
                .Select(i => Path.Combine(root, $"graph-{i}.hkx"))
                .ToArray();

            foreach (string path in paths)
                Require(Settings.TrySetGraphLayout(path, point, out string failure), failure);

            Require(Settings.TrySetGraphLayout(paths[0], point, out string refreshFailure), refreshFailure);
            string newest = Path.Combine(root, "graph-newest.hkx");
            Require(Settings.TrySetGraphLayout(newest, point, out string newestFailure), newestFailure);

            string[] lines = File.ReadAllLines(settingsPath);
            int remembered = lines.Count(line =>
                line.StartsWith("graph-layout.", StringComparison.Ordinal));

            Require(remembered == Settings.MaxRememberedGraphLayouts,
                $"expected {Settings.MaxRememberedGraphLayouts} graph layouts, found {remembered}");
            Require(Settings.GetGraphLayout(paths[0]).Count == 1,
                "the refreshed oldest graph layout was pruned");
            Require(Settings.GetGraphLayout(paths[1]).Count == 0,
                "the least-recently-used graph layout was retained");
            Require(Settings.GetGraphLayout(newest).Count == 1,
                "the newest graph layout was not retained");
            Require(Settings.Get("ordinary-preference") == "survives",
                "pruning graph layouts removed an unrelated preference");

            Console.WriteLine("settings retention smoke passed");
        }
        finally
        {
            Settings.SettingsPathForTest = previousPath;
            Settings.ReadAllLinesForTest = previousReader;
            try { Directory.Delete(root, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void Require(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException("settings retention smoke failed: " + failure);
    }
}
