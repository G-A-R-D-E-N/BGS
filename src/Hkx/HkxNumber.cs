using System;
using System.Globalization;

namespace OpenCommonwealth.Services.Hkx;

public static class HkxNumber
{

    public static string Text(float value) => Text((double)value);

    public static string Text(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";

        bool negative = value < 0 || (value == 0 && double.IsNegative(value));
        double magnitude = Math.Abs(value);
        if (magnitude == 0) return negative ? "-0.0" : "0.0";

        var (digits, point) = Shortest(magnitude);

        string body = magnitude >= 1e-3 && magnitude < 1e7
            ? Plain(digits, point)
            : Scientific(digits, point);

        return negative ? "-" + body : body;
    }

    private static (string Digits, int Point) Shortest(double magnitude)
    {
        string text = magnitude.ToString("R", CultureInfo.InvariantCulture);

        int exponent = 0;
        int e = text.IndexOf('E');
        if (e >= 0)
        {
            exponent = int.Parse(text[(e + 1)..], CultureInfo.InvariantCulture);
            text = text[..e];
        }

        int dot = text.IndexOf('.');
        int whole = dot < 0 ? text.Length : dot;
        string digits = dot < 0 ? text : text.Remove(dot, 1);

        int leading = 0;
        while (leading < digits.Length - 1 && digits[leading] == '0') leading++;
        digits = digits[leading..];
        whole -= leading;

        digits = digits.TrimEnd('0');
        if (digits.Length == 0) digits = "0";

        return (digits, whole + exponent);
    }

    private static string Plain(string digits, int point)
    {
        if (point <= 0) return "0." + new string('0', -point) + digits;
        if (point >= digits.Length) return digits + new string('0', point - digits.Length) + ".0";
        return digits[..point] + "." + digits[point..];
    }

    private static string Scientific(string digits, int point) =>
        digits[..1] + "." + (digits.Length > 1 ? digits[1..] : "0") +
        "E" + (point - 1).ToString(CultureInfo.InvariantCulture);
}
