using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class OpencodeModelCatalogTests
{
    [Fact]
    public void ParsingKeepsValidIdsInOrderAndDropsAnythingElse()
    {
        const string output =
            "opencode-go/gpt-5.6-luna\r\n" +
            "\n" +
            "  anthropic/claude-haiku-4-5  \n" +
            "opencode-go/gpt-5.6-luna\n" +
            "not-a-model\n" +
            "trailing/\n" +
            "/leading\n" +
            "has space/model\n" +
            "a/b/c\n" +
            "opencode/big-pickle\n";

        IReadOnlyList<CodexModel> models = OpencodeModelCatalog.Parse(output);

        Assert.Equal(
            new[] { "opencode-go/gpt-5.6-luna", "anthropic/claude-haiku-4-5", "opencode/big-pickle" },
            models.Select(model => model.WireModel));
        Assert.All(models, model => Assert.Equal(model.WireModel, model.DisplayName));
        Assert.All(models, model => Assert.Equal(AssistantBackend.Opencode,
            AssistantCliLocator.Parse(model.Agent)));
        Assert.Equal(new[] { "opencode-go", "anthropic", "opencode" }, models.Select(model => model.Group));
    }

    [Fact]
    public void AnEmptyCatalogParsesToNothingInsteadOfThrowing()
    {
        Assert.Empty(OpencodeModelCatalog.Parse(""));
        Assert.Empty(OpencodeModelCatalog.Parse("\n\n\r\n"));
    }

    [Fact]
    public async Task ReadingUsesTheCliCatalogRatherThanABuiltInList()
    {
        Func<AssistantCli, IReadOnlyList<string>, IReadOnlyDictionary<string, string>?, TimeSpan,
            CancellationToken, Task<AssistantCommandResult>> previous = AssistantCommandRunner.RunForTest;
        try
        {
            IReadOnlyList<string>? arguments = null;
            AssistantCommandRunner.RunForTest = (_, args, _, _, _) =>
            {
                arguments = args;
                return Task.FromResult(new AssistantCommandResult(0,
                    "opencode-go/gpt-5.6-luna\nanthropic/claude-haiku-4-5\n", ""));
            };

            IReadOnlyList<CodexModel> models = await OpencodeModelCatalog
                .ReadAsync(new AssistantCli("opencode", "detected"));

            Assert.Equal(
                new[] { "opencode-go/gpt-5.6-luna", "anthropic/claude-haiku-4-5" },
                models.Select(model => model.WireModel));
            Assert.Equal(new[] { "models" }, arguments);
        }
        finally
        {
            AssistantCommandRunner.RunForTest = previous;
        }
    }

    [Fact]
    public async Task AFailedCatalogCommandYieldsNoModelsRatherThanGuesses()
    {
        Func<AssistantCli, IReadOnlyList<string>, IReadOnlyDictionary<string, string>?, TimeSpan,
            CancellationToken, Task<AssistantCommandResult>> previous = AssistantCommandRunner.RunForTest;
        try
        {
            AssistantCommandRunner.RunForTest = (_, _, _, _, _) =>
                Task.FromResult(new AssistantCommandResult(1, "", "failed"));

            Assert.Empty(await OpencodeModelCatalog.ReadAsync(new AssistantCli("opencode", "detected")));
        }
        finally
        {
            AssistantCommandRunner.RunForTest = previous;
        }
    }

    [Fact]
    public void VerboseOutputYieldsNamesGroupsContextAndCapabilities()
    {
        const string output =
            "opencode-go/gpt-5.6-luna\n" +
            "{\n" +
            "  \"id\": \"gpt-5.6-luna\",\n" +
            "  \"providerID\": \"opencode-go\",\n" +
            "  \"name\": \"GPT-5.6 Luna\",\n" +
            "  \"limit\": {\n" +
            "    \"context\": 1000000,\n" +
            "    \"output\": 32000\n" +
            "  },\n" +
            "  \"capabilities\": {\n" +
            "    \"reasoning\": true\n" +
            "  },\n" +
            "  \"api\": { \"url\": \"https://example.test/v1\" },\n" +
            "  \"cost\": { \"input\": 0.15 }\n" +
            "}\n" +
            "opencode/plain-model\n" +
            "{\n" +
            "  \"id\": \"plain-model\",\n" +
            "  \"providerID\": \"opencode\",\n" +
            "  \"name\": \"Plain Model\",\n" +
            "  \"limit\": { \"context\": 200000 },\n" +
            "  \"capabilities\": { \"reasoning\": false }\n" +
            "}\n";

        IReadOnlyList<CodexModel> models = OpencodeModelCatalog.ParseVerbose(output);

        Assert.Equal(new[] { "opencode-go/gpt-5.6-luna", "opencode/plain-model" },
            models.Select(model => model.WireModel));
        Assert.Equal("GPT-5.6 Luna", models[0].DisplayName);
        Assert.Equal("opencode-go", models[0].Group);
        Assert.Equal(1_000_000, models[0].ContextLimit);
        Assert.True(models[0].Reasoning);
        Assert.Equal(200_000, models[1].ContextLimit);
        Assert.False(models[1].Reasoning);
    }

    [Fact]
    public void AVerboseBlockWithNoMetadataStillYieldsAModel()
    {
        IReadOnlyList<CodexModel> models = OpencodeModelCatalog.ParseVerbose("opencode/x\n{ not json");

        CodexModel model = Assert.Single(models);
        Assert.Equal("opencode/x", model.WireModel);
        Assert.Equal("opencode/x", model.DisplayName);
        Assert.Equal("opencode", model.Group);
    }

    [Fact]
    public void ContextSizesAreShownInShortForm()
    {
        Assert.Equal("1M", AssistantModelPicker.ContextLabel(1_000_000));
        Assert.Equal("262.1K", AssistantModelPicker.ContextLabel(262_144));
        Assert.Equal("200K", AssistantModelPicker.ContextLabel(200_000));
        Assert.Equal("", AssistantModelPicker.ContextLabel(0));
    }

    [Fact]
    public void FavouritesAndRecentsRoundTripThroughSettings()
    {
        string? previous = Settings.SettingsPathForTest;
        Settings.SettingsPathForTest =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "bgs-models-" + Guid.NewGuid().ToString("N") + ".cfg");
        try
        {
            Assert.False(AssistantModelPrefs.IsFavorite("opencode-go/gpt-5.6-luna"));
            AssistantModelPrefs.ToggleFavorite("opencode-go/gpt-5.6-luna");
            Assert.True(AssistantModelPrefs.IsFavorite("opencode-go/gpt-5.6-luna"));
            AssistantModelPrefs.ToggleFavorite("opencode-go/gpt-5.6-luna");
            Assert.False(AssistantModelPrefs.IsFavorite("opencode-go/gpt-5.6-luna"));

            AssistantModelPrefs.Remember("a/one");
            AssistantModelPrefs.Remember("b/two");
            AssistantModelPrefs.Remember("a/one");
            Assert.Equal(new[] { "a/one", "b/two" }, AssistantModelPrefs.Recents());

            for (int index = 0; index < AssistantModelPrefs.MaximumRecents + 4; index++)
                AssistantModelPrefs.Remember("m/" + index);
            Assert.Equal(AssistantModelPrefs.MaximumRecents, AssistantModelPrefs.Recents().Count);
        }
        finally
        {
            Settings.SettingsPathForTest = previous;
        }
    }

}
