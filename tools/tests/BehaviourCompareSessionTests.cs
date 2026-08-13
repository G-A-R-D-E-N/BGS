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

    private static string Clip(string name, string speed) => $"""
        <hkobject class="hkbClipGenerator" name="#100">
            <hkparam name="name">{name}</hkparam>
            <hkparam name="playbackSpeed">{speed}</hkparam>
        </hkobject>
        """;
}
