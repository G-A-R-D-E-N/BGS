using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OpenCommonwealth.Services.Hkx;
using Vector3 = System.Numerics.Vector3;

namespace BehaviourStudio.App;

// The skeleton drawn as lines between joints, at whichever frame it has been given.
//
// It decides nothing about the pose. AnimationPose composes every bone position and this projects
// them, which is the same split the node canvas has with GraphAuthor: a pose that is wrong is wrong
// in a place a headless check can reach, not in the drawing.
//
// Havok is Z up and the units are the game's, so the fit to screen is worked out from the pose's own
// bounds rather than from a constant. A Deathclaw and an eyebot are not the same size and neither is
// a metre.
public class SkeletonView : Control
{
    private AnimationPose.Pose? _pose;
    private AnimationPose.Pose? _reference;

    private double _yaw = 0.6;
    private double _pitch = 0.25;
    private double _zoom = 1;
    private Point _pan;
    private Point _lastPointer;
    private bool _orbiting;
    private bool _panning;

    private double _fit = 1;
    private Vector3 _centre;

    public string HoveredBone { get; private set; } = "";
    public Action<string>? BoneHovered;

    /// Whether the reference pose is drawn behind the animated one, which is the only way to see how
    /// far a frame has actually moved from rest.
    public bool ShowReference { get; set; }

    public int DrawnBones => _pose?.Bones.Count ?? 0;
    public int DrawnEdges => _mesh?.Length ?? 0;

    // The mesh as line segments in the same space the bones are in, already posed. Held as a flat
    // pair array and drawn as one geometry rather than as thousands of separate lines: Dogmeat is
    // about twenty six thousand edges across six shapes, and that many draw calls a frame is the
    // difference between scrubbing and waiting.
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
        if (pose != null && pose.Bones.Count > 0)
        {
            _centre = pose.Centre;
            _fit = pose.Radius;
        }
        InvalidateVisual();
    }

    /// Re-poses without re-fitting. Scrubbing has to leave the camera where it was, or every frame
    /// jumps the view about as the bounds change.
    public void Update(AnimationPose.Pose? pose)
    {
        _pose = pose;
        InvalidateVisual();
    }

    public void Reset()
    {
        _pose = null;
        _reference = null;
        _mesh = null;
        _yaw = 0.6;
        _pitch = 0.25;
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
    }

    public void Frame()
    {
        _zoom = 1;
        _pan = default;
        if (_pose is { Bones.Count: > 0 })
        {
            _centre = _pose.Centre;
            _fit = _pose.Radius;
        }
        InvalidateVisual();
    }

    // Yaw turns the model about the game's up axis, pitch tilts it towards the viewer. Orthographic
    // on purpose: a perspective divide makes near bones read as longer than far ones, which is the
    // opposite of what someone reading a pose wants.
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

        var pose = _pose;
        if (pose == null || pose.Bones.Count == 0)
        {
            var empty = new FormattedText("No pose to draw.", CultureInfo.InvariantCulture,
                                          FlowDirection.LeftToRight, Typeface.Default, 12, Ux.MutedBrush);
            ctx.DrawText(empty, new Point(14, 14));
            return;
        }

        DrawGround(ctx);

        if (ShowReference && _reference != null)
        {
            var ghost = new Pen(new SolidColorBrush(Ux.TextDisabled, 0.5), 1);
            foreach (var (from, to) in _reference.Links)
                ctx.DrawLine(ghost, Project(_reference.Bones[from].Position),
                                    Project(_reference.Bones[to].Position));
        }

        // The mesh first, so the skeleton reads on top of it rather than being lost inside it.
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

        var bone = new Pen(Ux.MetaBrush, 1.6);
        foreach (var (from, to) in pose.Links)
            ctx.DrawLine(bone, Project(pose.Bones[from].Position), Project(pose.Bones[to].Position));

        // Joints on top of the lines, and roots larger, because a forked root is the thing a reader
        // has to find first and 58 of the 163 vanilla skeletons have one.
        foreach (var b in pose.Bones)
        {
            var at = Project(b.Position);
            bool root = b.Parent < 0;
            bool hovered = b.Name == HoveredBone && HoveredBone.Length > 0;
            double radius = hovered ? 4.5 : root ? 3.5 : 2;
            var brush = hovered ? Ux.AccentBrush : root ? Ux.CodeBrush : Ux.TitleBrush;
            ctx.DrawEllipse(brush, null, at, radius, radius);
        }

        if (HoveredBone.Length > 0)
        {
            var label = new FormattedText(HoveredBone, CultureInfo.InvariantCulture,
                                          FlowDirection.LeftToRight, Typeface.Default, 12, Ux.TitleBrush);
            ctx.DrawText(label, new Point(14, Bounds.Height - 26));
        }
    }

    // A horizon line through the pose's own origin, so a character standing on the ground reads as
    // standing on it rather than floating in a void with no sense of scale.
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
            // Stopped just short of straight down: at the pole the model spins about the cursor and
            // the view stops answering the drag.
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
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _orbiting = _panning = false;
        e.Pointer.Capture(null);
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
