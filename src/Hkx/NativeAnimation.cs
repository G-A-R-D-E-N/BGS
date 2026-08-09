using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

// Writing an animation's frames back into a file, by writing them out as they are.
//
// The reason this exists at all: nothing here can re-encode a compressed animation. Spline and
// lossless are both compressors, and an editor that can read a clip and not write one is an editor
// that cannot change a clip. Havok's answer to that is a format with no compression in it at all,
// hkaInterleavedUncompressedAnimation, which is every frame of every track written out one after
// another. Fallout 4 registers the class at startup, so the engine has the code to read one; it just
// never ships a file that is one.
//
// The trade is honest and worth stating: the file gets much larger, because a second of a sixty bone
// character is sixty times thirty transforms at forty eight bytes each. That is the cost of not
// having an encoder, and it is what a re-encode would later have to beat rather than match.
//
// What this deliberately does not do is touch the animation that was there. Its bytes stay exactly
// where they are and every pointer that named it is aimed at the new one instead, so the old clip is
// left in the file unreferenced. That is the same shape as every other write here: nothing already
// in the file moves.
public static class NativeAnimation
{
    public const string InterleavedClass = "hkaInterleavedUncompressedAnimation";

    /// The classes this can convert from, which is the pair the game ships and this can decode.
    public static readonly string[] Compressed =
    {
        "hkaSplineCompressedAnimation",
        "hkaLosslessCompressedAnimation",
    };

    /// Where hkaAnimation keeps what every animation has, whatever it is compressed as. The
    /// interleaved class inherits all of it and adds its two arrays after.
    private const int Type = 0x10;
    private const int Duration = 0x14;
    private const int TransformTracks = 0x18;
    private const int FloatTracks = 0x1C;
    private const int ExtractedMotion = 0x20;
    private const int AnnotationTracks = 0x28;
    private const int Transforms = 0x38;
    private const int Floats = 0x48;

    /// hkaAnimation::AnimationType, from the enum the game itself registers.
    private const int InterleavedType = 1;

    public sealed record Result(byte[] Bytes, int Frames, int Tracks, string From, long Grew)
    {
        public override string ToString() =>
            $"{From} written out as {Frames} frame(s) of {Tracks} track(s), {Grew} bytes larger";
    }

