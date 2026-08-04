using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace OpenCommonwealth.Services.Hkx;

/// <summary>
/// Native C# reader for Fallout 4 Havok 2014 binary packfiles (.hkx).
/// Matches layout documented in anim_fo4.py and verified against real FO4 files.
///
/// hk_2014.1.0-r1 packfile layout (64-bit pointers):
///   0x00-0x3F  File header (64 bytes)
///   0x40-0x4F  Padding (16 bytes)
///   0x50+      3 section headers (0x40 bytes each): __classnames__, __types__, __data__
///
/// hkaSplineCompressedAnimation struct offsets (from object start):
///   +0x10  type (4)
///   +0x14  duration (float)
///   +0x18  numberOfTransformTracks (int32)
///   +0x1C  numberOfFloatTracks (int32)
///   +0x20  extractedMotion (ptr8)
///   +0x28  annotationTracks (hkArray - 16 bytes)
///   +0x38  numFrames (int32)
///   +0x3C  numBlocks (int32)
///   +0x40  maxFramesPerBlock (int32)
///   +0x44  maskAndQuantizationSize (int32)
///   +0x48  blockDuration (float)
///   +0x50  frameDuration (float)
///   +0x58  blockOffsets (hkArray&lt;u32&gt;)
///   +0x98  data (hkArray&lt;u8&gt;)
/// </summary>
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

    public static bool IsFo4Hkx(string filepath)
    {
        if (!File.Exists(filepath)) return false;
        try
        {
            using var fs = File.OpenRead(filepath);
            if (fs.Length < 64) return false;
            byte[] hdr = new byte[4];
            fs.Read(hdr, 0, 4);
            return hdr[0] == HkxMagic[0] && hdr[1] == HkxMagic[1] &&
                   hdr[2] == HkxMagic[2] && hdr[3] == HkxMagic[3];
        }
        catch { return false; }
    }

    // Refuses rather than returning an empty animation. ReadSkeleton deliberately does not go
    // through this, because a file can hold a skeleton this reader understands next to an animation
    // class it does not.
    public HkxAnimationData ReadAnimation(string filepath)
    {
        byte[] data = File.ReadAllBytes(filepath);
        var parsed = ParseHkx(data);

        if (parsed.HasUnsupportedAnimation)
            throw new NotSupportedException(
                $"unsupported animation class: {parsed.AnimationClass}. " +
                $"Only {HkxAnimationData.SupportedAnimationClass} is decoded, so no frame data was read from " +
                Path.GetFileName(filepath));

        return parsed;
    }

    // For a caller that wants to show the problem rather than be stopped by it. Returns false when
    // the file holds an animation class this reader cannot decode, with AnimationClass set to say
    // which, so the message can name it without picking an exception apart.
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
        public int LocalFixupAbs;   // abs file offset
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

        // Section headers start at 0x50 (after 64-byte file header + 16-byte padding)
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

        // ── Build local fixup map (src_rel -> dst_rel within __data__) ──
        var fixups = ParseLocalFixups(data, dataSec);

        // ── Build virtual fixup map (obj_rel -> class_name) ──
        var objectClasses = ParseVirtualFixups(data, dataSec, cnStart);

        var result = new HkxAnimationData();

        // ── Parse hkaSkeleton if present (select the skeleton with the most bones to avoid loading the ragdoll skeleton) ──
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

        // Havok ships several animation classes and Bethesda uses two of them. Record which one is
        // here before decoding, so a class this reader cannot decode is distinguishable from an
        // animation that really is empty. 857 of the 13990 vanilla animations are
        // hkaLosslessCompressedAnimation and used to come back as silently empty.
        result.AnimationClass = objectClasses.Keys.FirstOrDefault(
            c => c.StartsWith("hka", StringComparison.Ordinal) && c.EndsWith("Animation", StringComparison.Ordinal)) ?? "";

        // ── Parse hkaSplineCompressedAnimation if present ──
        if (objectClasses.TryGetValue("hkaSplineCompressedAnimation", out int animRel))
        {
            ParseSplineAnimation(data, dataAbs, animRel, fixups, result);
        }
        else if (result.Skeleton != null)
        {
            result.OriginalSkeletonName = result.Skeleton.Name;
        }

        // ── Parse hkaAnimationBinding if present ──
        if (objectClasses.TryGetValue("hkaAnimationBinding", out int bindRel))
        {
            ParseAnimationBinding(data, dataAbs, bindRel, fixups, result);
        }

        return result;
    }

    /// <summary>Parse local fixup table: maps src_rel -> dst_rel within __data__.</summary>
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

    /// <summary>Parse virtual fixup table: maps obj_rel -> class_name.</summary>
    private static Dictionary<string, int> ParseVirtualFixups(byte[] data, SectionInfo sec, int cnStart)
    {
        // Returns LAST class name to object offset (first wins for duplicates)
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
        // hkaSkeleton layout (64-bit P=8, hk_2014):
        //   +0x00  vtable (8)
        //   +0x08  memSizeAndFlags (8)
        //   +0x10  name (ptr8 -> string)
        //   +0x18  parentIndices (hkArray<int16>)  -> ptr(8) + count(4) + cap(4) = 16
        //   +0x28  bones (hkArray<hkaBone*>)
        //   +0x38  referencePose (hkArray<hkQsTransform>)
        int a = dataAbs + skelRel;
        if (a + 0x50 > data.Length) return null;

        var skel = new HkxSkeleton();

        // Name
        if (fixups.TryGetValue(skelRel + 0x10, out int nameRel))
            skel.Name = ReadNullTermString(data, dataAbs + nameRel, 256);

        // Parent indices (hkArray<int16>)
        int parCount = SafeReadI32(data, a + 0x20);
        if (fixups.TryGetValue(skelRel + 0x18, out int parDataRel))
        {
            int parAbs = dataAbs + parDataRel;
            for (int i = 0; i < parCount && parAbs + i * 2 + 2 <= data.Length; i++)
                skel.ParentIndices.Add(BitConverter.ToInt16(data, parAbs + i * 2));
        }

        // Bones array (hkArray<hkaBone>)
        // hkaBone: ptr(8) to name string + lockTranslation(4) + pad(4) = 0x10 bytes
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

        // Reference pose (hkArray<hkQsTransform>) — 48 bytes per entry
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

        // blockOffsets: hkArray<u32> at anim+0x58
        int blockOffsetsCount = SafeReadI32(data, a + 0x60);
        List<int> blockOffsets = new();
        if (fixups.TryGetValue(animRel + 0x58, out int boRel))
        {
            int boAbs = dataAbs + boRel;
            for (int i = 0; i < blockOffsetsCount && boAbs + i * 4 + 4 <= data.Length; i++)
                blockOffsets.Add(SafeReadI32(data, boAbs + i * 4));
        }

        // data blob: hkArray<u8> at anim+0x98
        int blobCount = SafeReadI32(data, a + 0xA0);
        int blobAbs = -1;
        if (fixups.TryGetValue(animRel + 0x98, out int blobRel))
            blobAbs = dataAbs + blobRel;

        // Parse annotation track names (bone names) at anim+0x28
        ParseAnnotationTracks(data, dataAbs, animRel + 0x28, fixups, anim);

        // Decompress spline data
        if (blobAbs > 0 && blockOffsets.Count > 0 && anim.NumTracks > 0 && anim.NumFrames > 0)
        {
            DecompressSpline(data, blobAbs, blobCount, anim.NumTracks, anim.NumFrames,
                anim.NumBlocks, anim.MaxFramesPerBlock, blockOffsets, maskAndQuantSize, anim);
        }
    }

    private static void ParseAnnotationTracks(byte[] data, int dataAbs, int arrRel,
        Dictionary<int, int> fixups, HkxAnimationData anim)
    {
        int count = SafeReadI32(data, dataAbs + arrRel + 8);
        if (count <= 0) return;
        if (!fixups.TryGetValue(arrRel, out int contentRel)) return;

        const int AnnotTrackStride = 0x18; // ptr(8) + hkArray(16)
        for (int i = 0; i < count; i++)
        {
            int trackRel = contentRel + i * AnnotTrackStride;
            string name = "";
            if (fixups.TryGetValue(trackRel, out int nameRel))
                name = ReadNullTermString(data, dataAbs + nameRel, 256);
            anim.BoneNames.Add(name);

            // Annotation events at trackRel+0x08
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

    private static void ParseAnimationBinding(byte[] data, int dataAbs, int bindRel,
        Dictionary<int, int> fixups, HkxAnimationData anim)
    {
        // +0x10  originalSkeletonName (ptr)
        // +0x20  transformTrackToBoneIndices (hkArray<int16>)
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

            // Guard: need 4*numTracks bytes for masks
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

                // ── POSITION ──
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

                // ── ROTATION ──
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

                // ── SCALE ──
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

    private static int FindKnotSpan(int degree, float t, int numCp, float[] knots)
    {
        if (numCp <= 0 || knots.Length == 0) return 0;
        if (t >= knots[numCp]) return numCp - 1;
        int lo = degree, hi = numCp, mid = (lo + hi) / 2;
        for (int iter = 0; iter < 100; iter++)
        {
            if (t < knots[mid]) hi = mid;
            else if (t >= knots[mid + 1]) lo = mid;
            else break;
            mid = (lo + hi) / 2;
        }
        return mid;
    }

    private static float EvalBSplineScalar(int span, int degree, float t, float[] knots, List<float> cps)
    {
        if (cps.Count == 0) return 0;
        if (cps.Count == 1) return cps[0];
        float[] N = new float[degree + 1];
        N[0] = 1f;
        for (int i = 1; i <= degree; i++)
            for (int j = i-1; j >= 0; j--)
            {
                float d = span+i-j < knots.Length && span-j >= 0
                    ? knots[span+i-j] - knots[span-j] : 0;
                float A = d >= 1e-10f ? (t - knots[span-j]) / d : 0;
                float tmp = N[j] * A;
                if (j+1 < N.Length) N[j+1] += N[j] - tmp;
                N[j] = tmp;
            }
        float r = 0;
        for (int i = 0; i <= degree; i++) { int idx = span-i; if (idx >= 0 && idx < cps.Count) r += cps[idx] * N[i]; }
        return r;
    }

    private static Quaternion EvalBSplineQuat(int span, int degree, float t, float[] knots, List<Quaternion> cps)
    {
        if (cps.Count == 0) return Quaternion.Identity;
        if (cps.Count == 1) return cps[0];
        float[] N = new float[degree + 1];
        N[0] = 1f;
        for (int i = 1; i <= degree; i++)
            for (int j = i-1; j >= 0; j--)
            {
                float d = span+i-j < knots.Length && span-j >= 0
                    ? knots[span+i-j] - knots[span-j] : 0;
                float A = d >= 1e-10f ? (t - knots[span-j]) / d : 0;
                float tmp = N[j] * A;
                if (j+1 < N.Length) N[j+1] += N[j] - tmp;
                N[j] = tmp;
            }
        Quaternion r = new(0,0,0,0);
        for (int i = 0; i <= degree; i++)
        {
            int idx = span - i;
            if (idx >= 0 && idx < cps.Count) { var q = cps[idx]; r = new(r.X+q.X*N[i], r.Y+q.Y*N[i], r.Z+q.Z*N[i], r.W+q.W*N[i]); }
        }
        return Quaternion.Normalize(r);
    }

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
