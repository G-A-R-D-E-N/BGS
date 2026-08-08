using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

// Writing an animation's frames back out as a spline compressed blob.
//
// This is the half of the codec that never existed. Reading one of these has worked for a long time,
// which is why the tool can show a clip and draw it; nothing could produce one, so saving a clip had
// to write every frame out uncompressed instead. That is correct and several times the size, and it
// is why a clip could not be edited and left looking like the clip it replaced.
//
// The layout is not chosen here. Every position, width and alignment below is the decoder's walk
// turned around, because the only reader that matters reads it that way, and the choices that are
// genuinely free were counted across the 13,514 vanilla animations rather than picked: sixteen bit
// positions and scales, forty bit rotations, 256 frames to a block. The counts are in `symrm
// splinestats`.
//
// What is deliberately not attempted: float tracks. Nothing here decodes them, and a blob whose
// transform tracks are right and whose float tracks are absent is a file the engine reads off the
// end of. Refused rather than approximated, the same way the interleaved writer refuses them.
public static class SplineEncoder
{
    /// Frames to a block. Every one of the 13,514 vanilla spline animations uses this, with no
    /// exceptions, and the knots are single bytes holding a frame index inside the block, so 256 is
    /// also the most the format can hold.
    public const int FramesPerBlock = 256;

    public sealed record Options
    {
        /// How far a bone may sit from where it was, in Havok units. A vanilla human is about 115
        /// units tall, so a hundredth of a unit is well under anything visible.
        public float PositionTolerance { get; init; } = 0.01f;

        /// How far a bone may be turned from where it was, in radians. A thousandth is about three
        /// hundredths of a degree.
        public float RotationTolerance { get; init; } = 0.001f;

        public float ScaleTolerance { get; init; } = 0.0005f;

        /// 1 for forty bit rotations, which is what 1,173,390 of the 1,291,826 vanilla track blocks
        /// use, or 2 for the forty eight bit form the other 118,436 use.
        public int RotationFormat { get; init; } = 1;

        /// Below this a channel is called unchanging and written once instead of as a curve.
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

    /// hkaAnimation::AnimationType for this class. Read off the shipped files rather than counted out
    /// of the enum, because the enum has entries the game does not use and an off by one there writes
    /// a file the engine will read as a different codec entirely. All 1,501 sampled say 3.
    public const int AnimationType = 3;

    /// Turns decoded frames into the bytes an hkaSplineCompressedAnimation carries.
    ///
    /// The animation's own timing is carried across rather than recomputed. A clip's duration is what
    /// the behaviour graph times transitions against, so deriving it from a frame count would retime
    /// every clip that did not happen to divide evenly.
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
            // Blocks start on a sixteen byte boundary. The format does not require it, since every
            // block is found through its own recorded offset, but it costs at most fifteen bytes a
            // block and keeps the blob looking like the ones the game ships.
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

            // Where this block's float channels would begin, which is where its transform channels
            // end, before the padding that carries the blob on to the next block. Nothing here writes
            // float tracks, so no reader will follow it, and it is filled in properly anyway because
            // the shipped files fill it in properly: on 1,486 of 1,501 sampled it lands at the end of
            // the block's own data with only the padding between it and the next block.
            floatOffsets[block] = body.Count;
        }

        float frameDuration = animation.FrameDuration > 0 ? animation.FrameDuration
                            : frames > 1 ? animation.Duration / (frames - 1) : animation.Duration;

        // A block covers the span between its first and last frame, not one frame more. Measured, not
        // reasoned about: every one of 1,501 sampled animations has blockDuration equal to
        // frameDuration times one less than maxFramesPerBlock, and none has it equal to
        // frameDuration times maxFramesPerBlock.
        float blockDuration = frameDuration * (FramesPerBlock - 1);

        var report = new Report(tracks, frames, blocks, identity, statics, splines, exact,
            worstPos, worstRot, worstScale);

