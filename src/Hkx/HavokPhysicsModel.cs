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
}

public sealed class HavokRigidBody
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int ShapeId { get; init; }
    public int BoneIndex { get; init; } = -1;
    public float Mass { get; init; }
    public Vector3 CenterOfMass { get; init; }
    public Quaternion Rotation { get; init; } = Quaternion.Identity;
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
}

public sealed record HavokRagdollBoneBinding(int BoneIndex, int BodyId);

public sealed class HavokRagdollModel
{
    public List<HavokPhysicsShape> Shapes { get; } = new();
    public List<HavokRigidBody> Bodies { get; } = new();
    public List<HavokConstraint> Constraints { get; } = new();
    public List<HavokRagdollBoneBinding> BoneBindings { get; } = new();
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

            foreach (int child in shape.Children)
            {
                if (!shapes.ContainsKey(child))
                    findings.Add(Error($"shape {shape.Id}", $"references missing child shape {child}"));
            }
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
            if (!boundBones.Add(binding.BoneIndex))
                findings.Add(Error($"bone {binding.BoneIndex}", "is mapped to more than one rigid body"));
            if (!boundBodies.Add(binding.BodyId))
                findings.Add(new HavokPhysicsValidationFinding(
                    HavokPhysicsValidationLevel.Warning,
                    $"body {binding.BodyId}",
                    "is mapped to more than one skeleton bone"));
        }

        return findings;
    }

    // A unit quaternion has length 1. Authored rotations are allowed a small slack for
    // float round-trips, but a near-zero length is a degenerate rotation, not slack.
    private const float RotationNormTolerance = 1e-3f;
    private const float RotationNormEpsilon = 1e-4f;

    // A compound shape may reference child shapes, which may themselves be compounds.
    // Direct self-reference is only the simplest cycle; an indirect cycle (A -> B -> A)
    // would make any recursive traversal of the compound tree loop forever. Walk the
    // whole containment graph with a depth-first colouring and report the first cycle
    // reachable from each shape.
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

    private static HavokPhysicsValidationFinding Error(string where, string message) =>
        new(HavokPhysicsValidationLevel.Error, where, message);
}