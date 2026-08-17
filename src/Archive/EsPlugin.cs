using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace OpenCommonwealth.Services.Archive;

/// <summary>
/// Reads the animation-set data Bethesda stores on race (RACE) records of a Fallout 4
/// master or plugin. Each animation set pairs a behavior graph (SGNM) with the animation
/// path prefixes (SAPT) the engine searches, in fallback order, when that behavior plays
/// an animation by bare name. The weapon sets are the authoritative weapon-type-to-folder
/// map: a weapon subgraph's generic clip is satisfied for a weapon type when a copy exists
/// under any of that type's prefixes.
///
/// The reader is deliberately narrow: it only needs the TES4/GRUP record shell and the
/// RACE subrecords named below, so it skips every other record without touching its bytes.
/// </summary>
public static class EsPlugin
{
    /// <summary>One animation set: a behavior and the path prefixes it resolves animations against.</summary>
    public sealed record AnimSet(uint SetId, string Behavior, IReadOnlyList<string> Paths, int Flags);

    private const uint CompressedFlag = 0x0004_0000;

    /// <summary>
    /// Every RACE record's animation sets in the plugin, in file order. Returns an empty
    /// list when the file holds none; throws <see cref="InvalidDataException"/> when the
    /// file is not a readable Bethesda plugin or a record's structure is impossible.
    /// </summary>
    public static List<AnimSet> RaceAnimationSets(string pluginPath)
    {
        using var stream = new FileStream(pluginPath, FileMode.Open, FileAccess.Read,
                                          FileShare.Read | FileShare.Delete, 1 << 16,
                                          FileOptions.SequentialScan);
        using var reader = new BinaryReader(stream);
        return RaceAnimationSets(reader, stream.Length);
    }

    public static List<AnimSet> RaceAnimationSets(BinaryReader reader, long length)
    {
        if (length < 24) throw new InvalidDataException("too short to be a Bethesda plugin");

        var sets = new List<AnimSet>();
        WalkRecords(reader, length, sets);
        return sets;
    }

    private static void WalkRecords(BinaryReader reader, long end, List<AnimSet> sets)
    {
        while (reader.BaseStream.Position + 24 <= end)
        {
            long start = reader.BaseStream.Position;
            string type = ReadAscii(reader, 4);
            uint size = reader.ReadUInt32();
            uint flags = reader.ReadUInt32();
            uint formid = reader.ReadUInt32();
            reader.ReadUInt32(); // timestamp
            reader.ReadUInt16(); // version
            reader.ReadUInt16(); // internal

            if (type == "GRUP")
            {
                // size covers the whole group including its 24-byte header
                if (size < 24 || (ulong)start + size > (ulong)end)
                    throw new InvalidDataException("a GRUP runs past the end of the file");
                WalkRecords(reader, start + size, sets);
                continue;
            }

            long dataAt = reader.BaseStream.Position;
            if (dataAt + size > end)
                throw new InvalidDataException($"a {type} record runs past the end of the file");

            byte[] payload;
            if ((flags & CompressedFlag) != 0)
            {
                if (size < 4)
                    throw new InvalidDataException($"a compressed {type} record has no size word");
                uint unpacked = reader.ReadUInt32();
                long packedLen = size - 4;
                payload = Inflate(reader, packedLen, unpacked, type);
            }
            else
            {
                payload = reader.ReadBytes(checked((int)size));
            }

            if (type == "RACE") ReadAnimationSets(payload, sets);

            reader.BaseStream.Position = dataAt + size;
        }
    }

    private static void ReadAnimationSets(byte[] payload, List<AnimSet> sets)
    {
        // RACE subrecords: 4-byte type, 2-byte size, data. The animation-set block is a
        // sequence of groups: SAKD (set id) followed by SGNM (behavior), SAPT (animation
        // path prefixes, fallback order), SRAF (flags) and STKD (template ids).
        uint setId = 0;
        string? behavior = null;
        var paths = new List<string>();
        int flags = 0;

        void Close()
        {
            if (setId != 0 && behavior != null && paths.Count > 0)
                sets.Add(new AnimSet(setId, behavior, paths.ToArray(), flags));
            setId = 0; behavior = null; paths.Clear(); flags = 0;
        }

        int at = 0;
        while (at + 6 <= payload.Length)
        {
            string tag = Encoding.ASCII.GetString(payload, at, 4);
            int size = payload[at + 4] | (payload[at + 5] << 8);
            at += 6;
            if (size < 0 || at + size > payload.Length)
                throw new InvalidDataException("a RACE subrecord runs past the end of its record");

            switch (tag)
            {
                case "SAKD" when size >= 4:
                    Close();
                    setId = (uint)(payload[at] | (payload[at + 1] << 8) | (payload[at + 2] << 16) | (payload[at + 3] << 24));
                    break;
                case "SGNM" when behavior == null:
                    behavior = ReadCString(payload, at, size);
                    break;
                case "SAPT" when behavior != null:
                    paths.Add(ReadCString(payload, at, size));
                    break;
                case "SRAF" when size >= 4:
                    flags = payload[at] | (payload[at + 1] << 8) | (payload[at + 2] << 16) | (payload[at + 3] << 24);
                    break;
                case "STKD":
                    break;
                default:
                    Close();
                    break;
            }
            at += size;
        }
        Close();
    }

    private static byte[] Inflate(BinaryReader reader, long packedLen, uint unpacked, string type)
    {
        if (unpacked > 256 * 1024 * 1024)
            throw new InvalidDataException($"a compressed {type} record declares an implausible size");
        var compressed = reader.ReadBytes(checked((int)packedLen));
        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        var output = new byte[unpacked];
        int got = 0;
        while (got < output.Length)
        {
            int n = zlib.Read(output, got, output.Length - got);
            if (n == 0) break;
            got += n;
        }
        if (got != output.Length)
            throw new InvalidDataException($"a compressed {type} record ends before its declared size");
        return output;
    }

    private static string ReadAscii(BinaryReader reader, int count) =>
        Encoding.ASCII.GetString(reader.ReadBytes(count));

    private static string ReadCString(byte[] data, int at, int size)
    {
        int len = 0;
        while (len < size && data[at + len] != 0) len++;
        return Encoding.UTF8.GetString(data, at, len);
    }
}
