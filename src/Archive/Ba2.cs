using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace OpenCommonwealth.Services.Archive;

// Just enough of the BA2 general archive format to read behaviour files out of
// Fallout4 - Animations.ba2. Reading the archive directly rather than shelling out to Archive2
// keeps this runnable on Linux with nothing installed.
//
// Layout: a 24 byte header, then one 36 byte entry per file, then a name table at the offset the
// header gives. An entry with a non zero packed size is zlib compressed. Version 1 and version 8
// archives both read the same way here; the next generation update only changed fields this does
// not touch.
//
// Opening the index does not read any file's bytes, which is what makes browsing one of these
// worthwhile: Fallout4 - Animations.ba2 holds 29,716 entries and the whole point is to reach one of
// them without unpacking the other 29,715.
public sealed class Ba2 : IDisposable
{
    public sealed record Entry(int Index, string Name, long Offset, uint Packed, uint Unpacked)
    {
        /// The archive's own path, in the form the game uses.
        public string Folder => Name.Contains('/') ? Name[..Name.LastIndexOf('/')] : "";
        public string FileName => Name.Contains('/') ? Name[(Name.LastIndexOf('/') + 1)..] : Name;

        public override string ToString() => $"{Name} ({Unpacked} bytes)";
    }

    private readonly FileStream _stream;
    private readonly BinaryReader _reader;

    public string Path { get; }
    public uint Version { get; }
    public IReadOnlyList<Entry> Entries { get; }

    private Ba2(string path, uint version, FileStream stream, BinaryReader reader, List<Entry> entries)
    {
        Path = path;
        Version = version;
        _stream = stream;
        _reader = reader;
        Entries = entries;
    }

    /// Reads the header, the entry table and the name table, and nothing else. The stream stays open
    /// so a file can be pulled out later without walking all of that again.
    public static Ba2 Open(string archivePath)
    {
        var stream = File.OpenRead(archivePath);
        var reader = new BinaryReader(stream);

        string magic = new(reader.ReadChars(4));
        uint version = reader.ReadUInt32();
        string kind = new(reader.ReadChars(4));
        uint count = reader.ReadUInt32();
        ulong nameTableOffset = reader.ReadUInt64();

        string name = System.IO.Path.GetFileName(archivePath);
        if (magic != "BTDX")
        {
            stream.Dispose();
            throw new InvalidDataException($"{name} is not a BA2");
        }
        if (kind != "GNRL")
        {
            stream.Dispose();
            throw new InvalidDataException(
                $"{name} is a {kind} archive, not GNRL. Textures are stored differently and nothing " +
                "here reads them.");
        }

        var offsets = new ulong[count];
        var packed = new uint[count];
        var unpacked = new uint[count];
        for (int i = 0; i < count; i++)
        {
            reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt32();
            offsets[i] = reader.ReadUInt64();
            packed[i] = reader.ReadUInt32();
            unpacked[i] = reader.ReadUInt32();
            reader.ReadUInt32();
        }

        stream.Position = (long)nameTableOffset;
        var entries = new List<Entry>((int)count);
        for (int i = 0; i < count; i++)
        {
            ushort length = reader.ReadUInt16();
            string entryName = Encoding.UTF8.GetString(reader.ReadBytes(length)).Replace('\\', '/');
            entries.Add(new Entry(i, entryName, (long)offsets[i], packed[i], unpacked[i]));
        }

        return new Ba2(archivePath, version, stream, reader, entries);
    }

    /// One file's bytes, inflated if the archive stored it compressed.
    public byte[] Read(Entry entry)
    {
        _stream.Position = entry.Offset;
        byte[] raw = _reader.ReadBytes((int)(entry.Packed != 0 ? entry.Packed : entry.Unpacked));
        return entry.Packed != 0 ? Inflate(raw) : raw;
    }

    /// Entries whose path contains every one of the given words, in any order and without case. Words
    /// rather than one substring, because the useful query is "dogmeat behavior" and the archive
    /// stores that as `meshes/actors/dogmeat/behaviors/...`, where no single substring matches both.
    public IEnumerable<Entry> Matching(string query, string extension = "")
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in Entries)
        {
            if (extension.Length > 0 &&
                !entry.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;

            if (words.All(w => entry.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                yield return entry;
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
        _stream.Dispose();
    }

    /// keepFolders writes the archive's own folder structure instead of flattening the path into the
    /// file name. Flat is right for a corpus, where 531 files called Behavior.hkx would overwrite each
    /// other; the tree is right when something has to resolve a project chain afterwards, because
    /// every reference inside those files is relative to the project folder.
    public static int ExtractMatching(string archivePath, string substring, string outputDir,
                                      string extension, Action<string> log, bool keepFolders = false)
    {
        using var archive = Open(archivePath);
        log($"{System.IO.Path.GetFileName(archivePath)}: version {archive.Version}, " +
            $"{archive.Entries.Count} files");

        Directory.CreateDirectory(outputDir);
        int written = 0;

        foreach (var entry in archive.Matching(substring, extension))
        {
            // The archive path becomes the file name, so two behaviours called Behavior.hkx in
            // different folders do not overwrite each other.
            string target = keepFolders
                ? System.IO.Path.Combine(outputDir, entry.Name.Replace('/', System.IO.Path.DirectorySeparatorChar))
                : System.IO.Path.Combine(outputDir, entry.Name.Replace('/', '_'));

            if (keepFolders) Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, archive.Read(entry));
            written++;
        }

        return written;
    }

    private static byte[] Inflate(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }
}
