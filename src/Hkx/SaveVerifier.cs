using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

/// <summary>
/// Proves that rebuilt bytes semantically match the intended NativeSave.Plan before the
/// source file is touched. Parsing, class signatures, and object count alone cannot detect
/// the wrong-object save bug; this verifier also re-reads the intended values from the
/// rebuilt file at the stable-id-resolved instance and compares them with the plan.
/// </summary>
public static class SaveVerifier
{
    public static void Verify(byte[] sourceBytes, byte[] rebuiltBytes, NativeSave.Plan plan)
    {
        PackfileObjects rebuilt;
        try
        {
            rebuilt = new PackfileObjects(PackfileImage.Read(rebuiltBytes));
        }
        catch (Exception e)
        {
            throw new InvalidDataException("the rebuilt file could not be read: " + e.Message);
        }

        var mismatched = HavokClassTypes.Shipped.SignatureProblems(rebuilt.ClassNames());
        if (mismatched.Count > 0)
            throw new InvalidDataException("rebuilt class signatures do not match: " + mismatched[0]);

        BehaviourGraphModel? model;
        try
        {
            model = NativeGraphModel.From(rebuilt);
        }
        catch (Exception e)
        {
            throw new InvalidDataException("rebuilt bytes do not model as a graph: " + e.Message);
        }
        if (model == null)
            throw new InvalidDataException("rebuilt bytes do not model as a graph");

        // The expected count is derived from the actual source packfile, never from a UI
        // collection that may already represent edited XML.
        PackfileObjects source;
        try
        {
            source = new PackfileObjects(PackfileImage.Read(sourceBytes));
        }
        catch (Exception e)
        {
            throw new InvalidDataException("the source file could not be read: " + e.Message);
        }

        int expected = source.Instances.Count - plan.Gone.Count + plan.Changes.Count(c => c.Added);
        if (rebuilt.Instances.Count != expected)
            throw new InvalidDataException(
                $"rebuilt holds {rebuilt.Instances.Count} objects, expected {expected}");

        // Deleting objects renumbers final ids, so every change's ORIGINAL id is resolved
        // through the stable order: survivors keep source order, adds append after them.
        var survivorIds = Enumerable.Range(NativeGraphModel.FirstId, source.Instances.Count)
            .Where(id => !plan.Gone.Contains(id)).ToList();
        var added = plan.Changes.Where(c => c.Added).ToList();

        int RebuiltIndex(int originalId)
        {
            int at = survivorIds.IndexOf(originalId);
            if (at >= 0) return at;
            int addedAt = added.FindIndex(c => c.Id == originalId);
            return addedAt >= 0 ? source.Instances.Count + addedAt : -1;
        }

        foreach (var change in plan.Changes.Where(c => !c.Added))
        {
            int index = RebuiltIndex(change.Id);
            if (index < 0 || index >= rebuilt.Instances.Count ||
                rebuilt.Instances[index].ClassName != change.ClassName)
                throw new InvalidDataException($"{change} has no surviving object in the rebuilt file");
            if (!IntendedValueLanded(rebuilt, index, change, RebuiltIndex))
                throw new InvalidDataException($"{change} did not land on the intended object");
        }

        foreach (var change in added)
        {
            int index = RebuiltIndex(change.Id);
            if (index < 0 || index >= rebuilt.Instances.Count ||
                rebuilt.Instances[index].ClassName != change.ClassName)
                throw new InvalidDataException($"added {change} is missing from the rebuilt file");
        }
    }

    private static bool IntendedValueLanded(PackfileObjects rebuilt, int index,
                                            NativeSave.Change change, Func<int, int> rebuiltIndex)
    {
        var instance = rebuilt.Instances[index];
        if (instance.ClassName != change.ClassName) return false;

        int? at = rebuilt.FieldAt(instance, change.Field);
        if (at is not int offset) return false;

        var member = HavokClasses.Shipped.Field(change.ClassName, change.Field);
        string type = member?.Type ?? "";

        if (change.Grow)
        {
            var array = rebuilt.ArrayAt(offset);
            return array != null &&
                   int.TryParse(change.Value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                out int wanted) &&
                   array.Count == wanted;
        }

        if (change.InElement)
        {
            var array = rebuilt.ArrayAt(offset);
            return array != null && change.Element < array.Count;
        }

        if (change.Array && change.Text)
            return TextArrayLanded(rebuilt, offset, change);

        if (change.Array && NativeSave.ValueElement(type) is int width and > 0)
            return ValueArrayLanded(rebuilt, offset, change, type, width);

        if (change.Array)
            return RefArrayLanded(rebuilt, offset, change, rebuiltIndex);

        if (change.Ref)
            return RefLanded(rebuilt, offset, change, rebuiltIndex);

        if (change.Text)
        {
            string? read = rebuilt.ReadStringAt(offset);
            return read != null && string.Equals(read, change.Value, StringComparison.Ordinal);
        }

        if (NativeSave.WideFloats(type) is int floats and > 0)
            return WideFloatsLanded(rebuilt, offset, change, floats);

        if (NativeSave.IsWideInteger(type))
            return WideIntLanded(rebuilt, offset, change, type);

        if (type == "real")
        {
            return rebuilt.ReadFloatAt(offset) is float read &&
                   float.TryParse(change.Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                                  out float wanted) &&
                   Math.Abs(read - wanted) <= 1e-4f;
        }

        return NarrowLanded(rebuilt, offset, change, type);
    }

