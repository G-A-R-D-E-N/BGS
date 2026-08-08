using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// Where a Havok packfile's data section puts things, and how to put them there again from nothing.
//
// `PackfileImage.Rebuild` already writes a file back byte for byte, but it keeps the data section
// exactly as it read it and recomputes only the offsets around it. That is enough to prove the
// container arithmetic and not enough to remove an object, because removing one moves every object
// after it and nothing knew where they would land.
//
// This knows. The order is the walk that already reproduces both fixup tables: objects in the order
// the virtual table lists them, and inside an object its members in offset order, stepping into an
// array or an inline struct at the point the member holding it is reached. Everything an object
// points at is written straight after that object and before the next one.
//
// The positions follow four rules, every one of them measured over the 531 vanilla behaviours
// rather than reasoned about, and three earlier readings of them were wrong:
//
//   An object occupies the size the game registers for its class, not the end of its last member.
//   BSRootTwistModifier is 144 registered and 112 to the end of its members.
//   Objects and array runs start on a sixteen byte boundary.
//   A string that is an element of an array of strings packs against the one before it on a two
//   byte boundary.
//   A string that is a field of its own starts on a sixteen byte boundary, unless it directly
//   follows an array run, in which case it starts at that run's last byte.
//
// Scores for the wrong readings, on Dogmeat's 2,493 items: strings aligned to two everywhere, 2,019.
// Sixteen everywhere, 2,064. Every record treated as its own block, 2,296. The rules above place all
// 138,420 objects and runs in all 531 files exactly where the file already has them.
public static class PackfileLayout
{
    /// One thing the writer puts down. `Kind` decides what it is aligned to, and is why an array's
    /// string elements are held apart from a string that is a field in its own right.
    public sealed record Item(string Kind, int At, int Length)
    {
        public bool IsString => Kind is "string" or "element string";

        public override string ToString() => $"{Kind} at 0x{At:x} for {Length}";
    }

    /// Every object and every run it points at, in the order they were written, at the offsets the
    /// file already has them at.
    ///
    /// Returns null when the walk cannot account for the file: a class the table does not describe,
    /// or a section that is not there. A partial answer would be worse than none, because the
    /// caller's next move is to lay the file out again and anything missed would be dropped.
    public static List<Item>? Of(PackfileImage image, HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;

        var data = image.Section("__data__");
        if (data == null) return null;

        PackfileObjects objects;
        try { objects = new PackfileObjects(image); }
        catch (InvalidOperationException) { return null; }

        if (objects.Instances.Any(i => !types.Knows(i.ClassName))) return null;

        var aims = new Dictionary<int, int>();
        foreach (var (source, destination) in data.Locals()) aims[source] = destination;

        var items = new List<Item>();
        var seen = new HashSet<int>();

        void Walk(int offset, string className, int depth)
        {
            // A class that somehow held itself would otherwise walk forever. Nothing in the corpus
            // nests more than three deep.
            if (depth > 8) return;

            foreach (var member in types.Members(className).OrderBy(m => m.Offset))
            {
                if (!member.Written) continue;
                int at = offset + member.Offset;

                if (member.VType is "TYPE_STRINGPTR" or "TYPE_CSTRING")
                {
                    if (aims.TryGetValue(at, out int text) && seen.Add(text))
                        items.Add(new Item("string", text, Zeroed(data.Data, text)));
                    continue;
                }

                if (member.VType == "TYPE_STRUCT")
                {
                    if (member.CType != null && types.Knows(member.CType))
                        Walk(at, member.CType, depth + 1);
                    continue;
                }

                if (member.VType is not ("TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY")) continue;

                var array = objects.ArrayAt(at);
                if (array == null || array.Count == 0) continue;
                if (!aims.ContainsKey(at)) continue;

                if (member.VSub == "TYPE_STRUCT" && member.CType != null && types.Knows(member.CType))
                {
                    int stride = types[member.CType]?.Size ?? 0;
                    if (stride <= 0) continue;

                    if (seen.Add(array.At))
                        items.Add(new Item("struct array", array.At, array.Count * stride));

                    for (int i = 0; i < array.Count; i++)
                        Walk(array.At + i * stride, member.CType, depth + 1);
                    continue;
                }

                int width = HavokClassTypes.Width(member.VSub);
                if (width <= 0) continue;

                string kind = member.VSub == "TYPE_POINTER" ? "pointer array"
                            : member.VSub is "TYPE_STRINGPTR" or "TYPE_CSTRING" ? "string array"
                            : "value array";

                if (seen.Add(array.At)) items.Add(new Item(kind, array.At, array.Count * width));

                if (member.VSub is not ("TYPE_STRINGPTR" or "TYPE_CSTRING")) continue;

                for (int i = 0; i < array.Count; i++)
                    if (aims.TryGetValue(array.At + i * 8, out int text) && seen.Add(text))
                        items.Add(new Item("element string", text, Zeroed(data.Data, text)));
            }
        }

        foreach (var instance in objects.Instances)
        {
            if (types[instance.ClassName]?.Size is not int size || size <= 0) return null;

            items.Add(new Item("object", instance.Offset, size));
            Walk(instance.Offset, instance.ClassName, 0);
        }

        return items;
    }

