using System;
using System.IO;
using System.Numerics;
using BepuPhysics.Constraints;

namespace OpenCommonwealth.Services.Hkx;

public sealed partial class RagdollSimulation
{
    private static readonly SpringSettings JointSpring = new(30, 1);

    private void AddJoint(HavokConstraint joint)
    {
        var a = _bodies[joint.BodyA];
        var b = _bodies[joint.BodyB];
        _adjacent.Add(Pair(a.Handle.Value, b.Handle.Value));
        Vector3 offsetA = Position(joint.FrameA!) - a.Center;
        Vector3 offsetB = Position(joint.FrameB!) - b.Center;
        var solver = Required().Solver;
        if (joint.Kind == HavokConstraintKind.Fixed)
        {
            Quaternion relative = Basis(joint.FrameA!, 2, 0) * Quaternion.Inverse(Basis(joint.FrameB!, 2, 0));
            solver.Add(a.Handle, b.Handle, new Weld { LocalOrientation = relative,
                LocalOffset = offsetA - Vector3.Transform(offsetB, relative), SpringSettings = JointSpring });
            return;
        }
        if (joint.Kind is HavokConstraintKind.Hinge or HavokConstraintKind.LimitedHinge)
        {
            solver.Add(a.Handle, b.Handle, new Hinge { LocalOffsetA = offsetA, LocalOffsetB = offsetB,
                LocalHingeAxisA = Axis(joint.FrameA!, joint.LimitAxis), LocalHingeAxisB = Axis(joint.FrameB!, joint.LimitAxis), SpringSettings = JointSpring });
            if (joint.Kind == HavokConstraintKind.LimitedHinge)
                solver.Add(a.Handle, b.Handle, new TwistLimit {
                    LocalBasisA = Basis(joint.FrameA!, joint.LimitAxis, (joint.LimitAxis + 1) % 3),
                    LocalBasisB = Basis(joint.FrameB!, joint.LimitAxis, (joint.LimitAxis + 1) % 3),
                    MinimumAngle = joint.MinAngle, MaximumAngle = joint.MaxAngle, SpringSettings = JointSpring });
            return;
        }
        solver.Add(a.Handle, b.Handle, new BallSocket { LocalOffsetA = offsetA, LocalOffsetB = offsetB, SpringSettings = JointSpring });
        if (joint.Kind != HavokConstraintKind.Ragdoll) return;
        solver.Add(a.Handle, b.Handle, new SwingLimit { AxisLocalA = Axis(joint.FrameA!, joint.ConeTwistAxis),
            AxisLocalB = Axis(joint.FrameB!, joint.ConeRefAxis), MaximumSwingAngle = joint.ConeMaxAngle!.Value, SpringSettings = JointSpring });
        solver.Add(a.Handle, b.Handle, new TwistLimit {
            LocalBasisA = Basis(joint.FrameA!, joint.TwistAxis, joint.TwistRefAxis),
            LocalBasisB = Basis(joint.FrameB!, joint.TwistAxis, joint.TwistRefAxis),
            MinimumAngle = joint.TwistMinAngle!.Value, MaximumAngle = joint.TwistMaxAngle!.Value, SpringSettings = JointSpring });
    }

    private static Vector3 Position(float[] frame) => new(frame[12], frame[13], frame[14]);
    private static Vector3 Axis(float[] frame, int axis) => new(frame[4 * axis], frame[4 * axis + 1], frame[4 * axis + 2]);

    private static Quaternion Basis(float[] frame, int twist, int reference)
    {
        Vector3 z = Axis(frame, twist), x = Axis(frame, reference), y = Vector3.Cross(z, x);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
    }

    private static void ValidateJoint(HavokConstraint joint)
    {
        if (joint.BodyA == joint.BodyB || joint.Kind == HavokConstraintKind.Unknown)
            throw new NotSupportedException($"Joint {joint.Id} has an unsupported kind or connects one body to itself.");
        ValidateFrame(joint.FrameA); ValidateFrame(joint.FrameB);
        if (joint.Kind is HavokConstraintKind.Hinge or HavokConstraintKind.LimitedHinge &&
            (!joint.HasPreviewLimits || joint.LimitAxis is < 0 or > 2))
            throw new InvalidDataException($"Joint {joint.Id} has no measured hinge axis/limits.");
        if (joint.Kind == HavokConstraintKind.LimitedHinge) Range(joint.MinAngle, joint.MaxAngle);
        if (joint.Kind == HavokConstraintKind.Ragdoll)
        {
            if (!joint.HasPreviewLimits || joint.TwistAxis is < 0 or > 2 || joint.TwistRefAxis is < 0 or > 2 ||
                joint.TwistAxis == joint.TwistRefAxis || joint.ConeTwistAxis is < 0 or > 2 || joint.ConeRefAxis is < 0 or > 2 ||
                joint.TwistMinAngle == null || joint.TwistMaxAngle == null || joint.ConeMaxAngle == null ||
                !float.IsFinite(joint.ConeMaxAngle.Value) || joint.ConeMaxAngle is < 0 or > MathF.PI)
                throw new InvalidDataException($"Joint {joint.Id} has incomplete angular limits.");
            Range(joint.TwistMinAngle.Value, joint.TwistMaxAngle.Value);
        }
    }

    private static void Range(float min, float max)
    {
        if (!float.IsFinite(min) || !float.IsFinite(max) || min < -MathF.PI || max > MathF.PI || min > max)
            throw new InvalidDataException("Angular limits must be ordered within [-pi, pi].");
    }

    private static void ValidateFrame(float[]? frame)
    {
        if (frame == null || frame.Length != 16 || Array.Exists(frame, v => !float.IsFinite(v)))
            throw new InvalidDataException("Simulation needs complete, finite joint frames.");
        Vector3 x = Axis(frame, 0), y = Axis(frame, 1), z = Axis(frame, 2);
        if (MathF.Abs(x.LengthSquared() - 1) > 0.002f || MathF.Abs(y.LengthSquared() - 1) > 0.002f || MathF.Abs(z.LengthSquared() - 1) > 0.002f ||
            MathF.Abs(Vector3.Dot(x, y)) > 0.002f || MathF.Abs(Vector3.Dot(x, z)) > 0.002f || MathF.Abs(Vector3.Dot(y, z)) > 0.002f ||
            Vector3.Dot(Vector3.Cross(x, y), z) < 0.998f)
            throw new InvalidDataException("Simulation joint frames must be orthonormal and right handed.");
    }
}
