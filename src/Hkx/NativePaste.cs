using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// Copying a subtree and pasting it back, into the same file or into another one.
//
// Building the same generator shape once per idle is the main thing anybody does with this tool by
// hand, and it is the thing a copy turns into one action. What makes it worth being careful about is
// that a copy which gets one reference wrong looks entirely correct: the tree draws the same shape,
// the checker is happy, and the graph plays the original's child rather than the copy's, because one
// pointer inside the copy still names an object of the original.
//
// So the rules here are about references and nothing else.
//
// **What a copy carries.** The objects the root owns, meaning the root and everything every pointer
// into which comes from inside the set. That is worked out as a fixpoint rather than by following
// child fields down a list of classes: a class this build has never heard of still has its pointers
// in the fixup table, and the table is what the walk reads. Anything the subtree points at that
// something outside also points at is shared rather than copied, because copying it would give the
// graph two of something the file deliberately has one of.
//
// **What a paste writes.** One appended object per copied object, its bytes copied over, and every
// run hanging off it copied too: its name, its arrays, the arrays inside its struct arrays. A run is
// not the bytes alone. A struct can hold a name, and a name is a pointer with a fixup naming it, so
// copying the bytes and stopping gives a second array whose strings are empty.
//
// **What a paste rewrites.** Every pointer inside the copy that named a copied object now names its
// copy. Every pointer that named something shared still names the original, which is the one case
// where pointing at the original is right. Every event and variable index is remapped by NAME rather
// than carried across as a number, because index four means different things in two files.
//
// **What a paste refuses.** A subtree going into another file which shares something with the file
// it came from, since the other file has no such object to point at. A subtree using an event or a
// variable the other file does not declare, named so the answer is "declare these two and paste
// again" rather than "it did not work".
public static class NativePaste
{
    /// What a root owns, and what it only borrows.
    ///
    /// `Ids` and `Shared` are hkxpack style numbers, the same ones the tree and the panel show, so a
    /// refusal can name an object a person can go and look at.
    public sealed record Subtree(int RootId, string RootClass, IReadOnlyList<int> Ids,
                                 IReadOnlyList<int> Shared, IReadOnlyList<string> Events,
                                 IReadOnlyList<string> Variables)
    {
        public override string ToString() =>
            $"#{RootId} {RootClass}: {Ids.Count} object(s), {Shared.Count} shared, " +
            $"{Events.Count} event(s), {Variables.Count} variable(s)";
    }

    /// A copy, held between the two halves of the action. The bytes are not carried: the source file
    /// is read again at paste time, so a copy cannot go stale against a file that was saved in
    /// between without that being noticed.
    public sealed record Clip(string Path, Subtree Tree)
    {
        public override string ToString() => $"{System.IO.Path.GetFileName(Path)} {Tree}";
    }

    /// What a paste did, or what it would have done.
    public sealed record Result(byte[] Bytes, int RootId, int Objects, int Pointers, int Shared,
                                int Symbols, string Note)
    {
        public override string ToString() => Note;
    }

    public static Clip Copy(string path, int rootId, HavokClassTypes? types = null) =>
        new(path, Of(PackfileImage.Read(path), rootId, types));

    /// Which objects a root owns, which it shares, and which symbols the set uses.
    ///
    /// Ownership is "every pointer aimed at this object comes from something already being copied",
    /// grown from the root until it stops growing. That is exactly the set of objects every route to
    /// which passes through the root, so nothing reachable from elsewhere is ever taken.
    ///
    /// A pointer cycle inside a subtree would leave both of its objects waiting for the other and so
    /// both shared rather than copied. That is the safe way round, and it does not happen: `symrm
    /// paste` counts the objects on a cycle over the whole corpus and reports 0 in 0 of the 531
    /// vanilla behaviours. It is counted on every run rather than recorded here once, so a file that
    /// did have one would say so rather than quietly coming out shared.
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

        // The graph, read off the fixup table rather than off the classes. A pointer stored in an
        // element of an array belongs to whichever object owns that array, which is what the spans
        // are for: they say which object's stretch of the section an offset falls in.
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

    /// Pastes a copied subtree into a file and returns the file's new bytes.
    ///
    /// `attachToId` and `attachField` say where the pasted root hangs. Left out, the copy goes in
    /// unattached: it is in the file, it is numbered, and nothing reaches it, which is a state the
    /// checker already reports and a person can wire up on the canvas. That is offered rather than
    /// insisted on because the useful shape to paste is often not the one that has a slot free.
    public static Result Paste(string targetPath, Clip clip, int attachToId = -1,
                               string attachField = "", HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;

        var target = PackfileImage.Read(targetPath);
        var source = System.IO.Path.GetFullPath(targetPath) == System.IO.Path.GetFullPath(clip.Path)
                     ? target
                     : PackfileImage.Read(clip.Path);

        bool sameFile = ReferenceEquals(source, target);

        // Worked out again rather than trusting what the copy recorded. A file can be saved between a
        // copy and a paste, and a subtree worked out against the file as it was would name objects by
        // numbers that have since moved.
        var tree = Of(source, clip.Tree.RootId, types);

        var result = Into(target, source, tree, sameFile, attachToId, attachField, types);
        return result with { Bytes = target.Rebuild() };
    }

