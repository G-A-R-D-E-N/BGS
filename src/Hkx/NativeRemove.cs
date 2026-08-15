using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class NativeRemove
{
    public sealed record Orphaned(int Id, int PointersCleared, int ElementsDropped)
    {
        public bool Reached => PointersCleared > 0 || ElementsDropped > 0;

        public override string ToString() =>
            $"#{Id}: {PointersCleared} pointer(s) cleared, {ElementsDropped} array element(s) dropped";
    }

    private readonly record struct Run(int FieldAt, int At, int Count);

    public sealed record Deleted(int Objects, int Bytes, int FixupsDropped)
    {
        public override string ToString() =>
            $"{Objects} object(s) taken out, {Bytes} byte(s) shorter, {FixupsDropped} pointer(s) dropped";
    }

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

        var leaving = new List<(int At, int End)>();
        foreach (int index in going)
            foreach (var item in runs[index])
                leaving.Add((item.At, item.At + item.Length));

        bool Inside(int offset) => leaving.Exists(r => offset >= r.At && offset < r.End);

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

                var held = data.Globals().FirstOrDefault(g => g.Source == run.At + e * 8);
                keep.Add(held.Destination == 0 && held.Source == 0 ? -1 : held.Destination);
            }

            Shrink(image, data, section, fieldAt, run, keep);
        }

        FixupOrder.Reorder(image, types);
        return new Orphaned(id, plain.Count, dropped);
    }

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
