using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenCommonwealth.Services.Hkx;

// A Havok packfile taken apart into the pieces it is actually made of, and put back together again.
//
// This exists so the tool can write a .hkx without going out through hkxpack's XML and back. The
// layout is not guessed: it was read out of Fallout 4's own writer, which the game still carries.
// hkBinaryPackfileWriter::save runs the sequence, writeAllObjects lays out the objects and the local
// fixups, and doDeferredWrites writes the other two tables. Notes and decompiles live in the F4SE
// workspace under ReverseEngineering/03-FINDINGS.md and Findings/Havok/.
//
// The measure of this class is Rebuild(): read a real file, write it back, and get the same bytes.
// Every offset in a packfile is derived from the sizes of what came before it, so a file that comes
// back byte for byte is a file whose every offset we computed correctly rather than copied.
public sealed class PackfileImage
{
    public const uint Magic0 = 0x57E0E057;
    public const uint Magic1 = 0x10C0C010;
    public const int HeaderSize = 0x40;
    public const int SectionHeaderSize = 0x40;

    /// Tables are padded up to this with 0xFF, and a table's recorded offset is the position after
    /// the previous table's padding. Getting this wrong shifts every later offset.
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

    /// The bytes between the file header and the first section header. Its length is what the header
    /// calls the predicate array size plus padding, and it is not always zero: Fallout 4's animation
    /// and skeleton files carry 16 bytes here while its behaviour files carry none. A reader that
    /// assumes one or the other silently reads the section headers at the wrong place.
    public byte[] Predicates = Array.Empty<byte>();

    public readonly List<PackfileSection> Sections = new();

    public PackfileSection? Section(string tag) =>
        Sections.Find(s => s.Tag == tag);

    public static PackfileImage Read(string path) => Read(File.ReadAllBytes(path));

