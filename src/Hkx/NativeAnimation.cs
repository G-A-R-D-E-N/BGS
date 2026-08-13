using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenCommonwealth.Services;

namespace OpenCommonwealth.Services.Hkx;


















public static class NativeAnimation
{
    public const string InterleavedClass = "hkaInterleavedUncompressedAnimation";


    public static readonly string[] Compressed =
    {
        "hkaSplineCompressedAnimation",
        "hkaLosslessCompressedAnimation",
    };



    private const int Type = 0x10;
    private const int Duration = 0x14;
    private const int TransformTracks = 0x18;
    private const int FloatTracks = 0x1C;
    private const int ExtractedMotion = 0x20;
    private const int AnnotationTracks = 0x28;
    private const int Transforms = 0x38;
    private const int Floats = 0x48;


    private const int InterleavedType = 1;

    public sealed record Result(byte[] Bytes, int Frames, int Tracks, string From, long Grew)
    {
        public override string ToString() =>
            $"{From} written out as {Frames} frame(s) of {Tracks} track(s), {Grew} bytes larger";
    }






    public static Result Interleave(string hkxPath, HkxAnimationData decoded) =>
        Interleave(InputFilePolicy.ReadHkx(hkxPath), decoded);

    public static Result Interleave(byte[] hkx, HkxAnimationData decoded)
    {
        ArgumentNullException.ThrowIfNull(hkx);
        ArgumentNullException.ThrowIfNull(decoded);
        InputFilePolicy.EnsureHkx(hkx.LongLength);
        var image = PackfileImage.Read(hkx);
        long was = hkx.LongLength;

        var data = image.Section("__data__")
            ?? throw new InvalidOperationException("this file has no data section");

        var objects = new PackfileObjects(image);
        var source = objects.Instances.FirstOrDefault(i => Array.IndexOf(Compressed, i.ClassName) >= 0)
            ?? throw new InvalidOperationException(
                "This file holds no animation of a class that can be decoded, so there was nothing " +
                "to write out.");

        int at = source.Offset;
        int floatTracks = BitConverter.ToInt32(data.Data, at + FloatTracks);
        if (floatTracks != 0)
            throw new InvalidOperationException(
                $"This animation drives {floatTracks} float track(s), which nothing here decodes, so " +
                "it was not written out rather than written out short.");

        int tracks = decoded.NumTracks, frames = decoded.NumFrames;
        if (tracks <= 0 || frames <= 0)
            throw new InvalidOperationException("This animation decoded to no frames, so there was " +
                                                "nothing to write out.");

        if (decoded.Tracks.Count != tracks)
            throw new InvalidOperationException(
                $"The animation says it has {tracks} track(s) and decoded to {decoded.Tracks.Count}, " +
                "so it was not written out.");

        foreach (var track in decoded.Tracks)
            if (track.Translations.Count != frames || track.Rotations.Count != frames ||
                track.Scales.Count != frames)
                throw new InvalidOperationException(
                    $"A track decoded to a different number of frames than the {frames} this " +
                    "animation declares, so it was not written out.");




        float duration = BitConverter.ToSingle(data.Data, at + Duration);
        var annotations = objects.ArrayAt(at + AnnotationTracks);
        int self = image.Sections.IndexOf(data);
        var motion = data.Globals().FirstOrDefault(g => g.Source == at + ExtractedMotion);
        bool hasMotion = data.Globals().Any(g => g.Source == at + ExtractedMotion);

        var added = NativeAppend.Object(image, InterleavedClass);



        objects = new PackfileObjects(image);
        int made = added.Offset;

        BitConverter.GetBytes(InterleavedType).CopyTo(data.Data, made + Type);
        BitConverter.GetBytes(duration).CopyTo(data.Data, made + Duration);
        BitConverter.GetBytes(tracks).CopyTo(data.Data, made + TransformTracks);
        BitConverter.GetBytes(0).CopyTo(data.Data, made + FloatTracks);










        if (annotations is { Count: > 0 })
        {
            int copied = CopyStructRun(image, data, annotations.At, annotations.Count, "hkaAnnotationTrack");
            data.SetLocal(made + AnnotationTracks, copied);
            Array.Copy(data.Data, at + AnnotationTracks + 8, data.Data, made + AnnotationTracks + 8, 8);
        }

        if (hasMotion) data.SetGlobal(made + ExtractedMotion, motion.Section, motion.Destination);


        data.AlignData(NativeAppend.Alignment);
        var run = new byte[frames * tracks * HkxBinaryReader.QsTransformSize];

        for (int f = 0; f < frames; f++)
            for (int t = 0; t < tracks; t++)
            {
                int p = (f * tracks + t) * HkxBinaryReader.QsTransformSize;
                var track = decoded.Tracks[t];

                Write(run, p, track.Translations[f]);
                Write(run, p + 16, Normalized(track.Rotations[f]));
                Write(run, p + 32, track.Scales[f]);
            }

        int landed = data.AppendData(run);
        data.SetLocal(made + Transforms, landed);
        BitConverter.GetBytes(frames * tracks).CopyTo(data.Data, made + Transforms + 8);
        BitConverter.GetBytes(0x80000000u | (uint)(frames * tracks)).CopyTo(data.Data, made + Transforms + 12);



        BitConverter.GetBytes(0).CopyTo(data.Data, made + Floats + 8);
        BitConverter.GetBytes(0x80000000u).CopyTo(data.Data, made + Floats + 12);





        var globals = data.Globals().ToList();
        int repointed = 0;
        for (int i = 0; i < globals.Count; i++)
        {
            if (globals[i].Section != self || globals[i].Destination != at) continue;
            globals[i] = (globals[i].Source, self, made);
            repointed++;
        }

        if (repointed == 0)
            throw new InvalidOperationException(
                "Nothing in this file pointed at its animation, so there was nothing to aim at the " +
                "one written out, and it was not written.");

        data.SetGlobals(globals);

        FixupOrder.Reorder(image);
        byte[] bytes = image.Rebuild();
        return new Result(bytes, frames, tracks, source.ClassName, bytes.Length - was);
    }

