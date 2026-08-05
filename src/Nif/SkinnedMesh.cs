using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenCommonwealth.Services.Hkx;

namespace OpenCommonwealth.Services.Nif;

// A mesh bound to a Havok skeleton, posed at a frame.
//
// The two halves come from different files that were authored together but do not reference each
// other: the mesh names its bones, the skeleton names its own, and nothing guarantees the two lists
// agree. So the matching is reported rather than assumed. A vertex whose bones did not match is
// still drawn, at its rest position, and the bones that failed are named, because a limb quietly
// missing from a drawing is the failure that looks like a rendering bug for hours.
public static class SkinnedMesh
{
    public sealed class Binding
    {
        /// For each of the shape's bones, which skeleton bone it is, or -1.
        public int[] ToSkeleton = Array.Empty<int>();

        /// Mesh bone names with no skeleton bone of that name.
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

    /// Matched by name, without case, because the mesh and the skeleton are authored by hand and do
    /// not agree on it. Nothing is matched by position: two rigs with the same bone count and
    /// different orders would silently weight every vertex to the wrong bone.
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

    /// BSSkin::BoneData stores a rotation row by row for column vectors, and System.Numerics
    /// multiplies row vectors, so the two disagree by a transpose. The translation is not part of
    /// that and stays where it is.
    ///
    /// Measured, not reasoned: posing Dogmeat's six shapes back onto the skeleton's own reference
    /// pose, which is the pose the mesh is authored on and so must not move it, drifts 0.245 units
    /// per vertex this way against 50 to 107 for reading it straight across, inverting it, or both.
    /// 0.245 on a dog a hundred units long is what half precision vertex positions cost.
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

    /// Every vertex moved to where the pose puts it. A vertex sits in skin space, so for each bone it
    /// is weighted to it goes through that bone's skin-to-bone transform and then through where the
    /// pose has that bone, and the results are blended by weight. This is linear blend skinning,
    /// which is what the game does and what makes a joint pinch when it bends a long way.
    public static Vector3[] Pose(NifShape shape, Binding binding, AnimationPose.Pose pose,
                                 HkxSkeleton skeleton)
    {
        var moved = new Vector3[shape.Vertices.Count];
        if (!shape.IsSkinned || shape.SkinToBone.Count != shape.BoneNames.Count)
        {
            shape.Vertices.CopyTo(moved);
            return moved;
        }

        // Where each of the shape's bones has ended up, as one matrix per bone, worked out once
        // rather than per vertex. A shape can carry six thousand vertices and thirty five bones.
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

            // Nothing usable claimed this vertex, so it stays where the mesh put it rather than
            // collapsing to the origin and dragging a spike across the drawing.
            moved[v] = total > 0.0001f ? sum / total : rest;
        }

        return moved;
    }

    /// How far the mesh moves when posed on the skeleton's own reference pose, averaged over every
    /// vertex. The mesh is authored on that pose, so the answer is zero when the bind transforms are
    /// being composed correctly and large when they are not. This is the check that catches a
    /// rotation read in the wrong order, which otherwise produces a mesh that is plausibly shaped,
    /// wrongly placed, and easy to blame on the camera.
    /// Measured only over the vertices every one of whose bones matched. A vertex weighted partly to a
    /// bone the skeleton does not have keeps that share at its rest position by design, which moves
    /// it, and counting those would report the binding gap as though it were a transform fault. The
    /// two are different problems and mixing them hid a real one: a human body mesh weights 45 of its
    /// 58 bones to skin helper bones that the Havok skeleton has none of, and reads as 39 units of
    /// drift while the transforms are composing perfectly.
    public static float BindError(NifShape shape, Binding binding, HkxSkeleton skeleton) =>
        BindError(shape, binding, skeleton, out _);

    public static float BindError(NifShape shape, Binding binding, HkxSkeleton skeleton, out int measured)
    {
        measured = 0;
        var posed = Pose(shape, binding, AnimationPose.ReferencePose(skeleton), skeleton);
        if (posed.Length == 0) return 0;

        float total = 0;
        for (int v = 0; v < posed.Length; v++)
        {
            if (!FullyBound(shape, binding, v)) continue;
            total += Vector3.Distance(posed[v], shape.Vertices[v]);
            measured++;
        }
        return measured == 0 ? 0 : total / measured;
    }

    /// Whether every bone carrying any of this vertex's weight matched the skeleton.
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

    /// Unique vertex pairs to draw as lines, one per triangle edge. Wireframe rather than shaded, so
    /// the same 2D surface the rest of the window uses can draw it; a shared edge would otherwise be
    /// drawn twice, which on a six thousand triangle shape is nine thousand wasted lines.
    public static List<(int From, int To)> Edges(NifShape shape)
    {
        var seen = new HashSet<long>();
        var edges = new List<(int, int)>();

        for (int t = 0; t + 2 < shape.Indices.Count; t += 3)
            for (int e = 0; e < 3; e++)
            {
                int a = shape.Indices[t + e];
                int b = shape.Indices[t + (e + 1) % 3];
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (seen.Add(key)) edges.Add((a, b));
            }

        return edges;
    }
}
