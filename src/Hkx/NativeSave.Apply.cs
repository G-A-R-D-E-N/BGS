using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;
using System.Xml.Linq;
using OpenCommonwealth.Services;

namespace OpenCommonwealth.Services.Hkx;

public static partial class NativeSave
{
    private static PackfileObjects.Instance Resolve(PackfileObjects objects, Change change)
    {
        if (change.Id <= 0)
            throw new InvalidOperationException(
                $"{change} was planned without a stable object id, so nothing was written.");

        int at = change.Id - NativeGraphModel.FirstId;
        if (at < 0 || at >= objects.Instances.Count ||
            objects.Instances[at].ClassName != change.ClassName)
            throw new InvalidOperationException(
                $"{change} does not correspond to anything in the file, so nothing was written.");

        return objects.Instances[at];
    }

    public static byte[] Apply(string hkxPath, Plan plan, HavokClasses? classes = null)
        => Apply(InputFilePolicy.ReadHkx(hkxPath), plan, classes);

    public static byte[] Apply(byte[] source, Plan plan, HavokClasses? classes = null)
    {
        if (!plan.Possible)
            throw new InvalidOperationException("This edit cannot be written in place: " + plan.Refusal);

        var image = PackfileImage.Read(source);

        NativeLayout.RequireWritable(image);

        var objects = new PackfileObjects(image, classes);

        var mismatched = HavokClassTypes.Shipped.SignatureProblems(objects.ClassNames());
        if (mismatched.Count > 0)
            throw new InvalidOperationException(
                "This file's classes are not the ones this build describes, so nothing was written " +
                $"into its bytes: {mismatched[0]}" +
                (mismatched.Count > 1 ? $", and {mismatched.Count - 1} more like it." : "."));

        RequireUnaliasedArrays(image, objects, plan);

        int adding = 0;
        foreach (var add in plan.Changes.Where(c => c.Added))
        {
            var data = image.Section("__data__")
                ?? throw new InvalidOperationException("this file has no data section");

            var layout = HavokClassTypes.Shipped[add.ClassName];
            if (layout?.Size is not int size || size <= 0)
                throw new InvalidOperationException(
                    $"No size for {add.ClassName}, so no object of it was added.");

            var names = image.Section("__classnames__")
                ?? throw new InvalidOperationException("this file has no class name section");

            int nameAt = NativeAppend.NameOffset(names, add.ClassName, layout.Signature);

            string expected = "#" + (NativeGraphModel.FirstId + objects.Instances.Count + adding);
            if (add.Value != expected)
                throw new InvalidOperationException(
                    $"The new {add.ClassName} is {add.Value} in the document and would be {expected} " +
                    "in the file, so nothing was written.");

            data.AddVirtual(data.AppendObject(new byte[size]), image.Sections.IndexOf(names), nameAt);
            adding++;
        }

        if (adding > 0) objects = new PackfileObjects(image, classes);

        bool grew = false;
        foreach (var change in plan.Changes.Where(c => c.Grow))
        {
            var instance = Resolve(objects, change);
            Regrow(image, objects, instance, change);
            grew = true;
        }

        if (grew)
        {
            objects = new PackfileObjects(image, classes);
        }

        var targets = new List<(Change Change, PackfileObjects.Instance Instance)>();
        foreach (var change in plan.Changes.Where(c => !c.Added))
            targets.Add((change, Resolve(objects, change)));

        foreach (var (change, instance) in targets)
        {
            if (change.Grow) continue;

            if (change.InElement)
            {
                if (!WriteInElement(image, objects, instance, change))
                    throw new InvalidOperationException($"{change} could not be written, so nothing was.");
                continue;
            }

            var member = (classes ?? HavokClasses.Shipped).Field(change.ClassName, change.Field)
                ?? throw new InvalidOperationException($"No layout for {change.ClassName}.{change.Field}.");

            bool written = change.Ref ? Repoint(image, objects, instance, change)
                         : change.Array && change.Text ? ResizeText(image, objects, instance, change)
                         : change.Array && ValueElement(member.Type) > 0
                             ? ResizeValues(image, objects, instance, change, member.Type)
                         : change.Array ? Resize(image, objects, instance, change)
                         : WideFloats(member.Type) > 0
                             ? WriteWide(objects, instance, change, WideFloats(member.Type), image)
                         : IsWideInteger(member.Type)
                             ? WriteWideInteger(objects, instance, change, member.Type, image)
                         : member.Type switch
            {
                "real" => objects.WriteFloat(instance, change.Field, AsFloat(change.Value)),
                _ when WritableText.Contains(member.Type) =>
                    objects.WriteString(instance, change.Field, change.Value),
                _ => WriteNarrow(objects, instance, change.Field, member.Type, change.Value,
                                 image.Section("__data__")!),
            };

            if (!written)
                throw new InvalidOperationException($"{change} could not be written, so nothing was.");
        }

        FixupOrder.Reorder(image);

        if (plan.Gone.Count > 0) NativeRemove.Delete(image, plan.Gone);

        return image.Rebuild();
    }

