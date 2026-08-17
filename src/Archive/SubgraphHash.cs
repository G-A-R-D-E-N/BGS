using System;
using System.Collections.Generic;
using System.Text;

namespace OpenCommonwealth.Services.Archive;

/// <summary>
/// The 64-bit subgraph identifier Bethesda's engine computes at runtime to key its
/// AnimTextData caches (Meshes\AnimTextData\AnimationOffsets\&lt;id&gt;.txt).
///
/// The engine builds the id from a race record's animation set: a behavior graph (SGNM)
/// plus the animation-folder prefixes (SAPT) the graph resolves bare clip names against.
///
///   id = raw_crc32(join(sapt_paths, "|")) &lt;&lt; 32 | raw_crc32(behavior_path)
///
/// where raw_crc32 is CRC-32 with the reflected 0xEDB88320 table, a running value
/// initialised to zero, and no final XOR — the same function the engine's subgraph
/// record constructor computes twice and packs into one 64-bit value
/// (GetSubgraphNodeIDPrefix / CreateRootIdleArray family). Both inputs are lowercased
/// and use backslashes; paths that arrive with forward slashes are normalised first.
///
/// Verified against the game's own shipped id -&gt; subgraph manifests: every one of the
/// 2065 base-game subgraph ids can be recomputed exactly from its race record's SGNM and
/// SAPT subrecords, and the high half of the Automatron weapon set resolves through the
/// DLC's additive race record. The reference crash-log hash 10448007347639226270 splits
/// as 0x90fec753 &lt;&lt; 32 | 0xa6b50f9e, where the low half is the CRC of
/// "actors\character\behaviors\weaponbehavior.hkx" and the high half is the Automatron
/// weapon-pool prefix join.
/// </summary>
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

    /// <summary>
    /// CRC-32 over the bytes, reflected 0xEDB88320 table, running value initialised to
    /// zero, no final XOR. This is the exact function the engine applies to the behavior
    /// path and the joined prefix list; it is deliberately not the zlib CRC-32 variant
    /// (which initialises to 0xFFFFFFFF and XORs the result).
    /// </summary>
    public static uint RawCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0;
        foreach (byte b in data)
            crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        return crc;
    }

    /// <summary>CRC of a single behavior path: lowercased, backslashes.</summary>
    public static uint BehaviorHalf(string behaviorPath) =>
        RawCrc32(Normalize(behaviorPath));

    /// <summary>
    /// The full subgraph id: the CRC of the joined, lowercased prefix list in the high
    /// half, the CRC of the lowercased behavior path in the low half.
    /// </summary>
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
