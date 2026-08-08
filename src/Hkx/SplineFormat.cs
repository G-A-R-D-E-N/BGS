using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Hkx;

// What a spline compressed animation's data blob is made of, in one place.
//
// The reader has carried this knowledge inside its decompressor since the beginning, spelled out
// three times over for position, rotation and scale. Writing a blob needs the same knowledge facing
// the other way, and two copies of a bit layout is how one of them quietly goes wrong. So the layout
// lives here and both directions read it from here.
//
// The blob is a run of blocks. A block covers maxFramesPerBlock consecutive frames and starts with
// one four byte mask per track, then every track's channels in order: position, rotation, scale.
// Nothing in the block says where a track's data starts; it is found only by decoding every track
// before it, which is why a single wrong width shifts the rest of the block rather than one value.
public static class SplineFormat
{
    /// How a channel varies across a block.
    public enum Channel
    {
        /// Not driven. Position and rotation fall back to nothing, scale to one.
        Identity,
        /// One value for the whole block, written once.
        Static,
        /// A B-spline: knots, a range per axis, then quantised control points.
        Spline,
    }

    // The mask is four bytes. The first holds the three quantisation formats, and the other three
    // hold the channel kinds, low nibble static and high nibble spline, one bit per axis.
    public const int QuantByte = 0;
    public const int PosByte   = 1;
    public const int RotByte   = 2;
    public const int ScaleByte = 3;

    public static Channel PosKind(byte flags, int axis) =>
        ((flags >> (axis + 4)) & 1) != 0 ? Channel.Spline :
        ((flags >> axis) & 1) != 0       ? Channel.Static : Channel.Identity;

    public static Channel ScaleKind(byte flags, int axis) => PosKind(flags, axis);

    // Rotation has no axes, so the whole nibble is the flag rather than one bit of it. Vanilla sets
    // every bit of the nibble rather than one, which is why this tests the nibble and not bit four.
    public static Channel RotKind(byte flags) =>
        ((flags >> 4) & 0x0F) != 0 ? Channel.Spline :
        (flags & 0x0F) != 0        ? Channel.Static : Channel.Identity;

    /// The bytes a control point of a rotation takes, by quantisation format.
    public static int RotWidth(int format) => format switch
    {
        0 => 4,   // 32 bit, polar
        1 => 5,   // 40 bit
        2 => 6,   // 48 bit
        5 => 16,  // four floats
        _ => 5,
    };

    /// What a rotation control point has to start on. The narrow formats pack without padding.
    public static int RotAlign(int format) => format switch
    {
        1 or 3 => 1,
        2 or 4 => 2,
        _ => 4,
    };

    public static int Align(int value, int to)
    {
        int over = value % to;
        return over == 0 ? value : value + (to - over);
    }

    /// The knot vector a block of this shape carries: clamped, integer valued, one span per frame.
    ///
    /// The count is fixed by the format, numItems + degree + 2 where numItems is one less than the
    /// number of control points, which is the ordinary clamped B-spline count written a different
    /// way. The values are frame indices inside the block and are stored as single bytes, which is
    /// what caps a block at 256 frames rather than any field width in the header.
    public static byte[] Knots(int controlPoints, int degree, int framesInBlock)
    {
        int count = controlPoints + degree + 1;
        var knots = new byte[count];
        int interior = controlPoints - degree - 1;
        int last = Math.Max(0, framesInBlock - 1);

        for (int i = 0; i <= degree; i++) knots[i] = 0;
        for (int i = 1; i <= interior; i++)
        {
            // Spread the interior knots evenly across the block. Rounded rather than truncated so a
            // knot vector never repeats a value it did not mean to, which would collapse a span.
            double at = (double)i * last / (interior + 1);
            knots[degree + i] = (byte)Math.Clamp((int)Math.Round(at), 0, 255);
        }
        for (int i = 0; i < degree + 1; i++) knots[count - 1 - i] = (byte)Math.Clamp(last, 0, 255);

        // A repeated interior knot is legal but means a span of zero width, and the evaluator divides
        // by that width. Nudging is wrong, so the fit asks for fewer control points instead.
        return knots;
    }

