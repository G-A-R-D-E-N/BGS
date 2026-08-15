using System;
using System.IO;
using System.Text;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

[Collection(TemplateStoreCollection.Name)]
public sealed class TemplateSnapshotTests
{
    [Fact]
    public void LiftPublishesTheSameSnapshotItAnalyzed()
    {
        string previous = TemplateStore.Folder;
        string root = Directory.CreateTempSubdirectory("bgs-template-snapshot").FullName;
        try
        {
            TemplateStore.Folder = Path.Combine(root, "templates");
            string sourcePath = Path.Combine(root, "source.hkx");
            byte[] expected = Source();
            byte[] external = expected.ToArray();
            external[^1] ^= 0x01;
            File.WriteAllBytes(sourcePath, expected);

            TemplateStore.BeforeSourceStageForTest = () =>
                File.WriteAllBytes(sourcePath, external);

            var template = TemplateStore.Lift(
                sourcePath, NativeGraphModel.FirstId, "Snapshot Template");

            Assert.Equal(external, File.ReadAllBytes(sourcePath));
            Assert.Equal(expected, File.ReadAllBytes(
                Path.Combine(TemplateStore.Folder, template.Slug + ".hkx")));
            Assert.NotNull(TemplateStore.Get(template.Slug));
        }
        finally
        {
            TemplateStore.BeforeSourceStageForTest = null;
            TemplateStore.BeforeDescriptionPublishForTest = null;
            TemplateStore.Folder = previous;
            try { Directory.Delete(root, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static byte[] Source()
    {
        var image = new PackfileImage();
        image.Sections.Add(new PackfileSection { TagBytes = Tag("__classnames__") });
        image.Sections.Add(new PackfileSection { TagBytes = Tag("__data__") });
        NativeAppend.Object(image, "hkbClipGenerator");
        FixupOrder.Reorder(image);
        return image.Rebuild();
    }

    private static byte[] Tag(string name)
    {
        var bytes = new byte[20];
        Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        return bytes;
    }
}
