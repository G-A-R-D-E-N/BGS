using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public partial class HkxBinaryReader
{
    #region HKX Section Parsing

    private struct SectionInfo
    {
        public int DataStart;
        public int LocalFixupAbs;
        public int GlobalFixupAbs;
        public int VirtualFixupAbs;
        public int ExportsAbs;
        public int End;
    }

    internal HkxAnimationData ParseHkx(byte[] data)
    {
        InputFilePolicy.EnsureHkx(data.LongLength);
        if (data.Length < 64)
            throw new InvalidDataException("HKX file too small.");
        if (data[0] != HkxMagic[0] || data[1] != HkxMagic[1] ||
            data[2] != HkxMagic[2] || data[3] != HkxMagic[3])
            throw new InvalidDataException("Not a valid HKX binary packfile (bad magic).");

        int version = ReadI32(data, 0x0C);
        if (version != 11)
            throw new InvalidDataException($"Unsupported HKX packfile version {version} (expected 11 for FO4).");

        const int SecHdrBase = 0x50;
        const int SecHdrStride = 0x40;

        var sections = new Dictionary<string, SectionInfo>(StringComparer.Ordinal);
        for (int i = 0; i < 3; i++)
        {
            int b = SecHdrBase + i * SecHdrStride;
            if (b + 0x30 > data.Length) break;
            string name = ReadNullTermString(data, b, 16);
            int ds    = ReadI32(data, b + 0x14);
            int lf    = ReadI32(data, b + 0x18);
            int gf    = ReadI32(data, b + 0x1C);
            int vf    = ReadI32(data, b + 0x20);
            int exp   = ReadI32(data, b + 0x24);
            int end   = ReadI32(data, b + 0x2C);
            sections[name] = new SectionInfo
            {
                DataStart      = ds,
                LocalFixupAbs  = ds + lf,
                GlobalFixupAbs = ds + gf,
                VirtualFixupAbs= ds + vf,
                ExportsAbs     = ds + exp,
                End            = ds + end,
            };
        }

        if (!sections.TryGetValue("__classnames__", out var cnSec))
            throw new InvalidDataException("Missing __classnames__ section.");
        if (!sections.TryGetValue("__data__", out var dataSec))
            throw new InvalidDataException("Missing __data__ section.");

        int cnStart = cnSec.DataStart;
        int dataAbs = dataSec.DataStart;

        var fixups = ParseLocalFixups(data, dataSec);

        var objectClasses = ParseVirtualFixups(data, dataSec, cnStart);

        var result = new HkxAnimationData();

        var skelOffsets = new List<int>();
        int posSkel = dataSec.VirtualFixupAbs;
        int endSkel = dataSec.ExportsAbs;
        while (posSkel + 12 <= endSkel && posSkel + 12 <= data.Length)
        {
            int src = ReadI32(data, posSkel);
            int nameOff = ReadI32(data, posSkel + 8);
            if (src == unchecked((int)0xFFFFFFFF)) break;
            string cls = ReadNullTermString(data, cnStart + nameOff, 256);
            if (cls == "hkaSkeleton")
            {
                skelOffsets.Add(src);
            }
            posSkel += 12;
        }

        HkxSkeleton? bestSkel = null;
        foreach (int skelRelOff in skelOffsets)
        {
            var parsed = ParseSkeleton(data, dataAbs, skelRelOff, dataSec.End, fixups);
            if (parsed != null)
            {
                if (bestSkel == null || parsed.BoneNames.Count > bestSkel.BoneNames.Count)
                {
                    bestSkel = parsed;
                }
            }
        }

        if (bestSkel != null)
        {
            result.Skeleton = bestSkel;
            result.BoneNames = new List<string>(bestSkel.BoneNames);
        }

        var byOffset = ParseObjectOffsets(data, dataSec, cnStart);
        int animRel = -1;

        if (objectClasses.TryGetValue("hkaAnimationBinding", out int boundRel))
        {
            var pointers = ParseGlobalFixups(data, dataSec);
            if (pointers.TryGetValue(boundRel + 0x18, out int target) && byOffset.ContainsKey(target))
                animRel = target;
        }

        if (animRel < 0)
            foreach (string wanted in HkxAnimationData.DecodedAnimationClasses)
                if (objectClasses.TryGetValue(wanted, out int found)) { animRel = found; break; }

        result.AnimationClass = animRel >= 0 && byOffset.TryGetValue(animRel, out string? bound)
            ? bound
            : objectClasses.Keys.FirstOrDefault(
                  c => c.StartsWith("hka", StringComparison.Ordinal) &&
                       c.EndsWith("Animation", StringComparison.Ordinal)) ?? "";

        switch (result.AnimationClass)
        {
            case "hkaSplineCompressedAnimation":
                ParseSplineAnimation(data, dataAbs, animRel, fixups, result);
                break;
            case "hkaLosslessCompressedAnimation":
                ParseLosslessAnimation(data, dataAbs, animRel, fixups, result);
                break;
            case "hkaInterleavedUncompressedAnimation":
                ParseInterleavedAnimation(data, dataAbs, animRel, fixups, result);
                break;
            default:
                if (result.Skeleton != null) result.OriginalSkeletonName = result.Skeleton.Name;
                break;
        }

        if (objectClasses.TryGetValue("hkaAnimationBinding", out int bindRel))
        {
            ParseAnimationBinding(data, dataAbs, bindRel, fixups, result);
        }

        return result;
    }

    private static Dictionary<int, int> ParseLocalFixups(byte[] data, SectionInfo sec)
    {
        var map = new Dictionary<int, int>();
        int pos = sec.LocalFixupAbs;
        int end = sec.GlobalFixupAbs;
        while (pos + 8 <= end && pos + 8 <= data.Length)
        {
            int src = ReadI32(data, pos);
            int dst = ReadI32(data, pos + 4);
            if (src == unchecked((int)0xFFFFFFFF)) break;
            map[src] = dst;
            pos += 8;
        }
        return map;
    }

    private static Dictionary<int, string> ParseObjectOffsets(byte[] data, SectionInfo sec, int cnStart)
    {
        var map = new Dictionary<int, string>();
        int pos = sec.VirtualFixupAbs;
        int end = sec.ExportsAbs;
        while (pos + 12 <= end && pos + 12 <= data.Length)
        {
            int src = ReadI32(data, pos);
            int nameOff = ReadI32(data, pos + 8);
            if (src == unchecked((int)0xFFFFFFFF)) break;
            map[src] = ReadNullTermString(data, cnStart + nameOff, 256);
            pos += 12;
        }
        return map;
    }

    private static Dictionary<int, int> ParseGlobalFixups(byte[] data, SectionInfo sec)
    {
        var map = new Dictionary<int, int>();
        int pos = sec.GlobalFixupAbs;
        int end = sec.VirtualFixupAbs;
        while (pos + 12 <= end && pos + 12 <= data.Length)
        {
            int src = ReadI32(data, pos);
            int dst = ReadI32(data, pos + 8);
            if (src == unchecked((int)0xFFFFFFFF)) { pos += 12; continue; }
            map[src] = dst;
            pos += 12;
        }
        return map;
    }

    private static Dictionary<string, int> ParseVirtualFixups(byte[] data, SectionInfo sec, int cnStart)
    {

        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        int pos = sec.VirtualFixupAbs;
        int end = sec.ExportsAbs;
        while (pos + 12 <= end && pos + 12 <= data.Length)
        {
            int src     = ReadI32(data, pos);
            int nameOff = ReadI32(data, pos + 8);
            if (src == unchecked((int)0xFFFFFFFF)) break;
            string cls = ReadNullTermString(data, cnStart + nameOff, 256);
            if (!string.IsNullOrEmpty(cls) && !map.ContainsKey(cls))
                map[cls] = src;
            pos += 12;
        }
        return map;
    }

    #endregion

}