    public const string SplineClass = "hkaSplineCompressedAnimation";

    public const string ReferenceFrameClass = "hkaDefaultAnimatedReferenceFrame";



    private const int MotionDuration = 0x40;
    private const int MotionSamples = 0x48;
    private const int MotionSize = 96;






















    public sealed record Timeline(float Duration, float FromTime, float ToTime, float Scale,
                                  RootMotion.Motion? Motion)
    {
        public static Timeline Of(AnimationEdit.Trimmed trimmed) =>
            new(trimmed.Animation.Duration, trimmed.FromTime, trimmed.ToTime, 1f, trimmed.Motion);

        public static Timeline Of(AnimationEdit.Retimed retimed, float was) =>
            new(retimed.Animation.Duration, 0f, was, retimed.Scale, retimed.Motion);

        public override string ToString() =>
            $"{FromTime:F3}s to {ToTime:F3}s at {Scale:F3} times, as a {Duration:F3}s clip, " +
            (Motion is { Any: true } ? $"{Motion.Samples.Count} motion sample(s)" : "no new motion");
    }


    private const int NumFrames = 0x38;
    private const int NumBlocks = 0x3C;
    private const int MaxFramesPerBlock = 0x40;
    private const int MaskAndQuantizationSize = 0x44;
    private const int BlockDuration = 0x48;
    private const int BlockInverseDuration = 0x4C;
    private const int SplineFrameDuration = 0x50;
    private const int BlockOffsets = 0x58;
    private const int FloatBlockOffsets = 0x68;
    private const int TransformOffsets = 0x78;
    private const int FloatOffsets = 0x88;
    private const int SplineData = 0x98;
    private const int Endian = 0xA8;