        return new Blob(body.ToArray(), offsets, floatOffsets, blocks, FramesPerBlock, tracks * 4,
            animation.Duration, blockDuration, blockDuration > 0 ? 1f / blockDuration : 0f,
            frameDuration, report);
    }

    private readonly record struct Masks(byte Quant, byte Pos, byte Rot, byte Scale);

    // One track's three channels, in the order the decoder walks them and with the padding it steps
    // over. The channel bytes are built into a list of their own so the alignment can be measured
    // from the start of the block's channel area, which is where the decoder measures it from.
    private static Masks WriteTrack(HkxTrackData track, int first, int inBlock, Options opts,
        List<byte> into, ref int identity, ref int statics, ref int splines, ref int exact,
        ref float worstPos, ref float worstRot, ref float worstScale)
    {
        // Position and scale are written the same way as each other and differently from rotation.
        // The neutral value differs, which is the only reason the two calls are not one: a position
        // nobody drives is nothing, and a scale nobody drives is one.
        byte pos = WriteVector(Slice(track.Translations, first, inBlock, Vector3.Zero), Vector3.Zero,
            opts.PositionTolerance, opts.StaticTolerance, into, ref identity, ref statics, ref splines,
            ref exact, ref worstPos);

        byte rot = WriteRotation(SliceQ(track.Rotations, first, inBlock), opts, into,
            ref identity, ref statics, ref splines, ref exact, ref worstRot);

        byte scale = WriteVector(Slice(track.Scales, first, inBlock, Vector3.One), Vector3.One,
            opts.ScaleTolerance, opts.StaticTolerance, into, ref identity, ref statics, ref splines,
            ref exact, ref worstScale);

        // Sixteen bit positions and scales, forty bit rotations, which is what vanilla uses
        // everywhere. The rotation format is the only one the options can move, because it is the
        // only one vanilla itself is not unanimous about.
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
            // No curve, so no header and no knots: just the axes that hold a value, as plain floats.
            for (int a = 0; a < 3; a++)
                if (kind[a] == SplineFormat.Channel.Static) AddFloat(into, axes[a][0]);
            Pad4(into);
            return flags;
        }

        // Every curve on this track's channel shares one knot vector, so they are fitted first and
        // the widest of them decides the shape all three are written with. Fitting them separately
        // and writing the first one's knots would put the other two on a curve they were not fitted
        // to, which is the kind of fault that shows as one axis drifting and the others clean.
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

        // Refit anything that came out smaller onto the shared shape, so all three agree.
        //
        // A refit is not automatically as good as the fit it replaces. The shared shape is the widest
        // axis's, and the widest axis is not always the fussiest one: an axis that needed a control
        // point per frame at degree one can be made to share a degree three shape with more control
        // points than it asked for and still come out worse, because what it needed was the exact
        // interpolation and not the width. Measured on the corpus that is not a small effect. It was
        // 55 of the 13,514 vanilla clips drifting up to 1.59 units, on hand and finger bones during
        // talking idles, while their rotations stayed clean. So the refits are measured, and if any
        // of them misses the tolerance all three axes drop to the shape that cannot miss.
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

            // The low nibble is what marks a rotation as unchanging. Every bit of it is set because
            // that is what vanilla writes: 250,434 of its unchanging rotations are 0x0f and the ones
            // that are not carry other bits alongside rather than instead.
            return 0x0F;
        }

        splines++;
        var fit = SplineFit.FitRotation(samples, opts.RotationTolerance, format);
        worst = MathF.Max(worst, fit.Error);
        if (fit.ControlPoints.Length >= frames) exact++;

        AddU16(into, (ushort)(fit.ControlPoints.Length - 1));
        into.Add((byte)fit.Degree);
        into.AddRange(fit.Knots);

        // Only the rotation's own alignment here, and no rounding up to four. The decoder aligns to
        // the quantisation width and nothing else at this point, so a blob padded to four would put
        // every control point one word late for the whole rest of the block.
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

    // Reading a blob back without a file around it.
    //
    // The decoder in HkxBinaryReader only reaches a blob through a packfile, which is the right shape
    // for reading the game's files and the wrong shape for checking an encoder: a difference could
    // then be the blob or it could be everything the file writer did around it. This walks the blob
    // alone, so a gate built on it is measuring the codec and nothing else.
    //
    // It is a second implementation of the same walk, which is usually the thing to avoid. Here it is
    // the point. If this and the reader ever disagree about where a run starts, one of them is wrong
    // about the format, and a gate that used the reader's own walk would agree with itself either way.
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
