using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace OpenCommonwealth.Services.Hkx;


























public class HkxBinaryReader
{
    private static readonly byte[] HkxMagic = new byte[] { 0x57, 0xE0, 0xE0, 0x57 };

    #region TrackMask
    private sealed class TrackMask
    {
        public readonly int PosQuant;
        public readonly int RotQuant;
        public readonly int ScaleQuant;
        public readonly byte PosFlags;
        public readonly byte RotFlags;
        public readonly byte ScaleFlags;

        public TrackMask(byte b0, byte b1, byte b2, byte b3)
        {
            PosQuant   = b0 & 0x03;
            RotQuant   = (b0 >> 2) & 0x0F;
            ScaleQuant = (b0 >> 6) & 0x03;
            PosFlags   = b1;
            RotFlags   = b2;
            ScaleFlags = b3;
        }

        public string GetPosType(int axis)
        {
            if (((PosFlags >> (axis + 4)) & 1) != 0) return "spline";
            if (((PosFlags >> axis) & 1) != 0) return "static";
            return "identity";
        }

        public string GetRotType()
        {
            if (((RotFlags >> 4) & 0x0F) != 0) return "spline";
            if ((RotFlags & 0x0F) != 0) return "static";
            return "identity";
        }

        public string GetScaleType(int axis)
        {
            if (((ScaleFlags >> (axis + 4)) & 1) != 0) return "spline";
            if (((ScaleFlags >> axis) & 1) != 0) return "static";
            return "identity";
        }

        public bool HasAnyPosSpline()
        {
            for (int a = 0; a < 3; a++) if (GetPosType(a) == "spline") return true;
            return false;
        }

        public bool HasAnyScaleSpline()
        {
            for (int a = 0; a < 3; a++) if (GetScaleType(a) == "spline") return true;
            return false;
        }
    }
    #endregion

    #region Public API

    private const int Fo4FileVersion = 11;
    private const string Fo4ContentsVersion = "hk_2014.1.0-r1";

    public static bool IsFo4Hkx(string filepath)
    {
        if (!File.Exists(filepath)) return false;
        try
        {
            byte[] hdr = new byte[0x40];
            using (var fs = File.OpenRead(filepath))
            {
                int got = 0;
                while (got < hdr.Length)
                {
                    int n = fs.Read(hdr, got, hdr.Length - got);
                    if (n <= 0) break;
                    got += n;
                }
                if (got < hdr.Length) return false;
            }
            return hdr[0] == HkxMagic[0] && hdr[1] == HkxMagic[1] &&
                   hdr[2] == HkxMagic[2] && hdr[3] == HkxMagic[3] &&
                   hdr[4] == 0x10 && hdr[5] == 0xC0 && hdr[6] == 0xC0 && hdr[7] == 0x10 &&
                   hdr[0x0C] == Fo4FileVersion && hdr[0x0D] == 0 && hdr[0x0E] == 0 && hdr[0x0F] == 0 &&
                   HasContentsVersion(hdr, Fo4ContentsVersion);
        }
        catch { return false; }
    }

    private static bool HasContentsVersion(byte[] hdr, string expected)
    {
        var want = Encoding.ASCII.GetBytes(expected);
        for (int i = 0; i < want.Length; i++)
            if (hdr[0x28 + i] != want[i]) return false;
        return true;
    }




    public HkxAnimationData ReadAnimation(string filepath)
    {
        byte[] data = File.ReadAllBytes(filepath);
        var parsed = ParseHkx(data);

        if (parsed.HasUnsupportedAnimation)
            throw new NotSupportedException(
                $"unsupported animation class: {parsed.AnimationClass}. " +
                $"Only {HkxAnimationData.SupportedAnimationClasses} are decoded, so no frame data was read from " +
                Path.GetFileName(filepath));

        return parsed;
    }




    public bool TryReadAnimation(string filepath, out HkxAnimationData data)
    {
        data = ParseHkx(File.ReadAllBytes(filepath));
        return !data.HasUnsupportedAnimation;
    }

    public HkxSkeleton ReadSkeleton(string filepath)
    {
        byte[] data = File.ReadAllBytes(filepath);
        var anim = ParseHkx(data);
        if (anim.Skeleton != null) return anim.Skeleton;
        throw new InvalidDataException($"No skeleton (hkaSkeleton) found in: {filepath}");
    }

    #endregion

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

