using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenCommonwealth.Services.Hkx;

// Writes the class metadata a file can carry about itself into its __types__ section, as the
// hkClass / hkClassMember / hkClassEnum / hkClassEnumItem objects the reader expects back.
// Fallout 4 ships the section empty and nothing requires it, so this is opt-in: a file that
// gains it can be checked against the shipped table, by this build or by anything else that
// reads the format.
public static class EmbeddedClassWriter
{
    public sealed record Result(int Described, int Bytes, IReadOnlyList<string> Refused)
    {
        public override string ToString() =>
            $"described {Described} class(es) in {Bytes} bytes" +
            (Refused.Count > 0 ? $", refused {Refused.Count}" : "");
    }

    // Every class the file names, plus everything those reach through a parent, a struct member,
    // or an array element. A description that stopped at the named classes would point at
    // members whose own class was never written.
    public static IReadOnlyList<string> Reachable(PackfileImage image, HavokClassTypes? types = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        var schema = types ?? HavokClassTypes.Shipped;

        var seen = new SortedSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();

        var data = image.Section(PackfileObjects.DataSection);
        var names = image.Section("__classnames__");
        if (data == null || names == null) return Array.Empty<string>();

        foreach (var (_, name) in new PackfileObjects(image, null, schema).ClassNames())
            if (schema.Knows(name)) queue.Enqueue(name);

        while (queue.Count > 0)
        {
            string name = queue.Dequeue();
            if (!seen.Add(name)) continue;

            var layout = schema[name];
            if (layout == null) continue;
            if (layout.Parent != null) queue.Enqueue(layout.Parent);

            foreach (var member in schema[name]!.Declared)
                if (member.CType != null) queue.Enqueue(member.CType);
        }

        return seen.ToList();
    }

    public static Result Write(PackfileImage image, IEnumerable<string>? classes = null,
                               HavokClassTypes? types = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        var schema = types ?? HavokClassTypes.Shipped;
        var wanted = (classes ?? Reachable(image, schema)).Distinct(StringComparer.Ordinal).ToList();

        var refused = new List<string>();
        var describe = new List<string>();
        foreach (string name in wanted)
        {
            if (!schema.Knows(name)) { refused.Add($"{name}: the build has no definition"); continue; }
            if (!LayoutWalker.CanPlace(schema, name)) { refused.Add($"{name}: not placeable at this pointer size"); continue; }
            describe.Add(name);
        }

        var section = image.Section(EmbeddedClasses.Section);
        if (section == null)
        {
            section = new PackfileSection { TagBytes = Tag(EmbeddedClasses.Section) };
            // the class-name and data sections keep their positions; types belongs between them,
            // which is where every file that carries one puts it
            int at = image.Sections.FindIndex(s => s.Tag == PackfileObjects.DataSection);
            image.Sections.Insert(at < 0 ? image.Sections.Count : at, section);
        }

        var built = Build(image, schema, describe);
        section.Data = built.Data;
        section.SetLocals(built.Locals);
        section.SetVirtuals(built.Virtuals);
        section.GlobalFixups = Array.Empty<byte>();
        section.Exports = Array.Empty<byte>();
        section.Imports = Array.Empty<byte>();

        return new Result(describe.Count, built.Data.Length, refused);
    }

    private sealed record Built(
        byte[] Data,
        List<(int Source, int Destination)> Locals,
        List<(int Source, int Section, int Destination)> Virtuals);