    public static Result Recompress(string hkxPath, HkxAnimationData decoded,
        SplineEncoder.Options? options = null, bool dropReplaced = true, Timeline? cut = null) =>
        Recompress(InputFilePolicy.ReadHkx(hkxPath), decoded, options, dropReplaced, cut);

    public static Result Recompress(byte[] hkx, HkxAnimationData decoded,
        SplineEncoder.Options? options = null, bool dropReplaced = true, Timeline? cut = null)
    {
        ArgumentNullException.ThrowIfNull(hkx);
        ArgumentNullException.ThrowIfNull(decoded);
        InputFilePolicy.EnsureHkx(hkx.LongLength);
        var image = PackfileImage.Read(hkx);
        long was = hkx.LongLength;

        var data = image.Section("__data__")
            ?? throw new InvalidOperationException("this file has no data section");

        var objects = new PackfileObjects(image);
        var source = objects.Instances.FirstOrDefault(i => Array.IndexOf(Compressed, i.ClassName) >= 0)
            ?? throw new InvalidOperationException(
                "This file holds no animation of a class that can be decoded, so there was nothing " +
                "to write out.");

        int at = source.Offset;
        int floatTracks = BitConverter.ToInt32(data.Data, at + FloatTracks);
        if (floatTracks != 0)
            throw new InvalidOperationException(
                $"This animation drives {floatTracks} float track(s), which nothing here decodes, so " +
                "it was not written out rather than written out short.");

        int tracks = decoded.NumTracks, frames = decoded.NumFrames;
        if (tracks <= 0 || frames <= 0)
            throw new InvalidOperationException("This animation decoded to no frames, so there was " +
                                                "nothing to write out.");

        if (decoded.Tracks.Count != tracks)
            throw new InvalidOperationException(
                $"The animation says it has {tracks} track(s) and decoded to {decoded.Tracks.Count}, " +
                "so it was not written out.");



        var blob = SplineEncoder.Encode(decoded, options);

        float duration = cut?.Duration ?? BitConverter.ToSingle(data.Data, at + Duration);
        var annotations = objects.ArrayAt(at + AnnotationTracks);
        int self = image.Sections.IndexOf(data);
        var motion = data.Globals().FirstOrDefault(g => g.Source == at + ExtractedMotion);
        bool hasMotion = data.Globals().Any(g => g.Source == at + ExtractedMotion);



        int replacedId = objects.IndexOf(source) + NativeGraphModel.FirstId;





        bool replacingMotion = cut?.Motion is not null && hasMotion &&
                               motion.Section == self &&
                               objects.Instances.Any(i => i.Offset == motion.Destination);
        int replacedMotionId = replacingMotion
            ? objects.IndexOf(objects.Instances.First(i => i.Offset == motion.Destination)) +
              NativeGraphModel.FirstId
            : -1;

        var added = NativeAppend.Object(image, SplineClass);
        objects = new PackfileObjects(image);
        int made = added.Offset;

        BitConverter.GetBytes(SplineEncoder.AnimationType).CopyTo(data.Data, made + Type);
        BitConverter.GetBytes(duration).CopyTo(data.Data, made + Duration);
        BitConverter.GetBytes(tracks).CopyTo(data.Data, made + TransformTracks);
        BitConverter.GetBytes(0).CopyTo(data.Data, made + FloatTracks);

        BitConverter.GetBytes(frames).CopyTo(data.Data, made + NumFrames);
        BitConverter.GetBytes(blob.NumBlocks).CopyTo(data.Data, made + NumBlocks);
        BitConverter.GetBytes(blob.MaxFramesPerBlock).CopyTo(data.Data, made + MaxFramesPerBlock);
        BitConverter.GetBytes(blob.MaskAndQuantizationSize).CopyTo(data.Data, made + MaskAndQuantizationSize);
        BitConverter.GetBytes(blob.BlockDuration).CopyTo(data.Data, made + BlockDuration);
        BitConverter.GetBytes(blob.BlockInverseDuration).CopyTo(data.Data, made + BlockInverseDuration);
        BitConverter.GetBytes(blob.FrameDuration).CopyTo(data.Data, made + SplineFrameDuration);
        BitConverter.GetBytes(0).CopyTo(data.Data, made + Endian);

        if (annotations is { Count: > 0 })
        {
            int copied = CopyStructRun(image, data, annotations.At, annotations.Count, "hkaAnnotationTrack");
            data.SetLocal(made + AnnotationTracks, copied);
            Array.Copy(data.Data, at + AnnotationTracks + 8, data.Data, made + AnnotationTracks + 8, 8);




            if (cut != null && copied >= 0)
                RebaseAnnotations(data, copied, annotations.Count, cut);
        }

        if (replacingMotion)
        {
            int frame = WriteReferenceFrame(image, data, motion.Destination, cut!.Motion!);
            data.SetGlobal(made + ExtractedMotion, self, frame);
        }
        else if (hasMotion) data.SetGlobal(made + ExtractedMotion, motion.Section, motion.Destination);

        WriteUintArray(data, made + BlockOffsets, blob.BlockOffsets);
        WriteUintArray(data, made + FloatBlockOffsets, blob.FloatBlockOffsets);



        WriteUintArray(data, made + TransformOffsets, Array.Empty<int>());
        WriteUintArray(data, made + FloatOffsets, Array.Empty<int>());

        data.AlignData(NativeAppend.Alignment);
        int landed = data.AppendData(blob.Data);
        data.SetLocal(made + SplineData, landed);
        BitConverter.GetBytes(blob.Data.Length).CopyTo(data.Data, made + SplineData + 8);
        BitConverter.GetBytes(0x80000000u | (uint)blob.Data.Length).CopyTo(data.Data, made + SplineData + 12);

        var globals = data.Globals().ToList();
        int repointed = 0;
        for (int i = 0; i < globals.Count; i++)
        {
            if (globals[i].Section != self || globals[i].Destination != at) continue;
            globals[i] = (globals[i].Source, self, made);
            repointed++;
        }

        if (repointed == 0)
            throw new InvalidOperationException(
                "Nothing in this file pointed at its animation, so there was nothing to aim at the " +
                "one written out, and it was not written.");

        data.SetGlobals(globals);













        if (dropReplaced)
        {
            var going = replacingMotion ? new[] { replacedId, replacedMotionId } : new[] { replacedId };
            try { NativeRemove.Delete(image, going); }
            catch (InvalidOperationException e)
            {
                throw new InvalidOperationException(
                    "The animation was written, but the one it replaced could not be taken out of the " +
                    $"file, so nothing was saved rather than saving a file with both in it. {e.Message}");
            }
        }

        FixupOrder.Reorder(image);
        byte[] bytes = image.Rebuild();
        return new Result(bytes, frames, tracks, source.ClassName, bytes.Length - was);
    }













