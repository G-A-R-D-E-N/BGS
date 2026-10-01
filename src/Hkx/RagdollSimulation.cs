using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using BepuUtilities.Memory;

namespace OpenCommonwealth.Services.Hkx;

public sealed partial class RagdollSimulation : IDisposable
{
    private const float StepSeconds = 1f / 120;
    private readonly BufferPool _pool = new();
    private Simulation? _simulation;
    private readonly Dictionary<int, (BodyHandle Handle, Vector3 Center)> _bodies = new();
    private readonly Dictionary<int, (Vector3 A, Vector3 B)[]> _hulls = new();
    private readonly HashSet<(int, int)> _adjacent = new();
    private double _accumulator;

    public RagdollSimulation(HavokRagdollModel model, IReadOnlyDictionary<int, float> masses,
        float gravity, float groundHeight = 0, IReadOnlyDictionary<int, HavokBoneFrame>? poses = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(masses);
        Validate(model, masses, gravity, groundHeight, poses);
        try
        {
            _simulation = Simulation.Create(_pool, new Contacts(_adjacent), new Gravity(gravity), new SolveDescription(12, 1));
            var shapes = model.Shapes.ToDictionary(shape => shape.Id);
            foreach (HavokRigidBody body in model.Bodies)
            {
                var hull = new ConvexHull(shapes[body.ShapeId].Vertices.ToArray().AsSpan(), _pool, out Vector3 center);
                var inertia = hull.ComputeInertia(masses[body.Id]);
                if (!float.IsFinite(inertia.InverseMass) || inertia.InverseMass <= 0 ||
                    !float.IsFinite(inertia.InverseInertiaTensor.XX) || !float.IsFinite(inertia.InverseInertiaTensor.YY) ||
                    !float.IsFinite(inertia.InverseInertiaTensor.ZZ))
                {
                    hull.Dispose(_pool);
                    throw new InvalidDataException($"Body {body.Id} has no usable uniform-density inertia.");
                }
                var shape = _simulation.Shapes.Add(hull);
                var edges = new List<(Vector3, Vector3)>();
                for (int face = 0; face < hull.FaceToVertexIndicesStart.Length; face++)
                {
                    hull.GetVertexIndicesForFace(face, out var indices);
                    for (int i = 0; i < indices.Length; i++)
                    {
                        hull.GetPoint(indices[i], out Vector3 from);
                        hull.GetPoint(indices[(i + 1) % indices.Length], out Vector3 to);
                        edges.Add((from, to));
                    }
                }
                _hulls.Add(body.Id, edges.ToArray());
                HavokBoneFrame frame = poses?.GetValueOrDefault(body.Id) ?? new HavokBoneFrame(body.Position, body.Rotation);
                var pose = new RigidPose(frame.Position + Vector3.Transform(center, frame.Rotation), frame.Rotation);
                BodyHandle handle = _simulation.Bodies.Add(BodyDescription.CreateDynamic(pose, inertia,
                    new CollidableDescription(shape, 0.1f), new BodyActivityDescription(0.01f)));
                _bodies.Add(body.Id, (handle, center));
            }
            var floor = _simulation.Shapes.Add(new Box(200000, 200000, 10));
            _simulation.Statics.Add(new StaticDescription(new Vector3(0, 0, groundHeight - 5), floor));
            foreach (HavokConstraint joint in model.Constraints) AddJoint(joint);
        }
        catch { Dispose(); throw; }
    }

    public IReadOnlyDictionary<int, HavokBoneFrame> Frames
    {
        get
        {
            Simulation simulation = Required();
            return _bodies.ToDictionary(pair => pair.Key, pair => {
                RigidPose pose = simulation.Bodies.GetBodyReference(pair.Value.Handle).Pose;
                return new HavokBoneFrame(pose.Position - Vector3.Transform(pair.Value.Center, pose.Orientation), pose.Orientation);
            });
        }
    }

    public void Advance(float seconds)
    {
        Simulation simulation = Required();
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        _accumulator += Math.Min(seconds, 0.25f);
        while (_accumulator >= StepSeconds)
        {
            simulation.Timestep(StepSeconds);
            _accumulator -= StepSeconds;
        }
        if (Frames.Values.Any(frame => !Finite(frame.Position) || !float.IsFinite(frame.Rotation.LengthSquared())))
            throw new InvalidDataException("Simulation produced a non-finite body frame.");
    }

