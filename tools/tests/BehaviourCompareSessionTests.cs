using System;
using System.Threading.Tasks;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class BehaviourCompareSessionTests
{
    [Fact]
    public void CompareTextFindsChangedFields()
    {
        string before = Clip("Walk", "1.0");
        string after = Clip("Run", "1.5");

        var result = BehaviourCompareSession.CompareText(before, after);

        Assert.False(result.Identical);
        Assert.Equal(2, result.Changed);
        Assert.Contains(result.Lines, line =>
            line.Kind == BehaviourDiff.Kind.Changed &&
            line.Where == "name" && line.Was == "Walk" && line.Now == "Run");
        Assert.Contains(result.Lines, line =>
            line.Kind == BehaviourDiff.Kind.Changed &&
            line.Where == "playbackSpeed" && line.Was == "1.0" && line.Now == "1.5");
    }

    [Fact]
    public void CompareNowUsesTheProvidedReader()
    {
        string pathSeen = "";

        var result = BehaviourCompareSession.CompareNow(
            Clip("Walk", "1.0"),
            "other.hkx",
            path =>
            {
                pathSeen = path;
                return Clip("Run", "1.0");
            });

        Assert.Equal("other.hkx", pathSeen);
        Assert.Single(result.Lines);
        Assert.Equal("name", result.Lines[0].Where);
    }

    [Fact]
    public async Task CompareSuppressesAResultFromAnOldDocumentRevision()
    {
        long revision = 10;
        var session = new BehaviourCompareSession(() => revision)
        {
            ReadComparableForTest = _ =>
            {
                revision = 11;
                return Clip("Run", "1.0");
            },
        };

        var outcome = await session.Compare(Clip("Walk", "1.0"), "other.hkx", revision: 10);

        Assert.True(outcome.Stale);
        Assert.False(outcome.Failed);
        Assert.Null(outcome.Value);
    }

    [Fact]
    public async Task CompareContainsReaderFailures()
    {
        var session = new BehaviourCompareSession(() => 4)
        {
            ReadComparableForTest = _ => throw new InvalidOperationException("fixture failed\nsecond line"),
        };

        var outcome = await session.Compare(Clip("Walk", "1.0"), "other.hkx", revision: 4);

        Assert.False(outcome.Stale);
        Assert.True(outcome.Failed);
        Assert.Null(outcome.Value);
        Assert.Equal("fixture failed", outcome.Error);
    }

    [Fact]
    public async Task CompareSuppressesAFailureFromAnOldDocumentRevision()
    {
        long revision = 20;
        var session = new BehaviourCompareSession(() => revision)
        {
            ReadComparableForTest = _ =>
            {
                revision = 21;
                throw new InvalidOperationException("the old file could not be read");
            },
        };

        var outcome = await session.Compare(Clip("Walk", "1.0"), "other.hkx", revision: 20);

        Assert.True(outcome.Stale);
        Assert.False(outcome.Failed);
        Assert.Equal("", outcome.Error);
        Assert.Null(outcome.Value);
    }

    [Fact]
    public async Task CompareRefusesAnUnreadableOrUnsupportedFile()
    {
        var session = new BehaviourCompareSession(() => 7)
        {
            ReadComparableForTest = _ => "",
        };

        var outcome = await session.Compare(Clip("Walk", "1.0"), "other.hkx", revision: 7);

        Assert.True(outcome.Failed);
        Assert.Contains("classes are not ones this build describes", outcome.Error);
    }

    [Fact]
    public async Task CompareReturnsTheResultAtTheCurrentRevision()
    {
        var session = new BehaviourCompareSession(() => 3)
        {
            ReadComparableForTest = _ => Clip("Run", "1.0"),
        };

        var outcome = await session.Compare(Clip("Walk", "1.0"), "other.hkx", revision: 3);

        Assert.False(outcome.Stale);
        Assert.False(outcome.Failed);
        Assert.NotNull(outcome.Value);
        Assert.Single(outcome.Value!.Lines);
    }

    [Fact]
    public void DiffFilterWithoutCriteriaKeepsEveryLine()
    {
        var source = FixtureResult();

        var filtered = BehaviourCompareSession.ApplyFilter(
            source, new BehaviourCompareSession.BehaviourDiffFilter());

        Assert.Equal(source.Lines.Count, filtered.Lines.Count);
    }

    [Theory]
    [InlineData(BehaviourDiff.Kind.Added, 1)]
    [InlineData(BehaviourDiff.Kind.Removed, 1)]
    [InlineData(BehaviourDiff.Kind.Changed, 2)]
    public void DiffFilterSelectsOneChangeKind(BehaviourDiff.Kind kind, int expected)
    {
        var filtered = BehaviourCompareSession.ApplyFilter(
            FixtureResult(), new BehaviourCompareSession.BehaviourDiffFilter(kind));

        Assert.Equal(expected, filtered.Lines.Count);
        Assert.All(filtered.Lines, line => Assert.Equal(kind, line.Kind));
    }

    [Fact]
    public void DiffFilterSelectsAnObjectClass()
    {
        var filtered = BehaviourCompareSession.ApplyFilter(
            FixtureResult(), new BehaviourCompareSession.BehaviourDiffFilter(
                ObjectClass: "hkbClipGenerator"));

        Assert.Equal(3, filtered.Lines.Count);
        Assert.All(filtered.Lines, line => Assert.Equal("hkbClipGenerator", line.Class));
    }

    [Fact]
    public void DiffFilterComposesChangeKindAndObjectClass()
    {
        var filtered = BehaviourCompareSession.ApplyFilter(
            FixtureResult(), new BehaviourCompareSession.BehaviourDiffFilter(
                BehaviourDiff.Kind.Changed, "hkbClipGenerator"));

        Assert.Equal(2, filtered.Lines.Count);
        Assert.Equal(new[] { "name", "playbackSpeed" },
            filtered.Lines.Select(line => line.Where));
    }

    [Fact]
    public void DiffExportReportsZeroMatchesForAnActiveFilter()
    {
        var export = BehaviourCompareSession.CreateExport(
            FixtureResult(), new BehaviourCompareSession.BehaviourDiffFilter(
                ObjectClass: "hkbMissingClass"));

        Assert.Empty(export.Differences);
        Assert.Equal(4, export.OriginalCount);
        Assert.True(export.IsFiltered);
        Assert.Equal("No differences match the current filter.\n",
            BehaviourCompareSession.ExportText(export));
    }

    [Fact]
    public void DiffExportReportsIdenticalFilesOnlyForAnEmptyUnfilteredResult()
    {
        var export = BehaviourCompareSession.CreateExport(
            new BehaviourDiff.Result(), new BehaviourCompareSession.BehaviourDiffFilter());

        Assert.False(export.IsFiltered);
        Assert.Equal("No differences.\n", BehaviourCompareSession.ExportText(export));
    }

    [Fact]
    public void DiffExportsHaveDeterministicJsonAndTextOrdering()
    {
        var source = new BehaviourDiff.Result();
        source.Lines.Add(new BehaviourDiff.Line(
            BehaviourDiff.Kind.Added, "hkbStateMachine", "#200", "", "name=State"));
        source.Lines.Add(new BehaviourDiff.Line(
            BehaviourDiff.Kind.Changed, "hkbClipGenerator", "playbackSpeed", "1.0", "1.5"));
        source.Lines.Add(new BehaviourDiff.Line(
            BehaviourDiff.Kind.Removed, "hkbClipGenerator", "Walk", "name=Walk", ""));
        source.Lines.Add(new BehaviourDiff.Line(
            BehaviourDiff.Kind.Changed, "hkbClipGenerator", "name", "Walk", "Run"));

        var export = BehaviourCompareSession.CreateExport(
            source, new BehaviourCompareSession.BehaviourDiffFilter());
        string json = BehaviourCompareSession.ExportJson(export);
        string text = BehaviourCompareSession.ExportText(export);

        Assert.Equal(json, BehaviourCompareSession.ExportJson(export));
        Assert.Equal(text, BehaviourCompareSession.ExportText(export));
        Assert.Equal(
            new[] { "name", "playbackSpeed", "Walk", "#200" },
            export.Differences.Select(line => line.Where));
        Assert.Contains("\"kind\": \"changed\"", json);
        Assert.Contains("hkbClipGenerator.name: Walk -> Run", text);
        Assert.Contains("added hkbStateMachine #200", text);
    }

    private static BehaviourDiff.Result FixtureResult()
    {
        var result = new BehaviourDiff.Result();
        result.Lines.Add(new BehaviourDiff.Line(
            BehaviourDiff.Kind.Added, "hkbStateMachine", "#200", "", "name=State"));
        result.Lines.Add(new BehaviourDiff.Line(
            BehaviourDiff.Kind.Removed, "hkbClipGenerator", "Walk", "name=Walk", ""));
        result.Lines.Add(new BehaviourDiff.Line(
            BehaviourDiff.Kind.Changed, "hkbClipGenerator", "name", "Walk", "Run"));
        result.Lines.Add(new BehaviourDiff.Line(
            BehaviourDiff.Kind.Changed, "hkbClipGenerator", "playbackSpeed", "1.0", "1.5"));
        return result;
    }

    private static string Clip(string name, string speed) => $"""
        <hkobject class="hkbClipGenerator" name="#100">
            <hkparam name="name">{name}</hkparam>
            <hkparam name="playbackSpeed">{speed}</hkparam>
        </hkobject>
        """;
}
