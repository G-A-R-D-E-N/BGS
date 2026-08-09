using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenCommonwealth.Services.Hkx;

// The objects inside a packfile's __data__ section, and their fields.
//
// A packfile does not list its objects anywhere. What it has is a virtual fixup per object, saying
// "at this offset sits an instance of the class whose name is at that offset in __classnames__", so
// the object list is that table read in order. Combined with the field layouts in HavokClasses,
// which say where each field sits inside an instance, that is enough to read and change a field
// without going out through XML and back.
//
// What this deliberately does not do is move anything. Every offset in the file is derived from the
// sizes of what precedes it, so changing an object's size means rebuilding every fixup that points
// past it. Writing a value over another value of the same width does not, which is why that is the
// operation offered here and resizing is not.
public sealed class PackfileObjects
{
    public sealed record Instance(int Offset, string ClassName)
    {
        public override string ToString() => $"0x{Offset:x} {ClassName}";
    }

    private readonly PackfileSection _data;
    private readonly PackfileSection _classNames;
    private readonly HavokClasses _classes;
    private readonly List<Instance> _instances = new();

    /// Where each pointer in the section aims, by the offset of the pointer itself. Built once
    /// rather than scanned per field: reading one string by walking the table is nothing, and
    /// reading every field of every object that way is 1,587 entries times 5,000 fields.
    private readonly Dictionary<int, int> _pointsAt = new();

    /// Which object starts at an offset, so a pointer can be resolved to the thing it names.
    private readonly Dictionary<int, Instance> _startsAt = new();

    public IReadOnlyList<Instance> Instances => _instances;

    public PackfileObjects(PackfileImage image, HavokClasses? classes = null)
    {
        _classes = classes ?? HavokClasses.Shipped;
        _data = image.Section("__data__")
                ?? throw new InvalidOperationException("The file has no __data__ section.");
        _classNames = image.Section("__classnames__")
                      ?? throw new InvalidOperationException("The file has no __classnames__ section.");

        foreach (var (source, _, nameAt) in _data.Virtuals())
        {
            string? name = NameAt(nameAt);
            if (name != null) _instances.Add(new Instance(source, name));
        }

        foreach (var instance in _instances) _startsAt[instance.Offset] = instance;
        foreach (var (source, destination) in _data.Locals()) _pointsAt[source] = destination;

        // A pointer from one object to another is a *global* fixup, not a local one, because the
        // format allows it to cross into another section even when nothing in these files does.
        // Reading only the local table finds every string and every array and no object reference
        // at all, which reads as a file where nothing points at anything.
        int self = image.Sections.IndexOf(_data);
        foreach (var (source, section, destination) in _data.Globals())
            if (section == self) _pointsAt[source] = destination;
    }

    /// A class name lives in __classnames__ preceded by five bytes of bookkeeping, and the fixup
    /// points at the name itself, so this reads forward from there to the terminator.
    private string? NameAt(int at)
    {
        if (at < 0 || at >= _classNames.Data.Length) return null;

        int end = Array.IndexOf(_classNames.Data, (byte)0, at);
        if (end < 0) return null;
        return Encoding.ASCII.GetString(_classNames.Data, at, end - at);
    }

    /// Where an object sits in the list, which is how anything outside here names one. The list is
    /// in the order the virtual fixups give, which is the order the file stores them in.
    public int IndexOf(Instance instance)
    {
        for (int i = 0; i < _instances.Count; i++)
            if (_instances[i].Offset == instance.Offset) return i;
        return -1;
    }

    public IEnumerable<Instance> OfClass(string className) =>
        _instances.Where(i => i.ClassName == className);

    /// Where a named field of a given instance sits in the section's bytes, or null when the class
    /// is unknown or has no such field. Never guesses: an unknown class is reported as unknown
    /// rather than treated as having no fields.
    public int? FieldAt(Instance instance, string field)
    {
        if (!_classes.Knows(instance.ClassName)) return null;

        var member = _classes.Field(instance.ClassName, field);
        if (member == null) return null;

        int at = instance.Offset + member.Offset;
        return at >= 0 && at < _data.Data.Length ? at : null;
    }

    /// The reads, by offset.
    ///
    /// Everything below comes in two shapes: a named field of an object, and a plain offset. The
    /// offset is the real one. A struct written inside an object sits at no offset that object's
    /// class describes, so anything that walks into one has an address and not a name, and the
    /// named form is that same read with the address worked out first.
    public float? ReadFloatAt(int at) =>
        at < 0 || at + 4 > _data.Data.Length ? null : BitConverter.ToSingle(_data.Data, at);

    public int? ReadIntAt(int at) =>
        at < 0 || at + 4 > _data.Data.Length ? null : BitConverter.ToInt32(_data.Data, at);

