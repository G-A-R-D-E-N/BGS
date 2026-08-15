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
        string older = path + ".aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.old.previous";
        string newer = path + ".bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.new.previous";
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
