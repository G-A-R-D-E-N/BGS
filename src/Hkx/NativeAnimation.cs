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
    public static Result Recompress(string hkxPath, HkxAnimationData decoded,
        SplineEncoder.Options? options = null, bool dropReplaced = true)
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

        float duration = BitConverter.ToSingle(data.Data, at + Duration);
        var annotations = objects.ArrayAt(at + AnnotationTracks);
        int self = image.Sections.IndexOf(data);
        var motion = data.Globals().FirstOrDefault(g => g.Source == at + ExtractedMotion);
        bool hasMotion = data.Globals().Any(g => g.Source == at + ExtractedMotion);

        // Deletion is addressed by the id a file's objects are numbered with, not by the position in
        // the instance list, and the two differ by where the numbering starts.
        int replacedId = objects.IndexOf(source) + NativeGraphModel.FirstId;

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
        }

        if (hasMotion) data.SetGlobal(made + ExtractedMotion, motion.Section, motion.Destination);

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
            try { NativeRemove.Delete(image, new[] { replacedId }); }
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
    /// The text at the far end is shared rather than copied, because a destination has no position in
    /// the pointer tables and therefore nothing to get out of order. It is only the sources that have
    /// to belong to one object.
    private static int CopyStructRun(PackfileImage image, PackfileSection data, int from, int count,
                                     string elementClass, int depth = 0)
    {
        var types = HavokClassTypes.Shipped;

        // Nothing in the corpus nests anywhere near this deep; the guard is against a class that
        // somehow holds itself rather than against real data.
        if (depth > 6 || count <= 0 || !types.Knows(elementClass)) return -1;

        int stride = types[elementClass]?.Size ?? 0;
        if (stride <= 0) return -1;

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
                    if (locals.TryGetValue(old, out int text)) data.SetLocal(made, text);
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
                    int copied = CopyStructRun(image, data, inner, held, member.CType, depth + 1);
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
                        data.SetLocal(landed + e * width, text);
            }
        }

        return to;
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
