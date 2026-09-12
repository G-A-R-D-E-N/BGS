using System.Buffers.Binary;

namespace OpenCommonwealth.Services.Nif;

public sealed record NifEmbeddedPhysics(
    int BlockIndex, string BlockType,
    uint NifVersion, byte[] Bytes);

public static class NifPhysics
{
    private const string RagdollBlock = "bhkRagdollSystem";

    public static IReadOnlyList<NifEmbeddedPhysics> Read(NifFile nif)
    {
        List<NifEmbeddedPhysics> answer = new();
        foreach (var block in nif.BlocksOfType(RagdollBlock))
        {
            int at = nif.BlockStart[block];
            int end = at + nif.BlockSize[block];
            if (at < 0 || end < at || end > nif.Data.Length || end - at < 4)
                throw new InvalidDataException($"physics block {block} does not fit in the NIF");

            uint encodedCount = BinaryPrimitives.ReadUInt32LittleEndian(nif.Data.AsSpan(at, 4));
            at += 4;
            if (encodedCount > (uint)(nif.BlockSize[block] - 4))
                throw new InvalidDataException($"embedded Havok length {encodedCount} does not fit in block {block}");

            int count = (int)encodedCount;
            answer.Add(new NifEmbeddedPhysics(
                block, nif.BlockType[block], nif.Version,
                nif.Data.AsSpan(at, count).ToArray()));
        }
        return answer;
    }
}
