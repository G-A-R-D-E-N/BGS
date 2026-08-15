using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services;

namespace OpenCommonwealth.Services.Hkx;

public sealed class PackfileImage
{
    public const uint Magic0 = 0x57E0E057;
    public const uint Magic1 = 0x10C0C010;
    public const int HeaderSize = 0x40;
    public const int SectionHeaderSize = 0x40;

    private const int TablePadding = 0x10;
    private const byte PadByte = 0xFF;

    public int UserTag;
    public int FileVersion = 11;
    public byte[] LayoutRules = { 8, 1, 0, 1 };
    public int ContentsSectionIndex;
    public int ContentsSectionOffset;
    public int ContentsClassNameSectionIndex;
    public int ContentsClassNameSectionOffset;
    public byte[] ContentsVersion = new byte[16];
    public int Flags;
    public short MaxPredicate;

    public byte[] Predicates = Array.Empty<byte>();

    public readonly List<PackfileSection> Sections = new();

    public PackfileSection? Section(string tag) =>
        Sections.Find(s => s.Tag == tag);

    public PointerLayout Layout => new(LayoutRules.Length > 0 ? LayoutRules[0] : 8);

    public static PackfileImage Read(string path) => Read(InputFilePolicy.ReadHkx(path));

    public static PackfileImage Read(byte[] bytes)
    {
        InputFilePolicy.EnsureHkx(bytes.LongLength);
        if (bytes.Length < HeaderSize) throw new InvalidDataException("Too small to be a packfile.");
        if (U32(bytes, 0) != Magic0 || U32(bytes, 4) != Magic1)
            throw new InvalidDataException("Not a Havok packfile: the magic does not match.");

        var image = new PackfileImage
        {
            UserTag = I32(bytes, 0x08),
            FileVersion = I32(bytes, 0x0C),
            LayoutRules = bytes[0x10..0x14],
            ContentsSectionIndex = I32(bytes, 0x18),
            ContentsSectionOffset = I32(bytes, 0x1C),
            ContentsClassNameSectionIndex = I32(bytes, 0x20),
            ContentsClassNameSectionOffset = I32(bytes, 0x24),
            ContentsVersion = bytes[0x28..0x38],
            Flags = I32(bytes, 0x38),
            MaxPredicate = (short)I16(bytes, 0x3C),
        };

        if (image.LayoutRules.Length < 4 || image.LayoutRules[1] != 1)
            throw new InvalidDataException(
                "Big-endian packfiles are not supported; only the little-endian layout is handled.");
        if (image.LayoutRules[0] != 4 && image.LayoutRules[0] != 8)
            throw new InvalidDataException(
                $"Unsupported pointer size {image.LayoutRules[0]}; only 4-byte and 8-byte layouts are handled.");

        int sectionCount = I32(bytes, 0x14);
        if (sectionCount < 0)
            throw new InvalidDataException("Negative section count.");
        int predicateBytes = I16(bytes, 0x3E);
        if (predicateBytes < 0 || HeaderSize + predicateBytes > bytes.Length)
            throw new InvalidDataException($"Predicate area of {predicateBytes} bytes does not fit.");

        image.Predicates = bytes[HeaderSize..(HeaderSize + predicateBytes)];

        int at = HeaderSize + predicateBytes;
        for (int i = 0; i < sectionCount; i++, at += SectionHeaderSize)
        {
            if (at + SectionHeaderSize > bytes.Length)
                throw new InvalidDataException($"Section header {i} runs past the end of the file.");
            image.Sections.Add(PackfileSection.Read(bytes, at));
        }

        return image;
    }

    public byte[] Rebuild()
    {
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);

        writer.Write(Magic0);
        writer.Write(Magic1);
        writer.Write(UserTag);
        writer.Write(FileVersion);
        writer.Write(LayoutRules, 0, 4);
        writer.Write(Sections.Count);
        writer.Write(ContentsSectionIndex);
        writer.Write(ContentsSectionOffset);
        writer.Write(ContentsClassNameSectionIndex);
        writer.Write(ContentsClassNameSectionOffset);
        writer.Write(ContentsVersion, 0, 16);
        writer.Write(Flags);
        writer.Write(MaxPredicate);
        writer.Write((short)Predicates.Length);
        writer.Write(Predicates);

        long headerTableAt = stream.Position;
        writer.Write(new byte[Sections.Count * SectionHeaderSize]);

        var placed = new List<int[]>();
        foreach (var section in Sections) placed.Add(section.Append(writer, stream));

        stream.Position = headerTableAt;
        for (int i = 0; i < Sections.Count; i++) Sections[i].WriteHeader(writer, placed[i]);

