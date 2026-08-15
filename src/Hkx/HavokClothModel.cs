using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

public enum HavokClothShapeKind
{
    Unknown,
    Sphere,
    Capsule,
    TaperedCapsule,
    Plane,
    ConvexGeometry,
}

public sealed class HavokClothShape
{
    public int Id { get; init; }
    public HavokClothShapeKind Kind { get; init; }
    public Vector3 Start { get; init; }
    public Vector3 End { get; init; }
    public float Radius { get; init; }
    public float EndRadius { get; init; }
}

public sealed record HavokClothParticle(int Index, float Mass, float InverseMass, float Radius, float Friction);

public sealed class HavokClothBuffer
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int Type { get; init; }
    public int SubType { get; init; }
    public int VertexCount { get; init; }
    public int TriangleCount { get; init; }
}

public sealed class HavokClothTransformSet
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int Type { get; init; }
    public int TransformCount { get; init; }
}

public sealed class HavokClothCollidable
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int ShapeId { get; init; }
    public Matrix4x4 Transform { get; init; } = Matrix4x4.Identity;
    public bool PinchDetectionEnabled { get; init; }
    public int PinchDetectionPriority { get; init; }
    public float PinchDetectionRadius { get; init; }
}

public enum HavokClothConstraintKind
{
    Unknown,
    StandardLink,
    StretchLink,
    CompressibleLink,
    BendLink,
    BendStiffness,
    LocalRange,
    Transition,
    Volume,
}

public abstract record HavokClothConstraint;
public sealed record HavokClothLinkConstraint(int ParticleA, int ParticleB, float RestLength, float Stiffness, float? CompressionLength = null) : HavokClothConstraint;
public sealed record HavokClothBendLinkConstraint(int ParticleA, int ParticleB, float BendMinLength, float StretchMaxLength, float BendStiffness, float StretchStiffness) : HavokClothConstraint;
public sealed record HavokClothBendStiffnessConstraint(int ParticleA, int ParticleB, int ParticleC, int ParticleD, float BendStiffness, float RestCurvature) : HavokClothConstraint;
public sealed record HavokClothLocalRangeConstraint(int ParticleIndex, int ReferenceVertex, float MaximumDistance, float MaxNormalDistance, float MinNormalDistance) : HavokClothConstraint;
public sealed record HavokClothTransitionConstraint(int ParticleIndex, int ReferenceVertex, float ToAnimDelay, float ToSimDelay, float ToSimMaxDistance) : HavokClothConstraint;
public sealed record HavokClothVolumeConstraint(int ParticleIndex, Vector3 FrameVector, float Weight, float Stiffness) : HavokClothConstraint;

public sealed class HavokClothConstraintSet
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public HavokClothConstraintKind Kind { get; init; }
    public int ReferenceBufferIndex { get; init; } = -1;
    public List<HavokClothConstraint> Constraints { get; } = new();
}

public sealed class HavokClothOperator
{
    public int Id { get; init; }
    public string TypeName { get; init; } = "";
    public string Name { get; init; } = "";
    public List<int> InputBufferIndices { get; } = new();
    public List<int> OutputBufferIndices { get; } = new();
    public int SimClothIndex { get; init; } = -1;
}

public sealed class HavokClothState
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public List<int> OperatorIndices { get; } = new();
    public List<int> UsedBufferIndices { get; } = new();
    public List<int> UsedTransformSetIndices { get; } = new();
    public List<int> UsedSimClothIndices { get; } = new();
}

public sealed class HavokSimCloth
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public float TotalMass { get; init; }
    public float MaxParticleRadius { get; init; }
    public bool DoNormals { get; init; }
    public List<HavokClothParticle> Particles { get; } = new();
    public HashSet<int> FixedParticles { get; } = new();
    public List<int> TriangleIndices { get; } = new();
    public List<int> ConstraintSetIds { get; } = new();
}