    /// A field narrower than four bytes, read at its own width.
    ///
    /// Reading a two byte value as four and masking works everywhere except the last two bytes of a
    /// section, where the four byte read runs off the end and the value comes back as nothing at
    /// all. Nothing in a vanilla file sits there, so this never showed until an array of numbers was
    /// lengthened and its new run was appended to the end: the last element of it read as blank
    /// while the count beside it said otherwise.
    public int? ReadNarrowAt(int at, int width)
    {
        if (at < 0 || width <= 0 || at + width > _data.Data.Length) return null;

        int value = 0;
        for (int b = 0; b < width; b++) value |= _data.Data[at + b] << (8 * b);
        return value;
    }

    /// Eight bytes rather than four. `hkbNode.userData` is the common one, and reading it as an int
    /// would come back right only while the top half happens to be zero.
    public ulong? ReadULongAt(int at) =>
        at < 0 || at + 8 > _data.Data.Length ? null : BitConverter.ToUInt64(_data.Data, at);

    /// A run of floats laid out one after another: four of them for a vector or a quaternion, twelve
    /// for a transform. Returns null rather than a short answer when the object does not reach that
    /// far, because a half read transform is worse than no transform.
    public float[]? ReadFloatsAt(int at, int count)
    {
        if (at < 0 || count < 0 || at + 4 * count > _data.Data.Length) return null;

        var values = new float[count];
        for (int i = 0; i < count; i++) values[i] = BitConverter.ToSingle(_data.Data, at + i * 4);
        return values;
    }

    /// A string field holds a pointer, not characters, so the value is wherever the fixup for that
    /// exact offset points. No fixup means the pointer is null, which is a real state and not a
    /// failure.
    public string? ReadStringAt(int at) => TextAt(Aim(at));

    /// The object a reference field names, or null when the field is null. A pointer that lands
    /// somewhere no object begins is reported as unresolved rather than as the nearest object,
    /// because the nearest object is a guess and this is meant to be a reading.
    public Instance? ReadRefAt(int at, out bool wasNull)
    {
        wasNull = false;

        int? destination = Aim(at);
        if (destination == null) { wasNull = true; return null; }

        return _startsAt.TryGetValue(destination.Value, out var target) ? target : null;
    }

    public IReadOnlyList<string?>? ReadStringArrayAt(int at)
    {
        var array = ArrayAt(at);
        if (array == null) return null;

        var values = new List<string?>(array.Count);
        for (int i = 0; i < array.Count; i++)
        {
            int slot = array.At + i * 8;
            if (slot + 8 > _data.Data.Length) return null;
            values.Add(TextAt(Aim(slot)));
        }
        return values;
    }

    public IReadOnlyList<Instance?>? ReadRefArrayAt(int at)
    {
        var array = ArrayAt(at);
        if (array == null) return null;

        var values = new List<Instance?>(array.Count);
        for (int i = 0; i < array.Count; i++)
        {
            int slot = array.At + i * 8;
            if (slot + 8 > _data.Data.Length) return null;

            int? destination = Aim(slot);
            values.Add(destination != null && _startsAt.TryGetValue(destination.Value, out var target)
                           ? target
                           : null);
        }
        return values;
    }

    /// Elements that are numbers rather than pointers, laid out one after another. `width` is how
    /// many bytes each takes and `read` turns those bytes into the value.
    public IReadOnlyList<T>? ReadValueArrayAt<T>(int at, int width, Func<byte[], int, T> read)
    {
        var array = ArrayAt(at);
        if (array == null || array.At + array.Count * width > _data.Data.Length) return null;

        var values = new List<T>(array.Count);
        for (int i = 0; i < array.Count; i++) values.Add(read(_data.Data, array.At + i * width));
        return values;
    }

    public float? ReadFloat(Instance instance, string field) =>
        FieldAt(instance, field) is { } at ? ReadFloatAt(at) : null;

    public int? ReadInt(Instance instance, string field) =>
        FieldAt(instance, field) is { } at ? ReadIntAt(at) : null;

    public ulong? ReadULong(Instance instance, string field) =>
        FieldAt(instance, field) is { } at ? ReadULongAt(at) : null;

    public float[]? ReadFloats(Instance instance, string field, int count) =>
        FieldAt(instance, field) is { } at ? ReadFloatsAt(at, count) : null;

    public string? ReadString(Instance instance, string field) =>
        FieldAt(instance, field) is { } at ? ReadStringAt(at) : null;

    public Instance? ReadRef(Instance instance, string field, out bool wasNull)
    {
        wasNull = false;
        return FieldAt(instance, field) is { } at ? ReadRefAt(at, out wasNull) : null;
    }

    private string? TextAt(int? destination)
    {
        if (destination == null || destination < 0 || destination >= _data.Data.Length) return null;

        int end = Array.IndexOf(_data.Data, (byte)0, destination.Value);
        return end < 0 ? null : Encoding.UTF8.GetString(_data.Data, destination.Value, end - destination.Value);
    }

