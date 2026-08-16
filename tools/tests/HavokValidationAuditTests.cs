using System;
using System.Numerics;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class HavokValidationAuditTests
{
    [Fact]
    public void PhysicsValidatorRejectsNegativeIdsAndNonFiniteData()
    {
        var model = new HavokRagdollModel();
        model.Shapes.Add(new HavokPhysicsShape
        {
            Id = -1,
            Kind = HavokPhysicsShapeKind.Box,
            Radius = float.NaN,
            HalfExtents = new Vector3(1, float.PositiveInfinity, 1),
        });
        model.Bodies.Add(new HavokRigidBody
        {
            Id = -2,
            ShapeId = -1,
            BoneIndex = -1,
            Mass = 1,
            CenterOfMass = new Vector3(float.NaN, 0, 0),
            Rotation = new Quaternion(0, 0, 0, float.PositiveInfinity),
        });
        model.Constraints.Add(new HavokConstraint
        {
            Id = -3,
            BodyA = -2,
            BodyB = -2,
            MinAngle = float.NaN,
            MaxAngle = 1,
        });

        var findings = HavokPhysicsValidator.Check(model);

        Assert.Contains(findings, finding => finding.Message.Contains("shape ids must be non-negative"));
        Assert.Contains(findings, finding => finding.Message.Contains("body ids must be non-negative"));
        Assert.Contains(findings, finding => finding.Message.Contains("constraint ids must be non-negative"));
        Assert.Contains(findings, finding => finding.Message.Contains("radius must be a finite non-negative"));
        Assert.Contains(findings, finding => finding.Message.Contains("half extent Y"));
        Assert.Contains(findings, finding => finding.Message.Contains("center of mass"));
        Assert.Contains(findings, finding => finding.Message.Contains("rotation must contain only finite"));
        Assert.Contains(findings, finding => finding.Message.Contains("angle limits must be finite"));
    }

    [Fact]
    public void PhysicsValidatorDetectsIndirectCompoundShapeCycles()
    {
        var model = new HavokRagdollModel();
        // A -> B -> A is a legal-looking structure that a direct self-check misses but
        // that would make any recursive compound traversal loop forever.
        var a = new HavokPhysicsShape { Id = 1, Kind = HavokPhysicsShapeKind.Compound };
        a.Children.Add(2);
        var b = new HavokPhysicsShape { Id = 2, Kind = HavokPhysicsShapeKind.Compound };
        b.Children.Add(1);
        model.Shapes.Add(a);
        model.Shapes.Add(b);
        // A longer cycle 3 -> 4 -> 5 -> 3.
        var c = new HavokPhysicsShape { Id = 3, Kind = HavokPhysicsShapeKind.Compound };
        c.Children.Add(4);
        var d = new HavokPhysicsShape { Id = 4, Kind = HavokPhysicsShapeKind.Compound };
        d.Children.Add(5);
        var e = new HavokPhysicsShape { Id = 5, Kind = HavokPhysicsShapeKind.Compound };
        e.Children.Add(3);
        model.Shapes.Add(c);
        model.Shapes.Add(d);
        model.Shapes.Add(e);

        var findings = HavokPhysicsValidator.Check(model);

        Assert.Contains(findings, finding => finding.Message.Contains("containment cycle"));
        Assert.True(findings.Count(finding => finding.Message.Contains("containment cycle")) >= 2);
    }

    [Fact]
    public void PhysicsValidatorAcceptsAcyclicCompoundShapes()
    {
        var model = new HavokRagdollModel();
        // A diamond (1 -> 2, 1 -> 3, 2 -> 4, 3 -> 4) shares a leaf but has no cycle.
        var root = new HavokPhysicsShape { Id = 1, Kind = HavokPhysicsShapeKind.Compound };
        root.Children.Add(2);
        root.Children.Add(3);
        var left = new HavokPhysicsShape { Id = 2, Kind = HavokPhysicsShapeKind.Compound };
        left.Children.Add(4);
        var right = new HavokPhysicsShape { Id = 3, Kind = HavokPhysicsShapeKind.Compound };
        right.Children.Add(4);
        model.Shapes.Add(root);
        model.Shapes.Add(left);
        model.Shapes.Add(right);
        model.Shapes.Add(new HavokPhysicsShape { Id = 4, Kind = HavokPhysicsShapeKind.Box });

        var findings = HavokPhysicsValidator.Check(model);

        Assert.DoesNotContain(findings, finding => finding.Message.Contains("containment cycle"));
    }

    [Fact]
    public void PhysicsValidatorRejectsDegenerateAndNonUnitRotations()
    {
        var model = new HavokRagdollModel();
        model.Shapes.Add(new HavokPhysicsShape { Id = 1, Kind = HavokPhysicsShapeKind.Box, Radius = 1 });
        model.Bodies.Add(new HavokRigidBody
        {
            Id = 10, ShapeId = 1, Mass = 1, Rotation = new Quaternion(0, 0, 0, 0),
        });
        model.Bodies.Add(new HavokRigidBody
        {
            Id = 11, ShapeId = 1, Mass = 1, Rotation = new Quaternion(0, 0, 0, 2),
        });
        model.Bodies.Add(new HavokRigidBody
        {
            Id = 12, ShapeId = 1, Mass = 1, Rotation = Quaternion.Identity,
        });

        var findings = HavokPhysicsValidator.Check(model);

        Assert.Contains(findings, finding =>
            finding.Where == "body 10" && finding.Message.Contains("degenerate"));
        Assert.Contains(findings, finding =>
            finding.Where == "body 11" && finding.Message.Contains("unit quaternion"));
        // The identity rotation on body 12 is a valid unit quaternion and must stay silent.
        Assert.DoesNotContain(findings, finding =>
            finding.Where == "body 12" && finding.Message.Contains("rotation"));
    }

    [Fact]
    public void ClothValidatorRejectsNegativeIdsWrongConstraintKindAndNonFiniteData()
    {
        var model = new HavokClothModel();
        model.Shapes.Add(new HavokClothShape
        {
            Id = 1,
            Kind = HavokClothShapeKind.Sphere,
            Radius = 1,
            Start = new Vector3(float.NaN, 0, 0),
        });
        model.Collidables.Add(new HavokClothCollidable
        {
            Id = -4,
            ShapeId = 1,
            Transform = new Matrix4x4(
                float.PositiveInfinity, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1),
        });
        model.Buffers.Add(new HavokClothBuffer { Id = -1, VertexCount = 1 });

        var set = new HavokClothConstraintSet
        {
            Id = -2,
            Kind = HavokClothConstraintKind.Volume,
            ReferenceBufferIndex = 0,
        };
        set.Constraints.Add(new HavokClothLocalRangeConstraint(
            0, 0, float.NaN, float.PositiveInfinity, 0));
        model.ConstraintSets.Add(set);

        var sim = new HavokSimCloth { Id = -3, TotalMass = 1, MaxParticleRadius = 1 };
        sim.Particles.Add(new HavokClothParticle(0, 1, 1, 1, 0));
        sim.ConstraintSetIds.Add(-2);
        model.SimCloths.Add(sim);

        var findings = HavokClothValidator.Check(model);

        Assert.Contains(findings, finding => finding.Message.Contains("cloth collidable ids must be non-negative"));
        Assert.Contains(findings, finding => finding.Message.Contains("cloth buffer ids must be non-negative"));
        Assert.Contains(findings, finding => finding.Message.Contains("cloth constraint set ids must be non-negative"));
        Assert.Contains(findings, finding => finding.Message.Contains("sim cloth ids must be non-negative"));
        Assert.Contains(findings, finding => finding.Message.Contains("shape endpoints must contain only finite"));
        Assert.Contains(findings, finding => finding.Message.Contains("transform must contain only finite"));
        Assert.Contains(findings, finding => finding.Message.Contains("declares Volume"));
        Assert.Contains(findings, finding => finding.Message.Contains("maximum distance must be a finite non-negative"));
        Assert.Contains(findings, finding => finding.Message.Contains("normal distance limits must be finite"));
    }
}