    private static void RebaseAnnotations(PackfileSection data, int run, int count, Timeline cut)
    {
        int trackStride = HavokClassTypes.Shipped["hkaAnnotationTrack"]?.Size ?? 24;
        int noteStride = HavokClassTypes.Shipped["hkaAnnotationTrackAnnotation"]?.Size ?? 16;




        var locals = data.Locals().ToList();
        var where = new Dictionary<int, int>();
        for (int i = 0; i < locals.Count; i++) where[locals[i].Source] = i;

        int? Destination(int source) => where.TryGetValue(source, out int i) ? locals[i].Destination : null;

        void Point(int source, int destination)
        {
            if (where.TryGetValue(source, out int i)) locals[i] = (source, destination);
            else { where[source] = locals.Count; locals.Add((source, destination)); }
        }

        void Clear(int source)
        {
            if (!where.TryGetValue(source, out int i)) return;


            locals[i] = (-1, -1);
            where.Remove(source);
        }

        for (int t = 0; t < count; t++)
        {
            int field = run + t * trackStride + 8;
            int held = BitConverter.ToInt32(data.Data, field + 8);
            if (held <= 0 || Destination(field) is not int notes) continue;

            var kept = new List<(float Time, int? Text)>();
            for (int n = 0; n < held; n++)
            {
                float when = BitConverter.ToSingle(data.Data, notes + n * noteStride);
                if (when < cut.FromTime - Slack || when > cut.ToTime + Slack) continue;
                kept.Add((Math.Clamp((when - cut.FromTime) * cut.Scale, 0, cut.Duration),
                          Destination(notes + n * noteStride + 8)));
            }

            for (int n = 0; n < held; n++)
            {
                int slot = notes + n * noteStride;
                if (n < kept.Count)
                {
                    BitConverter.GetBytes(kept[n].Time).CopyTo(data.Data, slot);
                    if (kept[n].Text is int text) Point(slot + 8, text); else Clear(slot + 8);
                    continue;
                }

                Array.Clear(data.Data, slot, noteStride);
                Clear(slot + 8);
            }

            BitConverter.GetBytes(kept.Count).CopyTo(data.Data, field + 8);
            BitConverter.GetBytes(0x80000000u | (uint)kept.Count).CopyTo(data.Data, field + 12);
            if (kept.Count == 0) Clear(field);
        }

        data.SetLocals(locals.Where(l => l.Source >= 0));
    }




