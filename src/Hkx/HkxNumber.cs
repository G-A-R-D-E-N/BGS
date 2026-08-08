using System;
using System.Globalization;

namespace OpenCommonwealth.Services.Hkx;

// A float spelled the way hkxpack spells it.
//
// hkxpack is Java, and it writes a float by widening it to a double and handing it to
// `Double.toString`. That is not the same as the shortest text that reads back as the same float,
// which is what .NET gives: 0.1f is "0.1" one way and "0.10000000149011612" the other, and 1.0f is
// "1" against "1.0". Both name the same bits. Only one of them is what is in the file.
//
// Which one is wanted depends on who is asking. A properties panel should show "0.1", because that
// is what a person typed and what they will type again. A reading being compared against hkxpack's
// own text has to say "0.10000000149011612" or it disagrees with the file on every float in it. So
// this lives beside the shortest form rather than replacing it.
//
// Java's rule, from the `Double.toString` documentation: take the shortest run of digits that tells
// this double apart from its neighbours, then write it plainly when the value is at least 10^-3 and
// below 10^7, and in scientific notation otherwise. Plain always keeps a digit after the point, so
// one is "1.0" rather than "1".
public static class HkxNumber
{
    /// The widening is the whole point. A float carries about seven digits and the text carries
    /// seventeen, because the digits are the double's, not the float's.
    public static string Text(float value) => Text((double)value);

    public static string Text(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";

        // Negative zero is a real value in these files and is written as such. Comparing it against
        // zero is what a reading that folded the sign away would do.
        bool negative = value < 0 || (value == 0 && double.IsNegative(value));
        double magnitude = Math.Abs(value);
        if (magnitude == 0) return negative ? "-0.0" : "0.0";

        var (digits, point) = Shortest(magnitude);

        string body = magnitude >= 1e-3 && magnitude < 1e7
            ? Plain(digits, point)
            : Scientific(digits, point);

        return negative ? "-" + body : body;
    }

    /// The shortest digits that read back as this double, and where the decimal point sits relative
    /// to the front of them: the value is 0.digits times ten to the point.
    ///
    /// .NET already works the digits out, since round tripping is what its own shortest form is for.
    /// Taking them from it rather than deriving them again means there is one implementation of the
    /// hard part and this is only deciding where to put the point.
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
