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