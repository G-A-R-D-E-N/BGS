using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenCommonwealth.Services.Hkx;

public static class NativeAppend
{

    public sealed record Added(int Id, int Offset, int Index)
    {
        public override string ToString() => $"#{Id} at 0x{Offset:x}, index {Index} of its class";
    }

    public const int Alignment = 16;

    public static Added Object(PackfileImage image, string className, HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;

        var layout = types[className]
                     ?? throw new InvalidOperationException(
                         $"The class table has no entry for {className}, so its size is unknown " +
                         "and an instance of it cannot be laid out.");

        if (layout.Size is not int size || size <= 0)
            throw new InvalidOperationException(
                $"{className} has no size in the class table, so an instance of it cannot be laid out.");

        var data = image.Section("__data__")
                   ?? throw new InvalidOperationException("The file has no __data__ section.");
        var names = image.Section("__classnames__")
                    ?? throw new InvalidOperationException("The file has no __classnames__ section.");

        var before = new PackfileObjects(image, HavokClasses.Shipped);
        int expectedId = NativeGraphModel.FirstId + before.Instances.Count;
        int expectedIndex = before.Instances.Count(i => i.ClassName == className);

        int nameAt = NameOffset(names, className, layout.Signature);

        image.Section("__data__")!.AlignData(Alignment);
        int offset = data.AppendData(new byte[size]);

        var virtuals = data.Virtuals().ToList();
        virtuals.Add((offset, image.Sections.IndexOf(names), nameAt));
        data.SetVirtuals(virtuals);

        FixupOrder.Reorder(image, types);

        var after = new PackfileObjects(image, HavokClasses.Shipped);

        if (after.Instances.Count != before.Instances.Count + 1)
            throw new InvalidOperationException(
                $"appending {className} changed the object count from {before.Instances.Count} to " +
                $"{after.Instances.Count} rather than adding one");

        int actualId = NativeGraphModel.FirstId + after.Instances.Count - 1;
        var last = after.Instances[^1];

        if (actualId != expectedId || last.Offset != offset || last.ClassName != className)
            throw new InvalidOperationException(
                $"the new {className} was expected to be #{expectedId} at 0x{offset:x} and is " +
                $"#{actualId} at 0x{last.Offset:x} holding {last.ClassName}");

        int actualIndex = after.Instances.Count(i => i.ClassName == className) - 1;
        if (actualIndex != expectedIndex)
            throw new InvalidOperationException(
                $"the new {className} was expected to be index {expectedIndex} of its class and is " +
                $"{actualIndex}");

        return new Added(expectedId, offset, expectedIndex);
    }

    public static void Attach(PackfileImage image, int fromId, string field, int toId,
                              HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;

        var data = image.Section("__data__")
                   ?? throw new InvalidOperationException("The file has no __data__ section.");
        var objects = new PackfileObjects(image, HavokClasses.Shipped);

        var from = At(objects, fromId, "the object being pointed from");
        var to = At(objects, toId, "the object being pointed at");

        if (objects.FieldAt(from, field) is not int at)
            throw new InvalidOperationException(
                $"#{fromId} is a {from.ClassName} and has no field called {field}, so nothing " +
                "was written.");

        var member = types.Members(from.ClassName).FirstOrDefault(m => m.Name == field);
        if (member is null || member.VType != "TYPE_POINTER")
            throw new InvalidOperationException(
                $"{from.ClassName}.{field} is {(member is null ? "not a field" : member.VType)} " +
                "rather than a pointer, so pointing it at an object would be writing nonsense.");

        data.SetGlobal(at, image.Sections.IndexOf(data), to.Offset);

        FixupOrder.Reorder(image, types);
    }

    private static PackfileObjects.Instance At(PackfileObjects objects, int id, string what)
    {
        int index = id - NativeGraphModel.FirstId;
        if (index < 0 || index >= objects.Instances.Count)
            throw new InvalidOperationException(
                $"#{id}, {what}, is not in this file, which holds " +
                $"#{NativeGraphModel.FirstId} to " +
                $"#{NativeGraphModel.FirstId + objects.Instances.Count - 1}.");

        return objects.Instances[index];
    }

    public static int NameOffset(PackfileSection names, string className, uint signature)
    {
        var wanted = Encoding.ASCII.GetBytes(className);

        for (int at = 0; at + wanted.Length + 1 <= names.Data.Length; at++)
        {
            if (at > 0 && names.Data[at - 1] != 0x09) continue;
            if (names.Data[at + wanted.Length] != 0x00) continue;

            bool same = true;
            for (int i = 0; i < wanted.Length && same; i++) same = names.Data[at + i] == wanted[i];
            if (same) return at;
        }

        int end = names.Data.Length;
        while (end > 0 && names.Data[end - 1] == 0xFF) end--;
        if (end != names.Data.Length) Array.Resize(ref names.Data, end);

        var entry = new byte[4 + 1 + wanted.Length + 1];
        BitConverter.GetBytes(signature).CopyTo(entry, 0);
        entry[4] = 0x09;
        wanted.CopyTo(entry, 5);

        return names.AppendData(entry) + 5;
    }
}
