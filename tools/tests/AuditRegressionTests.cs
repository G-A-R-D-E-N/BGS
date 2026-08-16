using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Archive;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class AuditRegressionTests
{
    [Fact]
    public void GrowingEmptyArrayPreservesCapacityFlagsAndMarksEmbeddedStorage()
    {
        var image = NewPackfile();
        var graphData = NativeAppend.Object(image, "hkbBehaviorGraphData");
        var field = HavokClasses.Shipped.Field("hkbBehaviorGraphData", "variableBounds")!;
        var data = image.Section("__data__")!;
        int header = graphData.Offset + field.Offset;
        BitConverter.GetBytes(0x40000000u).CopyTo(data.Data, header + 12);
        FixupOrder.Reorder(image);
        byte[] source = image.Rebuild();

        var plan = new NativeSave.Plan(
            new List<NativeSave.Change>
            {
                new("hkbBehaviorGraphData", 0, "variableBounds", "1",
                    Element: 0, Grow: true, Id: graphData.Id),
            },
            null);

        byte[] rebuilt = NativeSave.Apply(source, plan);
        var rebuiltImage = PackfileImage.Read(rebuilt);
        var objects = new PackfileObjects(rebuiltImage);
        var instance = objects.Instances.Single(i => i.ClassName == "hkbBehaviorGraphData");
        int at = objects.FieldAt(instance, "variableBounds")!.Value;
        var array = objects.ArrayAt(at);
        uint capacity = BitConverter.ToUInt32(rebuiltImage.Section("__data__")!.Data, at + 12);

        Assert.NotNull(array);
        Assert.Equal(1, array!.Count);
        Assert.Equal(1u, capacity & 0x3fffffffu);
        Assert.NotEqual(0u, capacity & 0x80000000u);
        Assert.NotEqual(0u, capacity & 0x40000000u);
    }

    [Fact]
    public void VerifierAcceptsDeleteAndAddInTheSameSave()
    {
        var image = NewPackfile();
        var first = NativeAppend.Object(image, "hkbClipGenerator");
        NativeAppend.Object(image, "hkbClipGenerator");
        FixupOrder.Reorder(image);
        byte[] source = image.Rebuild();
        int addedId = NativeGraphModel.FirstId + 2;

        var plan = new NativeSave.Plan(
            new List<NativeSave.Change>
            {
                new("hkbClipGenerator", 2, "", "#" + addedId,
                    Added: true, Id: addedId),
            },
            null,
            new List<int> { first.Id });

        byte[] rebuilt = NativeSave.Apply(source, plan);
        Exception? error = Record.Exception(() => SaveVerifier.Verify(source, rebuilt, plan));

        Assert.Null(error);
        Assert.Equal(2, new PackfileObjects(PackfileImage.Read(rebuilt)).Instances.Count);
    }

    [Fact]
    public void LargeButEntirelyValidArrayIsStillReadable()
    {
        const int count = 150_000;
        var image = NewPackfile();
        var data = image.Section("__data__")!;
        data.Data = new byte[count + 32];
        data.SetLocal(0, 16);
        BitConverter.GetBytes(count).CopyTo(data.Data, 8);
        var objects = new PackfileObjects(image);

        var array = objects.ArrayAt(0, 1);
        var values = objects.ReadValueArrayAt(0, 1, (bytes, at) => bytes[at]);

        Assert.NotNull(array);
        Assert.Equal(count, array!.Count);
        Assert.NotNull(values);
        Assert.Equal(count, values!.Count);
    }

    [Fact]
    public void LargeButEntirelyValidByteArrayStillRendersAsValues()
    {
        const int count = 150_000;
        var image = NewPackfile();
        var data = image.Section("__data__")!;
        data.Data = new byte[count + 32];
        data.SetLocal(0, 16);
        BitConverter.GetBytes(count).CopyTo(data.Data, 8);
        var objects = new PackfileObjects(image);
        var member = new HavokClassTypes.Member { Name = "data", VSub = "TYPE_UINT8" };

        int rendered = NativeGraphModel.Elements(
            objects, HavokClassTypes.Shipped, 0, 0, member, (_, _) => "null").Count();

        Assert.Equal(count, rendered);
    }

    [Fact]
    public void MalformedArrayBoundsReturnNullInsteadOfThrowing()
    {
        var image = NewPackfile();
        var data = image.Section("__data__")!;
        data.Data = new byte[64];
        data.SetLocal(0, 60);
        BitConverter.GetBytes(20).CopyTo(data.Data, 8);
        var objects = new PackfileObjects(image);

        Assert.Null(objects.ArrayAt(0));
        Assert.Null(objects.ReadRefArrayAt(0));
        Assert.Null(objects.ReadStringArrayAt(0));
        Assert.Null(objects.ReadValueArrayAt(0, 1, (bytes, at) => bytes[at]));
    }

    [Fact]
    public void ArrayReaderChecksElementWidthBeforeAllocating()
    {
        var image = NewPackfile();
        var data = image.Section("__data__")!;
        data.Data = new byte[64];
        data.SetLocal(0, 40);
        BitConverter.GetBytes(4).CopyTo(data.Data, 8);
        var objects = new PackfileObjects(image);

        Assert.Null(objects.ReadRefArrayAt(0));
    }

    [Fact]
    public void FolderExtractionRejectsParentTraversalOnEveryPlatform()
    {
        using var root = new TempDirectory("bgs-ba2-parent");
        string archive = Path.Combine(root.Path, "parent.ba2");
        string output = Path.Combine(root.Path, "out");
        WriteBa2(archive, "../outside/escape.hkx", new byte[] { 1, 2, 3 });

        Assert.Throws<InvalidDataException>(() =>
            Ba2.ExtractMatching(archive, "", output, ".hkx", _ => { }, keepFolders: true));
        Assert.False(File.Exists(Path.Combine(root.Path, "outside", "escape.hkx")));
    }

    [Fact]
    public void DriveQualifiedArchiveNamesAreRejectedOnEveryPlatform()
    {
        using var root = new TempDirectory("bgs-ba2-drive");
        string archive = Path.Combine(root.Path, "drive.ba2");
        WriteBa2(archive, "C:\\escape.hkx", new byte[] { 1, 2, 3 });

        var error = Assert.Throws<InvalidDataException>(() => Ba2.Open(archive));
        Assert.Contains("rooted archive path", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FlatExtractionKeepsNestedNamesInsideTheOutputDirectory()
    {
        using var root = new TempDirectory("bgs-ba2-flat");
        string archive = Path.Combine(root.Path, "flat.ba2");
        string output = Path.Combine(root.Path, "out");
        WriteBa2(archive, "folder/sub/file.hkx", new byte[] { 1, 2, 3 });

        int written = Ba2.ExtractMatching(
            archive, "", output, ".hkx", _ => { }, keepFolders: false);

        Assert.Equal(1, written);
        Assert.True(File.Exists(Path.Combine(output, "folder_sub_file.hkx")));
        Assert.Equal("a.hkx_stream", Ba2.FlatFileName("a.hkx:stream"));
    }

    [Fact]
    public void CheckedReplacementKeepsThePreviousVersionAsBackup()
    {
        using var root = new TempDirectory("bgs-checked-replace");
        string path = Path.Combine(root.Path, "graph.hkx");
        byte[] original = { 1, 2, 3, 4 };
        byte[] replacement = { 5, 6, 7, 8 };
        File.WriteAllBytes(path, original);

        FileSafety.ReplaceChecked(path, replacement, DocumentSourceStamp.Capture(path));

        Assert.Equal(replacement, File.ReadAllBytes(path));
        Assert.Equal(original, File.ReadAllBytes(path + ".bak"));
        Assert.Empty(Directory.EnumerateFiles(root.Path, "*.previous"));
    }

    [Fact]
    public void CheckedReplacementRestoresAnInterveningExternalEdit()
    {
        using var root = new TempDirectory("bgs-checked-race");
        string path = Path.Combine(root.Path, "graph.hkx");
        byte[] original = { 1, 2, 3, 4 };
        byte[] replacement = { 5, 6, 7, 8 };
        byte[] external = { 9, 10, 11, 12 };
        File.WriteAllBytes(path, original);
        var stamp = DocumentSourceStamp.Capture(path);

        var error = Assert.Throws<IOException>(() =>
            FileSafety.ReplaceChecked(
                path,
                replacement,
                stamp,
                beforeReplace: () => File.WriteAllBytes(path, external)));

        Assert.Contains("changed on disk", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(external, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
        Assert.Empty(Directory.EnumerateFiles(root.Path, "*.previous"));
    }

    [Fact]
    public void ReadHkxDoesNotTouchSaveSidecars()
    {
        using var root = new TempDirectory("bgs-read-sidecars");
        string path = Path.Combine(root.Path, "graph.hkx");
        byte[] current = { 5, 6, 7, 8 };
        File.WriteAllBytes(path, current);
        string token = DocumentSourceStamp.Capture(current).Token;
        string previous = path + "." + token + "." + Id("read") + ".previous";
        string writing = path + "." + Id("read") + ".writing";
        File.WriteAllBytes(previous, new byte[] { 1, 2, 3, 4 });
        File.WriteAllBytes(writing, new byte[] { 9, 9, 9, 9 });
        MakeStale(previous);
        MakeStale(writing);

        byte[] read = InputFilePolicy.ReadHkx(path);

        Assert.Equal(current, read);
        Assert.True(File.Exists(previous));
        Assert.True(File.Exists(writing));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void RecoveryRotatesOnceAndPromotesOnlyTheNewestMatchingStaleSidecar()
    {
        using var root = new TempDirectory("bgs-recovery-chain");
        string path = Path.Combine(root.Path, "graph.hkx");
        byte[] current = { 5, 6, 7, 8 };
        File.WriteAllBytes(path, current);
        File.WriteAllBytes(path + ".bak", new byte[] { 1 });
        File.WriteAllBytes(path + ".bak.1", new byte[] { 2 });
        File.WriteAllBytes(path + ".bak.2", new byte[] { 3 });

        string token = DocumentSourceStamp.Capture(current).Token;
        string newest = path + "." + token + "." + Id("new") + ".previous";
        string older = path + "." + token + "." + Id("old") + ".previous";
        string unrelated = path + "." + new string('0', 64) + "." + Id("other") + ".previous";
        File.WriteAllBytes(newest, new byte[] { 11 });
        File.WriteAllBytes(older, new byte[] { 12 });
        File.WriteAllBytes(unrelated, new byte[] { 13 });
        MakeStale(newest, minutes: 20);
        MakeStale(older, minutes: 30);
        MakeStale(unrelated, minutes: 40);

        FileSafety.RecoverInterrupted(path);

        Assert.Equal(new byte[] { 11 }, File.ReadAllBytes(path + ".bak"));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(path + ".bak.1"));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(path + ".bak.2"));
        Assert.Empty(Directory.EnumerateFiles(root.Path, "*.previous"));
    }

    [Fact]
    public void RecoverySweepsOnlyStaleWritingArtifacts()
    {
        using var root = new TempDirectory("bgs-recovery-writing");
        string path = Path.Combine(root.Path, "graph.hkx");
        File.WriteAllBytes(path, new byte[] { 5, 6, 7, 8 });
        string stale = path + "." + Id("stale") + ".writing";
        string fresh = path + "." + Id("fresh") + ".writing";
        File.WriteAllBytes(stale, new byte[] { 1 });
        File.WriteAllBytes(fresh, new byte[] { 2 });
        MakeStale(stale);

        FileSafety.RecoverInterrupted(path);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void RecoveryLeavesFreshPreviousArtifactsAlone()
    {
        using var root = new TempDirectory("bgs-recovery-fresh");
        string path = Path.Combine(root.Path, "graph.hkx");
        byte[] current = { 5, 6, 7, 8 };
        File.WriteAllBytes(path, current);
        string token = DocumentSourceStamp.Capture(current).Token;
        string previous = path + "." + token + "." + Id("fresh") + ".previous";
        File.WriteAllBytes(previous, new byte[] { 1, 2, 3, 4 });

        FileSafety.RecoverInterrupted(path);

        Assert.True(File.Exists(previous));
        Assert.False(File.Exists(path + ".bak"));
    }

    private static string Id(string seed) => seed switch
    {
        "read" => new string('1', 32),
        "new" => new string('2', 32),
        "old" => new string('3', 32),
        "other" => new string('4', 32),
        "stale" => new string('5', 32),
        _ => new string('6', 32),
    };

    private static PackfileImage NewPackfile()
    {
        var image = new PackfileImage { Predicates = new byte[16] };
        image.Sections.Add(new PackfileSection { TagBytes = Tag("__classnames__") });
        image.Sections.Add(new PackfileSection { TagBytes = Tag("__types__") });
        image.Sections.Add(new PackfileSection { TagBytes = Tag("__data__") });
        return image;
    }

    private static byte[] Tag(string name)
    {
        var bytes = new byte[20];
        Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        return bytes;
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

    private static void MakeStale(string path, int minutes = 20) =>
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromMinutes(minutes));

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
