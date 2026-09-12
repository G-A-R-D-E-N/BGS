using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// hkClassMember's type and subtype are stored as the numeric value of Havok's own member-type
// enum. The names are the shipped table's vocabulary, so the two only line up through this
// ordering. Every value below that appears anywhere in the Fallout 4 corpus was read back out
// of the reference reader's reflection rather than assumed; the unused ones keep the sequence
// contiguous, which is what makes the read ones land where they do.
public static class HavokMemberTypes
{
    private static readonly string[] Ordered =
    {
        "TYPE_VOID", "TYPE_BOOL", "TYPE_CHAR", "TYPE_INT8", "TYPE_UINT8", "TYPE_INT16",
        "TYPE_UINT16", "TYPE_INT32", "TYPE_UINT32", "TYPE_INT64", "TYPE_UINT64", "TYPE_REAL",
        "TYPE_VECTOR4", "TYPE_QUATERNION", "TYPE_MATRIX3", "TYPE_ROTATION", "TYPE_QSTRANSFORM",
        "TYPE_MATRIX4", "TYPE_TRANSFORM", "TYPE_ZERO", "TYPE_POINTER", "TYPE_FUNCTIONPOINTER",
        "TYPE_ARRAY", "TYPE_INPLACEARRAY", "TYPE_ENUM", "TYPE_STRUCT", "TYPE_SIMPLEARRAY",
        "TYPE_HOMOGENEOUSARRAY", "TYPE_VARIANT", "TYPE_CSTRING", "TYPE_ULONG", "TYPE_FLAGS",
        "TYPE_HALF", "TYPE_STRINGPTR", "TYPE_RELARRAY",
    };

    private static readonly Dictionary<string, int> ByName =
        Ordered.Select((name, value) => (name, value))
               .ToDictionary(pair => pair.name, pair => pair.value, StringComparer.Ordinal);

    public static int Count => Ordered.Length;

    public static string Name(int value) =>
        value >= 0 && value < Ordered.Length
            ? Ordered[value]
            : "TYPE_" + value.ToString(CultureInfo.InvariantCulture);

    public static int? Value(string name) =>
        ByName.TryGetValue(name, out int value) ? value : null;
}

// The class metadata some packfiles carry in a __types__ section: the file's own description of
// the classes it holds, as hkClass / hkClassMember / hkClassEnum / hkClassEnumItem objects.
// Fallout 4 files ship the section empty, so nothing in one can currently contradict the shipped
// table. A file that does describe itself gives that table something to be checked against.
public static class EmbeddedClasses
{
    public const string Section = "__types__";

    public sealed record Member(
        string Name,
        int Offset,
        int Type,
        int SubType,
        int CArraySize,
        int Flags,
        string? ClassName,
        string? EnumName)
    {
        public string TypeName => HavokMemberTypes.Name(Type);
        public string SubTypeName => HavokMemberTypes.Name(SubType);

        public override string ToString() => $"+{Offset} {Name} {TypeName}";
    }

    public sealed record EnumItem(string Name, int Value);

    public sealed record EnumDefinition(string Name, IReadOnlyList<EnumItem> Items);

    public sealed record Definition(
        string Name,
        string? Parent,
        int ObjectSize,
        int DescribedVersion,
        uint Flags,
        IReadOnlyList<Member> Declared,
        IReadOnlyList<EnumDefinition> Enums)
    {
        public override string ToString() =>
            $"{Name} size {ObjectSize}, {Declared.Count} declared member(s)";
    }

    public sealed record Catalog(
        bool Present,
        int PointerSize,
        IReadOnlyList<Definition> Definitions,
        IReadOnlyList<string> Unreadable)
    {
        public Definition? this[string className] =>
            Definitions.FirstOrDefault(d => string.Equals(d.Name, className, StringComparison.Ordinal));

        public override string ToString() => Present
            ? $"{Definitions.Count} class definition(s) at {PointerSize}-byte layout" +
              (Unreadable.Count > 0 ? $", {Unreadable.Count} unreadable" : "")
            : "no embedded class metadata";
    }

    public static bool Describes(PackfileImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var section = image.Section(Section);
        return section != null && section.Data.Length > 0 && section.Virtuals().Any();
    }

    public static Catalog Read(PackfileImage image, HavokClassTypes? types = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        _ = types;
        int pointer = image.Layout.PointerSize;

        if (!Describes(image))
            return new Catalog(false, pointer, Array.Empty<Definition>(), Array.Empty<string>());

        // hkClass and its companions are how the section is written, not something the file
        // describes, so the shipped definitions of those four always read it. The caller's
        // schema is what the described classes are then set against.
        var meta = HavokClassTypes.Shipped;
        var objects = new PackfileObjects(image, null, meta, Section);
        var memberLayout = LayoutWalker.Of(meta, "hkClassMember", image.Layout);
        var enumLayout = LayoutWalker.Of(meta, "hkClassEnum", image.Layout);
        var itemLayout = LayoutWalker.Of(meta, "hkClassEnumItem", image.Layout);

        var definitions = new List<Definition>();
        var unreadable = new List<string>();

        foreach (var instance in objects.OfClass("hkClass"))
        {
            string? name = objects.ReadString(instance, "name");
            if (name == null)
            {
                unreadable.Add($"an hkClass at 0x{instance.Offset:x} has no readable name");
                continue;
            }

            var parent = objects.ReadRef(instance, "parent", out _);
            definitions.Add(new Definition(
                name,
                parent == null ? null : objects.ReadString(parent, "name"),
                objects.ReadInt(instance, "objectSize") ?? 0,
                objects.ReadInt(instance, "describedVersion") ?? 0,
                (uint)(objects.ReadInt(instance, "flags") ?? 0),
                ReadMembers(objects, instance, memberLayout, unreadable, name),
                ReadEnums(objects, instance, enumLayout, itemLayout, unreadable, name)));
        }

        return new Catalog(true, pointer, definitions, unreadable);
    }

