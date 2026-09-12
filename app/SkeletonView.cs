using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OpenCommonwealth.Services.Hkx;
using Vector3 = System.Numerics.Vector3;

namespace BehaviourStudio.App;

public class SkeletonView : Control
{
    private AnimationPose.Pose? _pose;
    private AnimationPose.Pose? _reference;

    private double _yaw = 0.6;
    private double _pitch = 0.25;
    private double _zoom = 1;
    private Point _pan;
    private Point _lastPointer;
    private Point _pressAt;
    private bool _orbiting;
    private bool _panning;

    private double _fit = 1;
    private Vector3 _centre;

    private HavokRagdollModel? _bodies;
    private Vector3 _bodyCentre;
    private float _bodyFit = 1;
    private HavokBoneFrame[]? _physicsPose;

    public string HoveredBone { get; private set; } = "";
    public Action<string>? BoneHovered;
    public Action<string?>? ConstraintSelectionChanged;

    public bool ShowReference { get; set; }
    public bool ShowBodies { get; set; }

    private bool _showConstraints;
    public bool ShowConstraints
    {
        get => _showConstraints;
        set
        {
            if (_showConstraints == value) return;
            _showConstraints = value;
            if (!value)
            {
                _hoveredConstraint = -1;
                _pinnedConstraint = -1;
            }
            NotifyConstraintSelection();
            InvalidateVisual();
        }
    }

    public bool ShowBindings { get; set; }
    public bool ShowPhysicsSkeleton { get; set; }
    public bool ShowFrames { get; set; }
    public bool EngineeringFrames { get; set; }

    public int DrawnBones => _pose?.Bones.Count ?? 0;
    public int DrawnEdges => _mesh?.Length ?? 0;
    public int DrawnBodies => _bodies?.Bodies.Count ?? 0;
    public int DrawnConstraints => _bodies?.Constraints.Count ?? 0;
    public int DrawnBindings => _bodies?.BoneBindings.Count ?? 0;
    public int DrawnPhysicsBones => _bodies?.Skeleton?.ReferencePose.Count ?? 0;
    public int DrawnFrames => _bodies?.Bodies.Count ?? 0;
    public int DrawnMappings => _bodies?.Mappings.Count ?? 0;

    public float BodyFit => _bodyFit;
    internal Vector3 ViewCentre => _centre;
    public float AxisStubLength => Math.Max(0.5f, _bodyFit * 0.06f);

    public string ScaleStatusText
    {
        get
        {
            if (_bodies == null || _bodies.Bodies.Count == 0) return "";
            return "body fit " + _bodyFit.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                   "frame stub " + AxisStubLength.ToString("0.00", CultureInfo.InvariantCulture) +
                   " file units";
        }
    }

    private int _hoveredBody = -1;
    private int _hoveredConstraint = -1;
    private int _pinnedConstraint = -1;
    private readonly HashSet<int> _pinnedBodies = new();

    public int HoveredConstraintId => _hoveredConstraint;
    public int HoveredBodyId => _hoveredBody;
    public int PinnedConstraintId => _pinnedConstraint;
    public IReadOnlyCollection<int> PinnedBodyIds => _pinnedBodies;

    public void HoverBodyForTest(int bodyId)
    {
        _hoveredBody = bodyId;
        InvalidateVisual();
    }

    public void ToggleBodyPinForTest(int bodyId)
    {
        if (!_pinnedBodies.Remove(bodyId)) _pinnedBodies.Add(bodyId);
        InvalidateVisual();
    }

    public void HoverConstraintForTest(int constraintId)
    {
        _hoveredConstraint = constraintId;
        NotifyConstraintSelection();
        InvalidateVisual();
    }

    public void PinConstraintForTest(int constraintId)
    {
        _pinnedConstraint = constraintId;
        NotifyConstraintSelection();
        InvalidateVisual();
    }

    public static int PinnedAfterClick(int currentlyPinned, int picked) =>
        picked < 0 ? -1 : picked == currentlyPinned ? -1 : picked;

    private void NotifyConstraintSelection()
    {
        string? line = null;
        int id = _pinnedConstraint >= 0 ? _pinnedConstraint : _hoveredConstraint;
        if (_showConstraints && id >= 0 && _bodies is { Constraints.Count: > 0 })
        {
            var constraint = _bodies.Constraints.FirstOrDefault(c => c.Id == id);
            if (constraint != null) line = ConstraintInspectionLine(constraint, _bodies.Bodies);
        }
        ConstraintSelectionChanged?.Invoke(line);
    }

    public static string ConstraintInspectionLine(HavokConstraint constraint,
                                                  IReadOnlyList<HavokRigidBody> bodies)
    {
        string bodyA = bodies.FirstOrDefault(b => b.Id == constraint.BodyA)?.Name ?? "";
        string bodyB = bodies.FirstOrDefault(b => b.Id == constraint.BodyB)?.Name ?? "";
        if (bodyA.Length == 0) bodyA = "body " + constraint.BodyA;
        if (bodyB.Length == 0) bodyB = "body " + constraint.BodyB;

        var parts = new List<string>();
        switch (constraint.Kind)
        {
            case HavokConstraintKind.Ragdoll:
                if (constraint.TwistMinAngle is float twistMin && constraint.TwistMaxAngle is float twistMax)
                    parts.Add("twist " + Angle(twistMin) + ".." + Angle(twistMax) + " deg");
                if (constraint.ConeMaxAngle is float coneMax)
                    parts.Add("cone " + Angle(coneMax) + " deg");
                break;
            case HavokConstraintKind.LimitedHinge:
                if (constraint.MinAngle != 0 || constraint.MaxAngle != 0)
                    parts.Add("hinge " + Angle(constraint.MinAngle) + ".." + Angle(constraint.MaxAngle) + " deg");
                break;
        }

        if (constraint.FrameA != null)
            parts.Add("pivotA " + Offset(constraint.FrameA));
        if (constraint.FrameB != null)
            parts.Add("pivotB " + Offset(constraint.FrameB));

        string measured = parts.Count > 0 ? string.Join(", ", parts) : "no measured limits";
        return $"{KindName(constraint.Kind)}: c{constraint.Id} {bodyA}({constraint.BodyA}) -> " +
               $"{bodyB}({constraint.BodyB}) - {measured}";
    }

    private static string Offset(float[] frame) =>
        "(" + F(frame[12]) + ", " + F(frame[13]) + ", " + F(frame[14]) + ")";

    private static string F(float v) =>
        v == 0 ? "0.00" : v.ToString("0.00", CultureInfo.InvariantCulture);

    private (Vector3 A, Vector3 B)[]? _mesh;

