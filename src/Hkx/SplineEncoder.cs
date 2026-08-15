using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

public static class SplineEncoder
{

    public const int FramesPerBlock = 256;

    public sealed record Options
    {

        public float PositionTolerance { get; init; } = 0.01f;

        public float RotationTolerance { get; init; } = 0.001f;

        public float ScaleTolerance { get; init; } = 0.0005f;

        public int RotationFormat { get; init; } = 1;

        public float StaticTolerance { get; init; } = 1e-6f;
    }

    public sealed record Report(int Tracks, int Frames, int Blocks, int Identity, int Static, int Spline,
        int ExactCurves, float WorstPosition, float WorstRotation, float WorstScale)
    {
        public override string ToString() =>
            $"{Frames} frame(s) of {Tracks} track(s) in {Blocks} block(s): " +
            $"{Identity} channel(s) undriven, {Static} unchanging, {Spline} as curves " +
            $"({ExactCurves} needing one control point per frame)";
    }

    public sealed record Blob(byte[] Data, int[] BlockOffsets, int[] FloatBlockOffsets, int NumBlocks,
        int MaxFramesPerBlock, int MaskAndQuantizationSize, float Duration, float BlockDuration,
        float BlockInverseDuration, float FrameDuration, Report Report);

    public const int AnimationType = 3;

    public static Blob Encode(HkxAnimationData animation, Options? options = null)
    {
        var opts = options ?? new Options();

        int tracks = animation.Tracks.Count;
        int frames = animation.NumFrames;
        if (tracks <= 0) throw new InvalidOperationException("nothing to write: the animation has no tracks");
        if (frames <= 0) throw new InvalidOperationException("nothing to write: the animation has no frames");

        foreach (var track in animation.Tracks)
            if (track.Rotations.Count < frames && track.Translations.Count < frames && track.Scales.Count < frames)
                throw new InvalidOperationException(
                    "a track carries fewer frames than the animation says it has, so the blob would " +
                    "be written from values that were never decoded");

        int blocks = (frames + FramesPerBlock - 1) / FramesPerBlock;
        var body = new List<byte>();
        var offsets = new int[blocks];
        var floatOffsets = new int[blocks];

        int identity = 0, statics = 0, splines = 0, exact = 0;
        float worstPos = 0, worstRot = 0, worstScale = 0;

        for (int block = 0; block < blocks; block++)
        {

            while (body.Count % 16 != 0) body.Add(0);
            offsets[block] = body.Count;

            int first = block * FramesPerBlock;
            int inBlock = Math.Min(FramesPerBlock, frames - first);

            var masks = new byte[tracks * 4];
            var channels = new List<byte>();

            for (int t = 0; t < tracks; t++)
            {
                var track = animation.Tracks[t];
                var written = WriteTrack(track, first, inBlock, opts, channels,
                    ref identity, ref statics, ref splines, ref exact,
                    ref worstPos, ref worstRot, ref worstScale);

                masks[t * 4 + SplineFormat.QuantByte] = written.Quant;
                masks[t * 4 + SplineFormat.PosByte] = written.Pos;
                masks[t * 4 + SplineFormat.RotByte] = written.Rot;
                masks[t * 4 + SplineFormat.ScaleByte] = written.Scale;
            }

            body.AddRange(masks);
            body.AddRange(channels);

            floatOffsets[block] = body.Count;
        }

        float frameDuration = animation.FrameDuration > 0 ? animation.FrameDuration
                            : frames > 1 ? animation.Duration / (frames - 1) : animation.Duration;

        float blockDuration = frameDuration * (FramesPerBlock - 1);

        var report = new Report(tracks, frames, blocks, identity, statics, splines, exact,
            worstPos, worstRot, worstScale);

        return new Blob(body.ToArray(), offsets, floatOffsets, blocks, FramesPerBlock, tracks * 4,
            animation.Duration, blockDuration, blockDuration > 0 ? 1f / blockDuration : 0f,
            frameDuration, report);
    }

    private readonly record struct Masks(byte Quant, byte Pos, byte Rot, byte Scale);

    private static Masks WriteTrack(HkxTrackData track, int first, int inBlock, Options opts,
        List<byte> into, ref int identity, ref int statics, ref int splines, ref int exact,
        ref float worstPos, ref float worstRot, ref float worstScale)
    {

        byte pos = WriteVector(Slice(track.Translations, first, inBlock, Vector3.Zero), Vector3.Zero,
            opts.PositionTolerance, opts.StaticTolerance, into, ref identity, ref statics, ref splines,
            ref exact, ref worstPos);

        byte rot = WriteRotation(SliceQ(track.Rotations, first, inBlock), opts, into,
            ref identity, ref statics, ref splines, ref exact, ref worstRot);

        byte scale = WriteVector(Slice(track.Scales, first, inBlock, Vector3.One), Vector3.One,
            opts.ScaleTolerance, opts.StaticTolerance, into, ref identity, ref statics, ref splines,
            ref exact, ref worstScale);

        byte quant = (byte)(1 | ((opts.RotationFormat & 0x0F) << 2) | (1 << 6));

        Pad4(into);
        return new Masks(quant, pos, rot, scale);
    }

