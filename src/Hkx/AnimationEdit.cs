using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

// Cutting a span of frames out of a clip and keeping everything that hangs off them in step.
//
// The frame maths is the easy half and it is not why this is its own file. A clip is four things that
// all measure the same timeline: the frames, the clip's own duration, the annotations that fire at
// points along it, and the root's travel sampled across it. Change the frame count and the other
// three are wrong unless they are changed with it, and nothing about the file says so. A trim that
// only slices the frames produces a clip that loads, plays, and is wrong: the header still claims the
// old length, so the engine spreads the kept frames across it and plays them slowly.
//
// Nothing here interpolates. A trim keeps whole frames and drops whole frames, so every kept frame is
// the frame that was there and the only error in the result is the encoder's, which is measured
// separately. Retiming by resampling is a different job with an error budget of its own.
//
// The rules below are measured against the shipped corpus rather than reasoned about. See the
// `symrm cliptrim` counts: 14,370 clips hold an animation, 8,848 carry annotations, and 13,543 carry
// extracted motion, of which 11,882 sample the root once per animation frame and 1,661 carry exactly
// two samples whatever their frame count.
public static class AnimationEdit
{
    /// A clip after the cut, and the span of the original timeline it came from.
    ///
    /// `FromTime` and `ToTime` are carried because the writer needs them: the annotations in the file
    /// are still at their original times when it copies them, so it has to be told which span to keep
    /// and how far back to move what it kept.
    public sealed record Trimmed(HkxAnimationData Animation, RootMotion.Motion? Motion,
                                 int FirstFrame, int LastFrame, float FromTime, float ToTime,
                                 int AnnotationsDropped)
    {
        public int Frames => Animation.NumFrames;

        public override string ToString() =>
            $"frames {FirstFrame} to {LastFrame} kept, {Frames} frame(s) of " +
            $"{Animation.Duration:F3}s cut from {FromTime:F3}s to {ToTime:F3}s, " +
            $"{AnnotationsDropped} annotation(s) dropped, " +
            (Motion is { Any: true } ? $"{Motion.Samples.Count} motion sample(s)" : "no motion");
    }

    /// How long one frame lasts, taken from the clip's own field where it has one and derived from
    /// the duration where it does not. The same expression `SplineEncoder` uses, deliberately: if the
    /// two disagreed then a trimmed clip's declared length and its encoded length would differ.
    public static float FrameDuration(HkxAnimationData animation) =>
        animation.FrameDuration > 0 ? animation.FrameDuration
        : animation.NumFrames > 1 ? animation.Duration / (animation.NumFrames - 1)
        : 0;

    /// Keeps frames `firstFrame` to `lastFrame` inclusive and throws everything else away.
    ///
    /// Refuses rather than guessing. A cut of fewer than two frames is not a clip: the spline format
    /// stores curves between frames and a single frame has no interval, so it would have to be
    /// written as something other than the animation it was.
    public static Trimmed Trim(HkxAnimationData animation, RootMotion.Motion? motion,
                               int firstFrame, int lastFrame)
    {
        if (animation.NumFrames <= 0 || animation.Tracks.Count == 0)
            throw new InvalidOperationException(
                "This animation decoded to no frames, so there was nothing to cut.");

        if (firstFrame < 0 || lastFrame >= animation.NumFrames || firstFrame > lastFrame)
            throw new InvalidOperationException(
                $"Frames {firstFrame} to {lastFrame} are not inside this clip, which has " +
                $"{animation.NumFrames} frame(s) numbered 0 to {animation.NumFrames - 1}.");

        int kept = lastFrame - firstFrame + 1;
        if (kept < 2)
            throw new InvalidOperationException(
                "A cut has to keep at least two frames, because a clip is stored as curves between " +
                "frames and one frame has no interval to store.");

        foreach (var track in animation.Tracks)
            if (track.Translations.Count < animation.NumFrames ||
                track.Rotations.Count < animation.NumFrames ||
                track.Scales.Count < animation.NumFrames)
                throw new InvalidOperationException(
                    $"A track decoded to fewer frames than the {animation.NumFrames} this animation " +
                    "declares, so it was not cut.");

        float frameDuration = FrameDuration(animation);
        float from = firstFrame * frameDuration;
        float to = lastFrame * frameDuration;
        float duration = (kept - 1) * frameDuration;

        var cut = new HkxAnimationData
        {
            Duration = duration,
            NumFrames = kept,
            NumTracks = animation.NumTracks,
            MaxFramesPerBlock = animation.MaxFramesPerBlock,
            FrameDuration = frameDuration,
            BoneNames = animation.BoneNames.ToList(),
            TrackToBoneIndices = animation.TrackToBoneIndices.ToList(),
            OriginalSkeletonName = animation.OriginalSkeletonName,
            BlendHint = animation.BlendHint,
            AnimationClass = animation.AnimationClass,
            Skeleton = animation.Skeleton,
        };

        foreach (var track in animation.Tracks)
        {
            var slice = new HkxTrackData
            {
                Translations = track.Translations.GetRange(firstFrame, kept),
                Rotations = track.Rotations.GetRange(firstFrame, kept),
                Scales = track.Scales.GetRange(firstFrame, kept),
                RotationAnimated = track.RotationAnimated,
            };

            for (int axis = 0; axis < 3; axis++)
            {
                slice.TranslationAnimated[axis] = track.TranslationAnimated[axis];
                slice.ScaleAnimated[axis] = track.ScaleAnimated[axis];
            }

            cut.Tracks.Add(slice);
        }

        // An annotation outside the kept span goes, and one inside it moves back to where it now
        // sits. Rounded to the frame the annotation belongs to rather than compared exactly, because
        // a time that came out of a float divide can land a hair either side of a boundary and drop
        // an annotation that is sitting exactly on the first kept frame.
        int dropped = 0;
        foreach (var note in animation.Annotations)
        {
            if (!Inside(note.Time, from, to, frameDuration)) { dropped++; continue; }
            cut.Annotations.Add(new HkxAnnotation
            {
                Time = Math.Clamp(note.Time - from, 0, duration),
                Text = note.Text,
            });
        }

        return new Trimmed(cut, TrimMotion(motion, animation.NumFrames, firstFrame, lastFrame, duration),
                           firstFrame, lastFrame, from, to, dropped);
    }