    private static Built Build(PackfileImage image, HavokClassTypes schema, IReadOnlyList<string> describe)
    {
        var layout = image.Layout;
        // the record classes come from the shipped table whatever schema is being described,
        // because they are the section's own encoding rather than part of its content
        var meta = HavokClassTypes.Shipped;
        var classLayout = LayoutWalker.Of(meta, "hkClass", layout);
        var memberLayout = LayoutWalker.Of(meta, "hkClassMember", layout);
        int classSize = classLayout.Size;
        int memberSize = memberLayout.Size;

        var blob = new List<byte>();
        var locals = new List<(int, int)>();
        var virtuals = new List<(int, int, int)>();
        var strings = new Dictionary<string, int>(StringComparer.Ordinal);
        var starts = new Dictionary<string, int>(StringComparer.Ordinal);

        void Pad(int alignment)
        {
            while (blob.Count % alignment != 0) blob.Add(0);
        }

        int Text(string value)
        {
            if (strings.TryGetValue(value, out int at)) return at;
            Pad(2);
            at = blob.Count;
            blob.AddRange(Encoding.UTF8.GetBytes(value));
            blob.Add(0);
            strings[value] = at;
            return at;
        }

        // objects first, so a member pointing at another class has a destination to name
        foreach (string name in describe)
        {
            Pad(16);
            starts[name] = blob.Count;
            blob.AddRange(new byte[classSize]);
        }

        int selfSection = image.Sections.FindIndex(s => s.Tag == EmbeddedClasses.Section);
        int classNameAt = ClassNameOffset(image, "hkClass");

        foreach (string name in describe)
        {
            int start = starts[name];
            var definition = schema[name]!;
            var declared = definition.Declared;
            var walked = LayoutWalker.Of(schema, name, layout);

            virtuals.Add((start, selfSection, classNameAt));
            Point(locals, blob, classLayout, start, "name", Text(name));

            if (definition.Parent != null && starts.TryGetValue(definition.Parent, out int parentAt))
                Point(locals, blob, classLayout, start, "parent", parentAt);

            Put32(blob, classLayout, start, "objectSize", walked.Size);
            Put32(blob, classLayout, start, "numImplementedInterfaces", 0);
            Put32(blob, classLayout, start, "describedVersion", 0);

            if (declared.Count == 0) continue;

            Pad(16);
            int members = blob.Count;
            blob.AddRange(new byte[declared.Count * memberSize]);

            Point(locals, blob, classLayout, start, "declaredMembers", members);
            // a simple array carries its count in the int that follows the pointer
            Put32Raw(blob, start + (classLayout.OffsetOf("declaredMembers") ?? 0) + layout.PointerSize,
                     declared.Count);

            for (int i = 0; i < declared.Count; i++)
            {
                var member = declared[i];
                int record = members + i * memberSize;

                Point(locals, blob, memberLayout, record, "name", Text(member.Name));
                if (member.CType != null && starts.TryGetValue(member.CType, out int target))
                    Point(locals, blob, memberLayout, record, "class", target);

                PutNarrow(blob, memberLayout, record, "type", HavokMemberTypes.Value(member.VType) ?? 0, 1);
                PutNarrow(blob, memberLayout, record, "subtype", HavokMemberTypes.Value(member.VSub) ?? 0, 1);
                PutNarrow(blob, memberLayout, record, "cArraySize", member.ArrSize, 2);
                PutNarrow(blob, memberLayout, record, "offset", walked.OffsetOf(member.Name) ?? 0, 2);
            }
        }

        Pad(16);
        return new Built(blob.ToArray(), locals, virtuals);
    }

    private static void Point(List<(int, int)> locals, List<byte> blob, ObjectLayout layout,
                              int record, string member, int destination)
    {
        if (layout.OffsetOf(member) is not int offset) return;
        locals.Add((record + offset, destination));
    }

    private static void Put32(List<byte> blob, ObjectLayout layout, int record, string member, int value)
    {
        if (layout.OffsetOf(member) is not int offset) return;
        Put32Raw(blob, record + offset, value);
    }

    private static void Put32Raw(List<byte> blob, int at, int value)
    {
        if (at < 0 || at + 4 > blob.Count) return;
        var bytes = BitConverter.GetBytes(value);
        for (int i = 0; i < 4; i++) blob[at + i] = bytes[i];
    }

    private static void PutNarrow(List<byte> blob, ObjectLayout layout, int record, string member,
                                  int value, int width)
    {
        if (layout.OffsetOf(member) is not int offset) return;
        int at = record + offset;
        if (at < 0 || at + width > blob.Count) return;
        for (int i = 0; i < width; i++) blob[at + i] = (byte)((value >> (8 * i)) & 0xFF);
    }

    // The virtual fixup names the class through the class-name table, so a file that never held
    // an hkClass object has to gain the name before it can describe anything.
    private static int ClassNameOffset(PackfileImage image, string className)
    {
        var names = image.Section("__classnames__")
                    ?? throw new InvalidOperationException("The file has no __classnames__ section.");

        var types = HavokClassTypes.Shipped;
        var blob = names.Data;
        for (int at = 0; at + 5 < blob.Length; )
        {
            int end = Array.IndexOf(blob, (byte)0, at + 5);
            if (end < 0) break;
            if (Encoding.ASCII.GetString(blob, at + 5, end - at - 5) == className) return at + 5;
            at = end + 1;
        }

        uint signature = HavokClassTypes.Shipped[className]?.Signature ?? 0;
        int start = blob.Length;
        var entry = new byte[5 + className.Length + 1];
        BitConverter.GetBytes(signature).CopyTo(entry, 0);
        entry[4] = 0x09;
        Encoding.ASCII.GetBytes(className).CopyTo(entry, 5);
        names.AppendData(entry);
        return start + 5;
    }

    private static byte[] Tag(string name)
    {
        var tag = new byte[20];
        Array.Fill(tag, (byte)0xFF);
        var ascii = Encoding.ASCII.GetBytes(name);
        Array.Copy(ascii, tag, ascii.Length);
        tag[ascii.Length] = 0;
        return tag;
    }
}
