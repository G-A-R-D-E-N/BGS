using System.Collections.Generic;
using System.Text.Json;
using OpenCommonwealth.Services.Archive;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class ScanReportTests
{
    private static JsonElement Parse(ScanReport report) =>
        JsonDocument.Parse(report.ToJson()).RootElement;

    [Fact]
    public void SerializesModeContextSummaryFindingsAndExitCode()
    {
        var report = new ScanReport(
            "scan-clips",
            new ScanContext("C:/mo2", "Play", "C:/game/Data", 3),
            new Dictionary<string, int> { ["checked"] = 5, ["broken"] = 1, ["missingClips"] = 2, ["unreadable"] = 0 },
            new[]
            {
                new ScanFinding("missing-clip-animations", "meshes/actors/x/behaviors/y.hkx", "loose:MyMod",
                    new[] { "clip 'c' plays 'a' - resolves nowhere", "clip 'd' plays 'b' - resolves nowhere" }),
            },
            1);

        var root = Parse(report);
        Assert.Equal("scan-clips", root.GetProperty("mode").GetString());
        Assert.Equal(1, root.GetProperty("exitCode").GetInt32());

        var ctx = root.GetProperty("context");
        Assert.Equal("C:/mo2", ctx.GetProperty("target").GetString());
        Assert.Equal("Play", ctx.GetProperty("profile").GetString());
        Assert.Equal("C:/game/Data", ctx.GetProperty("dataFolder").GetString());
        Assert.Equal(3, ctx.GetProperty("modRoots").GetInt32());

        var summary = root.GetProperty("summary");
        Assert.Equal(5, summary.GetProperty("checked").GetInt32());
        Assert.Equal(2, summary.GetProperty("missingClips").GetInt32());

        var findings = root.GetProperty("findings");
        Assert.Equal(1, findings.GetArrayLength());
        var finding = findings[0];
        Assert.Equal("missing-clip-animations", finding.GetProperty("kind").GetString());
        Assert.Equal("meshes/actors/x/behaviors/y.hkx", finding.GetProperty("path").GetString());
        Assert.Equal("loose:MyMod", finding.GetProperty("source").GetString());
        Assert.Equal(2, finding.GetProperty("details").GetArrayLength());
        Assert.Equal("clip 'c' plays 'a' - resolves nowhere", finding.GetProperty("details")[0].GetString());
    }

    [Fact]
    public void OmitsNullSourceAndNullContextFields()
    {
        var report = new ScanReport(
            "scan-modlist",
            new ScanContext("C:/mo2", null, null, 0),
            new Dictionary<string, int>(),
            new[] { new ScanFinding("unreadable-character", "x/characters/y.hkx", null, new[] { "boom" }) },
            1);

        var root = Parse(report);
        Assert.False(root.GetProperty("findings")[0].TryGetProperty("source", out _));

        var ctx = root.GetProperty("context");
        Assert.False(ctx.TryGetProperty("profile", out _));
        Assert.False(ctx.TryGetProperty("dataFolder", out _));
        Assert.Equal("C:/mo2", ctx.GetProperty("target").GetString());
    }

    [Fact]
    public void CleanScanHasZeroExitAndNoFindings()
    {
        var report = new ScanReport(
            "scan-archives",
            new ScanContext("C:/root", null, null, 0),
            new Dictionary<string, int> { ["archives"] = 4, ["scanned"] = 120, ["corrupt"] = 0 },
            new List<ScanFinding>(),
            0);

        var root = Parse(report);
        Assert.Equal(0, root.GetProperty("exitCode").GetInt32());
        Assert.Empty(root.GetProperty("findings").EnumerateArray());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("corrupt").GetInt32());
    }
}