    private static IReadOnlyList<Member> ReadMembers(
        PackfileObjects objects, PackfileObjects.Instance owner, ObjectLayout layout,
        List<string> unreadable, string className)
    {
        int? at = objects.FieldAt(owner, "declaredMembers");
        var span = at == null ? null : objects.ArrayAt(at.Value, layout.Size);
        if (span == null || span.Count == 0) return Array.Empty<Member>();

        var members = new List<Member>(span.Count);
        for (int i = 0; i < span.Count; i++)
        {
            int record = span.At + i * layout.Size;
            string? name = Field(layout, record, "name", offset => objects.ReadStringAt(offset));
            if (name == null)
            {
                unreadable.Add($"{className} declares a member at index {i} with no readable name");
                continue;
            }

            var target = Field(layout, record, "class", offset => objects.ReadRefAt(offset, out _));
            var choices = Field(layout, record, "enum", offset => objects.ReadRefAt(offset, out _));

            members.Add(new Member(
                name,
                Field(layout, record, "offset", offset => objects.ReadNarrowAt(offset, 2)) ?? 0,
                Field(layout, record, "type", offset => objects.ReadNarrowAt(offset, 1)) ?? 0,
                Field(layout, record, "subtype", offset => objects.ReadNarrowAt(offset, 1)) ?? 0,
                Field(layout, record, "cArraySize", offset => objects.ReadNarrowAt(offset, 2)) ?? 0,
                Field(layout, record, "flags", offset => objects.ReadNarrowAt(offset, 2)) ?? 0,
                target == null ? null : objects.ReadString(target, "name"),
                choices == null ? null : objects.ReadString(choices, "name")));
        }
        return members;
    }

    private static IReadOnlyList<EnumDefinition> ReadEnums(
        PackfileObjects objects, PackfileObjects.Instance owner, ObjectLayout enumLayout,
        ObjectLayout itemLayout, List<string> unreadable, string className)
    {
        int? at = objects.FieldAt(owner, "declaredEnums");
        var span = at == null ? null : objects.ArrayAt(at.Value, enumLayout.Size);
        if (span == null || span.Count == 0) return Array.Empty<EnumDefinition>();

        var enums = new List<EnumDefinition>(span.Count);
        for (int i = 0; i < span.Count; i++)
        {
            int record = span.At + i * enumLayout.Size;
            string? name = Field(enumLayout, record, "name", offset => objects.ReadStringAt(offset));
            if (name == null)
            {
                unreadable.Add($"{className} declares an enum at index {i} with no readable name");
                continue;
            }
            enums.Add(new EnumDefinition(name, ReadItems(objects, enumLayout, itemLayout, record)));
        }
        return enums;
    }

    private static IReadOnlyList<EnumItem> ReadItems(
        PackfileObjects objects, ObjectLayout enumLayout, ObjectLayout itemLayout, int record)
    {
        int? at = enumLayout.OffsetOf("items") is int offset ? record + offset : null;
        var span = at == null ? null : objects.ArrayAt(at.Value, itemLayout.Size);
        if (span == null || span.Count == 0) return Array.Empty<EnumItem>();

        var items = new List<EnumItem>(span.Count);
        for (int i = 0; i < span.Count; i++)
        {
            int entry = span.At + i * itemLayout.Size;
            string? name = Field(itemLayout, entry, "name", o => objects.ReadStringAt(o));
            int value = Field(itemLayout, entry, "value", o => objects.ReadIntAt(o)) ?? 0;
            if (name != null) items.Add(new EnumItem(name, value));
        }
        return items;
    }

    private static T? Field<T>(ObjectLayout layout, int record, string member, Func<int, T?> read) =>
        layout.OffsetOf(member) is int offset ? read(record + offset) : default;
}

// What the file says about a class set against what the build's own table says. Nothing here
// picks a winner: a disagreement is reported with both values so the table can be corrected
// against the file, rather than one source quietly overriding the other.
public static class EmbeddedClassCheck
{
    public enum Kind
    {
        UnknownToBuild,
        ObjectSize,
        MissingMember,
        ExtraMember,
        MemberOffset,
        MemberType,
        Parent,
        Signature,
        NotPlaceable,
    }

