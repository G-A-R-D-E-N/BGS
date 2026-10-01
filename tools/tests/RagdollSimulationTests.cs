using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class RagdollSimulationTests
{
    [Fact]
    public void OffCenterRotatedHullRetainsBodyFrameAndCollidesWithGroundAndOtherBodies()
    {
        var model = Model();
        var masses = new Dictionary<int, float> { [0] = 1, [1] = 1 };
        for (int cycle = 0; cycle < 3; cycle++)
        {
            using var simulation = new RagdollSimulation(model, masses, 10);
            Assert.True(Vector3.Distance(simulation.Frames[0].Position, model.Bodies[0].Position) < 1e-4f);
            Assert.True(MathF.Abs(Quaternion.Dot(simulation.Frames[0].Rotation, model.Bodies[0].Rotation)) > 0.99999f);
            Assert.Contains(simulation.HullEdges, edge => Vector3.Distance(edge.A,
                model.Bodies[0].Position + Vector3.Transform(model.Shapes[0].Vertices[0], model.Bodies[0].Rotation)) < 1e-4f);
            for (int i = 0; i < 600; i++) simulation.Advance(1f / 120);
            var frames = simulation.Frames;
            Vector3 centerA = frames[0].Position + Vector3.Transform(new Vector3(3, 0, 0), frames[0].Rotation);
            Vector3 centerB = frames[1].Position + Vector3.Transform(new Vector3(3, 0, 0), frames[1].Rotation);
            Assert.InRange(centerA.Z, 0.85f, 5f);
            Assert.InRange(centerB.Z, 0.85f, 5f);
            Assert.True(Vector3.Distance(centerA, centerB) > 1.8f, "Unconnected bodies must collide rather than pass through each other.");
        }
    }

    [Fact]
    public void LimitedHingeKeepsItsAsymmetricAuthoredRangeForEitherBodyOrder()
    {
        foreach (bool reverse in new[] { false, true })
        {
            var model = Model();
            model.Constraints.Add(new HavokConstraint { Id = 0, Kind = HavokConstraintKind.LimitedHinge,
                BodyA = reverse ? 1 : 0, BodyB = reverse ? 0 : 1,
                FrameA = Frame(Vector3.Zero), FrameB = Frame(Vector3.Zero), LimitAxis = 2,
                MinAngle = -0.1f, MaxAngle = 0.4f, HasPreviewLimits = true });
            var poses = new Dictionary<int, HavokBoneFrame> {
                [0] = new(Vector3.Zero, Quaternion.Identity),
                [1] = new(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, reverse ? -0.9f : 0.9f))
            };
            using var simulation = new RagdollSimulation(model, new Dictionary<int, float> { [0] = 1, [1] = 1 }, 0, -100, poses);
            for (int i = 0; i < 240; i++) simulation.Advance(1f / 120);
            var frames = simulation.Frames;
            Quaternion relative = Quaternion.Inverse(frames[reverse ? 1 : 0].Rotation) * frames[reverse ? 0 : 1].Rotation;
            Vector3 direction = Vector3.Transform(Vector3.UnitX, relative);
            float angle = MathF.Atan2(direction.Y, direction.X);
            Assert.InRange(angle, -0.12f, 0.42f);
            Assert.True(Vector3.Distance(frames[0].Position, frames[1].Position) < 0.02f);
        }
    }

    [Fact]
    public void RotatedFixedFramesAndRagdollConeTwistLimitsConstrainTheRelativePose()
    {
        foreach (bool fixedJoint in new[] { true, false })
        {
            var model = Model();
            float[] frameA = Frame(Vector3.Zero), frameB = Frame(Vector3.Zero);
            if (fixedJoint)
            {
                var rotation = Matrix4x4.CreateRotationZ(0.4f);
                frameB = new[] { rotation.M11, rotation.M12, rotation.M13, 0, rotation.M21, rotation.M22, rotation.M23, 0,
                    rotation.M31, rotation.M32, rotation.M33, 0, 0, 0, 0, 1 };
            }
            model.Constraints.Add(new HavokConstraint { Id = 0, Kind = fixedJoint ? HavokConstraintKind.Fixed : HavokConstraintKind.Ragdoll,
                BodyA = 0, BodyB = 1, FrameA = frameA, FrameB = frameB, HasPreviewLimits = true,
                TwistAxis = 2, TwistRefAxis = 0, ConeTwistAxis = 2, ConeRefAxis = 2,
                TwistMinAngle = -0.1f, TwistMaxAngle = 0.3f, ConeMaxAngle = 0.2f });
            var poses = new Dictionary<int, HavokBoneFrame> {
                [0] = new(Vector3.Zero, Quaternion.Identity),
                [1] = new(Vector3.Zero, Quaternion.CreateFromYawPitchRoll(0.6f, 0, 0.7f)) };
            using var simulation = new RagdollSimulation(model, new Dictionary<int, float> { [0] = 1, [1] = 1 }, 0, -100, poses);
            for (int i = 0; i < 300; i++) simulation.Advance(1f / 120);
            var frames = simulation.Frames;
            Quaternion relative = Quaternion.Inverse(frames[0].Rotation) * frames[1].Rotation;
            Assert.True(Vector3.Distance(frames[0].Position, frames[1].Position) < 0.02f);
            if (fixedJoint)
                Assert.True(MathF.Abs(Quaternion.Dot(relative, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.4f))) > 0.999f);
            else
            {
                float swing = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.UnitZ, Vector3.Transform(Vector3.UnitZ, relative)), -1, 1));
                float twist = 2 * MathF.Atan2(relative.Z, relative.W);
                Assert.InRange(swing, 0, 0.22f);
                Assert.InRange(twist, -0.12f, 0.32f);
            }
        }
    }

    [Fact]
    public void MissingMassInvalidFramesAndUnsupportedGeometryRefuseBeforeSimulation()
    {
        var model = Model();
        var masses = new Dictionary<int, float> { [0] = 1, [1] = 1 };
        Assert.Throws<InvalidDataException>(() => new RagdollSimulation(model, new Dictionary<int, float>(), 10));
        model.Constraints.Add(new HavokConstraint { Id = 0, Kind = HavokConstraintKind.BallAndSocket, BodyA = 0, BodyB = 1 });
        Assert.Throws<InvalidDataException>(() => new RagdollSimulation(model, masses, 10));
        model.Constraints.Clear();
        model.Shapes[0].Vertices.Clear();
        Assert.Throws<NotSupportedException>(() => new RagdollSimulation(model, masses, 10));
    }

    [Fact]
    public void VanillaMeasuredHullsCanRunWithExplicitPreviewSettingsWithoutMutatingSource()
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "vanilla", "Meshes", "Actors", "Character", "CharacterAssets", "skeleton.hkx");
        byte[] source = File.ReadAllBytes(fixture);
        var extracted = HavokPhysicsExtractor.TryExtract(source)!;
        var model = new HavokRagdollModel();
        model.Shapes.AddRange(extracted.Shapes); model.Bodies.AddRange(extracted.Bodies);
        foreach (var joint in extracted.Constraints)
            model.Constraints.Add(new HavokConstraint { Id = joint.Id, Kind = joint.Kind, BodyA = joint.BodyA, BodyB = joint.BodyB,
                FrameA = joint.FrameA, FrameB = joint.FrameB, LimitAxis = 0, TwistAxis = 0, TwistRefAxis = 1,
                ConeTwistAxis = 0, ConeRefAxis = 0, MinAngle = -0.5f, MaxAngle = 0.5f,
                TwistMinAngle = -0.5f, TwistMaxAngle = 0.5f, ConeMaxAngle = 0.8f, HasPreviewLimits = true });
        using var simulation = new RagdollSimulation(model, model.Bodies.ToDictionary(body => body.Id, _ => 5f), 600);
        for (int i = 0; i < 240; i++) simulation.Advance(1f / 120);
        Assert.Equal(model.Bodies.Count, simulation.Frames.Count);
        Assert.All(simulation.Frames.Values, frame => Assert.True(float.IsFinite(frame.Position.LengthSquared())));
        Assert.Equal(source, File.ReadAllBytes(fixture));
        simulation.Dispose();
        Assert.Throws<ObjectDisposedException>(() => simulation.Advance(1f / 120));
    }

    private static HavokRagdollModel Model()
    {
        var shape = new HavokPhysicsShape { Id = 0, Kind = HavokPhysicsShapeKind.ConvexHull };
        foreach (float x in new[] { 2f, 4f })
            foreach (float y in new[] { -1f, 1f })
                foreach (float z in new[] { -1f, 1f }) shape.Vertices.Add(new Vector3(x, y, z));
        var model = new HavokRagdollModel();
        model.Shapes.Add(shape);
        model.Bodies.Add(new HavokRigidBody { Id = 0, ShapeId = 0, Position = new Vector3(0, 0, 8), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2) });
        model.Bodies.Add(new HavokRigidBody { Id = 1, ShapeId = 0, Position = new Vector3(-3, 3, 12) });
        return model;
    }

    private static float[] Frame(Vector3 position) => new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, position.X, position.Y, position.Z, 1 };
}
