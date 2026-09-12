using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

public enum HavokPhysicsShapeKind
{
    Unknown,
    Sphere,
    Capsule,
    Box,
    ConvexHull,
    Mesh,
    Compound,
}

public sealed class HavokPhysicsShape
{
    public int Id { get; init; }
    public HavokPhysicsShapeKind Kind { get; init; }
    public Vector3 HalfExtents { get; init; }
    public float Radius { get; init; }
    public List<int> Children { get; } = new();

    // A measured convex-polytope hull (vertices plus closed index rings, one ring per face),
    // as FO4 serializes hknpCapsuleShape and friends. Empty when the source file carried no
    // polytope payload for this shape; renderers then draw nothing but keep the shape listed.
    public List<Vector3> Vertices { get; } = new();
    public List<int[]> FaceRings { get; } = new();
}

public sealed class HavokRigidBody
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int ShapeId { get; init; }
    public int BoneIndex { get; init; } = -1;
    public float Mass { get; init; }

    // World-space center of mass, measured from the body's hknpMotionCinfo
    // (motionCinfos[motionId].centerOfMassWorld). Zero when the file carries no measured COM
    // for the body, so renderers can tell a real origin from a missing one.
    public Vector3 CenterOfMass { get; init; }

    // The body frame origin and orientation measured from hknpBodyCinfo.
    public Vector3 Position { get; init; }
    public Quaternion Rotation { get; init; } = Quaternion.Identity;

    // CenterOfMass is authored in the reference world frame, not body-local space. Convert
    // that reference-world offset back through the measured body orientation before applying
    // a posed body frame, otherwise a non-identity reference rotation is applied twice.
    public Vector3 PosedCenterOfMass(Vector3 posedPosition, Quaternion posedRotation)
    {
        if (CenterOfMass == Vector3.Zero) return Vector3.Zero;
        var referenceRotation = Rotation.LengthSquared() > 1e-10f
            ? Quaternion.Normalize(Rotation)
            : Quaternion.Identity;
        var targetRotation = posedRotation.LengthSquared() > 1e-10f
            ? Quaternion.Normalize(posedRotation)
            : Quaternion.Identity;
        var localOffset = Vector3.Transform(CenterOfMass - Position, Quaternion.Inverse(referenceRotation));
        return posedPosition + Vector3.Transform(localOffset, targetRotation);
    }
}

public enum HavokConstraintKind
{
    Unknown,
    Fixed,
    Hinge,
    LimitedHinge,
    BallAndSocket,
    Ragdoll,
}

public sealed class HavokConstraint
{
    public int Id { get; init; }
    public HavokConstraintKind Kind { get; init; }
    public int BodyA { get; init; }
    public int BodyB { get; init; }

    public float MinAngle { get; init; }
    public float MaxAngle { get; init; }

    public float? TwistMinAngle { get; init; }
    public float? TwistMaxAngle { get; init; }
    public float? ConeMaxAngle { get; init; }

    public int TwistAxis { get; init; }
    public int TwistRefAxis { get; init; } = 1;
    public int ConeTwistAxis { get; init; }
    public int ConeRefAxis { get; init; }
    public int LimitAxis { get; init; }

    public float[]? FrameA { get; init; }
    public float[]? FrameB { get; init; }
}

public sealed record HavokRagdollBoneBinding(int BoneIndex, int BodyId);

public sealed record HavokRagdollMapping(int BoneA, int BoneB, Vector3 AFromBTranslation, Quaternion AFromBRotation);

public readonly record struct HavokBoneFrame(Vector3 Position, Quaternion Rotation);

public sealed class HavokRagdollSkeleton
{
    public string Name { get; init; } = "";
    public List<string> BoneNames { get; } = new();
    public List<int> ParentIndices { get; } = new();
    public List<HkxBonePose> ReferencePose { get; } = new();

    // Compose the complete reference transform, not just translation. Keeping this as one
    // source of truth prevents mapper fallbacks and release previews from losing rotation.
    public IReadOnlyList<HavokBoneFrame> WorldReferenceFrames()
    {
        int count = ReferencePose.Count;
        var frames = new HavokBoneFrame[count];
        for (int i = 0; i < count; i++)
        {
            var pose = ReferencePose[i];
            int parent = i > 0 && ParentIndices.Count > i && ParentIndices[i] >= 0 && ParentIndices[i] < i
                ? ParentIndices[i]
                : -1;

            if (parent < 0)
            {
                frames[i] = new HavokBoneFrame(pose.Translation, Quaternion.Normalize(pose.Rotation));
            }
            else
            {
                var parentFrame = frames[parent];
                frames[i] = new HavokBoneFrame(
                    parentFrame.Position + Vector3.Transform(pose.Translation, parentFrame.Rotation),
                    Quaternion.Normalize(parentFrame.Rotation * pose.Rotation));
            }
        }
        return frames;
    }

