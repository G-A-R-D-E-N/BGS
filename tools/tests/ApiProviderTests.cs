using System;
using System.Linq;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class ApiProviderTests
{
    [Fact]
    public void GeminiAndLocalAreApiProvidersAndTheClisAreNot()
    {
        Assert.True(ApiProviders.IsApi(AssistantBackend.Gemini));
        Assert.True(ApiProviders.IsApi(AssistantBackend.Local));
        Assert.False(ApiProviders.IsApi(AssistantBackend.Codex));
        Assert.False(ApiProviders.IsApi(AssistantBackend.Claude));
        Assert.False(ApiProviders.IsApi(AssistantBackend.Opencode));
    }

    [Fact]
    public void GeminiRefusesAnInsecureBaseUrl()
    {
        string? previous = Settings.SettingsPathForTest;
        Settings.SettingsPathForTest = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "bgs-api-" + Guid.NewGuid().ToString("N") + ".cfg");
        try
        {
            Settings.TrySet(ApiProviders.BaseUrlSetting(AssistantBackend.Gemini),
                "http://example.test/v1", out _);

            Assert.False(ApiProviders.TryBaseUrl(AssistantBackend.Gemini, out _, out string error));
            Assert.Contains("https", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Settings.SettingsPathForTest = previous;
        }
    }

    [Fact]
    public void CredentialsAreOnlyAllowedOverTlsOrLoopback()
    {
        Assert.True(ApiProviders.AllowsCredentials(
            new Uri("https://generativelanguage.googleapis.com/v1beta/openai/")));
        Assert.True(ApiProviders.AllowsCredentials(new Uri("http://127.0.0.1:11434/v1")));
        Assert.True(ApiProviders.AllowsCredentials(new Uri("http://localhost:11434/v1")));
        Assert.False(ApiProviders.AllowsCredentials(new Uri("http://192.168.1.50:11434/v1")));
        Assert.False(ApiProviders.AllowsCredentials(new Uri("http://example.test/v1")));
    }

    [Fact]
    public void EachApiProviderHasItsOwnWireNameAndDefaultEndpoint()
    {
        Assert.Equal("gemini", AssistantCliLocator.CommandName(AssistantBackend.Gemini));
        Assert.Equal("local", AssistantCliLocator.CommandName(AssistantBackend.Local));
        Assert.Contains("generativelanguage.googleapis.com", ApiProviders.Gemini.DefaultBaseUrl);
        Assert.Contains("127.0.0.1", ApiProviders.Local.DefaultBaseUrl);
        Assert.NotEqual(ApiProviders.BaseUrlSetting(AssistantBackend.Gemini),
            ApiProviders.BaseUrlSetting(AssistantBackend.Local));
    }

    [Fact]
    public void TheNoticeSaysTheKeyIsNeverStoredAndNamesThePaidTierRule()
    {
        Assert.Contains("never writes one to disk", ApiProviders.Notice, StringComparison.Ordinal);
        Assert.Contains("EEA", ApiProviders.Notice, StringComparison.Ordinal);
        Assert.Contains("ships no API key", ApiProviders.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelsUrlAppendsTheOpenAiPathOnce()
    {
        Assert.Equal("http://127.0.0.1:11434/v1/models", ApiModelCatalog.ModelsUrl("http://127.0.0.1:11434/v1"));
        Assert.Equal("http://127.0.0.1:11434/v1/models", ApiModelCatalog.ModelsUrl("http://127.0.0.1:11434/v1/"));
        Assert.Equal("", ApiModelCatalog.ModelsUrl(""));
    }

    [Fact]
    public void ModelListingParsesTheOpenAiShapeAndSortsIt()
    {
        const string body = "{\"object\":\"list\",\"data\":[" +
            "{\"id\":\"zeta\"},{\"id\":\"alpha\"},{\"id\":\"alpha\"},{\"nope\":1},{\"id\":\"\"}]}";

        Assert.Equal(new[] { "alpha", "zeta" }, ApiModelCatalog.Parse(body));
    }

    [Fact]
    public void ModelListingRejectsMalformedBodiesInsteadOfThrowing()
    {
        Assert.Empty(ApiModelCatalog.Parse("not json"));
        Assert.Empty(ApiModelCatalog.Parse("{}"));
        Assert.Empty(ApiModelCatalog.Parse("{\"data\":\"nope\"}"));
    }

    [Fact]
    public void ApiModelsCarryTheirAgentSoThePaletteCanFilterThem()
    {
        var ids = new[] { "gemini-9-flash", "gemini-9-pro" };

        var gemini = AssistantUi.ApiModels(AssistantBackend.Gemini, ids);
        Assert.All(gemini, model => Assert.Equal("Gemini", model.Agent));
        Assert.Equal(ids, gemini.Select(model => model.WireModel));

        var local = AssistantUi.ApiModels(AssistantBackend.Local, ids);
        Assert.All(local, model => Assert.Equal("Local", model.Agent));
    }

    [Fact]
    public void LocalAndGeminiKeepTheirOwnModelSettings()
    {
        Assert.NotEqual(
            AssistantUi.ModelSettingKey(AssistantBackend.Gemini),
            AssistantUi.ModelSettingKey(AssistantBackend.Local));
        Assert.NotEqual(
            AssistantUi.ModelSettingKey(AssistantBackend.Gemini),
            AssistantUi.ModelSettingKey(AssistantBackend.Codex));
    }

    [Fact]
    public void AnApiProviderIsNeverTreatedAsACommandLineTool()
    {
        Assert.False(AssistantCliLocator.TryLocate(AssistantBackend.Gemini, "", out _, out string error));
        Assert.Contains("API endpoint", error, StringComparison.Ordinal);
        Assert.False(AssistantCliLocator.TryLocate(AssistantBackend.Local, "", out _, out _));
    }
}
