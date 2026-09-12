using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OpenCommonwealth.Services.Hkx;

internal sealed class BehaviourCompareSession
{
    internal sealed record BehaviourDiffFilter(
        BehaviourDiff.Kind? Kind = null,
        string ObjectClass = "")
    {
        internal bool IsActive => Kind.HasValue || ObjectClass.Trim().Length > 0;

        internal bool Matches(BehaviourDiff.Line line)
        {
            string objectClass = ObjectClass.Trim();
            return (!Kind.HasValue || line.Kind == Kind.Value) &&
                   (objectClass.Length == 0 || line.Class == objectClass);
        }
    }

    internal sealed record BehaviourDiffExportLine(
        BehaviourDiff.Kind Kind,
        string Class,
        string Where,
        string Was,
        string Now);

    internal sealed record BehaviourDiffExport(
        IReadOnlyList<BehaviourDiffExportLine> Differences,
        int Added,
        int Removed,
        int Changed,
        int OriginalCount,
        bool IsFiltered);

    internal sealed record Outcome(bool Stale, BehaviourDiff.Result? Value, string Error)
    {
        internal bool Failed => Error.Length > 0;
    }

    private readonly Func<long> _currentRevision;

    internal BehaviourCompareSession(Func<long> currentRevision) =>
        _currentRevision = currentRevision;

    internal Func<string, string>? ReadComparableForTest { get; set; }

    internal async Task<Outcome> Compare(string mine, string otherPath, long revision)
    {
        BehaviourDiff.Result result;
        try
        {
            result = await Task.Run(() => CompareNow(mine, otherPath, ReadComparableForTest));
        }
        catch (Exception error)
        {
            return revision != _currentRevision()
                ? new Outcome(true, null, "")
                : new Outcome(false, null, error.Message.Split('\n')[0]);
        }

        return revision != _currentRevision()
            ? new Outcome(true, null, "")
            : new Outcome(false, result, "");
    }

    internal static BehaviourDiff.Result CompareNow(
        string mine,
        string otherPath,
        Func<string, string>? readComparable = null)
    {
        string theirs = (readComparable ?? ReadComparable)(otherPath);
        if (theirs.Length == 0)
            throw new InvalidOperationException(
                "this file's classes are not ones this build describes");

        return CompareText(mine, theirs);
    }

    internal static BehaviourDiff.Result CompareText(string mine, string theirs) =>
        BehaviourDiff.Compare(RepackCheck.Take(mine), RepackCheck.Take(theirs));

    internal static BehaviourDiff.Result ApplyFilter(
        BehaviourDiff.Result source,
        BehaviourDiffFilter filter)
    {
        var result = new BehaviourDiff.Result();
        foreach (var line in OrderedLines(source.Lines).Where(filter.Matches))
            result.Lines.Add(line);
        return result;
    }

    internal static BehaviourDiffExport CreateExport(
        BehaviourDiff.Result source,
        BehaviourDiffFilter filter)
    {
        var lines = ApplyFilter(source, filter)
            .Lines
            .Select(line => new BehaviourDiffExportLine(
                line.Kind, line.Class, line.Where, line.Was, line.Now))
            .ToList();

        return new BehaviourDiffExport(
            lines,
            lines.Count(line => line.Kind == BehaviourDiff.Kind.Added),
            lines.Count(line => line.Kind == BehaviourDiff.Kind.Removed),
            lines.Count(line => line.Kind == BehaviourDiff.Kind.Changed),
            source.Lines.Count,
            filter.IsActive);
    }

    internal static string ExportJson(BehaviourDiffExport export) =>
        JsonSerializer.Serialize(export, ExportJsonOptions) + "\n";

    internal static string ExportText(BehaviourDiffExport export)
    {
        if (export.Differences.Count == 0)
            return export.OriginalCount == 0 || !export.IsFiltered
                ? "No differences.\n"
                : "No differences match the current filter.\n";

        string summary = $"{export.Added} added, {export.Removed} removed, " +
                         $"{export.Changed} value{(export.Changed == 1 ? "" : "s")} changed";
        return summary + "\n\n" + string.Join("\n", export.Differences.Select(FormatExportLine)) + "\n";
    }

    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static IEnumerable<BehaviourDiff.Line> OrderedLines(
        IEnumerable<BehaviourDiff.Line> lines) =>
        lines.OrderBy(line => KindOrder(line.Kind))
             .ThenBy(line => line.Class, StringComparer.Ordinal)
             .ThenBy(line => line.Where, StringComparer.Ordinal)
             .ThenBy(line => line.Was, StringComparer.Ordinal)
             .ThenBy(line => line.Now, StringComparer.Ordinal);

    private static int KindOrder(BehaviourDiff.Kind kind) => kind switch
    {
        BehaviourDiff.Kind.Changed => 0,
        BehaviourDiff.Kind.Removed => 1,
        BehaviourDiff.Kind.Added => 2,
        _ => 3,
    };

    private static string FormatExportLine(BehaviourDiffExportLine line) => line.Kind switch
    {
        BehaviourDiff.Kind.Added => $"added {line.Class} {line.Where}",
        BehaviourDiff.Kind.Removed => $"removed {line.Class} {line.Where}",
        _ => $"{line.Class}.{line.Where}: {line.Was} -> {line.Now}",
    };

    private static string ReadComparable(string path)
    {
        try
        {
            var bytes = InputFilePolicy.ReadHkx(path);
            var objects = new PackfileObjects(PackfileImage.Read(bytes));

            if (HavokClassTypes.Shipped.SignatureProblems(objects.ClassNames()).Count == 0)
                return NativeXml.From(bytes);
        }
        catch (Exception)
        {
        }

        return "";
    }
}
