using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Hkx;

namespace OpenCommonwealth.Services.Nif;

public static class SkinValidator
{
    public const float SumTolerance = 0.001f;

    public static IReadOnlyList<SkeletonValidator.Finding> Check(
        NifShape shape,
        SkinnedMesh.Binding? binding = null)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var findings = new List<SkeletonValidator.Finding>();

        if (shape.BoneNames.Count == 0)
            return findings;

        if (shape.SkinToBone.Count != shape.BoneNames.Count)
        {
            findings.Add(new SkeletonValidator.Finding(
                SkeletonValidator.Level.Error,
                "skin",
                $"it holds {shape.SkinToBone.Count} bind transform(s) against {shape.BoneNames.Count} bone(s)"));
        }

        if (!SkinEdit.Editable(shape))
        {
            findings.Add(new SkeletonValidator.Finding(
                SkeletonValidator.Level.Error,
                "skin",
                $"it names {shape.BoneNames.Count} bone(s) but holds {shape.BoneWeights.Count} weight(s) " +
                $"for {shape.Vertices.Count} vertices"));
            return findings;
        }

        var used = new bool[shape.BoneNames.Count];
        int loose = 0;
        int unbound = 0;
        int offSum = 0;
        int repeated = 0;
        int negative = 0;
        int outOfRange = 0;
        float worstSum = 0;

        for (int vertex = 0; vertex < shape.Vertices.Count; vertex++)
        {
            float total = 0;
            var seen = new HashSet<int>();
            bool anyUnmatched = false;

            for (int slot = 0; slot < SkinEdit.Slots; slot++)
            {
                float weight = shape.BoneWeights[vertex * SkinEdit.Slots + slot];
                int bone = shape.BoneIndices[vertex * SkinEdit.Slots + slot];

                if (!float.IsFinite(weight))
                {
                    findings.Add(new SkeletonValidator.Finding(
                        SkeletonValidator.Level.Error,
                        $"vertex {vertex}",
                        "one of its weights is not a finite number"));
                    continue;
                }

                if (weight < 0)
                {
                    negative++;
                    continue;
                }

                if (weight == 0)
                    continue;

                total += weight;

                if (bone < 0 || bone >= shape.BoneNames.Count)
                {
                    outOfRange++;
                    continue;
                }

                used[bone] = true;
                if (!seen.Add(bone))
                    repeated++;

                if (binding != null &&
                    bone < binding.ToSkeleton.Length &&
                    binding.ToSkeleton[bone] < 0)
                {
                    anyUnmatched = true;
                }
            }

            if (total <= 0)
            {
                loose++;
            }
            else if (MathF.Abs(total - 1f) > SumTolerance)
            {
                offSum++;
                worstSum = MathF.Max(worstSum, MathF.Abs(total - 1f));
            }

            if (anyUnmatched)
                unbound++;
        }

        Count(
            findings,
            SkeletonValidator.Level.Error,
            negative,
            "weights",
            "carry a weight below zero");
        Count(
            findings,
            SkeletonValidator.Level.Error,
            outOfRange,
            "weights",
            $"name a bone this shape does not have; it names {shape.BoneNames.Count}");
        Count(
            findings,
            SkeletonValidator.Level.Error,
            repeated,
            "weights",
            "put the same bone in two slots of one vertex, so the lighter one is thrown away");
        Count(
            findings,
            SkeletonValidator.Level.Error,
            loose,
            "vertices",
            "have no weight at all, so nothing moves them");

        if (offSum > 0)
        {
            findings.Add(new SkeletonValidator.Finding(
                SkeletonValidator.Level.Warning,
                "vertices",
                $"{offSum} do not add up to one, the worst by {worstSum:0.###}; " +
                "they shrink towards or stretch away from the origin as the rig moves"));
        }

        if (unbound > 0)
        {
            findings.Add(new SkeletonValidator.Finding(
                SkeletonValidator.Level.Warning,
                "vertices",
                $"{unbound} are weighted to a bone the skeleton does not have, so that share does nothing"));
        }

        for (int bone = 0; bone < used.Length; bone++)
        {
            if (!used[bone])
            {
                findings.Add(new SkeletonValidator.Finding(
                    SkeletonValidator.Level.Warning,
                    $"bone '{shape.BoneNames[bone]}'",
                    "no vertex of this shape is weighted to it"));
            }
        }

        return findings;
    }

    private static void Count(
        List<SkeletonValidator.Finding> findings,
        SkeletonValidator.Level level,
        int count,
        string where,
        string what)
    {
        if (count > 0)
            findings.Add(new SkeletonValidator.Finding(level, where, $"{count} {what}"));
    }
}
