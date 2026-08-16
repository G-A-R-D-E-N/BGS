using System.Linq;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class HavokConversionTests
{
    [Fact]
    public void ConversionPreservesSharedReferencesAndReportsLosses()
    {
        var source = new HavokIntermediateDocument { RootId = 1 };
        var root = source.Add(1, "NewGenerator");
        var child = source.Add(2, "SharedChild");
        root.Members["childA"] = new HavokIntermediateValue.ReferenceValue(2);
        root.Members["childB"] = new HavokIntermediateValue.ReferenceValue(2);
        root.Members["newOnly"] = new HavokIntermediateValue.IntegerValue(99);
        child.Members["name"] = new HavokIntermediateValue.StringValue("shared");

        var map = new HavokConversionMap();
        map.Map("NewGenerator", "OldGenerator")
            .Rename("childA", "primaryChild")
            .Drop("newOnly")
            .Default("enabled", new HavokIntermediateValue.BoolValue(true));
        map.AllowIdentity("SharedChild");

        var result = HavokSemanticConverter.Convert(source, map);
        var converted = result.Document.Get(1)!;

        Assert.Equal("OldGenerator", converted.TypeName);
        Assert.Equal(new HavokIntermediateValue.ReferenceValue(2), converted.Members["primaryChild"]);
        Assert.Equal(new HavokIntermediateValue.ReferenceValue(2), converted.Members["childB"]);
        Assert.Equal(new HavokIntermediateValue.BoolValue(true), converted.Members["enabled"]);
        Assert.Equal(2, result.Report.ConvertedObjects);
        Assert.Equal(1, result.Report.PatchedObjects);
        Assert.Equal(1, result.Report.ExactObjects);
        Assert.Equal(1, result.Report.DefaultedFields);
        Assert.Equal(1, result.Report.DroppedFields);
        Assert.Equal(0, result.Report.DroppedReferences);
        Assert.Equal(0, result.Report.EnumMappedFields);
        Assert.Equal(0, result.Report.UnsupportedEnumValues);
        Assert.Equal(0, result.Report.UnsupportedObjects);
    }

    [Fact]
    public void DeclaredButUnusedConversionOperationsDoNotCountAsPatched()
    {
        var source = new HavokIntermediateDocument();
        var value = source.Add(1, "StableType");
        value.Members["enabled"] = new HavokIntermediateValue.BoolValue(true);
        value.Members["name"] = new HavokIntermediateValue.StringValue("same");

        var map = new HavokConversionMap();
        map.Map("StableType", "StableType")
            .Rename("missingRename", "otherName")
            .Drop("missingDrop")
            .Default("enabled", new HavokIntermediateValue.BoolValue(false))
            .MapEnum("missingEnum", (1L, 2L))
            .ConvertWith((_, _, _) => { });

        var result = HavokSemanticConverter.Convert(source, map);

        Assert.Equal(1, result.Report.ConvertedObjects);
        Assert.Equal(0, result.Report.PatchedObjects);
        Assert.Equal(1, result.Report.ExactObjects);
        Assert.Equal(0, result.Report.DefaultedFields);
        Assert.Equal(0, result.Report.DroppedFields);
        Assert.Equal(0, result.Report.DroppedReferences);
        Assert.Equal(0, result.Report.EnumMappedFields);
        Assert.Equal(0, result.Report.UnsupportedEnumValues);
    }

    [Fact]
    public void SpecialConverterTypeChangeCountsAsPatched()
    {
        var source = new HavokIntermediateDocument();
        source.Add(1, "StableType");

        var map = new HavokConversionMap();
        map.Map("StableType", "StableType")
            .ConvertWith((_, _, target) => target.TypeName = "SpecializedType");

        var result = HavokSemanticConverter.Convert(source, map);

        Assert.Equal("SpecializedType", result.Document.Get(1)!.TypeName);
        Assert.Equal(1, result.Report.PatchedObjects);
        Assert.Equal(0, result.Report.ExactObjects);
    }

    [Fact]
    public void EnumMappingRemapsKnownValueAndMarksObjectPatched()
    {
        var source = new HavokIntermediateDocument();
        var value = source.Add(1, "NewType");
        value.Members["mode"] = new HavokIntermediateValue.IntegerValue(2);

        var map = new HavokConversionMap();
        map.Map("NewType", "NewType").MapEnum("mode", (0L, 0L), (2L, 7L));

        var result = HavokSemanticConverter.Convert(source, map);

        Assert.Equal(new HavokIntermediateValue.IntegerValue(7), result.Document.Get(1)!.Members["mode"]);
        Assert.Equal(1, result.Report.EnumMappedFields);
        Assert.Equal(0, result.Report.UnsupportedEnumValues);
        Assert.Equal(1, result.Report.PatchedObjects);
        Assert.Equal(0, result.Report.ExactObjects);
    }

    [Fact]
    public void IdentityEnumMappingKeepsObjectExact()
    {
        var source = new HavokIntermediateDocument();
        var value = source.Add(1, "StableType");
        value.Members["mode"] = new HavokIntermediateValue.IntegerValue(2);

        var map = new HavokConversionMap();
        map.Map("StableType", "StableType").MapEnum("mode", (2L, 2L));

        var result = HavokSemanticConverter.Convert(source, map);

        Assert.Equal(new HavokIntermediateValue.IntegerValue(2), result.Document.Get(1)!.Members["mode"]);
        Assert.Equal(1, result.Report.EnumMappedFields);
        Assert.Equal(0, result.Report.UnsupportedEnumValues);
        Assert.Equal(0, result.Report.PatchedObjects);
        Assert.Equal(1, result.Report.ExactObjects);
    }

    [Fact]
    public void UnmappedEnumValueIsNotSilentlyCopied()
    {
        var source = new HavokIntermediateDocument();
        var value = source.Add(1, "NewType");
        value.Members["mode"] = new HavokIntermediateValue.IntegerValue(99);

        var map = new HavokConversionMap();
        map.Map("NewType", "NewType").MapEnum("mode", (2L, 7L));

        var result = HavokSemanticConverter.Convert(source, map);

        Assert.IsType<HavokIntermediateValue.NullValue>(result.Document.Get(1)!.Members["mode"]);
        Assert.Equal(0, result.Report.EnumMappedFields);
        Assert.Equal(1, result.Report.UnsupportedEnumValues);
        Assert.Equal(1, result.Report.PatchedObjects);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Level == HavokConversionDiagnosticLevel.Error &&
            diagnostic.Member == "mode" &&
            diagnostic.Message.Contains("99"));
    }

    [Fact]
    public void ConversionValidatesAgainstTargetSchemaWhenRegistryIsSupplied()
    {
        var source = new HavokIntermediateDocument { RootId = 1 };
        var root = source.Add(1, "Src");
        root.Members["old"] = new HavokIntermediateValue.IntegerValue(5);
        source.Add(2, "Ghost").Members["x"] = new HavokIntermediateValue.IntegerValue(1);

        var map = new HavokConversionMap();
        map.Map("Src", "Dst").Rename("old", "missing");   // "missing" is not declared on Dst
        map.Map("Ghost", "Phantom");                       // "Phantom" is not in the registry at all

        var registry = new HavokTypeRegistry();
        registry.Register(new HavokTypeDefinition("Dst", 8, new[]
        {
            new HavokMemberDefinition("new", "TYPE_INT32"),
        }));

        var result = HavokSemanticConverter.Convert(source, map, registry);

        Assert.Contains(result.Diagnostics, d =>
            d.Level == HavokConversionDiagnosticLevel.Error && d.ObjectId == 1 &&
            d.Message.Contains("does not declare member missing"));
        Assert.Contains(result.Diagnostics, d =>
            d.Level == HavokConversionDiagnosticLevel.Error && d.ObjectId == 2 &&
            d.Message.Contains("target type Phantom is not declared"));
    }

    [Fact]
    public void ConversionReportsNoSchemaErrorsWhenTargetMembersAreDeclared()
    {
        var source = new HavokIntermediateDocument { RootId = 1 };
        source.Add(1, "Src").Members["old"] = new HavokIntermediateValue.IntegerValue(5);

        var map = new HavokConversionMap();
        map.Map("Src", "Dst").Rename("old", "new").Default("flag", new HavokIntermediateValue.BoolValue(true));

        var registry = new HavokTypeRegistry();
        registry.Register(new HavokTypeDefinition("Dst", 12, new[]
        {
            new HavokMemberDefinition("new", "TYPE_INT32"),
            new HavokMemberDefinition("flag", "TYPE_BOOL"),
        }));

        var result = HavokSemanticConverter.Convert(source, map, registry);

        Assert.DoesNotContain(result.Diagnostics, d => d.Level == HavokConversionDiagnosticLevel.Error);
        Assert.Equal("Dst", result.Document.Get(1)!.TypeName);
        Assert.Equal(new HavokIntermediateValue.IntegerValue(5), result.Document.Get(1)!.Members["new"]);
    }

    [Fact]
    public void UnsupportedReferenceBecomesNullAndIsReportedAsPatched()
    {
        var source = new HavokIntermediateDocument();
        var root = source.Add(1, "Supported");
        source.Add(2, "Unsupported");
        root.Members["target"] = new HavokIntermediateValue.ReferenceValue(2);

        var map = new HavokConversionMap().AllowIdentity("Supported");
        var result = HavokSemanticConverter.Convert(source, map);

        Assert.Equal(new HavokIntermediateValue.ReferenceValue(null), result.Document.Get(1)!.Members["target"]);
        Assert.Equal(1, result.Report.UnsupportedObjects);
        Assert.Equal(1, result.Report.DroppedReferences);
        Assert.Equal(1, result.Report.PatchedObjects);
        Assert.Equal(0, result.Report.ExactObjects);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Level == HavokConversionDiagnosticLevel.Error && diagnostic.ObjectId == 1);
    }

    [Fact]
    public void PhysicsValidatorRejectsBrokenRagdollMappings()
    {
        var model = new HavokRagdollModel();
        model.Shapes.Add(new HavokPhysicsShape { Id = 1, Kind = HavokPhysicsShapeKind.Capsule, Radius = 2.0f });
        model.Bodies.Add(new HavokRigidBody { Id = 10, ShapeId = 1, BoneIndex = 4, Mass = 5.0f });
        model.Constraints.Add(new HavokConstraint
        {
            Id = 20,
            Kind = HavokConstraintKind.Ragdoll,
            BodyA = 10,
            BodyB = 99,
            MinAngle = 1.0f,
            MaxAngle = -1.0f,
        });
        model.BoneBindings.Add(new HavokRagdollBoneBinding(4, 10));
        model.BoneBindings.Add(new HavokRagdollBoneBinding(4, 10));

        var findings = HavokPhysicsValidator.Check(model, skeletonBoneCount: 4);

        Assert.Contains(findings, finding => finding.Message.Contains("missing body 99"));
        Assert.Contains(findings, finding => finding.Message.Contains("minimum angle"));
        Assert.Contains(findings, finding => finding.Message.Contains("outside a 4-bone skeleton"));
        Assert.Contains(findings, finding => finding.Message.Contains("more than one rigid body"));
        Assert.True(findings.Count(finding => finding.Level == HavokPhysicsValidationLevel.Error) >= 4);
    }
}