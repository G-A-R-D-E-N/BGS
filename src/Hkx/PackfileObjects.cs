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

    public float? ReadFloat(Instance instance, string field)
    {
        int? at = FieldAt(instance, field);
        return at == null || at + 4 > _data.Data.Length
            ? null
            : BitConverter.ToSingle(_data.Data, at.Value);
    }

    public int? ReadInt(Instance instance, string field)
    {
        int? at = FieldAt(instance, field);
        return at == null || at + 4 > _data.Data.Length
            ? null
            : BitConverter.ToInt32(_data.Data, at.Value);
    }

    /// A string field holds a pointer, not characters, so the value is wherever this object's local
    /// fixup for that exact offset points. No fixup means the pointer is null, which is a real state
    /// and not a failure.
    public string? ReadString(Instance instance, string field)
    {
        int? at = FieldAt(instance, field);
        if (at == null) return null;

        foreach (var (source, destination) in _data.Locals())
        {
            if (source != at.Value) continue;
            int end = Array.IndexOf(_data.Data, (byte)0, destination);
            if (end < 0) return null;
            return Encoding.UTF8.GetString(_data.Data, destination, end - destination);
        }
        return null;
    }

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
