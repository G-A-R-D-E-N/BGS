using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// Taking an object out of the graph, and out of the file.
//
// Two things here, and they used to be one because only the first could be done. `Orphan` clears
// every pointer into an object and leaves its entry and its bytes exactly where they are: nothing
// renumbers, nothing shifts, and the file still holds it. `Delete` takes it out for real.
//
// Deleting means dropping the object's entry from the virtual fixup table, which is the object list,
// so every object after it renumbers and every byte after it moves. There was nowhere for the rest
// to go until a file could be laid out rather than edited, which is what `PackfileLayout` is.
//
// What deleting still does not settle is the renumbering hazard #19 is tracking. Every id above the
// hole shifts, and no check here can say what Fallout 4 makes of that; the file is correct by every
// measure available without the game. 531 of 531 vanilla behaviours have an object taken out and
// come back reading correctly, with the object gone, the section fully accounted for and no pointer
// left aiming into the hole.
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

    /// What a deletion took out.
    public sealed record Deleted(int Objects, int Bytes, int FixupsDropped)
    {
        public override string ToString() =>
            $"{Objects} object(s) taken out, {Bytes} byte(s) shorter, {FixupsDropped} pointer(s) dropped";
    }

    /// Takes objects out of the file properly, rather than leaving them in it unreferenced.
    ///
    /// This is what orphaning could not do. An object's bytes are not the object alone: a state
    /// machine takes its transition array and its name with it, and those sit wherever the writer
    /// put them. Removing any of it moves everything after it, so until the file could be laid out
    /// rather than edited there was nowhere for the rest to go. `PackfileLayout` is that, and this
    /// is the walk it needs done before the object leaves, because afterwards nothing says which
    /// runs were its.
    ///
    /// What this does not decide is whether deleting is safe to do. Every id above a hole shifts,
    /// which is the hazard #19 is tracking, and the caller has to have thought about that. What is
    /// checked here is the part that is checkable: nothing may still point at what is going.
    ///
    /// The class name section is left alone. A name nothing uses any more is dead text in a section
    /// the file already pads, and rewriting it would move every name after it for no gain.
    public static Deleted Delete(PackfileImage image, IReadOnlyCollection<int> ids,
                                 HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;

        var data = image.Section("__data__")
                   ?? throw new InvalidOperationException("The file has no __data__ section.");

        var objects = new PackfileObjects(image, HavokClasses.Shipped);

        var items = PackfileLayout.Of(image, types)
                    ?? throw new InvalidOperationException(
                        "This file holds a class this build cannot describe, so nothing was deleted: " +
                        "working out where the rest would go needs every object accounted for.");

        int section = image.Sections.IndexOf(data);

        // The looser of the two checks on purpose. Asking whether the walk covers every byte is
        // right for an untouched file and wrong here: an edit that appended anything, a longer
        // string or a resized array, leaves the run it replaced with nothing pointing at it, and
        // refusing over those would mean a file could never be deleted from after being edited.
        // Laying it out again drops them, which is a tidy up. What must not be dropped is something
        // still pointed at, and that is what this asks.
        if (!PackfileLayout.Reaches(items, data, section))
            throw new InvalidOperationException(
                "Something in this file points at bytes the reader cannot place, so nothing was " +
                "deleted: laying it out again would drop what that pointer names.");

        var runs = PackfileLayout.ByObject(items);
        if (runs.Count != objects.Instances.Count)
            throw new InvalidOperationException(
                $"The walk found {runs.Count} object(s) where the file lists {objects.Instances.Count}, " +
                "so nothing was deleted.");

        var going = new List<int>();
        foreach (int id in ids.Distinct())
        {
            int index = id - NativeGraphModel.FirstId;
            if (index < 0 || index >= objects.Instances.Count)
                throw new InvalidOperationException(
                    $"#{id} is not in this file, which holds #{NativeGraphModel.FirstId} to " +
                    $"#{NativeGraphModel.FirstId + objects.Instances.Count - 1}.");
            going.Add(index);
        }

        if (going.Count == 0) return new Deleted(0, 0, 0);

        // Every byte that is leaving, so a pointer can be asked whether it is aimed into the hole
        // or sitting inside one.
        var leaving = new List<(int At, int End)>();
        foreach (int index in going)
            foreach (var item in runs[index])
                leaving.Add((item.At, item.At + item.Length));

        bool Inside(int offset) => leaving.Exists(r => offset >= r.At && offset < r.End);

        // The check that makes this safe to offer. A pointer left aiming at a deleted object is not
        // a dangling reference the game shrugs off, it is a vtable read on freed space at
        // BShkbUtils::GraphTraverser::Next. Detaching first is the caller's job and this refuses
        // rather than doing it silently, because what to put in a field's place is a graph decision.
        foreach (var (source, whichSection, destination) in data.Globals())
        {
            if (whichSection != section || !Inside(destination) || Inside(source)) continue;

            int at = objects.IndexOf(objects.Instances.First(i => i.Offset == destination));
            throw new InvalidOperationException(
                $"Something still points at #{NativeGraphModel.FirstId + at}, from offset " +
                $"0x{source:x}, so nothing was deleted. Detach it first.");
        }

        int before = data.Data.Length;
        int dropped = 0;

        // Fixups first, then the layout, because the layout refuses a table naming a byte it is not
        // going to write.
        var locals = new List<(int Source, int Destination)>();
        foreach (var entry in data.Locals())
        {
            if (Inside(entry.Source) || Inside(entry.Destination)) { dropped++; continue; }
            locals.Add(entry);
        }

        var globals = new List<(int Source, int Section, int Destination)>();
        foreach (var entry in data.Globals())
        {
            if (Inside(entry.Source)) { dropped++; continue; }
            globals.Add(entry);
        }

        // The virtual table is the object list, so this is the line that actually removes them.
        var virtuals = new List<(int Source, int Section, int Destination)>();
        foreach (var entry in data.Virtuals())
        {
            if (Inside(entry.Source)) { dropped++; continue; }
            virtuals.Add(entry);
        }

        data.SetLocals(locals);
        data.SetGlobals(globals);
        data.SetVirtuals(virtuals);

        var kept = new List<PackfileLayout.Item>();
        for (int index = 0; index < runs.Count; index++)
            if (!going.Contains(index)) kept.AddRange(runs[index]);

        if (!PackfileLayout.RewriteAs(image, kept))
            throw new InvalidOperationException(
                "The file could not be laid out again after the deletion, so nothing was written.");

        return new Deleted(going.Count, before - data.Data.Length, dropped);
    }

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
