using System;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class AssistantInspectionTests
{
    [Fact]
    public void InspectBehaviorReturnsBoundedTypedSummary()
    {
        var result = new AssistantInspection().InspectBehavior(
            Fixture("Meshes/Actors/Character/Behaviors/SingleAnimFurniture.hkx"), 1);

        Assert.Equal("disk", result.Source);
        Assert.Equal("ok", result.Status);
        Assert.Equal("ok", result.Code);
        Assert.True(result.Readable);
        Assert.NotEmpty(result.ClassCounts);
        Assert.True(result.Findings.Count <= 1);
        Assert.Empty(result.RoundTrip);
    }

    [Fact]
    public void InspectAnimationReturnsSummaryWithoutFrames()
    {
        var result = new AssistantInspection().InspectAnimation(
            Fixture("Meshes/Actors/RadRoach/Animations/TurnLeft90.hkx"));

        Assert.Equal("ok", result.Status);
        Assert.True(result.Supported);
        Assert.NotEmpty(result.AnimationClass);
        Assert.NotEmpty(result.Bones);
        Assert.All(result.Annotations, annotation => Assert.True(annotation.Text.Length <= AssistantInspectionLimits.MaxDisplayString));
    }

    [Fact]
    public void InspectObjectReturnsReferencesAndBoundedFields()
    {
        string path = Fixture("Meshes/Actors/Character/Behaviors/SingleAnimFurniture.hkx");
        string xml = HkxTextEdit.TextOf(path);
        string id = BehaviourGraphModel.Parse(xml).Objects.First().Id;

        var result = new AssistantInspection().InspectObject(path, id, 1, 1);

        Assert.Equal("ok", result.Status);
        Assert.Equal(id, result.ObjectId);
        Assert.NotEmpty(result.ClassName);
        Assert.True(result.Scalars.Count + result.Lists.Count + result.Structs.Count + result.StructLists.Count <= 4);
    }

    [Fact]
    public void SearchProjectFindsCommittedFixture()
    {
        string root = Directory.CreateTempSubdirectory("bgs-assistant-search").FullName;
        try
        {
            string behaviors = Path.Combine(root, "Behaviors");
            Directory.CreateDirectory(behaviors);
            string target = Path.Combine(behaviors, "SingleAnimFurniture.hkx");
            File.Copy(Fixture("Meshes/Actors/Character/Behaviors/SingleAnimFurniture.hkx"), target);

            var result = new AssistantInspection().SearchProject(target, "hkbClipGenerator");

            Assert.Equal("ok", result.Status);
            Assert.Contains(result.Hits, hit => hit.Kind == "class" && hit.ClassName == "hkbClipGenerator");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void InvalidAndMissingInputsAreStructured()
    {
        var inspection = new AssistantInspection();

        Assert.Equal("invalid_argument", inspection.SearchProject("x", " ").Code);
        Assert.Equal("path_not_found", inspection.InspectBehavior(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).Code);
        Assert.Equal("object_not_found", inspection.InspectObject(
            Fixture("Meshes/Actors/Character/Behaviors/SingleAnimFurniture.hkx"), "999999").Code);
    }

    private static string Fixture(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures/vanilla", relative.Replace('/', Path.DirectorySeparatorChar));
}
