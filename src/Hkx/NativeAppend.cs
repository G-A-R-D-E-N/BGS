using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenCommonwealth.Services.Hkx;

// Putting a new object into a file without moving anything already in it.
//
// A packfile derives every offset from the sizes of what precedes it, which is why editing in place
// is only safe while nothing changes size. Appending is the way out of that, and it is not a trick:
// nothing in the format requires an object to sit anywhere in particular. The object list is the
// virtual fixup table read in order, and a pointer is a fixup entry naming a source and a
// destination. So a new object can go on the end of the data and its entry on the end of the table,
// and every byte that was already there stays where it was.
//
// That only holds because table order and file order are the same thing, which was measured rather
// than assumed: across all 533 vanilla behaviours, all 38,152 virtual entries are in strictly
// ascending source order. If they were not, appending to both ends would give the new object a
// number neither we nor hkxpack could predict.
//
// What this does not do is wire the new object to anything. It exists, it is numbered, and nothing
// points at it. Attaching it is a pointer write, which is a different piece of work that already
// exists.
public static class NativeAppend
{
    /// Where the new object landed. `Id` is what hkxpack will call it, `Index` is its position among
    /// the objects of its own class, which is how a change names one.
    public sealed record Added(int Id, int Offset, int Index)
    {
        public override string ToString() => $"#{Id} at 0x{Offset:x}, index {Index} of its class";
    }

    /// Objects are aligned to sixteen. A class holding a vector or a transform is sixteen aligned in
    /// the first place, and the game reads those with instructions that require it, so an object
    /// landing on an odd offset is a crash rather than an untidiness.
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

        // Aligned before the length is taken, so the offset written into the table is the offset the
        // object actually starts at.
        image.Section("__data__")!.AlignData(Alignment);
        int offset = data.AppendData(new byte[size]);

        var virtuals = data.Virtuals().ToList();
        virtuals.Add((offset, image.Sections.IndexOf(names), nameAt));
        data.SetVirtuals(virtuals);

        // The new object contributes nothing to either pointer table yet, since every field in it is
        // zero and a null pointer has no entry. Reordering anyway, because the tables are in the
        // order the writer walked the objects and the walk now has one more object to reach.
        FixupOrder.Reorder(image, types);

        var after = new PackfileObjects(image, HavokClasses.Shipped);

        if (after.Instances.Count != before.Instances.Count + 1)
            throw new InvalidOperationException(
                $"appending {className} changed the object count from {before.Instances.Count} to " +
                $"{after.Instances.Count} rather than adding one");

        // The two asserts that matter, and they are asserts rather than comments because everything
        // downstream of an append names an object by one number or the other. If the new object is
        // not last in the list its id is wrong, and if it is not last among its own class then every
        // change naming a later one of that class is now aimed at the wrong object.
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

    /// Points an existing object's field at another object, which is what turns an appended object
    /// from something in the file into something in the graph.
    ///
    /// Appending on its own leaves the new object dangling: it exists, it is numbered, and nothing
    /// reaches it. The graph is the pointers, so this is the half that makes the edit real.
    ///
    /// The write itself is the same one a rewire already does. A pointer is a global fixup naming a
    /// source and a destination, so aiming a field somewhere else moves no bytes and changes no
    /// lengths. The only new part is that the destination is an object that was not there a moment
    /// ago, which is why both ids are checked against the file rather than trusted.
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

        // Position in this table is not free, and this may have added an entry rather than moved
        // one, since a field that held null had no entry at all.
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

    /// Where a class's name sits in `__classnames__`, adding it when the file has never named that
    /// class before.
    ///
    /// A file that gains its first object of some class hits the second path, and it is the one
    /// worth having found now rather than after a failed load. Each name is stored as four bytes of
    /// signature, a `0x09` separator, the name, and a terminator, and the fixup points at the name
    /// itself rather than at the signature in front of it.
    private static int NameOffset(PackfileSection names, string className, uint signature)
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

        // The section is padded out to a sixteen byte boundary with 0xFF, and that padding is part
        // of the data as read. Appending after it puts the new name beyond the padding, where our
        // own reader still finds it, because it looks the name up at the offset the fixup names,
        // and hkxpack never does, because it walks the section from the front and stops at the
        // filler. The object then exists for us and does not exist for the game. Measured: the
        // append was invisible to hkxpack until the padding came off, while every check on our side
        // passed.
        int end = names.Data.Length;
        while (end > 0 && names.Data[end - 1] == 0xFF) end--;
        if (end != names.Data.Length) Array.Resize(ref names.Data, end);

        var entry = new byte[4 + 1 + wanted.Length + 1];
        BitConverter.GetBytes(signature).CopyTo(entry, 0);
        entry[4] = 0x09;
        wanted.CopyTo(entry, 5);

        // The name, not the signature in front of it, because that is what the fixup points at and
        // what reading a name back walks forward from.
        return names.AppendData(entry) + 5;
    }
}
