using System;
using System.IO;
using System.Text;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class HkxSkeletonCountTests
{
    [Fact]
    public void DeclaredBoneCountThatExceedsTheSectionIsRejected()
    {
        var image = new PackfileImage { Predicates = new byte[16] };
        foreach (string name in new[] { "__classnames__", "__types__", "__data__" })
        {
            var tag = new byte[20];
            Encoding.ASCII.GetBytes(name).CopyTo(tag, 0);
            image.Sections.Add(new PackfileSection { TagBytes = tag });
        }

        var obj = NativeAppend.Object(image, "hkaSkeleton");
        var data = image.Section("__data__")!;
        int bones = obj.Offset + HavokClasses.Shipped.Field("hkaSkeleton", "bones")!.Offset;
        data.SetLocal(bones, obj.Offset);
        BitConverter.GetBytes(100_000).CopyTo(data.Data, bones + image.Layout.PointerSize);
        FixupOrder.Reorder(image);
        byte[] bytes = image.Rebuild();

        Assert.True(bytes.Length < 2048);
        var error = Assert.Throws<InvalidDataException>(() => new HkxBinaryReader().ReadSkeleton(bytes));
        Assert.Contains("skeleton", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeclaredBoneCountThatCrossesTheOwningSectionIsRejected()
    {
        const int count = 64;
        var image = new PackfileImage { Predicates = new byte[16] };
        foreach (string name in new[] { "__classnames__", "__types__", "__data__" })
        {
            var tag = new byte[20];
            Encoding.ASCII.GetBytes(name).CopyTo(tag, 0);
            image.Sections.Add(new PackfileSection { TagBytes = tag });
        }

        var obj = NativeAppend.Object(image, "hkaSkeleton");
        var data = image.Section("__data__")!;
        int bones = obj.Offset + HavokClasses.Shipped.Field("hkaSkeleton", "bones")!.Offset;
        data.SetLocal(bones, obj.Offset);
        BitConverter.GetBytes(count).CopyTo(data.Data, bones + image.Layout.PointerSize);
        FixupOrder.Reorder(image);
        byte[] rebuilt = image.Rebuild();

        int dataStart = 0, dataEnd = 0;
        for (int i = 0; i < 3; i++)
        {
            int header = 0x50 + i * 0x40;
            string name = Encoding.ASCII.GetString(rebuilt, header, 16).TrimEnd('\0');
            if (name != "__data__") continue;
            dataStart = BitConverter.ToInt32(rebuilt, header + 0x14);
            dataEnd = dataStart + BitConverter.ToInt32(rebuilt, header + 0x2C);
        }
        int bonesAbs = dataStart + obj.Offset;
        int needed = bonesAbs + count * 0x10;
        Assert.True(needed > dataEnd);
        byte[] padded = new byte[needed + 64];
        rebuilt.CopyTo(padded, 0);
        Assert.True(needed <= padded.Length);

        var error = Assert.Throws<InvalidDataException>(() => new HkxBinaryReader().ReadSkeleton(padded));
        Assert.Contains("skeleton", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