    private static byte WriteVector(Vector3[] samples, Vector3 neutral, float tolerance,
        float staticTolerance, List<byte> into, ref int identity, ref int statics, ref int splines,
        ref int exact, ref float worst)
    {
        int frames = samples.Length;
        var axes = new float[3][];
        for (int a = 0; a < 3; a++)
        {
            axes[a] = new float[frames];
            for (int f = 0; f < frames; f++)
                axes[a][f] = a == 0 ? samples[f].X : a == 1 ? samples[f].Y : samples[f].Z;
        }

        var kind = new SplineFormat.Channel[3];
        for (int a = 0; a < 3; a++)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (float v in axes[a]) { if (v < min) min = v; if (v > max) max = v; }

            float neutralAxis = a == 0 ? neutral.X : a == 1 ? neutral.Y : neutral.Z;
            bool varies = max - min > staticTolerance;

            kind[a] = varies ? SplineFormat.Channel.Spline
                    : MathF.Abs(min - neutralAxis) <= staticTolerance ? SplineFormat.Channel.Identity
                    : SplineFormat.Channel.Static;

            if (kind[a] == SplineFormat.Channel.Identity) identity++;
            else if (kind[a] == SplineFormat.Channel.Static) statics++;
            else splines++;
        }

        byte flags = 0;
        for (int a = 0; a < 3; a++)
        {
            if (kind[a] == SplineFormat.Channel.Static) flags |= (byte)(1 << a);
            else if (kind[a] == SplineFormat.Channel.Spline) flags |= (byte)(1 << (a + 4));
        }

        bool anySpline = kind[0] == SplineFormat.Channel.Spline || kind[1] == SplineFormat.Channel.Spline
                      || kind[2] == SplineFormat.Channel.Spline;

        if (!anySpline)
        {

            for (int a = 0; a < 3; a++)
                if (kind[a] == SplineFormat.Channel.Static) AddFloat(into, axes[a][0]);
            Pad4(into);
            return flags;
        }

        var fitted = new SplineFit.Curve?[3];
        int controlPoints = 0, degree = 0;
        for (int a = 0; a < 3; a++)
        {
            if (kind[a] != SplineFormat.Channel.Spline) continue;
            var curve = SplineFit.FitScalar(axes[a], tolerance);
            fitted[a] = curve;
            if (curve.ControlPoints.Length > controlPoints)
            {
                controlPoints = curve.ControlPoints.Length;
                degree = curve.Degree;
            }
        }

        for (int a = 0; a < 3; a++)
        {
            if (fitted[a] == null) continue;
            if (fitted[a]!.ControlPoints.Length == controlPoints && fitted[a]!.Degree == degree) continue;
            fitted[a] = SplineFit.FitScalarAt(axes[a], controlPoints, degree);
        }

        bool missed = false;
        for (int a = 0; a < 3; a++)
            if (fitted[a] != null && fitted[a]!.Error > tolerance) missed = true;

        if (missed)
        {
            controlPoints = Math.Max(2, frames);
            degree = 1;
            for (int a = 0; a < 3; a++)
                if (fitted[a] != null) fitted[a] = SplineFit.FitScalarAt(axes[a], controlPoints, degree);
        }

        var knots = SplineFormat.Knots(controlPoints, degree, frames);
        foreach (var curve in fitted)
        {
            if (curve == null) continue;
            worst = MathF.Max(worst, curve.Error);
            if (curve.ControlPoints.Length >= frames) exact++;
        }

        AddU16(into, (ushort)(controlPoints - 1));
        into.Add((byte)degree);
        into.AddRange(knots);
        Pad4(into);

        for (int a = 0; a < 3; a++)
        {
            if (kind[a] == SplineFormat.Channel.Spline) { AddFloat(into, fitted[a]!.Min); AddFloat(into, fitted[a]!.Max); }
            else if (kind[a] == SplineFormat.Channel.Static) AddFloat(into, axes[a][0]);
        }

        for (int i = 0; i < controlPoints; i++)
            for (int a = 0; a < 3; a++)
            {
                if (kind[a] != SplineFormat.Channel.Spline) continue;
                var curve = fitted[a]!;
                AddU16(into, SplineFormat.Write16(curve.ControlPoints[i], curve.Min, curve.Max));
            }

