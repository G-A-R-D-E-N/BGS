using System;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class SaveRecoveryRegressionTests
{
    [Fact]
    public void ConcurrentRecoveryDoesNotConsumeALivePreviousSidecar()
    {
        using var root = new TempDirectory("bgs-live-sidecar");
        string path = Path.Combine(root.Path, "graph.hkx");
        byte[] original = { 1, 2, 3, 4 };
        byte[] replacement = { 5, 6, 7, 8 };
        File.WriteAllBytes(path, original);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromDays(3));
        var stamp = DocumentSourceStamp.Capture(path);

        Exception? error = Record.Exception(() =>
            FileSafety.ReplaceChecked(
                path,
                replacement,
                stamp,
                afterReplace: () =>
                {
                    Assert.Single(Directory.EnumerateFiles(root.Path, "*.previous"));
                    Assert.Single(Directory.EnumerateFiles(root.Path, "*.saving"));
                    FileSafety.RecoverInterrupted(path);
                    Assert.Single(Directory.EnumerateFiles(root.Path, "*.previous"));
                }));

        Assert.Null(error);
        Assert.Equal(replacement, File.ReadAllBytes(path));
        Assert.Equal(original, File.ReadAllBytes(path + ".bak"));
        Assert.Empty(Directory.EnumerateFiles(root.Path, "*.previous"));
        Assert.Empty(Directory.EnumerateFiles(root.Path, "*.saving"));
    }

    [Fact]
    public void RecoveryRestoresTheNewestStalePreviousWhenTheTargetIsMissing()
    {
        using var root = new TempDirectory("bgs-missing-target");
        string path = Path.Combine(root.Path, "graph.hkx");
        string older = path + "." + new string('a', 64) + "." + new string('3', 32) + ".previous";
        string newer = path + "." + new string('b', 64) + "." + new string('2', 32) + ".previous";
        File.WriteAllBytes(older, new byte[] { 1, 2, 3, 4 });
        File.WriteAllBytes(newer, new byte[] { 5, 6, 7, 8 });
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow - TimeSpan.FromMinutes(30));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow - TimeSpan.FromMinutes(20));

        FileSafety.RecoverInterrupted(path);

        Assert.Equal(new byte[] { 5, 6, 7, 8 }, File.ReadAllBytes(path));
        Assert.False(File.Exists(older));
        Assert.False(File.Exists(newer));
    }

    [Fact]
    public void ABackupThatCannotBeRotatedDoesNotFailAnAlreadyWrittenSave()
    {
        using var root = new TempDirectory("bgs-rotation-failure");
        string path = Path.Combine(root.Path, "graph.hkx");
        byte[] original = { 1, 2, 3, 4 };
        byte[] replacement = { 5, 6, 7, 8 };
        File.WriteAllBytes(path, original);
        var stamp = DocumentSourceStamp.Capture(path);

        // Rotation moves .bak onto .bak.1; a directory there makes that move fail.
        File.WriteAllBytes(path + ".bak", new byte[] { 9 });
        Directory.CreateDirectory(path + ".bak.1");

        Exception? error = Record.Exception(() =>
            FileSafety.ReplaceChecked(path, replacement, stamp));

        Assert.Null(error);
        Assert.Equal(replacement, File.ReadAllBytes(path));
        Assert.Single(Directory.EnumerateFiles(root.Path, "*.previous"));
    }

    [Fact]
    public void RecoveryLeavesSiblingsThatAreNotGeneratedArtifactsAlone()
    {
        using var root = new TempDirectory("bgs-foreign-siblings");
        string path = Path.Combine(root.Path, "graph.hkx");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });

        var foreign = new[]
        {
            path + ".manual.previous",
            path + ".manual.writing",
            path + ".manual.saving",
            path + "." + new string('z', 64) + "." + new string('z', 32) + ".previous",
            path + "." + new string('a', 63) + "." + new string('b', 32) + ".previous",
        };
        foreach (string file in foreign)
        {
            File.WriteAllBytes(file, new byte[] { 42 });
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow - TimeSpan.FromDays(1));
        }

        FileSafety.RecoverInterrupted(path);

        foreach (string file in foreign)
            Assert.True(File.Exists(file), $"{Path.GetFileName(file)} was consumed by save recovery");
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void RecoveryLeavesAForeignSiblingWhenTheTargetIsMissing()
    {
        using var root = new TempDirectory("bgs-foreign-missing-target");
        string path = Path.Combine(root.Path, "graph.hkx");
        string foreign = path + ".manual.previous";
        File.WriteAllBytes(foreign, new byte[] { 42 });
        File.SetLastWriteTimeUtc(foreign, DateTime.UtcNow - TimeSpan.FromDays(1));

        FileSafety.RecoverInterrupted(path);

        Assert.True(File.Exists(foreign));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void SavingAFileWhoseTargetVanishedRecoversItFirst()
    {
        using var root = new TempDirectory("bgs-missing-target-save");
        string path = Path.Combine(root.Path, "graph.hkx");
        string previous = path + "." + new string('c', 64) + "." + new string('d', 32) + ".previous";
        byte[] abandoned = { 1, 2, 3, 4 };
        File.WriteAllBytes(previous, abandoned);
        File.SetLastWriteTimeUtc(previous, DateTime.UtcNow - TimeSpan.FromMinutes(30));

        var result = DocumentSaveTransaction.Commit(path, "<hkpackfile/>", "<hkpackfile/>", null);

        Assert.False(result.Committed);
        Assert.Equal(abandoned, File.ReadAllBytes(path));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix) =>
            Path = Directory.CreateTempSubdirectory(prefix).FullName;

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
