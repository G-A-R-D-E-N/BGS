using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

public sealed record HavokSkeletonMappingPair(
    IReadOnlyList<HavokRagdollMapping> Forward,
    IReadOnlyList<HavokRagdollMapping> Reverse);

public static class SkeletonMapperAuthor
{
    public const int RagdollMappingType = 0;

    public const float UnitTolerance = 1e-3f;

    public const float ScaleTolerance = 1e-3f;

    public static HavokSkeletonMappingPair Reciprocal(
        HavokRagdollSkeleton skeletonA,
        HavokRagdollSkeleton skeletonB,
        IReadOnlyList<(int BoneA, int BoneB)> pairs)
    {
        var forward = FromReferencePose(skeletonA, skeletonB, pairs);
        return new HavokSkeletonMappingPair(forward, Reverse(forward));
    }

    public static IReadOnlyList<HavokRagdollMapping> FromReferencePose(
        HavokRagdollSkeleton skeletonA,
        HavokRagdollSkeleton skeletonB,
        IReadOnlyList<(int BoneA, int BoneB)> pairs)
    {
        ArgumentNullException.ThrowIfNull(skeletonA);
        ArgumentNullException.ThrowIfNull(skeletonB);
        ArgumentNullException.ThrowIfNull(pairs);

        var refusals = Refusals(skeletonA, skeletonB, pairs);
        if (refusals.Count > 0) throw new ArgumentException(string.Join("; ", refusals), nameof(pairs));

        var framesA = CheckedWorldFrames(skeletonA);
        var framesB = CheckedWorldFrames(skeletonB);

        var rows = new List<HavokRagdollMapping>(pairs.Count);
        for (int i = 0; i < pairs.Count; i++)
        {
            var (boneA, boneB) = pairs[i];
            if (!TryCompose(Invert(framesA[boneA]), framesB[boneB], out var composed))
                throw new ArgumentException(
                    $"pair {i}: the composed transform for bone {boneA} -> bone {boneB} is not usable",
                    nameof(pairs));

            rows.Add(new HavokRagdollMapping(boneA, boneB, composed.Position, composed.Rotation));
        }
        return rows;
    }

    public static IReadOnlyList<HavokRagdollMapping> Reverse(IReadOnlyList<HavokRagdollMapping> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var refusals = Refusals(rows);
        if (refusals.Count > 0) throw new ArgumentException(string.Join("; ", refusals), nameof(rows));

        var reversed = new List<HavokRagdollMapping>(rows.Count);
        foreach (var row in rows)
        {
            var inverse = Invert(new HavokBoneFrame(row.AFromBTranslation, row.AFromBRotation));
            reversed.Add(new HavokRagdollMapping(row.BoneB, row.BoneA, inverse.Position, inverse.Rotation));
        }
        return reversed;
    }

    public static IReadOnlyList<string> Refusals(
        HavokRagdollSkeleton skeletonA,
        HavokRagdollSkeleton skeletonB,
        IReadOnlyList<(int BoneA, int BoneB)> pairs)
    {
        ArgumentNullException.ThrowIfNull(skeletonA);
        ArgumentNullException.ThrowIfNull(skeletonB);
        ArgumentNullException.ThrowIfNull(pairs);

        var refusals = new List<string>();
        refusals.AddRange(Refusals(skeletonA));
        refusals.AddRange(Refusals(skeletonB));

        if (pairs.Count == 0) refusals.Add("a mapping needs at least one bone pair");

        var mappedA = new HashSet<int>();
        var mappedB = new HashSet<int>();
        for (int i = 0; i < pairs.Count; i++)
        {
            var (boneA, boneB) = pairs[i];

            if (boneA < 0 || boneA >= skeletonA.BoneNames.Count)
                refusals.Add($"pair {i}: bone {boneA} is not a bone of {Describe(skeletonA)}");
            else if (!mappedA.Add(boneA))
                refusals.Add($"pair {i}: bone '{Name(skeletonA, boneA)}' of {Describe(skeletonA)} is already mapped");

            if (boneB < 0 || boneB >= skeletonB.BoneNames.Count)
                refusals.Add($"pair {i}: bone {boneB} is not a bone of {Describe(skeletonB)}");
            else if (!mappedB.Add(boneB))
                refusals.Add($"pair {i}: bone '{Name(skeletonB, boneB)}' of {Describe(skeletonB)} is already mapped");
        }

        return refusals;
    }

    public static IReadOnlyList<string> Refusals(HavokRagdollSkeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);

        var refusals = new List<string>();
        int bones = skeleton.BoneNames.Count;

        if (bones == 0)
        {
            refusals.Add($"{Describe(skeleton)} has no bones");
            return refusals;
        }

        if (skeleton.ParentIndices.Count != bones)
            refusals.Add($"{Describe(skeleton)} has {bones} bones and {skeleton.ParentIndices.Count} parent indices");

        if (skeleton.ReferencePose.Count != bones)
            refusals.Add($"{Describe(skeleton)} has {bones} bones and {skeleton.ReferencePose.Count} reference-pose transforms");

