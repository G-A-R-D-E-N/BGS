using System;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Microsoft.Extensions.AI;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class CodexToolBoundaryTests
{
    [Fact]
    public async Task UnknownThreadToolCallFailsClosedWithoutExecution()
    {
        int calls = 0;
        AIFunction tool = AIFunctionFactory.Create(
            () => { calls++; return new { status = "ok" }; },
            "bgs.inspect_behavior", "test");
        using var fixture = new CodexSessionFixture(new[] { tool });
        fixture.Server.AutoCompleteTurns = false;

        Task<AssistantReply> pending = fixture.Session.SendAsync("inspect", CodexSessionFixture.Context());
        await WaitUntil(() => fixture.Server.LastTurnStartParams.HasValue);
        int expected = fixture.Server.ToolResponses + 1;
        fixture.Server.EmitServerRequest("item/tool/call", "901",
            "{\"threadId\":\"thread-other\",\"turnId\":\"turn-1\",\"callId\":\"call-1\"," +
            "\"namespace\":null,\"tool\":\"bgs_inspect_behavior\",\"arguments\":{}}");
        await WaitUntil(() => fixture.Server.ToolResponses >= expected);

        Assert.Equal(0, calls);
        Assert.Contains("\"success\":false", fixture.Server.LastToolResponse, StringComparison.Ordinal);
        Assert.Contains("not accepting tool calls", fixture.Server.LastToolResponse,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("active turn", fixture.Server.LastToolResponse,
            StringComparison.OrdinalIgnoreCase);

        Complete(fixture);
        await pending;
    }

    [Fact]
    public async Task WrongTurnToolCallIsRejectedWithoutExecution()
    {
        int calls = 0;
        AIFunction tool = AIFunctionFactory.Create(
            () => { calls++; return new { status = "ok" }; },
            "bgs.inspect_behavior", "test");
        using var fixture = new CodexSessionFixture(new[] { tool });
        fixture.Server.AutoCompleteTurns = false;

        Task<AssistantReply> pending = fixture.Session.SendAsync("inspect", CodexSessionFixture.Context());
        await WaitUntil(() => fixture.Server.LastTurnStartParams.HasValue);
        int expected = fixture.Server.ToolResponses + 1;
        fixture.Server.EmitServerRequest("item/tool/call", "902",
            "{\"threadId\":\"thread-1\",\"turnId\":\"turn-old\",\"callId\":\"call-2\"," +
            "\"namespace\":null,\"tool\":\"bgs_inspect_behavior\",\"arguments\":{}}");
        await WaitUntil(() => fixture.Server.ToolResponses >= expected);

        Assert.Equal(0, calls);
        Assert.Contains("\"success\":false", fixture.Server.LastToolResponse, StringComparison.Ordinal);

        Complete(fixture);
        await pending;
    }

    [Fact]
    public async Task ToolCallAfterTurnCompletionIsRejectedWithoutExecution()
    {
        int calls = 0;
        AIFunction tool = AIFunctionFactory.Create(
            () => { calls++; return new { status = "ok" }; },
            "bgs.inspect_behavior", "test");
        using var fixture = new CodexSessionFixture(new[] { tool });

        AssistantReply reply = await fixture.Session.SendAsync("inspect", CodexSessionFixture.Context());
        Assert.Equal("ok", reply.Status);

        int expected = fixture.Server.ToolResponses + 1;
        fixture.Server.EmitServerRequest("item/tool/call", "903",
            "{\"threadId\":\"thread-1\",\"turnId\":\"turn-1\",\"callId\":\"late\"," +
            "\"namespace\":null,\"tool\":\"bgs_inspect_behavior\",\"arguments\":{}}");
        await WaitUntil(() => fixture.Server.ToolResponses >= expected);

        Assert.Equal(0, calls);
        Assert.Contains("\"success\":false", fixture.Server.LastToolResponse, StringComparison.Ordinal);
    }

    private static void Complete(CodexSessionFixture fixture) =>
        fixture.Server.Notify("turn/completed",
            "{\"threadId\":\"thread-1\",\"turn\":{\"id\":\"turn-1\",\"status\":\"completed\",\"items\":[]}}");

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
            await Task.Delay(10);
        Assert.True(condition(), "the condition was not reached in time");
    }
}