    public void ShowMesh((Vector3 A, Vector3 B)[]? segments)
    {
        _mesh = segments;
        InvalidateVisual();
    }

    public void Show(AnimationPose.Pose? pose, AnimationPose.Pose? reference = null)
    {
        _pose = pose;
        _reference = reference;
        if (!EngineeringFrames && pose != null && pose.Bones.Count > 0)
        {
            _centre = pose.Centre;
            _fit = pose.Radius;
        }
        UpdatePhysicsPose();
        InvalidateVisual();
    }

    public void Update(AnimationPose.Pose? pose)
    {
        _pose = pose;
        UpdatePhysicsPose();
        InvalidateVisual();
    }

    public void Reset()
    {
        _pose = null;
        _reference = null;
        _mesh = null;
        _bodies = null;
        _bodyCentre = Vector3.Zero;
        _bodyFit = 1;
        ShowBodies = false;
        ShowConstraints = false;
        ShowBindings = false;
        ShowPhysicsSkeleton = false;
        ShowFrames = false;
        EngineeringFrames = false;
        _hoveredBody = -1;
        _hoveredConstraint = -1;
        _pinnedConstraint = -1;
        _pinnedBodies.Clear();
        _physicsPose = null;
        ClearDrop();
        _yaw = 0.6;
        _pitch = 0.25;
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
    }

    public void SetBodies(HavokRagdollModel? model)
    {
        _bodies = model;
        UpdatePhysicsPose();
        FitBodies();
        InvalidateVisual();
    }

    private const float DropGravity = 600f;
    private const float DropRestEpsilon = 0.01f;
    private const float DropDamping = 4f;
    private const float DropSubstep = 1f / 120f;
    private const int DropIterations = 20;

    private Vector3[]? _releasePositions;
    private Quaternion[]? _releaseRotations;
    private Quaternion[]? _dropRotations;
    private Vector3[]? _dropPositions;
    private Vector3[]? _dropVelocities;
    private Vector3[]? _dropPivotOffsets;
    private Dictionary<int, int>? _dropBodyIndexById;
    private int[]? _dropBodyBoneIndexes;
    private int[]? _dropBodyDepth;
    private double _dropAccumulator;
    private float _dropTotal;
    private bool _dropResting;

    public bool IsDropped => _dropPositions != null;
    public bool DropResting => _dropResting;
    internal IReadOnlyList<Vector3>? DroppedPositions => _dropPositions;
    internal IReadOnlyList<Quaternion>? DroppedRotations => _dropRotations;
    internal float DropDistance => _dropTotal;

    public void StartDrop()
    {
        if (_bodies is not { Skeleton: { ReferencePose.Count: > 0 } }) return;
        var source = ReleaseSourceFrames();
        if (source == null || source.Length == 0) return;

        _releasePositions = source.Select(f => f.Position).ToArray();
        _releaseRotations = source.Select(f => f.Rotation).ToArray();
        _dropPositions = (Vector3[])_releasePositions.Clone();
        _dropRotations = (Quaternion[])_releaseRotations.Clone();
        _dropVelocities = new Vector3[_dropPositions.Length];
        _dropTotal = Math.Max(0, GlobalLowestVertexZ());

        _dropBodyIndexById = new Dictionary<int, int>();
        for (int i = 0; i < _bodies.Bodies.Count; i++) _dropBodyIndexById[_bodies.Bodies[i].Id] = i;
        _dropBodyBoneIndexes = new int[_bodies.Bodies.Count];
        for (int i = 0; i < _bodies.Bodies.Count; i++)
        {
            _dropBodyBoneIndexes[i] = -1;
            foreach (var binding in _bodies.BoneBindings)
            {
                if (binding.BodyId != _bodies.Bodies[i].Id) continue;
                _dropBodyBoneIndexes[i] = binding.BoneIndex;
                break;
            }
        }

        _dropBodyDepth = new int[_bodies.Bodies.Count];
        Array.Fill(_dropBodyDepth, int.MaxValue);
        if (_dropBodyDepth.Length > 0)
        {
            _dropBodyDepth[0] = 0;
            var queue = new Queue<int>();
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (var constraint in _bodies.Constraints)
                {
                    if (!_dropBodyIndexById.TryGetValue(constraint.BodyA, out int ai) ||
                        !_dropBodyIndexById.TryGetValue(constraint.BodyB, out int bi)) continue;
                    int other = ai == current ? bi : bi == current ? ai : -1;
                    if (other < 0 || _dropBodyDepth[other] != int.MaxValue) continue;
                    _dropBodyDepth[other] = _dropBodyDepth[current] + 1;
                    queue.Enqueue(other);
                }
            }
        }

        _dropPivotOffsets = new Vector3[_bodies.Constraints.Count > 0
            ? _bodies.Constraints.Max(c => c.Id) + 1 : 0];
        foreach (var constraint in _bodies.Constraints)
        {
            if (constraint.FrameA == null || constraint.FrameB == null) continue;
            if (!_dropBodyIndexById.TryGetValue(constraint.BodyA, out int ai) ||
                !_dropBodyIndexById.TryGetValue(constraint.BodyB, out int bi)) continue;
            int ba = _dropBodyBoneIndexes[ai];
            int bb = _dropBodyBoneIndexes[bi];
            if (ba < 0 || bb < 0 || ba >= _dropPositions.Length || bb >= _dropPositions.Length) continue;
            if (constraint.Id < 0 || constraint.Id >= _dropPivotOffsets.Length) continue;

            var pivotA = _dropPositions[ba] + Vector3.Transform(Translation(constraint.FrameA), _dropRotations[ba]);
            var pivotB = _dropPositions[bb] + Vector3.Transform(Translation(constraint.FrameB), _dropRotations[bb]);
            _dropPivotOffsets[constraint.Id] = pivotB - pivotA;
        }

