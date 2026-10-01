using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Microsoft.Extensions.AI;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class CodexConversationTests
{
    [Fact]
    public async Task ThreadStartRequestsReadOnlyNeverEphemeralThread()
    {
        using var fixture = new CodexSessionFixture(new[] { PlainTool("bgs.inspect_behavior") });

        AssistantReply reply = await fixture.Session.SendAsync("Check it.", CodexSessionFixture.Context());

        Assert.Equal("ok", reply.Status);
        JsonElement parameters = fixture.Server.LastThreadStartParams!.Value;
        Assert.Equal("read-only", parameters.GetProperty("sandbox").GetString());
        Assert.Equal("never", parameters.GetProperty("approvalPolicy").GetString());
        Assert.True(parameters.GetProperty("ephemeral").GetBoolean());
        Assert.True(Path.IsPathRooted(parameters.GetProperty("cwd").GetString()));
        string instructions = parameters.GetProperty("baseInstructions").GetString() ?? "";
        Assert.Contains("untrusted data", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("explicit user approval", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.False(parameters.TryGetProperty("model", out _));
    }

    [Fact]
    public async Task DynamicToolsAreRegisteredWithProtocolSafeNames()
    {
        using var fixture = new CodexSessionFixture(new[]
        {
            PlainTool("bgs.inspect_behavior"), PlainTool("bgs.set_clip_animation"),
        });

        await fixture.Session.SendAsync("Check it.", CodexSessionFixture.Context());

        JsonElement tools = fixture.Server.LastThreadStartParams!.Value.GetProperty("dynamicTools");
        string[] names = tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()!)
            .ToArray();
        Assert.Equal(new[] { "bgs_inspect_behavior", "bgs_set_clip_animation" }, names);
        Assert.All(tools.EnumerateArray(), tool =>
        {
            Assert.Equal("function", tool.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Object, tool.GetProperty("inputSchema").ValueKind);
            Assert.DoesNotContain('.', tool.GetProperty("name").GetString()!);
        });
        Assert.True(fixture.Session.ToolsAvailable);
        Assert.True(fixture.Connection.DynamicToolsAvailable);
    }

    [Fact]
    public async Task DynamicToolRejectionFallsBackToChatOnlyWithoutBuiltInTools()
    {
        using var fixture = new CodexSessionFixture(new[] { PlainTool("bgs.inspect_behavior") });
        fixture.Server.RejectDynamicTools = true;

        AssistantReply reply = await fixture.Session.SendAsync("Check it.", CodexSessionFixture.Context());

        Assert.Equal("ok", reply.Status);
        Assert.False(fixture.Session.ToolsAvailable);
        Assert.False(fixture.Connection.DynamicToolsAvailable);
        Assert.False(fixture.Server.LastThreadStartParams!.Value.TryGetProperty("dynamicTools", out _));
        Assert.Equal(2, fixture.Server.Sent.Count(line =>
            CodexSessionFixture.Method(line) == "thread/start"));
        Assert.DoesNotContain(fixture.Server.Sent, line =>
            line.Contains("command/exec", StringComparison.Ordinal) ||
            line.Contains("shellCommand", StringComparison.Ordinal) ||
            line.Contains("danger-full-access", StringComparison.Ordinal) ||
            line.Contains("workspace-write", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReplyPrefersCompletedAgentMessageThenStreamedDeltas()
    {
        using var fixture = new CodexSessionFixture();
        fixture.Server.FinalText = "the final answer";

        AssistantReply reply = await fixture.Session.SendAsync("Check it.", CodexSessionFixture.Context());
        Assert.Equal("ok", reply.Status);
        Assert.Equal("the final answer", reply.Text);

        using var streaming = new CodexSessionFixture();
        streaming.Server.AutoCompleteTurns = false;
        Task<AssistantReply> pending = streaming.Session.SendAsync("Check it.", CodexSessionFixture.Context());
        await Task.Yield();
        streaming.Server.Notify("item/agentMessage/delta",
            "{\"threadId\":\"thread-1\",\"turnId\":\"turn-1\",\"itemId\":\"a1\",\"delta\":\"streamed \"}");
        streaming.Server.Notify("item/agentMessage/delta",
            "{\"threadId\":\"thread-1\",\"turnId\":\"turn-1\",\"itemId\":\"a1\",\"delta\":\"answer\"}");
        streaming.Server.Notify("turn/completed",
            "{\"threadId\":\"thread-1\",\"turn\":{\"id\":\"turn-1\",\"status\":\"completed\",\"items\":[]}}");

        AssistantReply streamed = await pending;
        Assert.Equal("ok", streamed.Status);
        Assert.Equal("streamed answer", streamed.Text);
    }

    [Fact]
    public async Task KnownToolIsDispatchedThroughAssistantTools()
    {
        string path = Fixture("Meshes", "Actors", "Character", "Behaviors", "SingleAnimFurniture.hkx");
        AssistantTools tools = BuildRealTools(path);
        using var fixture = new CodexSessionFixture(tools.Functions);
        fixture.Server.AutoCompleteTurns = false;

        Task<AssistantReply> pending = fixture.Session.SendAsync("Inspect it.", CodexSessionFixture.Context());
        await Task.Yield();
        await EmitToolCallAsync(fixture, "call-1", "bgs_inspect_behavior",
            JsonSerializer.Serialize(new { path }));
        fixture.Server.Notify("turn/completed",
            "{\"threadId\":\"thread-1\",\"turn\":{\"id\":\"turn-1\",\"status\":\"completed\",\"items\":[]}}");

        AssistantReply reply = await pending;
        Assert.Equal("ok", reply.Status);
        Assert.Single(reply.Tools);
        Assert.Equal("bgs.inspect_behavior", reply.Tools[0].Name);
        Assert.Contains("\"success\":true", fixture.Server.LastToolResponse, StringComparison.Ordinal);
        Assert.Contains("inputText", fixture.Server.LastToolResponse, StringComparison.Ordinal);
        Assert.Contains("ok", fixture.Server.LastToolResponse, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownToolAndInvalidArgumentsAreRefused()
    {
        string path = Fixture("Meshes", "Actors", "Character", "Behaviors", "SingleAnimFurniture.hkx");
        AssistantTools tools = BuildRealTools(path);
        using var fixture = new CodexSessionFixture(tools.Functions);
        fixture.Server.AutoCompleteTurns = false;
        fixture.Server.Handlers["turn/start"] = _ => TurnPending();

        Task<AssistantReply> pending = fixture.Session.SendAsync("Inspect it.", CodexSessionFixture.Context());
        await Task.Yield();

        await EmitToolCallAsync(fixture, "call-unknown", "bgs_not_a_tool", "{}");
        Assert.Contains("\"success\":false", fixture.Server.LastToolResponse, StringComparison.Ordinal);

        await EmitToolCallAsync(fixture, "call-bad-args", "bgs_inspect_behavior", "{\"nope\":1}");
        Assert.Contains("\"success\":false", fixture.Server.LastToolResponse, StringComparison.Ordinal);

        fixture.Server.Notify("turn/completed",
            "{\"threadId\":\"thread-1\",\"turn\":{\"id\":\"turn-1\",\"status\":\"completed\",\"items\":[]}}");
        AssistantReply reply = await pending;
        Assert.Equal("ok", reply.Status);
        Assert.Empty(reply.Tools);
    }
    [Fact]
    public async Task MutationPreviewCannotAutoApproveItself()
    {
        string path = Fixture("Meshes", "Actors", "Character", "Behaviors", "SingleAnimFurniture.hkx");
        string xml = HkxTextEdit.TextOf(path);
        var document = new RecordingDocument(xml, path);
        var tools = new AssistantTools(new AssistantInspection(), new AssistantClipMutation(document),
            Allow(path));
        using var fixture = new CodexSessionFixture(tools.Functions, () => tools.HasPendingApproval);
        fixture.Server.AutoCompleteTurns = false;

        string id = BehaviourGraphModel.Parse(xml).Objects
            .First(item => item.Class == "hkbClipGenerator").Id;

        Task<AssistantReply> pending = fixture.Session.SendAsync("Change the clip.", CodexSessionFixture.Context());
        await Task.Yield();
        await EmitToolCallAsync(fixture, "call-1", "bgs_set_clip_animation",
            JsonSerializer.Serialize(new { objectId = id, animationName = "Animations\\Assistant\\Approved.hkx" }));
        fixture.Server.Notify("turn/completed",
            "{\"threadId\":\"thread-1\",\"turn\":{\"id\":\"turn-1\",\"status\":\"completed\",\"items\":[]}}");

        AssistantReply reply = await pending;
        Assert.True(reply.AwaitingApproval, fixture.Server.LastToolResponse);
        Assert.True(tools.HasPendingApproval);
        Assert.Equal(0, document.Revision);
        Assert.Equal(xml, document.Xml);

        var applied = tools.ApprovePendingClipAnimation();
        Assert.True(applied.Applied);
        Assert.False(applied.Saved);
        Assert.True(applied.IsDirty);
        Assert.Equal(1, document.Revision);
        Assert.Contains("Approved.hkx", document.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PerTurnToolBudgetIsEnforced()
    {
        using var fixture = new CodexSessionFixture(new[] { PlainTool("bgs.inspect_behavior") });
        fixture.Server.AutoCompleteTurns = false;

        Task<AssistantReply> pending = fixture.Session.SendAsync("Check it.", CodexSessionFixture.Context());
        await Task.Yield();

        for (int call = 1; call <= 6; call++)
        {
            await EmitToolCallAsync(fixture, "call-" + call, "bgs_inspect_behavior", "{}");
            Assert.Contains("\"success\":true", fixture.Server.LastToolResponse, StringComparison.Ordinal);
        }

        await EmitToolCallAsync(fixture, "call-7", "bgs_inspect_behavior", "{}");
        Assert.Contains("\"success\":false", fixture.Server.LastToolResponse, StringComparison.Ordinal);
        Assert.Contains("budget", fixture.Server.LastToolResponse, StringComparison.Ordinal);
        Assert.Equal(7, fixture.Server.ToolResponses);

        fixture.Server.Notify("turn/completed",
            "{\"threadId\":\"thread-1\",\"turn\":{\"id\":\"turn-1\",\"status\":\"completed\",\"items\":[]}}");
        AssistantReply reply = await pending;
        Assert.Equal(7, reply.Iterations);
    }

    [Fact]
    public async Task ToolResultsAreBounded()
    {
        var big = AIFunctionFactory.Create(
            () => new { status = "ok", payload = new string('x', 20000) }, "bgs.inspect_behavior", "test");
        using var fixture = new CodexSessionFixture(new[] { big });
        fixture.Server.AutoCompleteTurns = false;

        Task<AssistantReply> pending = fixture.Session.SendAsync("Check it.", CodexSessionFixture.Context());
        await Task.Yield();
        await EmitToolCallAsync(fixture, "call-1", "bgs_inspect_behavior", "{}");
        fixture.Server.Notify("turn/completed",
            "{\"threadId\":\"thread-1\",\"turn\":{\"id\":\"turn-1\",\"status\":\"completed\",\"items\":[]}}");
        await pending;

        JsonElement response = CodexSessionFixture.Parse(fixture.Server.LastToolResponse);
        string text = response.GetProperty("contentItems")[0].GetProperty("text").GetString()!;
        Assert.True(text.Length <= 4003, $"tool result was not bounded: {text.Length}");
        Assert.EndsWith("...", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisallowedBuiltInItemInterruptsTheTurn()
    {
        using var fixture = new CodexSessionFixture(new[] { PlainTool("bgs.inspect_behavior") });
        fixture.Server.AutoCompleteTurns = false;

        Task<AssistantReply> pending = fixture.Session.SendAsync("Check it.", CodexSessionFixture.Context());
        await Task.Yield();
        fixture.Server.Notify("item/started",
            "{\"threadId\":\"thread-1\",\"turnId\":\"turn-1\",\"item\":{\"id\":\"c1\"," +
            "\"type\":\"commandExecution\",\"command\":\"rm -rf\",\"status\":\"inProgress\"," +
            "\"commandActions\":[],\"cwd\":\"\"}}");

        AssistantReply reply = await pending;
        Assert.Equal("policy_error", reply.Status);
        Assert.True(fixture.Server.Interrupted);
        Assert.Contains("turn/interrupt", fixture.Server.Sent.Select(CodexSessionFixture.Method));
    }

    [Fact]
    public async Task ApprovalServerRequestsAreRefused()
    {
        using var fixture = new CodexSessionFixture();
        await fixture.Connection.ConnectAsync();
        var refusals = new List<string>();
        fixture.Connection.Client.RefusedServerRequest += method => refusals.Add(method);

        fixture.Server.EmitServerRequest("item/permissions/requestApproval", "77",
            "{\"threadId\":\"thread-1\",\"permissions\":{}}");
        await WaitUntil(() => refusals.Count > 0);

        Assert.Contains("item/permissions/requestApproval", refusals);
        Assert.Contains(fixture.Server.Sent, line => line.Contains("\"permissions\":{}", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Server.Sent, line =>
            line.Contains("dangerFullAccess", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessExitMidRequestBecomesABoundedProviderError()
    {
        using var fixture = new CodexSessionFixture();
        fixture.Server.FailMethodOnce = "turn/start";

        AssistantReply reply = await fixture.Session.SendAsync("Check it.", CodexSessionFixture.Context());

        Assert.Equal("provider_error", reply.Status);
        Assert.DoesNotContain("auth.json", reply.Text, StringComparison.Ordinal);
        Assert.True(reply.Text.Length <= 303);
    }

    [Fact]
    public async Task MalformedAndUnknownLinesAreIgnoredAndTheTurnStillCompletes()
    {
        using var fixture = new CodexSessionFixture();
        fixture.Server.AutoCompleteTurns = false;

        Task<AssistantReply> pending = fixture.Session.SendAsync("Check it.", CodexSessionFixture.Context());
        await Task.Yield();
        fixture.Server.EmitRaw("this is not json");
        fixture.Server.EmitRaw("{\"id\":");
        fixture.Server.EmitRaw("{\"method\":\"totally/unknown/notification\",\"params\":{}}");
        fixture.Server.Notify("turn/completed",
            "{\"threadId\":\"thread-1\",\"turn\":{\"id\":\"turn-1\",\"status\":\"completed\"," +
            "\"items\":[{\"id\":\"a1\",\"type\":\"agentMessage\",\"text\":\"survived\"}]}}");

        AssistantReply reply = await pending;
        Assert.Equal("ok", reply.Status);
        Assert.Equal("survived", reply.Text);
    }

    [Fact]
    public async Task CancellationInterruptsTheActiveTurn()
    {
        using var fixture = new CodexSessionFixture();
        fixture.Server.AutoCompleteTurns = false;
        using var cancellation = new CancellationTokenSource();

        Task<AssistantReply> pending = fixture.Session.SendAsync(
            "Check it.", CodexSessionFixture.Context(), cancellation.Token);
        await Task.Yield();
        cancellation.Cancel();

        AssistantReply reply = await pending;
        Assert.Equal("cancelled", reply.Status);
        Assert.True(fixture.Server.Interrupted);
    }

    [Fact]
    public async Task NewChatStartsAFreshThreadWithoutReusingContext()
    {
        using var fixture = new CodexSessionFixture();
        await fixture.Session.SendAsync("first", CodexSessionFixture.Context());
        string firstThread = fixture.Server.LastTurnStartParams!.Value.GetProperty("threadId").GetString()!;
        Assert.Equal(1, fixture.Server.ThreadStarts);

        fixture.Session.ClearHistory();
        await fixture.Session.SendAsync("second", CodexSessionFixture.Context());
        string secondThread = fixture.Server.LastTurnStartParams!.Value.GetProperty("threadId").GetString()!;

        Assert.Equal(2, fixture.Server.ThreadStarts);
        Assert.NotEqual(firstThread, secondThread);
        Assert.Equal("thread-1", firstThread);
        Assert.Equal("thread-2", secondThread);
    }

    [Fact]
    public async Task TurnFailedStatusBecomesProviderError()
    {
        using var fixture = new CodexSessionFixture();
        fixture.Server.AutoCompleteTurns = false;

        Task<AssistantReply> pending = fixture.Session.SendAsync("Check it.", CodexSessionFixture.Context());
        await Task.Yield();
        fixture.Server.Notify("turn/completed",
            "{\"threadId\":\"thread-1\",\"turn\":{\"id\":\"turn-1\",\"status\":\"failed\",\"items\":[]," +
            "\"error\":{\"message\":\"upstream unavailable\"}}}");

        AssistantReply reply = await pending;
        Assert.Equal("provider_error", reply.Status);
        Assert.Contains("upstream unavailable", reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedTurnErrorNotificationIsSurfacedAsProviderError()
    {
        using var fixture = new CodexSessionFixture();
        fixture.Server.AutoCompleteTurns = false;

        Task<AssistantReply> pending = fixture.Session.SendAsync("Check it.", CodexSessionFixture.Context());
        await Task.Yield();
        fixture.Server.Notify("error",
            "{\"threadId\":\"thread-1\",\"turnId\":\"turn-1\",\"willRetry\":false," +
            "\"error\":{\"message\":\"usage limit reached\"}}");
        fixture.Server.Notify("turn/completed",
            "{\"threadId\":\"thread-1\",\"turn\":{\"id\":\"turn-1\",\"status\":\"failed\",\"items\":[]}}");

        AssistantReply reply = await pending;
        Assert.Equal("provider_error", reply.Status);
        Assert.Contains("usage limit reached", reply.Text, StringComparison.Ordinal);
    }

    private static string TurnPending() =>
        "{\"turn\":{\"id\":\"turn-1\",\"items\":[],\"itemsView\":\"notLoaded\",\"status\":\"inProgress\"}}";

    private static async Task EmitToolCallAsync(
        CodexSessionFixture fixture, string callId, string tool, string argumentsJson)
    {
        int expected = fixture.Server.ToolResponses + 1;
        fixture.Server.LastToolName = tool;
        fixture.Server.EmitServerRequest("item/tool/call", "900",
            "{\"threadId\":\"thread-1\",\"turnId\":\"turn-1\",\"callId\":" +
            JsonSerializer.Serialize(callId) + ",\"namespace\":null,\"tool\":" +
            JsonSerializer.Serialize(tool) + ",\"arguments\":" + argumentsJson + "}");
        await WaitUntil(() => fixture.Server.ToolResponses >= expected);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
            await Task.Delay(10);
        Assert.True(condition(), "the condition was not reached in time");
    }

    private static AIFunction PlainTool(string name) =>
        AIFunctionFactory.Create(() => new { status = "ok" }, name, "test");

    private static AssistantTools BuildRealTools(string path) =>
        new(new AssistantInspection(), new AssistantClipMutation(new RecordingDocument("{ }", path)),
            Allow(path));

    private static AssistantPathAuthorization Allow(string expected) =>
        (string requested, out string canonical, out string code, out string message) =>
        {
            canonical = requested;
            code = "ok";
            message = "";
            return string.Equals(requested, expected, StringComparison.Ordinal);
        };

    private static string Fixture(params string[] parts) =>
        Path.Combine(new[] { AppContext.BaseDirectory, "fixtures", "vanilla" }.Concat(parts).ToArray());

    private sealed class RecordingDocument : IAssistantEditorDocument
    {
        public RecordingDocument(string xml, string id = "fixture.hkx")
        {
            Xml = xml;
            DocumentId = id;
        }

        public string DocumentId { get; }
        public string Xml { get; private set; }
        public long Revision { get; private set; }
        public bool IsDirty { get; private set; }

        public AssistantEditorSnapshot Snapshot() =>
            new(DocumentId, Revision, Xml, false, IsDirty, (int)Revision);

        public bool TryCommit(string xml, out string failure)
        {
            Xml = xml;
            Revision++;
            IsDirty = true;
            failure = "";
            return true;
        }
    }
}
