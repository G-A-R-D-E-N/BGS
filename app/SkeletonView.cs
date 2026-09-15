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

public partial class SkeletonView : Control
{
    private AnimationPose.Pose? _pose;
    private AnimationPose.Pose? _reference;

    private double _yaw = Math.PI / 2;
    private double _pitch;
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
    public Action<string?>? FrameDistanceChanged;

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
    private (int First, int Second)? _frameDistanceBodies;

    public int HoveredConstraintId => _hoveredConstraint;
    public int HoveredBodyId => _hoveredBody;
    public int PinnedConstraintId => _pinnedConstraint;
    public IReadOnlyCollection<int> PinnedBodyIds => _pinnedBodies;
    public (int First, int Second)? FrameDistanceBodies => _frameDistanceBodies;

    public string FrameDistanceText
    {
        get
        {
            if (_bodies == null || _frameDistanceBodies is not { } selected) return "";
            var first = _bodies.Bodies.FirstOrDefault(body => body.Id == selected.First);
            var second = _bodies.Bodies.FirstOrDefault(body => body.Id == selected.Second);
            if (first == null || second == null) return "";

            float distance = Vector3.Distance(BodyFrame(_bodies, first).Position,
                                              BodyFrame(_bodies, second).Position);
            return $"distance {BodyName(first)} to {BodyName(second)}  {distance.ToString("0.00", CultureInfo.InvariantCulture)} file units";
        }
    }

    public void HoverBodyForTest(int bodyId)
    {
        _hoveredBody = bodyId;
        InvalidateVisual();
    }

    public void ToggleBodyPinForTest(int bodyId)
    {
        ToggleBodyPin(bodyId);
    }

    public void ClearFrameDistance()
    {
        if (_frameDistanceBodies == null) return;
        _frameDistanceBodies = null;
        NotifyFrameDistance();
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
        NotifyFrameDistance();
        InvalidateVisual();
    }

    public void Update(AnimationPose.Pose? pose)
    {
        _pose = pose;
        UpdatePhysicsPose();
        NotifyFrameDistance();
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
        _frameDistanceBodies = null;
        _physicsPose = null;
        ClearDrop();
        _yaw = Math.PI / 2;
        _pitch = 0;
        _zoom = 1;
        _pan = default;
        NotifyFrameDistance();
        InvalidateVisual();
    }

    public void SetBodies(HavokRagdollModel? model)
    {
        _bodies = model;
        _pinnedBodies.Clear();
        _frameDistanceBodies = null;
        UpdatePhysicsPose();
        FitBodies();
        NotifyFrameDistance();
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
        NotifyFrameDistance();
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
        NotifyFrameDistance();
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
        NotifyFrameDistance();
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
        if (_mesh is { Length: > 0 })
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var (a, b) in _mesh)
            {
                min = Vector3.Min(min, Vector3.Min(a, b));
                max = Vector3.Max(max, Vector3.Max(a, b));
            }
            _centre = (min + max) * 0.5f;
            var size = max - min;
            _fit = Math.Max(0.001f, Math.Max(size.X, Math.Max(size.Y, size.Z)) * 0.5f);
        }
        InvalidateVisual();
    }

}
