using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

public static class AnimationEdit
{

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

    public static float FrameDuration(HkxAnimationData animation) =>
        animation.FrameDuration > 0 ? animation.FrameDuration
        : animation.NumFrames > 1 ? animation.Duration / (animation.NumFrames - 1)
        : 0;

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

    public sealed record Retimed(HkxAnimationData Animation, RootMotion.Motion? Motion, float Scale,
                                 bool Resampled, float PositionError, float RotationError)
    {
        public override string ToString() =>
            $"{Animation.NumFrames} frame(s) of {Animation.Duration:F3}s at {Scale:F3} times the " +
            "length, " + (Resampled ? $"resampled for {PositionError:F4} unit(s) and " +
                                      $"{RotationError:F5} radian(s) of error"
                                    : "the same frames at a different rate, exactly");
    }

    public sealed record Budget(float Position, float Rotation)
    {

        public static readonly Budget Tail = new(37f, 1.05f);

        public override string ToString() =>
            $"{Position} unit(s) of position and {Rotation} radian(s) of rotation";
    }

    public static Retimed Retime(HkxAnimationData animation, RootMotion.Motion? motion, float scale,
                                 bool keepFrameRate = true, Budget? budget = null)
    {
        if (animation.NumFrames < 2 || animation.Tracks.Count == 0)
            throw new InvalidOperationException(
                "This animation decoded to fewer than two frames, so there was nothing to retime.");

        if (!float.IsFinite(scale) || scale <= 0)
            throw new InvalidOperationException(
                $"A clip cannot be retimed by {scale}: the scale has to be a positive number.");

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

    public static Vector3 Between(IReadOnlyList<Vector3> frames, float at)
    {
        if (frames.Count == 0) return Vector3.Zero;
        if (frames.Count == 1) return frames[0];

        float where = Math.Clamp(at, 0, 1) * (frames.Count - 1);
        int first = Math.Min((int)where, frames.Count - 2);
        return Vector3.Lerp(frames[first], frames[first + 1], where - first);
    }

    public static Quaternion Turned(IReadOnlyList<Quaternion> frames, float at)
    {
        if (frames.Count == 0) return Quaternion.Identity;
        if (frames.Count == 1) return frames[0];

        float where = Math.Clamp(at, 0, 1) * (frames.Count - 1);
        int first = Math.Min((int)where, frames.Count - 2);
        return Quaternion.Normalize(Quaternion.Slerp(frames[first], frames[first + 1], where - first));
    }

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

    public static bool Inside(float time, float from, float to, float frameDuration)
    {
        float slack = frameDuration > 0 ? frameDuration / 2 : 1e-4f;
        return time >= from - slack && time <= to + slack;
    }

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
