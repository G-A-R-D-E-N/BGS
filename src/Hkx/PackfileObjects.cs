using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenCommonwealth.Services.Hkx;

public sealed class PackfileObjects
{
    public sealed record Instance(int Offset, string ClassName)
    {
        public override string ToString() => $"0x{Offset:x} {ClassName}";
    }

    private readonly PackfileSection _data;
    private readonly PackfileSection _classNames;
    private readonly HavokClasses _classes;
    private readonly HavokClassTypes _types;
    private readonly List<Instance> _instances = new();

    private readonly Dictionary<int, int> _pointsAt = new();

    private readonly Dictionary<int, Instance> _startsAt = new();

    private readonly int _pointer;

    public IReadOnlyList<Instance> Instances => _instances;

    public PackfileObjects(PackfileImage image, HavokClasses? classes = null,
                           HavokClassTypes? types = null)
    {
        _classes = classes ?? HavokClasses.Shipped;
        _types = types ?? HavokClassTypes.Shipped;
        _pointer = image.Layout.PointerSize;
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

        int self = image.Sections.IndexOf(_data);
        foreach (var (source, section, destination) in _data.Globals())
            if (section == self) _pointsAt[source] = destination;
    }

    private string? NameAt(int at)
    {
        if (at < 0 || at >= _classNames.Data.Length) return null;

        int end = Array.IndexOf(_classNames.Data, (byte)0, at);
        if (end < 0) return null;
        return Encoding.ASCII.GetString(_classNames.Data, at, end - at);
    }

    public int IndexOf(Instance instance)
    {
        for (int i = 0; i < _instances.Count; i++)
            if (_instances[i].Offset == instance.Offset) return i;
        return -1;
    }

    public IEnumerable<Instance> OfClass(string className) =>
        _instances.Where(i => i.ClassName == className);

    public int? FieldAt(Instance instance, string field)
    {
        int? offset = MemberOffset(instance.ClassName, field);
        if (offset == null) return null;

        int at = instance.Offset + offset.Value;
        return at >= 0 && at < _data.Data.Length ? at : null;
    }

    private int? MemberOffset(string className, string field)
    {
        if (_pointer == 8)
            return _classes.Knows(className) ? _classes.Field(className, field)?.Offset : null;

        // The stored class table is 8-byte, so a 4-byte file's member offsets must come from the
        // pointer-width walker. Honor the schema the caller supplied, like the 8-byte path does.
        return LayoutWalker.Active(_types, className, _pointer)?.OffsetOf(field);
    }

    public float? ReadFloatAt(int at) =>
        at < 0 || at + 4 > _data.Data.Length ? null : BitConverter.ToSingle(_data.Data, at);

    public int? ReadIntAt(int at) =>
        at < 0 || at + 4 > _data.Data.Length ? null : BitConverter.ToInt32(_data.Data, at);

    public int? ReadNarrowAt(int at, int width)
    {
        if (at < 0 || width <= 0 || at + width > _data.Data.Length) return null;

        int value = 0;
        for (int b = 0; b < width; b++) value |= _data.Data[at + b] << (8 * b);
        return value;
    }

    public ulong? ReadULongAt(int at) =>
        at < 0 || at + 8 > _data.Data.Length ? null : BitConverter.ToUInt64(_data.Data, at);

    // The pointer width of the file being read. A TYPE_ULONG (hkUlong) is pointer-sized, so
    // callers must read it at this width, not always eight bytes.
    public int PointerWidth => _pointer;

    public ulong? ReadUnsignedAt(int at, int width)
    {
        if (at < 0 || width <= 0 || width > 8 || at + width > _data.Data.Length) return null;

        ulong value = 0;
        for (int b = 0; b < width; b++) value |= (ulong)_data.Data[at + b] << (8 * b);
        return value;
    }

    public long? ReadLongAt(int at) =>
        at < 0 || at + 8 > _data.Data.Length ? null : BitConverter.ToInt64(_data.Data, at);

    public float[]? ReadFloatsAt(int at, int count)
    {
        if (at < 0 || count < 0 || (long)at + 4L * count > _data.Data.Length) return null;

        var values = new float[count];
        for (int i = 0; i < count; i++) values[i] = BitConverter.ToSingle(_data.Data, at + i * 4);
        return values;
    }

    public string? ReadStringAt(int at) => TextAt(Aim(at));

