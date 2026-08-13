using System;
using System.IO;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Hkx;
using OpenCommonwealth.Services.Nif;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class InputFilePolicyTests
{
    [Fact]
    public void PackfileRead_RejectsOversizedSparseFileBeforeAllocation()
    {
        using var scope = new TempFile("oversized.hkx");
        scope.SetLength(InputFilePolicy.MaximumHkxBytes + 1);

        var failure = Assert.Throws<InvalidDataException>(() => PackfileImage.Read(scope.Path));

        Assert.Contains("oversized.hkx", failure.Message, StringComparison.Ordinal);
        Assert.Contains("HKX", failure.Message, StringComparison.Ordinal);
        Assert.Contains("512 MiB", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NifRead_RejectsOversizedSparseFileBeforeAllocation()
    {
        using var scope = new TempFile("oversized.nif");
        scope.SetLength(InputFilePolicy.MaximumNifBytes + 1);

        var failure = Assert.Throws<InvalidDataException>(() => NifFile.Read(scope.Path));

        Assert.Contains("oversized.nif", failure.Message, StringComparison.Ordinal);
        Assert.Contains("NIF", failure.Message, StringComparison.Ordinal);
        Assert.Contains("512 MiB", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Limits_AcceptTheBoundaryAndRejectTheNextByte()
    {
        InputFilePolicy.EnsureHkx(InputFilePolicy.MaximumHkxBytes);
        InputFilePolicy.EnsureNif(InputFilePolicy.MaximumNifBytes);

        Assert.Throws<InvalidDataException>(() =>
            InputFilePolicy.EnsureHkx(InputFilePolicy.MaximumHkxBytes + 1));
        Assert.Throws<InvalidDataException>(() =>
            InputFilePolicy.EnsureNif(InputFilePolicy.MaximumNifBytes + 1));
    }

    [Fact]
    public void BoundedReader_ReturnsEveryByteForNormalInputs()
    {
        using var scope = new TempFile("small.hkx");
        byte[] expected = { 0, 1, 2, 3, 4, 5, 255 };
        File.WriteAllBytes(scope.Path, expected);

        Assert.Equal(expected, InputFilePolicy.ReadHkx(scope.Path));
    }

    [Fact]
    public void SmallTruncatedFiles_StillReachFormatValidation()
    {
        using var hkx = new TempFile("truncated.hkx");
        File.WriteAllBytes(hkx.Path, new byte[PackfileImage.HeaderSize - 1]);
        var hkxFailure = Assert.Throws<InvalidDataException>(() => PackfileImage.Read(hkx.Path));
        Assert.Contains("Too small", hkxFailure.Message, StringComparison.Ordinal);

        using var nif = new TempFile("truncated.nif");
        File.WriteAllBytes(nif.Path, "Gamebryo"u8.ToArray());
        var nifFailure = Assert.Throws<InvalidDataException>(() => NifFile.Read(nif.Path));
        Assert.Contains("header", nifFailure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TempFile : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("bgs-input-policy").FullName;

        public TempFile(string name)
        {
            Path = System.IO.Path.Combine(_root, name);
            using var _ = File.Create(Path);
        }

        public string Path { get; }

        public void SetLength(long length)
        {
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Write, FileShare.None);
            stream.SetLength(length);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
