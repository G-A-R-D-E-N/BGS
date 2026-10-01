using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

[Collection("AssistantChatStore")]
public sealed partial class MultiChatControllerTests : IDisposable
{
    private readonly string _folder;
    private readonly string _previousFolder;

    public MultiChatControllerTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "bgs-assistant-ctrl-" + Guid.NewGuid().ToString("N"));
        _previousFolder = AssistantChatStore.Folder;
        AssistantChatStore.Folder = _folder;
    }

    public void Dispose()
    {
        AssistantChatStore.DeleteForTest = null;
        AssistantChatStore.Folder = _previousFolder;
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void ControllerSeedsInitialChatWhenNoneSaved()
    {
        var factory = MakeFactory();
        using var controller = new AssistantConversationController(factory);

        Assert.NotNull(controller.Active);
        Assert.NotNull(controller.Active!.Chat);
        Assert.NotNull(controller.List.ActiveChatId);
        Assert.True(AssistantChatStore.Exists(controller.Active.Chat.Id));
    }

    [Fact]
    public void ControllerRestoresPreviouslySavedChatsOnRestart()
    {
        AssistantChat savedA = Build("alpha", "alpha-1");
        AssistantChat savedB = Build("beta", "beta-1");
        AssistantChatStore.Save(savedA);
        AssistantChatStore.Save(savedB);

        var factory = MakeFactory();
        using var controller = new AssistantConversationController(factory,
            initialChats: new[] { savedA, savedB },
            activeChatId: savedB.Id);

        Assert.Equal(2, controller.List.Count);
        Assert.Equal(savedB.Id, controller.List.ActiveChatId);
    }

    [Fact]
    public void NewChatCreatesAndSelectsAFreshConversation()
    {
        var factory = MakeFactory();
        using var controller = new AssistantConversationController(factory);

        AssistantConversationEntry? before = controller.Active;
        AssistantConversationEntry? created = controller.NewChat();

        Assert.NotNull(created);
        Assert.NotNull(before);
        Assert.NotEqual(before!.Chat.Id, created!.Chat.Id);
        Assert.Equal(created.Chat.Id, controller.Active!.Chat.Id);
        Assert.Equal("New chat", created.Chat.Title);
    }

    [Fact]
    public void SwitchAndBackReturnsOriginalTranscript()
    {
        var factory = new Dictionary<string, Func<string, IAssistantSession?>>
        {
            [""] = _ => new FixedReplySession("hello world"),
        };
        Func<string, IAssistantSession?> sessionFactory =
            id => factory.TryGetValue(id, out var f) ? f(id)
                 : (factory.TryGetValue("", out var def) ? def(id) : null);
        using var controller = new AssistantConversationController(sessionFactory);

        AssistantConversationEntry a = controller.Active!;
        AssistantConversationEntry b = controller.NewChat()!;
        Assert.Equal(b.Chat.Id, controller.Active!.Chat.Id);

        Assert.True(controller.TrySelectById(a.Chat.Id));

        Assert.Equal(a.Chat.Id, controller.List.ActiveChatId);
    }

    [Fact]
    public void RenameUpdatesTitleAndPersists()
    {
        var factory = MakeFactory();
        using var controller = new AssistantConversationController(factory);
        AssistantConversationEntry? entry = controller.Active;

        Assert.True(controller.Rename(controller.List.ActiveIndex, "Renamed"));

        AssistantChat? loaded = AssistantChatStore.Load(entry!.Chat.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Renamed", loaded!.Title);
    }

    [Fact]
    public void DeleteRemovesFromDisk()
    {
        var factory = MakeFactory();
        using var controller = new AssistantConversationController(factory);

        AssistantConversationEntry a = controller.Active!;
        AssistantConversationEntry b = controller.NewChat()!;

        Assert.True(controller.TrySelectById(b.Chat.Id));
        int idxB = controller.List.IndexOf(b.Chat.Id);
        Assert.True(controller.TrySelectById(a.Chat.Id));

        AssistantConversationEntry? after = controller.Delete(idxB);
        Assert.NotNull(after);
        Assert.False(AssistantChatStore.Exists(b.Chat.Id));
    }

    [Fact]
    public void DeleteFinalChatLeavesAtLeastOneEntry()
    {
        var factory = MakeFactory();
        using var controller = new AssistantConversationController(factory);

        AssistantConversationEntry? after = controller.Delete(controller.List.ActiveIndex);
        Assert.NotNull(after);
        Assert.True(controller.List.Count >= 1);
        Assert.NotNull(controller.Active);
    }

    [Fact]
    public async Task CompletionDuringDeletionCannotResurrectHistory()
    {
        var session = new BlockingSession();
        using var controller = new AssistantConversationController(_ => session);
        AssistantConversationEntry entry = controller.Active!;
        string path = AssistantChatStore.FilePathFor(entry.Chat.Id);
        Assert.True(File.Exists(path));

        Task<AssistantReply> pending = controller.SendAsync("slow", CodexSessionFixture.Context());
        await session.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var deletedOnDisk = new ManualResetEventSlim(false);
        var completionFinished = new ManualResetEventSlim(false);
        AssistantChatStore.DeleteForTest = _ =>
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { return false; }
            deletedOnDisk.Set();
            completionFinished.Wait(TimeSpan.FromSeconds(10));
            return !File.Exists(path);
        };
        try
        {
            Task<AssistantConversationEntry?> deleting =
                Task.Run(() => controller.Delete(controller.List.ActiveIndex));
            Assert.True(deletedOnDisk.Wait(TimeSpan.FromSeconds(10)),
                "delete never reached the disk step");

            session.Complete();
            completionFinished.Set();
            AssistantConversationEntry? after = await deleting;
            AssistantReply reply = await pending;
            Assert.Equal("ok", reply.Status);

            Assert.NotNull(after);
            Assert.False(File.Exists(path),
                "a completion that races deletion must not recreate the chat file");
        }
        finally
        {
            completionFinished.Set();
            AssistantChatStore.DeleteForTest = null;
        }
    }

    [Fact]
    public async Task FailedDiskDeleteKeepsTheChatVisibleAndUsable()
    {
        using var controller = new AssistantConversationController(MakeFactory());
        AssistantConversationEntry entry = controller.Active!;
        AssistantChatStore.DeleteForTest = _ => false;
        try
        {
            AssistantConversationEntry? after = controller.Delete(controller.List.ActiveIndex);

            Assert.Null(after);
            Assert.Equal(1, controller.List.Count);
            Assert.Equal(entry.Chat.Id, controller.List.ActiveChatId);
            Assert.True(AssistantChatStore.Exists(entry.Chat.Id));
            Assert.False(entry.Deleted);
        }
        finally
        {
            AssistantChatStore.DeleteForTest = null;
        }

        AssistantReply reply = await controller.SendAsync("after failed delete", CodexSessionFixture.Context())
            .ConfigureAwait(false);
        Assert.Equal("ok", reply.Status);
        Assert.True(AssistantChatStore.Exists(entry.Chat.Id));
    }

    [Fact]
    public async Task CompletionDuringFailedDeleteIsRetained()
    {
        var session = new BlockingSession();
        using var controller = new AssistantConversationController(_ => session);
        AssistantConversationEntry entry = controller.Active!;

        Task<AssistantReply> pending = controller.SendAsync("slow", CodexSessionFixture.Context());
        await session.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var hookEntered = new ManualResetEventSlim(false);
        var releaseHook = new ManualResetEventSlim(false);
        AssistantChatStore.DeleteForTest = _ =>
        {
            hookEntered.Set();
            releaseHook.Wait(TimeSpan.FromSeconds(10));
            return false;
        };
        try
        {
            Task<AssistantConversationEntry?> deleting =
                Task.Run(() => controller.Delete(controller.List.ActiveIndex));
            Assert.True(hookEntered.Wait(TimeSpan.FromSeconds(10)),
                "delete never reached the disk step");

            session.Complete();
            await Task.Delay(200);
            bool completionWaited = !pending.IsCompleted;
            releaseHook.Set();

            AssistantConversationEntry? after = await deleting;
            AssistantReply reply = await pending;

            Assert.Null(after);
            Assert.Equal(1, controller.List.Count);
            Assert.True(completionWaited,
                "a completion must wait for the failed delete before it can be dropped");
            Assert.Equal("ok", reply.Status);
            Assert.True(entry.SessionPrimed);
            Assert.Contains(AssistantChatStore.Load(entry.Chat.Id)!.Messages,
                m => m.Role == AssistantChatRole.Assistant);
        }
        finally
        {
            releaseHook.Set();
            AssistantChatStore.DeleteForTest = null;
        }
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("policy_error")]
    [InlineData("tool_policy_error")]
    [InlineData("signed_out")]
    [InlineData("provider_error")]
    [InlineData("error")]
    [InlineData("busy")]
    public async Task BgsGeneratedStatusTextIsNotPersistedOrReplayed(string status)
    {
        using var controller = new AssistantConversationController(
            _ => new FixedReplySession("BGS-NOTICE-" + status, status: status));
        AssistantConversationEntry entry = controller.Active!;

        AssistantReply reply = await controller.SendAsync("hello", CodexSessionFixture.Context())
            .ConfigureAwait(false);

        Assert.Equal(status, reply.Status);
        Assert.Contains(entry.Chat.Messages, m => m.Role == AssistantChatRole.User);
        Assert.DoesNotContain(entry.Chat.Messages, m => m.Role == AssistantChatRole.Assistant);
        Assert.DoesNotContain("BGS-NOTICE",
            AssistantConversationController.ComposeReplay(entry.Chat, "next"), StringComparison.Ordinal);
        Assert.DoesNotContain(AssistantChatStore.Load(entry.Chat.Id)!.Messages,
            m => m.Role == AssistantChatRole.Assistant);
    }

    [Fact]
    public void ComposeReplayUsesTheStoreReplayBoundExactly()
    {
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        DateTime stamp = DateTime.UtcNow;
        int pairs = AssistantChatStore.MaxReplayTurns + 5;
        for (int i = 0; i < pairs; i++)
        {
            chat = AssistantChatStore.AppendMessage(chat,
                new AssistantChatMessage(AssistantChatRole.User, "u" + i, stamp.AddMinutes(i)));
            chat = AssistantChatStore.AppendMessage(chat,
                new AssistantChatMessage(AssistantChatRole.Assistant, "a" + i, stamp.AddMinutes(i).AddSeconds(1)));
        }

        string replay = AssistantConversationController.ComposeReplay(chat, "current");

        string nl = Environment.NewLine;
        int firstRetained = pairs - AssistantChatStore.MaxReplayTurns;
        Assert.Equal(AssistantChatStore.MaxReplayTurns, CountOf(replay, "[user] "));
        Assert.Equal(AssistantChatStore.MaxReplayTurns, CountOf(replay, "[assistant] "));
        Assert.Contains("[user] u" + firstRetained + nl, replay, StringComparison.Ordinal);
        Assert.Contains("[assistant] a" + (pairs - 1) + nl, replay, StringComparison.Ordinal);
        Assert.DoesNotContain("[user] u" + (firstRetained - 1) + nl, replay, StringComparison.Ordinal);
        Assert.EndsWith("# Current message" + nl + "current", replay, StringComparison.Ordinal);
        Assert.Equal(replay,
            AssistantConversationController.ComposeReplay(chat, "current"));
    }

    private static int CountOf(string text, string token)
    {
        int count = 0;
        for (int index = text.IndexOf(token, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    [Fact]
    public void MaxConversationsIsEnforced()
    {
        var factory = MakeFactory();
        using var controller = new AssistantConversationController(factory);
        for (int i = 0; i < AssistantChatStore.MaxConversations - 1; i++)
            controller.NewChat();

        Assert.Equal(AssistantChatStore.MaxConversations, controller.List.Count);

        for (int i = 0; i < 8; i++) controller.NewChat();

        Assert.Equal(AssistantChatStore.MaxConversations, controller.List.Count);
    }

    [Fact]
    public async Task SendPersistsUserAndAssistantMessagesButNotToolRows()
    {
        var registered = new Dictionary<string, IAssistantSession>();
        Func<string, IAssistantSession?> factory = chatId =>
        {
            if (registered.TryGetValue(chatId, out var session)) return session;
            var created = new FixedReplySession("ok-from-model", tools: new[]
            {
                new AssistantToolActivity("bgs.inspect_behavior", false),
            });
            registered[chatId] = created;
            return created;
        };
        using var controller = new AssistantConversationController(factory);
        AssistantConversationEntry entry = controller.Active!;

        AssistantReply reply = await controller.SendAsync("hello", CodexSessionFixture.Context())
            .ConfigureAwait(false);
        Assert.Equal("ok", reply.Status);

        AssistantChat? loaded = AssistantChatStore.Load(entry.Chat.Id);
        Assert.NotNull(loaded);
        Assert.Contains(loaded.Messages, m => m.Role == AssistantChatRole.User);
        Assert.Contains(loaded.Messages, m => m.Role == AssistantChatRole.Assistant);
        Assert.DoesNotContain(loaded.Messages, m => m.Role == AssistantChatRole.Tool);
        Assert.Equal(2, loaded.Messages.Count);
    }

    [Fact]
    public async Task RestoredSessionReplaysBoundedHistoryExactlyOnce()
    {
        var recorded = new List<string>();
        var sessions = new Dictionary<string, IAssistantSession>();
        Func<string, IAssistantSession?> factory = chatId =>
        {
            if (sessions.TryGetValue(chatId, out var session)) return session;
            var created = new RecordingReplySession(replied =>
            {
                recorded.Add(replied);
                return new AssistantReply("ok", "reply", 0, Array.Empty<AssistantToolActivity>(), false);
            });
            sessions[chatId] = created;
            return created;
        };

        using var controller = new AssistantConversationController(factory);
        AssistantConversationEntry entry = controller.Active!;

        AssistantChat seeded = entry.Chat;
        DateTime stamp = DateTime.UtcNow.AddMinutes(-10);
        seeded = AssistantChatStore.AppendMessage(seeded,
            new AssistantChatMessage(AssistantChatRole.User, "first question", stamp));
        seeded = AssistantChatStore.AppendMessage(seeded,
            new AssistantChatMessage(AssistantChatRole.Assistant, "first answer", stamp.AddSeconds(1)));
        AssistantChatStore.Save(seeded);
        entry.Chat = seeded;

        await controller.SendAsync("second question", CodexSessionFixture.Context())
            .ConfigureAwait(false);
        await controller.SendAsync("third question", CodexSessionFixture.Context())
            .ConfigureAwait(false);

        Assert.Equal(2, recorded.Count);
        Assert.Contains("first question", recorded[0]);
        Assert.Contains("first answer", recorded[0]);
        Assert.Contains("second question", recorded[0]);
        Assert.DoesNotContain("tool", recorded[0], StringComparison.OrdinalIgnoreCase);

        Assert.Equal("third question", recorded[1]);
        Assert.DoesNotContain("first question", recorded[1]);
    }

    [Fact]
    public async Task NewChatReplaysItsOwnHistoryOnceThenSendsBarePrompts()
    {
        var recorded = new List<string>();
        var sessions = new Dictionary<string, IAssistantSession>();
        Func<string, IAssistantSession?> factory = chatId =>
        {
            if (sessions.TryGetValue(chatId, out var session)) return session;
            var created = new RecordingReplySession(replied =>
            {
                recorded.Add(replied);
                return new AssistantReply("ok", "reply", 0, Array.Empty<AssistantToolActivity>(), false);
            });
            sessions[chatId] = created;
            return created;
        };

        using var controller = new AssistantConversationController(factory);
        await controller.SendAsync("one", CodexSessionFixture.Context()).ConfigureAwait(false);

        Assert.Single(recorded);
        Assert.Equal("one", recorded[0]);

        await controller.SendAsync("two", CodexSessionFixture.Context()).ConfigureAwait(false);
        Assert.Equal(2, recorded.Count);
        Assert.Equal("two", recorded[1]);
    }

    [Fact]
    public void OversizedPromptIsRejectedBeforeAnySessionStarts()
    {
        int factoryCalls = 0;
        Func<string, IAssistantSession?> factory = _ =>
        {
            factoryCalls++;
            return new FixedReplySession("reply");
        };
        using var controller = new AssistantConversationController(factory);
        AssistantConversationEntry entry = controller.Active!;

        AssistantReply reply = controller
            .SendAsync(new string('x', AssistantChatStore.MaxPromptCharacters + 1),
                CodexSessionFixture.Context())
            .GetAwaiter().GetResult();

        Assert.Equal("error", reply.Status);
        Assert.Equal(0, factoryCalls);
        Assert.Empty(entry.Chat.Messages);
    }

    [Fact]
    public async Task FirstUserMessageDerivesAndPersistsATitle()
    {
        using var controller = new AssistantConversationController(MakeFactory());
        AssistantConversationEntry entry = controller.Active!;
        Assert.Equal("New chat", entry.Chat.Title);

        await controller.SendAsync("How do I fix a broken clip?", CodexSessionFixture.Context())
            .ConfigureAwait(false);

        Assert.Equal("How do I fix a broken clip?", entry.Chat.Title);
        Assert.Equal(entry.Chat.Title, AssistantChatStore.Load(entry.Chat.Id)!.Title);
    }

    [Fact]
    public async Task MostRecentlyUpdatedChatSortsToTheTop()
    {
        using var controller = new AssistantConversationController(MakeFactory());
        AssistantConversationEntry a = controller.Active!;
        await Task.Delay(50);
        AssistantConversationEntry b = controller.NewChat()!;

        Assert.Equal(b.Chat.Id, controller.List.Summaries()[0].Id);

        await Task.Delay(50);
        Assert.True(controller.TrySelectById(a.Chat.Id));
        await controller.SendAsync("update a", CodexSessionFixture.Context()).ConfigureAwait(false);

        Assert.Equal(a.Chat.Id, controller.List.Summaries()[0].Id);
    }

    [Fact]
    public void RestoredToolRowsDoNotMarkTheChatAsPendingApproval()
    {
        AssistantChat chat = Build("tool-chat", "inspect it");
        chat = AssistantChatStore.AppendMessage(chat, new AssistantChatMessage(
            AssistantChatRole.Tool, "bgs.set_clip_animation (pending approval)", DateTime.UtcNow,
            ToolName: "bgs.set_clip_animation", ToolStatus: "pending approval",
            ToolRequiresApproval: true));
        AssistantChatStore.Save(chat);

        using var controller = new AssistantConversationController(MakeFactory(),
            initialChats: new[] { chat }, activeChatId: chat.Id);

        Assert.False(controller.Active!.PendingApproval);
        Assert.False(controller.Active!.ApprovalActive);
    }

    [Fact]
    public void SwitchRefusedWhileBusy()
    {
        var factory = MakeFactory();
        using var controller = new AssistantConversationController(factory);
        controller.List.IsBusy = true;

        AssistantConversationEntry a = controller.Active!;
        _ = controller.NewChat();

        Assert.False(controller.TrySelectById(a.Chat.Id));
    }

    [Fact]
    public void ApproveClearsPendingApprovalAndNeverRestoresIt()
    {
        var factory = MakeFactory();
        using var controller = new AssistantConversationController(factory);
        AssistantConversationEntry entry = controller.Active!;
        entry.MarkPendingApproval();

        controller.ApproveActive(entry);
        Assert.False(entry.ApprovalActive);

        entry = controller.NewChat()!;
        Assert.False(entry.ApprovalActive,
            "switching/NewChat must not carry approval state between conversations");
    }

    [Fact]
    public async Task SaveFailureIsReportedAndTheChatStaysUsable()
    {
        using var controller = new AssistantConversationController(MakeFactory());
        AssistantConversationEntry entry = controller.Active!;
        AssistantChatStore.SaveFailureForTest = () => throw new IOException("simulated disk failure");
        try
        {
            AssistantReply reply = await controller.SendAsync("hello", CodexSessionFixture.Context())
                .ConfigureAwait(false);

            Assert.Equal("ok", reply.Status);
            Assert.Equal(AssistantConversationController.NotSavedMessage, reply.PersistenceNotice);
            Assert.Contains(entry.Chat.Messages, m => m.Role == AssistantChatRole.Assistant);
            AssistantChat? onDisk = AssistantChatStore.Load(entry.Chat.Id);
            Assert.NotNull(onDisk);
            Assert.DoesNotContain(onDisk!.Messages, m => m.Role == AssistantChatRole.Assistant);
        }
        finally
        {
            AssistantChatStore.SaveFailureForTest = null;
        }

        AssistantReply next = await controller.SendAsync("again", CodexSessionFixture.Context())
            .ConfigureAwait(false);
        Assert.Equal("", next.PersistenceNotice);
        Assert.Contains(AssistantChatStore.Load(entry.Chat.Id)!.Messages,
            m => m.Role == AssistantChatRole.Assistant);
    }

    [Fact]
    public async Task CredentialPromptIsRedactedAndNeverPersisted()
    {
        using var controller = new AssistantConversationController(MakeFactory());
        AssistantConversationEntry entry = controller.Active!;
        string secret = "sk-" + new string('a', 22);

        AssistantReply reply = await controller.SendAsync(
            "here is my key " + secret, CodexSessionFixture.Context()).ConfigureAwait(false);

        Assert.Equal("ok", reply.Status);
        Assert.Equal(AssistantConversationController.RedactedMessage, reply.PersistenceNotice);
        string text = File.ReadAllText(AssistantChatStore.FilePathFor(entry.Chat.Id));
        Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        Assert.Contains("redacted", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenameFailureIsNonfatal()
    {
        using var controller = new AssistantConversationController(MakeFactory());
        AssistantConversationEntry entry = controller.Active!;
        string original = entry.Chat.Title;
        AssistantChatStore.SaveFailureForTest = () => throw new IOException("simulated disk failure");
        try
        {
            Assert.False(controller.Rename(controller.List.ActiveIndex, "Renamed"));
        }
        finally
        {
            AssistantChatStore.SaveFailureForTest = null;
        }
        Assert.Equal(original, entry.Chat.Title);
    }

    [Fact]
    public void NewChatFailureFallsBackToAnInMemoryChat()
    {
        using var controller = new AssistantConversationController(MakeFactory());
        int before = controller.List.Count;
        AssistantChatStore.SaveFailureForTest = () => throw new IOException("simulated disk failure");
        try
        {
            AssistantConversationEntry? created = controller.NewChat();

            Assert.NotNull(created);
            Assert.Equal(before + 1, controller.List.Count);
            Assert.False(AssistantChatStore.Exists(created!.Chat.Id));
        }
        finally
        {
            AssistantChatStore.SaveFailureForTest = null;
        }
    }

    private static string TurnInput(CodexTestServer server)
    {
        if (server.LastTurnStartParams is not { } parameters) return "";
        if (!parameters.TryGetProperty("input", out System.Text.Json.JsonElement input) ||
            input.ValueKind != System.Text.Json.JsonValueKind.Array)
            return "";
        return string.Join("\n", input.EnumerateArray().Select(element =>
            element.TryGetProperty("text", out System.Text.Json.JsonElement text) ? text.GetString() : null));
    }

    private static Func<string, IAssistantSession?> MakeFactory() =>
        _ => new FixedReplySession("hello world");

    private static AssistantChat Build(string title, string firstMessage)
    {
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        chat = chat.WithTitle(title);
        chat = AssistantChatStore.AppendMessage(chat,
            new AssistantChatMessage(AssistantChatRole.User, firstMessage, DateTime.UtcNow));
        return chat;
    }

    private sealed class FixedReplySession : IAssistantSession
    {
        public FixedReplySession(string text,
            IReadOnlyList<AssistantToolActivity>? tools = null, string status = "ok")
        {
            Text = text;
            Tools = tools ?? Array.Empty<AssistantToolActivity>();
            Status = status;
        }

        public string Text { get; }
        public IReadOnlyList<AssistantToolActivity> Tools { get; }
        public string Status { get; }

        public Task<AssistantReply> SendAsync(string prompt, AssistantContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new AssistantReply(Status, Text, 1, Tools, false));

        public void ClearHistory() { }

        public void Dispose() { }
    }

    private sealed class RecordingReplySession : IAssistantSession
    {
        private readonly Func<string, AssistantReply> _produce;
        public RecordingReplySession(Func<string, AssistantReply> produce) => _produce = produce;

        public Task<AssistantReply> SendAsync(string prompt, AssistantContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(_produce(prompt));

        public void ClearHistory() { }
        public void Dispose() { }
    }

    private sealed class TrackableSession : IAssistantSession
    {
        private readonly string _text;
        public TrackableSession(string text) => _text = text;

        public bool Disposed { get; private set; }

        public Task<AssistantReply> SendAsync(string prompt, AssistantContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new AssistantReply("ok", _text, 1, Array.Empty<AssistantToolActivity>(), false));

        public void ClearHistory() { }

        public void Dispose() => Disposed = true;
    }

    private sealed class BlockingSession : IAssistantSession
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool _awaitingApproval;

        public BlockingSession(bool awaitingApproval = false) => _awaitingApproval = awaitingApproval;

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public async Task<AssistantReply> SendAsync(
            string prompt, AssistantContext context, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await _release.Task.ConfigureAwait(false);
            return new AssistantReply("ok", "late reply", 1, Array.Empty<AssistantToolActivity>(), _awaitingApproval);
        }

        public void Complete() => _release.TrySetResult();

        public void ClearHistory() { }

        public void Dispose() => Disposed = true;
    }
}
