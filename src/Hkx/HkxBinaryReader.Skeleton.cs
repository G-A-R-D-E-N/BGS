using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

public partial class HkxBinaryReader
{
    private static HkxSkeleton? ParseSkeleton(byte[] data, int dataAbs, int skelRel, int sectionEnd,
        Dictionary<int, int> fixups)
    {
        int a = dataAbs + skelRel;
        if (a + 0x50 > sectionEnd || a + 0x50 > data.Length) return null;

        var skel = new HkxSkeleton();

        if (fixups.TryGetValue(skelRel + 0x10, out int nameRel))
            skel.Name = ReadNullTermString(data, dataAbs + nameRel, 256);

        int parCount = SafeReadI32(data, a + 0x20);
        if (fixups.TryGetValue(skelRel + 0x18, out int parDataRel))
        {
            int parAbs = dataAbs + parDataRel;
            for (int i = 0; i < parCount && parAbs + i * 2 + 2 <= data.Length; i++)
                skel.ParentIndices.Add(BitConverter.ToInt16(data, parAbs + i * 2));
        }

        int boneCount = SafeReadI32(data, a + 0x30);
        if (boneCount < 0) return null;
        if (fixups.TryGetValue(skelRel + 0x28, out int bonesDataRel))
        {
            int bonesAbs = dataAbs + bonesDataRel;
            if (bonesAbs < dataAbs || boneCount > (sectionEnd - bonesAbs) / 0x10) return null;
            for (int i = 0; i < boneCount; i++)
            {
                int boneOff = bonesDataRel + i * 0x10;
                if (fixups.TryGetValue(boneOff, out int bnRel))
                    skel.BoneNames.Add(ReadNullTermString(data, dataAbs + bnRel, 256));
                else
                    skel.BoneNames.Add($"Bone_{i}");
            }
        }

        int poseCount = SafeReadI32(data, a + 0x40);
        if (fixups.TryGetValue(skelRel + 0x38, out int poseDataRel))
        {
            int poseAbs = dataAbs + poseDataRel;
            for (int i = 0; i < poseCount && poseAbs + i * 48 + 48 <= data.Length; i++)
            {
                int p = poseAbs + i * 48;
                var tx  = new Vector3(ReadF32(data, p),      ReadF32(data, p + 4),  ReadF32(data, p + 8));
                var rot = new Quaternion(ReadF32(data, p+16), ReadF32(data, p+20), ReadF32(data, p+24), ReadF32(data, p+28));
                var sc  = new Vector3(ReadF32(data, p + 32), ReadF32(data, p + 36), ReadF32(data, p + 40));
                skel.ReferencePose.Add(new HkxBonePose(tx, rot, sc));
            }
        }

        if (skel.BoneNames.Count == 0) return null;
        if (string.IsNullOrEmpty(skel.Name) && skel.BoneNames.Count > 0)
            skel.Name = skel.BoneNames[0];
        return skel;
    }
}
