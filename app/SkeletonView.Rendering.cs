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

public partial class SkeletonView
{
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
            DrawFrameTable(ctx, _bodies);
            DrawFrameDistance(ctx, _bodies);
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
        if (_bodies is { Bodies.Count: > 0 }) DrawFrameDistance(ctx, _bodies);

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

    public static string FrameTableRow(HavokRigidBody body)
    {
        string name = body.Name.Length > 0 ? body.Name : "body " + body.Id;
        var p = body.Position;
        string origin = p.X.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                        p.Y.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                        p.Z.ToString("0.00", CultureInfo.InvariantCulture);
        string com = body.CenterOfMass == Vector3.Zero ? "no measured COM"
            : "(" + body.CenterOfMass.X.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
              body.CenterOfMass.Y.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
              body.CenterOfMass.Z.ToString("0.00", CultureInfo.InvariantCulture) + ")";
        return $"{body.Id,3}  {name,-16}  frame ({origin})  COM {com}";
    }

    private void DrawFrameTable(DrawingContext ctx, HavokRagdollModel model)
    {
        const float lineHeight = 15f;
        const float pad = 10f;
        var rows = model.Bodies.Select(FrameTableRow).ToList();
        if (rows.Count == 0) return;

        float width = rows.Max(r =>
        {
            var text = new FormattedText(r, CultureInfo.InvariantCulture,
                                         FlowDirection.LeftToRight, Typeface.Default, 11,
                                         Ux.TitleBrush);
            return (float)text.Width;
        });

        var panel = new Rect(8, 8, width + pad * 2, rows.Count * lineHeight + pad * 2);
        ctx.FillRectangle(new SolidColorBrush(Ux.Card, 0.92), panel);

        for (int i = 0; i < rows.Count; i++)
        {
            var text = new FormattedText(rows[i], CultureInfo.InvariantCulture,
                                         FlowDirection.LeftToRight, Typeface.Default, 11,
                                         i % 2 == 0 ? Ux.TitleBrush : Ux.MetaBrush);
            ctx.DrawText(text, new Point(8 + pad, 8 + pad + i * lineHeight));
        }
    }

    private void DrawFrameDistance(DrawingContext ctx, HavokRagdollModel model)
    {
        if (_frameDistanceBodies is not { } selected) return;
        var first = model.Bodies.FirstOrDefault(body => body.Id == selected.First);
        var second = model.Bodies.FirstOrDefault(body => body.Id == selected.Second);
        if (first == null || second == null) return;

        var a = Project(BodyFrame(model, first).Position);
        var b = Project(BodyFrame(model, second).Position);
        ctx.DrawLine(new Pen(new SolidColorBrush(Ux.TextTitle, 0.95), 1.7), a, b);
        var text = new FormattedText(FrameDistanceText, CultureInfo.InvariantCulture,
                                     FlowDirection.LeftToRight, Typeface.Default, 11, Ux.TitleBrush);
        ctx.DrawText(text, new Point((a.X + b.X) / 2 + 10, (a.Y + b.Y) / 2 - 22));
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
        {
            _pinnedBodies.Clear();
            if (_frameDistanceBodies is { Second: < 0 }) { _frameDistanceBodies = null; NotifyFrameDistance(); }
        }
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
                ToggleBodyPin(bodyPicked);
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

    private void ToggleBodyPin(int bodyId)
    {
        if (_pinnedBodies.Remove(bodyId))
        {
            if (_frameDistanceBodies is { } selected &&
                (selected.First == bodyId || selected.Second == bodyId))
                _frameDistanceBodies = null;
        }
        else
        {
            _pinnedBodies.Add(bodyId);
            if (_frameDistanceBodies is not { } selected)
                _frameDistanceBodies = (bodyId, -1);
            else
                _frameDistanceBodies = selected.Second < 0 ? (selected.First, bodyId) : selected.First == bodyId || selected.Second == bodyId ? selected : (selected.Second, bodyId);
        }

        NotifyFrameDistance();
        InvalidateVisual();
    }

    private void NotifyFrameDistance() =>
        FrameDistanceChanged?.Invoke(FrameDistanceText.Length > 0 ? FrameDistanceText : null);

    private static string BodyName(HavokRigidBody body) =>
        body.Name.Length > 0 ? body.Name : "body " + body.Id;
}