    public sealed record Conflict(Kind Kind, string Class, string Where, string File, string Build)
    {
        public override string ToString() =>
            Where.Length > 0
                ? $"{Class}.{Where}: the file says {File}, the build says {Build}"
                : $"{Class}: the file says {File}, the build says {Build}";
    }

    public sealed record Result(
        Catalogue Described,
        IReadOnlyList<Conflict> Conflicts,
        IReadOnlyList<string> UnknownToBuild)
    {
        public bool Agrees => Conflicts.Count == 0;

        public override string ToString() =>
            $"{Described.Definitions.Count} described, {Conflicts.Count} conflict(s), " +
            $"{UnknownToBuild.Count} class(es) the build does not know";
    }

    public sealed record Catalogue(IReadOnlyList<EmbeddedClasses.Definition> Definitions);

    public static Result Compare(PackfileImage image, HavokClassTypes? types = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        var schema = types ?? HavokClassTypes.Shipped;
        var catalog = EmbeddedClasses.Read(image, schema);

        var conflicts = new List<Conflict>();
        var unknown = new List<string>();

        foreach (var described in catalog.Definitions)
        {
            if (!schema.Knows(described.Name))
            {
                unknown.Add(described.Name);
                conflicts.Add(new Conflict(Kind.UnknownToBuild, described.Name, "",
                                           $"{described.Declared.Count} declared member(s)",
                                           "no definition"));
                continue;
            }

            if (!LayoutWalker.CanPlace(schema, described.Name))
            {
                conflicts.Add(new Conflict(Kind.NotPlaceable, described.Name, "",
                                           $"size {described.ObjectSize}",
                                           "the build cannot place this class at this pointer size"));
                continue;
            }

            var built = LayoutWalker.Of(schema, described.Name, image.Layout);
            if (described.ObjectSize != built.Size)
                conflicts.Add(new Conflict(Kind.ObjectSize, described.Name, "",
                                           described.ObjectSize.ToString(CultureInfo.InvariantCulture),
                                           built.Size.ToString(CultureInfo.InvariantCulture)));

            string? parent = schema[described.Name]?.Parent;
            if (!string.Equals(described.Parent ?? "", parent ?? "", StringComparison.Ordinal))
                conflicts.Add(new Conflict(Kind.Parent, described.Name, "",
                                           described.Parent ?? "none", parent ?? "none"));

            CompareMembers(schema, described, built, conflicts);
        }

        CompareSignatures(image, schema, catalog, conflicts);
        return new Result(new Catalogue(catalog.Definitions), conflicts, unknown);
    }

    private static void CompareMembers(
        HavokClassTypes schema, EmbeddedClasses.Definition described, ObjectLayout built,
        List<Conflict> conflicts)
    {
        var declared = schema[described.Name]?.Declared ?? Array.Empty<HavokClassTypes.Member>();
        var byName = declared.ToDictionary(m => m.Name, StringComparer.Ordinal);

        foreach (var member in described.Declared)
        {
            if (!byName.TryGetValue(member.Name, out var ours))
            {
                conflicts.Add(new Conflict(Kind.MissingMember, described.Name, member.Name,
                                           $"+{member.Offset} {member.TypeName}", "not declared"));
                continue;
            }

            int? offset = built.OffsetOf(member.Name);
            if (offset != null && offset.Value != member.Offset)
                conflicts.Add(new Conflict(Kind.MemberOffset, described.Name, member.Name,
                                           "+" + member.Offset.ToString(CultureInfo.InvariantCulture),
                                           "+" + offset.Value.ToString(CultureInfo.InvariantCulture)));

            if (!string.Equals(member.TypeName, ours.VType, StringComparison.Ordinal) ||
                !string.Equals(member.SubTypeName, ours.VSub, StringComparison.Ordinal))
                conflicts.Add(new Conflict(Kind.MemberType, described.Name, member.Name,
                                           $"{member.TypeName}/{member.SubTypeName}",
                                           $"{ours.VType}/{ours.VSub}"));
        }

        var described_ = described.Declared.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var ours in declared)
            if (!described_.Contains(ours.Name))
                conflicts.Add(new Conflict(Kind.ExtraMember, described.Name, ours.Name,
                                           "not declared", $"+{ours.Offset} {ours.VType}"));
    }

    // The class-name table carries a signature per class in every packfile, described or not,
    // so this half of the check runs on files that ship no __types__ section at all.
    private static void CompareSignatures(
        PackfileImage image, HavokClassTypes schema, EmbeddedClasses.Catalog catalog,
        List<Conflict> conflicts)
    {
        if (image.Section(PackfileObjects.DataSection) == null ||
            image.Section("__classnames__") == null) return;

        foreach (var (signature, name) in new PackfileObjects(image, null, schema).ClassNames())
        {
            var known = schema[name];
            if (known == null || known.Signature == signature) continue;
            conflicts.Add(new Conflict(Kind.Signature, name, "",
                                       "0x" + signature.ToString("x8", CultureInfo.InvariantCulture),
                                       "0x" + known.Signature.ToString("x8", CultureInfo.InvariantCulture)));
        }
    }
}