public sealed class HavokClothModel
{
    public string Name { get; init; } = "";
    public string TargetPlatform { get; init; } = "";
    public List<HavokClothShape> Shapes { get; } = new();
    public List<HavokClothCollidable> Collidables { get; } = new();
    public List<HavokClothBuffer> Buffers { get; } = new();
    public List<HavokClothTransformSet> TransformSets { get; } = new();
    public List<HavokClothConstraintSet> ConstraintSets { get; } = new();
    public List<HavokClothOperator> Operators { get; } = new();
    public List<HavokClothState> States { get; } = new();
    public List<HavokSimCloth> SimCloths { get; } = new();
}

public static class HavokClothValidator
{
    public static IReadOnlyList<HavokPhysicsValidationFinding> Check(HavokClothModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var findings = new List<HavokPhysicsValidationFinding>();

        CheckUniqueIds(model.Shapes.Select(value => value.Id), "cloth shape", findings);
        CheckUniqueIds(model.Collidables.Select(value => value.Id), "cloth collidable", findings);
        CheckUniqueIds(model.Buffers.Select(value => value.Id), "cloth buffer", findings);
        CheckUniqueIds(model.TransformSets.Select(value => value.Id), "cloth transform set", findings);
        CheckUniqueIds(model.ConstraintSets.Select(value => value.Id), "cloth constraint set", findings);
        CheckUniqueIds(model.Operators.Select(value => value.Id), "cloth operator", findings);
        CheckUniqueIds(model.States.Select(value => value.Id), "cloth state", findings);
        CheckUniqueIds(model.SimCloths.Select(value => value.Id), "sim cloth", findings);

        CheckNonNegativeIds(model.Shapes.Select(value => value.Id), "cloth shape", findings);
        CheckNonNegativeIds(model.Collidables.Select(value => value.Id), "cloth collidable", findings);
        CheckNonNegativeIds(model.Buffers.Select(value => value.Id), "cloth buffer", findings);
        CheckNonNegativeIds(model.TransformSets.Select(value => value.Id), "cloth transform set", findings);
        CheckNonNegativeIds(model.ConstraintSets.Select(value => value.Id), "cloth constraint set", findings);
        CheckNonNegativeIds(model.Operators.Select(value => value.Id), "cloth operator", findings);
        CheckNonNegativeIds(model.States.Select(value => value.Id), "cloth state", findings);
        CheckNonNegativeIds(model.SimCloths.Select(value => value.Id), "sim cloth", findings);

        var shapes = model.Shapes.GroupBy(value => value.Id).ToDictionary(group => group.Key, group => group.First());
        var constraintSets = model.ConstraintSets.GroupBy(value => value.Id).ToDictionary(group => group.Key, group => group.First());

        foreach (var shape in model.Shapes)
        {
            CheckFiniteNonNegative(shape.Radius, $"cloth shape {shape.Id}", "radius", findings);
            CheckFiniteNonNegative(shape.EndRadius, $"cloth shape {shape.Id}", "end radius", findings);
            if (!Finite(shape.Start) || !Finite(shape.End))
                findings.Add(Error($"cloth shape {shape.Id}", "shape endpoints must contain only finite values"));
        }

        foreach (var collidable in model.Collidables)
        {
            if (!shapes.ContainsKey(collidable.ShapeId))
                findings.Add(Error($"cloth collidable {collidable.Id}", $"references missing shape {collidable.ShapeId}"));
            CheckFiniteNonNegative(collidable.PinchDetectionRadius, $"cloth collidable {collidable.Id}", "pinch detection radius", findings);
            if (!Finite(collidable.Transform))
                findings.Add(Error($"cloth collidable {collidable.Id}", "transform must contain only finite values"));
        }

        foreach (var buffer in model.Buffers)
        {
            if (buffer.VertexCount < 0)
                findings.Add(Error($"cloth buffer {buffer.Id}", "vertex count cannot be negative"));
            if (buffer.TriangleCount < 0)
                findings.Add(Error($"cloth buffer {buffer.Id}", "triangle count cannot be negative"));
        }

        foreach (var transformSet in model.TransformSets)
            if (transformSet.TransformCount < 0)
                findings.Add(Error($"cloth transform set {transformSet.Id}", "transform count cannot be negative"));

        foreach (var set in model.ConstraintSets)
        {
            if (set.ReferenceBufferIndex < -1 || set.ReferenceBufferIndex >= model.Buffers.Count)
                findings.Add(Error($"cloth constraint set {set.Id}",
                    $"reference buffer index {set.ReferenceBufferIndex} is outside the available buffers"));

            foreach (var constraint in set.Constraints)
                if (!Matches(set.Kind, constraint))
                    findings.Add(Error($"cloth constraint set {set.Id}",
                        $"declares {set.Kind} but contains {constraint.GetType().Name}"));
        }

        foreach (var sim in model.SimCloths)
        {
            var particleIndices = sim.Particles.Select(value => value.Index).ToList();
            CheckUniqueIds(particleIndices, $"sim cloth {sim.Id} particle", findings);
            var particles = particleIndices.ToHashSet();

            foreach (var particle in sim.Particles)
            {
                if (particle.Index < 0)
                    findings.Add(Error($"sim cloth {sim.Id} particle {particle.Index}", "particle index must be non-negative"));
                CheckFiniteNonNegative(particle.Mass, $"sim cloth {sim.Id} particle {particle.Index}", "mass", findings);
                CheckFiniteNonNegative(particle.InverseMass, $"sim cloth {sim.Id} particle {particle.Index}", "inverse mass", findings);
                CheckFiniteNonNegative(particle.Radius, $"sim cloth {sim.Id} particle {particle.Index}", "radius", findings);
                CheckFiniteNonNegative(particle.Friction, $"sim cloth {sim.Id} particle {particle.Index}", "friction", findings);
            }

            foreach (int fixedParticle in sim.FixedParticles)
                if (!particles.Contains(fixedParticle))
                    findings.Add(Error($"sim cloth {sim.Id}", $"fixed particle {fixedParticle} does not exist"));

            if (sim.TriangleIndices.Count % 3 != 0)
                findings.Add(Error($"sim cloth {sim.Id}", "triangle index count must be divisible by three"));

            foreach (int particleIndex in sim.TriangleIndices)
                if (!particles.Contains(particleIndex))
                    findings.Add(Error($"sim cloth {sim.Id}", $"triangle references missing particle {particleIndex}"));

            CheckFiniteNonNegative(sim.TotalMass, $"sim cloth {sim.Id}", "total mass", findings);
            CheckFiniteNonNegative(sim.MaxParticleRadius, $"sim cloth {sim.Id}", "maximum particle radius", findings);

            foreach (int constraintSetId in sim.ConstraintSetIds)
                if (!constraintSets.ContainsKey(constraintSetId))
                    findings.Add(Error($"sim cloth {sim.Id}", $"references missing constraint set {constraintSetId}"));

            foreach (int constraintSetId in sim.ConstraintSetIds)
                if (constraintSets.TryGetValue(constraintSetId, out var set))
                    CheckConstraintSet(set, sim, model, findings);
        }

        foreach (var op in model.Operators)
        {
            foreach (int index in op.InputBufferIndices)
                CheckIndex(index, model.Buffers.Count, $"cloth operator {op.Id}", "input buffer", findings);
            foreach (int index in op.OutputBufferIndices)
                CheckIndex(index, model.Buffers.Count, $"cloth operator {op.Id}", "output buffer", findings);
            if (op.SimClothIndex >= 0)
                CheckIndex(op.SimClothIndex, model.SimCloths.Count, $"cloth operator {op.Id}", "sim cloth", findings);
            else if (op.SimClothIndex < -1)
                findings.Add(Error($"cloth operator {op.Id}", "sim cloth index cannot be below -1"));
        }

        foreach (var state in model.States)
        {
            foreach (int index in state.OperatorIndices)
                CheckIndex(index, model.Operators.Count, $"cloth state {state.Id}", "operator", findings);
            foreach (int index in state.UsedBufferIndices)
                CheckIndex(index, model.Buffers.Count, $"cloth state {state.Id}", "buffer", findings);
            foreach (int index in state.UsedTransformSetIndices)
                CheckIndex(index, model.TransformSets.Count, $"cloth state {state.Id}", "transform set", findings);
            foreach (int index in state.UsedSimClothIndices)
                CheckIndex(index, model.SimCloths.Count, $"cloth state {state.Id}", "sim cloth", findings);
        }

        return findings;
    }