    /// Rewrites a file's animation as an uncompressed one and returns the new bytes.
    ///
    /// Refuses rather than approximating. An animation with float tracks is turned away because
    /// nothing here decodes them, and writing a transform array without the matching float array
    /// would leave the engine reading past the end of one it was told is as long as the other.
    public static Result Interleave(string hkxPath, HkxAnimationData decoded)
    {
        var image = PackfileImage.Read(hkxPath);
        long was = new System.IO.FileInfo(hkxPath).Length;

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

        // The old animation's own values, read before anything is appended. Duration is the one that
        // matters most: the engine divides it by the frame count to find where a frame sits in time,
        // so a wrong one retimes the whole clip without changing a single transform.
        float duration = BitConverter.ToSingle(data.Data, at + Duration);
        var annotations = objects.ArrayAt(at + AnnotationTracks);
        int self = image.Sections.IndexOf(data);
        var motion = data.Globals().FirstOrDefault(g => g.Source == at + ExtractedMotion);
        bool hasMotion = data.Globals().Any(g => g.Source == at + ExtractedMotion);

        var added = NativeAppend.Object(image, InterleavedClass);

        // Read again: the append rewrote the tables and added an object, and everything below is
        // written through this view.
        objects = new PackfileObjects(image);
        int made = added.Offset;

        BitConverter.GetBytes(InterleavedType).CopyTo(data.Data, made + Type);
        BitConverter.GetBytes(duration).CopyTo(data.Data, made + Duration);
        BitConverter.GetBytes(tracks).CopyTo(data.Data, made + TransformTracks);
        BitConverter.GetBytes(0).CopyTo(data.Data, made + FloatTracks);

        // The annotations get a run of their own rather than sharing the one they came from.
        //
        // Sharing was tried first and it is what the format allows: every array in every vanilla file
        // carries the flag that says the memory is not Havok's to free, so two arrays naming one run
        // is two readers and no owner. It fails anyway, and not at runtime. The pointer tables are in
        // the order the writer walked the objects, and a shared run's inner pointers can only sit in
        // one place in that order. hkxpack reads the second object's names as empty and then loses
        // its place entirely, dropping the transform array that follows. Copying gives the new object
        // sources of its own, which the reorder can then put where its own walk implies.
        if (annotations is { Count: > 0 })
        {
            int copied = CopyStructRun(image, data, annotations.At, annotations.Count, "hkaAnnotationTrack");
            data.SetLocal(made + AnnotationTracks, copied);
            Array.Copy(data.Data, at + AnnotationTracks + 8, data.Data, made + AnnotationTracks + 8, 8);
        }

        if (hasMotion) data.SetGlobal(made + ExtractedMotion, motion.Section, motion.Destination);

        // The frames themselves, frame major, which is Havok's own indexing rather than a guess.
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

        // No float tracks, so the array beside it stays empty, which is a count of zero and no
        // pointer at all rather than a pointer at nothing.
        BitConverter.GetBytes(0).CopyTo(data.Data, made + Floats + 8);
        BitConverter.GetBytes(0x80000000u).CopyTo(data.Data, made + Floats + 12);

        // Everything that named the old animation now names the new one. Done by retargeting every
        // pointer rather than by finding the binding, because a file names its animation from more
        // than one place: the binding holds it and the container lists it, and leaving either behind
        // would load the clip that was replaced.
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

    /// Where hkaDefaultAnimatedReferenceFrame keeps the root's travel. Read out of the class table
    /// rather than written down, the same as everything else here.
    private const int MotionDuration = 0x40;
    private const int MotionSamples = 0x48;
    private const int MotionSize = 96;

    /// A clip whose length changes, which the default save path cannot express.
    ///
    /// `Recompress` was built for an in place frame edit, where the clip keeps its length, its
    /// annotations and its travel, and for that it is right to take all three from the file it is
    /// rewriting. A cut changes all three at once, and every one of them lives somewhere the frames
    /// do not: the duration is a field on the animation, the annotations are a run of structs hanging
    /// off it, and the travel is a separate object reached by pointer.
    ///
    /// So a caller that changed the frame count says so by handing this over, and a caller that did
    /// not hands over nothing and gets exactly the behaviour that was there before.
    ///
    /// `FromTime` and `ToTime` are the span of the original clip's timeline that was kept. The
    /// annotations in the file are still at their original times when they are copied, so the writer
    /// is told which of them survive and how far back to move the ones that do, rather than being
    /// handed a flat list that has lost which track each belonged to.
    public sealed record Cut(float Duration, float FromTime, float ToTime, RootMotion.Motion? Motion)
    {
        public static Cut Of(AnimationEdit.Trimmed trimmed) =>
            new(trimmed.Animation.Duration, trimmed.FromTime, trimmed.ToTime, trimmed.Motion);

        public override string ToString() =>
            $"{FromTime:F3}s to {ToTime:F3}s kept as a {Duration:F3}s clip, " +
            (Motion is { Any: true } ? $"{Motion.Samples.Count} motion sample(s)" : "no new motion");
    }

    /// Where hkaSplineCompressedAnimation keeps what the interleaved class does not have.
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

    /// Rewrites a file's animation as a spline compressed one and returns the new bytes.
    ///
    /// This is what `Interleave` was standing in for. Writing every frame out uncompressed is correct
    /// and about six times the size, and it was the only option while nothing could produce a spline
    /// blob. Now that something can, an edited clip can be saved as the kind of animation it was.
    ///
    /// Everything the file already carries is kept: its duration, its annotations, its extracted
    /// motion, and every pointer that named the old animation is aimed at the new one. The old bytes
    /// stay where they are and go unreferenced, which is the same shape as every other write here.
    ///
    /// Passing a `Cut` is what a clip that changed length needs, and passing nothing is what every
    /// caller that was here before passes. The two paths are deliberately the same code with the
    /// three values read from different places, so an in place frame edit cannot start behaving
    /// differently because a cut was added beside it.
    public static Result Recompress(string hkxPath, HkxAnimationData decoded,
        SplineEncoder.Options? options = null, bool dropReplaced = true, Cut? cut = null)
    {
        var image = PackfileImage.Read(hkxPath);
        long was = new System.IO.FileInfo(hkxPath).Length;

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

        // Encoded before anything is appended, so a clip the encoder turns away leaves the file
        // untouched rather than half rewritten.
        var blob = SplineEncoder.Encode(decoded, options);

        float duration = cut?.Duration ?? BitConverter.ToSingle(data.Data, at + Duration);
        var annotations = objects.ArrayAt(at + AnnotationTracks);
        int self = image.Sections.IndexOf(data);
        var motion = data.Globals().FirstOrDefault(g => g.Source == at + ExtractedMotion);
        bool hasMotion = data.Globals().Any(g => g.Source == at + ExtractedMotion);

        // Deletion is addressed by the id a file's objects are numbered with, not by the position in
        // the instance list, and the two differ by where the numbering starts.
        int replacedId = objects.IndexOf(source) + NativeGraphModel.FirstId;

        // The travel object goes the same way as the animation when it is replaced, for the same
        // reason: nothing points at it afterwards, and a file carrying two of them is a file that
        // grew for no purpose. Read before the append, while the instance list still describes the
        // file as it was.
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

            // The copy is of the annotations as they were, at the times they were at. A clip that
            // changed length has to have them moved before it is saved, and it is done on the copy
            // rather than on the original so a refusal further down leaves the file untouched.
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

        // Both of these are empty in every shipped animation sampled, so they are written as empty
        // rather than left as whatever the appended object came with.
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

        // The animation that was replaced is now unreferenced, so it comes out.
        //
        // Every other write here leaves what it displaced in the file, because nothing could take an
        // object out without moving everything after it. That is no longer true, and leaving it would
        // cost more than it saves: the whole point of writing a spline blob rather than an
        // uncompressed one is size, and a file carrying both animations is larger than one carrying
        // the uncompressed version alone.
        //
        // Deleting renumbers every id above the hole, which is the hazard #19 is about. It is done
        // here anyway and offered as a switch, because an animation file is reached by pointer from
        // the behaviour that plays it rather than by id, and because the alternative is a saved clip
        // that is bigger than the one it replaced. Anyone who would rather not take it can pass false.
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

    /// Moves a copied annotation run onto a cut clip's timeline, dropping what falls outside it.
    ///
    /// The run is a track per transform track, and each track holds an array of times and texts. What
    /// changes is only the times and how many of them there are, so the tracks themselves stay where
    /// they are, keep their names, and keep the order they were copied in. A track whose annotations
    /// all fall outside the cut becomes an empty array, which is the same shape as the empty arrays
    /// almost every track in every shipped clip already has.
    ///
    /// Compacting is why the text pointers are read out and written back rather than left alone. An
    /// annotation is a time and a pointer at a string, and the pointer is a fixup naming the slot it
    /// sits in. Moving an annotation up the array means its text has to be named from the slot it
    /// moved to, and the slots left over at the end have to stop naming anything at all.
    private static void RebaseAnnotations(PackfileSection data, int run, int count, Cut cut)
    {
        int trackStride = HavokClassTypes.Shipped["hkaAnnotationTrack"]?.Size ?? 24;
        int noteStride = HavokClassTypes.Shipped["hkaAnnotationTrackAnnotation"]?.Size ?? 16;

        // The whole table is read once and written once. Every one of these is a source offset in it,
        // and a shipped clip carries tens of thousands of entries, so rewriting it per annotation
        // would be the cost of the save.
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
            // Removing from the list renumbers everything after it, so the entry is neutralised by
            // pointing it at itself and swept afterwards instead.
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
                kept.Add((Math.Clamp(when - cut.FromTime, 0, cut.Duration),
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

    /// Half a frame at thirty, which is what the slack on an annotation's time is worth. An
    /// annotation sitting on the first kept frame can land a hair either side of it once the time has
    /// been through a float divide, and dropping it would be dropping the one the cut was aimed at.
    private const float Slack = 1f / 60f;

    /// Writes a new hkaDefaultAnimatedReferenceFrame holding the cut clip's travel.
    ///
    /// This is the part of a cut that is not a field write. The old object's samples cover the old
    /// frames, so the new clip needs an object of its own rather than a pointer at that one, and an
    /// object of its own means an append plus an array.
    ///
    /// The body is copied off the old one rather than filled in from nothing. Two of its fields are
    /// the axes the clip declares as up and forward and would have to be guessed otherwise, and one
    /// of them, the frame type, is a field hkxpack does not write at all, so there is nothing to read
    /// it from except the object already in the file. Copying from offset sixteen leaves the object
    /// header the append produced, which is what every other appended object here carries.
    private static int WriteReferenceFrame(PackfileImage image, PackfileSection data, int from,
                                           RootMotion.Motion motion)
    {
        var added = NativeAppend.Object(image, ReferenceFrameClass);
        int made = added.Offset;

        Array.Copy(data.Data, from + 16, data.Data, made + 16, MotionSize - 16);

        // The array field came across as bytes, which is a count and a capacity naming a run this
        // object has no fixup for. It is written properly below; cleared here so a throw between the
        // two cannot leave an array claiming samples that nothing points at.
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
                // The fourth lane of a sample is not a position's w. It is how far the root has
                // turned about the up axis, which is why this is written rather than left at zero
                // the way a transform's fourth lane is.
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

    /// Writes an hkArray of uint32 as its own run, or as an empty array with no pointer at all.
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

    /// Copies a run of structs to the end of the section, with everything hanging off it.
    ///
    /// The bytes alone are not the run. A struct can hold a name, and a name is a pointer with a
    /// fixup naming it; it can hold an array, and that is another run somewhere else with a fixup of
    /// its own. Copying the bytes and stopping would give a second run whose pointers are zero, which
    /// reads as a set of annotations with no text in them.
    ///
    /// The text at the far end is copied too, and it was shared until the trim gate caught what that
    /// costs. Sharing is fine while both objects stay in the file, and the pointer tables do not mind
    /// it: a destination has no position in them, so only the sources have to belong to one object.
    /// It stops being fine the moment the object that was copied from is deleted, which is what a
    /// save has done since it started dropping the animation it replaced. Laying the file out again
    /// walks the objects in order and gives each run to the first object that reaches it, so a shared
    /// string belongs to the old object, goes out with it, and the new object's pointer at it is
    /// dropped as naming a byte that is no longer there. The result loads and reads back with the
    /// right number of annotations at the right times and every one of them blank.
    private static int CopyStructRun(PackfileImage image, PackfileSection data, int from, int count,
                                     string elementClass, int depth = 0,
                                     Dictionary<int, int>? copiedText = null)
    {
        var types = HavokClassTypes.Shipped;

        // Nothing in the corpus nests anywhere near this deep; the guard is against a class that
        // somehow holds itself rather than against real data.
        if (depth > 6 || count <= 0 || !types.Knows(elementClass)) return -1;

        int stride = types[elementClass]?.Size ?? 0;
        if (stride <= 0) return -1;

        // One copy per string however many places name it. Every annotation track in a clip carries
        // the same track name, so copying per element would write it ninety four times.
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

                // A run of plain values, or of pointers at text. Either way the bytes come across and
                // then anything pointing out of them is repeated, element by element.
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

    /// A copy of a null terminated string at the end of the section, or the copy already made of it.
    ///
    /// The bytes are duplicated rather than pointed at for the reason above: the new object has to
    /// own everything it names, or a deletion of the object it was copied from takes the text with
    /// it. Nothing is paid for in the end, because the original goes out with that object.
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
        // The fourth lane is left at zero. It carries nothing: across all 119 vanilla skeletons and
        // 3,769 reference pose transforms it takes 2,838 different values on the translation, which
        // is leftover memory rather than a number anybody wrote.
    }

    private static void Write(byte[] into, int at, Quaternion value)
    {
        BitConverter.GetBytes(value.X).CopyTo(into, at);
        BitConverter.GetBytes(value.Y).CopyTo(into, at + 4);
        BitConverter.GetBytes(value.Z).CopyTo(into, at + 8);
        BitConverter.GetBytes(value.W).CopyTo(into, at + 12);
    }

    /// Havok asserts that a stored rotation is within half a unit of normalised and then normalises
    /// it anyway, so a decoder's rounding is expected and a wildly wrong one is not. Doing it here
    /// means the file holds what the engine would have made of it.
    private static Quaternion Normalized(Quaternion q)
    {
        float length = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
        return length > 1e-6f ? new Quaternion(q.X / length, q.Y / length, q.Z / length, q.W / length)
                              : Quaternion.Identity;
    }
}
