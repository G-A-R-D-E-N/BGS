using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BehaviourStudio.App;
using OpenCommonwealth.Services.Archive;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

[CollectionDefinition("ScanCli", DisableParallelization = true)]
public sealed class ScanCliCollection { }

[Collection("ScanCli")]
public sealed class ScanCliTests
{
    private static string Fixture(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures/vanilla", relative.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void ScanArchivesCleanDirEmitsSingleJsonToStdoutExit0()
    {
        using var dir = new TempDir("bgs-scan-clean");
        var (exit, stdout, stderr) = Capture(() =>
            ModlistScan.Run(new[] { "--scan-archives", dir.Path, "--json" }));

        Assert.Equal(0, exit);
        var json = ParseSingle(stdout);
        Assert.Equal("scan-archives", json.GetProperty("mode").GetString());
        Assert.Equal(exit, json.GetProperty("exitCode").GetInt32());
        Assert.Empty(json.GetProperty("findings").EnumerateArray());
        Assert.Equal(new[] { "archives", "corrupt", "roundTripLosses", "scanned" }, SortedKeys(json.GetProperty("summary")));
        Assert.Equal(ScanReport.CurrentSchemaVersion, json.GetProperty("schemaVersion").GetInt32());
        Assert.DoesNotContain("scanning", stdout);
        Assert.Contains("scanning", stderr);
    }

    [Fact]
    public void ScanArchivesBadDirEmitsJsonUsageExit2()
    {
        string bad = Path.Combine(Path.GetTempPath(), "bgs-no-such-" + Guid.NewGuid().ToString("N"));
        var (exit, stdout, stderr) = Capture(() =>
            ModlistScan.Run(new[] { "--scan-archives", bad, "--json" }));

        Assert.Equal(2, exit);
        var json = ParseSingle(stdout);
        Assert.Equal(2, json.GetProperty("exitCode").GetInt32());
        Assert.Equal("usage", json.GetProperty("findings")[0].GetProperty("kind").GetString());
        Assert.Equal("", stderr.Trim());
    }

    [Fact]
    public void ScanArchivesHumanModeIsTextNotJson()
    {
        using var dir = new TempDir("bgs-scan-human");
        var (exit, stdout, _) = Capture(() =>
            ModlistScan.Run(new[] { "--scan-archives", dir.Path }));

        Assert.Equal(0, exit);
        Assert.Contains("done", stdout);
        Assert.DoesNotContain("\"mode\"", stdout);
    }

    [Fact]
    public void ScanArchivesSkipIsWarningLevelKeptInJsonAndDoesNotFailExit()
    {
        using var dir = new TempDir("bgs-scan-skip");
        File.WriteAllText(Path.Combine(dir.Path, "broken.ba2"), "this is not a BTDX archive");

        var (exit, stdout, _) = Capture(() =>
            ModlistScan.Run(new[] { "--scan-archives", dir.Path, "--json", "--errors-only" }));

        Assert.Equal(0, exit);
        var json = ParseSingle(stdout);
        Assert.Equal(0, json.GetProperty("exitCode").GetInt32());
        Assert.Contains("skip", Kinds(json));
    }

    [Fact]
    public void ScanClipsBadInstanceEmitsJsonExit2()
    {
        string bad = Path.Combine(Path.GetTempPath(), "bgs-no-mo2-" + Guid.NewGuid().ToString("N"));
        var (exit, stdout, _) = Capture(() =>
            ModlistScan.ScanClips(new[] { "--scan-clips", bad, "--json" }));

        Assert.Equal(2, exit);
        Assert.Equal(2, ParseSingle(stdout).GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void ScanClipsFlagsAClipWhoseAnimationResolvesNowhere()
    {
        using var mo2 = new Mo2Instance();
        byte[] behaviour = BatchAnimationBuilder.Build(
            Source("hkbStateMachine"),
            NativeGraphModel.FirstId,
            new[] { new BatchAnimationBuilder.Entry("Idle", "Animations\\NoSuchAnim.hkx") }).Bytes;
        mo2.AddModFile("Meshes/Actors/Test/Behaviors/test.hkx", behaviour);

        var (exit, stdout, _) = Capture(() =>
            ModlistScan.ScanClips(new[] { "--scan-clips", mo2.Instance, "--json" }));

        Assert.Equal(1, exit);
        var json = ParseSingle(stdout);
        Assert.Equal(exit, json.GetProperty("exitCode").GetInt32());
        Assert.Equal(new[] { "broken", "checked", "missingClips", "roundTripLosses", "unreadable" }, SortedKeys(json.GetProperty("summary")));
        Assert.Contains("missing-clip-animations", Kinds(json));
    }

    [Fact]
    public void ScanModlistBadInstanceEmitsJsonExit2()
    {
        string bad = Path.Combine(Path.GetTempPath(), "bgs-no-mo2-" + Guid.NewGuid().ToString("N"));
        var (exit, stdout, _) = Capture(() =>
            ModlistScan.ScanModlist(new[] { "--scan-modlist", bad, "--json" }));

        Assert.Equal(2, exit);
        Assert.Equal(2, ParseSingle(stdout).GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void ScanArchivesEmitsProgressToStderrNotStdoutInJson()
    {
        using var dir = new TempDir("bgs-scan-progress");
        string behaviors = Path.Combine(dir.Path, "meshes", "actors", "x", "behaviors");
        Directory.CreateDirectory(behaviors);
        File.WriteAllBytes(Path.Combine(behaviors, "a.hkx"), new byte[] { 0, 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(behaviors, "b.hkx"), new byte[] { 4, 5, 6, 7 });

        var (exit, stdout, stderr) = Capture(() =>
            ModlistScan.Run(new[] { "--scan-archives", dir.Path, "--json" }));

        Assert.Equal(0, exit);
        ParseSingle(stdout);
        Assert.DoesNotContain("checked", stdout);
        Assert.Contains("checked", stderr);
    }

    [Fact]
    public void ScanClipsPassesWhenTheClipAnimationIsPresent()
    {
        using var mo2 = new Mo2Instance();
        byte[] behaviour = BatchAnimationBuilder.Build(
            Source("hkbStateMachine"),
            NativeGraphModel.FirstId,
            new[] { new BatchAnimationBuilder.Entry("Idle", "Animations\\Idle.hkx") }).Bytes;
        mo2.AddModFile("Meshes/Actors/Test/Behaviors/test.hkx", behaviour);
        mo2.AddModFile("Meshes/Actors/Test/Animations/Idle.hkx", new byte[] { 1, 2, 3 });

        var (exit, stdout, _) = Capture(() =>
            ModlistScan.ScanClips(new[] { "--scan-clips", mo2.Instance, "--json" }));

        Assert.Equal(0, exit);
        var json = ParseSingle(stdout);
        Assert.Equal(0, json.GetProperty("exitCode").GetInt32());
        Assert.Empty(json.GetProperty("findings").EnumerateArray());
    }

    [Fact]
    public void ScanClipsReportsRoundTripLossFromResolvedAnimation()
    {
        using var mo2 = new Mo2Instance();
        byte[] behaviour = BatchAnimationBuilder.Build(
            Source("hkbStateMachine"),
            NativeGraphModel.FirstId,
            new[] { new BatchAnimationBuilder.Entry("Idle", "Animations\\Unsafe.hkx") }).Bytes;
        mo2.AddModFile("Meshes/Actors/Test/Behaviors/test.hkx", behaviour);
        mo2.AddModFile(
            "Meshes/Actors/Test/Animations/Unsafe.hkx",
            File.ReadAllBytes(Fixture("Meshes/Actors/Character/CharacterAssets/Hair/Female/FemaleHair04.hkx")));

        var (exit, stdout, _) = Capture(() =>
            ModlistScan.ScanClips(new[] { "--scan-clips", mo2.Instance, "--json" }));

        Assert.Equal(1, exit);
        var json = ParseSingle(stdout);
        Assert.Equal(0, json.GetProperty("summary").GetProperty("missingClips").GetInt32());
        Assert.True(json.GetProperty("summary").GetProperty("roundTripLosses").GetInt32() > 0);

        var roundTrips = json.GetProperty("findings").EnumerateArray()
            .Where(f => f.GetProperty("kind").GetString() == "roundtrip-loss")
            .ToList();
        var animationFinding = Assert.Single(roundTrips.Where(f =>
            f.GetProperty("path").GetString()!.Replace('\\', '/')
                .EndsWith("Animations/Unsafe.hkx", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(animationFinding.GetProperty("details").EnumerateArray(), detail =>
            detail.GetString()!.Contains("UnsupportedClass", StringComparison.Ordinal));
    }

    [Fact]
    public void ArchiveIsParsedOnceAcrossDiscoveryAndResolution()
    {
        using var mo2 = new Mo2Instance();
        mo2.AddModArchive("Meshes/Actors/Test/Behaviors/main.hkx", new byte[] { 1, 2, 3 });

        Assert.True(Mo2ScanData.TryOpen(mo2.Instance, out var scan, out string error), error);
        using (scan!)
        {
            Ba2.OpenCountForTest = 0;
            var found = scan.ModHkxPaths("behaviors");
            Assert.Contains(found, p => p.Replace('\\', '/').EndsWith("behaviors/main.hkx", StringComparison.OrdinalIgnoreCase));
            Assert.True(scan.ExistsDataRelative("Meshes/Actors/Test/Behaviors/main.hkx"));
            Assert.Equal(1, Ba2.OpenCountForTest);
        }
    }

    private static void WriteSingleEntryArchive(string path, string entryName, byte[] payload)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        long headerEnd = 24 + 36;
        long nameTableAt = headerEnd + payload.Length;

        writer.Write(new[] { 'B', 'T', 'D', 'X' });
        writer.Write(1u);
        writer.Write(new[] { 'G', 'N', 'R', 'L' });
        writer.Write(1u);
        writer.Write((ulong)nameTableAt);

        writer.Write(0u); writer.Write(0u); writer.Write(0u); writer.Write(0u);
        writer.Write((ulong)headerEnd);
        writer.Write(0u);
        writer.Write((uint)payload.Length);
        writer.Write(0u);

        writer.Write(payload);
        var bytes = Encoding.UTF8.GetBytes(entryName.Replace('/', '\\'));
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    private static (int Exit, string Stdout, string Stderr) Capture(Func<int> action)
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        var savedOut = Console.Out;
        var savedErr = Console.Error;
        try
        {
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            int exit = action();
            return (exit, outWriter.ToString(), errWriter.ToString());
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }
    }

    private static JsonElement ParseSingle(string stdout)
    {
        using var doc = JsonDocument.Parse(stdout);
        return doc.RootElement.Clone();
    }

    private static string[] SortedKeys(JsonElement obj) =>
        obj.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private static List<string> Kinds(JsonElement report) =>
        report.GetProperty("findings").EnumerateArray()
            .Select(f => f.GetProperty("kind").GetString()!).ToList();

    private static byte[] Source(params string[] classes)
    {
        var image = new PackfileImage();
        image.Sections.Add(new PackfileSection { TagBytes = Tag("__classnames__") });
        image.Sections.Add(new PackfileSection { TagBytes = Tag("__data__") });
        foreach (string className in classes) NativeAppend.Object(image, className);
        FixupOrder.Reorder(image);
        return image.Rebuild();
    }

    private static byte[] Tag(string name)
    {
        var bytes = new byte[20];
        Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        return bytes;
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir(string prefix) => Path = Directory.CreateTempSubdirectory(prefix).FullName;
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed class Mo2Instance : IDisposable
    {
        private readonly TempDir _root = new("bgs-scan-mo2");
        public string Instance => Path.Combine(_root.Path, "instance");
        private string GameRoot => Path.Combine(_root.Path, "game");
        private string Mod => Path.Combine(Instance, "mods", "TestMod");

        public Mo2Instance()
        {
            Directory.CreateDirectory(Path.Combine(GameRoot, "Data"));
            Directory.CreateDirectory(Mod);
            Directory.CreateDirectory(Path.Combine(Instance, "profiles", "Play"));
            File.WriteAllText(Path.Combine(Instance, "profiles", "Play", "modlist.txt"), "+TestMod\n");
            File.WriteAllText(Path.Combine(Instance, "profiles", "Play", "plugins.txt"), "");
            string escapedGame = GameRoot.Replace("\\", "\\\\");
            File.WriteAllText(
                Path.Combine(Instance, "ModOrganizer.ini"),
                "selected_profile=@ByteArray(Play)\n" +
                $"gamePath=@ByteArray({escapedGame})\n" +
                "base_directory=@ByteArray(%BASE_DIR%)\n" +
                "overwrite_directory=@ByteArray(%BASE_DIR%/overwrite)\n");
        }

        public void AddModFile(string relative, byte[] bytes)
        {
            string path = Path.Combine(Mod, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        public void AddModArchive(string entryName, byte[] payload) =>
            WriteSingleEntryArchive(Path.Combine(Mod, "Test - Main.ba2"), entryName, payload);

        public void Dispose() => _root.Dispose();
    }
}
