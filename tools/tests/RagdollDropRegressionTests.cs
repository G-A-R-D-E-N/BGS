using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using BehaviourStudio.App;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class RagdollDropRegressionTests
{
    private const string Skeleton =
        "Meshes/Actors/Character/CharacterAssets/skeleton.hkx";

    private static string Fixture(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures/vanilla",
                     relative.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void GroundedMappedPoseStillReleasesAndSettles()
    {
        var (model, pose) = LoadReference();
        var mapped = Map(model, pose);
        float lowest = LowestVertex(model, mapped);
        var grounded = Shift(pose, -lowest);

        var view = new SkeletonView();
        view.SetBodies(model);
        view.Show(grounded);
        view.StartDrop();

        Assert.True(view.IsDropped);
        Assert.False(view.DropResting);
        var start = view.DroppedPositions!.ToArray();

        int steps = 0;
        while (!view.DropResting && steps < 600)
        {
            view.AdvanceDrop(1f / 60f);
            steps++;
        }

        Assert.True(view.DropResting, "a grounded release still reaches a settled state");
        Assert.True(steps > 1, "ground contact must not short-circuit the release");
        Assert.Contains(Enumerable.Range(0, start.Length), i =>
            Vector3.Distance(start[i], view.DroppedPositions![i]) > 0.1f);
    }

    [Fact]
    public void EqualElapsedTimeIsIndependentOfCallerChunking()
    {
        var (model, pose) = LoadReference();
        var lifted = Shift(pose, 20f);

        var coarse = NewDrop(model, lifted);
        var split = NewDrop(model, lifted);

        for (int i = 0; i < 20; i++)
        {
            coarse.AdvanceDrop(1f / 30f);
            split.AdvanceDrop(1f / 60f);
            split.AdvanceDrop(1f / 60f);
        }

        Assert.True(coarse.DroppedPositions!.SequenceEqual(split.DroppedPositions!),
            "position arrays changed with caller timer chunking");
        Assert.True(coarse.DroppedRotations!.SequenceEqual(split.DroppedRotations!),
            "rotation arrays changed with caller timer chunking");
    }

    [Fact]
    public void SettledPivotsPreserveCapturedOffsetVector()
    {
        var (model, pose) = LoadReference();
        var lifted = Shift(pose, 20f);
        var captured = Map(model, lifted);
        var view = NewDrop(model, lifted);

        int steps = 0;
        while (!view.DropResting && steps < 600)
        {
            view.AdvanceDrop(1f / 60f);
            steps++;
        }
        Assert.True(view.DropResting);

        var settled = view.DroppedPositions!;
        var rotations = view.DroppedRotations!;
        float worst = 0;

        foreach (var constraint in model.Constraints.Where(c => c.FrameA != null && c.FrameB != null))
        {
            int ba = BoundBone(model, constraint.BodyA);
            int bb = BoundBone(model, constraint.BodyB);
            if (ba < 0 || bb < 0) continue;

            Vector3 localA = Pivot(constraint.FrameA!);
            Vector3 localB = Pivot(constraint.FrameB!);
            Vector3 capturedOffset =
                captured[bb].Position + Vector3.Transform(localB, captured[bb].Rotation) -
                (captured[ba].Position + Vector3.Transform(localA, captured[ba].Rotation));
            Vector3 settledOffset =
                settled[bb] + Vector3.Transform(localB, rotations[bb]) -
                (settled[ba] + Vector3.Transform(localA, rotations[ba]));

            worst = Math.Max(worst, Vector3.Distance(capturedOffset, settledOffset));
        }

        Assert.True(worst < 1.5f,
            $"a pivot offset vector drifted by {worst:F3} file units");
    }

    [Fact]
    public void UnboundSkeletonFramesDoNotParticipateInGravityOrRestDetection()
    {
        var model = new HavokRagdollModel();
        var shape = new HavokPhysicsShape { Id = 10 };
        shape.Vertices.Add(Vector3.Zero);
        model.Shapes.Add(shape);
        model.Bodies.Add(new HavokRigidBody { Id = 1, ShapeId = 10, Mass = 1f });

        var bodyIndex = new Dictionary<int, int> { [1] = 0 };
        var bodyBones = new[] { 0 };
        var depth = new[] { 0 };
        var positions = new[] { new Vector3(0, 0, 10), new Vector3(3, 4, 123) };
        var rotations = new[] { Quaternion.Identity, Quaternion.Identity };
        var velocities = new[] { Vector3.Zero, new Vector3(7, 8, 9) };
        var pivots = Array.Empty<Vector3>();

        Vector3 unboundPosition = positions[1];
        Vector3 unboundVelocity = velocities[1];
        SkeletonView.StepConstrainedRelease(model, bodyIndex, bodyBones, depth,
            positions, rotations, velocities, pivots, 1f / 120f, 1);

        Assert.Equal(unboundPosition, positions[1]);
        Assert.Equal(unboundVelocity, velocities[1]);
    }

    [Fact]
    public void UnmappedPhysicsBoneKeepsItsCompleteReferenceFrame()
    {
        var model = new HavokRagdollModel { Skeleton = new HavokRagdollSkeleton() };
        model.Skeleton.BoneNames.AddRange(new[] { "root", "helper" });
        model.Skeleton.ParentIndices.AddRange(new[] { -1, 0 });
        model.Skeleton.ReferencePose.Add(new HkxBonePose(
            new Vector3(1, 2, 3),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.35f),
            Vector3.One));
        model.Skeleton.ReferencePose.Add(new HkxBonePose(
            new Vector3(0, 4, 0),
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.6f),
            Vector3.One));
        model.Mappings.Add(new HavokRagdollMapping(0, 0, Vector3.Zero, Quaternion.Identity));

        var animation = new[]
        {
            new HavokBoneFrame(new Vector3(20, 30, 40),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.8f))
        };
        var mapped = model.MapPose(animation);
        Assert.NotNull(mapped);

        var reference = model.Skeleton.WorldReferenceFrames();
        AssertNear(reference[1].Position, mapped![1].Position, 1e-5f);
        AssertRotationNear(reference[1].Rotation, mapped[1].Rotation, 1e-5f);
    }

    [Fact]
    public void WorldSpaceComIsReprojectedThroughTheReferenceBodyFrame()
    {
        var referenceRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        var referencePosition = new Vector3(10, 0, 0);
        var localCom = new Vector3(2, 0, 0);
        var body = new HavokRigidBody
        {
            Position = referencePosition,
            Rotation = referenceRotation,
            CenterOfMass = referencePosition + Vector3.Transform(localCom, referenceRotation),
        };

        var posedPosition = new Vector3(3, 4, 5);
        var posedRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI);
        var expected = posedPosition + Vector3.Transform(localCom, posedRotation);

        AssertNear(expected, body.PosedCenterOfMass(posedPosition, posedRotation), 1e-5f);
        AssertNear(body.CenterOfMass, body.PosedCenterOfMass(body.Position, body.Rotation), 1e-5f);
    }

    [Fact]
    public void HidingConstraintsClearsHoverAndPinImmediately()
    {
        var view = new SkeletonView { ShowConstraints = true };
        view.HoverConstraintForTest(4);
        view.PinConstraintForTest(7);
        Assert.Equal(4, view.HoveredConstraintId);
        Assert.Equal(7, view.PinnedConstraintId);

        view.ShowConstraints = false;

        Assert.Equal(-1, view.HoveredConstraintId);
        Assert.Equal(-1, view.PinnedConstraintId);
    }

    [Fact]
    public void ReleaseSourceIsCapturedByValue()
    {
        var (model, pose) = LoadReference();
        var view = new SkeletonView();
        view.SetBodies(model);
        view.Show(pose);
        view.StartDrop();

        var capturedPositions = view.DroppedPositions!.ToArray();
        var capturedRotations = view.DroppedRotations!.ToArray();

        var moved = Shift(pose, 50f);
        view.Update(moved);

        Assert.True(capturedPositions.SequenceEqual(view.DroppedPositions!),
            "animation updates mutated the captured release positions");
        Assert.True(capturedRotations.SequenceEqual(view.DroppedRotations!),
            "animation updates mutated the captured release rotations");
    }

    private static SkeletonView NewDrop(HavokRagdollModel model, AnimationPose.Pose pose)
    {
        var view = new SkeletonView();
        view.SetBodies(model);
        view.Show(pose);
        view.StartDrop();
        Assert.True(view.IsDropped);
        return view;
    }

    private static (HavokRagdollModel Model, AnimationPose.Pose Pose) LoadReference()
    {
        string path = Fixture(Skeleton);
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(path));
        Assert.NotNull(model);
        var skeleton = new HkxBinaryReader().ReadSkeleton(path);
        Assert.NotNull(skeleton);
        return (model!, AnimationPose.ReferencePose(skeleton!));
    }

    private static AnimationPose.Pose Shift(AnimationPose.Pose source, float dz)
    {
        var shifted = new AnimationPose.Pose();
        foreach (var bone in source.Bones)
            shifted.Bones.Add(new AnimationPose.Bone(bone.Index, bone.Name, bone.Parent,
                bone.Position + new Vector3(0, 0, dz), bone.Rotation));
        return shifted;
    }

    private static HavokBoneFrame[] Map(HavokRagdollModel model, AnimationPose.Pose pose)
    {
        var mapped = model.MapPose(pose.Bones
            .Select(b => new HavokBoneFrame(b.Position, b.Rotation)).ToArray());
        Assert.NotNull(mapped);
        return mapped!;
    }

    private static float LowestVertex(HavokRagdollModel model, IReadOnlyList<HavokBoneFrame> frames)
    {
        float lowest = float.MaxValue;
        foreach (var body in model.Bodies)
        {
            int bone = BoundBone(model, body.Id);
            if (bone < 0 || bone >= frames.Count) continue;
            var shape = model.Shapes.FirstOrDefault(s => s.Id == body.ShapeId);
            if (shape == null) continue;
            foreach (var vertex in shape.Vertices)
                lowest = Math.Min(lowest,
                    (frames[bone].Position + Vector3.Transform(vertex, frames[bone].Rotation)).Z);
        }
        Assert.NotEqual(float.MaxValue, lowest);
        return lowest;
    }

    private static int BoundBone(HavokRagdollModel model, int bodyId) =>
        model.BoneBindings.FirstOrDefault(binding => binding.BodyId == bodyId)?.BoneIndex ?? -1;

    private static Vector3 Pivot(float[] frame) => new(frame[12], frame[13], frame[14]);

    private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance)
    {
        Assert.True(Vector3.Distance(expected, actual) <= tolerance,
            $"expected {expected}, measured {actual}");
    }

    private static void AssertRotationNear(Quaternion expected, Quaternion actual, float tolerance)
    {
        float dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(expected), Quaternion.Normalize(actual)));
        Assert.True(dot >= 1f - tolerance, $"expected {expected}, measured {actual}, dot={dot}");
    }
}