    private const float Slack = 1f / 60f;












    private static int WriteReferenceFrame(PackfileImage image, PackfileSection data, int from,
                                           RootMotion.Motion motion)
    {
        var added = NativeAppend.Object(image, ReferenceFrameClass);
        int made = added.Offset;

        Array.Copy(data.Data, from + 16, data.Data, made + 16, MotionSize - 16);




        BitConverter.GetBytes(0).CopyTo(data.Data, made + MotionSamples + 8);
        BitConverter.GetBytes(0x80000000u).CopyTo(data.Data, made + MotionSamples + 12);

        BitConverter.GetBytes(motion.Duration).CopyTo(data.Data, made + MotionDuration);

        if (motion.Samples.Count > 0)
        {
            data.AlignData(NativeAppend.Alignment);
            var run = new byte[motion.Samples.Count * 16];

            for (int i = 0; i < motion.Samples.Count; i++)
            {
                var sample = motion.Samples[i];
                Write(run, i * 16, sample.Position);



                BitConverter.GetBytes(sample.TurnRadians).CopyTo(run, i * 16 + 12);
            }

            int landed = data.AppendData(run);
            data.SetLocal(made + MotionSamples, landed);
            BitConverter.GetBytes(motion.Samples.Count).CopyTo(data.Data, made + MotionSamples + 8);
            BitConverter.GetBytes(0x80000000u | (uint)motion.Samples.Count)
                        .CopyTo(data.Data, made + MotionSamples + 12);
        }

        return made;
    }


    private static void WriteUintArray(PackfileSection data, int field, int[] values)
    {
        if (values.Length == 0)
        {
            BitConverter.GetBytes(0).CopyTo(data.Data, field + 8);
            BitConverter.GetBytes(0x80000000u).CopyTo(data.Data, field + 12);
            return;
        }

        data.AlignData(NativeAppend.Alignment);
        var run = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++) BitConverter.GetBytes(values[i]).CopyTo(run, i * 4);