    private static bool Repoint(PackfileImage image, PackfileObjects objects,
                                PackfileObjects.Instance instance, Change change)
    {
        var data = image.Section("__data__")
            ?? throw new InvalidOperationException("this file has no data section");

        if (objects.FieldAt(instance, change.Field) is not int at)
            throw new InvalidOperationException(
                $"No offset for {change.ClassName}.{change.Field}, so nothing was written.");

        if (change.Value == "null")
        {
            data.SetGlobal(at, 0, -1);
            return true;
        }

        return RepointAt(image, objects, data, at, change.Value);
    }

    private static bool RepointAt(PackfileImage image, PackfileObjects objects,
                                  PackfileSection data, int at, string value)
    {
        if (value == "null")
        {
            data.SetGlobal(at, 0, -1);
            return true;
        }

        int index = int.Parse(value[1..], CultureInfo.InvariantCulture) - NativeGraphModel.FirstId;
        if (index < 0 || index >= objects.Instances.Count)
            throw new InvalidOperationException(
                $"#{value[1..]} is not an object this file has, so nothing was written.");

        data.SetGlobal(at, image.Sections.IndexOf(data), objects.Instances[index].Offset);
        return true;
    }

    private static bool Resize(PackfileImage image, PackfileObjects objects,
                               PackfileObjects.Instance instance, Change change)
    {
        var data = image.Section("__data__")
            ?? throw new InvalidOperationException("this file has no data section");

        if (objects.FieldAt(instance, change.Field) is not int at)
            throw new InvalidOperationException(
                $"No offset for {change.ClassName}.{change.Field}, so nothing was written.");

        var elements = change.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int section = image.Sections.IndexOf(data);

        int run = elements.Length == 0 ? -1 : data.AppendData(new byte[elements.Length * 8]);
        data.SetLocal(at, run);

        var old = objects.ArrayAt(at);
        var entries = data.Globals().ToList();
        int first = entries.Count;

        if (old != null && old.Count > 0)
        {
            int from = old.At, to = old.At + old.Count * 8;
            int found = entries.FindIndex(e => e.Source >= from && e.Source < to);
            if (found >= 0) first = found;
            entries.RemoveAll(e => e.Source >= from && e.Source < to);
        }

        var replacements = new List<(int, int, int)>();
        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i] == "null") continue;

            int index = int.Parse(elements[i][1..]) - NativeGraphModel.FirstId;
            if (index < 0 || index >= objects.Instances.Count)
                throw new InvalidOperationException(
                    $"{change} names an object this file does not have, so nothing was written.");

            replacements.Add((run + i * 8, section, objects.Instances[index].Offset));
        }

        entries.InsertRange(Math.Min(first, entries.Count), replacements);
        data.SetGlobals(entries);

        WriteArrayCount(data, at, elements.Length);

        return true;
    }

    private static bool WriteWide(PackfileObjects objects, PackfileObjects.Instance instance,
                                  Change change, int floats, PackfileImage image)
    {
        var data = image.Section("__data__");
        if (data == null) return false;

        if (objects.FieldAt(instance, change.Field) is not int at) return false;
        if (Bracketed(change.Value, floats) is not float[] values) return false;
        if (at < 0 || at + floats * 4 > data.Data.Length) return false;

        for (int i = 0; i < floats; i++)
            BitConverter.GetBytes(values[i]).CopyTo(data.Data, at + i * 4);

        return true;
    }

    private static bool WriteWideInteger(PackfileObjects objects, PackfileObjects.Instance instance,
                                         Change change, string type, PackfileImage image)
    {
        var data = image.Section("__data__");
        if (data == null) return false;

        if (objects.FieldAt(instance, change.Field) is not int at) return false;
        if (at < 0 || at + 8 > data.Data.Length) return false;

        try { NumberCodecs.WriteScalar(data.Data, at, type, change.Value); }
        catch (InvalidOperationException) { return false; }
        return true;
    }

    private static bool ResizeValues(PackfileImage image, PackfileObjects objects,
                                     PackfileObjects.Instance instance, Change change, string type)
    {
        var data = image.Section("__data__");
        if (data == null) return false;

        if (objects.FieldAt(instance, change.Field) is not int at) return false;

        int width = ValueElement(type);
        if (width <= 0) return false;
        if (Numbers(change.Value, type, width) is not byte[] run) return false;

        int count = run.Length / width;

        if (count == 0)
        {
            data.SetLocal(at, -1);
            WriteArrayCount(data, at, 0);
            return true;
        }

        data.AlignData(NativeAppend.Alignment);
        data.SetLocal(at, data.AppendData(run));

        WriteArrayCount(data, at, count);
        return true;
    }

    private static bool ResizeText(PackfileImage image, PackfileObjects objects,
                                   PackfileObjects.Instance instance, Change change)
    {
        var data = image.Section("__data__");
        if (data == null) return false;

        if (objects.FieldAt(instance, change.Field) is not int at) return false;

        var names = change.Value.Length == 0
                    ? new List<string>()
                    : change.Value.Split(TextSeparator).ToList();

        var old = objects.ArrayAt(at);
        if (old != null && old.Count > 0)
        {
            var keep = data.Locals()
                           .Where(l => l.Source < old.At || l.Source >= old.At + old.Count * 8)
                           .ToList();
            data.SetLocals(keep);
        }

        if (names.Count == 0)
        {
            data.SetLocal(at, -1);
            WriteArrayCount(data, at, 0);
            return true;
        }

        var wrote = new List<int>(names.Count);
        foreach (string name in names)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            var withEnd = new byte[bytes.Length + 1];
            bytes.CopyTo(withEnd, 0);
            wrote.Add(data.AppendAligned(withEnd, PackfileSection.StringAlignment));
        }

        data.AlignData(NativeAppend.Alignment);
        int run = data.AppendData(new byte[names.Count * 8]);

        data.SetLocal(at, run);
        for (int e = 0; e < names.Count; e++) data.SetLocal(run + e * 8, wrote[e]);

        WriteArrayCount(data, at, names.Count);
        return true;
    }

    private static void Regrow(PackfileImage image, PackfileObjects objects,
                               PackfileObjects.Instance instance, Change change)
    {
        var data = image.Section("__data__")
            ?? throw new InvalidOperationException("this file has no data section");

        if (objects.FieldAt(instance, change.Field) is not int at)
            throw new InvalidOperationException(
                $"No offset for {change.ClassName}.{change.Field}, so nothing was written.");

        string elementClass = ElementClass(change.ClassName, change.Field)
            ?? throw new InvalidOperationException(
                $"{change.ClassName}.{change.Field} does not say what class its elements are.");

        int stride = HavokClassTypes.Shipped[elementClass]?.Size ?? 0;
        if (stride <= 0)
            throw new InvalidOperationException(
                $"No size for {elementClass}, so {change.Field} was not resized.");

        int count = int.Parse(change.Value, CultureInfo.InvariantCulture);
        var old = objects.ArrayAt(at);
        int wasAt = old?.At ?? 0, had = old?.Count ?? 0;
        int carried = Math.Min(had, count);

        int run = -1;
        if (count > 0)
        {

            data.AlignData(16);
            run = data.AppendData(new byte[count * stride]);
            if (carried > 0) Array.Copy(data.Data, wasAt, data.Data, run, carried * stride);
        }

        data.SetLocal(at, run);
        Move(data.Locals().ToList(), data.SetLocals, l => l.Source, (l, s) => (s, l.Destination));
        Move(data.Globals().ToList(), data.SetGlobals, g => g.Source, (g, s) => (s, g.Section, g.Destination));

        WriteArrayCount(data, at, count);

        void Move<T>(List<T> entries, Action<IEnumerable<T>> write, Func<T, int> sourceOf,
                     Func<T, int, T> moved)
        {
            if (had == 0) return;

            var kept = new List<T>();
            foreach (var entry in entries)
            {
                int source = sourceOf(entry);
                if (source < wasAt || source >= wasAt + had * stride) { kept.Add(entry); continue; }
                if (source >= wasAt + carried * stride) continue;

                kept.Add(moved(entry, source - wasAt + run));
            }
            write(kept);
        }
    }

}