        Pad4(into);
        return flags;
    }

    private static byte WriteRotation(Quaternion[] samples, Options opts, List<byte> into,
        ref int identity, ref int statics, ref int splines, ref int exact, ref float worst)
    {
        int frames = samples.Length;
        int format = opts.RotationFormat;

        float spread = 0;
        for (int f = 1; f < frames; f++)
            spread = MathF.Max(spread, SplineQuat.AngleBetween(samples[f], samples[0]));

        if (spread <= opts.StaticTolerance)
        {
            if (SplineQuat.AngleBetween(samples[0], Quaternion.Identity) <= opts.StaticTolerance)
            {
                identity++;
                return 0x00;
            }

            statics++;
            AlignTo(into, SplineFormat.RotAlign(format));
            AddQuat(into, format, samples[0]);
            Pad4(into);

            return 0x0F;
        }

        splines++;
        var fit = SplineFit.FitRotation(samples, opts.RotationTolerance, format);
        worst = MathF.Max(worst, fit.Error);
        if (fit.ControlPoints.Length >= frames) exact++;

        AddU16(into, (ushort)(fit.ControlPoints.Length - 1));
        into.Add((byte)fit.Degree);
        into.AddRange(fit.Knots);

        AlignTo(into, SplineFormat.RotAlign(format));
        foreach (var q in fit.ControlPoints) AddQuat(into, format, q);
        Pad4(into);

        return 0xF0;
    }

    private static Vector3[] Slice(List<Vector3> from, int first, int count, Vector3 fallback)
    {
        var into = new Vector3[count];
        for (int i = 0; i < count; i++)
            into[i] = first + i < from.Count ? from[first + i] : fallback;
        return into;
    }

    private static Quaternion[] SliceQ(List<Quaternion> from, int first, int count)
    {
        var into = new Quaternion[count];
        for (int i = 0; i < count; i++)
            into[i] = first + i < from.Count ? from[first + i] : Quaternion.Identity;
        return into;
    }

    private static void AddFloat(List<byte> into, float value) => into.AddRange(BitConverter.GetBytes(value));
    private static void AddU16(List<byte> into, ushort value) => into.AddRange(BitConverter.GetBytes(value));

    private static void AddQuat(List<byte> into, int format, Quaternion q)
    {
        var scratch = new byte[16];
        SplineQuat.Write(format, q, scratch, 0);
        for (int i = 0; i < SplineFormat.RotWidth(format); i++) into.Add(scratch[i]);
    }

    private static void Pad4(List<byte> into) => AlignTo(into, 4);

    private static void AlignTo(List<byte> into, int to)
    {
        while (into.Count % to != 0) into.Add(0);
    }

    public static void Decode(byte[] blob, int[] blockOffsets, int tracks, int frames,
        int maskAndQuantizationSize, int framesPerBlock, HkxAnimationData into)
    {
        for (int i = 0; i < tracks; i++) into.Tracks.Add(new HkxTrackData());

        for (int block = 0; block < blockOffsets.Length; block++)
        {
            int start = blockOffsets[block];
            int first = block * framesPerBlock;
            int inBlock = Math.Min(framesPerBlock, frames - first);
            if (inBlock <= 0) continue;

            int at = start + maskAndQuantizationSize;

            for (int t = 0; t < tracks; t++)
            {
                byte quant = blob[start + t * 4];
                byte posFlags = blob[start + t * 4 + SplineFormat.PosByte];
                byte rotFlags = blob[start + t * 4 + SplineFormat.RotByte];
                byte scaleFlags = blob[start + t * 4 + SplineFormat.ScaleByte];

                int posQuant = quant & 3;
                int rotFormat = (quant >> 2) & 0x0F;
                int scaleQuant = (quant >> 6) & 3;
                var track = into.Tracks[t];

                ReadVector(blob, ref at, posFlags, posQuant, inBlock, Vector3.Zero, track.Translations, start);
                ReadRotation(blob, ref at, rotFlags, rotFormat, inBlock, track.Rotations, start);
                ReadVector(blob, ref at, scaleFlags, scaleQuant, inBlock, Vector3.One, track.Scales, start);
            }
        }
    }

    private static void ReadVector(byte[] blob, ref int at, byte flags, int quant, int inBlock,
        Vector3 neutral, List<Vector3> into, int blockStart)
    {
        var kind = new SplineFormat.Channel[3];
        bool anySpline = false;
        for (int a = 0; a < 3; a++)
        {
            kind[a] = SplineFormat.PosKind(flags, a);
            if (kind[a] == SplineFormat.Channel.Spline) anySpline = true;
        }

        var values = new float[3][];
        var fixedAt = new float[3];
        for (int a = 0; a < 3; a++)
            fixedAt[a] = a == 0 ? neutral.X : a == 1 ? neutral.Y : neutral.Z;

        if (!anySpline)
        {
            for (int a = 0; a < 3; a++)
                if (kind[a] == SplineFormat.Channel.Static)
                {
                    fixedAt[a] = BitConverter.ToSingle(blob, at);
                    at += 4;
                }

            at = blockStart + SplineFormat.Align(at - blockStart, 4);
            for (int f = 0; f < inBlock; f++) into.Add(new Vector3(fixedAt[0], fixedAt[1], fixedAt[2]));
            return;
        }

        int controlPoints = BitConverter.ToUInt16(blob, at) + 1;
        int degree = blob[at + 2];
        at += 3;

        int knotCount = controlPoints + degree + 1;
        var knots = new float[knotCount];
        for (int k = 0; k < knotCount; k++) knots[k] = blob[at + k];
        at += knotCount;
        at = blockStart + SplineFormat.Align(at - blockStart, 4);

        var min = new float[3];
        var max = new float[3];
        for (int a = 0; a < 3; a++)
        {
            if (kind[a] == SplineFormat.Channel.Spline)
            {
                min[a] = BitConverter.ToSingle(blob, at);
                max[a] = BitConverter.ToSingle(blob, at + 4);
                at += 8;
            }
            else if (kind[a] == SplineFormat.Channel.Static)
            {
                fixedAt[a] = BitConverter.ToSingle(blob, at);
                at += 4;
            }
        }

        for (int a = 0; a < 3; a++) values[a] = new float[controlPoints];
        for (int i = 0; i < controlPoints; i++)
            for (int a = 0; a < 3; a++)
            {
                if (kind[a] != SplineFormat.Channel.Spline) continue;
                if (quant == 0) { values[a][i] = SplineFormat.Read8(blob[at], min[a], max[a]); at += 1; }
                else { values[a][i] = SplineFormat.Read16(BitConverter.ToUInt16(blob, at), min[a], max[a]); at += 2; }
            }

        at = blockStart + SplineFormat.Align(at - blockStart, 4);

        for (int f = 0; f < inBlock; f++)
        {
            var got = new Vector3(fixedAt[0], fixedAt[1], fixedAt[2]);
            for (int a = 0; a < 3; a++)
            {
                if (kind[a] != SplineFormat.Channel.Spline) continue;
                int span = SplineFormat.FindKnotSpan(degree, f, controlPoints, knots);
                float v = SplineFormat.Evaluate(span, degree, f, knots, values[a]);
                if (a == 0) got.X = v; else if (a == 1) got.Y = v; else got.Z = v;
            }
            into.Add(got);
        }
    }

    private static void ReadRotation(byte[] blob, ref int at, byte flags, int format, int inBlock,
        List<Quaternion> into, int blockStart)
    {
        var kind = SplineFormat.RotKind(flags);
        int width = SplineFormat.RotWidth(format);

        if (kind == SplineFormat.Channel.Identity)
        {
            for (int f = 0; f < inBlock; f++) into.Add(Quaternion.Identity);
            at = blockStart + SplineFormat.Align(at - blockStart, 4);
            return;
        }

        if (kind == SplineFormat.Channel.Static)
        {
            at = blockStart + SplineFormat.Align(at - blockStart, SplineFormat.RotAlign(format));
            var q = SplineQuat.Read(format, blob, at);
            at += width;
            at = blockStart + SplineFormat.Align(at - blockStart, 4);
            for (int f = 0; f < inBlock; f++) into.Add(q);
            return;
        }

        int controlPoints = BitConverter.ToUInt16(blob, at) + 1;
        int degree = blob[at + 2];
        at += 3;

        int knotCount = controlPoints + degree + 1;
        var knots = new float[knotCount];
        for (int k = 0; k < knotCount; k++) knots[k] = blob[at + k];
        at += knotCount;
        at = blockStart + SplineFormat.Align(at - blockStart, SplineFormat.RotAlign(format));

        var cps = new List<Quaternion>(controlPoints);
        for (int i = 0; i < controlPoints; i++)
        {
            var q = SplineQuat.Read(format, blob, at);
            at += width;
            if (cps.Count > 0 && Quaternion.Dot(q, cps[^1]) < 0) q = -q;
            cps.Add(q);
        }

        at = blockStart + SplineFormat.Align(at - blockStart, 4);

        for (int f = 0; f < inBlock; f++)
        {
            int span = SplineFormat.FindKnotSpan(degree, f, cps.Count, knots);
            into.Add(Quaternion.Normalize(SplineFormat.Evaluate(span, degree, f, knots, cps)));
        }
    }
}