        return stream.ToArray();
    }

    public void Save(string path) => File.WriteAllBytes(path, Rebuild());

    internal static void PadTo(BinaryWriter writer, Stream stream)
    {
        int over = (int)(stream.Position % TablePadding);
        if (over == 0) return;
        for (int i = over; i < TablePadding; i++) writer.Write(PadByte);
    }

    internal static uint U32(byte[] b, int at) => BitConverter.ToUInt32(b, at);
    internal static int I32(byte[] b, int at) => BitConverter.ToInt32(b, at);
    internal static int I16(byte[] b, int at) => BitConverter.ToUInt16(b, at);
}

public sealed class PackfileSection
{

    public byte[] TagBytes = new byte[20];

    public byte[] Data = Array.Empty<byte>();
    public byte[] LocalFixups = Array.Empty<byte>();
    public byte[] GlobalFixups = Array.Empty<byte>();
    public byte[] VirtualFixups = Array.Empty<byte>();
    public byte[] Exports = Array.Empty<byte>();
    public byte[] Imports = Array.Empty<byte>();

    public byte[] HeaderTail = new byte[16];

    public string Tag
    {
        get
        {
            int end = Array.IndexOf(TagBytes, (byte)0);
            return Encoding.ASCII.GetString(TagBytes, 0, end < 0 ? TagBytes.Length : end);
        }
    }

    public IEnumerable<(int Source, int Destination)> Locals()
    {
        for (int at = 0; at + 8 <= LocalFixups.Length; at += 8)
        {
            if (IsFiller(LocalFixups, at, 8)) continue;
            yield return (PackfileImage.I32(LocalFixups, at), PackfileImage.I32(LocalFixups, at + 4));
        }
    }

    private static bool IsFiller(byte[] table, int at, int length)
    {
        for (int i = at; i < at + length; i++) if (table[i] != 0xFF) return false;
        return true;
    }

    public int AppendData(byte[] bytes)
    {
        int at = Data.Length;
        Array.Resize(ref Data, at + bytes.Length);
        bytes.CopyTo(Data, at);
        return at;
    }

    public const int StringAlignment = 2;

    public int AppendAligned(byte[] bytes, int alignment)
    {
        int over = Data.Length % alignment;
        if (over != 0) AppendData(new byte[alignment - over]);
        return AppendData(bytes);
    }

    public int AppendObject(byte[] bytes) => AppendAligned(bytes, 16);

    public void AddVirtual(int source, int section, int destination)
    {
        var entries = Virtuals().ToList();
        entries.Add((source, section, destination));

        var table = new byte[entries.Count * 12];
        for (int i = 0; i < entries.Count; i++)
        {
            BitConverter.GetBytes(entries[i].Source).CopyTo(table, i * 12);
            BitConverter.GetBytes(entries[i].Section).CopyTo(table, i * 12 + 4);
            BitConverter.GetBytes(entries[i].Destination).CopyTo(table, i * 12 + 8);
        }
        VirtualFixups = table;
    }

    public void SetLocal(int source, int destination)
    {
        var entries = Locals().ToList();
        int existing = entries.FindIndex(e => e.Source == source);

        if (destination < 0)
        {

            if (existing < 0) return;
            entries.RemoveAt(existing);
        }
        else if (existing >= 0) entries[existing] = (source, destination);
        else entries.Add((source, destination));

        var table = new byte[entries.Count * 8];
        for (int i = 0; i < entries.Count; i++)
        {
            BitConverter.GetBytes(entries[i].Source).CopyTo(table, i * 8);
            BitConverter.GetBytes(entries[i].Destination).CopyTo(table, i * 8 + 4);
        }
        LocalFixups = table;
    }

    public IEnumerable<(int Source, int Section, int Destination)> Globals() => Triples(GlobalFixups);

    public void SetGlobals(IEnumerable<(int Source, int Section, int Destination)> entries)
    {
        var all = entries.ToList();
        var table = new byte[all.Count * 12];
        for (int i = 0; i < all.Count; i++)
        {
            BitConverter.GetBytes(all[i].Source).CopyTo(table, i * 12);
            BitConverter.GetBytes(all[i].Section).CopyTo(table, i * 12 + 4);
            BitConverter.GetBytes(all[i].Destination).CopyTo(table, i * 12 + 8);
        }
        GlobalFixups = table;
    }

    public void SetLocals(IEnumerable<(int Source, int Destination)> entries)
    {
        var all = entries.ToList();
        var table = new byte[all.Count * 8];
        for (int i = 0; i < all.Count; i++)
        {
            BitConverter.GetBytes(all[i].Source).CopyTo(table, i * 8);
            BitConverter.GetBytes(all[i].Destination).CopyTo(table, i * 8 + 4);
        }
        LocalFixups = table;
    }