        int landed = data.AppendData(run);
        data.SetLocal(field, landed);
        BitConverter.GetBytes(values.Length).CopyTo(data.Data, field + 8);
        BitConverter.GetBytes(0x80000000u | (uint)values.Length).CopyTo(data.Data, field + 12);
    }

















    private static int CopyStructRun(PackfileImage image, PackfileSection data, int from, int count,
                                     string elementClass, int depth = 0,
                                     Dictionary<int, int>? copiedText = null)
    {
        var types = HavokClassTypes.Shipped;



        if (depth > 6 || count <= 0 || !types.Knows(elementClass)) return -1;

        int stride = types[elementClass]?.Size ?? 0;
        if (stride <= 0) return -1;



        copiedText ??= new Dictionary<int, int>();

        data.AlignData(NativeAppend.Alignment);
        int to = data.AppendData(new byte[count * stride]);
        Array.Copy(data.Data, from, data.Data, to, count * stride);

        int self = image.Sections.IndexOf(data);
        var locals = data.Locals().ToDictionary(l => l.Source, l => l.Destination);
        var globals = data.Globals().Where(g => g.Section == self)
                          .ToDictionary(g => g.Source, g => g.Destination);

        foreach (var member in types.Members(elementClass))
        {
            if (!member.Written) continue;

            for (int i = 0; i < count; i++)
            {
                int old = from + i * stride + member.Offset;
                int made = to + i * stride + member.Offset;

                if (member.VType is "TYPE_STRINGPTR" or "TYPE_CSTRING")
                {
                    if (locals.TryGetValue(old, out int text))
                        data.SetLocal(made, CopyText(data, text, copiedText));
                    continue;
                }

                if (member.VType == "TYPE_POINTER")
                {
                    if (globals.TryGetValue(old, out int target)) data.SetGlobal(made, self, target);
                    continue;
                }

                if (member.VType is not ("TYPE_ARRAY" or "TYPE_SIMPLEARRAY")) continue;

                int held = BitConverter.ToInt32(data.Data, old + 8);
                if (held <= 0 || !locals.TryGetValue(old, out int inner)) continue;

                if (member.VSub == "TYPE_STRUCT" && member.CType != null)
                {
                    int copied = CopyStructRun(image, data, inner, held, member.CType, depth + 1,
                                               copiedText);
                    if (copied >= 0) data.SetLocal(made, copied);
                    continue;
                }



                int width = member.VSub is "TYPE_STRINGPTR" or "TYPE_CSTRING" or "TYPE_POINTER" ? 8 : 0;
                if (width == 0) { data.SetLocal(made, inner); continue; }

                data.AlignData(NativeAppend.Alignment);
                int landed = data.AppendData(new byte[held * width]);
                Array.Copy(data.Data, inner, data.Data, landed, held * width);
                data.SetLocal(made, landed);

                for (int e = 0; e < held; e++)
                    if (locals.TryGetValue(inner + e * width, out int text))
                        data.SetLocal(landed + e * width, CopyText(data, text, copiedText));
            }
        }

        return to;
    }






    private static int CopyText(PackfileSection data, int at, Dictionary<int, int> already)
    {
        if (already.TryGetValue(at, out int made)) return made;

        int end = at;
        while (end < data.Data.Length && data.Data[end] != 0) end++;

        var text = new byte[end - at + 1];
        Array.Copy(data.Data, at, text, 0, text.Length - 1);

        made = data.AppendData(text);
        already[at] = made;
        return made;
    }

    private static void Write(byte[] into, int at, Vector3 value)
    {
        BitConverter.GetBytes(value.X).CopyTo(into, at);
        BitConverter.GetBytes(value.Y).CopyTo(into, at + 4);
        BitConverter.GetBytes(value.Z).CopyTo(into, at + 8);



    }

    private static void Write(byte[] into, int at, Quaternion value)
    {
        BitConverter.GetBytes(value.X).CopyTo(into, at);
        BitConverter.GetBytes(value.Y).CopyTo(into, at + 4);
        BitConverter.GetBytes(value.Z).CopyTo(into, at + 8);
        BitConverter.GetBytes(value.W).CopyTo(into, at + 12);
    }




    private static Quaternion Normalized(Quaternion q)
    {
        float length = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
        return length > 1e-6f ? new Quaternion(q.X / length, q.Y / length, q.Z / length, q.W / length)
                              : Quaternion.Identity;
    }
}
