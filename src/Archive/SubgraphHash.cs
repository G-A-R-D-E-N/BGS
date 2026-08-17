using System;
using System.Collections.Generic;
using System.Text;

namespace OpenCommonwealth.Services.Archive;

public static class SubgraphHash
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0u);
            table[i] = crc;
        }
        return table;
    }

    public static uint RawCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0;
        foreach (byte b in data)
            crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        return crc;
    }

    public static uint BehaviorHalf(string behaviorPath) =>
        RawCrc32(Normalize(behaviorPath));

    public static ulong Compute(string behaviorPath, IReadOnlyList<string> saptPaths)
    {
        string joined = string.Join('|', saptPaths);
        uint hi = RawCrc32(Normalize(joined));
        uint lo = RawCrc32(Normalize(behaviorPath));
        return ((ulong)hi << 32) | lo;
    }

    private static byte[] Normalize(string value) =>
        Encoding.UTF8.GetBytes(value.Replace('/', '\\').ToLowerInvariant());
}
