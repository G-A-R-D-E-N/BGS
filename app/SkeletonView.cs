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
    public SkeletonView() => ClipToBounds = true;

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
        _simulationFrames = null;
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
        _simulationFrames = null;
        _bodies = model;
        _pinnedBodies.Clear();
        _frameDistanceBodies = null;
        UpdatePhysicsPose();
        FitBodies();
        NotifyFrameDistance();
        InvalidateVisual();
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
        if (_simulationFrames?.TryGetValue(body.Id, out var simulation) == true)
            return (simulation.Position, simulation.Rotation);
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
