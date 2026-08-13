using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;
































public static class NativePaste
{




    public sealed record Subtree(int RootId, string RootClass, IReadOnlyList<int> Ids,
                                 IReadOnlyList<int> Shared, IReadOnlyList<string> Events,
                                 IReadOnlyList<string> Variables)
    {
        public override string ToString() =>
            $"#{RootId} {RootClass}: {Ids.Count} object(s), {Shared.Count} shared, " +
            $"{Events.Count} event(s), {Variables.Count} variable(s)";
    }




    public sealed record Clip(string Path, Subtree Tree)
    {
        public override string ToString() => $"{System.IO.Path.GetFileName(Path)} {Tree}";
    }


    public sealed record Result(byte[] Bytes, int RootId, int Objects, int Pointers, int Shared,
                                int Symbols, string Note)
    {
        public override string ToString() => Note;
    }

    public static Clip Copy(string path, int rootId, HavokClassTypes? types = null)
    {
        byte[] source = InputFilePolicy.ReadHkx(path);
        return new Clip(path, Of(PackfileImage.Read(source), rootId, types));
    }












    public static Subtree Of(PackfileImage image, int rootId, HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;

        var data = image.Section("__data__")
                   ?? throw new InvalidOperationException("The file has no __data__ section.");
        var objects = new PackfileObjects(image, HavokClasses.Shipped);

        int root = rootId - NativeGraphModel.FirstId;
        if (root < 0 || root >= objects.Instances.Count)
            throw new InvalidOperationException(
                $"#{rootId} is not in this file, which holds #{NativeGraphModel.FirstId} to " +
                $"#{NativeGraphModel.FirstId + objects.Instances.Count - 1}.");

        var spans = Spans(image, objects, types);

        int section = image.Sections.IndexOf(data);
        var startsAt = new Dictionary<int, int>();
        for (int i = 0; i < objects.Instances.Count; i++) startsAt[objects.Instances[i].Offset] = i;




        var outs = new List<HashSet<int>>();
        var preds = new List<HashSet<int>>();
        for (int i = 0; i < objects.Instances.Count; i++) { outs.Add(new()); preds.Add(new()); }

        foreach (var (source, which, destination) in data.Globals())
        {
            if (which != section) continue;
            if (!startsAt.TryGetValue(destination, out int to)) continue;

            int from = Owner(spans, source);
            if (from < 0 || from == to) continue;

            outs[from].Add(to);
            preds[to].Add(from);
        }

        var owned = new HashSet<int> { root };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (int from in owned.ToList())
                foreach (int to in outs[from])
                {
                    if (owned.Contains(to)) continue;
                    if (!preds[to].All(owned.Contains)) continue;
                    owned.Add(to);
                    grew = true;
                }
        }

        var shared = new HashSet<int>();
        foreach (int from in owned)
            foreach (int to in outs[from])
                if (!owned.Contains(to)) shared.Add(to);

        var ids = owned.OrderBy(i => i).Select(i => i + NativeGraphModel.FirstId).ToList();
        var inside = Inside(spans, owned);

        var events = new List<string>();
        var variables = new List<string>();
        var eventNames = Names(objects, "eventNames");
        var variableNames = Names(objects, "variableNames");

        foreach (var (wanted, names, into) in new[]
                 {
                     (true, eventNames, events),
                     (false, variableNames, variables),
                 })
            foreach (var site in SymbolIndexFixup.IndexSites(objects, wanted, types))
            {
                if (site.Value < 0 || !inside(site.At)) continue;
                if (site.Value >= names.Count)
                    throw new InvalidOperationException(
                        $"#{rootId} uses {(wanted ? "event" : "variable")} {site.Value} in " +
                        $"{site.Owner}.{site.Member} and this file only declares {names.Count}, so " +
                        "there is no name to copy it across by.");

                if (!into.Contains(names[site.Value])) into.Add(names[site.Value]);
            }

