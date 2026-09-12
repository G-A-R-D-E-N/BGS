using System;
using System.Collections.Generic;
using System.IO;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class RoundTripReportTests
{
    private static string Fixture(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures/vanilla", relative.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void CleanVanillaBehaviourHasNoRoundTripLosses()
    {
        var report = RoundTripReport.ForFile(Fixture("Meshes/Actors/Character/Behaviors/SingleAnimFurniture.hkx"));

        Assert.False(report.HasLosses);
        Assert.Equal("round-trip safe", report.ToString());
    }

    [Fact]
    public void RefusedVanillaClothFixtureNamesUnsupportedClasses()
    {
        var report = RoundTripReport.ForFile(
            Fixture("Meshes/Actors/Character/CharacterAssets/Hair/Female/FemaleHair04.hkx"));

        Assert.True(report.HasLosses);
        Assert.True(report.UnsupportedClasses > 0);
        Assert.Contains(report.Losses, loss =>
            loss.Kind == RoundTripLossKind.UnsupportedClass &&
            loss.Message.Contains("class this build has no definition for", StringComparison.Ordinal));
    }

    [Fact]
    public void ConversionLossesShareOneTypedReport()
    {
        var source = new HavokIntermediateDocument { RootId = 1 };
        var root = source.Add(1, "Supported");
        root.Members["legacy"] = new HavokIntermediateValue.IntegerValue(5);
        root.Members["mode"] = new HavokIntermediateValue.IntegerValue(99);
        root.Members["target"] = new HavokIntermediateValue.ReferenceValue(2);
        source.Add(2, "Unsupported");

        var map = new HavokConversionMap();
        map.Map("Supported", "Supported")
           .Drop("legacy")
           .MapEnum("mode", (2L, 7L));

        var conversion = HavokSemanticConverter.Convert(source, map);
        var report = RoundTripReport.FromConversion(conversion);

        Assert.Equal(4, report.Count);
        Assert.Equal(1, report.UnsupportedClasses);
        Assert.Equal(1, report.UnsupportedEnumValues);
        Assert.Equal(1, report.DroppedFields);
        Assert.Equal(1, report.DroppedReferences);
        Assert.Equal(0, report.ConversionErrors);
        Assert.Contains(report.Losses, loss => loss.ObjectId == 1 && loss.Member == "mode");
        Assert.Contains(report.Losses, loss => loss.ObjectId == 1 && loss.Member == "target");
        Assert.Contains(report.Losses, loss => loss.ObjectId == 2 && loss.Kind == RoundTripLossKind.UnsupportedClass);
    }

    [Fact]
    public void UnsupportedRootIsNotDoubleCountedByItsFollowUpDiagnostic()
    {
        var source = new HavokIntermediateDocument { RootId = 1 };
        source.Add(1, "Unsupported");

        var conversion = HavokSemanticConverter.Convert(source, new HavokConversionMap());
        var report = RoundTripReport.FromConversion(conversion);

        Assert.Single(report.Losses);
        Assert.Equal(RoundTripLossKind.UnsupportedClass, report.Losses[0].Kind);
    }

    [Fact]
    public void OtherConversionErrorsRemainVisible()
    {
        var source = new HavokIntermediateDocument { RootId = 1 };
        source.Add(1, "Src").Members["old"] = new HavokIntermediateValue.IntegerValue(5);

        var map = new HavokConversionMap();
        map.Map("Src", "Dst").Rename("old", "missing");

        var registry = new HavokTypeRegistry();
        registry.Register(new HavokTypeDefinition("Dst", 8, Array.Empty<HavokMemberDefinition>()));

        var conversion = HavokSemanticConverter.Convert(source, map, registry);
        var report = RoundTripReport.FromConversion(conversion);

        Assert.Equal(1, report.ConversionErrors);
        Assert.Contains(report.Losses, loss =>
            loss.Kind == RoundTripLossKind.ConversionError &&
            loss.Message.Contains("does not declare member missing", StringComparison.Ordinal));
    }
}
