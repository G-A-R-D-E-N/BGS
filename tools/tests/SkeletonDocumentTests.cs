using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Xml.Linq;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class SkeletonDocumentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedBoneArrayIsRefusedWithoutChangingEitherSkeletonOrTheFile(bool crossSection)
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "vanilla", "Meshes", "Actors", "Character", "CharacterAssets", "skeleton.hkx");
        var image = PackfileImage.Read(File.ReadAllBytes(fixture));
        var objects = new PackfileObjects(image);
        var skeletons = objects.Instances.Where(item => item.ClassName == "hkaSkeleton").Take(2).ToArray();
        Assert.Equal(2, skeletons.Length);
        var data = image.Section("__data__")!;
        if (crossSection)
        {
            int bones = objects.FieldAt(skeletons[0], "bones")!.Value;
            image.Section("__types__")!.SetGlobal(bones, image.Sections.IndexOf(data), objects.ArrayAt(bones)!.At);
        }
        else foreach (string field in new[] { "bones", "parentIndices", "referencePose" })
        {
            int first = objects.FieldAt(skeletons[0], field)!.Value;
            int second = objects.FieldAt(skeletons[1], field)!.Value;
            Array.Copy(data.Data, first, data.Data, second, image.Layout.PointerSize + 8);
            data.SetLocal(second, objects.ArrayAt(first)!.At);
        }
        FixupOrder.Reorder(image);
        byte[] source = image.Rebuild();
        string path = Path.Combine(Path.GetTempPath(), $"bgs-shared-bones-{Guid.NewGuid():N}.hkx");
        File.WriteAllBytes(path, source);
        try
        {
            var document = new SkeletonDocument(path);
            string otherName = document.Skeletons[1].BoneNames[0];
            Assert.Throws<InvalidDataException>(() => document.Edit(0, edit => edit.Rename(0, "Shared edit")));
            Assert.False(document.Dirty);
            Assert.False(document.CanUndo);
            Assert.Equal(otherName, document.Skeletons[1].BoneNames[0]);
            Assert.Equal(source, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public void StandaloneBoneAdditionRemovalAndReparentingSurviveSaveAndReopen()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bgs-standalone-{Guid.NewGuid():N}.hkx");
        var image = new PackfileImage { Predicates = new byte[16] };
        foreach (string name in new[] { "__classnames__", "__types__", "__data__" })
        {
            var tag = new byte[20]; Encoding.ASCII.GetBytes(name).CopyTo(tag, 0);
            image.Sections.Add(new PackfileSection { TagBytes = tag });
        }
        NativeAppend.Object(image, "hkaSkeleton");
        byte[] seed = image.Rebuild();
        string before = NativeXml.From(seed);
        var xml = XDocument.Parse(before);
        XElement skeleton = xml.Descendants("hkobject").Single(e => (string?)e.Attribute("class") == "hkaSkeleton");
        XElement Field(string name) => skeleton.Elements("hkparam").Single(e => (string?)e.Attribute("name") == name);
        Field("name").Value = "Standalone";
        Field("bones").SetAttributeValue("numelements", 1);
        Field("bones").Add(new XElement("hkobject", new XElement("hkparam", new XAttribute("name", "name"), "Root"),
            new XElement("hkparam", new XAttribute("name", "lockTranslation"), "false")));
        Field("parentIndices").SetAttributeValue("numelements", 1); Field("parentIndices").Value = "-1";
        Field("referencePose").SetAttributeValue("numelements", 1); Field("referencePose").Value = "(0 0 0 0) (0 0 0 1) (1 1 1 0)";
        var plan = NativeSave.Compare(before, xml.ToString());
        Assert.True(plan.Possible, plan.Refusal);
        File.WriteAllBytes(path, NativeSave.Apply(seed, plan));
        try
        {
            var document = new SkeletonDocument(path);
            Assert.True(document.CanChangeBoneCount);
            document.Edit(0, edit => edit.AddBone("Child", 0));
            document.Save();
            Assert.Equal(new[] { "Root", "Child" }, new SkeletonDocument(path).Skeletons[0].BoneNames);
            document.Edit(0, edit => edit.Reparent(1, -1));
            document.Edit(0, edit => edit.RemoveBone(0));
            document.Save();
            var reopened = new SkeletonDocument(path).Skeletons[0];
            Assert.Equal(new[] { "Child" }, reopened.BoneNames);
            Assert.Equal(new[] { -1 }, reopened.ParentIndices);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public void PoseAndNameEditsAreUndoableAndSaveWithoutOverwritingExternalChanges()
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "vanilla", "Meshes", "Actors", "Character", "CharacterAssets", "skeleton.hkx");
        string path = Path.Combine(Path.GetTempPath(), $"bgs-skeleton-{Guid.NewGuid():N}.hkx");
        byte[] source = File.ReadAllBytes(fixture);
        File.WriteAllBytes(path, source);
        try
        {
            var document = new SkeletonDocument(path);
            string originalName = document.Skeletons[0].BoneNames[0];
            document.Edit(0, edit => {
                edit.Name = "Edited skeleton";
                edit.Rename(0, "Edited Root");
                HkxBonePose pose = edit.PoseOf(0);
                pose.Translation += new Vector3(12.5f, 0, 0);
                edit.SetPose(0, pose);
            });
            Assert.True(document.Dirty);
            Assert.Equal(source, File.ReadAllBytes(path));
            document.Undo();
            Assert.False(document.Dirty);
            Assert.Equal(originalName, document.Skeletons[0].BoneNames[0]);
            document.Redo();
            document.Save();
            Assert.False(document.Dirty);
            Assert.Equal(source, File.ReadAllBytes(path + ".bak"));
            Assert.Equal("Edited Root", new SkeletonDocument(path).Skeletons[0].BoneNames[0]);
            Assert.Equal("Edited skeleton", new SkeletonDocument(path).Skeletons[0].Name);
            document.Edit(0, edit => edit.Rename(0, "Another Root"));
            File.WriteAllBytes(path, source);
            Assert.Throws<IOException>(document.Save);
            Assert.Equal(source, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public void DependentRigCannotChangeBoneIndicesAndInvalidEditsDoNotEnterHistory()
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "vanilla", "Meshes", "Actors", "Character", "CharacterAssets", "skeleton.hkx");
        var document = new SkeletonDocument(fixture);
        Assert.Throws<NotSupportedException>(() => document.Edit(0, edit => edit.AddBone("Extra Root")));
        Assert.Throws<InvalidDataException>(() => document.Edit(0, edit => edit.SetPose(0,
            new HkxBonePose(new Vector3(float.NaN, 0, 0), Quaternion.Identity, Vector3.One))));
        Assert.False(document.Dirty);
        Assert.False(document.CanUndo);
    }
}