    private static bool WideFloatsLanded(PackfileObjects rebuilt, int offset,
                                         NativeSave.Change change, int floats)
    {
        var read = rebuilt.ReadFloatsAt(offset, floats);
        var intended = Bracketed(change.Value, floats);
        if (read == null || intended == null) return false;
        for (int i = 0; i < floats; i++)
            if (Math.Abs(read[i] - intended[i]) > 1e-4f) return false;
        return true;
    }

    private static bool WideIntLanded(PackfileObjects rebuilt, int offset,
                                      NativeSave.Change change, string type)
    {
        string text = change.Value.Trim();
        if (type == "int64")
            return TryParseInt(text) is long n && rebuilt.ReadLongAt(offset) == n;
        return TryParseUInt(text) is ulong u && rebuilt.ReadULongAt(offset) == u;
    }

    private static bool NarrowLanded(PackfileObjects rebuilt, int offset,
                                     NativeSave.Change change, string type)
    {
        string storage = NumberCodecs.Underlying(type);
        int width = storage switch
        {
            "int8" or "uint8" or "char" or "bool" or "enum" => 1,
            "int16" or "uint16" => 2,
            "int32" or "uint32" => 4,
            _ => 0,
        };
        if (width == 0) return false;

        int? read = rebuilt.ReadNarrowAt(offset, width);
        if (read == null) return false;

        if (storage == "bool")
        {
            string text = change.Value.Trim();
            if (text is "true" or "1") return read == 1;
            if (text is "false" or "0") return read == 0;
            return false;
        }

        if (TryParseInt(change.Value) is not long n) return false;
        return storage == "uint32"
            ? (long)(uint)read.Value == n
            : read.Value == n;
    }

    private static bool TextArrayLanded(PackfileObjects rebuilt, int offset, NativeSave.Change change)
    {
        var intended = change.Value.Length == 0
            ? Array.Empty<string>()
            : change.Value.Split('\0');
        var read = rebuilt.ReadStringArrayAt(offset);
        if (read == null || read.Count != intended.Length) return false;
        for (int i = 0; i < intended.Length; i++)
            if (!string.Equals(read[i], intended[i], StringComparison.Ordinal)) return false;
        return true;
    }

    private static bool ValueArrayLanded(PackfileObjects rebuilt, int offset,
                                         NativeSave.Change change, string type, int width)
    {
        var array = rebuilt.ArrayAt(offset);
        if (array == null) return false;

        string elementType = type[("array of ").Length..];
        var intended = NumberCodecs.ArrayBytes(change.Value, elementType, width);
        if (intended == null || array.Count != intended.Length / width) return false;
        if (array.Count == 0) return true;

        var read = rebuilt.ReadValueArrayAt(offset, width, (data, o) => data.Skip(o).Take(width).ToArray());
        if (read == null) return false;
        for (int e = 0; e < array.Count; e++)
            for (int b = 0; b < width; b++)
                if (read[e][b] != intended[e * width + b]) return false;
        return true;
    }

    private static bool RefArrayLanded(PackfileObjects rebuilt, int offset,
                                       NativeSave.Change change, Func<int, int> rebuiltIndex)
    {
        var array = rebuilt.ArrayAt(offset);
        if (array == null) return false;

        var intended = change.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (array.Count != intended.Length) return false;

        var read = rebuilt.ReadRefArrayAt(offset);
        if (read == null) return false;
        for (int i = 0; i < intended.Length; i++)
        {
            bool wantNull = intended[i] == "null";
            bool isNull = read[i] == null;
            if (wantNull != isNull) return false;
            if (wantNull) continue;
            if (TargetLanded(rebuilt, read[i]!, intended[i], rebuiltIndex) != true) return false;
        }
        return true;
    }

    private static bool RefLanded(PackfileObjects rebuilt, int offset,
                                  NativeSave.Change change, Func<int, int> rebuiltIndex)
    {
        var read = rebuilt.ReadRefAt(offset, out bool wasNull);
        if (change.Value == "null") return wasNull;
        if (read == null) return false;
        return TargetLanded(rebuilt, read, change.Value, rebuiltIndex) == true;
    }

    private static bool? TargetLanded(PackfileObjects rebuilt, PackfileObjects.Instance read,
                                      string intendedValue, Func<int, int> rebuiltIndex)
    {
        if (intendedValue.Length <= 1 || intendedValue[0] != '#') return null;
        if (!int.TryParse(intendedValue[1..], NumberStyles.Integer, CultureInfo.InvariantCulture,
                          out int target)) return null;
        int targetIndex = rebuiltIndex(target);
        return targetIndex >= 0 && targetIndex < rebuilt.Instances.Count &&
               read.Offset == rebuilt.Instances[targetIndex].Offset;
    }

    private static float[]? Bracketed(string value, int wanted)
    {
        var numbers = new List<float>();
        foreach (string token in value.Replace('(', ' ').Replace(')', ' ')
                                      .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ||
                float.IsNaN(f) || float.IsInfinity(f))
                return null;
            numbers.Add(f);
        }
        return numbers.Count == wanted ? numbers.ToArray() : null;
    }

    private static long? TryParseInt(string value)
    {
        string text = value.Trim();
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n)) return n;
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
               long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)
            ? n
            : null;
    }

    private static ulong? TryParseUInt(string value)
    {
        string text = value.Trim();
        if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong n)) return n;
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
               ulong.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)
            ? n
            : null;
    }
}