    /// Whether a time sits inside the kept span, with half a frame of slack at each end.
    public static bool Inside(float time, float from, float to, float frameDuration)
    {
        float slack = frameDuration > 0 ? frameDuration / 2 : 1e-4f;
        return time >= from - slack && time <= to + slack;
    }

    /// The root's travel over the kept span.
    ///
    /// Two shapes, both measured over the corpus rather than assumed, and neither needs refusing:
    ///
    /// - One sample per animation frame, 11,882 clips. The samples are sliced over the same index
    ///   range as the frames, so every kept sample is the sample that was there.
    /// - Exactly two samples, 1,661 clips, whatever the frame count. That is a reference frame that
    ///   is linear across the whole clip, so a cut keeps two samples read at the new start and end.
    ///   Exact for a linear frame rather than an approximation of one.
    ///
    /// Anything else is resampled to one sample per kept frame, which is the honest answer for a
    /// shape the corpus does not contain: it keeps the path and pays interpolation error, and it is
    /// reached by nothing the game ships.
    ///
    /// The kept samples are then rebased so the cut clip starts where every shipped clip starts, at
    /// the origin facing along its own forward. That is a change of frame and not a translation: the
    /// path after the first kept sample was written in the clip's starting frame, so it is turned
    /// back by the first kept sample's own turn as well as moved back by its position.
    private static RootMotion.Motion? TrimMotion(RootMotion.Motion? motion, int frames,
                                                 int firstFrame, int lastFrame, float duration)
    {
        if (motion is not { Any: true }) return motion;

        int kept = lastFrame - firstFrame + 1;
        var taken = new List<RootMotion.Sample>();

        if (motion.Samples.Count == frames)
            taken.AddRange(motion.Samples.GetRange(firstFrame, kept));
        else if (motion.Samples.Count == 2)
        {
            taken.Add(RootMotion.At(motion, frames > 1 ? (float)firstFrame / (frames - 1) : 0));
            taken.Add(RootMotion.At(motion, frames > 1 ? (float)lastFrame / (frames - 1) : 1));
        }
        else
            for (int f = 0; f < kept; f++)
                taken.Add(RootMotion.At(motion, frames > 1 ? (float)(firstFrame + f) / (frames - 1) : 0));

        var cut = new RootMotion.Motion { Up = motion.Up, Forward = motion.Forward, Duration = duration };
        cut.Samples.AddRange(Rebased(taken, motion.Up));
        return cut;
    }

    /// The same path read from its own first sample: nothing moved relative to anything else, only
    /// said from where the cut clip now begins.
    public static IEnumerable<RootMotion.Sample> Rebased(IReadOnlyList<RootMotion.Sample> samples,
                                                         Vector3 up)
    {
        if (samples.Count == 0) yield break;

        var first = samples[0];
        var axis = up.LengthSquared() > 1e-6f ? Vector3.Normalize(up) : Vector3.UnitZ;
        var back = Matrix4x4.CreateFromAxisAngle(axis, -first.TurnRadians);

        foreach (var sample in samples)
            yield return new RootMotion.Sample(
                Vector3.Transform(sample.Position - first.Position, back),
                sample.TurnRadians - first.TurnRadians);
    }
}