    private HkxAnimationData ParseHkx(byte[] data)
    {
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
            var parsed = ParseSkeleton(data, dataAbs, skelRelOff, fixups);
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

    #region Skeleton Parsing

    private static HkxSkeleton? ParseSkeleton(byte[] data, int dataAbs, int skelRel, Dictionary<int, int> fixups)
    {







        int a = dataAbs + skelRel;
        if (a + 0x50 > data.Length) return null;

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
        if (fixups.TryGetValue(skelRel + 0x28, out int bonesDataRel))
        {
            int bonesAbs = dataAbs + bonesDataRel;
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

    #endregion

    #region Spline Animation Parsing

    private static void ParseSplineAnimation(byte[] data, int dataAbs, int animRel,
        Dictionary<int, int> fixups, HkxAnimationData anim)
    {
        int a = dataAbs + animRel;
        if (a + 0xB0 > data.Length) return;

        anim.Duration           = ReadF32(data, a + 0x14);
        anim.NumTracks          = SafeReadI32(data, a + 0x18);
        anim.NumFrames          = SafeReadI32(data, a + 0x38);
        anim.NumBlocks          = SafeReadI32(data, a + 0x3C);
        anim.MaxFramesPerBlock  = SafeReadI32(data, a + 0x40);
        int maskAndQuantSize    = SafeReadI32(data, a + 0x44);
        anim.BlockDuration      = ReadF32(data, a + 0x48);
        anim.FrameDuration      = ReadF32(data, a + 0x50);

        if (maskAndQuantSize == 0)
            maskAndQuantSize = Align(4 * anim.NumTracks, 4);
        if (anim.NumFrames == 0 && anim.FrameDuration > 0 && anim.Duration > 0)
            anim.NumFrames = (int)Math.Round(anim.Duration / anim.FrameDuration) + 1;


        int blockOffsetsCount = SafeReadI32(data, a + 0x60);
        List<int> blockOffsets = new();
        if (fixups.TryGetValue(animRel + 0x58, out int boRel))
        {
            int boAbs = dataAbs + boRel;
            for (int i = 0; i < blockOffsetsCount && boAbs + i * 4 + 4 <= data.Length; i++)
                blockOffsets.Add(SafeReadI32(data, boAbs + i * 4));
        }


        int blobCount = SafeReadI32(data, a + 0xA0);
        int blobAbs = -1;
        if (fixups.TryGetValue(animRel + 0x98, out int blobRel))
            blobAbs = dataAbs + blobRel;


        ParseAnnotationTracks(data, dataAbs, animRel + 0x28, fixups, anim);


        if (blobAbs > 0 && blockOffsets.Count > 0 && anim.NumTracks > 0 && anim.NumFrames > 0)
        {
            DecompressSpline(data, blobAbs, blobCount, anim.NumTracks, anim.NumFrames,
                anim.NumBlocks, anim.MaxFramesPerBlock, blockOffsets, maskAndQuantSize, anim);
        }
    }














    private static void ParseInterleavedAnimation(byte[] data, int dataAbs, int animRel,
        Dictionary<int, int> fixups, HkxAnimationData anim)
    {
        int a = dataAbs + animRel;
        if (a + 0x48 > data.Length) return;

        anim.Duration = ReadF32(data, a + 0x14);
        anim.NumTracks = SafeReadI32(data, a + 0x18);

        int transforms = SafeReadI32(data, a + 0x40);
        if (anim.NumTracks <= 0 || transforms <= 0) return;

        anim.NumFrames = transforms / anim.NumTracks;
        anim.NumBlocks = 1;
        anim.MaxFramesPerBlock = anim.NumFrames;
        anim.BlockDuration = anim.Duration;
        if (anim.NumFrames > 1 && anim.Duration > 0)
            anim.FrameDuration = anim.Duration / (anim.NumFrames - 1);

        ParseAnnotationTracks(data, dataAbs, animRel + 0x28, fixups, anim);

        if (!fixups.TryGetValue(animRel + 0x38, out int runRel)) return;
        int run = dataAbs + runRel;

        for (int t = 0; t < anim.NumTracks; t++)
        {


            var track = new HkxTrackData { RotationAnimated = true };
            for (int c = 0; c < 3; c++)
            {
                track.TranslationAnimated[c] = true;
                track.ScaleAnimated[c] = true;
            }

            for (int f = 0; f < anim.NumFrames; f++)
            {
                int p = run + (f * anim.NumTracks + t) * QsTransformSize;
                if (p + QsTransformSize > data.Length) break;

                track.Translations.Add(new Vector3(ReadF32(data, p), ReadF32(data, p + 4), ReadF32(data, p + 8)));
                track.Rotations.Add(new Quaternion(ReadF32(data, p + 16), ReadF32(data, p + 20),
                                                   ReadF32(data, p + 24), ReadF32(data, p + 28)));
                track.Scales.Add(new Vector3(ReadF32(data, p + 32), ReadF32(data, p + 36), ReadF32(data, p + 40)));
            }
            anim.Tracks.Add(track);
        }
    }



    public const int QsTransformSize = 48;

    private static void ParseAnnotationTracks(byte[] data, int dataAbs, int arrRel,
        Dictionary<int, int> fixups, HkxAnimationData anim)
    {
        int count = SafeReadI32(data, dataAbs + arrRel + 8);
        if (count <= 0) return;
        if (!fixups.TryGetValue(arrRel, out int contentRel)) return;

        const int AnnotTrackStride = 0x18;
        for (int i = 0; i < count; i++)
        {
            int trackRel = contentRel + i * AnnotTrackStride;
            string name = "";
            if (fixups.TryGetValue(trackRel, out int nameRel))
                name = ReadNullTermString(data, dataAbs + nameRel, 256);
            anim.BoneNames.Add(name);


            int evtCount = SafeReadI32(data, dataAbs + trackRel + 0x10);
            if (evtCount > 0 && fixups.TryGetValue(trackRel + 0x08, out int evtRel))
            {
                for (int j = 0; j < evtCount; j++)
                {
                    int e = dataAbs + evtRel + j * 0x10;
                    if (e + 0x10 > data.Length) break;
                    float time = ReadF32(data, e);
                    string text = "";
                    if (fixups.TryGetValue(evtRel + j * 0x10 + 0x08, out int txtRel))
                        text = ReadNullTermString(data, dataAbs + txtRel, 256);
                    anim.Annotations.Add(new HkxAnnotation { Time = time, Text = text });
                }
            }
        }
    }









    private const int LosslessDuration = 20, LosslessTransformTracks = 24, LosslessNumFrames = 216;






    private const int AnimationAnnotationTracks = 0x28;
    private const int LosslessDynamicTranslations = 56, LosslessStaticTranslations = 72, LosslessTranslationWords = 88;
    private const int LosslessDynamicRotations = 104, LosslessStaticRotations = 120, LosslessRotationWords = 136;
    private const int LosslessDynamicScales = 152, LosslessStaticScales = 168, LosslessScaleWords = 184;

    private const int TrackClear = 0, TrackStatic = 1, TrackDynamic = 2;

    private static void ParseLosslessAnimation(byte[] data, int dataAbs, int animRel,
        Dictionary<int, int> fixups, HkxAnimationData anim)
    {
        int a = dataAbs + animRel;
        if (a + LosslessNumFrames + 4 > data.Length) return;

        anim.Duration  = ReadF32(data, a + LosslessDuration);
        anim.NumTracks = SafeReadI32(data, a + LosslessTransformTracks);
        anim.NumFrames = SafeReadI32(data, a + LosslessNumFrames);
        if (anim.NumFrames <= 0 || anim.NumTracks <= 0) return;

        anim.NumBlocks = 1;
        anim.MaxFramesPerBlock = anim.NumFrames;
        anim.BlockDuration = anim.Duration;
        if (anim.NumFrames > 1 && anim.Duration > 0)
            anim.FrameDuration = anim.Duration / (anim.NumFrames - 1);

        ParseAnnotationTracks(data, dataAbs, animRel + AnimationAnnotationTracks, fixups, anim);

        var dynamicT = ReadFloats(data, dataAbs, animRel + LosslessDynamicTranslations, fixups);
        var staticT  = ReadFloats(data, dataAbs, animRel + LosslessStaticTranslations, fixups);
        var wordsT   = ReadWords64(data, dataAbs, animRel + LosslessTranslationWords, fixups);
        var dynamicR = ReadQuaternions(data, dataAbs, animRel + LosslessDynamicRotations, fixups);
        var staticR  = ReadQuaternions(data, dataAbs, animRel + LosslessStaticRotations, fixups);
        var wordsR   = ReadWords16(data, dataAbs, animRel + LosslessRotationWords, fixups);
        var dynamicS = ReadFloats(data, dataAbs, animRel + LosslessDynamicScales, fixups);
        var staticS  = ReadFloats(data, dataAbs, animRel + LosslessStaticScales, fixups);
        var wordsS   = ReadWords64(data, dataAbs, animRel + LosslessScaleWords, fixups);

        int frames = anim.NumFrames;




        int strideT = dynamicT.Count / frames;
        int strideR = dynamicR.Count / frames;
        int strideS = dynamicS.Count / frames;

        for (int t = 0; t < anim.NumTracks; t++)
        {
            ulong wordT = t < wordsT.Count ? wordsT[t] : 0;
            ulong wordR = t < wordsR.Count ? wordsR[t] : 0;
            ulong wordS = t < wordsS.Count ? wordsS[t] : 0;

            var track = new HkxTrackData();
            for (int c = 0; c < 3; c++)
            {
                track.TranslationAnimated[c] = LosslessType(wordT, c) != TrackClear;
                track.ScaleAnimated[c] = LosslessType(wordS, c) != TrackClear;
            }
            track.RotationAnimated = LosslessType(wordR, 0) != TrackClear;

            for (int f = 0; f < frames; f++)
            {
                track.Translations.Add(new Vector3(
                    LosslessValue(wordT, 0, f, strideT, dynamicT, staticT, 0f),
                    LosslessValue(wordT, 1, f, strideT, dynamicT, staticT, 0f),
                    LosslessValue(wordT, 2, f, strideT, dynamicT, staticT, 0f)));

                track.Scales.Add(new Vector3(
                    LosslessValue(wordS, 0, f, strideS, dynamicS, staticS, 1f),
                    LosslessValue(wordS, 1, f, strideS, dynamicS, staticS, 1f),
                    LosslessValue(wordS, 2, f, strideS, dynamicS, staticS, 1f)));

                track.Rotations.Add(LosslessRotation(wordR, f, strideR, dynamicR, staticR));
            }
            anim.Tracks.Add(track);
        }
    }












    public static int LosslessField(ulong word, int component) => (int)((word >> (component * 16)) & 0xFFFF);

    public static int LosslessOffset(ulong word, int component) => (LosslessField(word, component) >> 2) & 0x3FFF;

    public static int LosslessType(ulong word, int component) => LosslessField(word, component) & 3;




    public static float LosslessValue(ulong word, int component, int frame, int stride,
                                      List<float> dynamic, List<float> constant, float fallback)
    {
        int offset = LosslessOffset(word, component);

        switch (LosslessType(word, component))
        {
            case TrackStatic:
                return offset < constant.Count ? constant[offset] : fallback;
            case TrackDynamic:
                int index = offset + frame * stride;
                return index >= 0 && index < dynamic.Count ? dynamic[index] : fallback;
            default:
                return fallback;
        }
    }

    private static Quaternion LosslessRotation(ulong word, int frame, int stride,
                                               List<Quaternion> dynamic, List<Quaternion> constant)
    {
        int field = LosslessField(word, 0);
        int offset = (field >> 2) & 0x3FFF;

        switch (field & 3)
        {
            case TrackStatic:
                return offset < constant.Count ? constant[offset] : Quaternion.Identity;
            case TrackDynamic:
                int index = offset + frame * stride;
                return index >= 0 && index < dynamic.Count ? dynamic[index] : Quaternion.Identity;
            default:
                return Quaternion.Identity;
        }
    }


    private static int ArrayAt(byte[] data, int dataAbs, int memberRel,
                               Dictionary<int, int> fixups, out int count)
    {
        count = SafeReadI32(data, dataAbs + memberRel + 8);
        if (count <= 0 || !fixups.TryGetValue(memberRel, out int contentRel)) { count = 0; return 0; }
        return dataAbs + contentRel;
    }

    private static List<float> ReadFloats(byte[] data, int dataAbs, int memberRel, Dictionary<int, int> fixups)
    {
        int at = ArrayAt(data, dataAbs, memberRel, fixups, out int count);
        var list = new List<float>(count);
        for (int i = 0; i < count; i++) list.Add(ReadF32(data, at + i * 4));
        return list;
    }

    private static List<Quaternion> ReadQuaternions(byte[] data, int dataAbs, int memberRel, Dictionary<int, int> fixups)
    {
        int at = ArrayAt(data, dataAbs, memberRel, fixups, out int count);
        var list = new List<Quaternion>(count);
        for (int i = 0; i < count; i++)
        {
            int p = at + i * 16;
            list.Add(new Quaternion(ReadF32(data, p), ReadF32(data, p + 4), ReadF32(data, p + 8), ReadF32(data, p + 12)));
        }
        return list;
    }

    private static List<ulong> ReadWords64(byte[] data, int dataAbs, int memberRel, Dictionary<int, int> fixups)
    {
        int at = ArrayAt(data, dataAbs, memberRel, fixups, out int count);
        var list = new List<ulong>(count);
        for (int i = 0; i < count; i++)
            list.Add(CanRead(data, at + i * 8, 8) ? BitConverter.ToUInt64(data, at + i * 8) : 0UL);
        return list;
    }

    private static List<ulong> ReadWords16(byte[] data, int dataAbs, int memberRel, Dictionary<int, int> fixups)
    {
        int at = ArrayAt(data, dataAbs, memberRel, fixups, out int count);
        var list = new List<ulong>(count);
        for (int i = 0; i < count; i++)
            list.Add(CanRead(data, at + i * 2, 2) ? BitConverter.ToUInt16(data, at + i * 2) : (ulong)0);
        return list;
    }

    private static void ParseAnimationBinding(byte[] data, int dataAbs, int bindRel,
        Dictionary<int, int> fixups, HkxAnimationData anim)
    {


        if (fixups.TryGetValue(bindRel + 0x10, out int nameRel))
            anim.OriginalSkeletonName = ReadNullTermString(data, dataAbs + nameRel, 256);

        int count = SafeReadI32(data, dataAbs + bindRel + 0x28);
        if (count > 0 && fixups.TryGetValue(bindRel + 0x20, out int idxRel))
        {
            int abs = dataAbs + idxRel;
            for (int i = 0; i < count && abs + i * 2 + 2 <= data.Length; i++)
                anim.TrackToBoneIndices.Add(BitConverter.ToInt16(data, abs + i * 2));
        }
    }

    #endregion

    #region Spline Decompression

    private static void DecompressSpline(byte[] data, int blobAbs, int blobLen,
        int numTracks, int numFrames, int numBlocks, int maxFramesPerBlock,
        List<int> blockOffsets, int maskAndQuantSize, HkxAnimationData anim)
    {
        for (int i = 0; i < numTracks; i++)
            anim.Tracks.Add(new HkxTrackData());

        for (int blockIdx = 0; blockIdx < numBlocks && blockIdx < blockOffsets.Count; blockIdx++)
        {
            int blockStart = blobAbs + blockOffsets[blockIdx];
            int firstFrame = blockIdx * maxFramesPerBlock;
            int framesInBlock = (blockIdx == numBlocks - 1) ? (numFrames - firstFrame) : maxFramesPerBlock;
            if (framesInBlock <= 0) continue;


            if (!CanRead(data, blockStart, 4 * numTracks)) continue;

            var masks = new TrackMask[numTracks];
            for (int t = 0; t < numTracks; t++)
            {
                int mOff = blockStart + t * 4;
                masks[t] = new TrackMask(data[mOff], data[mOff + 1], data[mOff + 2], data[mOff + 3]);
            }

            int off = blockStart + maskAndQuantSize;

            for (int ti = 0; ti < numTracks; ti++)
            {
                var mask  = masks[ti];
                var track = anim.Tracks[ti];





                for (int axis = 0; axis < 3; axis++)
                {
                    if (mask.GetPosType(axis) != "identity") track.TranslationAnimated[axis] = true;
                    if (mask.GetScaleType(axis) != "identity") track.ScaleAnimated[axis] = true;
                }
                if (mask.GetRotType() != "identity") track.RotationAnimated = true;


                var posFrames = new List<Vector3>(framesInBlock);
                if (mask.HasAnyPosSpline())
                {
                    if (!CanRead(data, off, 3)) goto SkipPos;
                    ushort numItems = BitConverter.ToUInt16(data, off);
                    byte degree = data[off + 2];
                    off += 3;
                    int numKnots = numItems + degree + 2;
                    if (!CanRead(data, off, numKnots)) goto SkipPos;
                    float[] knots = new float[numKnots];
                    for (int k = 0; k < numKnots; k++) knots[k] = data[off + k];
                    off += numKnots;
                    off = Align(off, 4);

                    var axisInfo = new (string Type, float Min, float Max)[3];
                    for (int axis = 0; axis < 3; axis++)
                    {
                        string pt = mask.GetPosType(axis);
                        if (pt == "spline")
                        {
                            if (!CanRead(data, off, 8)) goto SkipPos;
                            float mn = ReadF32(data, off); off += 4;
                            float mx = ReadF32(data, off); off += 4;
                            axisInfo[axis] = ("spline", mn, mx);
                        }
                        else if (pt == "static")
                        {
                            if (!CanRead(data, off, 4)) goto SkipPos;
                            float v = ReadF32(data, off); off += 4;
                            axisInfo[axis] = ("static", v, v);
                        }
                        else axisInfo[axis] = ("identity", 0f, 0f);
                    }

                    var cps = new List<float>[3] { new(), new(), new() };
                    for (int item = 0; item <= numItems; item++)
                    {
                        for (int axis = 0; axis < 3; axis++)
                        {
                            if (axisInfo[axis].Type == "spline")
                            {
                                int need = mask.PosQuant == 0 ? 1 : 2;
                                if (!CanRead(data, off, need)) goto SkipPos;
                                float v = mask.PosQuant == 0
                                    ? Read8BitScalar(data, ref off, axisInfo[axis].Min, axisInfo[axis].Max)
                                    : Read16BitScalar(data, ref off, axisInfo[axis].Min, axisInfo[axis].Max);
                                cps[axis].Add(v);
                            }
                        }
                    }
                    off = Align(off, 4);

                    for (int f = 0; f < framesInBlock; f++)
                    {
                        float ft = f;
                        Vector3 pos = Vector3.Zero;
                        for (int axis = 0; axis < 3; axis++)
                        {
                            var info = axisInfo[axis];
                            if (info.Type == "spline" && cps[axis].Count > 0)
                            {
                                int span = FindKnotSpan(degree, ft, cps[axis].Count, knots);
                                float v  = EvalBSplineScalar(span, degree, ft, knots, cps[axis]);
                                if (axis == 0) pos.X = v; else if (axis == 1) pos.Y = v; else pos.Z = v;
                            }
                            else if (info.Type == "static")
                            {
                                if (axis == 0) pos.X = info.Min; else if (axis == 1) pos.Y = info.Min; else pos.Z = info.Min;
                            }
                        }
                        posFrames.Add(pos);
                    }
                    goto AfterPos;
                SkipPos:
                    for (int f = 0; f < framesInBlock; f++) posFrames.Add(Vector3.Zero);
                }
                else
                {
                    Vector3 pos = Vector3.Zero;
                    for (int axis = 0; axis < 3; axis++)
                    {
                        if (mask.GetPosType(axis) == "static" && CanRead(data, off, 4))
                        {
                            float v = ReadF32(data, off); off += 4;
                            if (axis == 0) pos.X = v; else if (axis == 1) pos.Y = v; else pos.Z = v;
                        }
                    }
                    for (int f = 0; f < framesInBlock; f++) posFrames.Add(pos);
                }
            AfterPos:
                off = Align(off, 4);
                track.Translations.AddRange(posFrames);


                var rotFrames = new List<Quaternion>(framesInBlock);
                string rotType = mask.GetRotType();
                int qfmt   = mask.RotQuant;
                int qalign = qfmt == 1 || qfmt == 3 ? 1 : (qfmt == 2 || qfmt == 4 ? 2 : 4);

                if (rotType == "spline")
                {
                    if (!CanRead(data, off, 3)) goto SkipRot;
                    ushort numItems = BitConverter.ToUInt16(data, off);
                    byte degree = data[off + 2];
                    off += 3;
                    int numKnots = numItems + degree + 2;
                    if (!CanRead(data, off, numKnots)) goto SkipRot;
                    float[] knots = new float[numKnots];
                    for (int k = 0; k < numKnots; k++) knots[k] = data[off + k];
                    off += numKnots;
                    if (qalign > 1) off = Align(off, qalign);

                    var quatCps = new List<Quaternion>();
                    for (int item = 0; item <= numItems; item++)
                    {
                        Quaternion q = ReadQuat(qfmt, data, ref off);
                        if (quatCps.Count > 0 && Quaternion.Dot(q, quatCps[^1]) < 0) q = -q;
                        quatCps.Add(q);
                    }
                    for (int f = 0; f < framesInBlock; f++)
                    {
                        float ft   = f;
                        int span   = FindKnotSpan(degree, ft, quatCps.Count, knots);
                        Quaternion q = EvalBSplineQuat(span, degree, ft, knots, quatCps);
                        rotFrames.Add(Quaternion.Normalize(q));
                    }
                    goto AfterRot;
                SkipRot:
                    for (int f = 0; f < framesInBlock; f++) rotFrames.Add(Quaternion.Identity);
                }
                else if (rotType == "static")
                {
                    if (qalign > 1) off = Align(off, qalign);
                    Quaternion q = ReadQuat(qfmt, data, ref off);
                    for (int f = 0; f < framesInBlock; f++) rotFrames.Add(q);
                }
                else
                {
                    for (int f = 0; f < framesInBlock; f++) rotFrames.Add(Quaternion.Identity);
                }
            AfterRot:
                off = Align(off, 4);
                track.Rotations.AddRange(rotFrames);


                var scaleFrames = new List<Vector3>(framesInBlock);
                if (mask.HasAnyScaleSpline())
                {
                    if (!CanRead(data, off, 3)) goto SkipScale;
                    ushort numItems = BitConverter.ToUInt16(data, off);
                    byte degree = data[off + 2];
                    off += 3;
                    int numKnots = numItems + degree + 2;
                    if (!CanRead(data, off, numKnots)) goto SkipScale;
                    float[] knots = new float[numKnots];
                    for (int k = 0; k < numKnots; k++) knots[k] = data[off + k];
                    off += numKnots;
                    off = Align(off, 4);

                    var axisInfo = new (string Type, float Min, float Max)[3];
                    for (int axis = 0; axis < 3; axis++)
                    {
                        string st = mask.GetScaleType(axis);
                        if (st == "spline")
                        {
                            if (!CanRead(data, off, 8)) goto SkipScale;
                            float mn = ReadF32(data, off); off += 4;
                            float mx = ReadF32(data, off); off += 4;
                            axisInfo[axis] = ("spline", mn, mx);
                        }
                        else if (st == "static")
                        {
                            if (!CanRead(data, off, 4)) goto SkipScale;
                            float v = ReadF32(data, off); off += 4;
                            axisInfo[axis] = ("static", v, v);
                        }
                        else axisInfo[axis] = ("identity", 1f, 1f);
                    }

                    var cps = new List<float>[3] { new(), new(), new() };
                    for (int item = 0; item <= numItems; item++)
                    {
                        for (int axis = 0; axis < 3; axis++)
                        {
                            if (axisInfo[axis].Type == "spline")
                            {
                                int need = mask.ScaleQuant == 0 ? 1 : 2;
                                if (!CanRead(data, off, need)) goto SkipScale;
                                float v = mask.ScaleQuant == 0
                                    ? Read8BitScalar(data, ref off, axisInfo[axis].Min, axisInfo[axis].Max)
                                    : Read16BitScalar(data, ref off, axisInfo[axis].Min, axisInfo[axis].Max);
                                cps[axis].Add(v);
                            }
                        }
                    }
                    off = Align(off, 4);

                    for (int f = 0; f < framesInBlock; f++)
                    {
                        float ft = f;
                        Vector3 sc = Vector3.One;
                        for (int axis = 0; axis < 3; axis++)
                        {
                            var info = axisInfo[axis];
                            if (info.Type == "spline" && cps[axis].Count > 0)
                            {
                                int span = FindKnotSpan(degree, ft, cps[axis].Count, knots);
                                float v  = EvalBSplineScalar(span, degree, ft, knots, cps[axis]);
                                if (axis == 0) sc.X = v; else if (axis == 1) sc.Y = v; else sc.Z = v;
                            }
                            else if (info.Type == "static")
                            {
                                if (axis == 0) sc.X = info.Min; else if (axis == 1) sc.Y = info.Min; else sc.Z = info.Min;
                            }
                        }
                        scaleFrames.Add(sc);
                    }
                    goto AfterScale;
                SkipScale:
                    for (int f = 0; f < framesInBlock; f++) scaleFrames.Add(Vector3.One);
                }
                else
                {
                    Vector3 sc = Vector3.One;
                    for (int axis = 0; axis < 3; axis++)
                    {
                        if (mask.GetScaleType(axis) == "static" && CanRead(data, off, 4))
                        {
                            float v = ReadF32(data, off); off += 4;
                            if (axis == 0) sc.X = v; else if (axis == 1) sc.Y = v; else sc.Z = v;
                        }
                    }
                    for (int f = 0; f < framesInBlock; f++) scaleFrames.Add(sc);
                }
            AfterScale:
                off = Align(off, 4);
                track.Scales.AddRange(scaleFrames);
            }
        }
    }

    #endregion

    #region Quaternion Decompression

    private static Quaternion ReadQuat(int fmt, byte[] data, ref int off)
    {
        return fmt switch
        {
            0 => Read32BitQuat(data, ref off),
            1 => Read40BitQuat(data, ref off),
            2 => Read48BitQuat(data, ref off),
            5 => ReadUncompressedQuat(data, ref off),
            _ => Read40BitQuat(data, ref off),
        };
    }

    private static Quaternion Read32BitQuat(byte[] data, ref int off)
    {
        if (!CanRead(data, off, 4)) return Quaternion.Identity;
        uint cv = BitConverter.ToUInt32(data, off); off += 4;
        float rFrac = 1.0f / ((1 << 10) - 1);
        float R = ((cv >> 18) & 0x3FFu) * rFrac;
        R = 1f - R * R;
        float pt = cv & 0x3FFFFu;
        float phi = MathF.Floor(MathF.Sqrt(pt));
        float theta = 0f;
        if (phi > 0) { theta = MathF.PI / 4f * (pt - phi * phi) / phi; phi = MathF.PI / 2f / 511f * phi; }
        float mag = MathF.Sqrt(MathF.Max(0, 1 - R * R));
        float sp = MathF.Sin(phi), cp = MathF.Cos(phi), st = MathF.Sin(theta), ct = MathF.Cos(theta);
        float[] r = { sp * ct * mag, sp * st * mag, cp * mag, R };
        uint[] sm = { 0x10000000u, 0x20000000u, 0x40000000u, 0x80000000u };
        for (int i = 0; i < 4; i++) if ((cv & sm[i]) != 0) r[i] = -r[i];
        return Quaternion.Normalize(new Quaternion(r[0], r[1], r[2], r[3]));
    }

    private static Quaternion Read40BitQuat(byte[] data, ref int off)
    {
        if (!CanRead(data, off, 5)) return Quaternion.Identity;
        const float FRACTAL = 0.000345436f;
        ulong raw = 0;
        for (int i = 0; i < 5; i++) raw |= (ulong)data[off + i] << (i * 8);
        off += 5;
        float v0 = ((long)((raw >> 0)  & 0xFFF) - 2049) * FRACTAL;
        float v1 = ((long)((raw >> 12) & 0xFFF) - 2049) * FRACTAL;
        float v2 = ((long)((raw >> 24) & 0xFFF) - 2049) * FRACTAL;
        float w = MathF.Sqrt(MathF.Max(0, 1 - v0*v0 - v1*v1 - v2*v2));
        if (((raw >> 38) & 1) != 0) w = -w;
        return Quaternion.Normalize(((raw >> 36) & 3) switch
        {
            0 => new Quaternion(w,  v0, v1, v2),
            1 => new Quaternion(v0, w,  v1, v2),
            2 => new Quaternion(v0, v1, w,  v2),
            _ => new Quaternion(v0, v1, v2, w),
        });
    }

    private static Quaternion Read48BitQuat(byte[] data, ref int off)
    {
        if (!CanRead(data, off, 6)) return Quaternion.Identity;
        const float FRACTAL = 0.000043161f;
        const int MASK = (1 << 15) - 1;
        ushort xr = BitConverter.ToUInt16(data, off);
        ushort yr = BitConverter.ToUInt16(data, off + 2);
        ushort zr = BitConverter.ToUInt16(data, off + 4);
        off += 6;
        int shift = ((yr >> 14) & 2) | ((xr >> 15) & 1);
        bool neg  = (zr >> 15) != 0;
        float v0 = ((xr & MASK) - (MASK >> 1)) * FRACTAL;
        float v1 = ((yr & MASK) - (MASK >> 1)) * FRACTAL;
        float v2 = ((zr & MASK) - (MASK >> 1)) * FRACTAL;
        float w = MathF.Sqrt(MathF.Max(0, 1 - v0*v0 - v1*v1 - v2*v2));
        if (neg) w = -w;
        return Quaternion.Normalize(shift switch
        {
            0 => new Quaternion(w,  v0, v1, v2),
            1 => new Quaternion(v0, w,  v1, v2),
            2 => new Quaternion(v0, v1, w,  v2),
            _ => new Quaternion(v0, v1, v2, w),
        });
    }

    private static Quaternion ReadUncompressedQuat(byte[] data, ref int off)
    {
        if (!CanRead(data, off, 16)) return Quaternion.Identity;
        float x = ReadF32(data, off); float y = ReadF32(data, off+4);
        float z = ReadF32(data, off+8); float w = ReadF32(data, off+12);
        off += 16;
        return Quaternion.Normalize(new Quaternion(x, y, z, w));
    }

    #endregion

    #region B-Spline Evaluation




    private static int FindKnotSpan(int degree, float t, int numCp, float[] knots) =>
        SplineFormat.FindKnotSpan(degree, t, numCp, knots);

    private static float EvalBSplineScalar(int span, int degree, float t, float[] knots, List<float> cps) =>
        SplineFormat.Evaluate(span, degree, t, knots, cps);

    private static Quaternion EvalBSplineQuat(int span, int degree, float t, float[] knots, List<Quaternion> cps) =>
        SplineFormat.Evaluate(span, degree, t, knots, cps);

    #endregion

    #region Primitive Helpers

    private static bool CanRead(byte[] data, int off, int size) =>
        off >= 0 && size >= 0 && (long)off + size <= data.Length;

    private static int Align(int v, int a) { int r = v % a; return r == 0 ? v : v + (a - r); }

    private static float ReadF32(byte[] data, int off) =>
        CanRead(data, off, 4) ? BitConverter.ToSingle(data, off) : 0f;

    private static int ReadI32(byte[] data, int off) =>
        CanRead(data, off, 4) ? BitConverter.ToInt32(data, off) : 0;

    private static int SafeReadI32(byte[] data, int off) =>
        CanRead(data, off, 4) ? BitConverter.ToInt32(data, off) : 0;

    private static float Read8BitScalar(byte[] data, ref int off, float mn, float mx)
    {
        if (!CanRead(data, off, 1)) return mn;
        float v = mn + (mx - mn) * (data[off] / 255f);
        off++;
        return v;
    }

    private static float Read16BitScalar(byte[] data, ref int off, float mn, float mx)
    {
        if (!CanRead(data, off, 2)) return mn;
        float v = mn + (mx - mn) * (BitConverter.ToUInt16(data, off) / 65535f);
        off += 2;
        return v;
    }

    private static string ReadNullTermString(byte[] data, int off, int maxLen)
    {
        if (!CanRead(data, off, 1)) return "";
        int end = off;
        int limit = Math.Min(off + maxLen, data.Length);
        while (end < limit && data[end] != 0) end++;
        return Encoding.ASCII.GetString(data, off, end - off);
    }

    #endregion
}
