using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class CodexModelTests
{
    internal const string Catalog = """
        {"data":[
          {"id":"opaque-a","model":"model-a","displayName":"Model A","isDefault":true},
          {"id":"opaque-b","model":"model-b","displayName":"Model B"}
        ],"nextCursor":null}
        """;

    [Fact]
    public async Task DiscoveryAndSelectionUseSlugsOnFreshAndExistingThreads()
    {
        using var fixture = new CodexSessionFixture();
        fixture.Server.Handlers[CodexMethods.ModelList] = _ => Catalog;
        await fixture.Connection.ConnectAsync();
        Assert.Equal(new[] { "model-a", "model-b" }, fixture.Connection.Models.Select(m => m.WireModel));
        Assert.True(fixture.Connection.TrySelectModel("model-b", out _));
        Assert.Equal("model-b", Settings.Get("assistant.model"));
        Assert.Equal("ok", (await fixture.Session.SendAsync("first", CodexSessionFixture.Context())).Status);
        Assert.Equal("model-b", Model(fixture.Server.LastThreadStartParams));
        Assert.Equal("model-b", Model(fixture.Server.LastTurnStartParams));
        Assert.True(fixture.Connection.TrySelectModel("model-a", out _));
        Assert.Equal("ok", (await fixture.Session.SendAsync("second", CodexSessionFixture.Context())).Status);
        Assert.Equal(1, fixture.Server.ThreadStarts);
        Assert.Equal("model-a", Model(fixture.Server.LastTurnStartParams));
        Assert.True(fixture.Connection.TrySelectModel("model-b", out _));
        Assert.True(fixture.Connection.TrySelectModel("", out _));
        Assert.Equal("", Settings.Get("assistant.model"));
        Assert.Equal("ok", (await fixture.Session.SendAsync("reset", CodexSessionFixture.Context())).Status);
        Assert.False(fixture.Server.LastTurnStartParams!.Value.TryGetProperty("model", out _));
        Assert.Equal(1, fixture.Server.ThreadStarts);
    }

    [Fact]
    public async Task UnsetModelIsReportedAsTheCodexConfigDefaultAndUnknownModelsAreRefused()
    {
        using var fixture = new CodexSessionFixture();
        fixture.Server.Handlers[CodexMethods.ModelList] = _ => Catalog;
        await fixture.Connection.ConnectAsync();

        Assert.Equal("Codex config default", fixture.Connection.ModelLabel);
        Assert.False(fixture.Connection.TrySelectModel("gpt-9-does-not-exist", out string failure));
        Assert.Contains("not available", failure, StringComparison.Ordinal);
        Assert.Equal("", Settings.Get("assistant.model"));
        Assert.True(fixture.Connection.TrySelectModel("model-b", out _));
        Assert.Equal("Model B", fixture.Connection.ModelLabel);
    }

    private static string? Model(JsonElement? parameters) => parameters!.Value.GetProperty("model").GetString();
}