    /// The paste itself, on images rather than paths, so a test can do it without a file.
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

        // Everything a copied object points at that is not itself copied. Inside one file those keep
        // naming the original, which is the whole meaning of a shared object. Across two files there
        // is nothing to name.
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

        // Every copied object is appended before anything is written into any of them, so a pointer
        // from the first to the last has somewhere to aim by the time it is written.
        var made = new Dictionary<int, int>();
        var newIds = new List<int>();
        foreach (int id in tree.Ids)
        {
            var instance = sourceObjects.Instances[id - NativeGraphModel.FirstId];
            var added = NativeAppend.Object(target, instance.ClassName, types);
            made[instance.Offset] = added.Offset;
            newIds.Add(added.Id);
        }

        // Where a pointer out of the copy should aim. A copied object's copy, or, when the object was
        // shared, the original itself, which only exists in the same file.
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

    /// A copied object's runs: its strings and its arrays, and whatever hangs off those in turn.
    ///
    /// The bytes of the object were copied wholesale before this runs, so every count, every capacity
    /// word and every plain value is already right. What is not right is anything that was a pointer,
    /// because a pointer in this format is a fixup and not a number in the bytes, and the copy has no
    /// fixups of its own until they are written here.
    private static void CopyMembers(PackfileSection targetData, PackfileSection sourceData,
                                    IReadOnlyDictionary<int, int> sourceLocals,
                                    IReadOnlyDictionary<int, int> sourceGlobals,
                                    int targetSection, int from, int to, string className,
                                    Func<int, int?> aim, HavokClassTypes types, ref int pointers,
                                    int depth)
    {
        // Nothing in the corpus nests anywhere near this deep; the guard is against a class that
        // somehow holds itself rather than against real data.
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

    /// Rewrites every event and variable index inside the pasted objects to the number the file being
    /// pasted into uses for that name.
    ///
    /// Done over the file as it now stands rather than while the bytes are being copied, because the
    /// walk that finds an index has to go through the class table into inline structs and struct
    /// array elements, and doing it once afterwards means it is the same walk that renumbering and
    /// the symbols tab already use. The pasted objects are told apart from the originals by offset:
    /// the two hold the same numbers and only the offset says which is which.
    private static int Rewrite(PackfileImage target, PackfileSection data, HavokClassTypes types,
                               HashSet<int> pastedAt, IReadOnlyDictionary<int, int> events,
                               IReadOnlyDictionary<int, int> variables)
    {
        // Inside one file every name keeps the number it had, so there is nothing to rewrite and no
        // reason to walk the file again looking for nothing.
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

    /// Hangs the pasted root off a field of an object already in the file.
    ///
    /// Two shapes, and they are the two the format has: a field that holds one pointer, and a field
    /// that holds an array of them. An array gains an element rather than being written over, and its
    /// element fixups go back where the old ones sat rather than on the end of the table, because
    /// position in that table is not free and moving a run of them makes hkxpack read every element
    /// of the array as null.
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

        // A state carries a number that is unique inside its own machine, so a copy dropped into a
        // machine that already has states cannot keep the one it was copied with. Two states with the
        // same number is not a file that fails to load, it is a transition that arrives at whichever
        // of them the engine finds first.
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

    /// A symbol's number in the file it came from against its number in the file it is going to,
    /// matched by name.
    ///
    /// Refusing here is the honest answer rather than a failure. An event the other file does not
    /// declare cannot be invented without also writing its info and its flags, and a paste that
    /// quietly aimed at whatever event happened to sit at the same number would play the wrong thing
    /// and report success.
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

    /// The event or variable names a file declares, read out of its own bytes.
    private static List<string> Names(PackfileObjects objects, string field)
    {
        var strings = objects.OfClass("hkbBehaviorGraphStringData").FirstOrDefault();
        if (strings == null) return new List<string>();

        var names = objects.ReadStringArray(strings, field);
        return names == null ? new List<string>() : names.Select(n => n ?? "").ToList();
    }

    /// Every object's stretch of the section, so an offset can be asked which object it belongs to.
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
        return data.AppendData(text);
    }
}
