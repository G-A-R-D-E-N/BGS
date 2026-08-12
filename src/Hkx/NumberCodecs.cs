using System;
using System.Globalization;

namespace OpenCommonwealth.Services.Hkx;

public static class NumberCodecs
{
    public static string Underlying(string type) =>
        type.StartsWith("enum of ", StringComparison.Ordinal)
            ? type["enum of ".Length..]
            : type;

    public static bool Parses(string text, string type)
    {
        type = Underlying(type);
        string value = text.Trim();
        if (type == "bool") return value is "true" or "false" or "1" or "0";
        if (type == "real")
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                                  out float f) && !float.IsNaN(f) && !float.IsInfinity(f);

        if (IsSigned64(type)) return TrySigned(value, out long s) && InSigned(type, s);
        if (IsUnsigned64(type)) return TryUnsigned(value, out ulong u) && InUnsigned(type, u);

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) &&
            !(value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
              long.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)))
            return false;

        return type switch
        {
            "int8" => n is >= -128 and <= 127,
            "uint8" or "char" or "enum" => n is >= 0 and <= 255,
            "int16" => n is >= short.MinValue and <= short.MaxValue,
            "uint16" => n is >= 0 and <= ushort.MaxValue,
            "int32" => n is >= int.MinValue and <= int.MaxValue,
            "uint32" => n is >= 0 and <= uint.MaxValue,
            _ => false,
        };
    }

    public static byte[]? ArrayBytes(string value, string elementType, int width)
    {
        var tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var run = new byte[tokens.Length * width];

        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            if (elementType == "real")
            {
                if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture,
                                    out float f) || float.IsNaN(f) || float.IsInfinity(f))
                    return null;
                BitConverter.GetBytes(f).CopyTo(run, i * 4);
                continue;
            }
            if (elementType == "bool")
            {
                if (token is "true" or "1") { run[i] = 1; continue; }
                if (token is "false" or "0") { run[i] = 0; continue; }
                return null;
            }
            if (!Parses(token, elementType)) return null;
            WriteOne(run, i * width, elementType, token);
        }
        return run;
    }

    public static void WriteScalar(byte[] data, int at, string type, string value)
    {
        if (!Parses(value, type))
            throw new InvalidOperationException(
                $"'{value}' is not a {type}, so it cannot be written into a {type} field.");
        WriteOne(data, at, Underlying(type), value.Trim());
    }

    private static void WriteOne(byte[] data, int at, string type, string value)
    {
        switch (type)
        {
            case "int8": data[at] = unchecked((byte)(sbyte)Signed(value)); break;
            case "uint8" or "char": data[at] = (byte)Signed(value); break;
            case "int16": BitConverter.GetBytes((short)Signed(value)).CopyTo(data, at); break;
            case "uint16": BitConverter.GetBytes((ushort)Signed(value)).CopyTo(data, at); break;
            case "int32": BitConverter.GetBytes((int)Signed(value)).CopyTo(data, at); break;
            case "uint32": BitConverter.GetBytes((uint)Signed(value)).CopyTo(data, at); break;
            case "int64": BitConverter.GetBytes(Signed(value)).CopyTo(data, at); break;
            case "uint64" or "ulong": BitConverter.GetBytes(Unsigned(value)).CopyTo(data, at); break;
            case "bool": data[at] = (byte)(value is "true" or "1" ? 1 : 0); break;
            case "enum": data[at] = (byte)Signed(value); break;
            default: throw new InvalidOperationException($"no codec for {type}");
        }
    }

    private static bool IsSigned64(string type) => type == "int64";
    private static bool IsUnsigned64(string type) => type is "uint64" or "ulong";

    private static bool TrySigned(string value, out long n) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ||
        (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
         long.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n));

    private static bool TryUnsigned(string value, out ulong n) =>
        ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ||
        (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
         ulong.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n));

    private static bool InSigned(string type, long n) => type switch
    {
        "int8" => n is >= -128 and <= 127,
        "int16" => n is >= short.MinValue and <= short.MaxValue,
        "int32" => n is >= int.MinValue and <= int.MaxValue,
        "int64" => true,
        _ => false,
    };

    private static bool InUnsigned(string type, ulong n) => type switch
    {
        "uint8" or "char" => n <= 255,
        "uint16" => n <= ushort.MaxValue,
        "uint32" => n <= uint.MaxValue,
        "uint64" or "ulong" => true,
        _ => false,
    };

    private static long Signed(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? n
        : long.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static ulong Unsigned(string value) =>
        ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong n) ? n
        : ulong.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