    private static bool Matches(HavokClothConstraintKind kind, HavokClothConstraint constraint) => kind switch
    {
        HavokClothConstraintKind.StandardLink or
        HavokClothConstraintKind.StretchLink or
        HavokClothConstraintKind.CompressibleLink => constraint is HavokClothLinkConstraint,
        HavokClothConstraintKind.BendLink => constraint is HavokClothBendLinkConstraint,
        HavokClothConstraintKind.BendStiffness => constraint is HavokClothBendStiffnessConstraint,
        HavokClothConstraintKind.LocalRange => constraint is HavokClothLocalRangeConstraint,
        HavokClothConstraintKind.Transition => constraint is HavokClothTransitionConstraint,
        HavokClothConstraintKind.Volume => constraint is HavokClothVolumeConstraint,
        _ => false,
    };

    private static void CheckConstraintSet(HavokClothConstraintSet set, HavokSimCloth sim, HavokClothModel model, List<HavokPhysicsValidationFinding> findings)
    {
        var particles = sim.Particles.Select(value => value.Index).ToHashSet();

        void Particle(int index)
        {
            if (!particles.Contains(index))
                findings.Add(Error($"cloth constraint set {set.Id}", $"references missing particle {index}"));
        }

        foreach (var constraint in set.Constraints)
        {
            switch (constraint)
            {
                case HavokClothLinkConstraint link:
                    Particle(link.ParticleA);
                    Particle(link.ParticleB);
                    if (link.ParticleA == link.ParticleB)
                        findings.Add(Error($"cloth constraint set {set.Id}", "link cannot constrain a particle to itself"));
                    CheckFiniteNonNegative(link.RestLength, $"cloth constraint set {set.Id}", "rest length", findings);
                    CheckFiniteNonNegative(link.Stiffness, $"cloth constraint set {set.Id}", "stiffness", findings);
                    if (link.CompressionLength is float compression)
                        CheckFiniteNonNegative(compression, $"cloth constraint set {set.Id}", "compression length", findings);
                    break;
                case HavokClothBendLinkConstraint bendLink:
                    Particle(bendLink.ParticleA);
                    Particle(bendLink.ParticleB);
                    if (bendLink.ParticleA == bendLink.ParticleB)
                        findings.Add(Error($"cloth constraint set {set.Id}", "bend link cannot constrain a particle to itself"));
                    CheckFiniteNonNegative(bendLink.BendMinLength, $"cloth constraint set {set.Id}", "bend minimum length", findings);
                    CheckFiniteNonNegative(bendLink.StretchMaxLength, $"cloth constraint set {set.Id}", "stretch maximum length", findings);
                    CheckFiniteNonNegative(bendLink.BendStiffness, $"cloth constraint set {set.Id}", "bend stiffness", findings);
                    CheckFiniteNonNegative(bendLink.StretchStiffness, $"cloth constraint set {set.Id}", "stretch stiffness", findings);
                    break;
                case HavokClothBendStiffnessConstraint bend:
                    Particle(bend.ParticleA);
                    Particle(bend.ParticleB);
                    Particle(bend.ParticleC);
                    Particle(bend.ParticleD);
                    CheckFiniteNonNegative(bend.BendStiffness, $"cloth constraint set {set.Id}", "bend stiffness", findings);
                    if (!Finite(bend.RestCurvature))
                        findings.Add(Error($"cloth constraint set {set.Id}", "rest curvature must be finite"));
                    break;
                case HavokClothLocalRangeConstraint range:
                    Particle(range.ParticleIndex);
                    CheckReferenceVertex(set, range.ReferenceVertex, model, findings);
                    CheckFiniteNonNegative(range.MaximumDistance, $"cloth constraint set {set.Id}", "maximum distance", findings);
                    if (!Finite(range.MaxNormalDistance) || !Finite(range.MinNormalDistance))
                        findings.Add(Error($"cloth constraint set {set.Id}", "normal distance limits must be finite"));
                    else if (range.MinNormalDistance > range.MaxNormalDistance)
                        findings.Add(Error($"cloth constraint set {set.Id}", "minimum normal distance is greater than maximum normal distance"));
                    break;
                case HavokClothTransitionConstraint transition:
                    Particle(transition.ParticleIndex);
                    CheckReferenceVertex(set, transition.ReferenceVertex, model, findings);
                    CheckFiniteNonNegative(transition.ToAnimDelay, $"cloth constraint set {set.Id}", "animation delay", findings);
                    CheckFiniteNonNegative(transition.ToSimDelay, $"cloth constraint set {set.Id}", "simulation delay", findings);
                    CheckFiniteNonNegative(transition.ToSimMaxDistance, $"cloth constraint set {set.Id}", "simulation maximum distance", findings);
                    break;
                case HavokClothVolumeConstraint volume:
                    Particle(volume.ParticleIndex);
                    if (!Finite(volume.FrameVector))
                        findings.Add(Error($"cloth constraint set {set.Id}", "volume frame vector must contain only finite values"));
                    CheckFiniteNonNegative(volume.Weight, $"cloth constraint set {set.Id}", "volume weight", findings);
                    CheckFiniteNonNegative(volume.Stiffness, $"cloth constraint set {set.Id}", "volume stiffness", findings);
                    break;
            }
        }
    }

