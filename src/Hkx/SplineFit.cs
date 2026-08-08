using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Hkx;

// Choosing control points so a curve passes close enough to a set of frames.
//
// The compression in a spline compressed animation is entirely here. Everything else is packing: how
// many bits a number gets, where a run starts. This is the part that decides a hundred frames can be
// described by eight numbers, and it is the part that can be wrong in a way no format check catches,
// because a badly fitted curve is a perfectly well formed file that plays the wrong motion.
//
// So the fit never trusts itself. Every candidate is quantised, decoded back through the same
// evaluator a reader will use, and measured against the frames it came from. A candidate is accepted
// because its measured error is small, not because the mathematics says it should be.
//
// There is always an answer. A curve of degree one with one control point per frame passes exactly
// through every frame, since a clamped linear B-spline interpolates its control points, so the search
// below is only ever choosing something smaller than a guaranteed fallback rather than hoping to find
// one at all.
public static class SplineFit
{
    /// The control point counts tried, smallest first, before falling back to one per frame.
    private static readonly int[] Ladder = { 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128 };

    public sealed record Curve(int Degree, byte[] Knots, float[] ControlPoints, float Min, float Max, float Error);

    /// Fits one axis of a position or a scale.
    ///
    /// Returns the smallest curve on the ladder whose measured error is within the tolerance, and the
    /// exact one per frame curve otherwise. The exact one is not a failure and is not reported as one:
    /// a channel with real noise in it genuinely needs a control point per frame, and describing that
    /// as a fallback would invite somebody to loosen the tolerance until the noise disappeared.
    public static Curve FitScalar(IReadOnlyList<float> samples, float tolerance)
    {
        int frames = samples.Count;
        float min = float.MaxValue, max = float.MinValue;
        foreach (float s in samples) { if (s < min) min = s; if (s > max) max = s; }

        // A flat channel still has to be written as a curve if its neighbours on the same track are
        // curves, because the mask has one kind per axis and the caller has already decided. Two
        // control points is the smallest thing the format will hold.
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

    /// Fits an axis onto a shape somebody else chose.
    ///
    /// The three axes of a position share one knot vector, so once the widest of them has decided how
    /// many control points there are the other two have to be refitted onto that same shape rather
    /// than keeping the smaller one they would have picked alone. Reports its error the same way, so
    /// a refit that turns out worse than the fit it replaced is still visible.
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

        // At one control point per frame the least squares system is square and the answer is the
        // samples themselves, so it is written straight in. That also keeps the exact case exact:
        // solving for it would introduce the solver's own rounding into the one path whose whole
        // point is that it does not need to approximate.
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

    /// One control point per frame at degree one, which interpolates every frame exactly.
    ///
    /// The only error left is the sixteen bits each control point is stored in, which is the channel's
    /// own range divided by 65535 and is reported rather than assumed to be nothing.
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

    /// Fits a rotation as four curves sharing one knot vector, which is what the format stores.
    ///
    /// The components are fitted independently and the result is normalised on the way out, which is
    /// what the reader does too. Fitting them jointly on the sphere would be more correct and is not
    /// what the format can express: it holds plain control points and the reader interpolates them
    /// linearly in four dimensions, so a fit that assumed anything else would be fitting a curve
    /// nobody draws.
    public static (int Degree, byte[] Knots, System.Numerics.Quaternion[] ControlPoints, float Error)
        FitRotation(IReadOnlyList<System.Numerics.Quaternion> samples, float tolerance, int format)
    {
        int frames = samples.Count;

        // Sign is free in a quaternion and a flip halfway through a channel looks like a curve
        // swinging the long way round. Made continuous before anything is fitted, because a fit over
        // a discontinuity produces a curve that is wrong everywhere rather than at one frame.
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

        // One control point per frame at degree one, exact but for the packing.
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

    // Putting every control point through the packing it will actually get, so the error measured
    // below is the error a reader will see rather than the error of an unquantised curve.
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

        // The reader flips a control point to match the one before it as it reads them back, so a
        // packed pair that came out on opposite sides has to be made continuous again here or the
        // measurement below is of a curve the reader will not build.
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

    /// Least squares over the frames, for as many channels as share one knot vector.
    ///
    /// A B-spline of degree d touches only d+1 control points at any one frame, so the normal
    /// equations come out banded and are solved in place as a band rather than as a full matrix. That
    /// is not only speed: a full solve of a 128 by 128 system per channel per block, over a corpus
    /// this size, is the difference between a gate that runs and one nobody ever runs twice.
    ///
    /// The tiny value added down the diagonal is there for the ends, where a control point can be
    /// reached by no frame at all and would otherwise leave the system singular. It biases such a
    /// point toward zero, which is harmless because nothing evaluates through it.
    private static float[][] Solve(IReadOnlyList<IReadOnlyList<float>> channels, int count, int degree,
        float[] knots, int frames)
    {
        int band = degree + 1;
        var normal = new double[count, band];      // lower band, normal[i, j] is row i, column i-j
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
                    if (col < 0 || col > row) continue;      // lower triangle only
                    normal[row, row - col] += (double)basis[a] * basis[b];
                }

                for (int c = 0; c < channels.Count; c++)
                    rhs[c][row] += (double)basis[a] * channels[c][f];
            }
        }

        for (int i = 0; i < count; i++) normal[i, 0] += 1e-6;

        // Banded Cholesky, in place. normal[i, j] holds row i, column i-j, so the band is stored
        // leaning left and every index below is a distance back from the diagonal rather than a
        // column number. The columns of a row are done furthest first because a nearer one is built
        // out of them.
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
