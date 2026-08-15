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
        try
        {
        string magic = new(reader.ReadChars(4));
        uint version = reader.ReadUInt32();
        string kind = new(reader.ReadChars(4));
        uint count = reader.ReadUInt32();
        ulong nameTableOffset = reader.ReadUInt64();

        string name = System.IO.Path.GetFileName(archivePath);
        if (magic != "BTDX")
            throw new InvalidDataException($"{name} is not a BA2");
        if (kind != "GNRL")
            throw new InvalidDataException(
                $"{name} is a {kind} archive, not GNRL. Textures are stored differently and nothing " +
                "here reads them.");
        if (count > 1_000_000)
            throw new InvalidDataException($"{name} declares an implausible number of files");
        long nameTable = (long)nameTableOffset;
        if (nameTable < 0 || nameTable > stream.Length)
            throw new InvalidDataException($"{name} has a name table outside the file");

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

        stream.Position = nameTable;
        var entries = new List<Entry>((int)count);
        for (int i = 0; i < count; i++)
        {
            ushort length = reader.ReadUInt16();
            if ((long)length > stream.Length - stream.Position)
                throw new InvalidDataException($"{name} has a name table that runs past the end of the file");
            string entryName = Encoding.UTF8.GetString(reader.ReadBytes(length)).Replace('\\', '/');
            if (HasRootedComponent(entryName))
                throw new InvalidDataException($"{name} contains a rooted archive path: {entryName}");
            if (entryName.Contains(':'))
                throw new InvalidDataException($"{name} contains an archive path with a colon: {entryName}");
            entries.Add(new Entry(i, entryName, (long)offsets[i], packed[i], unpacked[i]));
        }

        foreach (var entry in entries)
        {
            long needed = entry.Packed != 0 ? entry.Packed : entry.Unpacked;
            if (entry.Offset < 0 || needed < 0 || needed > stream.Length - entry.Offset)
                throw new InvalidDataException($"{entry.Name} points outside the archive");
        }

        return new Ba2(archivePath, version, stream, reader, entries);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    public byte[] Read(Entry entry)
    {
        long wanted = entry.Packed != 0 ? entry.Packed : entry.Unpacked;
        if (wanted < 0 || wanted > _stream.Length - entry.Offset)
            throw new InvalidDataException($"{entry.Name} points outside the archive");
        if (wanted > 1 << 30)
            throw new InvalidDataException($"{entry.Name} declares an implausible packed size");

        _stream.Position = entry.Offset;
        var raw = new byte[wanted];
        int got = 0;
        while (got < wanted)
        {
            int n = _reader.Read(raw, got, (int)(wanted - got));
            if (n == 0) throw new InvalidDataException($"{entry.Name} is truncated");
            got += n;
        }
        return entry.Packed != 0 ? Inflate(raw, entry.Unpacked) : raw;
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

    public static string FlatFileName(string name) =>
        name.Replace('/', '_').Replace('\\', '_').Replace(':', '_');

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

            string target;
            if (keepFolders)
            {
                string flat = entry.Name.Replace('/', System.IO.Path.DirectorySeparatorChar);
                if (System.IO.Path.IsPathRooted(flat))
                    throw new InvalidDataException($"{entry.Name} is an absolute path");

                string root = System.IO.Path.GetFullPath(outputDir);
                string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, flat));
                string relative = System.IO.Path.GetRelativePath(root, full);
                bool outside = System.IO.Path.IsPathRooted(relative) ||
                               relative.Equals("..", StringComparison.Ordinal) ||
                               relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar,
                                                   StringComparison.Ordinal) ||
                               (System.IO.Path.AltDirectorySeparatorChar != System.IO.Path.DirectorySeparatorChar &&
                                relative.StartsWith(".." + System.IO.Path.AltDirectorySeparatorChar,
                                                    StringComparison.Ordinal));
                if (outside)
                    throw new InvalidDataException($"{entry.Name} is not inside the extraction folder");
                RefuseLinkedComponents(root, full, entry.Name);
                target = full;

                string? folder = System.IO.Path.GetDirectoryName(target);
                if (folder != null) Directory.CreateDirectory(folder);
            }
            else
            {
                target = System.IO.Path.Combine(outputDir, FlatFileName(entry.Name));
            }
            File.WriteAllBytes(target, archive.Read(entry));
            written++;
        }

        return written;
    }

    private static bool HasRootedComponent(string name)
    {
        if (name.StartsWith("/", StringComparison.Ordinal)) return true;
        foreach (string part in name.Split('/', StringSplitOptions.RemoveEmptyEntries))
            if (part.Length >= 2 && char.IsAsciiLetter(part[0]) && part[1] == ':') return true;
        return false;
    }

    private static byte[] Inflate(byte[] compressed, uint expectedUnpacked)
    {
        if (expectedUnpacked > 512 * 1024 * 1024)
            throw new InvalidDataException($"an entry declares an implausible unpacked size");

        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        var output = new byte[expectedUnpacked];
        int got = 0;
        while (got < expectedUnpacked)
        {
            int n = zlib.Read(output, got, (int)(expectedUnpacked - got));
            if (n == 0) break;
            got += n;
        }

        if (zlib.ReadByte() >= 0)
            throw new InvalidDataException(
                $"a stored file expands beyond its declared {expectedUnpacked} bytes");
        if (got != expectedUnpacked)
            throw new InvalidDataException(
                $"a stored file ends before its declared {expectedUnpacked} bytes");
        return output;
    }

    private static void RefuseLinkedComponents(string root, string target, string entryName)
    {
        string relative = System.IO.Path.GetRelativePath(root, target);
        string current = root;
        string[] parts = relative.Split(System.IO.Path.DirectorySeparatorChar,
                                        StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            current = System.IO.Path.Combine(current, parts[i]);
            FileSystemInfo info = i == parts.Length - 1
                ? new FileInfo(current)
                : new DirectoryInfo(current);
            try
            {
                if (info.LinkTarget != null ||
                    (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0))
                    throw new InvalidDataException(
                        $"{entryName} crosses a linked path inside the extraction folder");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
