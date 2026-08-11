using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenCommonwealth.Services.Hkx;

namespace OpenCommonwealth.Services.Nif;








public static class SkinnedMesh
{
    public sealed class Binding
    {

        public int[] ToSkeleton = Array.Empty<int>();


        public readonly List<string> Unmatched = new();

        public int Matched => ToSkeleton.Count(b => b >= 0);
        public int Total => ToSkeleton.Length;
        public bool Complete => Unmatched.Count == 0 && Total > 0;

        public override string ToString() =>
            Total == 0 ? "the shape names no bones"
            : Complete ? $"all {Total} bones matched the skeleton"
            : $"{Matched} of {Total} bones matched; no skeleton bone called " +
              string.Join(", ", Unmatched.Take(6)) +
              (Unmatched.Count > 6 ? $", and {Unmatched.Count - 6} more" : "");
    }




    public static Binding Bind(NifShape shape, HkxSkeleton skeleton)
    {
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < skeleton.BoneNames.Count; i++) byName.TryAdd(skeleton.BoneNames[i], i);

        var binding = new Binding { ToSkeleton = new int[shape.BoneNames.Count] };
        for (int i = 0; i < shape.BoneNames.Count; i++)
        {
            binding.ToSkeleton[i] = byName.TryGetValue(shape.BoneNames[i], out int at) ? at : -1;
            if (binding.ToSkeleton[i] < 0) binding.Unmatched.Add(shape.BoneNames[i]);
        }
        return binding;
    }









    private static Matrix4x4 Bind(Matrix4x4 stored)
    {
        var m = Matrix4x4.Transpose(stored);
        m.M41 = stored.M41;
        m.M42 = stored.M42;
        m.M43 = stored.M43;
        m.M14 = 0;
        m.M24 = 0;
        m.M34 = 0;
        m.M44 = 1;
        return m;
    }








    public static Matrix4x4? BoneMatrix(NifShape shape, Binding binding, AnimationPose.Pose pose,
                                        int bone)
    {
        if (bone < 0 || bone >= binding.ToSkeleton.Length || bone >= shape.SkinToBone.Count)
            return null;

        int at = binding.ToSkeleton[bone];
        if (at < 0 || at >= pose.Bones.Count) return null;

        var posed = pose.Bones[at];
        var world = Matrix4x4.CreateFromQuaternion(posed.Rotation);
        world.M41 = posed.Position.X;
        world.M42 = posed.Position.Y;
        world.M43 = posed.Position.Z;

        return Bind(shape.SkinToBone[bone]) * world;
    }





    public static Vector3[] Pose(NifShape shape, Binding binding, AnimationPose.Pose pose,
                                 HkxSkeleton skeleton)
    {
        var moved = new Vector3[shape.Vertices.Count];
        if (!shape.IsSkinned || shape.SkinToBone.Count != shape.BoneNames.Count)
        {
            shape.Vertices.CopyTo(moved);
            return moved;
        }



        var boneMatrix = new Matrix4x4[shape.BoneNames.Count];
        var usable = new bool[shape.BoneNames.Count];
        for (int b = 0; b < shape.BoneNames.Count; b++)
        {
            int at = binding.ToSkeleton[b];
            if (at < 0 || at >= pose.Bones.Count) continue;

            var bone = pose.Bones[at];
            var world = Matrix4x4.CreateFromQuaternion(bone.Rotation);
            world.M41 = bone.Position.X;
            world.M42 = bone.Position.Y;
            world.M43 = bone.Position.Z;

            boneMatrix[b] = Bind(shape.SkinToBone[b]) * world;
            usable[b] = true;
        }









        var placement = Placement(shape, binding, AnimationPose.ReferencePose(skeleton))
                        ?? Matrix4x4.Identity;

        for (int v = 0; v < moved.Length; v++)
        {
            var rest = shape.Vertices[v];
            var sum = Vector3.Zero;
            float total = 0;

            for (int s = 0; s < 4; s++)
            {
                float weight = shape.BoneWeights[v * 4 + s];
                if (weight <= 0) continue;

                int b = shape.BoneIndices[v * 4 + s];
                if (b < 0 || b >= boneMatrix.Length || !usable[b]) continue;

                sum += Vector3.Transform(rest, boneMatrix[b]) * weight;
                total += weight;
            }

            moved[v] = total > 0.0001f ? sum / total : Vector3.Transform(rest, placement);
        }

        return moved;
    }








    public static Matrix4x4? Placement(NifShape shape, Binding binding, AnimationPose.Pose pose)
    {
        for (int b = 0; b < shape.BoneNames.Count; b++)
            if (BoneMatrix(shape, binding, pose, b) is { } m) return m;

        return null;
    }




















    public static float BindError(NifShape shape, Binding binding, HkxSkeleton skeleton) =>
        BindError(shape, binding, skeleton, out _);

    public static float BindError(NifShape shape, Binding binding, HkxSkeleton skeleton, out int measured)
    {
        var rest = AnimationPose.ReferencePose(skeleton);
        var composed = new List<Matrix4x4>();

        for (int b = 0; b < shape.BoneNames.Count; b++)
            if (BoneMatrix(shape, binding, rest, b) is { } m) composed.Add(m);

        measured = composed.Count;
        if (measured < 2) return 0;

        float worst = 0;
        foreach (var m in composed)
            worst = Math.Max(worst, Disagreement(composed[0], m));

        return worst;
    }




    public static float Disagreement(Matrix4x4 a, Matrix4x4 b)
    {
        float worst = 0;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? -50 : 50,
                                     (i & 2) == 0 ? -50 : 50,
                                     (i & 4) == 0 ? -50 : 50);
            worst = Math.Max(worst, Vector3.Distance(Vector3.Transform(corner, a),
                                                     Vector3.Transform(corner, b)));
        }
        return worst;
    }


    public static bool FullyBound(NifShape shape, Binding binding, int vertex)
    {
        for (int s = 0; s < 4; s++)
        {
            if (shape.BoneWeights[vertex * 4 + s] <= 0) continue;

            int b = shape.BoneIndices[vertex * 4 + s];
            if (b < 0 || b >= binding.ToSkeleton.Length || binding.ToSkeleton[b] < 0) return false;
        }
        return true;
    }




    public static List<(int From, int To)> Edges(NifShape shape)
    {
        var seen = new HashSet<long>();
        var edges = new List<(int, int)>();

        for (int t = 0; t + 2 < shape.Indices.Count; t += 3)
        {
            int a = shape.Indices[t];
            int b = shape.Indices[t + 1];
            int c = shape.Indices[t + 2];
            if (a < 0 || a >= shape.Vertices.Count || b < 0 || b >= shape.Vertices.Count ||
                c < 0 || c >= shape.Vertices.Count) continue;

            foreach ((int x, int y) in new[] { (a, b), (b, c), (c, a) })
            {
                long key = x < y ? ((long)x << 32) | (uint)y : ((long)y << 32) | (uint)x;
                if (seen.Add(key)) edges.Add((x, y));
            }
        }

        return edges;
    }
}