    private static void CheckReferenceVertex(HavokClothConstraintSet set, int vertex, HavokClothModel model, List<HavokPhysicsValidationFinding> findings)
    {
        if (vertex < 0)
        {
            findings.Add(Error($"cloth constraint set {set.Id}", $"reference vertex {vertex} cannot be negative"));
            return;
        }
        if (set.ReferenceBufferIndex < 0)
            return;
        if (set.ReferenceBufferIndex >= model.Buffers.Count)
            return;
        int vertexCount = model.Buffers[set.ReferenceBufferIndex].VertexCount;
        if (vertex >= vertexCount)
            findings.Add(Error($"cloth constraint set {set.Id}", $"reference vertex {vertex} is outside buffer {set.ReferenceBufferIndex} with {vertexCount} vertices"));
    }

    private static void CheckIndex(int index, int count, string where, string kind, List<HavokPhysicsValidationFinding> findings)
    {
        if (index < 0 || index >= count)
            findings.Add(Error(where, $"{kind} index {index} is outside the available range 0..{count - 1}"));
    }

    private static void CheckUniqueIds(IEnumerable<int> ids, string kind, List<HavokPhysicsValidationFinding> findings)
    {
        foreach (var duplicate in ids.GroupBy(value => value).Where(group => group.Count() > 1))
            findings.Add(Error($"{kind} {duplicate.Key}", $"duplicate {kind} id"));
    }

    private static void CheckNonNegativeIds(IEnumerable<int> ids, string kind, List<HavokPhysicsValidationFinding> findings)
    {
        foreach (int id in ids.Where(value => value < 0))
            findings.Add(Error($"{kind} {id}", $"{kind} ids must be non-negative"));
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool Finite(Vector3 value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);

    private static bool Finite(Matrix4x4 value) =>
        Finite(value.M11) && Finite(value.M12) && Finite(value.M13) && Finite(value.M14) &&
        Finite(value.M21) && Finite(value.M22) && Finite(value.M23) && Finite(value.M24) &&
        Finite(value.M31) && Finite(value.M32) && Finite(value.M33) && Finite(value.M34) &&
        Finite(value.M41) && Finite(value.M42) && Finite(value.M43) && Finite(value.M44);

    private static void CheckFiniteNonNegative(float value, string where, string field, List<HavokPhysicsValidationFinding> findings)
    {
        if (!Finite(value) || value < 0)
            findings.Add(Error(where, $"{field} must be a finite non-negative value"));
    }

    private static HavokPhysicsValidationFinding Error(string where, string message) =>
        new(HavokPhysicsValidationLevel.Error, where, message);
}