using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class TagfileImageTests
{
    [Fact]
    public void PackfileIsNotMistakenForTagfile()
    {
        var packfile = new byte[] { 0x57, 0xE0, 0xE0, 0x57, 0x10, 0xC0, 0xC0, 0x10 };
        Assert.False(TagfileImage.Looks(packfile));
        Assert.Throws<InvalidDataException>(() => TagfileImage.Read(packfile, "packfile.hkx"));
    }

    [Fact]
    public void SectionTreeAndTablesAreRead()
    {
        var file = Tagfile(
            Leaf("SDKV", Encoding.ASCII.GetBytes("20150100")),
            Holder("TYPE",
                Leaf("TSTR", Strings("hkRootLevelContainer", "hkArray", "hkReal")),
                Leaf("FSTR", Strings("namedVariants", "m_data"))));

        var image = TagfileImage.Read(file);

        Assert.Equal("TAG0", image.Root.Name);
        Assert.Equal(new[] { "SDKV", "TYPE" }, image.Root.Sections.Select(s => s.Name));
        Assert.Equal("20150100", image.SdkVersion);
        Assert.Equal(new[] { "hkRootLevelContainer", "hkArray", "hkReal" }, image.TypeNames);
        Assert.Equal(new[] { "namedVariants", "m_data" }, image.FieldNames);
    }

    [Fact]
    public void SectionLengthOverrunIsRefused()
    {
        var file = Tagfile(Leaf("SDKV", Encoding.ASCII.GetBytes("20150100")));
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(8, 4), 0x40000000u | 9999);

        var error = Assert.Throws<InvalidDataException>(() => TagfileImage.Read(file, "broken.hkx"));
        Assert.Contains("does not fit", error.Message);
    }

    [Theory]
    [InlineData(new byte[] { 0x00 }, 0u, 1)]
    [InlineData(new byte[] { 0x7F }, 127u, 1)]
    [InlineData(new byte[] { 0x80, 0xCD }, 0xCDu, 2)]
    [InlineData(new byte[] { 0xBF, 0xFF }, 0x3FFFu, 2)]
    [InlineData(new byte[] { 0xC1, 0x02, 0x03 }, 0x010203u, 3)]
    [InlineData(new byte[] { 0xE1, 0x02, 0x03, 0x04 }, 0x01020304u, 4)]
    [InlineData(new byte[] { 0xE8, 0x7F, 0xFF, 0xFF, 0xFF }, 0x7FFFFFFFu, 5)]
    public void PackedIntegerFormsDecode(byte[] bytes, uint expected, int width)
    {
        int at = 0;
        Assert.Equal(expected, TagfileImage.Packed(bytes, ref at));
        Assert.Equal(width, at);
    }

    [Fact]
    public void UnknownPackedIntegerFormIsRefused()
    {
        int at = 0;
        var bytes = new byte[] { 0xFF, 0, 0, 0, 0 };
        Assert.Throws<InvalidDataException>(() => TagfileImage.Packed(bytes, ref at));
    }

    [Theory]
    [InlineData(0x80)]
    [InlineData(0xC0)]
    [InlineData(0xE0)]
    [InlineData(0xE8)]
    public void PackedIntegerCannotEscapeTnamSection(byte lead)
    {
        var file = Tagfile(
            Holder("TYPE",
                Leaf("TSTR", Strings("hkRootLevelContainer")),
                Leaf("TNAM", new byte[] { 2, lead }),
                Leaf("NEXT", Array.Empty<byte>())));

        var error = Assert.Throws<InvalidDataException>(() => TagfileImage.Read(file, "bounded.hkx"));
        Assert.Contains("TNAM", error.Message);
        Assert.Contains("packed integer", error.Message);
    }

    [Fact]
    public void PackedIntegerCannotEscapeTbodSection()
    {
        var file = Tagfile(
            Holder("TYPE",
                Leaf("TBOD", new byte[] { 2, 0, 0x80 }),
                Leaf("NEXT", Array.Empty<byte>())));

        var error = Assert.Throws<InvalidDataException>(() => TagfileImage.Read(file, "bounded.hkx"));
        Assert.Contains("TBOD", error.Message);
        Assert.Contains("packed integer", error.Message);
    }

    [Fact]
    public void TemplateArgumentCountMustFitTnamSection()
    {
        var file = Tagfile(
            Holder("TYPE",
                Leaf("TSTR", Strings("hkRootLevelContainer")),
                Leaf("TNAM", new byte[] { 2, 0, 0xE8, 0x80, 0, 0, 0 })));

        var error = Assert.Throws<InvalidDataException>(() => TagfileImage.Read(file, "count.hkx"));
        Assert.Contains("TNAM", error.Message);
        Assert.Contains("template", error.Message);
    }

    [Fact]
    public void TypeCarriesTemplateArguments()
    {
        var file = Tagfile(
            Holder("TYPE",
                Leaf("TSTR", Strings("hkRootLevelContainer", "hkArray", "tT", "hkReal")),
                Leaf("TNAM", new byte[] { 3, 0, 0, 1, 1, 2, 3 })));

        var types = TagfileImage.Read(file).Types;

        Assert.Equal(2, types.Count);
        Assert.Equal("hkRootLevelContainer", types[0].ToString());
        Assert.Equal("hkArray<tT=3>", types[1].ToString());
        Assert.True(types[1].Arguments[0].NamesAType);
    }

    [Fact]
    public void TypeLayoutPreservesMemberOffsets()
    {
        var file = Tagfile(
            Holder("TYPE",
                Leaf("FSTR", Strings("m_data", "m_size", "m_capacityAndFlags")),
                Leaf("TBOD", new byte[]
                {
                    2, 0, 0x2B,
                    8, 3,
                    16, 8,
                    3,
                    0, 0x22, 0, 5,
                    1, 0x22, 8, 6,
                    2, 0x22, 12, 6,
                })));

        var layout = Assert.Single(TagfileImage.Read(file).Layouts);

        Assert.Equal(16u, layout.Size);
        Assert.Equal(8u, layout.Alignment);
        Assert.Equal(
            new[] { "m_data", "m_size", "m_capacityAndFlags" },
            layout.Members.Select(m => m.Name));
        Assert.Equal(new uint[] { 0, 8, 12 }, layout.Members.Select(m => m.Offset));
    }

    [Fact]
    public void RecordOverrunIsRefused()
    {
        var file = Tagfile(
            Holder("TYPE",
                Leaf("FSTR", Strings("m_data")),
                Leaf("TBOD", new byte[] { 2, 0, 0x20, 40, 0, 0x22, 0, 5 })));

        Assert.Throws<InvalidDataException>(() => TagfileImage.Read(file, "odd.hkx"));
    }

    [Fact]
    public void InterfaceFlagConsumesItsField()
    {
        var file = Tagfile(
            Holder("TYPE",
                Leaf("FSTR", Strings("m_data")),
                Leaf("TBOD", new byte[]
                {
                    2, 0, 0x48,
                    16, 8,
                    7,
                    3, 0, 0x08,
                    4, 4,
                })));

        var layouts = TagfileImage.Read(file).Layouts;

        Assert.Equal(2, layouts.Count);
        Assert.Equal(16u, layouts[0].Size);
        Assert.Equal(3u, layouts[1].TypeIndex);
        Assert.Equal(4u, layouts[1].Size);
    }

    [Fact]
    public void ClassTableUsesDeclaredLayout()
    {
        var file = Tagfile(
            Holder("TYPE",
                Leaf("TSTR", Strings("hkReferencedObject", "hkArray", "hkInt32")),
                Leaf("TNAM", new byte[] { 4, 0, 0, 1, 0, 2, 0 }),
                Leaf("FSTR", Strings("m_data", "m_size")),
                Leaf("TBOD", new byte[]
                {
                    2, 1, 0x28,
                    16, 8,
                    2,
                    0, 0x22, 0, 1,
                    1, 0x22, 8, 3,
                })));

        var classes = TagfileClasses.Of(TagfileImage.Read(file));
        var array = classes["hkArray"];

        Assert.Equal("hkReferencedObject", array.Parent);
        Assert.Equal(16u, array.Size);
        Assert.Equal(0u, array["m_data"]!.Offset);
        Assert.Equal("hkReferencedObject", array["m_data"]!.TypeName);
        Assert.Equal("hkInt32", array["m_size"]!.TypeName);
    }

    private static byte[] Strings(params string[] values)
    {
        using var stream = new MemoryStream();
        foreach (string value in values)
        {
            stream.Write(Encoding.ASCII.GetBytes(value));
            stream.WriteByte(0);
        }
        return stream.ToArray();
    }

    private static byte[] Leaf(string name, byte[] body) => Section(name, body, false);

    private static byte[] Holder(string name, params byte[][] inner) =>
        Section(name, inner.SelectMany(bytes => bytes).ToArray(), true);

    private static byte[] Tagfile(params byte[][] inner) => Holder("TAG0", inner);

    private static byte[] Section(string name, byte[] body, bool holds)
    {
        var bytes = new byte[8 + body.Length];
        uint header = (uint)bytes.Length;
        if (!holds) header |= 0x40000000u;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), header);
        Encoding.ASCII.GetBytes(name).CopyTo(bytes, 4);
        body.CopyTo(bytes, 8);
        return bytes;
    }
}
