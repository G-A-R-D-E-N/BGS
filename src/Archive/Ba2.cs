using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace OpenCommonwealth.Services.Archive;













public sealed class Ba2 : IDisposable
{
    public sealed record Entry(int Index, string Name, long Offset, uint Packed, uint Unpacked)
    {

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


    public byte[] Read(Entry entry)
    {
        _stream.Position = entry.Offset;
        byte[] raw = _reader.ReadBytes((int)(entry.Packed != 0 ? entry.Packed : entry.Unpacked));
        return entry.Packed != 0 ? Inflate(raw) : raw;
    }




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
