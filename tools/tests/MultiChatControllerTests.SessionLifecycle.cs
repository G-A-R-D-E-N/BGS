using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed partial class MultiChatControllerTests
{
    [Fact]
    public async Task ClearingChatDisposesItsSessionAndStartsFresh()
    {
        var created = new List<TrackableSession>();
        using var controller = new AssistantConversationController(_ =>
        {
            var session = new TrackableSession("answer");
            created.Add(session);
            return session;
        });
        await controller.SendAsync("old question", CodexSessionFixture.Context());

        Assert.True(controller.ClearActive());
        Assert.True(created[0].Disposed);
        Assert.Null(controller.ActiveSession);
        Assert.Empty(controller.Active!.Chat.Messages);
        Assert.Empty(AssistantChatStore.Load(controller.Active.Chat.Id)!.Messages);

        await controller.SendAsync("fresh question", CodexSessionFixture.Context());
        Assert.Equal(2, created.Count);
        Assert.DoesNotContain(controller.Active.Chat.Messages, message => message.Text == "old question");
    }

    [Fact]
    public async Task FailedClearPreservesChatAndSession()
    {
        var session = new TrackableSession("answer");
        using var controller = new AssistantConversationController(_ => session);
        await controller.SendAsync("old question", CodexSessionFixture.Context());
        var before = controller.Active!.Chat;
        AssistantChatStore.Folder = Path.Combine(_folder, "not-a-directory");
        File.WriteAllText(AssistantChatStore.Folder, "occupied");

        Assert.False(controller.ClearActive());
        Assert.Same(before, controller.Active.Chat);
        Assert.Same(session, controller.ActiveSession);
        Assert.False(session.Disposed);
    }

    [Fact]
    public void DeleteDisposesTheRemovedSession()
    {
        var session = new TrackableSession("reply");
        using var controller = new AssistantConversationController(_ => session);
        AssistantConversationEntry entry = controller.Active!;
        entry.Session = session;

        AssistantConversationEntry? after = controller.Delete(controller.List.ActiveIndex);

        Assert.NotNull(after);
        Assert.True(session.Disposed, "deleting a chat must dispose its session");
        Assert.Null(entry.Session);
    }

    [Fact]
    public async Task DeletingAChatDisposesAndUnregistersItsRealCodexThread()
    {
        using var connection = new FakeConnection();
        var session = new CodexAssistantSession(connection.Connection,
            new[] { Microsoft.Extensions.AI.AIFunctionFactory.Create(
                () => "ok", "bgs.inspect_behavior", "test") });
        using var controller = new AssistantConversationController(_ => session);
        AssistantConversationEntry entry = controller.Active!;

        await controller.SendAsync("hello", CodexSessionFixture.Context()).ConfigureAwait(false);
        string thread = connection.Server.LastThreadId;
        Assert.False(string.IsNullOrEmpty(thread));
        Assert.Equal("1", connection.Connection.Client.RegisteredToolThreadCount);

        controller.Delete(controller.List.ActiveIndex);

        Assert.Equal("0", connection.Connection.Client.RegisteredToolThreadCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => session.SendAsync("again", CodexSessionFixture.Context()));

        int expected = connection.Server.ToolResponses + 1;
        connection.Server.EmitServerRequest(CodexMethods.DynamicToolCall, "\"s:700\"",
            "{\"threadId\":\"" + thread +
            "\",\"turnId\":\"turn-x\",\"callId\":\"c-x\",\"tool\":\"bgs_inspect_behavior\",\"arguments\":{}}");
        for (int attempt = 0; attempt < 200 && connection.Server.ToolResponses < expected; attempt++)
            await Task.Delay(10);
        Assert.Equal(expected, connection.Server.ToolResponses);
        Assert.Contains("\"success\":false", connection.Server.LastToolResponse,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LateReplyAfterLogoutCannotMutateTheChat()
    {
        var session = new BlockingSession(awaitingApproval: true);
        using var controller = new AssistantConversationController(_ => session);
        var gate = new AssistantMutationGate(
            () => false, () => { }, () => AssistantTools.NoPendingResult());
        controller.Mutations = gate;
        AssistantConversationEntry entry = controller.Active!;

        Task<AssistantReply> pending = controller.SendAsync("slow", CodexSessionFixture.Context());
        await session.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        controller.EndAllSessions();
        session.Complete();
        AssistantReply reply = await pending;

        Assert.Equal("ok", reply.Status);
        Assert.Empty(entry.Chat.Messages);
        Assert.False(entry.SessionPrimed);
        Assert.False(entry.PendingApproval);
        Assert.False(entry.ApprovalActive);
        Assert.Null(gate.OwnerChatId);
        Assert.Empty(AssistantChatStore.Load(entry.Chat.Id)!.Messages);
    }

    [Fact]
    public async Task AReplacedServerStillGetsTheReplayPrompt()
    {
        using var connection = new FakeConnection();
        var session = new CodexAssistantSession(connection.Connection,
            new[] { Microsoft.Extensions.AI.AIFunctionFactory.Create(
                () => "ok", "bgs.inspect_behavior", "test") });
        IAssistantReplayAware replayAware = session;

        await session.SendAsync("first", CodexSessionFixture.Context()).ConfigureAwait(false);
        string firstThread = connection.Server.LastThreadId;

        connection.Server.Exit();

        await replayAware.SendAsync("bare-second", "REPLAY-MARKER second",
            CodexSessionFixture.Context()).ConfigureAwait(false);
        string secondThread = connection.Server.LastThreadId;

        Assert.NotEqual(firstThread, secondThread);
        Assert.Contains("REPLAY-MARKER", TurnInput(connection.Server), StringComparison.Ordinal);

        await replayAware.SendAsync("bare-third", "REPLAY-MARKER third",
            CodexSessionFixture.Context()).ConfigureAwait(false);

        Assert.Equal(secondThread, connection.Server.LastThreadId);
        Assert.Contains("bare-third", TurnInput(connection.Server), StringComparison.Ordinal);
        Assert.DoesNotContain("REPLAY-MARKER", TurnInput(connection.Server), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignOutEndsEverySessionAndUnregistersItsThread()
    {
        using var connection = new FakeConnection();
        var created = new List<CodexAssistantSession>();
        Func<string, IAssistantSession?> factory = _ =>
        {
            var session = new CodexAssistantSession(connection.Connection,
                new[] { Microsoft.Extensions.AI.AIFunctionFactory.Create(
                    () => "ok", "bgs.inspect_behavior", "test") });
            created.Add(session);
            return session;
        };
        using var controller = new AssistantConversationController(factory);
        AssistantConversationEntry a = controller.Active!;
        await controller.SendAsync("first", CodexSessionFixture.Context()).ConfigureAwait(false);
        AssistantConversationEntry b = controller.NewChat()!;
        await controller.SendAsync("second", CodexSessionFixture.Context()).ConfigureAwait(false);

        Assert.Equal(2, created.Count);
        Assert.Equal("2", connection.Connection.Client.RegisteredToolThreadCount);
        string threadBefore = connection.Server.LastThreadId;

        controller.EndAllSessions();

        Assert.Equal("0", connection.Connection.Client.RegisteredToolThreadCount);
        Assert.Null(a.Session);
        Assert.Null(b.Session);
        Assert.False(a.SessionPrimed);
        Assert.False(b.SessionPrimed);
        Assert.True(AssistantChatStore.Exists(a.Chat.Id));
        Assert.True(AssistantChatStore.Exists(b.Chat.Id));
        foreach (CodexAssistantSession session in created)
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => session.SendAsync("after sign out", CodexSessionFixture.Context()));

        Assert.True(controller.TrySelectById(a.Chat.Id));
        await controller.SendAsync("third", CodexSessionFixture.Context()).ConfigureAwait(false);

        Assert.Equal(3, created.Count);
        Assert.NotEqual(threadBefore, connection.Server.LastThreadId);
        Assert.Contains("# BGS chat history", TurnInput(connection.Server), StringComparison.Ordinal);
        Assert.Equal("1", connection.Connection.Client.RegisteredToolThreadCount);

        await controller.SendAsync("fourth", CodexSessionFixture.Context()).ConfigureAwait(false);
        Assert.DoesNotContain("# BGS chat history", TurnInput(connection.Server), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppServerReplacementNeverReusesTheOldThreadIdAndReplaysOnce()
    {
        using var connection = new FakeConnection();
        var session = new CodexAssistantSession(connection.Connection,
            new[] { Microsoft.Extensions.AI.AIFunctionFactory.Create(
                () => "ok", "bgs.inspect_behavior", "test") });
        using var controller = new AssistantConversationController(_ => session);

        await controller.SendAsync("one", CodexSessionFixture.Context()).ConfigureAwait(false);
        await controller.SendAsync("two", CodexSessionFixture.Context()).ConfigureAwait(false);
        string firstThread = connection.Server.LastThreadId;
        Assert.False(string.IsNullOrEmpty(firstThread));

        connection.Server.Exit();

        await controller.SendAsync("three", CodexSessionFixture.Context()).ConfigureAwait(false);
        string secondThread = connection.Server.LastThreadId;
        Assert.NotEqual(firstThread, secondThread);
        Assert.Contains("# BGS chat history", TurnInput(connection.Server), StringComparison.Ordinal);

        await controller.SendAsync("fourth", CodexSessionFixture.Context()).ConfigureAwait(false);
        Assert.Equal(secondThread, connection.Server.LastThreadId);
        Assert.DoesNotContain("# BGS chat history", TurnInput(connection.Server), StringComparison.Ordinal);
    }

}
