using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class EmbeddedClassesTests
{
    [Fact]
    public void EveryMemberTypeNumberRoundTripsThroughItsName()
    {
        for (int value = 0; value < HavokMemberTypes.Count; value++)
            Assert.Equal(value, HavokMemberTypes.Value(HavokMemberTypes.Name(value)));

        Assert.Equal("TYPE_VOID", HavokMemberTypes.Name(0));
        Assert.Null(HavokMemberTypes.Value("TYPE_NOT_A_MEMBER_TYPE"));
        Assert.Equal("TYPE_99", HavokMemberTypes.Name(99));
    }

    [Fact]
    public void AFileWithoutTheSectionDescribesNothingAndIsNotAConflict()
    {
        var image = Objects("hkbStateMachine");
        Assert.False(EmbeddedClasses.Describes(image));

        var catalog = EmbeddedClasses.Read(image);
        Assert.False(catalog.Present);
        Assert.Empty(catalog.Definitions);

        var result = EmbeddedClassCheck.Compare(image);
        Assert.True(result.Agrees);
        Assert.Empty(result.UnknownToBuild);
    }

    [Fact]
    public void AnEmptySectionStillDescribesNothing()
    {
        var image = Objects("hkbStateMachine");
        image.Sections.Insert(1, new PackfileSection { TagBytes = Tag("__types__") });

        Assert.False(EmbeddedClasses.Describes(image));
        Assert.True(EmbeddedClassCheck.Compare(image).Agrees);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(4)]
    public void EmittedMetadataReadsBackAndAgreesWithTheShippedTable(int pointerSize)
    {
        var image = Bare(pointerSize, "hkbClipGenerator");
        var written = EmbeddedClassWriter.Write(image, EmbeddedClassWriter.Reachable(image));
        Assert.True(written.Described > 1);
        Assert.Empty(written.Refused);

        var reread = PackfileImage.Read(image.Rebuild());
        Assert.Equal(pointerSize, reread.Layout.PointerSize);
        Assert.True(EmbeddedClasses.Describes(reread));

        var catalog = EmbeddedClasses.Read(reread);
        Assert.Empty(catalog.Unreadable);
        Assert.Equal(written.Described, catalog.Definitions.Count);
        Assert.Equal(pointerSize, catalog.PointerSize);

        var result = EmbeddedClassCheck.Compare(reread);
        Assert.True(result.Agrees, string.Join("\n", result.Conflicts.Select(c => c.ToString())));
        Assert.Empty(result.UnknownToBuild);
    }

    [Fact]
    public void TheDescriptionCarriesTheRealMemberNamesOffsetsAndTypes()
    {
        var image = Bare(8, "hkbClipGenerator");
        EmbeddedClassWriter.Write(image, EmbeddedClassWriter.Reachable(image));

        var catalog = EmbeddedClasses.Read(PackfileImage.Read(image.Rebuild()));
        var clip = catalog["hkbClipGenerator"];
        Assert.NotNull(clip);

        var shipped = HavokClassTypes.Shipped["hkbClipGenerator"]!;
        Assert.Equal(shipped.Parent, clip!.Parent);
        Assert.Equal(shipped.Declared.Count, clip.Declared.Count);
        Assert.Equal(LayoutWalker.Of(HavokClassTypes.Shipped, "hkbClipGenerator", PointerLayout.EightByte).Size,
                     clip.ObjectSize);

        var animation = clip.Declared.Single(m => m.Name == "animationName");
        Assert.Equal("TYPE_STRINGPTR", animation.TypeName);
        Assert.Equal(LayoutWalker.Of(HavokClassTypes.Shipped, "hkbClipGenerator", PointerLayout.EightByte)
                                 .OffsetOf("animationName"),
                     animation.Offset);

        // a class reached only through a parent or a member's own class is described too,
        // or the description would point at a class it never wrote
        Assert.NotNull(catalog["hkbBindable"]);
        Assert.NotNull(catalog["hkBaseObject"]);
    }

    // Both sides derive offsets from the same walker, so a disagreement only exists when the
    // definitions themselves differ. Each case below changes one thing about the described
    // class and names the conflict that has to come out of it.
    [Fact]
    public void AMemberOffsetTheFileDisagreesWithIsReported()
    {
        // a wider first member pushes the second one along, which is exactly the shape of a
        // real table error: the member that is wrong is not the one that reads wrong
        var image = Described(Subject(new Field("first", 0, "TYPE_VECTOR4"),
                                      new Field("second", 0, "TYPE_INT32")));

        var conflict = Only(image, EmbeddedClassCheck.Kind.MemberOffset);
        Assert.Equal("hkTestSubject", conflict.Class);
        Assert.Equal("second", conflict.Where);
        Assert.NotEqual(conflict.File, conflict.Build);
    }

    [Fact]
    public void AnObjectSizeTheFileDisagreesWithIsReported()
    {
        var image = Described(Subject(new Field("first", 0, "TYPE_INT32"),
                                      new Field("second", 0, "TYPE_INT32"),
                                      new Field("third", 0, "TYPE_INT32")));

        var conflict = Only(image, EmbeddedClassCheck.Kind.ObjectSize);
        Assert.Equal("hkTestSubject", conflict.Class);
        Assert.NotEqual(conflict.File, conflict.Build);
    }

    [Fact]
    public void AMemberTypeTheFileDisagreesWithIsReported()
    {
        // the same width, so nothing moves and the type is the only thing left to disagree about
        var image = Described(Subject(new Field("first", 0, "TYPE_INT32"),
                                      new Field("second", 0, "TYPE_REAL")));

        var conflict = Only(image, EmbeddedClassCheck.Kind.MemberType);
        Assert.Equal("second", conflict.Where);
        Assert.Equal("TYPE_REAL/TYPE_VOID", conflict.File);
        Assert.Equal("TYPE_INT32/TYPE_VOID", conflict.Build);
        Assert.DoesNotContain(EmbeddedClassCheck.Kind.MemberOffset,
                              Conflicts(image).Select(c => c.Kind));
    }

    [Fact]
    public void AMemberTheBuildDoesNotDeclareIsReportedRatherThanIgnored()
    {
        var image = Described(Subject(new Field("first", 0, "TYPE_INT32"),
                                      new Field("second", 0, "TYPE_INT32"),
                                      new Field("third", 0, "TYPE_INT32")));

        var conflict = Only(image, EmbeddedClassCheck.Kind.MissingMember);
        Assert.Equal("third", conflict.Where);
        Assert.Equal("not declared", conflict.Build);
    }

    [Fact]
    public void AMemberTheFileDoesNotDeclareIsReportedRatherThanIgnored()
    {
        var image = Described(Subject(new Field("first", 0, "TYPE_INT32")));

        var conflict = Only(image, EmbeddedClassCheck.Kind.ExtraMember);
        Assert.Equal("second", conflict.Where);
        Assert.Equal("not declared", conflict.File);
    }

    [Fact]
    public void AClassTheBuildDoesNotKnowIsReportedRatherThanDropped()
    {
        var described = Subject(new Field("first", 0, "TYPE_INT32"),
                                new Field("second", 0, "TYPE_INT32"));
        described.Add(new Declared("hkNotInAnyBuild", null, 0x22222222,
                                   new List<Field> { new("only", 0, "TYPE_INT32") }));

        var image = Emit(described, "hkTestSubject", "hkNotInAnyBuild");
        var result = EmbeddedClassCheck.Compare(image, Straight());

        Assert.Contains("hkNotInAnyBuild", result.UnknownToBuild);
        var conflict = result.Conflicts.Single(c => c.Kind == EmbeddedClassCheck.Kind.UnknownToBuild);
        Assert.Equal("hkNotInAnyBuild", conflict.Class);
        Assert.Equal("no definition", conflict.Build);

        // the file still describes it, so there is a definition to fall back on
        var fallback = EmbeddedClasses.Read(image)["hkNotInAnyBuild"];
        Assert.NotNull(fallback);
        Assert.Equal("only", fallback!.Declared.Single().Name);
    }

    [Fact]
    public void ASignatureTheFileDisagreesWithIsReportedWithoutAnyEmbeddedMetadata()
    {
        var image = Objects("hkbStateMachine");
        image.Section("__classnames__")!.Data[0] ^= 0xFF;

        var conflict = EmbeddedClassCheck.Compare(image).Conflicts
                                         .Single(c => c.Kind == EmbeddedClassCheck.Kind.Signature);
        Assert.Equal("hkbStateMachine", conflict.Class);
        Assert.NotEqual(conflict.File, conflict.Build);
    }

    [Fact]
    public void WritingRefusesAClassTheBuildCannotDescribeRatherThanInventingOne()
    {
        var image = Objects("hkbStateMachine");
        var written = EmbeddedClassWriter.Write(image, new[] { "hkbStateMachine", "hkbNotAClass" });

        Assert.Equal(1, written.Described);
        Assert.Contains(written.Refused, r => r.StartsWith("hkbNotAClass", StringComparison.Ordinal));
    }

    [Fact]
    public void EmittingLeavesTheDataSectionAndItsObjectsAlone()
    {
        var image = Objects("hkbStateMachine", "hkbClipGenerator");
        byte[] before = image.Section("__data__")!.Data.ToArray();
        var namesBefore = new PackfileObjects(image).Instances.Select(i => i.ClassName).ToList();

        EmbeddedClassWriter.Write(image);
        var reread = PackfileImage.Read(image.Rebuild());

        Assert.Equal(before, reread.Section("__data__")!.Data);
        Assert.Equal(namesBefore, new PackfileObjects(reread).Instances.Select(i => i.ClassName).ToList());
    }

    // A small stand-in schema. Describing a file with one shape and checking it against
    // another is how a disagreement is produced, rather than bending bytes by hand.
    private sealed record Field(string Name, int Offset, string VType, string VSub = "TYPE_VOID");

    private sealed record Declared(string Name, string? Parent, uint Signature, List<Field> Members)
    {
        public int? Size { get; init; }
    }

    private static List<Declared> Subject(params Field[] members) =>
        new() { new Declared("hkTestSubject", null, 0x11111111, members.ToList()) };

    private static HavokClassTypes Straight() =>
        Schema(Subject(new Field("first", 0, "TYPE_INT32"), new Field("second", 0, "TYPE_INT32")));

    // The stand-in has to agree with itself before it can be emitted: the walker refuses a
    // class whose stored size and offsets it cannot reproduce. So the shape is walked once
    // with nothing stored, and the answers it gives are what gets stored.
    private static HavokClassTypes Schema(IEnumerable<Declared> classes)
    {
        var declared = classes.ToList();
        var draft = Draft(declared);
        var settled = declared.Select(c =>
        {
            var walked = LayoutWalker.Of(draft, c.Name, PointerLayout.EightByte);
            return c with
            {
                Size = walked.Size,
                Members = c.Members.Select(m => m with { Offset = walked.OffsetOf(m.Name) ?? m.Offset })
                           .ToList(),
            };
        }).ToList();
        return Draft(settled);
    }

    private static HavokClassTypes Draft(IEnumerable<Declared> classes)
    {
        var text = new StringBuilder("{\"classes\":{");
        bool first = true;
        foreach (var declared in classes)
        {
            if (!first) text.Append(',');
            first = false;
            text.Append($"\"{declared.Name}\":{{\"parent\":{(declared.Parent == null ? "null" : $"\"{declared.Parent}\"")},");
            text.Append($"\"signature\":\"0x{declared.Signature:x8}\",\"size\":{(declared.Size?.ToString() ?? "null")},\"members\":[");
            text.Append(string.Join(",", declared.Members.Select(m =>
                $"{{\"name\":\"{m.Name}\",\"offset\":{m.Offset},\"vtype\":\"{m.VType}\",\"vsub\":\"{m.VSub}\"," +
                "\"ctype\":null,\"etype\":null,\"arrsize\":0,\"written\":true,\"default\":null}")));
            text.Append("]}");
        }
        text.Append("}}");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text.ToString()));
        return HavokClassTypes.Parse(stream);
    }

    private static PackfileImage Emit(List<Declared> described, params string[] classes)
    {
        var image = Bare(8);
        var written = EmbeddedClassWriter.Write(image, classes, Schema(described));
        Assert.Empty(written.Refused);
        return PackfileImage.Read(image.Rebuild());
    }

    private static PackfileImage Described(List<Declared> described) =>
        Emit(described, "hkTestSubject");

    private static IReadOnlyList<EmbeddedClassCheck.Conflict> Conflicts(PackfileImage image) =>
        EmbeddedClassCheck.Compare(image, Straight()).Conflicts;

    private static EmbeddedClassCheck.Conflict Only(PackfileImage image, EmbeddedClassCheck.Kind kind)
    {
        var all = Conflicts(image);
        var matching = all.Where(c => c.Kind == kind).ToList();
        Assert.True(matching.Count == 1,
                    $"expected one {kind}, got: {string.Join(" | ", all)}");
        return matching[0];
    }

    private static PackfileImage Bare(int pointerSize, params string[] named)
    {
        var image = new PackfileImage { LayoutRules = new byte[] { (byte)pointerSize, 1, 0, 1 } };
        var names = new PackfileSection { TagBytes = Tag("__classnames__") };
        image.Sections.Add(names);
        image.Sections.Add(new PackfileSection { TagBytes = Tag("__data__") });

        foreach (string name in named)
        {
            var entry = new byte[5 + name.Length + 1];
            BitConverter.GetBytes(HavokClassTypes.Shipped[name]?.Signature ?? 0).CopyTo(entry, 0);
            entry[4] = 0x09;
            Encoding.ASCII.GetBytes(name).CopyTo(entry, 5);
            names.AppendData(entry);
        }
        return image;
    }

    private static PackfileImage Objects(params string[] classes)
    {
        var image = new PackfileImage();
        image.Sections.Add(new PackfileSection { TagBytes = Tag("__classnames__") });
        image.Sections.Add(new PackfileSection { TagBytes = Tag("__data__") });

        foreach (string className in classes) NativeAppend.Object(image, className);
        FixupOrder.Reorder(image);
        return PackfileImage.Read(image.Rebuild());
    }

    private static byte[] Tag(string name)
    {
        var bytes = new byte[20];
        Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        return bytes;
    }
}
