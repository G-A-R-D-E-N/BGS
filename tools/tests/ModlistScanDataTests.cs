using System;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Archive;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class ModlistScanDataTests
{
    [Fact]
    public void UsesSelectedProfileGamePathAndCustomOverwrite()
    {
        using var fixture = new Mo2Fixture();

        Assert.True(Mo2ScanData.TryOpen(fixture.Instance, out var scan, out string error), error);
        using (scan!)
        {
            Assert.Equal("Play", scan.Info.Profile);
            Assert.Equal(Path.GetFullPath(fixture.GameData), scan.Info.GameDataFolder);
            Assert.Equal(Path.GetFullPath(fixture.Overwrite), scan.Info.OverwriteFolder);
            Assert.Equal(
                new[]
                {
                    Path.GetFullPath(fixture.Overwrite),
                    Path.GetFullPath(fixture.High),
                    Path.GetFullPath(fixture.Low),
                },
                scan.ModRootsHighToLow);
            Assert.Equal(Path.GetFullPath(fixture.Overwrite), scan.Data.ModRoots.Last());
        }
    }

    [Fact]
    public void ExplicitMissingSelectedProfileFailsClosed()
    {
        using var root = new TempDirectory("bgs-mo2-profile");
        Directory.CreateDirectory(Path.Combine(root.Path, "mods"));
        Directory.CreateDirectory(Path.Combine(root.Path, "profiles", "Other"));
        File.WriteAllText(Path.Combine(root.Path, "profiles", "Other", "modlist.txt"), "+Example\n");
        File.WriteAllText(
            Path.Combine(root.Path, "ModOrganizer.ini"),
            "selected_profile=@ByteArray(Missing)\n");

        Assert.False(Mo2ScanData.TryOpen(root.Path, out _, out string error));
        Assert.Contains("selected MO2 profile 'Missing'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void WinningReadAndCandidatesUseOverwriteCopyOnly()
    {
        using var fixture = new Mo2Fixture();
        string rel = Path.Combine("Meshes", "Actors", "Test", "Behaviors", "Main.hkx");

        WriteAsset(fixture.Low, rel, 1);
        WriteAsset(fixture.High, rel, 2);
        WriteAsset(fixture.Overwrite, rel, 3);

        Assert.True(Mo2ScanData.TryOpen(fixture.Instance, out var scan, out string error), error);
        using (scan!)
        {
            var candidates = scan.ModHkxPaths("behaviors");
            Assert.Single(candidates);
            Assert.Equal(GameData.Normalize(rel), GameData.Normalize(candidates[0]));

            var read = scan.ReadDataRelative(rel);
            Assert.NotNull(read);
            Assert.Equal(new byte[] { 3 }, read!.Bytes);
            Assert.Contains(
                Path.GetFileName(fixture.Overwrite),
                scan.DescribeSource(rel, read),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void WeaponSubfoldersIncludeLooseEnabledMods()
    {
        using var fixture = new Mo2Fixture();
        string rel = Path.Combine(
            "Meshes", "Actors", "Test", "Animations", "Weapon", "Rifle", "Reload.hkx");
        WriteAsset(fixture.Low, rel, 7);

        Assert.True(Mo2ScanData.TryOpen(fixture.Instance, out var scan, out string error), error);
        using (scan!)
        {
            var folders = scan.WeaponSubfolders("Meshes/Actors/Test");
            Assert.Contains(folders, f => f.Equals("Rifle", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void WriteAsset(string root, string relative, byte value)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new[] { value });
    }

    private sealed class Mo2Fixture : IDisposable
    {
        private readonly TempDirectory _root = new("bgs-mo2-scan");

        public string Instance => Path.Combine(_root.Path, "instance");
        public string GameRoot => Path.Combine(_root.Path, "game");
        public string GameData => Path.Combine(GameRoot, "Data");
        public string High => Path.Combine(Instance, "mods", "High");
        public string Low => Path.Combine(Instance, "mods", "Low");
        public string Overwrite => Path.Combine(Instance, "custom-overwrite");

        public Mo2Fixture()
        {
            Directory.CreateDirectory(GameData);
            Directory.CreateDirectory(High);
            Directory.CreateDirectory(Low);
            Directory.CreateDirectory(Overwrite);
            Directory.CreateDirectory(Path.Combine(Instance, "profiles", "Play"));

            File.WriteAllText(
                Path.Combine(Instance, "profiles", "Play", "modlist.txt"),
                "+High\n+Low\n");
            File.WriteAllText(
                Path.Combine(Instance, "profiles", "Play", "plugins.txt"),
                "");

            string escapedGame = GameRoot.Replace("\\", "\\\\");
            File.WriteAllText(
                Path.Combine(Instance, "ModOrganizer.ini"),
                "selected_profile=@ByteArray(Play)\n" +
                $"gamePath=@ByteArray({escapedGame})\n" +
                "base_directory=@ByteArray(%BASE_DIR%)\n" +
                "overwrite_directory=@ByteArray(%BASE_DIR%/custom-overwrite)\n");
        }

        public void Dispose() => _root.Dispose();
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix) =>
            Path = Directory.CreateTempSubdirectory(prefix).FullName;

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
