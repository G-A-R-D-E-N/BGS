using System;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class NativeVariableBuilderTests
{
    [Fact]
    public void BuildsAlignedNativeVariablesWithTypedInitialValues()
    {
        byte[] source = Source(
            "hkbBehaviorGraphStringData",
            "hkbBehaviorGraphData",
            "hkbVariableValueSet");

        var result = NativeVariableBuilder.Build(source, new[]
        {
            new NativeVariableBuilder.Entry("Counter", SymbolEditor.VariableType.Int32, "-7"),
            new NativeVariableBuilder.Entry("Speed", SymbolEditor.VariableType.Real, "1.25"),
            new NativeVariableBuilder.Entry("Enabled", SymbolEditor.VariableType.Bool, "true"),
        });

        var model = Model(result.Bytes);
        var audit = SymbolEditor.Audit(model);

        Assert.Equal(new[] { "Counter", "Speed", "Enabled" }, SymbolEditor.VariableNames(model));
        Assert.Equal(new[]
        {
            SymbolEditor.VariableType.Int32,
            SymbolEditor.VariableType.Real,
            SymbolEditor.VariableType.Bool,
        }, SymbolEditor.VariableTypes(model));

        var values = SymbolEditor.VariableValues(model);
        Assert.Equal("-7", values[0]);
        Assert.Equal("1.25", SymbolEditor.DecodeValue(SymbolEditor.VariableType.Real, values[1]));
        Assert.Equal("true", SymbolEditor.DecodeValue(SymbolEditor.VariableType.Bool, values[2]));
        Assert.True(audit.VariablesConsistent);
        Assert.True(audit.BoundsAreParallel);
        Assert.Equal(3, audit.Names);
        Assert.Equal(3, audit.Infos);
        Assert.Equal(3, audit.Values);
        Assert.Equal(3, audit.Bounds);
        Assert.Equal(new[] { 0, 1, 2 }, result.Created.Select(item => item.Index).ToArray());
        Assert.DoesNotContain(result.Findings, finding => finding.BlocksSave);
    }

    [Fact]
    public void RefusesDuplicateExistingVariableName()
    {
        byte[] source = Source(
            "hkbBehaviorGraphStringData",
            "hkbBehaviorGraphData",
            "hkbVariableValueSet");
        var first = NativeVariableBuilder.Build(source, new[]
        {
            new NativeVariableBuilder.Entry("Speed", SymbolEditor.VariableType.Real, "1"),
        });

        var error = Assert.Throws<ArgumentException>(() => NativeVariableBuilder.Build(first.Bytes, new[]
        {
            new NativeVariableBuilder.Entry("speed", SymbolEditor.VariableType.Real, "2"),
        }));

        Assert.Contains("already exists", error.Message);
    }

    [Fact]
    public void RefusesGraphWithoutVariableValueSet()
    {
        byte[] source = Source("hkbBehaviorGraphStringData", "hkbBehaviorGraphData");

        var error = Assert.Throws<InvalidOperationException>(() => NativeVariableBuilder.Build(source, new[]
        {
            new NativeVariableBuilder.Entry("Counter", SymbolEditor.VariableType.Int32),
        }));

        Assert.Contains("hkbVariableValueSet", error.Message);
    }

    private static BehaviourGraphModel Model(byte[] bytes)
    {
        var objects = new PackfileObjects(PackfileImage.Read(bytes), HavokClasses.Shipped);
        return NativeGraphModel.From(objects) ?? throw new InvalidOperationException("test file could not be modeled");
    }

    private static byte[] Source(params string[] classes)
    {
        var image = new PackfileImage();
        image.Sections.Add(new PackfileSection { TagBytes = Tag("__classnames__") });
        image.Sections.Add(new PackfileSection { TagBytes = Tag("__data__") });

        foreach (string className in classes) NativeAppend.Object(image, className);
        FixupOrder.Reorder(image);
        return image.Rebuild();
    }

    private static byte[] Tag(string name)
    {
        var bytes = new byte[20];
        Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        return bytes;
    }
}
