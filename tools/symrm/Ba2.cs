using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BehaviourStudio.Tools;

// Just enough of the BA2 general archive format to pull behaviour files out of
// Fallout4 - Animations.ba2. Reading the archive directly rather than shelling out to Archive2
// keeps the corpus step runnable on Linux with nothing installed.
//
// Layout: a 24 byte header, then one 36 byte entry per file, then a name table at the offset the
// header gives. An entry with a non zero packed size is zlib compressed. Version 1 and version 8
// archives both read the same way here; the next generation update only changed fields this does
// not touch.
public static class Ba2
{
    /// keepFolders writes the archive's own folder structure instead of flattening the path into the
    /// file name. Flat is right for a corpus, where 531 files called Behavior.hkx would overwrite each
    /// other; the tree is right when something has to resolve a project chain afterwards, because
    /// every reference inside those files is relative to the project folder.
    public static int ExtractMatching(string archivePath, string substring, string outputDir,
                                      string extension, Action<string> log, bool keepFolders = false)
    {
        using var stream = File.OpenRead(archivePath);
        using var reader = new BinaryReader(stream);

        string magic = new string(reader.ReadChars(4));
        uint version = reader.ReadUInt32();
        string kind = new string(reader.ReadChars(4));
        uint count = reader.ReadUInt32();
        ulong nameTableOffset = reader.ReadUInt64();

        if (magic != "BTDX") throw new InvalidDataException($"{Path.GetFileName(archivePath)} is not a BA2");
        if (kind != "GNRL") throw new InvalidDataException($"{Path.GetFileName(archivePath)} is a {kind} archive, not GNRL");
        log($"{Path.GetFileName(archivePath)}: version {version}, {count} files");

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
        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            ushort length = reader.ReadUInt16();
            names[i] = Encoding.UTF8.GetString(reader.ReadBytes(length));
        }

        Directory.CreateDirectory(outputDir);
        int written = 0;
        for (int i = 0; i < count; i++)
        {
            string name = names[i].Replace('\\', '/');
            if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.Contains(substring, StringComparison.OrdinalIgnoreCase)) continue;

            stream.Position = (long)offsets[i];
            byte[] raw = reader.ReadBytes((int)(packed[i] != 0 ? packed[i] : unpacked[i]));
            byte[] data = packed[i] != 0 ? Inflate(raw) : raw;

            // The archive path becomes the file name, so two behaviours called Behavior.hkx in
            // different folders do not overwrite each other.
            string target = keepFolders
                ? Path.Combine(outputDir, name.Replace('/', Path.DirectorySeparatorChar))
                : Path.Combine(outputDir, name.Replace('/', '_'));

            if (keepFolders) Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, data);
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