    /// Where each of those items would go if the section were written from nothing, in the same
    /// order. Given a file's own items, this returns that file's own offsets, which is the check.
    public static List<int> Where(IReadOnlyList<Item> items)
    {
        var at = new List<int>(items.Count);
        int cursor = 0;
        string previous = "";

        foreach (var item in items)
        {
            cursor = Align(cursor, Boundary(item.Kind, previous));
            at.Add(cursor);
            cursor += item.Length;
            previous = item.Kind;
        }

        return at;
    }

    /// Lays the data section out again from nothing and moves every pointer to match.
    ///
    /// This is the thing editing in place cannot do. In place writing only works while nothing
    /// changes size, so removing an object, or growing one past the space in front of the next, is
    /// refused today. Written this way, an object's position is worked out rather than kept, and
    /// what moves is arithmetic rather than damage.
    ///
    /// Returns false and touches nothing when the walk cannot account for the file. The caller is
    /// then no worse off than before, which is the point of not writing half of it.
    ///
    /// The check on this is `symrm relayout`: lay a vanilla file out again and it has to come back
    /// as the file it already was, because the offsets are all derived and none are carried over.
    public static bool Rewrite(PackfileImage image, HavokClassTypes? types = null)
    {
        var data = image.Section("__data__");
        if (data == null) return false;

        var items = Of(image, types);
        if (items == null) return false;

        if (!Accounted(items, data.Data.Length)) return false;

        var at = Where(items);

        // Where each old offset ends up. Kept as the items sorted by where they were, so an offset
        // part way into one, which is what every pointer source is, can be found by the item it
        // falls inside and carried across at the same distance from its start.
        var byOldOffset = items.Select((item, k) => (item, To: at[k]))
                               .OrderBy(x => x.item.At).ToList();

        int? Moved(int offset)
        {
            int low = 0, high = byOldOffset.Count - 1;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                var (item, to) = byOldOffset[mid];

                if (offset < item.At) high = mid - 1;
                else if (offset >= item.At + item.Length) low = mid + 1;
                else return to + (offset - item.At);
            }
            return null;
        }

        int end = items.Count == 0 ? 0 : at[^1] + items[^1].Length;
        var written = new byte[Align(end, NativeAppend.Alignment)];

        for (int k = 0; k < items.Count; k++)
        {
            if (items[k].At + items[k].Length > data.Data.Length) return false;
            Array.Copy(data.Data, items[k].At, written, at[k], items[k].Length);
        }

        // Every table is rewritten rather than patched, and the order is left exactly as it was.
        // Position in these tables is not free: moving entries about makes hkxpack misread the file
        // even though our own reader, which looks them up by source, is unaffected.
        var locals = new List<(int Source, int Destination)>();
        foreach (var (source, destination) in data.Locals())
        {
            if (Moved(source) is not int from || Moved(destination) is not int to) return false;
            locals.Add((from, to));
        }

        var globals = new List<(int Source, int Section, int Destination)>();
        foreach (var (source, section, destination) in data.Globals())
        {
            if (Moved(source) is not int from) return false;

            // A global's destination is an offset in whichever section it names. When that is this
            // one, which is every object to object pointer in these files, it has moved too.
            int to = section == image.Sections.IndexOf(data)
                     ? Moved(destination) ?? -1
                     : destination;
            if (to < 0) return false;

            globals.Add((from, section, to));
        }

        var virtuals = new List<(int Source, int Section, int Destination)>();
        foreach (var (source, section, destination) in data.Virtuals())
        {
            if (Moved(source) is not int from) return false;
            virtuals.Add((from, section, destination));
        }

        data.Data = written;
        data.SetLocals(locals);
        data.SetGlobals(globals);
        data.SetVirtuals(virtuals);
        return true;
    }

    /// Whether the items add up to the whole section, give or take the padding between them.
    ///
    /// This is the guard that stops a half read file being quietly shortened. A stretch the walk
    /// never reaches looks exactly like padding if all you check is whether the items you did find
    /// are where you predicted, and five skeletons passed that check while losing 288 bytes of
    /// reference pose each: the stride table had TYPE_QSTRANSFORM at sixteen rather than forty
    /// eight, so two thirds of every pose array was outside every item.
    public static bool Accounted(IReadOnlyList<Item> items, int length)
    {
        int covered = 0;
        foreach (var item in items.OrderBy(i => i.At))
        {
            if (item.At - covered >= NativeAppend.Alignment) return false;
            covered = Math.Max(covered, item.At + item.Length);
        }

        return length - covered < NativeAppend.Alignment;
    }

    /// What an item's start is rounded up to, given what was written before it.
    private static int Boundary(string kind, string previous) => kind switch
    {
        "element string" => 2,

        // A string field written straight after an array run starts at that run's last byte. This is
        // the one rule that depends on what came before, and leaving it out misplaces 429 of
        // Dogmeat's 1,208 strings.
        "string" when previous.EndsWith("array", StringComparison.Ordinal) => 1,

        _ => NativeAppend.Alignment,
    };

    /// A null terminated string's length in the section, terminator included.
    private static int Zeroed(byte[] data, int at)
    {
        int end = Array.IndexOf(data, (byte)0, at);
        return end < 0 ? data.Length - at : end - at + 1;
    }

    public static int Align(int value, int to) => (value + to - 1) / to * to;
}
