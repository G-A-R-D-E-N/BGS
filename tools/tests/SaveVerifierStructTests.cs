using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class SaveVerifierStructTests
{
    [Theory]
    [InlineData("eventId", "7", 99)]
    [InlineData("triggerInterval.enterEventId", "-1", 3)]
    public void RejectsStructMemberThatDidNotActuallyLand(string memberPath, string intended, int tampered)
    {
        const string ClassName = "hkbStateMachineTransitionInfoArray";
        byte[] source = Source(ClassName);
        int id = NativeGraphModel.FirstId;
        var plan = new NativeSave.Plan(
            new List<NativeSave.Change>
            {
                new(ClassName, 0, "transitions", "1", Element: 0, Grow: true, Id: id),
                new(ClassName, 0, "transitions", intended, Element: 0, Member: memberPath, Id: id),
            },
            null);

        byte[] rebuilt = NativeSave.Apply(source, plan);
        Assert.Null(Record.Exception(() => SaveVerifier.Verify(source, rebuilt, plan)));

        byte[] corrupted = TamperStructInt(rebuilt, ClassName, "transitions", 0, memberPath, tampered);
        var error = Assert.Throws<System.IO.InvalidDataException>(() =>
            SaveVerifier.Verify(source, corrupted, plan));

        Assert.Contains("did not land", error.Message);
    }

    private static byte[] TamperStructInt(byte[] bytes, string ownerClass, string field,
                                          int element, string memberPath, int value)
    {
        var image = PackfileImage.Read(bytes);
        var objects = new PackfileObjects(image, HavokClasses.Shipped);
        var instance = objects.OfClass(ownerClass).Single();
        int fieldOffset = objects.FieldAt(instance, field)
            ?? throw new InvalidOperationException($"no offset for {ownerClass}.{field}");

        var types = HavokClassTypes.Shipped;
        var fieldMember = types.Members(ownerClass).First(member => member.Name == field);
        string elementClass = fieldMember.CType
            ?? throw new InvalidOperationException($"{ownerClass}.{field} has no element class");
        int stride = types[elementClass]?.Size
            ?? throw new InvalidOperationException($"{elementClass} has no size");
        var array = objects.ArrayAt(fieldOffset, stride)
            ?? throw new InvalidOperationException($"{ownerClass}.{field} has no array storage");

        int memberOffset = 0;
        string currentClass = elementClass;
        string[] steps = memberPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < steps.Length; i++)
        {
            var member = types.Members(currentClass).First(value => value.Name == steps[i]);
            memberOffset += member.Offset;
            if (i < steps.Length - 1)
                currentClass = member.CType
                    ?? throw new InvalidOperationException($"{currentClass}.{steps[i]} is not a struct");
        }

        var data = image.Section("__data__")
            ?? throw new InvalidOperationException("test image has no data section");
        BitConverter.GetBytes(value).CopyTo(data.Data, array.At + element * stride + memberOffset);
        return image.Rebuild();
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
