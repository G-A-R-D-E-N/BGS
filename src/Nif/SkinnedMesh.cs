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

    /// What one bone does to the vertices weighted to it: into that bone's space, then out to where
    /// the pose has put it. Null when the bone matched nothing in the skeleton.
    ///
    /// Worth having on its own rather than only inside the loop below, because on the reference pose
    /// this has to come back as the identity for every bone. A whole mesh drifting says only that
    /// something is wrong; this says which bone, which is the difference between a measurement and a
    /// hunt.
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

        // Where the mesh as a whole sits once it is on the skeleton, for the vertices no bone below
        // can move. Leaving those at the raw authored position is what drew a second body a hundred
        // and twenty units under the first one: a human body mesh is authored with its origin at the
        // neck, so its vertices run from -120 to -6 and the bind lifts them onto the ground. A vertex
        // held back from that lift is not merely unanimated, it is somewhere else entirely.
        // Taken on the reference pose rather than on this one, because these vertices are meant to
        // hold still. Reading it off the animated pose would swing them with whichever bone happened
        // to be first in the list.
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

    /// Where the mesh sits once it is on the skeleton, taken from the first bone that matched.
    ///
    /// A mesh is rigid in the space it was authored in, so on the skeleton's own reference pose every
    /// bone has to compose to the same transform. That shared transform is where the authored space
    /// sits relative to the skeleton, which is data rather than a fault: Dogmeat is authored at the
    /// origin and this comes back as the identity, and a human body is authored at the neck and it
    /// comes back as a lift of about 120 units. Null when no bone matched.
    public static Matrix4x4? Placement(NifShape shape, Binding binding, AnimationPose.Pose pose)
    {
        for (int b = 0; b < shape.BoneNames.Count; b++)
            if (BoneMatrix(shape, binding, pose, b) is { } m) return m;

        return null;
    }

    /// How far the bones disagree with each other on the skeleton's own reference pose.
    ///
    /// A mesh is rigid in the space it was authored in, so on that pose every bone has to compose to
    /// one and the same transform. It does not have to be the identity. This is what the check used
    /// to assume, and it is what made a human body mesh read as 120 units of fault while the
    /// transforms were composing perfectly: the body is authored with its origin at the neck, its
    /// vertices run from -120 to -6, and the bind lifts the whole thing onto the ground. Every bone
    /// agreed on that lift to within a hundredth of a unit. Measuring the distance from the authored
    /// position instead of the disagreement between bones reported the mesh's own placement as though
    /// it were a defect.
    ///
    /// Disagreement is the thing that cannot be innocent, and it still catches what the old measure
    /// was there to catch. A rotation read in the wrong order gives every bone a different wrong
    /// answer, since each one is turned differently, so the spread goes to tens of units: measured on
    /// Dogmeat at 50 to 107 for reading the stored rotation straight across, inverting it, or both,
    /// against 0.245 for the transpose the game uses.
    ///
    /// Counted over the bones that matched the skeleton. A bone the skeleton does not have is a
    /// different problem, reported by name, and mixing the two hid this one for a session.
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

    /// How far apart two transforms put the same points, as the largest distance between them over
    /// the corners of a hundred unit cube. A cube rather than the origin alone, so a difference that
    /// is purely a turn is not read as agreement.
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