    public (Vector3 A, Vector3 B)[] HullEdges
    {
        get
        {
            Simulation simulation = Required();
            return _hulls.SelectMany(pair => {
                RigidPose pose = simulation.Bodies.GetBodyReference(_bodies[pair.Key].Handle).Pose;
                return pair.Value.Select(edge => (pose.Position + Vector3.Transform(edge.A, pose.Orientation),
                    pose.Position + Vector3.Transform(edge.B, pose.Orientation)));
            }).ToArray();
        }
    }

    public void Impulse(int bodyId, Vector3 impulse)
    {
        Simulation simulation = Required();
        if (!Finite(impulse) || impulse.LengthSquared() > 1e10f) throw new ArgumentException("Impulse must be finite and no greater than 100000.", nameof(impulse));
        var body = simulation.Bodies.GetBodyReference(_bodies[bodyId].Handle);
        body.Awake = true;
        body.ApplyLinearImpulse(impulse);
    }

    public void Dispose()
    {
        _simulation?.Dispose();
        _simulation = null;
        _bodies.Clear();
        _hulls.Clear();
        _adjacent.Clear();
        _pool.Clear();
    }

    private Simulation Required() => _simulation ?? throw new ObjectDisposedException(nameof(RagdollSimulation));
    private static (int, int) Pair(int a, int b) => a < b ? (a, b) : (b, a);
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static void Validate(HavokRagdollModel model, IReadOnlyDictionary<int, float> masses,
        float gravity, float ground, IReadOnlyDictionary<int, HavokBoneFrame>? poses)
    {
        if (!float.IsFinite(gravity) || gravity < 0 || gravity > 100000 || !float.IsFinite(ground) || MathF.Abs(ground) > 100000)
            throw new ArgumentOutOfRangeException(nameof(gravity), "Gravity and ground must be finite and within preview bounds.");
        if (model.Bodies.Count is < 1 or > 128 || model.Constraints.Count > 512)
            throw new InvalidDataException("Preview supports 1–128 bodies and at most 512 joints.");
        var errors = HavokPhysicsValidator.Check(model).Where(f => f.Level == HavokPhysicsValidationLevel.Error).ToArray();
        if (errors.Length > 0) throw new InvalidDataException(errors[0].Message);
        var shapes = model.Shapes.ToDictionary(shape => shape.Id);
        foreach (HavokRigidBody body in model.Bodies)
        {
            if (!masses.TryGetValue(body.Id, out float mass) || !float.IsFinite(mass) || mass < 0.0001f || mass > 100000)
                throw new InvalidDataException($"Enter an explicit simulation mass for body {body.Id}, between 0.0001 and 100000.");
            HavokBoneFrame frame = poses?.GetValueOrDefault(body.Id) ?? new HavokBoneFrame(body.Position, body.Rotation);
            if (!Finite(frame.Position) || frame.Position.LengthSquared() > 1e10f ||
                !float.IsFinite(frame.Rotation.LengthSquared()) || MathF.Abs(frame.Rotation.LengthSquared() - 1) > 0.002f)
                throw new InvalidDataException($"Body {body.Id} has an invalid position or rotation.");
            HavokPhysicsShape shape = shapes[body.ShapeId];
            if (shape.Kind is HavokPhysicsShapeKind.Mesh or HavokPhysicsShapeKind.Compound or HavokPhysicsShapeKind.Unknown ||
                shape.Vertices.Count is < 4 or > 8192 || shape.Vertices.Any(v => !Finite(v) || v.LengthSquared() > 1e10f) || !FullVolume(shape.Vertices))
                throw new NotSupportedException($"Body {body.Id} needs a measured, nondegenerate convex hull; unsupported geometry is not approximated.");
        }
        foreach (HavokConstraint joint in model.Constraints) ValidateJoint(joint);
    }

    private static bool FullVolume(IReadOnlyList<Vector3> vertices)
    {
        Vector3 origin = vertices[0];
        Vector3 edge = vertices.FirstOrDefault(v => Vector3.DistanceSquared(v, origin) > 1e-8f) - origin;
        Vector3 normal = default;
        foreach (Vector3 vertex in vertices)
        {
            normal = Vector3.Cross(edge, vertex - origin);
            if (normal.LengthSquared() > 1e-8f) break;
        }
        if (normal.LengthSquared() <= 1e-8f) return false;
        normal = Vector3.Normalize(normal);
        return vertices.Any(v => MathF.Abs(Vector3.Dot(v - origin, normal)) > 1e-4f);
    }
}
