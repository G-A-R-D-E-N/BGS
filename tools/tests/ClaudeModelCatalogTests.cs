using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class ClaudeModelCatalogTests
{
    internal const string Sample = """
        {"version":1,"fetchedAt":1789658046609,"staleAt":1789661606049,
         "catalog":{"surface":"cc","config":{"id":"cc","models":[
           {"id":"claude-fable-5-1","name":"Fable 5.1","short_name":"Fable"},
           {"id":"claude-opus-5","name":"Opus 5","short_name":"Opus"},
           {"id":"claude-sonnet-5","name":"Sonnet 5","short_name":"Sonnet"},
           {"id":"claude-haiku-4-5-20251001","name":"Haiku 4.5","short_name":"Haiku"}
         ]},"state":{}}}
        """;

    [Fact]
    public void ParsingKeepsTheCatalogIdsAndNamesInOrder()
    {
        IReadOnlyList<CodexModel> models = ClaudeModelCatalog.Parse(Sample);

        Assert.Equal(
            new[] { "claude-fable-5-1", "claude-opus-5", "claude-sonnet-5", "claude-haiku-4-5-20251001" },
            models.Select(model => model.WireModel));
        Assert.Equal("Opus 5", models[1].DisplayName);
    }

    [Fact]
    public void ANameFallsBackToTheIdRatherThanShowingNothing()
    {
        IReadOnlyList<CodexModel> models =
            ClaudeModelCatalog.Parse("{\"catalog\":{\"config\":{\"models\":[{\"id\":\"claude-x\"}]}}}");

        Assert.Equal("claude-x", Assert.Single(models).DisplayName);
    }

    [Fact]
    public void MalformedOrUnexpectedShapesYieldNoModelsInsteadOfThrowing()
    {
        Assert.Empty(ClaudeModelCatalog.Parse("not json"));
        Assert.Empty(ClaudeModelCatalog.Parse("{}"));
        Assert.Empty(ClaudeModelCatalog.Parse("{\"catalog\":{\"config\":{\"models\":[]}}}"));
        Assert.Empty(ClaudeModelCatalog.Parse("{\"catalog\":{\"config\":{\"models\":\"nope\"}}}"));
        Assert.Empty(ClaudeModelCatalog.Parse(
            "{\"catalog\":{\"config\":{\"models\":[{\"name\":\"no id\"}]}}}"));
    }

    [Fact]
    public void DuplicateIdsAreCollapsed()
    {
        IReadOnlyList<CodexModel> models = ClaudeModelCatalog.Parse(
            "{\"catalog\":{\"config\":{\"models\":[" +
            "{\"id\":\"claude-opus-5\",\"name\":\"Opus 5\"}," +
            "{\"id\":\"claude-opus-5\",\"name\":\"Opus 5 again\"}]}}}");

        Assert.Equal("Opus 5", Assert.Single(models).DisplayName);
    }

    [Fact]
    public void ReadingUsesTheNewestCatalogFileAndNothingHardcoded()
    {
        Func<string> previousRoot = ClaudeModelCatalog.ConfigRootForTest;
        Func<string, IReadOnlyList<string>> previousFiles = ClaudeModelCatalog.FilesForTest;
        Func<string, string?> previousRead = ClaudeModelCatalog.ReadFileForTest;
        try
        {
            string? asked = null;
            ClaudeModelCatalog.ConfigRootForTest = () => "root";
            ClaudeModelCatalog.FilesForTest = folder =>
            {
                Assert.Equal(Path.Combine("root", "cache", ClaudeModelCatalog.CacheFolder), folder);
                return new[] { "newest.json", "older.json" };
            };
            ClaudeModelCatalog.ReadFileForTest = path =>
            {
                asked = path;
                return Sample;
            };

            IReadOnlyList<CodexModel> models = ClaudeModelCatalog.Read();

            Assert.Equal("newest.json", Path.GetFileName(asked!));
            Assert.Equal(4, models.Count);
        }
        finally
        {
            ClaudeModelCatalog.ConfigRootForTest = previousRoot;
            ClaudeModelCatalog.FilesForTest = previousFiles;
            ClaudeModelCatalog.ReadFileForTest = previousRead;
        }
    }

    [Fact]
    public void AHostWithoutTheCacheYieldsNoModelsRatherThanFailing()
    {
        Func<string> previousRoot = ClaudeModelCatalog.ConfigRootForTest;
        Func<string, IReadOnlyList<string>> previousFiles = ClaudeModelCatalog.FilesForTest;
        try
        {
            ClaudeModelCatalog.ConfigRootForTest = () => "";
            Assert.Empty(ClaudeModelCatalog.Read());

            ClaudeModelCatalog.ConfigRootForTest = () => "root";
            ClaudeModelCatalog.FilesForTest = _ => Array.Empty<string>();
            Assert.Empty(ClaudeModelCatalog.Read());
        }
        finally
        {
            ClaudeModelCatalog.ConfigRootForTest = previousRoot;
            ClaudeModelCatalog.FilesForTest = previousFiles;
        }
    }
}
