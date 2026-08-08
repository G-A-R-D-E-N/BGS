using System;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

// Packing a rotation into the widths the format uses.
//
// The reading side of all of this has been in HkxBinaryReader since the beginning. This is the
// inverse, and it is written here rather than beside the readers because the readers are private to
// a class that only reads. Every one of these is checked against its own reader rather than against
// a specification, by the pack-then-unpack tests: a writer that agrees with a description and not
// with the reader beside it produces files nothing can read.
//
// The scheme in both narrow formats is the same one: a unit quaternion has one component whose size
// is fixed by the other three, so the largest is dropped and its position and sign are recorded. The
// three that survive are each at most one over root two, which is what sets the scale factors.
public static class SplineQuat
{
    private const float Fractal40 = 0.000345436f;
    private const float Fractal48 = 0.000043161f;
    private const int Mask15 = (1 << 15) - 1;

    /// The largest component by size, which is the one the narrow formats leave out.
    private static int Largest(Quaternion q)
    {
        Span<float> c = stackalloc float[4] { q.X, q.Y, q.Z, q.W };
        int at = 0;
        for (int i = 1; i < 4; i++)
            if (MathF.Abs(c[i]) > MathF.Abs(c[at])) at = i;
        return at;
    }

    private static void Others(Quaternion q, int skip, Span<float> into)
    {
        Span<float> c = stackalloc float[4] { q.X, q.Y, q.Z, q.W };
        int n = 0;
        for (int i = 0; i < 4; i++)
            if (i != skip) into[n++] = c[i];
    }

    /// Five bytes: three twelve bit components, then the dropped one's position and sign.
    ///
    /// The scale factor cannot quite reach one over root two: the largest storable value is 0.70676
    /// against a possible 0.70711. A rotation sitting exactly on that corner therefore comes back a
    /// fraction short. That is the format's own limit rather than this writer's, it is what every
    /// shipped animation is already stored with, and the reader normalises afterwards, which is where
    /// it goes away.
    public static void Write40(Quaternion q, byte[] into, int at)
    {
        q = Quaternion.Normalize(q);
        int skip = Largest(q);
        Span<float> v = stackalloc float[3];
        Others(q, skip, v);

        bool negative = (skip switch { 0 => q.X, 1 => q.Y, 2 => q.Z, _ => q.W }) < 0;

        ulong raw = 0;
        for (int i = 0; i < 3; i++)
        {
            int quantised = Math.Clamp((int)MathF.Round(v[i] / Fractal40) + 2049, 0, 4095);
            raw |= (ulong)(uint)quantised << (i * 12);
        }
        raw |= (ulong)(uint)skip << 36;
        if (negative) raw |= 1UL << 38;

        for (int i = 0; i < 5; i++) into[at + i] = (byte)(raw >> (i * 8));
    }

    /// Six bytes: three fifteen bit components, with the dropped one's position split across two of
    /// the spare top bits and its sign in the third.
    public static void Write48(Quaternion q, byte[] into, int at)
    {
        q = Quaternion.Normalize(q);
        int skip = Largest(q);
        Span<float> v = stackalloc float[3];
        Others(q, skip, v);

        bool negative = (skip switch { 0 => q.X, 1 => q.Y, 2 => q.Z, _ => q.W }) < 0;

        Span<ushort> word = stackalloc ushort[3];
        for (int i = 0; i < 3; i++)
        {
            int quantised = Math.Clamp((int)MathF.Round(v[i] / Fractal48) + (Mask15 >> 1), 0, Mask15);
            word[i] = (ushort)quantised;
        }

        // The reader takes the low bit of the position from bit fifteen of the first word and the
        // high bit from bit fifteen of the second, and the sign from bit fifteen of the third.
        if ((skip & 1) != 0) word[0] |= 1 << 15;
        if ((skip & 2) != 0) word[1] |= 1 << 15;
        if (negative) word[2] |= 1 << 15;

        for (int i = 0; i < 3; i++)
        {
            into[at + i * 2] = (byte)word[i];
            into[at + i * 2 + 1] = (byte)(word[i] >> 8);
        }
    }

    /// Sixteen bytes, four floats, which is what the format calls uncompressed.
    public static void WritePlain(Quaternion q, byte[] into, int at)
    {
        q = Quaternion.Normalize(q);
        BitConverter.GetBytes(q.X).CopyTo(into, at);
        BitConverter.GetBytes(q.Y).CopyTo(into, at + 4);
        BitConverter.GetBytes(q.Z).CopyTo(into, at + 8);
        BitConverter.GetBytes(q.W).CopyTo(into, at + 12);
    }

