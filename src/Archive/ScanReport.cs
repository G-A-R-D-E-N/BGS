using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCommonwealth.Services.Archive;

public sealed record ScanFinding(
    string Kind,
    string Path,
    string? Source,
    IReadOnlyList<string> Details);

public sealed record ScanContext(
    string Target,
    string? Profile,
    string? DataFolder,
    int ModRoots);

public sealed record ScanReport(
    string Mode,
    ScanContext Context,
    IReadOnlyDictionary<string, int> Summary,
    IReadOnlyList<ScanFinding> Findings,
    int ExitCode)
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyOrder(-1)]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);
}