    public static PackfileImage Read(byte[] bytes)
    {
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

        int sectionCount = I32(bytes, 0x14);
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

    /// The file these pieces describe. Offsets are computed from the sizes here, never carried over
    /// from what was read, so a rebuild that matches its input proves the computation.
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

        // The section headers cannot be filled in until their contents have been laid out, so the
        // space is reserved and written over at the end. This is what the game's writer does too:
        // it remembers the position, writes everything, then seeks back.
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
    /// Kept as raw bytes rather than a string: the field is 20 bytes, the name is null terminated
    /// inside it, and what follows the terminator is left over from the 0xFF the whole header is
    /// filled with before the name is copied in. Rewriting it from the string alone would not
    /// reproduce the file.
    public byte[] TagBytes = new byte[20];

    public byte[] Data = Array.Empty<byte>();
    public byte[] LocalFixups = Array.Empty<byte>();
    public byte[] GlobalFixups = Array.Empty<byte>();
    public byte[] VirtualFixups = Array.Empty<byte>();
    public byte[] Exports = Array.Empty<byte>();
    public byte[] Imports = Array.Empty<byte>();

    /// The last 16 bytes of the 64 byte section header, past the seven offsets. Left over from the
    /// 0xFF fill and carried rather than regenerated, for the same reason as the tag bytes.
    public byte[] HeaderTail = new byte[16];

    public string Tag
    {
        get
        {
            int end = Array.IndexOf(TagBytes, (byte)0);
            return Encoding.ASCII.GetString(TagBytes, 0, end < 0 ? TagBytes.Length : end);
        }
    }

    /// A pointer from one place in this section to another place in the same section. Both ends are
    /// relative to the section's own data.
    public IEnumerable<(int Source, int Destination)> Locals()
    {
        for (int at = 0; at + 8 <= LocalFixups.Length; at += 8)
        {
            if (IsFiller(LocalFixups, at, 8)) continue;
            yield return (PackfileImage.I32(LocalFixups, at), PackfileImage.I32(LocalFixups, at + 4));
        }
    }

    /// A table is padded up to a 16 byte boundary with 0xFF and its recorded offset is taken after
    /// that padding, so the padding is inside the table as far as the next offset is concerned and
    /// there is no count anywhere saying where the real entries stop. Reading the padding as entries
    /// invents fixups that were never written. An entry of nothing but 0xFF is either that padding
    /// or, in the two pointer tables, a pointer the writer could not resolve; neither is something a
    /// caller should act on, so both are skipped.
    private static bool IsFiller(byte[] table, int at, int length)
    {
        for (int i = at; i < at + length; i++) if (table[i] != 0xFF) return false;
        return true;
    }

    /// Puts bytes at the end of this section's data and says where they landed.
    ///
    /// This is how a value that changes size gets written without moving anything. Nothing in the
    /// format requires a string, or an object, to sit in any particular place: what makes a byte
    /// mean something is a fixup pointing at it. So the new bytes go on the end, where no offset
    /// anybody already holds can reach, and only the fixup that names them has to change. Whatever
    /// the pointer held before is left where it is, unreferenced, which is what an unreferenced run
    /// of bytes in this format already looks like.
    public int AppendData(byte[] bytes)
    {
        int at = Data.Length;
        Array.Resize(ref Data, at + bytes.Length);
        bytes.CopyTo(Data, at);
        return at;
    }

    /// What a string has to land on, and why it is two rather than sixteen.
    ///
    /// A string member keeps an ownership flag in the lowest bit of its pointer, so an odd address
    /// reads as "this buffer belongs to me, release it with the object". Section data starts on a
    /// sixteen byte boundary, so the parity of an offset is the parity of the loaded address, and a
    /// string on an odd offset hands the game a pointer claiming to own memory inside the packfile.
    ///
    /// Two rather than sixteen because that is what the shipped files actually do. Every one of the
    /// 37,545 local fixup destinations across the 453 sample files is even, but of the 7,618 that
    /// point at text only 6,278 are sixteen byte aligned. Rounding to sixteen would be safe and
    /// would stop matching Havok's own output.
    public const int StringAlignment = 2;

    /// Appends so the run begins on a multiple of `alignment`, padding with zeroes to get there.
    ///
    /// Here rather than at each caller because three places append a string and one appends an
    /// object, and the last time a rule like this lived in four places, three of them had it wrong.
    public int AppendAligned(byte[] bytes, int alignment)
    {
        int over = Data.Length % alignment;
        if (over != 0) AppendData(new byte[alignment - over]);
        return AppendData(bytes);
    }

    /// Puts a new object's bytes at the end of this section, aligned the way every object in every
    /// vanilla file is.
    ///
    /// Measured rather than assumed: 24,788 objects across 120 behaviour files, every one of them on
    /// a sixteen byte boundary. Appending an object at whatever offset the data happens to end on
    /// would be the kind of thing that works until it does not.
    public int AppendObject(byte[] bytes) => AppendAligned(bytes, 16);

    /// Adds the entry that says an object at this offset is an instance of the class whose name sits
    /// at that offset in `__classnames__`. Its position is put right by the reorder that follows.
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

    /// Points the local fixup for a source offset at a new destination, adding one when the field
    /// held no pointer at all.
    ///
    /// The order of the table is left exactly as it was found, and a new entry goes on the end.
    /// Sorting it looked tidier and was wrong: Fallout 4's own tables are not in source order, 383
    /// of Dogmeat's 1587 entries move if you sort them, so a sort rewrites most of a table to no
    /// purpose and hides a real change among the noise. Nothing reads this table by position.
    public void SetLocal(int source, int destination)
    {
        var entries = Locals().ToList();
        int existing = entries.FindIndex(e => e.Source == source);

        if (destination < 0)
        {
            // An empty array has no pointer at all, the same way a null reference has no fixup. A
            // fixup left aiming at offset zero would point the array at the start of the section.
            if (existing < 0) return;
            entries.RemoveAt(existing);
        }
        else if (existing >= 0) entries[existing] = (source, destination);
        else entries.Add((source, destination));

        // Rewritten from the entries rather than patched in place, because adding one lengthens the
        // table. The trailing 0xFF it was padded with is not carried over: the rebuild pads every
        // table to the boundary itself, so putting it back here would pad the padding.
        var table = new byte[entries.Count * 8];
        for (int i = 0; i < entries.Count; i++)
        {
            BitConverter.GetBytes(entries[i].Source).CopyTo(table, i * 8);
            BitConverter.GetBytes(entries[i].Destination).CopyTo(table, i * 8 + 4);
        }
        LocalFixups = table;
    }

    /// A pointer into another section.
    public IEnumerable<(int Source, int Section, int Destination)> Globals() => Triples(GlobalFixups);

    /// Points the global fixup for a source offset at a new object, adds one where the field held
    /// nothing, or drops it when the field is being set to null.
    ///
    /// This is what rewiring a node is, in bytes. A pointer from one object to another is a global
    /// fixup naming a source and a destination, and nothing about the objects themselves changes
    /// when it is aimed somewhere else. No byte moves, nothing is appended, and the file is the same
    /// length afterwards. It is only structural in the editor's sense.
    ///
    /// Same rule as `SetLocal` about order: the table is left as found and a new entry goes on the
    /// end, because Fallout 4's own tables are not sorted and nothing reads them by position.
    /// Writes the whole table from a list, so a caller that has to control where entries sit can.
    ///
    /// Position in this table is not free. It is written in the order the writer walked the objects,
    /// which is not offset order: an array's element pointers are written while the array is being
    /// walked, before the fields that follow it in the owning object. Measured on Dogmeat, 22 of the
    /// 1,151 steps go backwards, and every one of them is an array. Sorting the table by source
    /// makes hkxpack misread more than a hundred fields, so something downstream depends on that
    /// order and it is not ours to tidy.
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

    /// The local table written from a list, for the same reason the global one can be.
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
            // A null pointer is the absence of a fixup, not a fixup to nowhere. Leaving one pointing
            // at offset zero would aim the field at whatever object happens to sit first.
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

    /// The class name an object is an instance of. Always points into __classnames__, which is why
    /// the middle field is always zero.
    public IEnumerable<(int Source, int Section, int Destination)> Virtuals() => Triples(VirtualFixups);

    /// The virtual table written from a list.
    ///
    /// This table is the object list. An object's position in it is the position everything outside
    /// the file names it by, and hkxpack's `#90` upward numbering is that position plus ninety. So
    /// where an entry goes here decides what a new object is called, and a new one goes on the end
    /// because its bytes went on the end. Measured across the corpus first: in all 533 vanilla files
    /// the sources in this table are strictly ascending, 38,152 of them, so table order and file
    /// order are the same thing and appending to both keeps them that way.
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

    /// Pads the data out to a boundary so what is appended next starts on it, and answers where that
    /// will be. A class holding a vector is sixteen aligned and the game reads it with instructions
    /// that require the alignment, so an object landing on an odd offset is not a cosmetic problem.
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
        int[] mark =
        {
            PackfileImage.I32(bytes, header + 0x18),  // local fixups
            PackfileImage.I32(bytes, header + 0x1C),  // global fixups
            PackfileImage.I32(bytes, header + 0x20),  // virtual fixups
            PackfileImage.I32(bytes, header + 0x24),  // exports
            PackfileImage.I32(bytes, header + 0x28),  // imports
            PackfileImage.I32(bytes, header + 0x2C),  // end
        };

        foreach (int m in mark)
            if (m < 0 || start + m > bytes.Length)
                throw new InvalidDataException($"Section '{section.Tag}' points past the end of the file.");

        section.Data = bytes[start..(start + mark[0])];
        section.LocalFixups = bytes[(start + mark[0])..(start + mark[1])];
        section.GlobalFixups = bytes[(start + mark[1])..(start + mark[2])];
        section.VirtualFixups = bytes[(start + mark[2])..(start + mark[3])];
        section.Exports = bytes[(start + mark[3])..(start + mark[4])];
        section.Imports = bytes[(start + mark[4])..(start + mark[5])];
        return section;
    }

    /// Writes this section's contents and reports where everything landed, as the seven numbers its
    /// header carries: the absolute start, then each table's offset from that start.
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
