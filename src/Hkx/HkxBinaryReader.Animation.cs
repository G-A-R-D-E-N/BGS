using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

public partial class HkxBinaryReader
{
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

}
