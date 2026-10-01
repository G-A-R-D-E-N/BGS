using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenCommonwealth.Services.Nif;

public static partial class SkinEdit
{
    public const int Slots = 4;

    public readonly record struct Influence(int Bone, float Weight)
    {
        public override string ToString() => $"bone {Bone} at {Weight:0.###}";
    }

    public sealed class Influenced
    {
        public int Bone;
        public string BoneName = "";
        public readonly List<int> Vertices = new();
        public Vector3 Min = new(float.MaxValue);
        public Vector3 Max = new(float.MinValue);
        public float Weight;
        public bool Any => Vertices.Count > 0;
        public Vector3 Centre => Any ? (Min + Max) * 0.5f : Vector3.Zero;
        public Vector3 Size => Any ? Max - Min : Vector3.Zero;

        public override string ToString() =>
            !Any ? $"'{BoneName}' moves no vertex of this shape"
            : $"'{BoneName}' moves {Vertices.Count} vertices in a box {Size.X:0.##} by {Size.Y:0.##} " +
              $"by {Size.Z:0.##} around {Centre.X:0.##}, {Centre.Y:0.##}, {Centre.Z:0.##}";
    }

    public static bool Editable(NifShape shape) =>
        shape.IsSkinned &&
        shape.BoneIndices.Count == shape.Vertices.Count * Slots &&
        shape.BoneWeights.Count == shape.Vertices.Count * Slots;

    public static List<Influence> InfluencesOf(NifShape shape, int vertex)
    {
        Require(shape, vertex);
        var found = new List<Influence>(Slots);

        for (int slot = 0; slot < Slots; slot++)
        {
            float weight = shape.BoneWeights[vertex * Slots + slot];
            if (weight <= 0)
                continue;

            found.Add(new Influence(shape.BoneIndices[vertex * Slots + slot], weight));
        }

        found.Sort((a, b) => b.Weight.CompareTo(a.Weight));
        return found;
    }

    public static void SetInfluences(
        NifShape shape,
        int vertex,
        IReadOnlyList<Influence> influences)
    {
        Require(shape, vertex);
        var kept = CheckedInfluences(shape, influences);
        WriteInfluences(shape, vertex, kept);
    }

    private static void WriteInfluences(NifShape shape, int vertex, IReadOnlyList<Influence> kept)
    {
        for (int slot = 0; slot < Slots; slot++)
        {
            shape.BoneIndices[vertex * Slots + slot] = slot < kept.Count ? kept[slot].Bone : 0;
            shape.BoneWeights[vertex * Slots + slot] = slot < kept.Count ? kept[slot].Weight : 0;
        }
    }

    private static List<Influence> CheckedInfluences(NifShape shape, IReadOnlyList<Influence> influences)
    {
        ArgumentNullException.ThrowIfNull(influences);

        if (influences.Any(value => !float.IsFinite(value.Weight)))
            throw new ArgumentException("weights must be finite", nameof(influences));

        var kept = influences.Where(value => value.Weight > 0).ToList();
        if (kept.Count > Slots)
            throw new ArgumentException(
                $"a vertex holds {Slots} influences and this names {kept.Count}",
                nameof(influences));

        foreach (var influence in kept)
        {
            if (influence.Bone < 0 || influence.Bone >= shape.BoneNames.Count)
                throw new ArgumentOutOfRangeException(
                    nameof(influences),
                    $"this shape is weighted to {shape.BoneNames.Count} bone(s), so there is no bone {influence.Bone}");
        }

        if (kept.Select(value => value.Bone).Distinct().Count() != kept.Count)
            throw new ArgumentException(
                "the same bone appears twice on one vertex",
                nameof(influences));

        kept.Sort((a, b) => b.Weight.CompareTo(a.Weight));
        return kept;
    }

    public static float WeightSum(NifShape shape, int vertex)
    {
        Require(shape, vertex);
        float total = 0;
        for (int slot = 0; slot < Slots; slot++)
            total += shape.BoneWeights[vertex * Slots + slot];
        return total;
    }

    public static int Normalise(
        NifShape shape,
        float tolerance = SkinValidator.SumTolerance)
    {
        RequireEditable(shape);
        int changed = 0;

        for (int vertex = 0; vertex < shape.Vertices.Count; vertex++)
        {
            float total = WeightSum(shape, vertex);
            if (total <= 0 || !float.IsFinite(total) || MathF.Abs(total - 1f) <= tolerance)
                continue;

            for (int slot = 0; slot < Slots; slot++)
                shape.BoneWeights[vertex * Slots + slot] /= total;
            changed++;
        }

        return changed;
    }

