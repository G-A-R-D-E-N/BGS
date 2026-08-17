using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Hkx;

public readonly struct PointerLayout
{
    public int PointerSize { get; }

    public PointerLayout(int pointerSize) => PointerSize = pointerSize;

    public static PointerLayout EightByte => new(8);
    public static PointerLayout FourByte => new(4);
}

public sealed class ObjectLayout
{
    private readonly IReadOnlyDictionary<string, int> _byName;

    public IReadOnlyList<int> Offsets { get; }
    public int Size { get; }
    public int Alignment { get; }

    public ObjectLayout(IReadOnlyList<int> offsets, IReadOnlyDictionary<string, int> byName,
                        int size, int alignment)
    {
        Offsets = offsets;
        _byName = byName;
        Size = size;
        Alignment = alignment;
    }

    public int? OffsetOf(string member) => _byName.TryGetValue(member, out int at) ? at : null;
}

public static class LayoutWalker
{
    // Layout results depend on the class table that produced them: two HavokClassTypes
    // instances can define the same class name differently. Scope every cache to the
    // specific schema instance rather than sharing it process-wide keyed by name only.
    private sealed class SchemaCache
    {
        public readonly Dictionary<(int Pointer, string Class), ObjectLayout> Layouts = new();
        public readonly Dictionary<string, int> ExtraAlign = new(StringComparer.Ordinal);
        public readonly Dictionary<string, bool> Reproduces = new(StringComparer.Ordinal);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HavokClassTypes, SchemaCache> Caches = new();

    private static SchemaCache CacheFor(HavokClassTypes types) =>
        Caches.GetValue(types, _ => new SchemaCache());

    public static ObjectLayout Of(HavokClassTypes types, string className, PointerLayout layout)
    {
        var cache = CacheFor(types);
        var key = (layout.PointerSize, className);
        if (cache.Layouts.TryGetValue(key, out var hit)) return hit;

        int extra = ExtraAlignment(types, className);
        var result = Lay(types, className, layout, extra);
        cache.Layouts[key] = result;
        return result;
    }

    public static bool CanPlace(HavokClassTypes types, string className)
    {
        var cache = CacheFor(types);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? at = className; at != null; at = types[at]?.Parent)
        {
            if (!seen.Add(at)) return false;
            if (!types.Knows(at)) return false;
            ExtraAlignment(types, at);
            if (cache.Reproduces.TryGetValue(at, out bool ok) && !ok) return false;
        }
        return true;
    }

    // The offsets and size that actually apply to a class in a file of the given pointer
    // width. The stored class table is laid out for 8-byte pointers, so an 8-byte file reads
    // the stored offsets directly; a 4-byte file has to re-derive them, because the stored
    // member.Offset/.Size values describe 64-bit structures. Returns null when the layout
    // cannot be derived (unknown class, or one the walker cannot reproduce), so walkers can
    // refuse rather than interpret the bytes with the wrong offsets.
    public static ObjectLayout? Active(HavokClassTypes types, string className, int pointerSize)
    {
        if (pointerSize != 8)
        {
            if (pointerSize != 4 || !CanPlace(types, className)) return null;
            return Of(types, className, new PointerLayout(pointerSize));
        }

        if (!types.Knows(className)) return null;

        var members = types.Members(className);
        var offsets = new int[members.Count];
        var byName = new Dictionary<string, int>(members.Count, StringComparer.Ordinal);
        for (int i = 0; i < members.Count; i++)
        {
            offsets[i] = members[i].Offset;
            byName[members[i].Name] = members[i].Offset;
        }

        return new ObjectLayout(offsets, byName, types[className]?.Size ?? 0, 0);
    }

    private static ObjectLayout Lay(HavokClassTypes types, string className, PointerLayout layout,
                                    int extraAlign)
    {
        var offsets = new List<int>();
        int cursor;
        bool rooted;

        string? parentName = types[className]?.Parent;
        if (parentName != null && types.Knows(parentName))
        {
            var parent = Of(types, parentName, layout);
            offsets.AddRange(parent.Offsets);
            cursor = parent.Size;
            rooted = true;
        }
        else
        {
            bool isRoot = className == "hkBaseObject";
            cursor = isRoot ? layout.PointerSize : 0;
            rooted = isRoot;
        }

        var declared = types[className]?.Declared ?? Array.Empty<HavokClassTypes.Member>();

        int classAlign = types[className]?.Align ?? 0;
        cursor = Align(cursor, classAlign);

        foreach (var member in declared)
        {
            cursor = Align(cursor, AlignOf(types, member, layout));
            offsets.Add(cursor);
            cursor += WidthOf(types, member, layout) * Math.Max(1, member.ArrSize);
        }

        int align = Math.Max(Math.Max(NaturalAlign(types, className, layout), extraAlign), classAlign);
        int size = Align(cursor, align);
        if (!rooted && declared.Count == 0 && cursor == 0) size = 1;

        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        var flat = types.Members(className);
        for (int i = 0; i < flat.Count && i < offsets.Count; i++) byName[flat[i].Name] = offsets[i];

        return new ObjectLayout(offsets, byName, size, align);
    }

