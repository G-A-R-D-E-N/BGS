using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class HavokConversionDanglingReferenceTests
{
    [Fact]
    public void SpecialConverterDanglingReferenceIsRejectedByTargetSchema()
    {
        var source = new HavokIntermediateDocument { RootId = 1 };
        source.Add(1, "SourceOwner");

        var map = new HavokConversionMap();
        map.Map("SourceOwner", "TargetOwner")
            .ConvertWith((_, _, target) =>
                target.Members["link"] = new HavokIntermediateValue.ReferenceValue(99));

        var targetTypes = new HavokTypeRegistry();
        targetTypes.Register(new HavokTypeDefinition("TargetOwner", 8, new[]
        {
            new HavokMemberDefinition("link", "TYPE_POINTER", "TargetChild"),
        }));
        targetTypes.Register(new HavokTypeDefinition(
            "TargetChild", 8, System.Array.Empty<HavokMemberDefinition>()));

        var result = HavokSemanticConverter.Convert(source, map, targetTypes);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Level == HavokConversionDiagnosticLevel.Error &&
            diagnostic.ObjectId == 1 &&
            diagnostic.Member == "link" &&
            diagnostic.Message.Contains("99") &&
            diagnostic.Message.Contains("does not exist"));
    }

    [Fact]
    public void SpecialConverterDanglingReferenceInsideArrayIsRejectedByTargetSchema()
    {
        var source = new HavokIntermediateDocument { RootId = 1 };
        source.Add(1, "SourceOwner");

        var map = new HavokConversionMap();
        map.Map("SourceOwner", "TargetOwner")
            .ConvertWith((_, _, target) =>
                target.Members["links"] = new HavokIntermediateValue.ArrayValue(new[]
                {
                    new HavokIntermediateValue.ReferenceValue(99),
                }));

        var targetTypes = new HavokTypeRegistry();
        targetTypes.Register(new HavokTypeDefinition("TargetOwner", 8, new[]
        {
            new HavokMemberDefinition("links", "TYPE_ARRAY", "TargetChild"),
        }));
        targetTypes.Register(new HavokTypeDefinition(
            "TargetChild", 8, System.Array.Empty<HavokMemberDefinition>()));

        var result = HavokSemanticConverter.Convert(source, map, targetTypes);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Level == HavokConversionDiagnosticLevel.Error &&
            diagnostic.ObjectId == 1 &&
            diagnostic.Member == "links[0]" &&
            diagnostic.Message.Contains("99") &&
            diagnostic.Message.Contains("does not exist"));
    }
}
