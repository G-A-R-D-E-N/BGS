using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class CodexAuthTests
{
    [Fact]
    public async Task SignedOutSessionAsksForSignInWithoutAnyCredential()
    {
        using var fixture = new CodexSessionFixture();
        fixture.Server.SignedIn = false;

        AssistantReply reply = await fixture.Session.SendAsync("hello", CodexSessionFixture.Context());

        Assert.Equal("signed_out", reply.Status);
        Assert.False(fixture.Session.ToolsAvailable);
        Assert.DoesNotContain(fixture.Server.Sent,
            line => line.Contains("turn/start", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Server.Sent,
            line => line.Contains("api_key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ApiKeyAuthCannotStartAChatGptSubscriptionTurn()
    {
        using var fixture = new CodexSessionFixture();
        fixture.Server.AuthMode = "apiKey";

        AssistantReply reply = await fixture.Session.SendAsync("hello", CodexSessionFixture.Context());

        Assert.Equal("signed_out", reply.Status);
        Assert.False(fixture.Connection.Account.SignedIn);
        Assert.False(fixture.Connection.Account.IsChatGpt);
        Assert.Equal("apiKey", fixture.Connection.Account.AuthMode);
        Assert.DoesNotContain(fixture.Server.Sent,
            line => line.Contains("thread/start", StringComparison.Ordinal) ||
                    line.Contains("turn/start", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandshakeRunsInitializeThenInitializedBeforeAccountRead()
    {
        using var fixture = new CodexSessionFixture();

        CodexAccount account = await fixture.Connection.ConnectAsync();

        string[] methods = fixture.Server.Sent.Select(CodexSessionFixture.Method)
            .Where(method => method.Length > 0).ToArray();
        Assert.Equal("initialize", methods[0]);
        JsonElement initialize = CodexSessionFixture.Parse(fixture.Server.Sent
            .Single(line => CodexSessionFixture.Method(line) == "initialize"));
        Assert.Equal(BuildInfo.Version, initialize.GetProperty("params")
            .GetProperty("clientInfo").GetProperty("version").GetString());
        Assert.Equal("initialized", methods[1]);
        Assert.Contains("account/read", methods);
        Assert.True(fixture.Connection.Client.IsConnected);
        Assert.Equal("0.154.0", fixture.Connection.ServerInfo!.Version);
        Assert.True(account.SignedIn);
        Assert.True(account.IsChatGpt);
        Assert.Equal("plus", account.PlanType);
    }

    [Fact]
    public async Task LaunchesPinnedIsolatedStdioAppServer()
    {
        using var fixture = new CodexSessionFixture();

        await fixture.Connection.ConnectAsync();

        Assert.Equal(new[]
        {
            "app-server", "--listen", "stdio://",
            "-c", "model_provider=\"openai\"",
            "-c", "mcp_servers={}",
            "-c", "features.shell_tool=false",
            "-c", "features.unified_exec=false",
            "-c", "web_search=\"disabled\"",
        }, fixture.Connection.LaunchArguments);
    }

    [Fact]
    public async Task ThreadPinsOpenAiAndDisablesAmbientCodexTools()
    {
        using var fixture = new CodexSessionFixture();

        AssistantReply reply = await fixture.Session.SendAsync("hello", CodexSessionFixture.Context());

        Assert.Equal("ok", reply.Status);
        JsonElement parameters = fixture.Server.LastThreadStartParams!.Value;
        Assert.Equal("openai", parameters.GetProperty("modelProvider").GetString());
        JsonElement config = parameters.GetProperty("config");
        Assert.False(config.GetProperty("features.shell_tool").GetBoolean());
        Assert.False(config.GetProperty("features.unified_exec").GetBoolean());
        Assert.False(config.GetProperty("features.standalone_web_search").GetBoolean());
        Assert.False(config.GetProperty("features.plugins").GetBoolean());
        Assert.False(config.GetProperty("features.apps").GetBoolean());
        Assert.Equal("disabled", config.GetProperty("web_search").GetString());
        Assert.Equal(0, config.GetProperty("mcp_servers").EnumerateObject().Count());
    }

    [Fact]
    public async Task BrowserLoginReturnsAuthUrlAndCancelSucceeds()
    {
        using var fixture = new CodexSessionFixture();
        fixture.Server.SignedIn = false;

        CodexLoginChallenge challenge = await fixture.Connection.BeginChatGptLoginAsync();

        Assert.Equal("login-browser", challenge.LoginId);
        Assert.Equal("https://example.invalid/auth", challenge.AuthUrl);
        Assert.False(challenge.DeviceCode);
        Assert.True(await fixture.Connection.CancelLoginAsync(challenge.LoginId));
        Assert.Equal("account/login/cancel", fixture.Server.Sent.Select(CodexSessionFixture.Method).Last());
    }

    [Fact]
    public async Task DeviceCodeLoginReturnsVerificationUrlAndUserCode()
    {
        using var fixture = new CodexSessionFixture();
        fixture.Server.SignedIn = false;

        CodexLoginChallenge challenge = await fixture.Connection.BeginDeviceCodeLoginAsync();

        Assert.True(challenge.DeviceCode);
        Assert.Equal("login-device", challenge.LoginId);
        Assert.Equal("ABCD-1234", challenge.UserCode);
        Assert.Equal("https://example.invalid/device", challenge.VerificationUrl);
    }

    [Fact]
    public async Task AccountUpdatedNotificationCarriesPlanType()
    {
        using var fixture = new CodexSessionFixture();
        await fixture.Connection.ConnectAsync();

        fixture.Server.Notify("account/updated", "{\"authMode\":\"chatgpt\",\"planType\":\"pro\"}");

        Assert.Equal("pro", fixture.Connection.Account.PlanType);
        Assert.True(fixture.Connection.Account.SignedIn);

        fixture.Server.Notify("account/updated", "{\"authMode\":\"apikey\",\"planType\":null}");
        Assert.False(fixture.Connection.Account.SignedIn);
        Assert.Equal("apikey", fixture.Connection.Account.AuthMode);

        fixture.Server.Notify("account/updated", "{\"authMode\":null,\"planType\":null}");
        Assert.False(fixture.Connection.Account.SignedIn);
    }

    [Fact]
    public async Task FailedLoginCompletionSignsOutAndLogoutClearsAccount()
    {
        using var fixture = new CodexSessionFixture();
        await fixture.Connection.ConnectAsync();

        fixture.Server.Notify("account/login/completed",
            "{\"loginId\":\"login-browser\",\"success\":false,\"error\":\"cancelled\"}");
        Assert.False(fixture.Connection.Account.SignedIn);

        await fixture.Connection.SignOutAsync();
        Assert.False(fixture.Connection.Account.SignedIn);
        Assert.Contains("account/logout", fixture.Server.Sent.Select(CodexSessionFixture.Method));
    }

    [Fact]
    public async Task SuccessfulLoginCompletionReReadsTheAccount()
    {
        using var fixture = new CodexSessionFixture();
        fixture.Server.SignedIn = false;
        await fixture.Connection.ConnectAsync();
        Assert.False(fixture.Connection.Account.SignedIn);

        fixture.Server.SignedIn = true;
        fixture.Server.PlanType = "business";
        fixture.Server.Notify("account/login/completed",
            "{\"loginId\":\"login-browser\",\"success\":true,\"error\":null}");

        for (int attempt = 0; attempt < 50 && !fixture.Connection.Account.SignedIn; attempt++)
            await Task.Delay(20);

        Assert.True(fixture.Connection.Account.SignedIn);
        Assert.Equal("business", fixture.Connection.Account.PlanType);
    }

    [Theory]
    [InlineData("plus", "Plus")]
    [InlineData("prolite", "Prolite")]
    [InlineData("unknown", "Unknown")]
    [InlineData("", "")]
    public void PlanLabelsOnlyTitleCaseProtocolValues(string protocolValue, string expected) =>
        Assert.Equal(expected, AssistantUi.PlanLabel(protocolValue));

    [Fact]
    public async Task MissingCodexExecutableReportsABoundedError()
    {
        string? previousPath = Settings.SettingsPathForTest;
        Func<string, bool> previousExists = CodexLocator.FileExistsForTest;
        string settingsPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"bgs-codex-missing-{Guid.NewGuid():N}.cfg");
        try
        {
            Settings.SettingsPathForTest = settingsPath;
            CodexLocator.FileExistsForTest = _ => false;
            using var connection = new CodexAssistantConnection(
                new AssistantProviderOptions(AssistantProviderOptions.CodexBackend, "", ""));
            using var session = new CodexAssistantSession(connection, Array.Empty<Microsoft.Extensions.AI.AIFunction>());

            AssistantReply reply = await session.SendAsync("hello", CodexSessionFixture.Context());

            Assert.Equal("provider_error", reply.Status);
            Assert.Contains("Codex CLI was not found", reply.Text, StringComparison.Ordinal);
        }
        finally
        {
            Settings.SettingsPathForTest = previousPath;
            CodexLocator.FileExistsForTest = previousExists;
        }
    }

    [Fact]
    public void ProviderOptionsDefaultToCodexAndCarryNoCredential()
    {
        string? previousPath = Settings.SettingsPathForTest;
        Settings.SettingsPathForTest =
            Path.Combine(Path.GetTempPath(), "bgs-provider-" + Guid.NewGuid().ToString("N") + ".cfg");
        try
        {
            AssistantProviderOptions options = AssistantProviderOptions.FromSettings();

            Assert.True(options.IsCodex);
            Assert.Equal("codex", options.Backend);
            Assert.DoesNotContain("key", options.Model, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http", options.CodexExecutableOverride, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("ChatGPT via Codex", AssistantProvider.DisplayName);
        }
        finally
        {
            Settings.SettingsPathForTest = previousPath;
        }
    }
}