        _dropAccumulator = 0;
        _dropResting = false;
        InvalidateVisual();
    }

    public void ClearDrop()
    {
        _releasePositions = null;
        _releaseRotations = null;
        _dropRotations = null;
        _dropPositions = null;
        _dropVelocities = null;
        _dropPivotOffsets = null;
        _dropBodyIndexById = null;
        _dropBodyBoneIndexes = null;
        _dropBodyDepth = null;
        _dropAccumulator = 0;
        _dropTotal = 0;
        _dropResting = false;
        InvalidateVisual();
    }

    public void AdvanceDrop(float seconds)
    {
        if (_dropPositions == null || _dropResting || _dropBodyIndexById == null ||
            _dropBodyBoneIndexes == null || _dropBodyDepth == null || _dropVelocities == null ||
            _dropPivotOffsets == null || seconds <= 0 || !float.IsFinite(seconds)) return;

        _dropAccumulator += seconds;
        while (_dropAccumulator + 1e-12 >= DropSubstep && !_dropResting)
        {
            _dropAccumulator -= DropSubstep;
            _dropResting = StepConstrainedRelease(
                _bodies!, _dropBodyIndexById, _dropBodyBoneIndexes, _dropBodyDepth,
                _dropPositions, _dropRotations!, _dropVelocities, _dropPivotOffsets,
                DropSubstep, DropIterations);
        }
        InvalidateVisual();
    }

    internal static bool StepConstrainedRelease(
        HavokRagdollModel model,
        Dictionary<int, int> bodyIndexById,
        int[] bodyBoneIndexes,
        int[] bodyDepth,
        Vector3[] positions,
        Quaternion[] rotations,
        Vector3[] velocities,
        Vector3[] pivotOffsets,
        float dt,
        int iterations)
    {
        var activeBones = new HashSet<int>();
        foreach (int bone in bodyBoneIndexes)
            if (bone >= 0 && bone < positions.Length && bone < rotations.Length && bone < velocities.Length)
                activeBones.Add(bone);
        if (activeBones.Count == 0) return true;

        var start = (Vector3[])positions.Clone();
        foreach (int bone in activeBones)
        {
            velocities[bone].Z -= DropGravity * dt;
            positions[bone] += velocities[bone] * dt;
        }

        for (int iter = 0; iter < iterations; iter++)
        {
            foreach (var constraint in model.Constraints)
            {
                if (!bodyIndexById.TryGetValue(constraint.BodyA, out int ai) ||
                    !bodyIndexById.TryGetValue(constraint.BodyB, out int bi)) continue;
                int ba = bodyBoneIndexes[ai];
                int bb = bodyBoneIndexes[bi];
                if (ba < 0 || bb < 0 || ba >= positions.Length || bb >= positions.Length) continue;
                if (constraint.FrameA == null || constraint.FrameB == null) continue;
                if (constraint.Id < 0 || constraint.Id >= pivotOffsets.Length) continue;

                var rotationA = rotations[ba];
                var rotationB = rotations[bb];
                var pivotA = positions[ba] + Vector3.Transform(Translation(constraint.FrameA), rotationA);
                var pivotB = positions[bb] + Vector3.Transform(Translation(constraint.FrameB), rotationB);
                var correction = pivotA + pivotOffsets[constraint.Id] - pivotB;
                float massA = Math.Max(0.01f, model.Bodies[ai].Mass);
                float massB = Math.Max(0.01f, model.Bodies[bi].Mass);
                float total = massA + massB;
                positions[ba] -= correction * (massB / total);
                positions[bb] += correction * (massA / total);

                bool childIsA = bodyDepth[ai] > bodyDepth[bi];
                int childBone = childIsA ? ba : bb;
                if (constraint.Kind == HavokConstraintKind.Ragdoll)
                {
                    rotations[childBone] = ClampConstraintRotation(
                        constraint, childIsA ? rotations[bb] : rotations[ba], rotations[childBone], childIsA);
                }
                else if (constraint is { Kind: HavokConstraintKind.Hinge or HavokConstraintKind.LimitedHinge } &&
                         (constraint.MinAngle != 0 || constraint.MaxAngle != 0))
                {
                    int parentBone = childIsA ? bb : ba;
                    var axis = Vector3.Normalize(WorldDir(constraint.FrameA, constraint.LimitAxis, rotationA));
                    var toward = positions[childBone] - positions[parentBone];
                    var perpendicular = toward - axis * Vector3.Dot(toward, axis);
                    if (perpendicular.LengthSquared() > 1e-8f)
                    {
                        var reference = Vector3.Normalize(perpendicular);
                        var childFrame = childIsA ? constraint.FrameA : constraint.FrameB;
                        var childLocal = Vector3.Normalize(WorldDir(childFrame, 0, rotations[childBone]));
                        var childPerpendicular = childLocal - axis * Vector3.Dot(childLocal, axis);
                        if (childPerpendicular.LengthSquared() > 1e-8f)
                        {
                            float current = SignedAngleAround(axis, reference, Vector3.Normalize(childPerpendicular));
                            float clamped = Math.Clamp(current, constraint.MinAngle, constraint.MaxAngle);
                            float delta = clamped - current;
                            if (Math.Abs(delta) > 1e-5f)
                                rotations[childBone] = Quaternion.Normalize(
                                    Quaternion.CreateFromAxisAngle(axis, delta) * rotations[childBone]);
                        }
                    }
                }
            }

            foreach (var body in model.Bodies)
            {
                if (!bodyIndexById.TryGetValue(body.Id, out int bi)) continue;
                int bone = bodyBoneIndexes[bi];
                if (bone < 0 || bone >= positions.Length) continue;
                var shape = model.Shapes.FirstOrDefault(s => s.Id == body.ShapeId);
                if (shape == null || shape.Vertices.Count == 0) continue;

                float lowest = float.MaxValue;
                foreach (var vertex in shape.Vertices)
                    lowest = Math.Min(lowest,
                        (positions[bone] + Vector3.Transform(vertex, rotations[bone])).Z);
                if (lowest >= 0) continue;

                positions[bone].Z -= lowest;
                velocities[bone].Z = Math.Max(0, velocities[bone].Z);
                velocities[bone].X *= 0.6f;
                velocities[bone].Y *= 0.6f;
            }
        }

        bool resting = true;
        foreach (int bone in activeBones)
        {
            velocities[bone] *= MathF.Max(0, 1f - DropDamping * dt);
            if ((positions[bone] - start[bone]).LengthSquared() > DropRestEpsilon * DropRestEpsilon)
                resting = false;
        }
        return resting;
    }

    private static Quaternion ClampConstraintRotation(
        HavokConstraint constraint, Quaternion rotationParent, Quaternion rotationChild, bool fileAIsChild)
    {
        if (constraint.FrameA == null || constraint.Kind != HavokConstraintKind.Ragdoll) return rotationChild;

        float twistMin = 0, twistMax = 0, coneMax = 0;
        bool hasTwist = false, hasCone = false;
        if (constraint.TwistMinAngle is float min && constraint.TwistMaxAngle is float max)
        {
            twistMin = min;
            twistMax = max;
            hasTwist = true;
        }
        if (constraint.ConeMaxAngle is float cone)
        {
            coneMax = cone;
            hasCone = true;
        }
        if (!hasTwist && !hasCone) return rotationChild;

        Quaternion relative = fileAIsChild
            ? Quaternion.Normalize(Quaternion.Inverse(rotationChild) * rotationParent)
            : Quaternion.Normalize(Quaternion.Inverse(rotationParent) * rotationChild);
        var twistAxis = Vector3.Normalize(Column(constraint.FrameA, constraint.TwistAxis));
        var coneAxis = Vector3.Normalize(Column(constraint.FrameA, constraint.ConeTwistAxis));
        for (int pass = 0; pass < 4; pass++)
        {
            if (hasTwist) relative = ClampTwist(relative, twistAxis, twistMin, twistMax);
            if (hasCone) relative = ClampCone(relative, coneAxis, coneMax);
        }

        if (fileAIsChild)
        {
            var original = Quaternion.Normalize(Quaternion.Inverse(rotationChild) * rotationParent);
            var adjustment = Quaternion.Normalize(original * Quaternion.Inverse(relative));
            return Quaternion.Normalize(rotationChild * adjustment);
        }
        return Quaternion.Normalize(rotationParent * relative);
    }

    internal static float SignedAngleAround(Vector3 axis, Vector3 from, Vector3 to)
    {
        axis = Vector3.Normalize(axis);
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);
        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(from, to), -1f, 1f));
        if (Vector3.Dot(Vector3.Cross(from, to), axis) < 0) angle = -angle;
        return angle;
    }

    internal static float TwistAngle(Quaternion relative, Vector3 axis)
    {
        var v = new Vector3(relative.X, relative.Y, relative.Z);
        return 2f * MathF.Atan2(Vector3.Dot(v, Vector3.Normalize(axis)), relative.W);
    }

    private static (Quaternion Twist, Quaternion Swing) Decompose(Quaternion relative, Vector3 axis)
    {
        var axisUnit = Vector3.Normalize(axis);
        var v = new Vector3(relative.X, relative.Y, relative.Z);
        var twist = new Quaternion(Vector3.Dot(v, axisUnit) * axisUnit, relative.W);
        if (twist.LengthSquared() < 1e-10f)
            return (Quaternion.Identity, Quaternion.Normalize(relative));
        twist = Quaternion.Normalize(twist);
        return (twist, Quaternion.Normalize(relative * Quaternion.Inverse(twist)));
    }

    internal static float SwingAngle(Quaternion relative, Vector3 axis)
    {
        var (_, swing) = Decompose(relative, axis);
        return 2f * MathF.Atan2(new Vector3(swing.X, swing.Y, swing.Z).Length(), swing.W);
    }

    internal static Quaternion ClampTwist(Quaternion relative, Vector3 axis, float min, float max)
    {
        var axisUnit = Vector3.Normalize(axis);
        var (_, swing) = Decompose(relative, axis);
        float angle = TwistAngle(relative, axis);
        float clamped = Math.Clamp(angle, min, max);
        if (Math.Abs(clamped - angle) < 1e-5f) return relative;
        return Quaternion.Normalize(swing * Quaternion.CreateFromAxisAngle(axisUnit, clamped));
    }

    internal static Quaternion ClampCone(Quaternion relative, Vector3 axis, float coneMax)
    {
        var (twist, swing) = Decompose(relative, axis);
        float swingAngle = 2f * MathF.Atan2(new Vector3(swing.X, swing.Y, swing.Z).Length(), swing.W);
        if (swingAngle <= coneMax + 1e-5f) return relative;
        float t = coneMax / MathF.Max(swingAngle, 1e-5f);
        return Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, swing, t) * twist);
    }

    private float GlobalLowestVertexZ()
    {
        float lowest = float.MaxValue;
        foreach (var body in _bodies!.Bodies)
        {
            var shape = _bodies.Shapes.FirstOrDefault(s => s.Id == body.ShapeId);
            if (shape == null) continue;
            var (position, rotation) = BodyFrame(_bodies, body);
            foreach (var v in shape.Vertices)
            {
                var world = position + Vector3.Transform(v, rotation);
                lowest = Math.Min(lowest, world.Z);
            }
        }
        return lowest == float.MaxValue ? 0 : lowest;
    }

    private HavokBoneFrame[]? ReleaseSourceFrames()
    {
        if (_physicsPose is { Length: > 0 }) return (HavokBoneFrame[])_physicsPose.Clone();
        if (_bodies?.Skeleton is not { ReferencePose.Count: > 0 } skeleton) return null;
        return skeleton.WorldReferenceFrames().ToArray();
    }

    private void UpdatePhysicsPose()
    {
        if (_bodies == null || _pose == null || _bodies.Mappings.Count == 0)
        {
            _physicsPose = null;
            return;
        }

        var source = new HavokBoneFrame[_pose.Bones.Count];
        for (int i = 0; i < _pose.Bones.Count; i++)
            source[i] = new HavokBoneFrame(_pose.Bones[i].Position, _pose.Bones[i].Rotation);

        _physicsPose = _bodies.MapPose(source);
    }

    private (Vector3 Position, Quaternion Rotation) BodyFrame(HavokRagdollModel model, HavokRigidBody body)
    {
        foreach (var binding in model.BoneBindings)
        {
            if (binding.BodyId != body.Id) continue;
            if (_dropPositions != null && binding.BoneIndex >= 0 && binding.BoneIndex < _dropPositions.Length)
                return (_dropPositions[binding.BoneIndex], _dropRotations![binding.BoneIndex]);
            if (_physicsPose != null && binding.BoneIndex >= 0 && binding.BoneIndex < _physicsPose.Length)
                return (_physicsPose[binding.BoneIndex].Position, _physicsPose[binding.BoneIndex].Rotation);
        }
        return (body.Position, body.Rotation);
    }

    private void FitBodies()
    {
        var bounds = BodyBounds(current: false);
        _bodyCentre = bounds.Centre;
        _bodyFit = bounds.Fit;
    }

    private (Vector3 Centre, float Fit) BodyBounds(bool current)
    {
        if (_bodies == null || _bodies.Bodies.Count == 0) return (Vector3.Zero, 1);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        bool any = false;

        foreach (var body in _bodies.Bodies)
        {
            var (position, rotation) = current
                ? BodyFrame(_bodies, body)
                : (body.Position, body.Rotation);
            var shape = _bodies.Shapes.FirstOrDefault(s => s.Id == body.ShapeId);
            if (shape != null)
                foreach (var vertex in shape.Vertices)
                {
                    var world = position + Vector3.Transform(vertex, rotation);
                    min = Vector3.Min(min, world);
                    max = Vector3.Max(max, world);
                    any = true;
                }

            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
            any = true;
        }

        if (!any) return (Vector3.Zero, 1);

        var centre = (min + max) * 0.5f;
        var size = max - min;
        float fit = Math.Max(0.001f, Math.Max(size.X, Math.Max(size.Y, size.Z)) * 0.5f);
        return (centre, fit);
    }

    public void Frame()
    {
        _zoom = 1;
        _pan = default;
        if (EngineeringFrames)
        {
            var bounds = BodyBounds(current: true);
            _centre = bounds.Centre;
            _fit = bounds.Fit;
        }
        else if (_pose is { Bones.Count: > 0 })
        {
            _centre = _pose.Centre;
            _fit = _pose.Radius;
        }
        InvalidateVisual();
    }

    private Point Project(Vector3 world)
    {
        var p = world - _centre;

        double cy = Math.Cos(_yaw), sy = Math.Sin(_yaw);
        double x = p.X * cy - p.Y * sy;
        double depth = p.X * sy + p.Y * cy;

        double cp = Math.Cos(_pitch), sp = Math.Sin(_pitch);
        double up = p.Z * cp - depth * sp;

        double scale = Math.Min(Bounds.Width, Bounds.Height) / (_fit * 2.6) * _zoom;
        return new Point(Bounds.Width / 2 + x * scale + _pan.X,
                         Bounds.Height / 2 - up * scale + _pan.Y);
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Ux.CardBrush, new Rect(Bounds.Size));

        if (EngineeringFrames && _bodies is { Bodies.Count: > 0 })
        {
            DrawGround(ctx);
            DrawFrames(ctx, _bodies);
            return;
        }

        var pose = _pose;
        bool bodies = ShowBodies && _bodies != null && _bodies.Bodies.Count > 0;
        bool physicsSkeleton = ShowPhysicsSkeleton &&
                               _bodies is { Skeleton: { ParentIndices.Count: > 0, ReferencePose.Count: > 0 } };
        if ((pose == null || pose.Bones.Count == 0) && !bodies && !physicsSkeleton)
        {
            var empty = new FormattedText("No pose to draw.", CultureInfo.InvariantCulture,
                                          FlowDirection.LeftToRight, Typeface.Default, 12, Ux.MutedBrush);
            ctx.DrawText(empty, new Point(14, 14));
            return;
        }

        if (pose == null || pose.Bones.Count == 0)
        {
            _centre = _bodyCentre;
            _fit = _bodyFit;
        }

        DrawGround(ctx);

        if (ShowReference && _reference != null)
        {
            var ghost = new Pen(new SolidColorBrush(Ux.TextDisabled, 0.5), 1);
            foreach (var (from, to) in _reference.Links)
                ctx.DrawLine(ghost, Project(_reference.Bones[from].Position),
                                    Project(_reference.Bones[to].Position));
        }

        if (_mesh is { Length: > 0 })
        {
            var skin = new StreamGeometry();
            using (var draw = skin.Open())
                foreach (var (a, b) in _mesh)
                {
                    draw.BeginFigure(Project(a), false);
                    draw.LineTo(Project(b));
                    draw.EndFigure(false);
                }
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Ux.TextMeta, 0.28), 0.7), skin);
        }

        if (bodies) DrawBodies(ctx, _bodies!);
        if (ShowConstraints && _bodies != null && _bodies.Constraints.Count > 0)
            DrawConstraints(ctx, _bodies);

        var bone = new Pen(Ux.MetaBrush, 1.6);
        if (pose != null)
        {
            foreach (var (from, to) in pose.Links)
                ctx.DrawLine(bone, Project(pose.Bones[from].Position), Project(pose.Bones[to].Position));

            foreach (var b in pose.Bones)
            {
                var at = Project(b.Position);
                bool root = b.Parent < 0;
                bool hovered = b.Name == HoveredBone && HoveredBone.Length > 0;
                double radius = hovered ? 4.5 : root ? 3.5 : 2;
                var brush = hovered ? Ux.AccentBrush : root ? Ux.CodeBrush : Ux.TitleBrush;
                ctx.DrawEllipse(brush, null, at, radius, radius);
            }
        }

        if (ShowBindings && _bodies is { BoneBindings.Count: > 0, Skeleton: { BoneNames.Count: > 0 } })
            DrawBindings(ctx, _bodies);
        if (physicsSkeleton) DrawPhysicsSkeleton(ctx, _bodies!);
        if (ShowFrames && _bodies is { Bodies.Count: > 0 }) DrawFrames(ctx, _bodies);

        if (ShowConstraints && _bodies is { Constraints.Count: > 0 })
        {
            if (_pinnedConstraint >= 0) DrawConstraintLabel(ctx, _bodies, _pinnedConstraint);
            if (_hoveredConstraint >= 0 && _hoveredConstraint != _pinnedConstraint)
                DrawConstraintLabel(ctx, _bodies, _hoveredConstraint);
        }

        if (HoveredBone.Length > 0)
        {
            var label = new FormattedText(HoveredBone, CultureInfo.InvariantCulture,
                                          FlowDirection.LeftToRight, Typeface.Default, 12, Ux.TitleBrush);
            ctx.DrawText(label, new Point(14, Bounds.Height - 26));
        }
    }

    private void DrawBodies(DrawingContext ctx, HavokRagdollModel model)
    {
        var hull = new Pen(new SolidColorBrush(Ux.Accent, 0.9), 1.2);
        var centre = new Pen(new SolidColorBrush(Ux.Accent, 0.55), 1);

        foreach (var body in model.Bodies)
        {
            var (position, rotation) = BodyFrame(model, body);
            var shape = model.Shapes.FirstOrDefault(s => s.Id == body.ShapeId);
            if (shape == null) continue;

            foreach (var ring in shape.FaceRings)
            {
                if (ring.Length < 2) continue;
                for (int i = 0; i < ring.Length; i++)
                {
                    int from = ring[i];
                    int to = ring[(i + 1) % ring.Length];
                    if (from < 0 || from >= shape.Vertices.Count ||
                        to < 0 || to >= shape.Vertices.Count) continue;
                    var a = position + Vector3.Transform(shape.Vertices[from], rotation);
                    var b = position + Vector3.Transform(shape.Vertices[to], rotation);
                    ctx.DrawLine(hull, Project(a), Project(b));
                }
            }
            ctx.DrawEllipse(null, centre, Project(position), 2.2, 2.2);
        }
    }

    private void DrawPhysicsSkeleton(DrawingContext ctx, HavokRagdollModel model)
    {
        var chain = new Pen(new SolidColorBrush(Ux.Good, 0.95), 2);
        var marker = new SolidColorBrush(Ux.Good, 0.9);

        Vector3[] positions = _dropPositions != null
            ? _dropPositions
            : _physicsPose != null
                ? _physicsPose.Take(model.Skeleton!.BoneNames.Count).Select(p => p.Position).ToArray()
                : model.Skeleton!.WorldReferencePositions().ToArray();
        var parents = model.Skeleton.ParentIndices;

        for (int i = 0; i < positions.Length && i < parents.Count; i++)
        {
            int parent = parents[i];
            if (parent >= 0 && parent < positions.Length)
                ctx.DrawLine(chain, Project(positions[parent]), Project(positions[i]));
        }
        for (int i = 0; i < positions.Length; i++)
        {
            bool root = i >= parents.Count || parents[i] < 0;
            double radius = root ? 3.2 : 2.2;
            ctx.DrawEllipse(marker, null, Project(positions[i]), radius, radius);
        }
    }

    private void DrawFrames(DrawingContext ctx, HavokRagdollModel model)
    {
        var xAxis = new Pen(new SolidColorBrush(Ux.Bad, 0.95), 1.6);
        var yAxis = new Pen(new SolidColorBrush(Ux.Good, 0.95), 1.6);
        var zAxis = new Pen(new SolidColorBrush(Ux.Accent, 0.95), 1.6);
        var comPen = new Pen(new SolidColorBrush(Ux.TextTitle, 0.9), 1.2);
        float arm = AxisStubLength;

        foreach (var body in model.Bodies)
        {
            var (position, rotation) = BodyFrame(model, body);
            var at = Project(position);
            ctx.DrawLine(xAxis, at, Project(position + Vector3.Transform(Vector3.UnitX, rotation) * arm));
            ctx.DrawLine(yAxis, at, Project(position + Vector3.Transform(Vector3.UnitY, rotation) * arm));
            ctx.DrawLine(zAxis, at, Project(position + Vector3.Transform(Vector3.UnitZ, rotation) * arm));

            if (body.CenterOfMass != Vector3.Zero)
            {
                var com = body.PosedCenterOfMass(position, rotation);
                ctx.DrawEllipse(null, comPen, Project(com), 2.6, 2.6);
            }

            if (body.Id == _hoveredBody || _pinnedBodies.Contains(body.Id))
            {
                var label = new FormattedText(BodyFrameLabel(body, AxisStubLength),
                                              CultureInfo.InvariantCulture,
                                              FlowDirection.LeftToRight, Typeface.Default, 11,
                                              Ux.TitleBrush);
                ctx.DrawText(label, new Point(at.X + 10, at.Y - 16));
            }
        }
    }

    public static string BodyFrameLabel(HavokRigidBody body, float stubLength)
    {
        string stub = stubLength.ToString("0.00", CultureInfo.InvariantCulture);
        if (body.CenterOfMass == Vector3.Zero)
            return "no measured COM  stub " + stub;

        var c = body.CenterOfMass;
        string com = c.X.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                     c.Y.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                     c.Z.ToString("0.00", CultureInfo.InvariantCulture);
        return "COM (" + com + ")  stub " + stub;
    }

    private void DrawBindings(DrawingContext ctx, HavokRagdollModel model)
    {
        var labelPen = new Pen(new SolidColorBrush(Ux.Accent, 0.75), 1);
        var hoveredBody = model.Bodies.FirstOrDefault(b => b.Id == _hoveredBody);

        foreach (var binding in model.BoneBindings)
        {
            var body = model.Bodies.FirstOrDefault(b => b.Id == binding.BodyId);
            if (body == null || model.Skeleton == null ||
                binding.BoneIndex < 0 || binding.BoneIndex >= model.Skeleton.BoneNames.Count)
                continue;

            string label = model.Skeleton.BoneNames[binding.BoneIndex];
            if (label.Length == 0) continue;

            bool hovered = binding.BodyId == _hoveredBody;
            var (position, rotation) = BodyFrame(model, body);
            var at = Project(position);
            var text = new FormattedText(label, CultureInfo.InvariantCulture,
                                         FlowDirection.LeftToRight, Typeface.Default,
                                         hovered ? 12 : 11,
                                         hovered ? Ux.AccentBrush : Ux.MetaBrush);
            ctx.DrawText(text, new Point(at.X + 7, at.Y - 15));
        }

        if (hoveredBody == null || model.Skeleton == null) return;
        var shape = model.Shapes.FirstOrDefault(s => s.Id == hoveredBody.ShapeId);
        if (shape == null) return;

        foreach (var ring in shape.FaceRings)
        {
            if (ring.Length < 2) continue;
            for (int i = 0; i < ring.Length; i++)
            {
                int from = ring[i];
                int to = ring[(i + 1) % ring.Length];
                if (from < 0 || from >= shape.Vertices.Count ||
                    to < 0 || to >= shape.Vertices.Count) continue;
                var (hoveredPosition, hoveredRotation) = BodyFrame(model, hoveredBody);
                var a = hoveredPosition + Vector3.Transform(shape.Vertices[from], hoveredRotation);
                var b = hoveredPosition + Vector3.Transform(shape.Vertices[to], hoveredRotation);
                ctx.DrawLine(labelPen, Project(a), Project(b));
            }
        }
    }

    private const float ConstraintArm = 8f;

    public static string ConstraintLabel(HavokConstraint constraint, IReadOnlyList<HavokRigidBody> bodies)
    {
        string bodyA = bodies.FirstOrDefault(b => b.Id == constraint.BodyA)?.Name ?? "";
        string bodyB = bodies.FirstOrDefault(b => b.Id == constraint.BodyB)?.Name ?? "";
        if (bodyA.Length == 0) bodyA = "body " + constraint.BodyA;
        if (bodyB.Length == 0) bodyB = "body " + constraint.BodyB;
        string kind = KindName(constraint.Kind);
        var limits = new List<string>();
        switch (constraint.Kind)
        {
            case HavokConstraintKind.Ragdoll:
                if (constraint.TwistMinAngle is float twistMin && constraint.TwistMaxAngle is float twistMax)
                    limits.Add("twist " + Angle(twistMin) + ".." + Angle(twistMax) + " deg");
                if (constraint.ConeMaxAngle is float coneMax)
                    limits.Add("cone " + Angle(coneMax) + " deg");
                break;
            case HavokConstraintKind.LimitedHinge:
                if (constraint.MinAngle != 0 || constraint.MaxAngle != 0)
                    limits.Add("hinge " + Angle(constraint.MinAngle) + ".." + Angle(constraint.MaxAngle) + " deg");
                break;
        }
        string measured = limits.Count > 0 ? string.Join(", ", limits) : "no measured limits";
        return kind + ": " + bodyA + " -> " + bodyB + " - " + measured;
    }

    private static string Angle(float radians) =>
        (radians * 180f / MathF.PI).ToString("0.0", CultureInfo.InvariantCulture);

    private static string KindName(HavokConstraintKind kind) => kind switch
    {
        HavokConstraintKind.Ragdoll => "ragdoll",
        HavokConstraintKind.LimitedHinge => "limited hinge",
        HavokConstraintKind.Hinge => "hinge",
        HavokConstraintKind.BallAndSocket => "ball and socket",
        HavokConstraintKind.Fixed => "fixed",
        _ => "constraint",
    };

    private void DrawConstraints(DrawingContext ctx, HavokRagdollModel model)
    {
        var link = new Pen(new SolidColorBrush(Ux.Warn, 0.55), 1);
        var pivot = new Pen(new SolidColorBrush(Ux.Warn, 0.95), 1.2);
        var arm = new Pen(new SolidColorBrush(Ux.Warn, 0.85), 1.1);
        var linkHot = new Pen(new SolidColorBrush(Ux.TextTitle, 0.95), 1.5);
        var pivotHot = new Pen(new SolidColorBrush(Ux.TextTitle, 0.95), 1.7);
        var armHot = new Pen(new SolidColorBrush(Ux.TextTitle, 0.9), 1.4);

        foreach (var constraint in model.Constraints)
        {
            bool hovered = constraint.Id == _hoveredConstraint || constraint.Id == _pinnedConstraint;
            var linkPen = hovered ? linkHot : link;
            var pivotPen = hovered ? pivotHot : pivot;
            var armPen = hovered ? armHot : arm;

            var bodyA = model.Bodies.FirstOrDefault(b => b.Id == constraint.BodyA);
            var bodyB = model.Bodies.FirstOrDefault(b => b.Id == constraint.BodyB);
            if (bodyA == null || bodyB == null) continue;

            var (frameA, rotationA) = BodyFrame(model, bodyA);
            var (frameB, rotationB) = BodyFrame(model, bodyB);
            ctx.DrawLine(linkPen, Project(frameA), Project(frameB));

            if (constraint.FrameA == null) continue;
            var pivotA = frameA + Vector3.Transform(Translation(constraint.FrameA), rotationA);
            ctx.DrawEllipse(null, pivotPen, Project(pivotA), 2.2, 2.2);

            Vector3 pivotB = pivotA;
            if (constraint.FrameB != null)
            {
                pivotB = frameB + Vector3.Transform(Translation(constraint.FrameB), rotationB);
                ctx.DrawEllipse(null, pivotPen, Project(pivotB), 2.2, 2.2);
            }

            switch (constraint.Kind)
            {
                case HavokConstraintKind.Ragdoll:
                    DrawTwist(ctx, armPen, constraint, rotationA, pivotA);
                    DrawCone(ctx, armPen, constraint, rotationA, pivotA);
                    break;
                case HavokConstraintKind.LimitedHinge:
                    DrawHinge(ctx, armPen, constraint, rotationA, pivotB, pivotA);
                    break;
            }
        }
    }

    private void DrawConstraintLabel(DrawingContext ctx, HavokRagdollModel model, int constraintId)
    {
        var constraint = model.Constraints.FirstOrDefault(c => c.Id == constraintId);
        var bodyA = constraint != null ? model.Bodies.FirstOrDefault(b => b.Id == constraint.BodyA) : null;
        var bodyB = constraint != null ? model.Bodies.FirstOrDefault(b => b.Id == constraint.BodyB) : null;
        if (constraint == null || bodyA == null || bodyB == null) return;

        var a = Project(BodyFrame(model, bodyA).Position);
        var b = Project(BodyFrame(model, bodyB).Position);
        var text = new FormattedText(ConstraintLabel(constraint, model.Bodies), CultureInfo.InvariantCulture,
                                     FlowDirection.LeftToRight, Typeface.Default, 11, Ux.TitleBrush);
        ctx.DrawText(text, new Point((a.X + b.X) / 2 + 10, (a.Y + b.Y) / 2 - 22));
    }

    private void DrawTwist(DrawingContext ctx, Pen pen, HavokConstraint constraint,
                           Quaternion rotationA, Vector3 pivotA)
    {
        if (constraint.FrameA == null) return;
        var axis = Vector3.Normalize(WorldDir(constraint.FrameA, constraint.TwistAxis, rotationA));
        var reference = Vector3.Normalize(WorldDir(constraint.FrameA, constraint.TwistRefAxis, rotationA));
        ctx.DrawLine(pen, Project(pivotA), Project(pivotA + axis * ConstraintArm));
        if (constraint.TwistMinAngle is float min && constraint.TwistMaxAngle is float max)
        {
            ctx.DrawLine(pen, Project(pivotA),
                              Project(pivotA + RotateAround(reference, axis, min) * ConstraintArm));
            ctx.DrawLine(pen, Project(pivotA),
                              Project(pivotA + RotateAround(reference, axis, max) * ConstraintArm));
        }
    }

    private void DrawCone(DrawingContext ctx, Pen pen, HavokConstraint constraint,
                          Quaternion rotationA, Vector3 pivotA)
    {
        if (constraint.FrameA == null || constraint.ConeMaxAngle is not float coneMax) return;
        var axis = Vector3.Normalize(WorldDir(constraint.FrameA, constraint.ConeTwistAxis, rotationA));
        ctx.DrawLine(pen, Project(pivotA), Project(pivotA + axis * ConstraintArm));
        var centre = pivotA + axis * (MathF.Cos(coneMax) * ConstraintArm);
        var radius = MathF.Sin(coneMax) * ConstraintArm;
        var right = Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitY));
        if (right.LengthSquared() < 1e-6f) right = Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitX));
        var up = Vector3.Normalize(Vector3.Cross(axis, right));
        const int segments = 24;
        var previous = Project(centre + right * radius);
        for (int i = 1; i <= segments; i++)
        {
            float angle = MathF.PI * 2 * i / segments;
            var point = centre + (right * MathF.Cos(angle) + up * MathF.Sin(angle)) * radius;
            var at = Project(point);
            ctx.DrawLine(pen, previous, at);
            previous = at;
        }
    }

    private void DrawHinge(DrawingContext ctx, Pen pen, HavokConstraint constraint,
                           Quaternion rotationA, Vector3 pivotB, Vector3 pivotA)
    {
        if (constraint.FrameA == null) return;
        var axis = Vector3.Normalize(WorldDir(constraint.FrameA, constraint.LimitAxis, rotationA));
        ctx.DrawLine(pen, Project(pivotA), Project(pivotA + axis * ConstraintArm));
        if (constraint.MinAngle == 0 && constraint.MaxAngle == 0) return;
        var toward = pivotB - pivotA;
        var perpendicular = toward - axis * Vector3.Dot(toward, axis);
        var reference = perpendicular.LengthSquared() < 1e-8f
            ? Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitY))
            : Vector3.Normalize(perpendicular);
        ctx.DrawLine(pen, Project(pivotA),
                          Project(pivotA + RotateAround(reference, axis, constraint.MinAngle) * ConstraintArm));
        ctx.DrawLine(pen, Project(pivotA),
                          Project(pivotA + RotateAround(reference, axis, constraint.MaxAngle) * ConstraintArm));
    }

    private static Vector3 Translation(float[] frame) => new(frame[12], frame[13], frame[14]);

    internal static Vector3 Column(float[] frame, int axis) =>
        new(frame[4 * axis], frame[4 * axis + 1], frame[4 * axis + 2]);

    internal static Vector3 WorldDir(float[] frame, int axis, Quaternion rotation) =>
        Vector3.Transform(Column(frame, axis), rotation);

    private static Vector3 RotateAround(Vector3 vector, Vector3 axis, float angle) =>
        Vector3.Transform(vector, Quaternion.CreateFromAxisAngle(axis, angle));

    private void DrawGround(DrawingContext ctx)
    {
        var pen = new Pen(new SolidColorBrush(Ux.Border, 0.7), 1);
        float reach = (float)(_fit * 1.5);
        for (int i = -2; i <= 2; i++)
        {
            float at = reach * i / 2f;
            ctx.DrawLine(pen, Project(new Vector3(-reach, at, 0) + _centre with { Z = 0 }),
                              Project(new Vector3(reach, at, 0) + _centre with { Z = 0 }));
            ctx.DrawLine(pen, Project(new Vector3(at, -reach, 0) + _centre with { Z = 0 }),
                              Project(new Vector3(at, reach, 0) + _centre with { Z = 0 }));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _lastPointer = e.GetPosition(this);
        _pressAt = _lastPointer;
        var props = e.GetCurrentPoint(this).Properties;
        _panning = props.IsMiddleButtonPressed || props.IsRightButtonPressed;
        _orbiting = props.IsLeftButtonPressed;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var at = e.GetPosition(this);
        var delta = at - _lastPointer;
        _lastPointer = at;

        if (_orbiting)
        {
            _yaw += delta.X * 0.01;
            _pitch = Math.Clamp(_pitch + delta.Y * 0.01, -1.5, 1.5);
            InvalidateVisual();
            return;
        }
        if (_panning)
        {
            _pan = new Point(_pan.X + delta.X, _pan.Y + delta.Y);
            InvalidateVisual();
            return;
        }

        NameBoneUnder(at);
        NameBodyUnder(at);
        NameConstraintUnder(at);
    }

    private void NameBodyUnder(Point at)
    {
        int found = PickBodyUnder(at);
        if (!ShowBindings && !ShowFrames && !EngineeringFrames && _pinnedBodies.Count > 0)
            _pinnedBodies.Clear();
        if (found == _hoveredBody) return;
        _hoveredBody = found;
        InvalidateVisual();
    }

    private int PickBodyUnder(Point at)
    {
        if ((!ShowBindings && !ShowFrames && !EngineeringFrames) ||
            _bodies == null || _bodies.Bodies.Count == 0) return -1;

        int found = -1;
        double nearest = 12 * 12;
        foreach (var body in _bodies.Bodies)
        {
            var p = Project(BodyFrame(_bodies, body).Position);
            double d = (p.X - at.X) * (p.X - at.X) + (p.Y - at.Y) * (p.Y - at.Y);
            if (d >= nearest) continue;
            nearest = d;
            found = body.Id;
        }
        return found;
    }

    private void NameConstraintUnder(Point at)
    {
        int found = PickConstraintUnder(at);
        if (found == _hoveredConstraint) return;
        _hoveredConstraint = found;
        NotifyConstraintSelection();
        InvalidateVisual();
    }

    private int PickConstraintUnder(Point at)
    {
        int found = -1;
        if (ShowConstraints && _bodies != null && _bodies.Constraints.Count > 0)
        {
            double nearest = 12 * 12;
            foreach (var constraint in _bodies.Constraints)
            {
                var bodyA = _bodies.Bodies.FirstOrDefault(b => b.Id == constraint.BodyA);
                var bodyB = _bodies.Bodies.FirstOrDefault(b => b.Id == constraint.BodyB);
                if (bodyA == null || bodyB == null) continue;

                var a = Project(BodyFrame(_bodies, bodyA).Position);
                var b = Project(BodyFrame(_bodies, bodyB).Position);
                double d = DistanceSquaredToSegment(at, a, b);
                if (d >= nearest) continue;
                nearest = d;
                found = constraint.Id;
            }
        }
        return found;
    }

    private static double DistanceSquaredToSegment(Point p, Point a, Point b)
    {
        double alongX = b.X - a.X;
        double alongY = b.Y - a.Y;
        double lengthSquared = alongX * alongX + alongY * alongY;
        if (lengthSquared <= 1e-9)
        {
            double qx = p.X - a.X;
            double qy = p.Y - a.Y;
            return qx * qx + qy * qy;
        }

        double t = Math.Clamp(((p.X - a.X) * alongX + (p.Y - a.Y) * alongY) / lengthSquared, 0, 1);
        double nearX = a.X + t * alongX - p.X;
        double nearY = a.Y + t * alongY - p.Y;
        return nearX * nearX + nearY * nearY;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        bool wasOrbiting = _orbiting;
        _orbiting = _panning = false;
        e.Pointer.Capture(null);

        var released = e.GetPosition(this);
        double dx = released.X - _pressAt.X;
        double dy = released.Y - _pressAt.Y;
        if (wasOrbiting && dx * dx + dy * dy < 5 * 5)
        {
            int next = PinnedAfterClick(_pinnedConstraint, PickConstraintUnder(released));
            if (next != _pinnedConstraint)
            {
                _pinnedConstraint = next;
                NotifyConstraintSelection();
                InvalidateVisual();
            }

            int bodyPicked = PickBodyUnder(released);
            if (bodyPicked >= 0)
            {
                if (!_pinnedBodies.Remove(bodyPicked)) _pinnedBodies.Add(bodyPicked);
                InvalidateVisual();
            }
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.12 : 1 / 1.12), 0.05, 40);
        InvalidateVisual();
        e.Handled = true;
    }

    private void NameBoneUnder(Point at)
    {
        string found = "";
        double nearest = 12 * 12;
        if (_pose != null)
            foreach (var b in _pose.Bones)
            {
                var p = Project(b.Position);
                double d = (p.X - at.X) * (p.X - at.X) + (p.Y - at.Y) * (p.Y - at.Y);
                if (d >= nearest) continue;
                nearest = d;
                found = b.Name;
            }

        if (found == HoveredBone) return;
        HoveredBone = found;
        BoneHovered?.Invoke(found);
        InvalidateVisual();
    }
}