    private static int ExtraAlignment(HavokClassTypes types, string className)
    {
        var cache = CacheFor(types);
        if (cache.ExtraAlign.TryGetValue(className, out int cached)) return cached;
        cache.ExtraAlign[className] = 0;

        int natural = NaturalAlign(types, className, PointerLayout.EightByte);
        int extra = 0;
        bool reproduces = false;

        if (types[className]?.Size is int stored)
        {
            foreach (int candidate in new[] { natural, 16, 32 })
            {
                if (candidate < natural) continue;
                var laid = Lay(types, className, PointerLayout.EightByte, candidate);
                if (laid.Size != stored) continue;

                bool offsetsMatch = true;
                var declared = types[className]!.Declared;
                for (int i = 0; i < declared.Count; i++)
                    if (laid.OffsetOf(declared[i].Name) != declared[i].Offset) { offsetsMatch = false; break; }

                if (offsetsMatch) { extra = candidate > natural ? candidate : 0; reproduces = true; break; }
            }
        }

        cache.ExtraAlign[className] = extra;
        cache.Reproduces[className] = reproduces;
        return extra;
    }

    private static int NaturalAlign(HavokClassTypes types, string className, PointerLayout layout)
    {
        string? parentName = types[className]?.Parent;
        int align = parentName != null && types.Knows(parentName)
            ? Of(types, parentName, layout).Alignment
            : (className == "hkBaseObject" ? layout.PointerSize : 1);

        foreach (var member in types[className]?.Declared ?? Array.Empty<HavokClassTypes.Member>())
            align = Math.Max(align, AlignOf(types, member, layout));
        return align;
    }

    private static int WidthOf(HavokClassTypes types, HavokClassTypes.Member member, PointerLayout layout)
    {
        int p = layout.PointerSize;
        return member.VType switch
        {
            "TYPE_POINTER" or "TYPE_STRINGPTR" or "TYPE_CSTRING" or "TYPE_ULONG" => p,
            // A relative array header is a single four-byte element count at both pointer
            // widths: the schema's hknpConvexShape.vertices@48 / planes@64, faces@68,
            // indices@72 prove it four bytes apart even in the eight-byte exe.
            "TYPE_RELARRAY" => 4,
            "TYPE_VARIANT" => 2 * p,
            "TYPE_SIMPLEARRAY" => p + 4,
            "TYPE_ARRAY" => p + 8,
            "TYPE_ENUM" or "TYPE_FLAGS" => HavokClassTypes.Width(member.VSub),
            "TYPE_STRUCT" => member.CType != null && types.Knows(member.CType)
                ? Of(types, member.CType, layout).Size
                : HavokClassTypes.Width(member.VType),
            _ => HavokClassTypes.Width(member.VType),
        };
    }

    private static int AlignOf(HavokClassTypes types, HavokClassTypes.Member member, PointerLayout layout)
    {
        int p = layout.PointerSize;
        return member.VType switch
        {
            "TYPE_POINTER" or "TYPE_STRINGPTR" or "TYPE_CSTRING" or "TYPE_ULONG"
                or "TYPE_VARIANT" or "TYPE_SIMPLEARRAY" or "TYPE_ARRAY" => p,
            // faces@68 following a four-byte planes@64 header shows the header aligns to its
            // own width, not the pointer width.
            "TYPE_RELARRAY" => 4,
            "TYPE_ENUM" or "TYPE_FLAGS" => HavokClassTypes.Width(member.VSub),
            "TYPE_VECTOR4" or "TYPE_QUATERNION" or "TYPE_QSTRANSFORM" or "TYPE_MATRIX3"
                or "TYPE_ROTATION" or "TYPE_TRANSFORM" or "TYPE_MATRIX4" => 16,
            "TYPE_STRUCT" => member.CType != null && types.Knows(member.CType)
                ? Of(types, member.CType, layout).Alignment
                : 4,
            _ => HavokClassTypes.Width(member.VType),
        };
    }

    private static int Align(int value, int to) => to <= 1 ? value : (value + to - 1) / to * to;
}
