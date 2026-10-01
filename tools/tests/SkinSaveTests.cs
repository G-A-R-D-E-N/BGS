using System;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Nif;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class SkinSaveTests
{
    private static byte[] Source() => File.ReadAllBytes(System.IO.Path.GetFullPath(
        System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "symrm", "samples",
            "Meshes", "Actors", "Turret", "CharacterAssets", "TurretMounted.nif")));

    [Fact]
    public void WeightEditPreservesEveryByteOutsideTheOwnedSkinSlots()
    {
        byte[] source = Source();
        var nif = NifFile.Parse(source, "fixture");
        var shapes = NifGeometry.Shapes(nif);
        int index = shapes.FindIndex(SkinEdit.Editable);
        Assert.True(index >= 0);
        NifShape shape = shapes[index];
        var layout = shape.SkinSource!.Value;
        shape.BoneWeights[0] = 0.75f;

        byte[] result = SkinSave.Apply(source, index, shape);

        Assert.Equal(source.Length, result.Length);
        Assert.False(source.SequenceEqual(result));
        for (int at = 0; at < source.Length; at++)
        {
            int relative = at - layout.Start;
            int vertex = relative / layout.Stride;
            int slot = relative % layout.Stride;
            bool owned = relative >= 0 && vertex < shape.Vertices.Count &&
                (slot >= layout.Weights && slot < layout.Weights + 8 ||
                 slot >= layout.Indices && slot < layout.Indices + 4);
            if (!owned) Assert.Equal(source[at], result[at]);
        }
        Assert.Equal(0.75f, NifGeometry.Shapes(NifFile.Parse(result, "reopened"))[index].BoneWeights[0]);
    }

    [Fact]
    public void SaveSupportsUndoBackupsAndRejectsExternalChanges()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bgs-skin-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = System.IO.Path.Combine(directory, "mesh.nif");
        byte[] source = Source();
        File.WriteAllBytes(path, source);
        try
        {
            var document = new SkinDocument(path);
            int index = document.Shapes.ToList().FindIndex(SkinEdit.Editable);
            document.Edit(index, shape => shape.BoneWeights[0] = 0.75f);
            Assert.True(document.Dirty);
            Assert.Equal(source, File.ReadAllBytes(path));
            document.Undo();
            Assert.False(document.Dirty);
            document.Redo();
            document.Save();
            Assert.Equal(source, File.ReadAllBytes(path + ".bak"));
            Assert.False(document.Dirty);
            Assert.Equal(0.75f, new SkinDocument(path).Shapes[index].BoneWeights[0]);
            document.Edit(index, shape => shape.BoneWeights[0] = 0.5f);
            File.WriteAllBytes(path, source);
            Assert.Throws<IOException>(document.Save);
            Assert.Equal(source, File.ReadAllBytes(path));
        }
        finally
        {
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