    /// Whether a knot vector has a usable span for every frame it covers.
    public static bool KnotsUsable(byte[] knots, int controlPoints, int degree)
    {
        for (int i = degree; i < controlPoints; i++)
            if (knots[i] >= knots[i + 1]) return false;
        return true;
    }

    // The curve itself.
    //
    // This is the one place a control point becomes a frame value. The decoder called its own copy
    // for years; the encoder has to fit against exactly the curve the decoder will draw, and two
    // implementations of a basis function that agree today are two that can stop agreeing. So both
    // call these, and a fit measured with them is measured against what a reader will actually get.

    public static int FindKnotSpan(int degree, float t, int controlPoints, float[] knots)
    {
        if (controlPoints <= 0 || knots.Length == 0) return 0;
        if (t >= knots[controlPoints]) return controlPoints - 1;

        int lo = degree, hi = controlPoints, mid = (lo + hi) / 2;
        for (int step = 0; step < 100; step++)
        {
            if (t < knots[mid]) hi = mid;
            else if (t >= knots[mid + 1]) lo = mid;
            else break;
            mid = (lo + hi) / 2;
        }
        return mid;
    }

    /// The basis values at t, highest index first, which is the order the sums below read them in.
    ///
    /// Written in place rather than returned so a fit can call it once per frame across a search over
    /// control point counts without allocating a small array every time.
    public static void Basis(int span, int degree, float t, float[] knots, float[] into)
    {
        Array.Clear(into, 0, degree + 1);
        into[0] = 1f;

        for (int i = 1; i <= degree; i++)
            for (int j = i - 1; j >= 0; j--)
            {
                float width = span + i - j < knots.Length && span - j >= 0
                    ? knots[span + i - j] - knots[span - j] : 0;
                float along = width >= 1e-10f ? (t - knots[span - j]) / width : 0;
                float scaled = into[j] * along;
                if (j + 1 < into.Length) into[j + 1] += into[j] - scaled;
                into[j] = scaled;
            }
    }

    public static float Evaluate(int span, int degree, float t, float[] knots, IReadOnlyList<float> cps)
    {
        if (cps.Count == 0) return 0;
        if (cps.Count == 1) return cps[0];

        var basis = new float[degree + 1];
        Basis(span, degree, t, knots, basis);

        float total = 0;
        for (int i = 0; i <= degree; i++)
        {
            int at = span - i;
            if (at >= 0 && at < cps.Count) total += cps[at] * basis[i];
        }
        return total;
    }

    public static System.Numerics.Quaternion Evaluate(int span, int degree, float t, float[] knots,
        IReadOnlyList<System.Numerics.Quaternion> cps)
    {
        if (cps.Count == 0) return System.Numerics.Quaternion.Identity;
        if (cps.Count == 1) return cps[0];

        var basis = new float[degree + 1];
        Basis(span, degree, t, knots, basis);

        System.Numerics.Quaternion total = new(0, 0, 0, 0);
        for (int i = 0; i <= degree; i++)
        {
            int at = span - i;
            if (at < 0 || at >= cps.Count) continue;
            var q = cps[at];
            total = new System.Numerics.Quaternion(total.X + q.X * basis[i], total.Y + q.Y * basis[i],
                                                   total.Z + q.Z * basis[i], total.W + q.W * basis[i]);
        }
        return System.Numerics.Quaternion.Normalize(total);
    }

    // Quantisation, both ways.
    //
    // The reading side of each of these already existed and is what the game's files are decoded
    // with. The writing side is its inverse and is put beside it deliberately: an encoder whose
    // rounding does not match the decoder's spacing loses a bit of range at one end and nobody
    // notices until a value at the very top of a channel's range comes back wrong.

    public static float Read16(ushort raw, float min, float max) => min + (max - min) * (raw / 65535f);

    public static ushort Write16(float value, float min, float max)
    {
        if (max <= min) return 0;
        float along = (value - min) / (max - min);
        return (ushort)Math.Clamp((int)MathF.Round(along * 65535f), 0, 65535);
    }

    public static float Read8(byte raw, float min, float max) => min + (max - min) * (raw / 255f);

    public static byte Write8(float value, float min, float max)
    {
        if (max <= min) return 0;
        float along = (value - min) / (max - min);
        return (byte)Math.Clamp((int)MathF.Round(along * 255f), 0, 255);
    }
}