    public Instance? ReadRefAt(int at, out bool wasNull)
    {
        wasNull = false;

        int? destination = Aim(at);
        if (destination == null) { wasNull = true; return null; }

        return _startsAt.TryGetValue(destination.Value, out var target) ? target : null;
    }

    public IReadOnlyList<string?>? ReadStringArrayAt(int at)
    {
        var array = ArrayAt(at, _pointer);
        if (array == null) return null;

        var values = new List<string?>(array.Count);
        for (int i = 0; i < array.Count; i++)
        {
            int slot = array.At + i * _pointer;
            if (slot + _pointer > _data.Data.Length) return null;
            values.Add(TextAt(Aim(slot)));
        }
        return values;
    }

    public IReadOnlyList<Instance?>? ReadRefArrayAt(int at)
    {
        var array = ArrayAt(at, _pointer);
        if (array == null) return null;

        var values = new List<Instance?>(array.Count);
        for (int i = 0; i < array.Count; i++)
        {
            int slot = array.At + i * _pointer;
            if (slot + _pointer > _data.Data.Length) return null;

            int? destination = Aim(slot);
            values.Add(destination != null && _startsAt.TryGetValue(destination.Value, out var target)
                           ? target
                           : null);
        }
        return values;
    }

    public IReadOnlyList<T>? ReadValueArrayAt<T>(int at, int width, Func<byte[], int, T> read)
    {
        if (width <= 0) return null;
        var array = ArrayAt(at, width);
        if (array == null) return null;

        var values = new List<T>(array.Count);
        for (int i = 0; i < array.Count; i++) values.Add(read(_data.Data, array.At + i * width));
        return values;
    }

    public float? ReadFloat(Instance instance, string field) =>
        FieldAt(instance, field) is { } at ? ReadFloatAt(at) : null;

    public int? ReadInt(Instance instance, string field) =>
        FieldAt(instance, field) is { } at ? ReadIntAt(at) : null;

    public ulong? ReadULong(Instance instance, string field) =>
        FieldAt(instance, field) is { } at ? ReadUnsignedAt(at, _pointer) : null;

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

    private int? Aim(int at) => _pointsAt.TryGetValue(at, out int destination) ? destination : null;

    public sealed record Elements(int At, int Count);

    public Elements? ReadArray(Instance instance, string field)
    {
        int? at = FieldAt(instance, field);
        return at == null ? null : ArrayAt(at.Value);
    }

    public Elements? ArrayAt(int at)
    {
        if (at < 0 || at + _pointer + 4 > _data.Data.Length) return null;

        int count = BitConverter.ToInt32(_data.Data, at + _pointer);
        if (count < 0) return null;

        int? destination = Aim(at);
        if (destination == null) return count == 0 ? new Elements(0, 0) : null;
        if (destination.Value < 0 || destination.Value > _data.Data.Length) return null;
        if (count > _data.Data.Length - destination.Value) return null;

        return new Elements(destination.Value, count);
    }

    public Elements? ArrayAt(int at, int elementWidth)
    {
        if (elementWidth <= 0) return null;
        var array = ArrayAt(at);
        if (array == null || array.Count == 0) return array;

        long bytes = (long)array.Count * elementWidth;
        if (bytes > _data.Data.Length - array.At) return null;
        return array;
    }

    public int RunToNull(int at)
    {
        if (at < 0 || at >= _data.Data.Length) return 0;
        int end = Array.IndexOf(_data.Data, (byte)0, at);
        return end < 0 ? _data.Data.Length - at : end - at + 1;
    }

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

    public bool WriteString(Instance instance, string field, string value)
    {
        int? at = FieldAt(instance, field);
        if (at == null || at + _pointer > _data.Data.Length) return false;

        if (ReadString(instance, field) == value) return true;

        var bytes = Encoding.UTF8.GetBytes(value);
        var withTerminator = new byte[bytes.Length + 1];
        bytes.CopyTo(withTerminator, 0);

        int landed = _data.AppendAligned(withTerminator, PackfileSection.StringAlignment);
        _data.SetLocal(at.Value, landed);

        _pointsAt[at.Value] = landed;
        return true;
    }

    public (int Known, int Unknown) Coverage()
    {
        int known = _instances.Count(i => _classes.Knows(i.ClassName));
        return (known, _instances.Count - known);
    }

    public IEnumerable<string> UnknownClasses() =>
        _instances.Select(i => i.ClassName).Where(n => !_classes.Knows(n)).Distinct().OrderBy(n => n);
}
