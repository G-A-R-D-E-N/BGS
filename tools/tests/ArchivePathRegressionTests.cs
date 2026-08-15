using System;
using System.IO;
using System.Text;
using OpenCommonwealth.Services.Archive;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class ArchivePathRegressionTests
{
    [Fact]
    public void ArchiveNamesWithColonAreRejectedBeforeExtraction()
    {
        using var root = new TempDirectory("bgs-ba2-colon");
        string archive = Path.Combine(root.Path, "colon.ba2");
        WriteBa2(archive, "a.hkx:stream", new byte[] { 1, 2, 3 });

        var error = Assert.Throws<InvalidDataException>(() => Ba2.Open(archive));
        Assert.Contains("path with a colon", error.Message, StringComparison.Ordinal);
    }

    private static void WriteBa2(string path, string entryName, byte[] payload)
    {
        byte[] name = Encoding.UTF8.GetBytes(entryName);
        const int headerBytes = 24;
        const int recordBytes = 36;
        ulong nameTable = headerBytes + recordBytes;
        ulong dataOffset = nameTable + 2u + (uint)name.Length;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("BTDX"));
        writer.Write((uint)1);
        writer.Write(Encoding.ASCII.GetBytes("GNRL"));
        writer.Write((uint)1);
        writer.Write(nameTable);
        writer.Write((uint)0);
        writer.Write((uint)0);
        writer.Write((uint)0);
        writer.Write((uint)0);
        writer.Write(dataOffset);
        writer.Write((uint)0);
        writer.Write((uint)payload.Length);
        writer.Write((uint)0);
        writer.Write((ushort)name.Length);
        writer.Write(name);
        writer.Write(payload);
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
