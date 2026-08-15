using System;
using System.IO;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class GraphMutationTransactionTests
{
    [Fact]
    public void CommitPublishesVerifiedBytesAndKeepsAnExactBackup()
    {
        using var scope = new MutationScope();
        byte[] source = File.ReadAllBytes(scope.Path);

        var result = GraphMutationTransaction.Commit(
            scope.Path,
            DocumentSourceStamp.Capture(scope.Path),
            AddClip);

        Assert.True(result.Committed);
        Assert.NotNull(result.Change);
        Assert.Equal(source, File.ReadAllBytes(scope.Path + ".bak"));
        Assert.False(source.SequenceEqual(File.ReadAllBytes(scope.Path)));
        Assert.Equal(2, new PackfileObjects(PackfileImage.Read(scope.Path)).Instances.Count);
        Assert.Contains("Added clip", result.Message, StringComparison.Ordinal);
        Assert.Contains("source.hkx.bak", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CommitRejectsAFileChangedAfterItWasOpened()
    {
        using var scope = new MutationScope();
        var opened = DocumentSourceStamp.Capture(scope.Path);
        byte[] external = File.ReadAllBytes(scope.Path);
        external[^1] ^= 0x01;
        File.WriteAllBytes(scope.Path, external);
        bool built = false;

        var result = GraphMutationTransaction.Commit(
            scope.Path,
            opened,
            source =>
            {
                built = true;
                return AddClip(source);
            });

        Assert.False(result.Committed);
        Assert.False(built);
        Assert.Equal(external, File.ReadAllBytes(scope.Path));
        Assert.False(File.Exists(scope.Path + ".bak"));
        Assert.Contains("changed on disk", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommitRejectsAChangeInjectedBeforePublication()
    {
        using var scope = new MutationScope();
        byte[] external = File.ReadAllBytes(scope.Path);
        external[^1] ^= 0x01;

        var result = GraphMutationTransaction.Commit(
            scope.Path,
            DocumentSourceStamp.Capture(scope.Path),
            AddClip,
            beforeSourceRecheck: () => File.WriteAllBytes(scope.Path, external));

        Assert.False(result.Committed);
        Assert.Equal(external, File.ReadAllBytes(scope.Path));
        Assert.False(File.Exists(scope.Path + ".bak"));
        Assert.Contains("changed on disk", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommitNeverPublishesBytesThatFailVerification()
    {
        using var scope = new MutationScope();
        byte[] source = File.ReadAllBytes(scope.Path);

        var result = GraphMutationTransaction.Commit(
            scope.Path,
            DocumentSourceStamp.Capture(scope.Path),
            AddClip,
            verificationFault: () => new InvalidDataException("injected verification fault"));

        Assert.False(result.Committed);
        Assert.Equal(source, File.ReadAllBytes(scope.Path));
        Assert.False(File.Exists(scope.Path + ".bak"));
        Assert.Contains("failed verification", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("injected verification fault", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CommitRejectsMalformedOutput()
    {
        using var scope = new MutationScope();
        byte[] source = File.ReadAllBytes(scope.Path);

        var result = GraphMutationTransaction.Commit(
            scope.Path,
            DocumentSourceStamp.Capture(scope.Path),
            _ => new GraphMutationTransaction.Mutation(
                new byte[] { 1, 2, 3 }, NativeGraphModel.FirstId + 1, 1,
                "hkbClipGenerator", "bad"));

        Assert.False(result.Committed);
        Assert.Equal(source, File.ReadAllBytes(scope.Path));
        Assert.False(File.Exists(scope.Path + ".bak"));
        Assert.Contains("failed verification", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommitRejectsAnIncorrectObjectCountOrRootClass()
    {
        using var scope = new MutationScope();
        byte[] source = File.ReadAllBytes(scope.Path);
        var valid = AddClip(source);

        var count = GraphMutationTransaction.Commit(
            scope.Path,
            DocumentSourceStamp.Capture(scope.Path),
            _ => valid with { AddedObjects = 2 });
        Assert.False(count.Committed);
        Assert.Contains("expected 3 objects", count.Message, StringComparison.Ordinal);

        var rootClass = GraphMutationTransaction.Commit(
            scope.Path,
            DocumentSourceStamp.Capture(scope.Path),
            _ => valid with { RootClass = "hkbStateMachine" });
        Assert.False(rootClass.Committed);
        Assert.Contains("not hkbStateMachine", rootClass.Message, StringComparison.Ordinal);
    }

    private static GraphMutationTransaction.Mutation AddClip(byte[] source)
    {
        var image = PackfileImage.Read(source);
        var added = NativeAppend.Object(image, "hkbClipGenerator");
        FixupOrder.Reorder(image);
        return new GraphMutationTransaction.Mutation(
            image.Rebuild(), added.Id, 1, "hkbClipGenerator", "Added clip.");
    }

    private sealed class MutationScope : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("bgs-graph-mutation").FullName;

        public MutationScope()
        {
            Path = System.IO.Path.Combine(_root, "source.hkx");
            var image = new PackfileImage();
            image.Sections.Add(new PackfileSection { TagBytes = Tag("__classnames__") });
            image.Sections.Add(new PackfileSection { TagBytes = Tag("__data__") });
            NativeAppend.Object(image, "hkbClipGenerator");
            FixupOrder.Reorder(image);
            File.WriteAllBytes(Path, image.Rebuild());
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }

        private static byte[] Tag(string name)
        {
            var bytes = new byte[20];
            Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
            return bytes;
        }
    }
}