    public static void Write(int format, Quaternion q, byte[] into, int at)
    {
        switch (format)
        {
            case 2: Write48(q, into, at); break;
            case 5: WritePlain(q, into, at); break;
            default: Write40(q, into, at); break;
        }
    }

    // The readers, in public form. HkxBinaryReader keeps its own private copies for the decode path
    // it has always had; these exist so a fit can ask what a control point will actually come back as
    // after it has been packed, which is the only rounding that matters to the error it reports.

    public static Quaternion Read40(byte[] from, int at)
    {
        ulong raw = 0;
        for (int i = 0; i < 5; i++) raw |= (ulong)from[at + i] << (i * 8);

        float v0 = ((long)((raw >> 0) & 0xFFF) - 2049) * Fractal40;
        float v1 = ((long)((raw >> 12) & 0xFFF) - 2049) * Fractal40;
        float v2 = ((long)((raw >> 24) & 0xFFF) - 2049) * Fractal40;
        float w = MathF.Sqrt(MathF.Max(0, 1 - v0 * v0 - v1 * v1 - v2 * v2));
        if (((raw >> 38) & 1) != 0) w = -w;

        return Quaternion.Normalize(((raw >> 36) & 3) switch
        {
            0 => new Quaternion(w, v0, v1, v2),
            1 => new Quaternion(v0, w, v1, v2),
            2 => new Quaternion(v0, v1, w, v2),
            _ => new Quaternion(v0, v1, v2, w),
        });
    }

    public static Quaternion Read48(byte[] from, int at)
    {
        ushort xr = BitConverter.ToUInt16(from, at);
        ushort yr = BitConverter.ToUInt16(from, at + 2);
        ushort zr = BitConverter.ToUInt16(from, at + 4);

        int skip = ((yr >> 14) & 2) | ((xr >> 15) & 1);
        bool negative = (zr >> 15) != 0;

        float v0 = ((xr & Mask15) - (Mask15 >> 1)) * Fractal48;
        float v1 = ((yr & Mask15) - (Mask15 >> 1)) * Fractal48;
        float v2 = ((zr & Mask15) - (Mask15 >> 1)) * Fractal48;
        float w = MathF.Sqrt(MathF.Max(0, 1 - v0 * v0 - v1 * v1 - v2 * v2));
        if (negative) w = -w;

        return Quaternion.Normalize(skip switch
        {
            0 => new Quaternion(w, v0, v1, v2),
            1 => new Quaternion(v0, w, v1, v2),
            2 => new Quaternion(v0, v1, w, v2),
            _ => new Quaternion(v0, v1, v2, w),
        });
    }

    public static Quaternion ReadPlain(byte[] from, int at) => Quaternion.Normalize(new Quaternion(
        BitConverter.ToSingle(from, at), BitConverter.ToSingle(from, at + 4),
        BitConverter.ToSingle(from, at + 8), BitConverter.ToSingle(from, at + 12)));

    public static Quaternion Read(int format, byte[] from, int at) => format switch
    {
        2 => Read48(from, at),
        5 => ReadPlain(from, at),
        _ => Read40(from, at),
    };

    /// How far apart two rotations are, in radians, ignoring which of the two signs each carries.
    ///
    /// Not the obvious `2 * acos(dot)`. That form cannot resolve a small angle at all: acos near one
    /// turns the last few bits of the dot product into the whole answer, so in single precision it
    /// bottoms out around 0.0009 radians and reports that for everything closer. It looked like a
    /// real measurement of the rotation codec until a packer known to be good measured the same
    /// 0.0007 as everything else, which is the giveaway.
    ///
    /// The half angle form below is conditioned the other way round: the two lengths are large where
    /// the answer is small, so it stays accurate all the way down. Done in double for the same
    /// reason, since the inputs are single and the differences are where the answer lives.
    public static float AngleBetween(Quaternion a, Quaternion b)
    {
        a = Quaternion.Normalize(a);
        b = Quaternion.Normalize(b);
        if (Quaternion.Dot(a, b) < 0) b = -b;

        double dx = (double)a.X - b.X, dy = (double)a.Y - b.Y;
        double dz = (double)a.Z - b.Z, dw = (double)a.W - b.W;
        double sx = (double)a.X + b.X, sy = (double)a.Y + b.Y;
        double sz = (double)a.Z + b.Z, sw = (double)a.W + b.W;

        double apart = Math.Sqrt(dx * dx + dy * dy + dz * dz + dw * dw);
        double together = Math.Sqrt(sx * sx + sy * sy + sz * sz + sw * sw);
        return (float)(2.0 * Math.Atan2(apart, together));
    }
}