    public static int Prune(NifShape shape, float minimum)
    {
        RequireEditable(shape);
        if (!(minimum > 0))
            throw new ArgumentOutOfRangeException(
                nameof(minimum),
                "the cut has to be above zero");

        var writes = new List<(int Vertex, List<Influence> Influences)>();
        for (int vertex = 0; vertex < shape.Vertices.Count; vertex++)
        {
            var influences = InfluencesOf(shape, vertex);
            if (influences.Count == 0)
                continue;

            var kept = influences.Where(value => value.Weight >= minimum).ToList();
            if (kept.Count == influences.Count)
                continue;
            if (kept.Count == 0)
                kept.Add(influences[0]);

            float total = kept.Sum(value => value.Weight);
            writes.Add((vertex, CheckedInfluences(shape,
                kept.Select(value => value with { Weight = value.Weight / total }).ToList())));
        }

        foreach (var (vertex, influences) in writes) WriteInfluences(shape, vertex, influences);
        return writes.Count;
    }

    public static Influenced InfluencedBy(
        NifShape shape,
        int bone,
        float minimum = 0.01f)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var found = new Influenced
        {
            Bone = bone,
            BoneName = bone >= 0 && bone < shape.BoneNames.Count
                ? shape.BoneNames[bone]
                : $"bone {bone}"
        };

        if (!Editable(shape) || bone < 0)
            return found;

        for (int vertex = 0; vertex < shape.Vertices.Count; vertex++)
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                if (shape.BoneIndices[vertex * Slots + slot] != bone)
                    continue;

                float weight = shape.BoneWeights[vertex * Slots + slot];
                if (weight < minimum)
                    continue;

                found.Vertices.Add(vertex);
                found.Weight += weight;
                found.Min = Vector3.Min(found.Min, shape.Vertices[vertex]);
                found.Max = Vector3.Max(found.Max, shape.Vertices[vertex]);
                break;
            }
        }

        return found;
    }

    public static int CopyWeights(
        NifShape shape,
        int fromBone,
        int toBone,
        bool keepSource = false)
    {
        RequireEditable(shape);
        RequireBone(shape, fromBone, nameof(fromBone));
        RequireBone(shape, toBone, nameof(toBone));
        if (fromBone == toBone)
            throw new ArgumentException(
                "a bone cannot be copied onto itself",
                nameof(toBone));

        var writes = new List<(int Vertex, List<Influence> Influences)>();
        for (int vertex = 0; vertex < shape.Vertices.Count; vertex++)
        {
            var influences = InfluencesOf(shape, vertex);
            var source = influences.FirstOrDefault(value => value.Bone == fromBone);
            if (source.Weight <= 0)
                continue;

            var rebuilt = new List<Influence>();
            bool landed = false;
            foreach (var influence in influences)
            {
                if (influence.Bone == fromBone && !keepSource)
                    continue;

                if (influence.Bone == toBone)
                {
                    rebuilt.Add(influence with { Weight = influence.Weight + source.Weight });
                    landed = true;
                    continue;
                }

                rebuilt.Add(influence);
            }

            if (!landed)
                rebuilt.Add(new Influence(toBone, source.Weight));

            if (rebuilt.Count > Slots)
            {
                rebuilt.Sort((a, b) => b.Weight.CompareTo(a.Weight));
                float before = rebuilt.Sum(value => value.Weight);
                rebuilt = rebuilt.Take(Slots).ToList();
                float after = rebuilt.Sum(value => value.Weight);
                if (after > 0)
                {
                    rebuilt = rebuilt
                        .Select(value => value with { Weight = value.Weight * before / after })
                        .ToList();
                }
            }

            writes.Add((vertex, CheckedInfluences(shape, rebuilt)));
        }

        foreach (var (vertex, influences) in writes) WriteInfluences(shape, vertex, influences);
        return writes.Count;
    }

    private static void Require(NifShape shape, int vertex)
    {
        RequireEditable(shape);
        if (vertex < 0 || vertex >= shape.Vertices.Count)
            throw new ArgumentOutOfRangeException(
                nameof(vertex),
                $"there is no vertex {vertex}");
    }

    private static void RequireEditable(NifShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (!Editable(shape))
            throw new InvalidOperationException(
                $"'{shape.Name}' is not skinned, or its weights do not cover its {shape.Vertices.Count} vertices");
    }

    private static void RequireBone(NifShape shape, int bone, string argument)
    {
        if (bone < 0 || bone >= shape.BoneNames.Count)
            throw new ArgumentOutOfRangeException(
                argument,
                $"this shape is weighted to {shape.BoneNames.Count} bone(s), so there is no bone {bone}");
    }
}
