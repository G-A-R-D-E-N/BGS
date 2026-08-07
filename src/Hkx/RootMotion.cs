using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

// Where a clip travels, as distinct from what it does with the bones.
//
// An animation that walks does not move its bones across the ground. It plays a walk on the spot and
// carries a separate track saying where the character has got to, which the game applies to the
// object rather than to the rig. That track is `hkaAnimation.extractedMotion`, pointing at a
// `hkaDefaultAnimatedReferenceFrame`.
//
// Two things need it. Drawing a travelling clip without it walks the character off the middle of the
// view, because the bones stay put and nothing moves the camera. And a clip's real displacement is
// not readable from the bones at all, so "how far does this take you" has no answer without it.
//
// Every offset below is read out of the class table rather than written down here, so a field that
// moves in some other build moves here too instead of silently reading its neighbour.
public static class RootMotion
{
    /// One frame of travel: where the character has got to, and how far it has turned.
    ///
    /// The stored sample is four floats. The first three are a position. The fourth is not a
    /// position's w: it is the rotation about the up axis, stored as an angle, which is why this is
    /// not simply a Vector4 with a meaningful w.
    public sealed record Sample(Vector3 Position, float TurnRadians)
    {
        public override string ToString() =>
            $"{Position.X:F2} {Position.Y:F2} {Position.Z:F2}, turned {TurnRadians * 180 / MathF.PI:F1} degrees";
    }

    public sealed class Motion
    {
        /// Which way is up and which way is forward, as the animation itself declares them, rather
        /// than as this game is assumed to hold them.
        public Vector3 Up = Vector3.UnitZ;
        public Vector3 Forward = Vector3.UnitY;

        public float Duration;
        public readonly List<Sample> Samples = new();

        public bool Any => Samples.Count > 0;

        /// How far the clip travels in total. The straight line from the first sample to the last,
        /// not the length of the path, because the question this answers is where a character ends
        /// up rather than how far it walked to get there.
        public Vector3 Travel => Samples.Count > 1 ? Samples[^1].Position - Samples[0].Position
                                                   : Vector3.Zero;

        public float Turn => Samples.Count > 1 ? Samples[^1].TurnRadians - Samples[0].TurnRadians : 0;

        public override string ToString() =>
            !Any ? "no root motion"
                 : $"{Samples.Count} samples over {Duration:F2}s, travelling {Travel.Length():F1} units " +
                   $"and turning {Turn * 180 / MathF.PI:F0} degrees";
    }

    /// The motion an animation file carries, or an empty one when it stays on the spot.
    ///
    /// An animation with no extracted motion is the ordinary case, not a failure: an idle goes
    /// nowhere and has no reference frame object at all.
    public static Motion Read(byte[] hkx, HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;

        var motion = new Motion();
        var image = PackfileImage.Read(hkx);
        var objects = new PackfileObjects(image);

        var frame = objects.Instances.FirstOrDefault(
            i => i.ClassName == "hkaDefaultAnimatedReferenceFrame");
        if (frame == null) return motion;

        Vector3 Vector(string field)
        {
            var read = objects.ReadFloats(frame, field, 3);
            return read == null ? Vector3.Zero : new Vector3(read[0], read[1], read[2]);
        }

        var up = Vector("up");
        var forward = Vector("forward");
        if (up != Vector3.Zero) motion.Up = up;
        if (forward != Vector3.Zero) motion.Forward = forward;

        motion.Duration = objects.ReadFloat(frame, "duration") ?? 0;

        // Four floats per sample, so sixteen bytes, which is also what the format aligns a vector to.
        var samples = objects.ReadValueArray(frame, "referenceFrameSamples", 16,
            (bytes, at) => new Sample(
                new Vector3(BitConverter.ToSingle(bytes, at),
                            BitConverter.ToSingle(bytes, at + 4),
                            BitConverter.ToSingle(bytes, at + 8)),
                BitConverter.ToSingle(bytes, at + 12)));

        if (samples != null) motion.Samples.AddRange(samples);
        return motion;
    }

    public static Motion Read(string path, HavokClassTypes? types = null) =>
        Read(System.IO.File.ReadAllBytes(path), types);

    /// Where the character has got to at a given frame of the clip.
    ///
    /// The samples are spread evenly across the clip's own duration and there is no promise there is
    /// one per animation frame, so this reads between them rather than indexing. Falls back to
    /// nothing rather than to the first sample when there is no motion, because returning the first
    /// sample would put a stationary clip at wherever that sample happens to sit.
    public static Sample At(Motion motion, float fraction)
    {
        if (!motion.Any) return new Sample(Vector3.Zero, 0);
        if (motion.Samples.Count == 1) return motion.Samples[0];

        float where = Math.Clamp(fraction, 0, 1) * (motion.Samples.Count - 1);
        int first = (int)where;
        int second = Math.Min(first + 1, motion.Samples.Count - 1);
        float between = where - first;

        var a = motion.Samples[first];
        var b = motion.Samples[second];

        return new Sample(Vector3.Lerp(a.Position, b.Position, between),
                          a.TurnRadians + (b.TurnRadians - a.TurnRadians) * between);
    }
}
