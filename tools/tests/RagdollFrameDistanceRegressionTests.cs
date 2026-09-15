using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using BehaviourStudio.App;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class RagdollFrameDistanceRegressionTests
{
    private const string Skeleton =
        "Meshes/Actors/Character/CharacterAssets/skeleton.hkx";

    private static string Fixture(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures/vanilla",
                     relative.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void BodyFrameDistanceTextUsesInvariantNumericFormatting()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        var view = new SkeletonView();
        view.SetBodies(model);
        view.ToggleBodyPinForTest(0);
        view.ToggleBodyPinForTest(1);
        float distance = Vector3.Distance(model!.Bodies[0].Position, model.Bodies[1].Position);
        string expected = $"distance {model.Bodies[0].Name} to {model.Bodies[1].Name}  " +
                          $"{distance.ToString("0.00", CultureInfo.InvariantCulture)} file units";

        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(expected, view.FrameDistanceText);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void BodyFrameDistanceFollowsMappedPlaybackPose()
    {
        string path = Fixture(Skeleton);
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(path));
        Assert.NotNull(model);
        var skeleton = new HkxBinaryReader().ReadSkeleton(path);
        Assert.NotNull(skeleton);

        var initial = AnimationPose.ReferencePose(skeleton!);
        var moved = new AnimationPose.Pose();
        foreach (var bone in initial.Bones)
            moved.Bones.Add(new AnimationPose.Bone(bone.Index, bone.Name, bone.Parent,
                bone.Position, bone.Rotation));

        var firstBinding = model!.BoneBindings.First();
        var firstMapping = model.Mappings.First(mapping => mapping.BoneB == firstBinding.BoneIndex);
        int secondBody = model.Bodies.First(body => body.Id != firstBinding.BodyId).Id;
        moved.Bones[firstMapping.BoneA] = moved.Bones[firstMapping.BoneA] with
        {
            Position = moved.Bones[firstMapping.BoneA].Position + new Vector3(10, 0, 0)
        };

        var view = new SkeletonView();
        view.SetBodies(model);
        view.ToggleBodyPinForTest(firstBinding.BodyId);
        view.ToggleBodyPinForTest(secondBody);
        view.Show(initial);
        string before = view.FrameDistanceText;
        view.Show(moved);

        Assert.NotEqual(before, view.FrameDistanceText);
    }

    [Fact]
    public void BodyFrameDistanceFollowsDropPose()
    {
        string path = Fixture(Skeleton);
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(path));
        Assert.NotNull(model);
        var skeleton = new HkxBinaryReader().ReadSkeleton(path);
        Assert.NotNull(skeleton);

        var view = new SkeletonView();
        view.SetBodies(model);
        view.Show(AnimationPose.ReferencePose(skeleton!));
        view.ToggleBodyPinForTest(model!.Bodies[0].Id);
        view.ToggleBodyPinForTest(model.Bodies[1].Id);
        string before = view.FrameDistanceText;

        view.StartDrop();
        view.AdvanceDrop(1f);

        Assert.NotEqual(before, view.FrameDistanceText);
    }

    [Fact]
    public void BodyFrameDistanceUsesTwoPickedFramesAndCanBeReplacedOrClearedUnderFrenchCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
            Assert.NotNull(model);

            var view = new SkeletonView { EngineeringFrames = true };
            view.SetBodies(model);
            view.ToggleBodyPinForTest(0);
            Assert.Equal("", view.FrameDistanceText);
            view.ToggleBodyPinForTest(1);
            var first = model!.Bodies.Single(body => body.Id == 0);
            var second = model.Bodies.Single(body => body.Id == 1);
            float distance = Vector3.Distance(first.Position, second.Position);
            Assert.Equal($"distance {first.Name} to {second.Name}  " +
                         $"{distance.ToString("0.00", CultureInfo.InvariantCulture)} file units",
                         view.FrameDistanceText);

            view.ToggleBodyPinForTest(2);
            Assert.Equal((1, 2), view.FrameDistanceBodies);
            view.ClearFrameDistance();
            Assert.Null(view.FrameDistanceBodies);
            Assert.Equal("", view.FrameDistanceText);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
