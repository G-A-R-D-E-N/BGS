using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// Taking an object out of the graph without taking it out of the file.
//
// Deleting properly means dropping the object's entry from the virtual fixup table, and that table
// is the object list, so every object after it renumbers. Every id above the hole moves, the per
// class index a change names moves with it, and the diff front end that matches objects by position
// within their class cannot follow a deletion the way it follows an addition. That is the exact
// hazard #19 is tracking and there is no way to check it against the engine from here, so it waits.
//
// This is the half that can be proved today. Clear every pointer into the object and leave its entry
// and its bytes where they are. Nothing renumbers, nothing shifts, no offset moves. The graph no
// longer reaches it; the file still holds it. hkxpack still lists it and so does the object list,
// and that is the honest cost of not renumbering.
//
// One thing this must not do is leave a null where a child used to be. Fallout 4 walks a node's
// children and reads each one's vtable without a null check, at `BShkbUtils::GraphTraverser::Next`,
// so a null element in a children array is a crash on load rather than an empty slot. An element
// pointing at the orphan is therefore dropped from the array and the array shrinks, which is the
// write that already exists. A plain field is cleared to null, which is what a field with no pointer
// looks like in every vanilla file, and the checker's refusal still catches the case where that
// leaves a state with no generator.
public static class NativeRemove
{
    public sealed record Orphaned(int Id, int PointersCleared, int ElementsDropped)
    {
        public bool Reached => PointersCleared > 0 || ElementsDropped > 0;

        public override string ToString() =>
            $"#{Id}: {PointersCleared} pointer(s) cleared, {ElementsDropped} array element(s) dropped";
    }

    /// Where a run of element pointers lives, and which field owns it.
    private readonly record struct Run(int FieldAt, int At, int Count);

    public static Orphaned Orphan(PackfileImage image, int id, HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;

        var data = image.Section("__data__")
                   ?? throw new InvalidOperationException("The file has no __data__ section.");
        var objects = new PackfileObjects(image, HavokClasses.Shipped);

        int index = id - NativeGraphModel.FirstId;
        if (index < 0 || index >= objects.Instances.Count)
            throw new InvalidOperationException(
                $"#{id} is not in this file, which holds #{NativeGraphModel.FirstId} to " +
                $"#{NativeGraphModel.FirstId + objects.Instances.Count - 1}.");

        int target = objects.Instances[index].Offset;
        int section = image.Sections.IndexOf(data);

        // Found from the fixup table rather than by walking the classes looking for pointers. A
        // pointer into this object is an entry whose destination is its offset, and that is true
        // however deeply the field holding it is nested.
        var incoming = data.Globals().Where(g => g.Section == section && g.Destination == target)
                           .Select(g => g.Source).ToList();

        if (incoming.Count == 0) return new Orphaned(id, 0, 0);

        var runs = PointerRuns(objects, types);

        var inRuns = new Dictionary<int, List<int>>();
        var plain = new List<int>();

        foreach (int source in incoming)
        {
            var run = runs.FirstOrDefault(r => source >= r.At && source < r.At + r.Count * 8);
            if (run.Count == 0) { plain.Add(source); continue; }

            if (!inRuns.TryGetValue(run.FieldAt, out var hits)) inRuns[run.FieldAt] = hits = new List<int>();
            hits.Add((source - run.At) / 8);
        }

        foreach (int source in plain) data.SetGlobal(source, 0, -1);

        int dropped = 0;
        foreach (var (fieldAt, hits) in inRuns)
        {
            var run = runs.First(r => r.FieldAt == fieldAt);
            var keep = new List<int>();

            for (int e = 0; e < run.Count; e++)
            {
                if (hits.Contains(e)) { dropped++; continue; }

                // A destination of its own or nothing. An element with no fixup is a null child and
                // stays null; the array only loses the elements aimed at the orphan.
                var held = data.Globals().FirstOrDefault(g => g.Source == run.At + e * 8);
                keep.Add(held.Destination == 0 && held.Source == 0 ? -1 : held.Destination);
            }

            Shrink(image, data, section, fieldAt, run, keep);
        }

        FixupOrder.Reorder(image, types);
        return new Orphaned(id, plain.Count, dropped);
    }

    /// Writes a shorter run of element pointers, appended rather than edited in place so nothing
    /// already in the file moves.
    ///
    /// The element entries are put back where the old ones sat rather than on the end of the table.
    /// Position there is not free: the table is in the order the writer walked the objects, and
    /// moving a run to the end makes hkxpack read every element of that array as null.
    private static void Shrink(PackfileImage image, PackfileSection data, int section, int fieldAt,
                               Run run, List<int> keep)
    {
        int at = keep.Count == 0 ? -1 : data.AppendData(new byte[keep.Count * 8]);
        data.SetLocal(fieldAt, at);

        var entries = data.Globals().ToList();
        int from = run.At, to = run.At + run.Count * 8;
        int first = entries.FindIndex(e => e.Source >= from && e.Source < to);
        if (first < 0) first = entries.Count;
        entries.RemoveAll(e => e.Source >= from && e.Source < to);

        var replacements = new List<(int, int, int)>();
        for (int e = 0; e < keep.Count; e++)
            if (keep[e] >= 0) replacements.Add((at + e * 8, section, keep[e]));

        entries.InsertRange(Math.Min(first, entries.Count), replacements);
        data.SetGlobals(entries);

        BitConverter.GetBytes(keep.Count).CopyTo(data.Data, fieldAt + 8);
        uint capacity = BitConverter.ToUInt32(data.Data, fieldAt + 12);
        BitConverter.GetBytes((capacity & 0xC0000000u) | (uint)keep.Count).CopyTo(data.Data, fieldAt + 12);
    }

    /// Every array of object pointers in the file, wherever it sits. Inline structs and arrays of
    /// structs are walked into, because a transition's event property array is not a field of the
    /// object that owns the transition.
    private static List<Run> PointerRuns(PackfileObjects objects, HavokClassTypes types)
    {
        var runs = new List<Run>();
        foreach (var instance in objects.Instances)
            Collect(objects, types, instance.Offset, instance.ClassName, runs, 0);
        return runs;
    }

    private static void Collect(PackfileObjects objects, HavokClassTypes types, int offset,
                                string className, List<Run> runs, int depth)
    {
        // Nothing in the corpus nests more than three deep, and a class that somehow held itself
        // would otherwise walk forever.
        if (depth > 8 || !types.Knows(className)) return;

        foreach (var member in types.Members(className))
        {
            if (!member.Written) continue;
            int at = offset + member.Offset;

            if (member.VType == "TYPE_STRUCT")
            {
                if (member.CType != null) Collect(objects, types, at, member.CType, runs, depth + 1);
                continue;
            }

            if (member.VType is not ("TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY")) continue;

            var array = objects.ArrayAt(at);
            if (array == null || array.Count == 0) continue;

            if (member.VSub == "TYPE_POINTER") { runs.Add(new Run(at, array.At, array.Count)); continue; }

            if (member.VSub != "TYPE_STRUCT" || member.CType == null) continue;

            int stride = types[member.CType]?.Size ?? 0;
            if (stride <= 0) continue;

            for (int e = 0; e < array.Count; e++)
                Collect(objects, types, array.At + e * stride, member.CType, runs, depth + 1);
        }
    }
}
