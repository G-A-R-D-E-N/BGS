using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class CodexIsolationTests
{
    private static readonly string[] DisabledFeatures =
    {
        "features.apps",
        "features.code_mode",
        "features.code_mode_only",
        "features.context_management",
        "features.current_time_reminder",
        "features.deferred_executor",
        "features.enable_fanout",
        "features.goals",
        "features.hooks",
        "features.image_generation",
        "features.memories",
        "features.multi_agent",
        "features.multi_agent_v2",
        "features.plugins",
        "features.request_permissions_tool",
        "features.shell_snapshot",
        "features.shell_tool",
        "features.standalone_web_search",
        "features.token_budget",
        "features.tool_suggest",
        "features.unified_exec",
        "features.view_image",
        "orchestrator.skills.enabled",
        "skills.include_instructions",
        "token_budget.use_history_notes_extension",
        "tools.experimental_request_user_input.enabled",
        "tools.update_plan.enabled",
    };

    [Fact]
    public async Task EffectiveConfigIsReadBeforeThreadAndAmbientToolsAreDisabled()
    {
        using var fixture = new CodexSessionFixture();
        fixture.Server.EffectiveMcpServers = new[] { "global-one", "global-two" };

        AssistantReply reply = await fixture.Session.SendAsync("Check it.", CodexSessionFixture.Context());

        Assert.Equal("ok", reply.Status);
        string[] methods = fixture.Server.Sent.Select(CodexSessionFixture.Method).ToArray();
        Assert.True(Array.IndexOf(methods, CodexMethods.ConfigRead) >= 0);
        Assert.True(Array.IndexOf(methods, CodexMethods.ConfigRead) < Array.IndexOf(methods, CodexMethods.ThreadStart));

        JsonElement configRead = fixture.Server.LastConfigReadParams!.Value;
        Assert.False(configRead.GetProperty("includeLayers").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(configRead.GetProperty("cwd").GetString()));

        JsonElement thread = fixture.Server.LastThreadStartParams!.Value;
        Assert.Equal("openai", thread.GetProperty("modelProvider").GetString());
        Assert.Equal("read-only", thread.GetProperty("sandbox").GetString());
        Assert.Equal("never", thread.GetProperty("approvalPolicy").GetString());
        Assert.Equal("system", thread.GetProperty("threadSource").GetString());
        Assert.Empty(thread.GetProperty("runtimeWorkspaceRoots").EnumerateArray());
        Assert.Empty(thread.GetProperty("environments").EnumerateArray());
        Assert.Empty(thread.GetProperty("selectedCapabilityRoots").EnumerateArray());

        JsonElement config = thread.GetProperty("config");
        foreach (string feature in DisabledFeatures)
        {
            Assert.True(config.TryGetProperty(feature, out JsonElement value), feature);
            Assert.False(value.GetBoolean(), feature);
        }
        Assert.Equal("disabled", config.GetProperty("web_search").GetString());

        JsonElement mcp = config.GetProperty("mcp_servers");
        Assert.False(mcp.GetProperty("global-one").GetProperty("enabled").GetBoolean());
        Assert.False(mcp.GetProperty("global-two").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task ConfigReadFailureFailsClosedBeforeThreadStart()
    {
        using var fixture = new CodexSessionFixture();
        fixture.Server.RejectConfigRead = true;

        AssistantReply reply = await fixture.Session.SendAsync("Check it.", CodexSessionFixture.Context());

        Assert.Equal("provider_error", reply.Status);
        Assert.Contains("could not be verified", reply.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.Server.ThreadStarts);
        Assert.DoesNotContain(fixture.Server.Sent,
            line => CodexSessionFixture.Method(line) == CodexMethods.ThreadStart);
        Assert.DoesNotContain("config read unavailable", reply.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MalformedMcpConfigurationFailsClosed()
    {
        using var fixture = new CodexSessionFixture();
        fixture.Server.Handlers[CodexMethods.ConfigRead] = _ =>
            "{\"config\":{\"mcp_servers\":[\"unexpected\"]},\"origins\":{}}";

        AssistantReply reply = await fixture.Session.SendAsync("Check it.", CodexSessionFixture.Context());

        Assert.Equal("provider_error", reply.Status);
        Assert.Contains("could not be verified", reply.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.Server.ThreadStarts);
    }

    [Theory]
    [InlineData("modelProvider", "other")]
    [InlineData("approvalPolicy", "on-request")]
    [InlineData("sandbox", "workspaceWrite")]
    public async Task UnsafeThreadStartResponseFailsBeforeTurnStart(string field, string value)
    {
        using var fixture = new CodexSessionFixture();
        fixture.Server.Handlers[CodexMethods.ThreadStart] = _ => field switch
        {
            "modelProvider" => fixture.Server.ThreadResult("thread-1").Replace(
                "\"modelProvider\":\"openai\"", "\"modelProvider\":\"" + value + "\"",
                StringComparison.Ordinal),
            "approvalPolicy" => fixture.Server.ThreadResult("thread-1").Replace(
                "\"approvalPolicy\":\"never\"", "\"approvalPolicy\":\"" + value + "\"",
                StringComparison.Ordinal),
            _ => fixture.Server.ThreadResult("thread-1").Replace(
                "\"type\":\"readOnly\"", "\"type\":\"" + value + "\"",
                StringComparison.Ordinal),
        };

        AssistantReply reply = await fixture.Session.SendAsync("Check it.", CodexSessionFixture.Context());

        Assert.Equal("provider_error", reply.Status);
        Assert.Contains("could not be verified", reply.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.Server.TurnStarts);
        Assert.DoesNotContain(fixture.Server.Sent,
            line => CodexSessionFixture.Method(line) == CodexMethods.TurnStart);
    }
}