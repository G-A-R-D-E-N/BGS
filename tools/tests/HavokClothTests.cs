using System.Linq;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class HavokClothTests
{
    [Fact]
    public void SchemaCatalogContainsObservedClothAndPhysicsClasses()
    {
        Assert.True(HavokPhysicsSchemaCatalog.Matches("hclClothContainer", 0x3512912b));
        Assert.True(HavokPhysicsSchemaCatalog.Matches("hclClothData", 0xf943cea2));
        Assert.True(HavokPhysicsSchemaCatalog.Matches("hclSimClothData", 0xe6105187));
        Assert.True(HavokPhysicsSchemaCatalog.Matches("hknpRagdollData", 0xdc8f20ab));
        Assert.True(HavokPhysicsSchemaCatalog.Matches("hkpRagdollConstraintData", 0xb77d2036));
        Assert.Equal(HavokPhysicsClassFamily.Cloth, HavokPhysicsSchemaCatalog.FamilyOf("hclClothState"));
        Assert.Equal(HavokPhysicsClassFamily.NewPhysics, HavokPhysicsSchemaCatalog.FamilyOf("hknpPhysicsSystemData"));
        Assert.Equal(HavokPhysicsClassFamily.LegacyPhysics, HavokPhysicsSchemaCatalog.FamilyOf("hkpLimitedHingeConstraintData"));
    }

    [Fact]
    public void ClothValidatorAcceptsAConsistentSimulation()
    {
        var model = new HavokClothModel { Name = "Body", TargetPlatform = "HCL_PLATFORM_X64" };
        model.Shapes.Add(new HavokClothShape { Id = 1, Kind = HavokClothShapeKind.Sphere, Radius = 0.2f });
        model.Collidables.Add(new HavokClothCollidable { Id = 2, ShapeId = 1, PinchDetectionRadius = 0.01f });
        model.Buffers.Add(new HavokClothBuffer { Id = 3, Name = "Sim", VertexCount = 3, TriangleCount = 1 });
        model.TransformSets.Add(new HavokClothTransformSet { Id = 4, Name = "Master", TransformCount = 1 });

        var set = new HavokClothConstraintSet { Id = 5, Name = "Standard Links", Kind = HavokClothConstraintKind.StandardLink };
        set.Constraints.Add(new HavokClothLinkConstraint(0, 1, 1.0f, 0.5f));
        model.ConstraintSets.Add(set);

        var sim = new HavokSimCloth { Id = 6, Name = "Sim", TotalMass = 2.0f, MaxParticleRadius = 0.01f, DoNormals = true };
        sim.Particles.Add(new HavokClothParticle(0, 1.0f, 1.0f, 0.01f, 0.5f));
        sim.Particles.Add(new HavokClothParticle(1, 1.0f, 1.0f, 0.01f, 0.5f));
        sim.Particles.Add(new HavokClothParticle(2, 0.0f, 0.0f, 0.01f, 0.5f));
        sim.FixedParticles.Add(2);
        sim.TriangleIndices.AddRange(new[] { 0, 1, 2 });
        sim.ConstraintSetIds.Add(5);
        model.SimCloths.Add(sim);

        var op = new HavokClothOperator { Id = 7, TypeName = "hclSimulateOperator", Name = "Simulate", SimClothIndex = 0 };
        op.InputBufferIndices.Add(0);
        op.OutputBufferIndices.Add(0);
        model.Operators.Add(op);

        var state = new HavokClothState { Id = 8, Name = "#01#Default" };
        state.OperatorIndices.Add(0);
        state.UsedBufferIndices.Add(0);
        state.UsedTransformSetIndices.Add(0);
        state.UsedSimClothIndices.Add(0);
        model.States.Add(state);

        var findings = HavokClothValidator.Check(model);
        Assert.DoesNotContain(findings, finding => finding.Level == HavokPhysicsValidationLevel.Error);
    }

    [Fact]
    public void ClothValidatorRejectsBrokenParticleBufferAndCollidableReferences()
    {
        var model = new HavokClothModel();
        model.Collidables.Add(new HavokClothCollidable { Id = 2, ShapeId = 99, PinchDetectionRadius = -1.0f });
        model.Buffers.Add(new HavokClothBuffer { Id = 3, VertexCount = 1, TriangleCount = 1 });

        var set = new HavokClothConstraintSet { Id = 5, Kind = HavokClothConstraintKind.LocalRange, ReferenceBufferIndex = 0 };
        set.Constraints.Add(new HavokClothLocalRangeConstraint(4, 7, 1.0f, 2.0f, -1.0f));
        model.ConstraintSets.Add(set);

        var sim = new HavokSimCloth { Id = 6, TotalMass = 1.0f, MaxParticleRadius = 0.01f };
        sim.Particles.Add(new HavokClothParticle(0, 1.0f, 1.0f, 0.01f, 0.5f));
        sim.FixedParticles.Add(4);
        sim.TriangleIndices.AddRange(new[] { 0, 4, 0 });
        sim.ConstraintSetIds.Add(5);
        model.SimCloths.Add(sim);

        var op = new HavokClothOperator { Id = 7, SimClothIndex = 3 };
        op.InputBufferIndices.Add(4);
        model.Operators.Add(op);

        var state = new HavokClothState { Id = 8 };
        state.OperatorIndices.Add(2);
        model.States.Add(state);

        var findings = HavokClothValidator.Check(model);

        Assert.Contains(findings, finding => finding.Message.Contains("missing shape 99"));
        Assert.Contains(findings, finding => finding.Message.Contains("fixed particle 4"));
        Assert.Contains(findings, finding => finding.Message.Contains("missing particle 4"));
        Assert.Contains(findings, finding => finding.Message.Contains("reference vertex 7"));
        Assert.Contains(findings, finding => finding.Message.Contains("input buffer index 4"));
        Assert.Contains(findings, finding => finding.Message.Contains("operator index 2"));
        Assert.True(findings.Count(finding => finding.Level == HavokPhysicsValidationLevel.Error) >= 6);
    }
}
