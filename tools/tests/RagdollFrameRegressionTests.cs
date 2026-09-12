using System;
using System.IO;
using System.Linq;
using System.Numerics;
using BehaviourStudio.App;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class RagdollFrameRegressionTests
{
    private const string Skeleton =
        "Meshes/Actors/Character/CharacterAssets/skeleton.hkx";

    private static string Fixture(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures/vanilla",
                     relative.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void StaticDropStartsFromTheMeasuredBodyFramesAlreadyOnScreen()
    {
        string path = Fixture(Skeleton);
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(path));
        Assert.NotNull(model);

        var view = new SkeletonView();
        view.SetBodies(model);
        view.StartDrop();

        Assert.True(view.IsDropped);
        Assert.NotNull(view.DroppedPositions);
        Assert.NotNull(view.DroppedRotations);

        foreach (var binding in model!.BoneBindings)
        {
            var body = model.Bodies.FirstOrDefault(candidate => candidate.Id == binding.BodyId);
            if (body == null || binding.BoneIndex < 0 || binding.BoneIndex >= view.DroppedPositions!.Count)
                continue;

            Assert.True(Vector3.Distance(body.Position, view.DroppedPositions[binding.BoneIndex]) <= 5e-3f,
                $"body {body.Id} changed position when the static drop was captured");
            Assert.True(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(body.Rotation),
                                                 Quaternion.Normalize(view.DroppedRotations![binding.BoneIndex]))) >= 0.999f,
                $"body {body.Id} changed rotation when the static drop was captured");
        }
    }

    [Fact]
    public void EngineeringFitUsesDisplayedMappedBodyFrames()
    {
        string path = Fixture(Skeleton);
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(path));
        Assert.NotNull(model);
        var skeleton = new HkxBinaryReader().ReadSkeleton(path);
        Assert.NotNull(skeleton);
        var reference = AnimationPose.ReferencePose(skeleton!);

        var view = new SkeletonView { EngineeringFrames = true };
        view.SetBodies(model);
        view.Show(reference);
        view.Frame();
        Vector3 baseline = view.ViewCentre;

        view.Show(Shift(reference, 50f));
        view.Frame();

        Vector3 moved = view.ViewCentre - baseline;
        Assert.True(Vector3.Distance(new Vector3(0, 0, 50), moved) <= 0.02f,
            $"engineering Fit stayed on reference bounds instead of the displayed mapped pose: delta {moved}");
    }

    private static AnimationPose.Pose Shift(AnimationPose.Pose source, float dz)
    {
        var shifted = new AnimationPose.Pose();
        foreach (var bone in source.Bones)
            shifted.Bones.Add(new AnimationPose.Bone(bone.Index, bone.Name, bone.Parent,
                bone.Position + new Vector3(0, 0, dz), bone.Rotation));
        return shifted;
    }
}
