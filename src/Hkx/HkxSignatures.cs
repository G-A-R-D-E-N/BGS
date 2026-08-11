using System;

namespace OpenCommonwealth.Services.Hkx;

internal static class HkxSignatures
{
    public static string Of(string className)
    {
        var layout = HavokClassTypes.Shipped[className] ??
            throw new InvalidOperationException($"no shipped class definition for {className}");
        return $"0x{layout.Signature:x}";
    }
}
