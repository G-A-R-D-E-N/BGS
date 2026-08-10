using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Hkx;
















public static class SplineFit
{

    private static readonly int[] Ladder = { 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128 };

    public sealed record Curve(int Degree, byte[] Knots, float[] ControlPoints, float Min, float Max, float Error);







    public static Curve FitScalar(IReadOnlyList<float> samples, float tolerance)
    {
        int frames = samples.Count;
        float min = float.MaxValue, max = float.MinValue;
        foreach (float s in samples) { if (s < min) min = s; if (s > max) max = s; }




        if (max - min <= 0f)
        {
            var flatKnots = SplineFormat.Knots(2, 1, frames);
            return new Curve(1, flatKnots, new[] { min, min }, min, max, 0f);
        }

        foreach (int count in Ladder)
        {
            if (count >= frames) break;
            var candidate = TryScalar(samples, count, 3, min, max, frames);
            if (candidate != null && candidate.Error <= tolerance) return candidate;
        }

        return ExactScalar(samples, min, max, frames);
    }







    public static Curve FitScalarAt(IReadOnlyList<float> samples, int controlPoints, int degree)
    {
        int frames = samples.Count;
        float min = float.MaxValue, max = float.MinValue;
        foreach (float s in samples) { if (s < min) min = s; if (s > max) max = s; }

        if (max - min <= 0f)
        {
            var flat = new float[controlPoints];
            for (int i = 0; i < controlPoints; i++) flat[i] = min;
            return new Curve(degree, SplineFormat.Knots(controlPoints, degree, frames), flat, min, max, 0f);
        }





        if (degree == 1 && controlPoints >= frames)
        {
            var cps = new float[controlPoints];
            for (int i = 0; i < controlPoints; i++) cps[i] = samples[Math.Min(i, frames - 1)];
            Quantise(cps, min, max);
            var exactKnots = SplineFormat.Knots(controlPoints, degree, frames);
            return new Curve(degree, exactKnots, cps, min, max, MeasureScalar(samples, degree, exactKnots, cps, frames));
        }

        var knots = SplineFormat.Knots(controlPoints, degree, frames);
        var solved = Solve(new[] { samples }, controlPoints, degree, ToFloat(knots), frames)[0];
        Quantise(solved, min, max);
        return new Curve(degree, knots, solved, min, max, MeasureScalar(samples, degree, knots, solved, frames));
    }





    private static Curve ExactScalar(IReadOnlyList<float> samples, float min, float max, int frames)
    {
        int count = Math.Max(2, frames);
        var knots = SplineFormat.Knots(count, 1, frames);
        var cps = new float[count];
        for (int i = 0; i < count; i++) cps[i] = samples[Math.Min(i, frames - 1)];

        Quantise(cps, min, max);
        return new Curve(1, knots, cps, min, max, MeasureScalar(samples, 1, knots, cps, frames));
    }

    private static Curve? TryScalar(IReadOnlyList<float> samples, int count, int degree,
        float min, float max, int frames)
    {
        if (count < degree + 1) return null;

        var knots = SplineFormat.Knots(count, degree, frames);
        if (!SplineFormat.KnotsUsable(knots, count, degree)) return null;

        var floatKnots = ToFloat(knots);
        var cps = Solve(new[] { samples }, count, degree, floatKnots, frames)[0];
        Quantise(cps, min, max);

        return new Curve(degree, knots, cps, min, max, MeasureScalar(samples, degree, knots, cps, frames));
    }








    public static (int Degree, byte[] Knots, System.Numerics.Quaternion[] ControlPoints, float Error)
        FitRotation(IReadOnlyList<System.Numerics.Quaternion> samples, float tolerance, int format)
    {
        int frames = samples.Count;




        var flat = new System.Numerics.Quaternion[frames];
        flat[0] = System.Numerics.Quaternion.Normalize(samples[0]);
        for (int f = 1; f < frames; f++)
        {
            var q = System.Numerics.Quaternion.Normalize(samples[f]);
            if (System.Numerics.Quaternion.Dot(q, flat[f - 1]) < 0) q = -q;
            flat[f] = q;
        }

        var channels = new float[4][];
        for (int c = 0; c < 4; c++) channels[c] = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            channels[0][f] = flat[f].X; channels[1][f] = flat[f].Y;
            channels[2][f] = flat[f].Z; channels[3][f] = flat[f].W;
        }

        foreach (int count in Ladder)
        {
            if (count >= frames) break;
            var candidate = TryRotation(flat, channels, count, 3, frames, format);
            if (candidate != null && candidate.Value.Error <= tolerance) return candidate.Value;
        }


