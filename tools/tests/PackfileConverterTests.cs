using System;
using System.IO;
using System.Text;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class PackfileConverterTests
{
    private static string Fixture(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures/vanilla",
                     relative.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void Fo4FixtureSupportsBothCanonicalPointerWidthDirections()
    {
        var image = PackfileImage.Read(Fixture(
            "Meshes/Actors/Character/Behaviors/SingleAnimFurniture.hkx"));
        Assert.Equal(8, image.Layout.PointerSize);
        Assert.Equal(new byte[] { 8, 1, 0, 1 }, image.LayoutRules);

        Assert.True(PackfileConverter.ConvertTo(image, PointerLayout.FourByte));
        var four = PackfileImage.Read(image.Rebuild());
        Assert.Equal(4, four.Layout.PointerSize);
        Assert.Equal(new byte[] { 4, 1, 0, 1 }, four.LayoutRules);

        Assert.True(PackfileConverter.ConvertTo(four, PointerLayout.EightByte));
        Assert.Equal(8, PackfileImage.Read(four.Rebuild()).Layout.PointerSize);
    }

    [Fact]
    public void ConvertToNamesSourceAndTargetLayoutsWhenWidthIsUnsupported()
    {
        var image = PackfileImage.Read(Fixture(
            "Meshes/Actors/Character/Behaviors/SingleAnimFurniture.hkx"));

        var error = Assert.Throws<InvalidDataException>(() =>
            PackfileConverter.ConvertTo(image, new PointerLayout(3)));

        Assert.Contains("8-byte pointers with rules [8,1,0,1]", error.Message);
        Assert.Contains("3-byte pointers with rules [3,1,0,1]", error.Message);
        Assert.Contains("target pointer width is unsupported", error.Message);
        Assert.Equal(8, image.Layout.PointerSize);
    }

    [Fact]
    public void TryConvertToReportsTheExactUnsafeSection()
    {
        var image = PackfileImage.Read(Fixture(
            "Meshes/Actors/Character/Behaviors/SingleAnimFurniture.hkx"));
        image.Sections.Add(new PackfileSection
        {
            TagBytes = Tag("__types__"),
            Data = new byte[] { 1 },
        });

        Assert.False(PackfileConverter.TryConvertTo(image, PointerLayout.FourByte, out string? reason));
        Assert.Equal("section '__types__' contains data outside __data__ and __classnames__", reason);
        Assert.Equal(8, image.Layout.PointerSize);
    }

    [Fact]
    public void TryConvertToPreservesBooleanFailClosedApi()
    {
        var image = PackfileImage.Read(Fixture(
            "Meshes/Actors/Character/Behaviors/SingleAnimFurniture.hkx"));

        Assert.False(PackfileConverter.TryConvertTo(image, new PointerLayout(3)));
        Assert.Equal(8, image.Layout.PointerSize);
    }

    private static byte[] Tag(string value)
    {
        var bytes = new byte[20];
        Encoding.ASCII.GetBytes(value).CopyTo(bytes, 0);
        return bytes;
    }
}