    public IReadOnlyList<Vector3> WorldReferencePositions() =>
        WorldReferenceFrames().Select(frame => frame.Position).ToArray();
}

public sealed class HavokRagdollModel
{
    public List<HavokPhysicsShape> Shapes { get; } = new();
    public List<HavokRigidBody> Bodies { get; } = new();
    public List<HavokConstraint> Constraints { get; } = new();
    public List<HavokRagdollBoneBinding> BoneBindings { get; } = new();
    public HavokRagdollSkeleton? Skeleton { get; set; }
    public List<HavokRagdollMapping> Mappings { get; } = new();
    public HavokRagdollSkeleton? AnimationSkeleton { get; set; }

    public HavokBoneFrame[]? MapPose(IReadOnlyList<HavokBoneFrame>? animationWorld)
    {
        if (animationWorld == null || Mappings.Count == 0 || Skeleton == null) return null;

        var reference = Skeleton.WorldReferenceFrames();
        var frames = new HavokBoneFrame[reference.Count];
        var covered = new bool[reference.Count];

        foreach (var mapping in Mappings)
        {
            if (mapping.BoneA < 0 || mapping.BoneA >= animationWorld.Count) return null;
            if (mapping.BoneB < 0 || mapping.BoneB >= frames.Length) return null;

            var a = animationWorld[mapping.BoneA];
            frames[mapping.BoneB] = new HavokBoneFrame(
                a.Position + Vector3.Transform(mapping.AFromBTranslation, a.Rotation),
                Quaternion.Normalize(a.Rotation * mapping.AFromBRotation));
            covered[mapping.BoneB] = true;
        }

        for (int i = 0; i < reference.Count; i++)
            if (!covered[i])
                frames[i] = reference[i];

        return frames;
    }
}

public enum HavokPhysicsValidationLevel
{
    Warning,
    Error,
}

public sealed record HavokPhysicsValidationFinding(
    HavokPhysicsValidationLevel Level,
    string Where,
    string Message);

