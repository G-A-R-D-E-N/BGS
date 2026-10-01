using System;
using System.Collections.Generic;
using System.Numerics;
using OpenCommonwealth.Services.Hkx;

namespace OpenCommonwealth.Services.Nif;

public static partial class SkinEdit
{
    public sealed class MirrorReport
    {
        public int Mirrored;
        public int NoPartnerVertex;
        public int AmbiguousVertex;
        public int NoPartnerBone;
        public float Worst;
        public readonly List<string> Skipped = new();
        public bool Any => Mirrored > 0;

        public override string ToString() =>
            $"{Mirrored} vertices mirrored" +
            (Worst > 0 ? $", furthest partner {Worst:0.###} away" : "") +
            (NoPartnerVertex > 0 ? $"; {NoPartnerVertex} had no mirrored vertex" : "") +
            (AmbiguousVertex > 0 ? $"; {AmbiguousVertex} had more than one" : "") +
            (NoPartnerBone > 0 ? $"; {NoPartnerBone} were weighted to a bone with no opposite number" : "");
    }

    public static MirrorReport MirrorWeights(
        NifShape shape,
        int lane,
        bool fromPositiveSide,
        float tolerance = 0.05f,
        float boneTolerance = 0,
        Func<int, Vector3?>? bonePosition = null)
    {
        RequireEditable(shape);
        if (lane < 0 || lane > 2)
            throw new ArgumentOutOfRangeException(
                nameof(lane),
                "the mirror plane is X, Y or Z");

        if (boneTolerance <= 0)
            boneTolerance = Extent(shape) * 0.02f;
        bonePosition ??= bone => Centroid(shape, bone);

        var pairing = SkeletonMirror.Confirm(
            SkeletonMirror.Of(shape.BoneNames),
            shape.BoneNames,
            lane,
            boneTolerance,
            bonePosition);

        var report = new MirrorReport();
        report.Skipped.AddRange(pairing.Ambiguous);
        report.Skipped.AddRange(pairing.Unconfirmed);

        var partnerVertex = Partners(
            shape,
            lane,
            tolerance,
            out var apart,
            out var candidates);

        var writes = new List<(int Vertex, List<Influence> Influences)>();
        for (int vertex = 0; vertex < shape.Vertices.Count; vertex++)
        {
            float side = Lane(shape.Vertices[vertex], lane);
            if (fromPositiveSide ? side >= 0 : side <= 0)
                continue;

            int sourceVertex = partnerVertex[vertex];
            if (sourceVertex < 0)
            {
                if (candidates[vertex] == 0)
                    report.NoPartnerVertex++;
                else
                    report.AmbiguousVertex++;
                continue;
            }

            var mirrored = new List<Influence>();
            bool usable = true;
            foreach (var influence in InfluencesOf(shape, sourceVertex))
            {
                int bone = pairing.PartnerOf(influence.Bone);
                if (bone < 0)
                {
                    usable = false;
                    break;
                }

                mirrored.Add(influence with { Bone = bone });
            }

            if (!usable)
            {
                report.NoPartnerBone++;
                continue;
            }

            writes.Add((vertex, CheckedInfluences(shape, mirrored)));
        }

        foreach (var (vertex, influences) in writes)
        {
            WriteInfluences(shape, vertex, influences);
            report.Mirrored++;
            report.Worst = MathF.Max(report.Worst, apart[vertex]);
        }

        return report;
    }

    private static Vector3? Centroid(NifShape shape, int bone)
    {
        if (bone < 0 || bone >= shape.BoneNames.Count)
            return null;

        var sum = Vector3.Zero;
        float total = 0;

        for (int vertex = 0; vertex < shape.Vertices.Count; vertex++)
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                if (shape.BoneIndices[vertex * Slots + slot] != bone)
                    continue;

                float weight = shape.BoneWeights[vertex * Slots + slot];
                if (weight <= 0)
                    continue;

                sum += shape.Vertices[vertex] * weight;
                total += weight;
            }
        }

        return total > 0 ? sum / total : null;
    }

    private static float Extent(NifShape shape)
    {
        if (shape.Vertices.Count == 0)
            return 1;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var vertex in shape.Vertices)
        {
            min = Vector3.Min(min, vertex);
            max = Vector3.Max(max, vertex);
        }

        var size = max - min;
        return MathF.Max(1e-3f, MathF.Max(size.X, MathF.Max(size.Y, size.Z)));
    }

    private static int[] Partners(
        NifShape shape,
        int lane,
        float tolerance,
        out float[] apart,
        out int[] candidates)
    {
        var partner = new int[shape.Vertices.Count];
        Array.Fill(partner, -1);
        apart = new float[shape.Vertices.Count];
        candidates = new int[shape.Vertices.Count];

        float cell = MathF.Max(tolerance, 1e-4f) * 2;
        var grid = new Dictionary<(int, int, int), List<int>>();

        (int, int, int) Cell(Vector3 value) =>
            ((int)MathF.Floor(value.X / cell),
             (int)MathF.Floor(value.Y / cell),
             (int)MathF.Floor(value.Z / cell));

        for (int vertex = 0; vertex < shape.Vertices.Count; vertex++)
        {
            var key = Cell(shape.Vertices[vertex]);
            if (!grid.TryGetValue(key, out var bucket))
            {
                bucket = new List<int>();
                grid[key] = bucket;
            }
            bucket.Add(vertex);
        }

        for (int vertex = 0; vertex < shape.Vertices.Count; vertex++)
        {
            var wanted = SkeletonMirror.Reflect(shape.Vertices[vertex], lane);
            var (cx, cy, cz) = Cell(wanted);
            int found = -1;
            int count = 0;
            float nearest = float.MaxValue;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var bucket))
                            continue;

                        foreach (int other in bucket)
                        {
                            if (other == vertex)
                                continue;

                            float distance = Vector3.Distance(shape.Vertices[other], wanted);
                            if (distance > tolerance)
                                continue;

                            count++;
                            if (distance >= nearest)
                                continue;

                            nearest = distance;
                            found = other;
                        }
                    }
                }
            }

            candidates[vertex] = count;
            if (count != 1)
                continue;

            partner[vertex] = found;
            apart[vertex] = nearest;
        }

        return partner;
    }

    private static float Lane(Vector3 value, int lane) =>
        lane switch { 0 => value.X, 1 => value.Y, _ => value.Z };
}