    /// Where the pointer stored at an offset aims, or null when nothing points from there, which is
    /// how the format spells a null pointer: the eight bytes hold zero and no fixup names them.
    private int? Aim(int at) => _pointsAt.TryGetValue(at, out int destination) ? destination : null;

    /// An hkArray is a pointer, a count, and a capacity with flags packed into its top bits. The
    /// pointer is a fixup like any other, and an empty array has none, which is why a missing fixup
    /// here means no elements rather than a fault.
    public sealed record Elements(int At, int Count);

    public Elements? ReadArray(Instance instance, string field)
    {
        int? at = FieldAt(instance, field);
        return at == null ? null : ArrayAt(at.Value);
    }

    /// The same, by offset rather than by name. A struct written inside an object is not at any
    /// offset that object's class describes, so reading one means working out where it sits and
    /// asking from there.
    public Elements? ArrayAt(int at)
    {
        if (at < 0 || at + 12 > _data.Data.Length) return null;

        int count = BitConverter.ToInt32(_data.Data, at + 8);
        if (count < 0) return null;

        int? destination = Aim(at);
        if (destination == null) return count == 0 ? new Elements(0, 0) : null;

        return new Elements(destination.Value, count);
    }

    /// Every class the file names, with the signature it stores in front of the name. A class name
    /// in `__classnames__` is four bytes of signature, one separator, the text, and a terminator.
    public IEnumerable<(uint Signature, string Name)> ClassNames()
    {
        var blob = _classNames.Data;
        for (int at = 0; at + 5 < blob.Length; )
        {
            int end = Array.IndexOf(blob, (byte)0, at + 5);
            if (end < 0) yield break;

            yield return (BitConverter.ToUInt32(blob, at), Encoding.ASCII.GetString(blob, at + 5, end - at - 5));
            at = end + 1;
        }
    }

    public IReadOnlyList<string?>? ReadStringArray(Instance instance, string field) =>
        FieldAt(instance, field) is { } at ? ReadStringArrayAt(at) : null;

    public IReadOnlyList<Instance?>? ReadRefArray(Instance instance, string field) =>
        FieldAt(instance, field) is { } at ? ReadRefArrayAt(at) : null;

    public IReadOnlyList<T>? ReadValueArray<T>(Instance instance, string field, int width,
                                               Func<byte[], int, T> read) =>
        FieldAt(instance, field) is { } at ? ReadValueArrayAt(at, width, read) : null;

    /// Overwrites a field in place. Same width in, same width out, so nothing moves and every offset
    /// in the file stays valid. Returns false when the field is not one we can place, rather than
    /// writing somewhere approximate.
    public bool WriteFloat(Instance instance, string field, float value)
    {
        int? at = FieldAt(instance, field);
        if (at == null || at + 4 > _data.Data.Length) return false;

        BitConverter.GetBytes(value).CopyTo(_data.Data, at.Value);
        return true;
    }

    public bool WriteInt(Instance instance, string field, int value)
    {
        int? at = FieldAt(instance, field);
        if (at == null || at + 4 > _data.Data.Length) return false;

        BitConverter.GetBytes(value).CopyTo(_data.Data, at.Value);
        return true;
    }

    /// Changes a string, at whatever length it wants to be.
    ///
    /// The text goes on the end of the section and the field's fixup is pointed at it, so no byte
    /// that anything else refers to moves and no offset recorded anywhere becomes wrong. A field
    /// that held no pointer at all gains one, which is how a name the file left empty gets a value.
    /// The bytes the pointer used to name stay where they are and stop being referenced.
    public bool WriteString(Instance instance, string field, string value)
    {
        int? at = FieldAt(instance, field);
        if (at == null || at + 8 > _data.Data.Length) return false;

        if (ReadString(instance, field) == value) return true;

        var bytes = Encoding.UTF8.GetBytes(value);
        var withTerminator = new byte[bytes.Length + 1];
        bytes.CopyTo(withTerminator, 0);

        int landed = _data.AppendAligned(withTerminator, PackfileSection.StringAlignment);
        _data.SetLocal(at.Value, landed);
        // The lookup is a copy of the table, so it has to be updated too or a later read still
        // finds the old text.
        _pointsAt[at.Value] = landed;
        return true;
    }

    /// How much of this file we can account for. An unknown class is not fatal, but it is the number
    /// worth watching: it says how much of a file an edit could not reason about.
    public (int Known, int Unknown) Coverage()
    {
        int known = _instances.Count(i => _classes.Knows(i.ClassName));
        return (known, _instances.Count - known);
    }

    public IEnumerable<string> UnknownClasses() =>
        _instances.Select(i => i.ClassName).Where(n => !_classes.Knows(n)).Distinct().OrderBy(n => n);
}