        int shared = Math.Min(bones, Math.Min(skeleton.ParentIndices.Count, skeleton.ReferencePose.Count));
        for (int i = 0; i < shared; i++)
        {
            int parent = skeleton.ParentIndices[i];
            if (parent < -1 || parent >= bones)
                refusals.Add($"bone {i} ('{Name(skeleton, i)}') has parent {parent}, which is not a bone of {Describe(skeleton)}");
            else if (parent >= i)
                refusals.Add($"bone {i} ('{Name(skeleton, i)}') has parent {parent} stored at or after it; " +
                             "this primitive composes frames in array order and needs parents before children");

            var pose = skeleton.ReferencePose[i];
            if (!Finite(pose.Translation) || !Finite(pose.Rotation))
            {
                refusals.Add($"bone {i} ('{Name(skeleton, i)}') has a non-finite reference transform");
                continue;
            }

            float rotationLength = pose.Rotation.Length();
            if (MathF.Abs(rotationLength - 1f) > UnitTolerance)
                refusals.Add($"bone {i} ('{Name(skeleton, i)}') has a reference rotation of length {rotationLength:0.######}, " +
                             "which is not a unit quaternion");

            if (!Finite(pose.Scale))
                refusals.Add($"bone {i} ('{Name(skeleton, i)}') has a non-finite reference scale");
            else if (MathF.Abs(pose.Scale.X - 1f) > ScaleTolerance ||
                     MathF.Abs(pose.Scale.Y - 1f) > ScaleTolerance ||
                     MathF.Abs(pose.Scale.Z - 1f) > ScaleTolerance)
                refusals.Add($"bone {i} ('{Name(skeleton, i)}') has a reference scale of " +
                             $"({pose.Scale.X:0.######}, {pose.Scale.Y:0.######}, {pose.Scale.Z:0.######}); " +
                             "non-identity reference scale is not supported");
        }

        return refusals;
    }

    public static IReadOnlyList<string> Refusals(IReadOnlyList<HavokRagdollMapping> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var refusals = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (!Usable(row.AFromBTranslation, row.AFromBRotation))
                refusals.Add($"row {i} ({row.BoneA} -> {row.BoneB}) has a transform that cannot be inverted");
        }
        return refusals;
    }

    private static HavokBoneFrame[] CheckedWorldFrames(HavokRagdollSkeleton skeleton)
    {
        var frames = new HavokBoneFrame[skeleton.ReferencePose.Count];
        for (int i = 0; i < frames.Length; i++)
        {
            var pose = skeleton.ReferencePose[i];
            var local = new HavokBoneFrame(pose.Translation, pose.Rotation);
            int parent = skeleton.ParentIndices[i];

            if (parent < 0)
            {
                if (!Usable(local.Position, local.Rotation))
                    throw new ArgumentException(
                        $"bone {i} ('{Name(skeleton, i)}') of {Describe(skeleton)} has a reference transform " +
                        "that is not a usable unit rotation", nameof(skeleton));
                frames[i] = new HavokBoneFrame(local.Position, Unit(local.Rotation));
                continue;
            }

            if (!TryCompose(frames[parent], local, out var composed))
                throw new ArgumentException(
                    $"bone {i} ('{Name(skeleton, i)}') of {Describe(skeleton)} accumulates a reference " +
                    "transform that is not a usable unit rotation", nameof(skeleton));

            frames[i] = composed;
        }
        return frames;
    }

    private static bool Usable(Vector3 translation, Quaternion rotation) =>
        Finite(translation) && Finite(rotation) && MathF.Abs(rotation.Length() - 1f) <= UnitTolerance;

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool Finite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static bool TryCompose(HavokBoneFrame outer, HavokBoneFrame inner, out HavokBoneFrame composed)
    {
        composed = default;

        var translation = outer.Position + Vector3.Transform(inner.Position, outer.Rotation);
        var rotation = outer.Rotation * inner.Rotation;

        if (!Finite(translation) || !Finite(rotation) || MathF.Abs(rotation.Length() - 1f) > UnitTolerance)
            return false;

        composed = new HavokBoneFrame(translation, Unit(rotation));
        return true;
    }

    private static Quaternion Unit(Quaternion rotation)
    {
        float length = rotation.Length();
        return MathF.Abs(length - 1f) <= UnitTolerance ? Quaternion.Normalize(rotation) : rotation;
    }

    private static HavokBoneFrame Invert(HavokBoneFrame frame)
    {
        var inverse = Quaternion.Inverse(frame.Rotation);
        return new HavokBoneFrame(-Vector3.Transform(frame.Position, inverse), inverse);
    }

    private static string Name(HavokRagdollSkeleton skeleton, int bone) =>
        bone >= 0 && bone < skeleton.BoneNames.Count ? skeleton.BoneNames[bone] : $"bone {bone}";

    private static string Describe(HavokRagdollSkeleton skeleton) =>
        skeleton.Name.Length > 0 ? $"skeleton '{skeleton.Name}'" : "the skeleton";
}