    public void SetGlobal(int source, int section, int destination)
    {
        var entries = Globals().ToList();
        int existing = entries.FindIndex(e => e.Source == source);

        if (destination < 0)
        {

            if (existing < 0) return;
            entries.RemoveAt(existing);
        }
        else if (existing >= 0) entries[existing] = (source, section, destination);
        else entries.Add((source, section, destination));

        var table = new byte[entries.Count * 12];
        for (int i = 0; i < entries.Count; i++)
        {
            BitConverter.GetBytes(entries[i].Source).CopyTo(table, i * 12);
            BitConverter.GetBytes(entries[i].Section).CopyTo(table, i * 12 + 4);
            BitConverter.GetBytes(entries[i].Destination).CopyTo(table, i * 12 + 8);
        }
        GlobalFixups = table;
    }

    public IEnumerable<(int Source, int Section, int Destination)> Virtuals() => Triples(VirtualFixups);

    public void SetVirtuals(IEnumerable<(int Source, int Section, int Destination)> entries)
    {
        var all = entries.ToList();
        var table = new byte[all.Count * 12];
        for (int i = 0; i < all.Count; i++)
        {
            BitConverter.GetBytes(all[i].Source).CopyTo(table, i * 12);
            BitConverter.GetBytes(all[i].Section).CopyTo(table, i * 12 + 4);
            BitConverter.GetBytes(all[i].Destination).CopyTo(table, i * 12 + 8);
        }
        VirtualFixups = table;
    }

    public int AlignData(int boundary)
    {
        int over = Data.Length % boundary;
        if (over != 0) Array.Resize(ref Data, Data.Length + boundary - over);
        return Data.Length;
    }

    private static IEnumerable<(int, int, int)> Triples(byte[] table)
    {
        for (int at = 0; at + 12 <= table.Length; at += 12)
        {
            if (IsFiller(table, at, 12)) continue;
            yield return (PackfileImage.I32(table, at),
                          PackfileImage.I32(table, at + 4),
                          PackfileImage.I32(table, at + 8));
        }
    }

    internal static PackfileSection Read(byte[] bytes, int header)
    {
        var section = new PackfileSection
        {
            TagBytes = bytes[header..(header + 20)],
            HeaderTail = bytes[(header + 0x30)..(header + 0x40)],
        };

        int start = PackfileImage.I32(bytes, header + 0x14);
        if (start < 0)
            throw new InvalidDataException("Section starts before the file.");
        int[] mark =
        {
            PackfileImage.I32(bytes, header + 0x18),
            PackfileImage.I32(bytes, header + 0x1C),
            PackfileImage.I32(bytes, header + 0x20),
            PackfileImage.I32(bytes, header + 0x24),
            PackfileImage.I32(bytes, header + 0x28),
            PackfileImage.I32(bytes, header + 0x2C),
        };

        for (int i = 1; i < mark.Length; i++)
            if (mark[i] < mark[i - 1])
                throw new InvalidDataException("Section table is not monotonic.");

        foreach (int m in mark)
            if (m < 0 || m > bytes.Length - start)
                throw new InvalidDataException($"Section '{section.Tag}' points past the end of the file.");

        section.Data = bytes[start..(start + mark[0])];
        section.LocalFixups = bytes[(start + mark[0])..(start + mark[1])];
        section.GlobalFixups = bytes[(start + mark[1])..(start + mark[2])];
        section.VirtualFixups = bytes[(start + mark[2])..(start + mark[3])];
        section.Exports = bytes[(start + mark[3])..(start + mark[4])];
        section.Imports = bytes[(start + mark[4])..(start + mark[5])];
        return section;
    }

    internal int[] Append(BinaryWriter writer, Stream stream)
    {
        PackfileImage.PadTo(writer, stream);
        int start = (int)stream.Position;

        var offsets = new int[7];
        offsets[0] = start;

        writer.Write(Data);
        PackfileImage.PadTo(writer, stream);
        offsets[1] = (int)stream.Position - start;

        writer.Write(LocalFixups);
        PackfileImage.PadTo(writer, stream);
        offsets[2] = (int)stream.Position - start;

        writer.Write(GlobalFixups);
        PackfileImage.PadTo(writer, stream);
        offsets[3] = (int)stream.Position - start;

        writer.Write(VirtualFixups);
        PackfileImage.PadTo(writer, stream);
        offsets[4] = (int)stream.Position - start;

        writer.Write(Exports);
        offsets[5] = (int)stream.Position - start;

        writer.Write(Imports);
        offsets[6] = (int)stream.Position - start;

        return offsets;
    }

    internal void WriteHeader(BinaryWriter writer, int[] offsets)
    {
        writer.Write(TagBytes, 0, 20);
        foreach (int value in offsets) writer.Write(value);
        writer.Write(HeaderTail, 0, 16);
    }
}
