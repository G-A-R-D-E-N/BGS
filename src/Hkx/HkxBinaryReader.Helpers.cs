using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace OpenCommonwealth.Services.Hkx;

public partial class HkxBinaryReader
{
    #region B-Spline Evaluation

    private static int FindKnotSpan(int degree, float t, int numCp, float[] knots) =>
        SplineFormat.FindKnotSpan(degree, t, numCp, knots);

    private static float EvalBSplineScalar(int span, int degree, float t, float[] knots, List<float> cps) =>
        SplineFormat.Evaluate(span, degree, t, knots, cps);

    private static Quaternion EvalBSplineQuat(int span, int degree, float t, float[] knots, List<Quaternion> cps) =>
        SplineFormat.Evaluate(span, degree, t, knots, cps);

    #endregion

    #region Primitive Helpers

    private static bool CanRead(byte[] data, int off, int size) =>
        off >= 0 && size >= 0 && (long)off + size <= data.Length;

    private static int Align(int v, int a) { int r = v % a; return r == 0 ? v : v + (a - r); }

    private static float ReadF32(byte[] data, int off) =>
        CanRead(data, off, 4) ? BitConverter.ToSingle(data, off) : 0f;

    private static int ReadI32(byte[] data, int off) =>
        CanRead(data, off, 4) ? BitConverter.ToInt32(data, off) : 0;

    private static int SafeReadI32(byte[] data, int off) =>
        CanRead(data, off, 4) ? BitConverter.ToInt32(data, off) : 0;

    private static float Read8BitScalar(byte[] data, ref int off, float mn, float mx)
    {
        if (!CanRead(data, off, 1)) return mn;
        float v = mn + (mx - mn) * (data[off] / 255f);
        off++;
        return v;
    }

    private static float Read16BitScalar(byte[] data, ref int off, float mn, float mx)
    {
        if (!CanRead(data, off, 2)) return mn;
        float v = mn + (mx - mn) * (BitConverter.ToUInt16(data, off) / 65535f);
        off += 2;
        return v;
    }

    private static string ReadNullTermString(byte[] data, int off, int maxLen)
    {
        if (!CanRead(data, off, 1)) return "";
        int end = off;
        int limit = Math.Min(off + maxLen, data.Length);
        while (end < limit && data[end] != 0) end++;
        return Encoding.ASCII.GetString(data, off, end - off);
    }

    #endregion

}
