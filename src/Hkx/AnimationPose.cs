using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

// Where every bone is at a given frame, as world positions and the lines between them.
//
// The whole of this is arithmetic on data both readers already return, so it is checkable without a
// window. The viewport draws what comes out of here and decides nothing itself, which is deliberate:
// a pose that is wrong should be provable wrong in a test rather than only visible as a shape that
// looks odd.
//
// Two things this owns that neither reader does:
//
//   A track drives a channel or leaves it clear, and clear means the bone keeps its reference pose
//   value there. Both decoders prefill a cleared channel with zero, identity or one, which is what
//   the engine does before it fills anything in and is indistinguishable afterwards from a bone
//   genuinely at the origin, so HkxTrackData carries the mask flags and this reads them.
//
//   Tracks are not bones. transformTrackToBoneIndices says which bone each track drives, a bone with
//   no track keeps its reference pose, and an animation authored against a different skeleton can
//   name a bone this one does not have.
public static class AnimationPose
{
    /// One bone, composed to world space. Units and axes are the game's, unconverted, the same as
    /// everything else these readers hand out; the viewport owns the fit to screen.
    public readonly record struct Bone(int Index, string Name, int Parent, Vector3 Position, Quaternion Rotation);

    public sealed class Pose
    {
        public int Frame;
        public float Time;
        public readonly List<Bone> Bones = new();

        /// Parent to child, as index pairs into Bones, which is what a line drawing needs. A root has
        /// no line of its own; forking is normal, so a bone can be the parent of several.
        public readonly List<(int From, int To)> Links = new();

        public Vector3 Min = new(float.MaxValue);
        public Vector3 Max = new(float.MinValue);

        public Vector3 Centre => Bones.Count == 0 ? Vector3.Zero : (Min + Max) * 0.5f;
        public float Radius
        {
            get
            {
                if (Bones.Count == 0) return 1f;
                var size = Max - Min;
                return Math.Max(0.001f, Math.Max(size.X, Math.Max(size.Y, size.Z)) * 0.5f);
            }
        }
    }

    /// The pose the skeleton holds with no animation on it, which is what an unanimated bone falls
    /// back to and what a viewport shows before a clip is picked.
    public static Pose ReferencePose(HkxSkeleton skeleton) => At(skeleton, null, 0);

    public static Pose At(HkxSkeleton skeleton, HkxAnimationData? animation, int frame)
    {
        var pose = new Pose { Frame = frame };
        int count = skeleton.BoneNames.Count;
        if (count == 0) return pose;

        if (animation != null)
        {
            frame = Math.Clamp(frame, 0, Math.Max(0, animation.NumFrames - 1));
            pose.Frame = frame;
            pose.Time = frame * animation.FrameDuration;
        }

        var trackForBone = TracksByBone(skeleton, animation);
        var world = new Matrix4x4[count];
        var rotation = new Quaternion[count];

        for (int i = 0; i < count; i++)
        {
            var local = Local(skeleton, animation, trackForBone[i], i, frame);
            var matrix = Matrix4x4.CreateScale(local.Scale)
                       * Matrix4x4.CreateFromQuaternion(local.Rotation)
                       * Matrix4x4.CreateTranslation(local.Translation);

            int parent = i < skeleton.ParentIndices.Count ? skeleton.ParentIndices[i] : -1;

            // Parents come before children in every Havok skeleton, so one pass composes the whole
            // tree. A parent index that does not point backwards would compose against an unwritten
            // matrix, so it is treated as a root rather than trusted.
            bool composed = parent >= 0 && parent < i;
            world[i] = composed ? matrix * world[parent] : matrix;
            rotation[i] = composed ? Quaternion.Normalize(rotation[parent] * local.Rotation) : local.Rotation;

            var position = world[i].Translation;
            pose.Bones.Add(new Bone(i, skeleton.BoneNames[i], composed ? parent : -1, position, rotation[i]));
            pose.Min = Vector3.Min(pose.Min, position);
            pose.Max = Vector3.Max(pose.Max, position);

            if (composed) pose.Links.Add((parent, i));
        }

        return pose;
    }

