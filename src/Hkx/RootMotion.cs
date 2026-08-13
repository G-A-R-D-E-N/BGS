using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenCommonwealth.Services;

namespace OpenCommonwealth.Services.Hkx;














public static class RootMotion
{





    public sealed record Sample(Vector3 Position, float TurnRadians)
    {
        public override string ToString() =>
            $"{Position.X:F2} {Position.Y:F2} {Position.Z:F2}, turned {TurnRadians * 180 / MathF.PI:F1} degrees";
    }

    public sealed class Motion
    {


        public Vector3 Up = Vector3.UnitZ;
        public Vector3 Forward = Vector3.UnitY;

        public float Duration;
        public readonly List<Sample> Samples = new();

        public bool Any => Samples.Count > 0;




        public Vector3 Travel => Samples.Count > 1 ? Samples[^1].Position - Samples[0].Position
                                                   : Vector3.Zero;

        public float Turn => Samples.Count > 1 ? Samples[^1].TurnRadians - Samples[0].TurnRadians : 0;

        public override string ToString() =>
            !Any ? "no root motion"
                 : $"{Samples.Count} samples over {Duration:F2}s, travelling {Travel.Length():F1} units " +
                   $"and turning {Turn * 180 / MathF.PI:F0} degrees";
    }





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
        Read(InputFilePolicy.ReadHkx(path), types);







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
