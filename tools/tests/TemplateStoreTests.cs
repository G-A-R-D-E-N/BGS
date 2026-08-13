using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

[CollectionDefinition("TemplateStore serial", DisableParallelization = true)]
public sealed class TemplateStoreCollection
{
    public const string Name = "TemplateStore serial";
}

[Collection(TemplateStoreCollection.Name)]
public class TemplateStoreTests
{
    [Fact]
    public void Lift_PublishesCompleteTemplateWithDescriptorLast()
    {
        using var scope = new TemplateScope();
        string source = scope.CreateSource();

        var template = TemplateStore.Lift(
            source, NativeGraphModel.FirstId, "Atomic Template", "kept note");

        Assert.Equal("atomic-template", template.Slug);
        Assert.Equal("hkbClipGenerator", template.RootClass);
        string storedSource = Path.Combine(scope.TemplateFolder, "atomic-template.hkx");
        Assert.True(File.Exists(storedSource));
        Assert.True(File.Exists(Path.Combine(scope.TemplateFolder, "atomic-template.template")));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(storedSource));
        Assert.Equal("kept note", TemplateStore.Get("atomic-template")?.Note);
        Assert.Empty(Directory.EnumerateFiles(scope.TemplateFolder, "*.tmp"));
    }

    [Fact]
    public void Lift_FailureBeforeDescriptorPublishRollsBackAndDoesNotBlockRetry()
    {
        using var scope = new TemplateScope();
        string source = scope.CreateSource();
        TemplateStore.BeforeDescriptionPublishForTest = () =>
            throw new IOException("injected descriptor publish failure");

        var failure = Assert.Throws<IOException>(() =>
            TemplateStore.Lift(source, NativeGraphModel.FirstId, "Retryable Template"));

        Assert.Contains("injected", failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(scope.TemplateFolder, "retryable-template.hkx")));
        Assert.False(File.Exists(Path.Combine(scope.TemplateFolder, "retryable-template.template")));
        Assert.Empty(TemplateStore.All());
        Assert.Empty(Directory.EnumerateFiles(scope.TemplateFolder, "*.tmp"));

        TemplateStore.BeforeDescriptionPublishForTest = null;
        var retried = TemplateStore.Lift(
            source, NativeGraphModel.FirstId, "Retryable Template");

        Assert.Equal("retryable-template", retried.Slug);
        Assert.NotNull(TemplateStore.Get(retried.Slug));
    }

    [Fact]
    public void Lift_ConcurrentSameSlugPublishesExactlyOneCompleteTemplate()
    {
        using var scope = new TemplateScope();
        string source = scope.CreateSource();
        var successes = new ConcurrentBag<TemplateStore.Template>();
        var failures = new ConcurrentBag<Exception>();

        Parallel.For(0, 2, _ =>
        {
            try
            {
                successes.Add(TemplateStore.Lift(
                    source, NativeGraphModel.FirstId, "One Winner"));
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        });

        Assert.Single(successes);
        Assert.Single(failures);
        Assert.IsType<InvalidOperationException>(failures.Single());
        Assert.Single(TemplateStore.All());
        Assert.True(File.Exists(Path.Combine(scope.TemplateFolder, "one-winner.hkx")));
        Assert.True(File.Exists(Path.Combine(scope.TemplateFolder, "one-winner.template")));
        Assert.Empty(Directory.EnumerateFiles(scope.TemplateFolder, "*.tmp"));
    }

    private sealed class TemplateScope : IDisposable
    {
        private readonly string _previousFolder = TemplateStore.Folder;
        private readonly string _root = Directory.CreateTempSubdirectory("bgs-template-test").FullName;

        public TemplateScope()
        {
            TemplateFolder = Path.Combine(_root, "templates");
            TemplateStore.Folder = TemplateFolder;
            TemplateStore.BeforeDescriptionPublishForTest = null;
        }

        public string TemplateFolder { get; }

        public string CreateSource()
        {
            var image = new PackfileImage();
            image.Sections.Add(new PackfileSection { TagBytes = Tag("__classnames__") });
            image.Sections.Add(new PackfileSection { TagBytes = Tag("__data__") });

            var added = NativeAppend.Object(image, "hkbClipGenerator");
            Assert.Equal(NativeGraphModel.FirstId, added.Id);

            string path = Path.Combine(_root, "source.hkx");
            File.WriteAllBytes(path, image.Rebuild());
            return path;
        }

        public void Dispose()
        {
            TemplateStore.BeforeDescriptionPublishForTest = null;
            TemplateStore.Folder = _previousFolder;
            try { Directory.Delete(_root, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        private static byte[] Tag(string name)
        {
            var bytes = new byte[20];
            Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
            return bytes;
        }
    }
}
