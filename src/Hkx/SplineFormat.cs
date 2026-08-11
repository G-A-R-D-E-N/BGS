using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Hkx;












public static class SplineFormat
{

    public enum Channel
    {

        Identity,

        Static,

        Spline,
    }



    public const int QuantByte = 0;
    public const int PosByte   = 1;
    public const int RotByte   = 2;
    public const int ScaleByte = 3;

    public static Channel PosKind(byte flags, int axis) =>
        ((flags >> (axis + 4)) & 1) != 0 ? Channel.Spline :
        ((flags >> axis) & 1) != 0       ? Channel.Static : Channel.Identity;

    public static Channel ScaleKind(byte flags, int axis) => PosKind(flags, axis);



    public static Channel RotKind(byte flags) =>
        ((flags >> 4) & 0x0F) != 0 ? Channel.Spline :
        (flags & 0x0F) != 0        ? Channel.Static : Channel.Identity;


    public static int RotWidth(int format) => format switch
    {
        0 => 4,
        1 => 5,
        2 => 6,
        5 => 16,
        _ => 5,
    };


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







    public static byte[] Knots(int controlPoints, int degree, int framesInBlock)
    {
        int count = controlPoints + degree + 1;
        var knots = new byte[count];
        int interior = controlPoints - degree - 1;
        int last = Math.Max(0, framesInBlock - 1);

        for (int i = 0; i <= degree; i++) knots[i] = 0;
        for (int i = 1; i <= interior; i++)
        {


            double at = (double)i * last / (interior + 1);
            knots[degree + i] = (byte)Math.Clamp((int)Math.Round(at), 0, 255);
        }
        for (int i = 0; i < degree + 1; i++) knots[count - 1 - i] = (byte)Math.Clamp(last, 0, 255);



        return knots;
    }


    public static bool KnotsUsable(byte[] knots, int controlPoints, int degree)
    {
        for (int i = degree; i < controlPoints; i++)
            if (knots[i] >= knots[i + 1]) return false;
        return true;
    }








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