public static class HavokPhysicsValidator
{
    public static IReadOnlyList<HavokPhysicsValidationFinding> Check(HavokRagdollModel model, int? skeletonBoneCount = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var findings = new List<HavokPhysicsValidationFinding>();

        CheckUniqueIds(model.Shapes.Select(shape => shape.Id), "shape", findings);
        CheckUniqueIds(model.Bodies.Select(body => body.Id), "body", findings);
        CheckUniqueIds(model.Constraints.Select(constraint => constraint.Id), "constraint", findings);

        var shapes = model.Shapes.GroupBy(shape => shape.Id).ToDictionary(group => group.Key, group => group.First());
        var bodies = model.Bodies.GroupBy(body => body.Id).ToDictionary(group => group.Key, group => group.First());

        foreach (var shape in model.Shapes)
        {
            if (shape.Id < 0)
                findings.Add(Error($"shape {shape.Id}", "shape ids must be non-negative"));
            CheckFiniteNonNegative(shape.Radius, $"shape {shape.Id}", "radius", findings);
            CheckFiniteNonNegative(shape.HalfExtents.X, $"shape {shape.Id}", "half extent X", findings);
            CheckFiniteNonNegative(shape.HalfExtents.Y, $"shape {shape.Id}", "half extent Y", findings);
            CheckFiniteNonNegative(shape.HalfExtents.Z, $"shape {shape.Id}", "half extent Z", findings);

            foreach (var vertex in shape.Vertices)
                if (!Finite(vertex))
                    findings.Add(Error($"shape {shape.Id}", "polytope vertices must contain only finite values"));

            foreach (var ring in shape.FaceRings)
                foreach (int index in ring)
                    if (index < 0 || index >= shape.Vertices.Count)
                        findings.Add(new HavokPhysicsValidationFinding(
                            HavokPhysicsValidationLevel.Warning,
                            $"shape {shape.Id}",
                            $"face references missing polytope vertex {index}"));

            foreach (int child in shape.Children)
                if (!shapes.ContainsKey(child))
                    findings.Add(Error($"shape {shape.Id}", $"references missing child shape {child}"));
        }

        CheckShapeCycles(shapes, findings);

        foreach (var body in model.Bodies)
        {
            if (body.Id < 0)
                findings.Add(Error($"body {body.Id}", "body ids must be non-negative"));
            if (!shapes.ContainsKey(body.ShapeId))
                findings.Add(Error($"body {body.Id}", $"references missing shape {body.ShapeId}"));
            CheckFiniteNonNegative(body.Mass, $"body {body.Id}", "mass", findings);
            if (!Finite(body.CenterOfMass))
                findings.Add(Error($"body {body.Id}", "center of mass must contain only finite values"));
            if (!Finite(body.Position))
                findings.Add(Error($"body {body.Id}", "position must contain only finite values"));
            if (!Finite(body.Rotation))
                findings.Add(Error($"body {body.Id}", "rotation must contain only finite values"));
            else
            {
                float norm = MathF.Sqrt(
                    body.Rotation.X * body.Rotation.X + body.Rotation.Y * body.Rotation.Y +
                    body.Rotation.Z * body.Rotation.Z + body.Rotation.W * body.Rotation.W);
                if (norm < RotationNormEpsilon)
                    findings.Add(Error($"body {body.Id}", "rotation is degenerate (its length is effectively zero)"));
                else if (MathF.Abs(norm - 1f) > RotationNormTolerance)
                    findings.Add(Error($"body {body.Id}",
                        $"rotation is not a unit quaternion (length {norm:0.####}); normalise it before use"));
            }
            if (body.BoneIndex < -1)
                findings.Add(Error($"body {body.Id}", "bone index cannot be below -1"));
            if (skeletonBoneCount is int count && body.BoneIndex >= count)
                findings.Add(Error($"body {body.Id}", $"bone index {body.BoneIndex} is outside a {count}-bone skeleton"));
        }

        foreach (var constraint in model.Constraints)
        {
            if (constraint.Id < 0)
                findings.Add(Error($"constraint {constraint.Id}", "constraint ids must be non-negative"));
            if (!bodies.ContainsKey(constraint.BodyA))
                findings.Add(Error($"constraint {constraint.Id}", $"references missing body {constraint.BodyA}"));
            if (!bodies.ContainsKey(constraint.BodyB))
                findings.Add(Error($"constraint {constraint.Id}", $"references missing body {constraint.BodyB}"));
            if (constraint.BodyA == constraint.BodyB)
                findings.Add(Error($"constraint {constraint.Id}", "cannot constrain a body to itself"));
            if (!Finite(constraint.MinAngle) || !Finite(constraint.MaxAngle))
                findings.Add(Error($"constraint {constraint.Id}", "angle limits must be finite"));
            else if (constraint.MinAngle > constraint.MaxAngle)
                findings.Add(Error($"constraint {constraint.Id}", "minimum angle is greater than maximum angle"));

            CheckFrame(constraint.FrameA, $"constraint {constraint.Id}", "frame A", findings);
            CheckFrame(constraint.FrameB, $"constraint {constraint.Id}", "frame B", findings);

            if (constraint.TwistMinAngle is float twistMin && constraint.TwistMaxAngle is float twistMax)
            {
                if (!Finite(twistMin) || !Finite(twistMax))
                    findings.Add(Error($"constraint {constraint.Id}", "twist angle limits must be finite"));
                else if (twistMin > twistMax)
                    findings.Add(Error($"constraint {constraint.Id}", "minimum twist angle is greater than maximum twist angle"));
            }

            if (constraint.ConeMaxAngle is float coneMax)
            {
                if (!Finite(coneMax) || coneMax < 0)
                    findings.Add(Error($"constraint {constraint.Id}", "cone half-angle must be a finite non-negative value"));
            }

            CheckAxisIndex(constraint.TwistAxis, constraint.FrameA, $"constraint {constraint.Id}", "twist axis", findings);
            CheckAxisIndex(constraint.TwistRefAxis, constraint.FrameA, $"constraint {constraint.Id}", "twist reference axis", findings);
            CheckAxisIndex(constraint.ConeTwistAxis, constraint.FrameA, $"constraint {constraint.Id}", "cone twist axis", findings);
            CheckAxisIndex(constraint.ConeRefAxis, constraint.FrameB, $"constraint {constraint.Id}", "cone reference axis", findings);
            CheckAxisIndex(constraint.LimitAxis, constraint.FrameA, $"constraint {constraint.Id}", "limit axis", findings);
        }

        var boundBones = new HashSet<int>();
        var boundBodies = new HashSet<int>();
        foreach (var binding in model.BoneBindings)
        {
            if (!bodies.ContainsKey(binding.BodyId))
                findings.Add(Error($"bone {binding.BoneIndex}", $"references missing body {binding.BodyId}"));
            if (binding.BoneIndex < 0)
                findings.Add(Error($"bone {binding.BoneIndex}", "bone index must be non-negative"));
            if (skeletonBoneCount is int count && binding.BoneIndex >= count)
                findings.Add(Error($"bone {binding.BoneIndex}", $"is outside a {count}-bone skeleton"));
            if (model.Skeleton is { } boundSkeleton && binding.BoneIndex >= boundSkeleton.BoneNames.Count)
                findings.Add(Error($"bone {binding.BoneIndex}",
                    $"is outside the {boundSkeleton.BoneNames.Count}-bone physics skeleton"));
            if (!boundBones.Add(binding.BoneIndex))
                findings.Add(Error($"bone {binding.BoneIndex}", "is mapped to more than one rigid body"));
            if (!boundBodies.Add(binding.BodyId))
                findings.Add(new HavokPhysicsValidationFinding(
                    HavokPhysicsValidationLevel.Warning,
                    $"body {binding.BodyId}",
                    "is mapped to more than one skeleton bone"));
        }

        if (model.Skeleton is { } skeleton)
        {
            int boneCount = skeleton.BoneNames.Count;
            if (skeleton.ParentIndices.Count != boneCount)
                findings.Add(Error("skeleton",
                    $"has {boneCount} bone names but {skeleton.ParentIndices.Count} parent indices"));
            if (skeleton.ReferencePose.Count != boneCount)
                findings.Add(Error("skeleton",
                    $"has {boneCount} bone names but {skeleton.ReferencePose.Count} reference poses"));

            for (int i = 0; i < skeleton.ParentIndices.Count; i++)
            {
                int parent = skeleton.ParentIndices[i];
                if (parent < -1 || parent >= boneCount)
                    findings.Add(Error($"bone {i}", $"parent index {parent} is outside the skeleton"));
            }

            for (int i = 0; i < skeleton.ReferencePose.Count; i++)
            {
                var pose = skeleton.ReferencePose[i];
                if (!Finite(pose.Translation) || !Finite(pose.Rotation))
                    findings.Add(Error($"bone {i}", "reference pose must contain only finite values"));
                if (Vector3.Distance(pose.Scale, Vector3.One) > ScaleTolerance)
                    findings.Add(new HavokPhysicsValidationFinding(
                        HavokPhysicsValidationLevel.Warning,
                        $"bone {i}",
                        $"reference pose scale ({pose.Scale}) is not identity and is not applied"));
            }
        }

        if (model.AnimationSkeleton is { } animation)
        {
            int animBoneCount = animation.BoneNames.Count;
            if (animation.ParentIndices.Count != animBoneCount)
                findings.Add(Error("animation skeleton",
                    $"has {animBoneCount} bone names but {animation.ParentIndices.Count} parent indices"));
            if (animation.ReferencePose.Count != animBoneCount)
                findings.Add(Error("animation skeleton",
                    $"has {animBoneCount} bone names but {animation.ReferencePose.Count} reference poses"));
        }

        var mappedPhysicsBones = new HashSet<int>();
        for (int i = 0; i < model.Mappings.Count; i++)
        {
            var mapping = model.Mappings[i];
            string where = $"mapping {i}";
            if (mapping.BoneA < 0 || (model.AnimationSkeleton is { } animated &&
                                      mapping.BoneA >= animated.BoneNames.Count))
                findings.Add(Error(where, $"animation bone {mapping.BoneA} is outside the animation skeleton"));
            if (mapping.BoneB < 0 || (model.Skeleton is { } physics &&
                                      mapping.BoneB >= physics.BoneNames.Count))
                findings.Add(Error(where, $"physics bone {mapping.BoneB} is outside the physics skeleton"));

            if (!Finite(mapping.AFromBTranslation))
                findings.Add(Error(where, "aFromB translation must contain only finite values"));
            if (!Finite(mapping.AFromBRotation))
                findings.Add(Error(where, "aFromB rotation must contain only finite values"));
            else
            {
                float norm = MathF.Sqrt(
                    mapping.AFromBRotation.X * mapping.AFromBRotation.X +
                    mapping.AFromBRotation.Y * mapping.AFromBRotation.Y +
                    mapping.AFromBRotation.Z * mapping.AFromBRotation.Z +
                    mapping.AFromBRotation.W * mapping.AFromBRotation.W);
                if (norm < RotationNormEpsilon)
                    findings.Add(Error(where, "aFromB rotation is degenerate (its length is effectively zero)"));
            }

            if (!mappedPhysicsBones.Add(mapping.BoneB))
                findings.Add(Error(where, $"physics bone {mapping.BoneB} is mapped more than once"));
        }

        if (model.Mappings.Count > 0 && model.Skeleton is { } mappedPhysics &&
            model.AnimationSkeleton != null)
            for (int bone = 0; bone < mappedPhysics.BoneNames.Count; bone++)
                if (!mappedPhysicsBones.Contains(bone))
                    findings.Add(new HavokPhysicsValidationFinding(
                        HavokPhysicsValidationLevel.Warning,
                        $"bone {bone}",
                        "is not covered by any animation mapping and stays at its reference frame"));

        return findings;
    }

