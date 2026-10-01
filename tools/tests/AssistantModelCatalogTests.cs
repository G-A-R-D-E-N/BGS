using System.Collections.Generic;
using System.Linq;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class AssistantModelCatalogTests
{
    private static CodexModel Model(string agent, string wire, string group = "") =>
        new(wire, wire, false, wire) { Agent = agent, Group = group.Length > 0 ? group : agent };

    [Fact]
    public void MergeKeepsEveryAgentAndDropsOnlyTrueDuplicates()
    {
        var sources = new IReadOnlyList<CodexModel>[]
        {
            new[] { Model("Codex", "gpt-a"), Model("Codex", "gpt-b") },
            new[] { Model("Claude", "opus") },
            new[] { Model("opencode", "shared"), Model("Claude", "shared"), Model("Codex", "gpt-a") },
        };

        IReadOnlyList<CodexModel> merged = AssistantModelCatalog.Merge(sources);

        Assert.Equal(new[] { "gpt-a", "gpt-b", "opus", "shared", "shared" },
            merged.Select(model => model.WireModel));
        Assert.Equal(new[] { "Codex", "Codex", "Claude", "opencode", "Claude" },
            merged.Select(AssistantModelCatalog.AgentOf));
    }

    [Fact]
    public void MergeSkipsNullSourcesAndEmptyInput()
    {
        Assert.Empty(AssistantModelCatalog.Merge(new IReadOnlyList<CodexModel>[] { null! }));
        Assert.Empty(AssistantModelCatalog.Merge(System.Array.Empty<IReadOnlyList<CodexModel>>()));
    }

    [Fact]
    public void AgentsInOrderPrefersTheCanonicalOrder()
    {
        IReadOnlyList<CodexModel> models = new[]
        {
            Model("opencode", "x"),
            Model("Claude", "y"),
            Model("Codex", "z"),
            Model("Gemini", "w"),
        };

        Assert.Equal(new[] { "Codex", "Claude", "opencode", "Gemini" },
            AssistantModelCatalog.AgentsInOrder(models));
    }

    [Fact]
    public void SectionTitleNamesTheAgentAndItsProvider()
    {
        Assert.Equal("Codex", AssistantModelCatalog.SectionTitle("Codex", "Codex"));
        Assert.Equal("opencode \u00B7 opencode-go", AssistantModelCatalog.SectionTitle("opencode", "opencode-go"));
        Assert.Equal("Claude", AssistantModelCatalog.SectionTitle("Claude", ""));
    }

    [Fact]
    public void AgentOfFallsBackToGroupThenModels()
    {
        Assert.Equal("Codex", AssistantModelCatalog.AgentOf(new CodexModel("a", "a", false) { Agent = "Codex" }));
        Assert.Equal("Claude", AssistantModelCatalog.AgentOf(new CodexModel("b", "b", false) { Group = "Claude" }));
        Assert.Equal("Models", AssistantModelCatalog.AgentOf(new CodexModel("c", "c", false)));
    }

    [Fact]
    public void DuplicateWireModelsAcrossAgentsHaveDistinctIdentities()
    {
        var codex = new CodexModel("shared", "Shared", false, "shared") { Agent = "Codex" };
        var opencode = new CodexModel("shared", "Shared", false, "shared") { Agent = "opencode" };

        Assert.Equal("Codex::shared", AssistantModelCatalog.Identity(codex));
        Assert.NotEqual(AssistantModelCatalog.Identity(codex), AssistantModelCatalog.Identity(opencode));
    }

    [Fact]
    public void AnIdentityCannotBeSplitByThePreferenceStoreSeparator()
    {
        var model = new CodexModel("google/gemini-3.5-flash-lite", "Gemini", false,
            "google/gemini-3.5-flash-lite") { Agent = "opencode" };

        Assert.DoesNotContain('|', AssistantModelCatalog.Identity(model));
    }

    [Fact]
    public void EachBackendMapsToItsOwnAgent()
    {
        Assert.Equal("Codex", AssistantModelCatalog.AgentFor(AssistantBackend.Codex));
        Assert.Equal("opencode", AssistantModelCatalog.AgentFor(AssistantBackend.Opencode));
        Assert.Equal("Gemini", AssistantModelCatalog.AgentFor(AssistantBackend.Gemini));
        Assert.Equal("Local", AssistantModelCatalog.AgentFor(AssistantBackend.Local));
    }

    [Fact]
    public void EveryCatalogStampsItsAgent()
    {
        CodexModel? codex = CodexAccountClient.ParseModelForTest(
            "{\"id\":\"gpt-x\",\"model\":\"gpt-x\",\"displayName\":\"GPT-X\"}");
        Assert.Equal("Codex", codex?.Agent);

        CodexModel? claude = ClaudeModelCatalog
            .Parse("{\"catalog\":{\"config\":{\"models\":[{\"id\":\"opus\",\"name\":\"Opus\"}]}}}")
            .FirstOrDefault();
        Assert.Equal("Claude", claude?.Agent);

        CodexModel? opencode = OpencodeModelCatalog.ParseVerbose(
            "opencode-go/luna\n{\"id\":\"luna\",\"providerID\":\"opencode-go\",\"name\":\"Luna\"}")
            .FirstOrDefault();
        Assert.Equal("opencode", opencode?.Agent);
    }
}
