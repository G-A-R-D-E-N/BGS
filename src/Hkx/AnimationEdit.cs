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

    /// A clip after its length changed, and what that cost.
    ///
    /// `Scale` is the length it actually came out at over the length it went in at, which is not
    /// always the scale that was asked for: a clip keeping its frame rate has to land on a whole
    /// number of frames, so a request to make a 41 frame clip 1.5 times longer gives 61 frames and a
    /// scale of 1.5 exactly, and a request for 1.51 gives the same 61 frames. Everything downstream,
    /// the annotations and the travel, is moved by the scale that happened rather than the one that
    /// was asked for, or they would end up describing a clip of a different length to the frames.
    ///
    /// `PositionError` and `RotationError` are what the resampling cost, measured rather than
    /// bounded: the retimed clip is read back at each of the original frame's own moments and
    /// compared to the frame that was there. Zero when nothing was resampled.
    public sealed record Retimed(HkxAnimationData Animation, RootMotion.Motion? Motion, float Scale,
                                 bool Resampled, float PositionError, float RotationError)
    {
        public override string ToString() =>
            $"{Animation.NumFrames} frame(s) of {Animation.Duration:F3}s at {Scale:F3} times the " +
            "length, " + (Resampled ? $"resampled for {PositionError:F4} unit(s) and " +
                                      $"{RotationError:F5} radian(s) of error"
                                    : "the same frames at a different rate, exactly");
    }

    /// How much error a retime is allowed to cost before it is refused rather than written.
    ///
    /// There is a real limit here and it is not a matter of taste. Making a clip shorter at a fixed
    /// frame rate throws frames away, and a clip whose fastest movement happens between two of the
    /// frames that are kept cannot be recovered from what is left. That is information loss and the
    /// honest thing is to say so with the number rather than to write it quietly.
    /// Nothing applies one by default, and that is a decision the measurement made rather than a gap.
    /// Halving a clip at a fixed frame rate costs a median of 0.37 units of position across the
    /// 13,155 shipped clips `symrm retime` sweeps, and a tenth of them cost more than 4.4. Losing
    /// that much is what halving a clip is, not a fault in doing it, so a default that refused it
    /// would refuse a third of ordinary requests and teach whoever hit it to turn the check off. The
    /// error is measured on every retime and handed back on `Retimed` whether or not anybody sets a
    /// limit; a caller who wants a limit passes one.
    public sealed record Budget(float Position, float Rotation)
    {
        /// The worst hundredth of the corpus, for a caller that wants to refuse only what is far
        /// outside what a retime normally costs. Measured with `symrm retime` over the tree, on the
        /// halving pass: p99 of 37.2 units and 1.06 radians, against a worst of 445.7 and 1.5708.
        /// Not applied anywhere; it is a number to pass, not a default.
        public static readonly Budget Tail = new(37f, 1.05f);

        public override string ToString() =>
            $"{Position} unit(s) of position and {Rotation} radian(s) of rotation";
    }

    /// Makes a clip longer or shorter.
    ///
    /// Two ways to do that and they are not the same operation, which is why this takes a switch
    /// rather than guessing:
    ///
    /// - Keeping the frame rate, which is the default and what "play this at half speed" means. The
    ///   clip gets more frames or fewer, each one read between the frames that were there, and the
    ///   frames per second stay what they were. This interpolates and therefore costs error.
    /// - Keeping the frames, which changes how long each one is shown for. Nothing is read between
    ///   anything, so it is exact, and it is the right answer when a clip is being slowed down for
    ///   playback rather than being rebuilt at a new rate.
    ///
    /// Rotation is read with a proper spherical interpolation rather than a straight one. A straight
    /// interpolation between two rotations is not a rotation, and normalising it afterwards gives a
    /// rotation that is on the right path at the wrong speed. Over a single frame at thirty the
    /// difference is small and over a clip slowed to a quarter speed it is not.
    public static Retimed Retime(HkxAnimationData animation, RootMotion.Motion? motion, float scale,
                                 bool keepFrameRate = true, Budget? budget = null)
    {
        if (animation.NumFrames < 2 || animation.Tracks.Count == 0)
            throw new InvalidOperationException(
                "This animation decoded to fewer than two frames, so there was nothing to retime.");

        if (!float.IsFinite(scale) || scale <= 0)
            throw new InvalidOperationException(
                $"A clip cannot be retimed by {scale}: the scale has to be a positive number.");

        // Wide enough that nothing anybody would ask for is turned away, and narrow enough that a
        // scale arrived at by dividing by something near zero does not try to build a clip of
        // millions of frames.
        if (scale < 0.01f || scale > 100f)
            throw new InvalidOperationException(
                $"A scale of {scale} is outside the hundredth to hundredfold this will retime by, so " +
                "the clip was left alone rather than rebuilt at a length nothing asked for.");

        foreach (var track in animation.Tracks)
            if (track.Translations.Count < animation.NumFrames ||
                track.Rotations.Count < animation.NumFrames ||
                track.Scales.Count < animation.NumFrames)
                throw new InvalidOperationException(
                    $"A track decoded to fewer frames than the {animation.NumFrames} this animation " +
                    "declares, so it was not retimed.");

        float frameDuration = FrameDuration(animation);
        float was = (animation.NumFrames - 1) * frameDuration;

        int frames = keepFrameRate
            ? Math.Max(2, (int)MathF.Round((animation.NumFrames - 1) * scale) + 1)
            : animation.NumFrames;

        float perFrame = keepFrameRate ? frameDuration : frameDuration * scale;
        float duration = (frames - 1) * perFrame;

        // The scale that happened, which is the one everything else has to follow.
        float happened = was > 0 ? duration / was : scale;

        var made = new HkxAnimationData
        {
            Duration = duration,
            NumFrames = frames,
            NumTracks = animation.NumTracks,
            MaxFramesPerBlock = animation.MaxFramesPerBlock,
            FrameDuration = perFrame,
            BoneNames = animation.BoneNames.ToList(),
            TrackToBoneIndices = animation.TrackToBoneIndices.ToList(),
            OriginalSkeletonName = animation.OriginalSkeletonName,
            BlendHint = animation.BlendHint,
            AnimationClass = animation.AnimationClass,
            Skeleton = animation.Skeleton,
        };

        bool resampled = frames != animation.NumFrames;

        foreach (var track in animation.Tracks)
        {
            var built = new HkxTrackData { RotationAnimated = track.RotationAnimated };
            for (int axis = 0; axis < 3; axis++)
            {
                built.TranslationAnimated[axis] = track.TranslationAnimated[axis];
                built.ScaleAnimated[axis] = track.ScaleAnimated[axis];
            }

            if (!resampled)
            {
                built.Translations.AddRange(track.Translations.GetRange(0, animation.NumFrames));
                built.Rotations.AddRange(track.Rotations.GetRange(0, animation.NumFrames));
                built.Scales.AddRange(track.Scales.GetRange(0, animation.NumFrames));
            }
            else
                for (int f = 0; f < frames; f++)
                {
                    float at = frames > 1 ? (float)f / (frames - 1) : 0;
                    built.Translations.Add(Between(track.Translations, at));
                    built.Rotations.Add(Turned(track.Rotations, at));
                    built.Scales.Add(Between(track.Scales, at));
                }

            made.Tracks.Add(built);
        }

        foreach (var note in animation.Annotations)
            made.Annotations.Add(new HkxAnnotation
            {
                Time = Math.Clamp(note.Time * happened, 0, duration),
                Text = note.Text,
            });

        // What the resampling cost, asked the only way worth asking: read the new clip back at each
        // moment the old clip had a frame at, and compare it to the frame that was there. A measure
        // taken against the new frames instead would compare the interpolation to itself.
        float positionError = 0, rotationError = 0;
        if (resampled)
            for (int t = 0; t < animation.Tracks.Count; t++)
                for (int f = 0; f < animation.NumFrames; f++)
                {
                    float at = animation.NumFrames > 1 ? (float)f / (animation.NumFrames - 1) : 0;
                    positionError = MathF.Max(positionError,
                        (Between(made.Tracks[t].Translations, at) - animation.Tracks[t].Translations[f]).Length());
                    rotationError = MathF.Max(rotationError,
                        SplineQuat.AngleBetween(Turned(made.Tracks[t].Rotations, at),
                                                animation.Tracks[t].Rotations[f]));
                }

        if (budget != null && (positionError > budget.Position || rotationError > budget.Rotation))
            throw new InvalidOperationException(
                $"Retiming this clip to {happened:F3} times its length loses {positionError:F3} " +
                $"unit(s) of position and {rotationError:F4} radian(s) of rotation, past the " +
                $"{budget} allowed, so it was not written. Frames it had are between the frames a " +
                "clip this length can hold, and nothing can read them back out afterwards.");

        return new Retimed(made, RetimeMotion(motion, animation.NumFrames, frames, duration, resampled),
                           happened, resampled, positionError, rotationError);
    }

    /// A value read between the frames either side of a point along the clip.
    public static Vector3 Between(IReadOnlyList<Vector3> frames, float at)
    {
        if (frames.Count == 0) return Vector3.Zero;
        if (frames.Count == 1) return frames[0];

        float where = Math.Clamp(at, 0, 1) * (frames.Count - 1);
        int first = Math.Min((int)where, frames.Count - 2);
        return Vector3.Lerp(frames[first], frames[first + 1], where - first);
    }

    /// A rotation read between the two either side of a point, along the arc rather than across it.
    public static Quaternion Turned(IReadOnlyList<Quaternion> frames, float at)
    {
        if (frames.Count == 0) return Quaternion.Identity;
        if (frames.Count == 1) return frames[0];

        float where = Math.Clamp(at, 0, 1) * (frames.Count - 1);
        int first = Math.Min((int)where, frames.Count - 2);
        return Quaternion.Normalize(Quaternion.Slerp(frames[first], frames[first + 1], where - first));
    }

    /// The root's travel over a clip that changed length.
    ///
    /// The travel does not move. A clip played at half speed goes exactly as far, it just takes twice
    /// as long about it, so the samples describe the same path and only the duration beside them
    /// changes. What has to follow the frames is how many samples there are, and only for the clips
    /// that sample once per frame; a two sample frame is linear and stays two whatever happens.
    private static RootMotion.Motion? RetimeMotion(RootMotion.Motion? motion, int was, int frames,
                                                   float duration, bool resampled)
    {
        if (motion is not { Any: true }) return motion;

        var made = new RootMotion.Motion { Up = motion.Up, Forward = motion.Forward, Duration = duration };

        if (!resampled || motion.Samples.Count != was)
        {
            made.Samples.AddRange(motion.Samples);
            return made;
        }

        for (int f = 0; f < frames; f++)
            made.Samples.Add(RootMotion.At(motion, frames > 1 ? (float)f / (frames - 1) : 0));

        return made;
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
