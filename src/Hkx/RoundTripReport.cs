using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public enum RoundTripLossKind
{
    UnsupportedClass,
    UnsupportedAnimation,
    UnsupportedEnumValue,
    DroppedField,
    DroppedReference,
    ConversionError,
}

public sealed record RoundTripLoss(
    RoundTripLossKind Kind,
    long? ObjectId,
    string Member,
    string Message);

/// <summary>
/// Everything BGS can prove would either block a native save or lose information during an
/// explicit semantic conversion. Raw-file analysis stays conservative: it reports only facts
/// present in the file itself. Conversion-specific losses are added from HavokConversionResult.
/// </summary>
public sealed class RoundTripReport
{
    private readonly List<RoundTripLoss> _losses = new();

    public IReadOnlyList<RoundTripLoss> Losses => _losses;
    public int Count => _losses.Count;
    public bool HasLosses => _losses.Count > 0;

    public int UnsupportedClasses => CountOf(RoundTripLossKind.UnsupportedClass);
    public int UnsupportedAnimations => CountOf(RoundTripLossKind.UnsupportedAnimation);
    public int UnsupportedEnumValues => CountOf(RoundTripLossKind.UnsupportedEnumValue);
    public int DroppedFields => CountOf(RoundTripLossKind.DroppedField);
    public int DroppedReferences => CountOf(RoundTripLossKind.DroppedReference);
    public int ConversionErrors => CountOf(RoundTripLossKind.ConversionError);

    public int CountOf(RoundTripLossKind kind) => _losses.Count(loss => loss.Kind == kind);

    public void Add(RoundTripLoss loss)
    {
        ArgumentNullException.ThrowIfNull(loss);
        if (!_losses.Contains(loss)) _losses.Add(loss);
    }

    public void AddRange(IEnumerable<RoundTripLoss> losses)
    {
        ArgumentNullException.ThrowIfNull(losses);
        foreach (var loss in losses) Add(loss);
    }

    public void Merge(RoundTripReport other)
    {
        ArgumentNullException.ThrowIfNull(other);
        AddRange(other.Losses);
    }

    public override string ToString()
    {
        if (!HasLosses) return "round-trip safe";

        var parts = new List<string>();
        AddCount(parts, UnsupportedClasses, "unsupported class");
        AddCount(parts, UnsupportedAnimations, "unsupported animation");
        AddCount(parts, UnsupportedEnumValues, "unsupported enum value");
        AddCount(parts, DroppedFields, "dropped field");
        AddCount(parts, DroppedReferences, "dropped reference");
        AddCount(parts, ConversionErrors, "conversion error");
        return $"{Count} round-trip issue{(Count == 1 ? "" : "s")}: {string.Join(", ", parts)}";
    }

    public static RoundTripReport ForFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ForBytes(InputFilePolicy.ReadHkx(path));
    }

    public static RoundTripReport ForBytes(byte[] hkx)
    {
        ArgumentNullException.ThrowIfNull(hkx);
        var report = new RoundTripReport();

        // A class that is absent from the shipped schema, or whose signature disagrees with it,
        // is exactly the condition the native writer already refuses before writing. Keep the
        // report aligned with that gate rather than trying to infer support from class names.
        try
        {
            var objects = new PackfileObjects(PackfileImage.Read(hkx));
            foreach (string problem in HavokClassTypes.Shipped.SignatureProblems(objects.ClassNames()))
                report.Add(new RoundTripLoss(RoundTripLossKind.UnsupportedClass, null, "", problem));
        }
        catch (Exception error) when (IsFormatFailure(error))
        {
            // Unreadable/malformed files are reported by the normal reader and project/scan
            // diagnostics. They are not silently reclassified as a round-trip loss here.
        }

        // TryReadAnimation deliberately returns false while still filling AnimationClass when a
        // real animation subtype is present but its decoder is not implemented. A valid behavior
        // file has an empty animation class and therefore contributes nothing here.
        try
        {
            if (!new HkxBinaryReader().TryReadAnimation(hkx, out var animation) &&
                animation.HasUnsupportedAnimation)
                report.Add(new RoundTripLoss(
                    RoundTripLossKind.UnsupportedAnimation,
                    null,
                    "",
                    $"animation class {animation.AnimationClass} is not decoded; only " +
                    $"{HkxAnimationData.SupportedAnimationClasses} are supported"));
        }
        catch (Exception error) when (IsFormatFailure(error))
        {
            // Same rule as above: parsing failures belong to the ordinary unreadable path.
        }

        return report;
    }

    public static RoundTripReport FromConversion(HavokConversionResult conversion)
    {
        ArgumentNullException.ThrowIfNull(conversion);
        var report = new RoundTripReport();

        foreach (var diagnostic in conversion.Diagnostics)
        {
            RoundTripLossKind? kind = ConversionKind(diagnostic);
            if (kind == null) continue;
            report.Add(new RoundTripLoss(kind.Value, diagnostic.ObjectId,
                                         diagnostic.Member ?? "", diagnostic.Message));
        }

        return report;
    }

    private static RoundTripLossKind? ConversionKind(HavokConversionDiagnostic diagnostic)
    {
        string message = diagnostic.Message;
        if (message.StartsWith("no conversion rule exists for ", StringComparison.Ordinal))
            return RoundTripLossKind.UnsupportedClass;
        if (message.StartsWith("field was explicitly dropped", StringComparison.Ordinal))
            return RoundTripLossKind.DroppedField;
        if (message.StartsWith("reference to unsupported object ", StringComparison.Ordinal))
            return RoundTripLossKind.DroppedReference;
        if (message.StartsWith("enum value ", StringComparison.Ordinal) ||
            message.StartsWith("enum mapping expected ", StringComparison.Ordinal))
            return RoundTripLossKind.UnsupportedEnumValue;

        // The root diagnostic repeats the unsupported-object fact already emitted for that
        // object. Do not make one unsupported class look like two separate losses.
        if (message.StartsWith("the source root object is unsupported", StringComparison.Ordinal))
            return null;

        return diagnostic.Level == HavokConversionDiagnosticLevel.Error
            ? RoundTripLossKind.ConversionError
            : null;
    }

    private static bool IsFormatFailure(Exception error) =>
        error is InvalidDataException or InvalidOperationException or NotSupportedException or ArgumentException;

    private static void AddCount(List<string> parts, int count, string label)
    {
        if (count > 0) parts.Add($"{count} {label}{(count == 1 ? "" : "s")}");
    }
}
