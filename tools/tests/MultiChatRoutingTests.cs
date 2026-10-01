using System;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Microsoft.Extensions.AI;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class MultiChatRoutingTests
{
    [Fact]
    public async Task ConcurrentSessionsRouteDynamicToolsByThreadId()
    {
        using var fixture = new FakeConnection();
        fixture.Server.AutoCompleteTurns = false;

        using var sessionA = new CodexAssistantSession(fixture.Connection,
            new[] { AIFunctionFactory.Create(() => "answer-a", "bgs.inspect_behavior", "test") });
        using var sessionB = new CodexAssistantSession(fixture.Connection,
            new[] { AIFunctionFactory.Create(() => "answer-b", "bgs.inspect_behavior", "test") });

        Task<AssistantReply> sendA = sessionA.SendAsync("a", CodexSessionFixture.Context());
        await WaitUntil(() => fixture.Server.ThreadStarts >= 1);

        Task<AssistantReply> sendB = sessionB.SendAsync("b", CodexSessionFixture.Context());
        await WaitUntil(() => fixture.Server.ThreadStarts >= 2 && fixture.Server.TurnStarts >= 2);

        string threadA = "thread-1";
        string threadB = "thread-2";
        string turnA = "turn-1";
        string turnB = "turn-2";
        Assert.NotEqual(threadA, threadB);

        await EmitToolCallAsync(fixture, threadB, turnB, "c-b");
        Assert.Contains("answer-b", fixture.Server.LastToolResponse, StringComparison.Ordinal);
        Assert.DoesNotContain("answer-a", fixture.Server.LastToolResponse, StringComparison.Ordinal);

        await EmitToolCallAsync(fixture, threadA, turnA, "c-a");
        Assert.Contains("answer-a", fixture.Server.LastToolResponse, StringComparison.Ordinal);
        Assert.DoesNotContain("answer-b", fixture.Server.LastToolResponse, StringComparison.Ordinal);

        await EmitToolCallAsync(fixture, "thread-ghost", "turn-ghost", "c-g");
        Assert.Contains("\"success\":false", fixture.Server.LastToolResponse, StringComparison.OrdinalIgnoreCase);

        fixture.Server.Notify(CodexMethods.TurnCompleted,
            "{\"threadId\":\"" + threadB + "\",\"turn\":{\"id\":\"" + turnB + "\",\"status\":\"completed\",\"items\":[]}}");
        fixture.Server.Notify(CodexMethods.TurnCompleted,
            "{\"threadId\":\"" + threadA + "\",\"turn\":{\"id\":\"" + turnA + "\",\"status\":\"completed\",\"items\":[]}}");

        AssistantReply replyA = await sendA;
        AssistantReply replyB = await sendB;
        Assert.Equal("ok", replyA.Status);
        Assert.Equal("ok", replyB.Status);

        sessionA.Dispose();
        await EmitToolCallAsync(fixture, threadA, turnA, "c-after");
        Assert.Contains("\"success\":false", fixture.Server.LastToolResponse, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task EmitToolCallAsync(
        FakeConnection fixture, string threadId, string turnId, string callId)
    {
        int expected = fixture.Server.ToolResponses + 1;
        fixture.Server.EmitServerRequest(CodexMethods.DynamicToolCall, "\"s:100\"",
            "{\"threadId\":\"" + threadId + "\",\"turnId\":\"" + turnId + "\",\"callId\":\"" + callId +
            "\",\"tool\":\"bgs_inspect_behavior\",\"arguments\":{}}");
        await WaitUntil(() => fixture.Server.ToolResponses >= expected);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 500 && !condition(); attempt++)
            await Task.Delay(10);
        Assert.True(condition(), "the condition was not reached in time");
    }
}