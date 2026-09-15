using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

public partial class HkxBinaryReader
{
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

}