        return new Subtree(rootId, objects.Instances[root].ClassName, ids,
                           shared.OrderBy(i => i).Select(i => i + NativeGraphModel.FirstId).ToList(),
                           events, variables);
    }







    public static Result Paste(string targetPath, Clip clip, int attachToId = -1,
                               string attachField = "", HavokClassTypes? types = null) =>
        Paste(InputFilePolicy.ReadHkx(targetPath), targetPath, clip, attachToId, attachField, types);

    public static Result Paste(byte[] targetBytes, string targetPath, Clip clip, int attachToId = -1,
                               string attachField = "", HavokClassTypes? types = null)
    {
        bool sameFile = SamePath(targetPath, clip.Path);
        byte[]? sourceBytes = sameFile ? null : InputFilePolicy.ReadHkx(clip.Path);
        return Paste(targetBytes, targetPath, sourceBytes, clip, attachToId, attachField, types);
    }

    internal static Result Paste(byte[] targetBytes, string targetPath, byte[]? sourceBytes,
                                 Clip clip, int attachToId = -1, string attachField = "",
                                 HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;

        var target = PackfileImage.Read(targetBytes);
        bool sameFile = SamePath(targetPath, clip.Path);
        var source = sameFile
            ? target
            : PackfileImage.Read(sourceBytes
                ?? throw new InvalidOperationException("The copied source bytes were not supplied."));

        var tree = Of(source, clip.Tree.RootId, types);
        var result = Into(target, source, tree, sameFile, attachToId, attachField, types);
        return result with { Bytes = target.Rebuild() };
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(System.IO.Path.GetFullPath(left), System.IO.Path.GetFullPath(right),
                      OperatingSystem.IsWindows()
                          ? StringComparison.OrdinalIgnoreCase
                          : StringComparison.Ordinal);


    public static Result Into(PackfileImage target, PackfileImage source, Subtree tree, bool sameFile,
                              int attachToId = -1, string attachField = "",
                              HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;

        var sourceData = source.Section("__data__")
                         ?? throw new InvalidOperationException("The file copied from has no __data__ section.");
        var targetData = target.Section("__data__")
                         ?? throw new InvalidOperationException("The file pasted into has no __data__ section.");

        var sourceObjects = new PackfileObjects(source, HavokClasses.Shipped);
        var before = new PackfileObjects(target, HavokClasses.Shipped);

        int sourceSection = source.Sections.IndexOf(sourceData);
        int targetSection = target.Sections.IndexOf(targetData);




        if (!sameFile && tree.Shared.Count > 0)
            throw new InvalidOperationException(
                $"#{tree.RootId} shares {tree.Shared.Count} object(s) with the rest of the file it " +
                "came from, and the file being pasted into has no such object to point at, so " +
                "nothing was pasted: " +
                string.Join(", ", tree.Shared.Take(8).Select(id => "#" + id)) +
                (tree.Shared.Count > 8 ? ", and more" : "") + ".");

        var events = Remap(sourceObjects, before, "eventNames", tree.Events, "event");
        var variables = Remap(sourceObjects, before, "variableNames", tree.Variables, "variable");

        var sourceLocals = sourceData.Locals().ToDictionary(l => l.Source, l => l.Destination);
        var sourceGlobals = new Dictionary<int, int>();
        foreach (var (s, which, d) in sourceData.Globals())
            if (which == sourceSection) sourceGlobals[s] = d;



        var made = new Dictionary<int, int>();
        var newIds = new List<int>();
        foreach (int id in tree.Ids)
        {
            var instance = sourceObjects.Instances[id - NativeGraphModel.FirstId];
            var added = NativeAppend.Object(target, instance.ClassName, types);
            made[instance.Offset] = added.Offset;
            newIds.Add(added.Id);
        }



        int? Aim(int destination)
        {
            if (made.TryGetValue(destination, out int copy)) return copy;
            return sameFile ? destination : null;
        }

        int pointers = 0;

        foreach (int id in tree.Ids)
        {
            var instance = sourceObjects.Instances[id - NativeGraphModel.FirstId];
            int to = made[instance.Offset];
            int size = types[instance.ClassName]?.Size ?? 0;
            if (size <= 0)
                throw new InvalidOperationException(
                    $"{instance.ClassName} has no size in the class table, so #{id} could not be copied.");

            Array.Copy(sourceData.Data, instance.Offset, targetData.Data, to, size);
            CopyMembers(targetData, sourceData, sourceLocals, sourceGlobals, targetSection,
                        instance.Offset, to, instance.ClassName, Aim, types, ref pointers, 0);
        }

        int rootAt = made[sourceObjects.Instances[tree.RootId - NativeGraphModel.FirstId].Offset];
        int rootId = newIds[IndexOf(tree.Ids, tree.RootId)];

        int symbols = Rewrite(target, targetData, types, made.Values.ToHashSet(), events, variables);

        string attached = "unattached";
        if (attachToId >= 0 && attachField.Length > 0)
            attached = Attach(target, targetData, targetSection, types, attachToId, attachField,
                              rootAt, rootId, tree.RootClass);

        FixupOrder.Reorder(target, types);

        string note =
            $"#{tree.RootId} pasted as #{rootId}, {tree.Ids.Count} object(s) copied, " +
            $"{pointers} reference(s) rewritten" +
            (tree.Shared.Count > 0 ? $", {tree.Shared.Count} shared object(s) left pointing at the original" : "") +
            (symbols > 0 ? $", {symbols} event or variable index/indices remapped" : "") +
            ", " + attached + ".";

        return new Result(Array.Empty<byte>(), rootId, tree.Ids.Count, pointers, tree.Shared.Count,
                          symbols, note);
    }







    private static void CopyMembers(PackfileSection targetData, PackfileSection sourceData,
                                    IReadOnlyDictionary<int, int> sourceLocals,
                                    IReadOnlyDictionary<int, int> sourceGlobals,
                                    int targetSection, int from, int to, string className,
                                    Func<int, int?> aim, HavokClassTypes types, ref int pointers,
                                    int depth)
    {


        if (depth > 8 || !types.Knows(className)) return;

        foreach (var member in types.Members(className))
        {
            if (!member.Written) continue;

            int at = from + member.Offset;
            int put = to + member.Offset;

            if (member.VType is "TYPE_STRINGPTR" or "TYPE_CSTRING")
            {
                if (sourceLocals.TryGetValue(at, out int text))
                    targetData.SetLocal(put, AppendText(targetData, sourceData.Data, text));
                continue;
            }

            if (member.VType == "TYPE_POINTER")
            {
                if (!sourceGlobals.TryGetValue(at, out int destination)) continue;
                if (aim(destination) is not int landed)
                    throw new InvalidOperationException(
                        $"{className}.{member.Name} points at something outside the copy, so there " +
                        "was nothing in the other file to aim it at and nothing was pasted.");

                targetData.SetGlobal(put, targetSection, landed);
                pointers++;
                continue;
            }

            if (member.VType == "TYPE_STRUCT")
            {
                if (member.CType != null)
                    CopyMembers(targetData, sourceData, sourceLocals, sourceGlobals,
                                targetSection, at, put, member.CType, aim, types, ref pointers,
                                depth + 1);
                continue;
            }

            if (member.VType is not ("TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY")) continue;

            int count = BitConverter.ToInt32(sourceData.Data, at + 8);
            if (count <= 0 || !sourceLocals.TryGetValue(at, out int run)) continue;

            if (member.VSub == "TYPE_STRUCT" && member.CType != null && types.Knows(member.CType))
            {
                int stride = types[member.CType]?.Size ?? 0;
                if (stride <= 0) continue;

                targetData.AlignData(NativeAppend.Alignment);
                int landed = targetData.AppendData(new byte[count * stride]);
                Array.Copy(sourceData.Data, run, targetData.Data, landed, count * stride);
                targetData.SetLocal(put, landed);

                for (int e = 0; e < count; e++)
                    CopyMembers(targetData, sourceData, sourceLocals, sourceGlobals,
                                targetSection, run + e * stride, landed + e * stride, member.CType,
                                aim, types, ref pointers, depth + 1);
                continue;
            }

            int width = HavokClassTypes.Width(member.VSub);
            if (width <= 0) continue;

            targetData.AlignData(NativeAppend.Alignment);
            int placed = targetData.AppendData(new byte[count * width]);
            Array.Copy(sourceData.Data, run, targetData.Data, placed, count * width);
            targetData.SetLocal(put, placed);

            if (member.VSub is "TYPE_STRINGPTR" or "TYPE_CSTRING")
            {
                for (int e = 0; e < count; e++)
                    if (sourceLocals.TryGetValue(run + e * 8, out int text))
                        targetData.SetLocal(placed + e * 8,
                                            AppendText(targetData, sourceData.Data, text));
                continue;
            }

            if (member.VSub != "TYPE_POINTER") continue;

            for (int e = 0; e < count; e++)
            {
                if (!sourceGlobals.TryGetValue(run + e * 8, out int destination)) continue;
                if (aim(destination) is not int element)
                    throw new InvalidOperationException(
                        $"{className}.{member.Name} holds an element pointing at something outside " +
                        "the copy, so there was nothing in the other file to aim it at and nothing " +
                        "was pasted.");

                targetData.SetGlobal(placed + e * 8, targetSection, element);
                pointers++;
            }
        }
    }









    private static int Rewrite(PackfileImage target, PackfileSection data, HavokClassTypes types,
                               HashSet<int> pastedAt, IReadOnlyDictionary<int, int> events,
                               IReadOnlyDictionary<int, int> variables)
    {


        bool moved = events.Any(m => m.Key != m.Value) || variables.Any(m => m.Key != m.Value);
        if (!moved) return 0;

        var objects = new PackfileObjects(target, HavokClasses.Shipped);
        var items = PackfileLayout.Of(target, types);
        if (items == null)
            throw new InvalidOperationException(
                "The pasted file holds a class this build cannot describe, so the copied event and " +
                "variable indices could not be found to remap, and nothing was pasted.");

        var runs = PackfileLayout.ByObject(items);
        if (runs.Count != objects.Instances.Count)
            throw new InvalidOperationException(
                "The walk over the pasted file found a different number of objects than it lists, " +
                "so nothing was pasted.");

        var spans = new List<(int At, int End)>();
        for (int i = 0; i < runs.Count; i++)
        {
            if (!pastedAt.Contains(objects.Instances[i].Offset)) continue;
            foreach (var item in runs[i]) spans.Add((item.At, item.At + item.Length));
        }

        bool Inside(int offset) => spans.Exists(s => offset >= s.At && offset < s.End);

        int changed = 0;
        foreach (var (wanted, map) in new[] { (true, events), (false, variables) })
        {
            if (map.Count == 0) continue;

            foreach (var site in SymbolIndexFixup.IndexSites(objects, wanted, types))
            {
                if (site.Value < 0 || !Inside(site.At)) continue;
                if (!map.TryGetValue(site.Value, out int to) || to == site.Value) continue;
                if (site.At + site.Width > data.Data.Length) continue;

                for (int b = 0; b < site.Width; b++) data.Data[site.At + b] = (byte)(to >> (8 * b));
                changed++;
            }
        }

        return changed;
    }








    private static string Attach(PackfileImage image, PackfileSection data, int section,
                                 HavokClassTypes types, int attachToId, string field, int rootAt,
                                 int rootId, string rootClass)
    {
        var objects = new PackfileObjects(image, HavokClasses.Shipped);

        int index = attachToId - NativeGraphModel.FirstId;
        if (index < 0 || index >= objects.Instances.Count)
            throw new InvalidOperationException(
                $"#{attachToId}, the object the paste was to hang off, is not in this file.");

        var parent = objects.Instances[index];
        var member = types.Members(parent.ClassName).FirstOrDefault(m => m.Name == field)
                     ?? throw new InvalidOperationException(
                         $"#{attachToId} is a {parent.ClassName} and has no field called {field}, so " +
                         "the paste had nowhere to hang and nothing was pasted.");

        int at = parent.Offset + member.Offset;

        if (member.VType == "TYPE_POINTER")
        {
            data.SetGlobal(at, section, rootAt);
            return $"attached to #{attachToId}.{field}";
        }

        if (member.VType is not ("TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY")
            || member.VSub != "TYPE_POINTER")
            throw new InvalidOperationException(
                $"{parent.ClassName}.{field} is {member.VType} rather than a pointer or an array of " +
                "pointers, so the paste had nowhere to hang and nothing was pasted.");

        var old = objects.ArrayAt(at);
        int count = old?.Count ?? 0;

        var keep = new List<int>();
        for (int e = 0; e < count; e++)
        {
            var held = data.Globals().FirstOrDefault(g => g.Source == old!.At + e * 8);
            keep.Add(held.Destination == 0 && held.Source == 0 ? -1 : held.Destination);
        }





        if (rootClass == "hkbStateMachineStateInfo" && field == "states")
        {
            int highest = -1;
            for (int e = 0; e < count; e++)
            {
                if (keep[e] < 0) continue;
                var state = objects.Instances.FirstOrDefault(i => i.Offset == keep[e]);
                if (state == null) continue;
                if (objects.ReadInt(state, "stateId") is int held) highest = Math.Max(highest, held);
            }

            var member2 = types.Members(rootClass).FirstOrDefault(m => m.Name == "stateId");
            if (member2 != null) BitConverter.GetBytes(highest + 1).CopyTo(data.Data, rootAt + member2.Offset);
        }

        keep.Add(rootAt);

        data.AlignData(NativeAppend.Alignment);
        int run = data.AppendData(new byte[keep.Count * 8]);

        var entries = data.Globals().ToList();
        int first = entries.Count;
        if (count > 0)
        {
            int lo = old!.At, hi = old.At + count * 8;
            int found = entries.FindIndex(e => e.Source >= lo && e.Source < hi);
            if (found >= 0) first = found;
            entries.RemoveAll(e => e.Source >= lo && e.Source < hi);
        }

        var replacements = new List<(int, int, int)>();
        for (int e = 0; e < keep.Count; e++)
            if (keep[e] >= 0) replacements.Add((run + e * 8, section, keep[e]));

        entries.InsertRange(Math.Min(first, entries.Count), replacements);
        data.SetGlobals(entries);
        data.SetLocal(at, run);

        BitConverter.GetBytes(keep.Count).CopyTo(data.Data, at + 8);
        uint capacity = BitConverter.ToUInt32(data.Data, at + 12);
        BitConverter.GetBytes((capacity & 0xC0000000u) | (uint)keep.Count).CopyTo(data.Data, at + 12);

        return $"added to #{attachToId}.{field} as element {keep.Count - 1}";
    }








    private static Dictionary<int, int> Remap(PackfileObjects source, PackfileObjects target,
                                              string field, IReadOnlyList<string> used, string what)
    {
        var map = new Dictionary<int, int>();
        if (used.Count == 0) return map;

        var from = Names(source, field);
        var to = Names(target, field);

        var missing = new List<string>();
        foreach (string name in used)
        {
            int was = from.IndexOf(name);
            int now = to.IndexOf(name);
            if (now < 0) { missing.Add(name); continue; }
            if (was >= 0) map[was] = now;
        }

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"The copy uses {missing.Count} {what}(s) the file being pasted into does not " +
                $"declare, so nothing was pasted. Declare {(missing.Count == 1 ? "it" : "them")} on " +
                $"the symbols tab and paste again: {string.Join(", ", missing)}.");

        return map;
    }


    private static List<string> Names(PackfileObjects objects, string field)
    {
        var strings = objects.OfClass("hkbBehaviorGraphStringData").FirstOrDefault();
        if (strings == null) return new List<string>();

        var names = objects.ReadStringArray(strings, field);
        return names == null ? new List<string>() : names.Select(n => n ?? "").ToList();
    }


    private static List<(int At, int End, int Object)> Spans(PackfileImage image,
                                                             PackfileObjects objects,
                                                             HavokClassTypes types)
    {
        var items = PackfileLayout.Of(image, types)
                    ?? throw new InvalidOperationException(
                        "This file holds a class this build cannot describe, so which bytes belong " +
                        "to which object cannot be worked out and nothing was copied.");

        var runs = PackfileLayout.ByObject(items);
        if (runs.Count != objects.Instances.Count)
            throw new InvalidOperationException(
                $"The walk found {runs.Count} object(s) where the file lists {objects.Instances.Count}, " +
                "so nothing was copied.");

        var spans = new List<(int, int, int)>();
        for (int i = 0; i < runs.Count; i++)
            foreach (var item in runs[i])
                spans.Add((item.At, item.At + item.Length, i));

        spans.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return spans;
    }

    private static int Owner(List<(int At, int End, int Object)> spans, int offset)
    {
        int low = 0, high = spans.Count - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (offset < spans[mid].At) high = mid - 1;
            else if (offset >= spans[mid].End) low = mid + 1;
            else return spans[mid].Object;
        }
        return -1;
    }

    private static Func<int, bool> Inside(List<(int At, int End, int Object)> spans, HashSet<int> which)
    {
        var mine = spans.Where(s => which.Contains(s.Object)).OrderBy(s => s.At).ToList();
        return offset =>
        {
            int low = 0, high = mine.Count - 1;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (offset < mine[mid].At) high = mid - 1;
                else if (offset >= mine[mid].End) low = mid + 1;
                else return true;
            }
            return false;
        };
    }

    private static int IndexOf(IReadOnlyList<int> ids, int wanted)
    {
        for (int i = 0; i < ids.Count; i++) if (ids[i] == wanted) return i;
        return -1;
    }

    private static int AppendText(PackfileSection data, byte[] from, int at)
    {
        int end = Array.IndexOf(from, (byte)0, at);
        int length = end < 0 ? from.Length - at : end - at;

        var text = new byte[length + 1];
        Array.Copy(from, at, text, 0, length);
        return data.AppendAligned(text, PackfileSection.StringAlignment);
    }
}