    /// One bone's parent relative transform at a frame: the animation where it drives a channel, the
    /// skeleton's reference pose everywhere else.
    public static HkxBonePose Local(HkxSkeleton skeleton, HkxAnimationData? animation,
                                    int track, int bone, int frame)
    {
        var reference = bone < skeleton.ReferencePose.Count ? skeleton.ReferencePose[bone] : new HkxBonePose();
        if (animation == null || track < 0 || track >= animation.Tracks.Count) return reference;

        var data = animation.Tracks[track];
        var pose = reference;

        if (frame < data.Translations.Count)
        {
            var animated = data.Translations[frame];
            pose.Translation = new Vector3(
                data.TranslationAnimated[0] ? animated.X : reference.Translation.X,
                data.TranslationAnimated[1] ? animated.Y : reference.Translation.Y,
                data.TranslationAnimated[2] ? animated.Z : reference.Translation.Z);
        }

        if (data.RotationAnimated && frame < data.Rotations.Count)
        {
            var q = data.Rotations[frame];
            // A quaternion of length zero is what a short read leaves behind, and normalising it
            // produces NaN that then poisons every child down the chain.
            pose.Rotation = q.LengthSquared() > 1e-8f ? Quaternion.Normalize(q) : reference.Rotation;
        }

        if (frame < data.Scales.Count)
        {
            var animated = data.Scales[frame];
            pose.Scale = new Vector3(
                data.ScaleAnimated[0] ? animated.X : reference.Scale.X,
                data.ScaleAnimated[1] ? animated.Y : reference.Scale.Y,
                data.ScaleAnimated[2] ? animated.Z : reference.Scale.Z);
        }

        return pose;
    }

    /// Which track drives each bone, or -1. transformTrackToBoneIndices is the mapping the engine
    /// uses; without it, an animation with fewer tracks than the skeleton has bones would drive the
    /// wrong ones from track 0 up.
    public static int[] TracksByBone(HkxSkeleton skeleton, HkxAnimationData? animation)
    {
        var forBone = new int[skeleton.BoneNames.Count];
        Array.Fill(forBone, -1);
        if (animation == null) return forBone;

        if (animation.TrackToBoneIndices.Count > 0)
        {
            for (int track = 0; track < animation.TrackToBoneIndices.Count; track++)
            {
                int bone = animation.TrackToBoneIndices[track];
                if (bone >= 0 && bone < forBone.Length) forBone[bone] = track;
            }
            return forBone;
        }

        // No mapping in the file. One track per bone in order is the only reading left, and it is only
        // safe while the counts agree; guessing past that drives the wrong bones.
        if (animation.Tracks.Count == forBone.Length)
            for (int i = 0; i < forBone.Length; i++) forBone[i] = i;

        return forBone;
    }

    /// How far the pose moves between two frames, summed over every bone. Zero means the two frames
    /// are the same pose, which is the difference between a clip that plays and a clip that holds.
    public static float Distance(Pose a, Pose b)
    {
        float total = 0;
        int count = Math.Min(a.Bones.Count, b.Bones.Count);
        for (int i = 0; i < count; i++) total += Vector3.Distance(a.Bones[i].Position, b.Bones[i].Position);
        return total;
    }

    /// Why this animation cannot be posed on this skeleton, or null when it can. Named rather than
    /// counted: an animation authored for another rig is the ordinary case in a shared behaviour, and
    /// it has to read as that rather than as a broken file.
    public static string? WhyNotPosable(HkxSkeleton? skeleton, HkxAnimationData? animation)
    {
        if (skeleton == null || skeleton.BoneNames.Count == 0)
            return "No skeleton was resolved for this file. The Chain tab says which rig the character names.";
        if (animation == null || animation.NumFrames <= 0)
            return "This animation decoded to no frames, so there is nothing to play.";

        if (animation.TrackToBoneIndices.Count > 0)
        {
            int highest = 0;
            foreach (int bone in animation.TrackToBoneIndices) highest = Math.Max(highest, bone);
            if (highest >= skeleton.BoneNames.Count)
                return $"This animation drives bone {highest}, and this skeleton has " +
                       $"{skeleton.BoneNames.Count}. It was authored against a different rig, so what " +
                       "is drawn would not be what plays.";
        }
        else if (animation.Tracks.Count != skeleton.BoneNames.Count)
        {
            return $"This animation has {animation.Tracks.Count} tracks and names no bone for any of " +
                   $"them, and the skeleton has {skeleton.BoneNames.Count} bones. Which track drives " +
                   "which bone is not readable, so the reference pose is shown instead.";
        }

        return null;
    }
}
