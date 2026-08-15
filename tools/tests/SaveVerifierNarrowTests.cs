using System;
using System.Collections.Generic;
using System.Text;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class SaveVerifierNarrowTests
{
    [Theory]
    [InlineData("animationBindingIndex", "-1")]
    [InlineData("animationBindingIndex", "-32768")]
    public void ANegativeNarrowIntegerIsAcceptedByTheVerifier(string field, string value)
    {
        byte[] source = Source("hkbClipGenerator");
        var plan = new NativeSave.Plan(
            new List<NativeSave.Change>
            {
                new("hkbClipGenerator", 0, field, value, Id: NativeGraphModel.FirstId),
            },
            null);

        byte[] rebuilt = NativeSave.Apply(source, plan);

        Exception? error = Record.Exception(() => SaveVerifier.Verify(source, rebuilt, plan));
        Assert.Null(error);
    }

    [Fact]
    public void APositiveNarrowIntegerStillVerifies()
    {
        byte[] source = Source("hkbClipGenerator");
        var plan = new NativeSave.Plan(
            new List<NativeSave.Change>
            {
                new("hkbClipGenerator", 0, "animationBindingIndex", "7", Id: NativeGraphModel.FirstId),
            },
            null);

        byte[] rebuilt = NativeSave.Apply(source, plan);

        Assert.Null(Record.Exception(() => SaveVerifier.Verify(source, rebuilt, plan)));
    }

    [Fact]
    public void AValueThatDidNotLandIsStillRejected()
    {
        byte[] source = Source("hkbClipGenerator");
        var applied = new NativeSave.Plan(
            new List<NativeSave.Change>
            {
                new("hkbClipGenerator", 0, "animationBindingIndex", "-1", Id: NativeGraphModel.FirstId),
            },
            null);
        byte[] rebuilt = NativeSave.Apply(source, applied);

        var claimed = new NativeSave.Plan(
            new List<NativeSave.Change>
            {
                new("hkbClipGenerator", 0, "animationBindingIndex", "-2", Id: NativeGraphModel.FirstId),
            },
            null);

        Assert.Throws<System.IO.InvalidDataException>(() => SaveVerifier.Verify(source, rebuilt, claimed));
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