    private const float RotationNormTolerance = 1e-3f;
    private const float RotationNormEpsilon = 1e-4f;
    private const float ScaleTolerance = 1e-4f;

    private static void CheckShapeCycles(
        IReadOnlyDictionary<int, HavokPhysicsShape> shapes,
        List<HavokPhysicsValidationFinding> findings)
    {
        const int visiting = 1;
        const int done = 2;
        var state = new Dictionary<int, int>();
        var reported = new HashSet<int>();

        foreach (var shape in shapes.Values)
            Visit(shape.Id, new List<int>());

        void Visit(int id, List<int> path)
        {
            if (state.GetValueOrDefault(id) == done) return;
            if (state.GetValueOrDefault(id) == visiting)
            {
                int start = path.LastIndexOf(id);
                var loop = start >= 0 ? path.Skip(start).Append(id) : path.Append(id);
                if (reported.Add(id))
                    findings.Add(Error($"shape {id}",
                        "is part of a containment cycle (" + string.Join(" -> ", loop) + ")"));
                return;
            }

            if (!shapes.TryGetValue(id, out var shape)) return;
            state[id] = visiting;
            path.Add(id);
            foreach (int child in shape.Children)
                Visit(child, path);
            path.RemoveAt(path.Count - 1);
            state[id] = done;
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool Finite(Vector3 value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);

    private static bool Finite(Quaternion value) =>
        Finite(value.X) && Finite(value.Y) && Finite(value.Z) && Finite(value.W);

    private static void CheckFiniteNonNegative(float value, string where, string field,
                                               List<HavokPhysicsValidationFinding> findings)
    {
        if (!Finite(value) || value < 0)
            findings.Add(Error(where, $"{field} must be a finite non-negative value"));
    }

    private static void CheckUniqueIds(IEnumerable<int> ids, string kind, List<HavokPhysicsValidationFinding> findings)
    {
        foreach (var duplicate in ids.GroupBy(id => id).Where(group => group.Count() > 1))
            findings.Add(Error($"{kind} {duplicate.Key}", $"duplicate {kind} id"));
    }

    private static void CheckFrame(float[]? frame, string where, string label,
                                   List<HavokPhysicsValidationFinding> findings)
    {
        if (frame == null) return;
        if (frame.Length != 16)
        {
            findings.Add(Error(where, $"{label} must hold sixteen floats"));
            return;
        }
        if (frame.Any(value => !Finite(value)))
            findings.Add(Error(where, $"{label} must contain only finite values"));
    }

    private static void CheckAxisIndex(int axis, float[]? frame, string where, string label,
                                       List<HavokPhysicsValidationFinding> findings)
    {
        if (frame == null) return;
        if (axis < 0 || axis > 2)
            findings.Add(Error(where, $"{label} {axis} is outside a three-column frame"));
    }

    private static HavokPhysicsValidationFinding Error(string where, string message) =>
        new(HavokPhysicsValidationLevel.Error, where, message);
}