        int exact = Math.Max(2, frames);
        var knots = SplineFormat.Knots(exact, 1, frames);
        var cps = new System.Numerics.Quaternion[exact];
        for (int i = 0; i < exact; i++) cps[i] = flat[Math.Min(i, frames - 1)];
        Round(cps, format);
        return (1, knots, cps, MeasureRotation(flat, 1, knots, cps, frames));
    }

    private static (int Degree, byte[] Knots, System.Numerics.Quaternion[] ControlPoints, float Error)?
        TryRotation(
        System.Numerics.Quaternion[] flat, float[][] channels, int count, int degree, int frames, int format)
    {
        if (count < degree + 1) return null;

        var knots = SplineFormat.Knots(count, degree, frames);
        if (!SplineFormat.KnotsUsable(knots, count, degree)) return null;

        var solved = Solve(channels, count, degree, ToFloat(knots), frames);
        var cps = new System.Numerics.Quaternion[count];
        for (int i = 0; i < count; i++)
            cps[i] = new System.Numerics.Quaternion(solved[0][i], solved[1][i], solved[2][i], solved[3][i]);

        Round(cps, format);
        return (degree, knots, cps, MeasureRotation(flat, degree, knots, cps, frames));
    }



    private static void Quantise(float[] cps, float min, float max)
    {
        for (int i = 0; i < cps.Length; i++)
            cps[i] = SplineFormat.Read16(SplineFormat.Write16(cps[i], min, max), min, max);
    }

    private static void Round(System.Numerics.Quaternion[] cps, int format)
    {
        var scratch = new byte[16];
        for (int i = 0; i < cps.Length; i++)
        {
            SplineQuat.Write(format, cps[i], scratch, 0);
            cps[i] = SplineQuat.Read(format, scratch, 0);
        }




        for (int i = 1; i < cps.Length; i++)
            if (System.Numerics.Quaternion.Dot(cps[i], cps[i - 1]) < 0) cps[i] = -cps[i];
    }

    private static float MeasureScalar(IReadOnlyList<float> samples, int degree, byte[] knots,
        float[] cps, int frames)
    {
        var k = ToFloat(knots);
        float worst = 0;
        for (int f = 0; f < frames; f++)
        {
            int span = SplineFormat.FindKnotSpan(degree, f, cps.Length, k);
            float got = SplineFormat.Evaluate(span, degree, f, k, cps);
            worst = MathF.Max(worst, MathF.Abs(got - samples[f]));
        }
        return worst;
    }

    private static float MeasureRotation(System.Numerics.Quaternion[] samples, int degree, byte[] knots,
        System.Numerics.Quaternion[] cps, int frames)
    {
        var k = ToFloat(knots);
        float worst = 0;
        for (int f = 0; f < frames; f++)
        {
            int span = SplineFormat.FindKnotSpan(degree, f, cps.Length, k);
            var got = SplineFormat.Evaluate(span, degree, f, k, cps);
            worst = MathF.Max(worst, SplineQuat.AngleBetween(got, samples[f]));
        }
        return worst;
    }

    private static float[] ToFloat(byte[] knots)
    {
        var f = new float[knots.Length];
        for (int i = 0; i < knots.Length; i++) f[i] = knots[i];
        return f;
    }











    private static float[][] Solve(IReadOnlyList<IReadOnlyList<float>> channels, int count, int degree,
        float[] knots, int frames)
    {
        int band = degree + 1;
        var normal = new double[count, band];
        var rhs = new double[channels.Count][];
        for (int c = 0; c < channels.Count; c++) rhs[c] = new double[count];

        var basis = new float[degree + 1];
        for (int f = 0; f < frames; f++)
        {
            int span = SplineFormat.FindKnotSpan(degree, f, count, knots);
            SplineFormat.Basis(span, degree, f, knots, basis);

            for (int a = 0; a <= degree; a++)
            {
                int row = span - a;
                if (row < 0 || row >= count) continue;

                for (int b = 0; b <= degree; b++)
                {
                    int col = span - b;
                    if (col < 0 || col > row) continue;
                    normal[row, row - col] += (double)basis[a] * basis[b];
                }

                for (int c = 0; c < channels.Count; c++)
                    rhs[c][row] += (double)basis[a] * channels[c][f];
            }
        }

        for (int i = 0; i < count; i++) normal[i, 0] += 1e-6;





        for (int i = 0; i < count; i++)
        {
            for (int j = Math.Min(i, band - 1); j >= 0; j--)
            {
                double sum = normal[i, j];
                for (int k = j + 1; k < band && i - k >= 0; k++)
                    if (k - j < band) sum -= normal[i, k] * normal[i - j, k - j];

                if (j == 0) normal[i, 0] = Math.Sqrt(Math.Max(sum, 1e-12));
                else normal[i, j] = sum / normal[i - j, 0];
            }
        }

        var result = new float[channels.Count][];
        for (int c = 0; c < channels.Count; c++)
        {
            var y = new double[count];
            for (int i = 0; i < count; i++)
            {
                double sum = rhs[c][i];
                for (int j = 1; j < band && i - j >= 0; j++) sum -= normal[i, j] * y[i - j];
                y[i] = sum / normal[i, 0];
            }

            var x = new double[count];
            for (int i = count - 1; i >= 0; i--)
            {
                double sum = y[i];
                for (int j = 1; j < band && i + j < count; j++) sum -= normal[i + j, j] * x[i + j];
                x[i] = sum / normal[i, 0];
            }

            result[c] = new float[count];
            for (int i = 0; i < count; i++) result[c][i] = (float)x[i];
        }

        return result;
    }
}
