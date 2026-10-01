using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BehaviourStudio.App;
using Microsoft.Extensions.AI;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.UiSmoke;

internal static class MultiChatSmoke
{
    internal static void Run()
    {
        RunPhase(LayoutAndConversations);
        RunPhase(MutationIsolation);
        RunPhase(PersistenceBoundary);
    }

    private static void PersistenceBoundary()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            AssistantLifecycleSmoke.OpenChat(window);
            Dispatcher.UIThread.RunJobs();
            AssistantUi ui = window.AssistantUiForTest;

            var fake = new FakeChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "understood")),
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "still here")),
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "all good")));
            ui.SetSessionForTest(new AssistantSession(fake, Array.Empty<AIFunction>()));

            string syntheticKey = "sk-" + new string('a', 22);
            ui.Pane.Composer.Text = "my key is " + syntheticKey;
            Click(window, "Send");
            Dispatcher.UIThread.RunJobs();
            Check("a credential-like prompt still gets a reply", ui.MessageCount == 2);
            Check("the UI reports the redaction instead of crashing",
                ui.Status.Contains("redacted", StringComparison.OrdinalIgnoreCase));
            string chatId = ui.ActiveChatIdForTest!;
            string text = File.ReadAllText(AssistantChatStore.FilePathFor(chatId));
            Check("the credential never reaches disk",
                !text.Contains(syntheticKey, StringComparison.Ordinal));

            AssistantChatStore.SaveFailureForTest = () => throw new IOException("simulated disk failure");
            ui.Pane.Composer.Text = "second message";
            Click(window, "Send");
            Dispatcher.UIThread.RunJobs();
            Check("an I/O save failure is reported instead of crashing",
                ui.Status.Contains("could not save", StringComparison.OrdinalIgnoreCase));
            Check("the reply stays visible after a save failure", ui.MessageCount == 4);
            AssistantChatStore.SaveFailureForTest = null;

            ui.Pane.Composer.Text = "third message";
            Click(window, "Send");
            Dispatcher.UIThread.RunJobs();
            Check("the chat is still usable after a save failure", ui.MessageCount == 6);

            AssistantChatStore.SaveFailureForTest = () => throw new IOException("simulated disk failure");
            int chatsBefore = ui.ChatListForTest.Count;
            Click(window, "+ New chat");
            Dispatcher.UIThread.RunJobs();
            Check("a failed New chat visibly reports it could not save",
                ui.Status.Contains("could not save", StringComparison.OrdinalIgnoreCase));
            Check("the new chat is still listed and usable",
                ui.ChatListForTest.Count == chatsBefore + 1);

            ui.SetSessionForTest(new CancelledSession());
            ui.Pane.Composer.Text = "cancel this";
            Click(window, "Send");
            Dispatcher.UIThread.RunJobs();
            Check("a cancelled reply keeps its status over a persistence notice",
                ui.Status == "Cancelled.");
            AssistantChatStore.SaveFailureForTest = null;
        }
        finally
        {
            AssistantChatStore.SaveFailureForTest = null;
            Smoke.CloseForTest(window);
        }
    }

    private static void RunPhase(Action phase)
    {
        string previousFolder = AssistantChatStore.Folder;
        string? previousSettings = Settings.SettingsPathForTest;
        string tempFolder = Path.Combine(Path.GetTempPath(),
            "bgs-multichat-smoke-" + Guid.NewGuid().ToString("N"));
        string settingsPath = Path.Combine(Path.GetTempPath(),
            "bgs-multichat-settings-" + Guid.NewGuid().ToString("N") + ".cfg");
        AssistantChatStore.Folder = tempFolder;
        Settings.SettingsPathForTest = settingsPath;
        try
        {
            phase();
        }
        finally
        {
            AssistantChatStore.DeleteForTest = null;
            AssistantChatStore.Folder = previousFolder;
            Settings.SettingsPathForTest = previousSettings;
            try { if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            try { if (File.Exists(settingsPath)) File.Delete(settingsPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void LayoutAndConversations()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            AssistantUi ui = window.AssistantUiForTest;

            var root = (Grid)window.Content!;
            Check("root grid keeps the main shell in column 0 plus three assistant columns",
                root.ColumnDefinitions.Count == 4);
            Check("conversation sidebar occupies column 1", ui.SidebarColumnIndexForTest == 1);
            Check("splitter occupies column 2", ui.SplitterColumnIndexForTest == 2);
            Check("assistant pane occupies column 3", ui.PaneColumnIndexForTest == 3);

            Check("drawer starts closed", !window.AssistantDrawerOpen);
            Check("closed drawer hides the sidebar", !ui.SidebarVisibleForTest);
            Check("closed drawer hides the assistant pane", !ui.Pane.IsVisible);
            Check("closed sidebar column has zero width", ui.SidebarColumnWidthForTest == 0);
            Check("closed splitter column has zero width", ui.SplitterColumnWidthForTest == 0);
            Check("closed pane column has zero width", ui.PaneColumnWidthForTest == 0);

            Check("the sidebar lists exactly one auto-created chat", ui.ChatListForTest.Count == 1);

            AssistantLifecycleSmoke.OpenChat(window);
            Dispatcher.UIThread.RunJobs();
            Check("Chat button opens the drawer to the active chat", window.AssistantDrawerOpen);
            Check("open drawer shows the sidebar", ui.SidebarVisibleForTest);
            Check("open drawer shows the assistant pane", ui.Pane.IsVisible);
            Check("open sidebar column has a real width", ui.SidebarColumnWidthForTest >= 160);
            Check("open splitter column has a real width", ui.SplitterColumnWidthForTest > 0);
            Check("open pane column restores a stored width", ui.PaneColumnWidthForTest >= 520);

            Click(window, "+ New chat");
            Dispatcher.UIThread.RunJobs();
            Check("+ New chat creates a second conversation in the sidebar",
                ui.ChatListForTest.Count == 2);

            Click(window, "+ New chat");
            Dispatcher.UIThread.RunJobs();
            Check("another + New chat creates a third conversation in the sidebar",
                ui.ChatListForTest.Count == 3);

            var chats = ui.ChatListForTest.ToList();
            string secondId = chats[1].Id;
            string thirdId = chats[2].Id;

            ui.SelectChatForTest(secondId);
            Dispatcher.UIThread.RunJobs();
            Check("switching to chat B leaves the active chat as B",
                ui.ActiveChatIdForTest == secondId);

            ui.Pane.Composer.Text = "first message";
            var fake1 = new FakeChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "reply-one")));
            var session = new AssistantSession(fake1, Array.Empty<AIFunction>());
            ui.SetSessionForTest(session);
            Click(window, "Send");
            Dispatcher.UIThread.RunJobs();
            Check("sending in chat B updates the visible conversation",
                ui.MessageCount == 2 && fake1.CallCount == 1);

            ui.SelectChatForTest(thirdId);
            Dispatcher.UIThread.RunJobs();
            Check("switching to chat C shows an empty transcript", ui.MessageCount == 0);

            ui.SelectChatForTest(secondId);
            Dispatcher.UIThread.RunJobs();
            Check("switching back to chat B restores both messages", ui.MessageCount == 2);

            Check("created chat ids were persisted",
                AssistantChatStore.Exists(secondId) && AssistantChatStore.Exists(thirdId));

            Click(window, "Close");
            Dispatcher.UIThread.RunJobs();
            Check("closing the drawer hides the sidebar again",
                !ui.SidebarVisibleForTest && ui.SidebarColumnWidthForTest == 0);
            Check("closing the drawer hides the assistant pane", !ui.Pane.IsVisible);
        }
        finally
        {
            Smoke.CloseForTest(window);
        }
    }

    private static void MutationIsolation()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            string path = Path.Combine("tools", "tests", "fixtures", "vanilla", "Meshes", "Actors",
                "Character", "Behaviors", "SingleAnimFurniture.hkx");
            window.Open(path);
            Dispatcher.UIThread.RunJobs();
            string objectId = BehaviourGraphModel.Parse(window.LoadedXml).Objects
                .First(objectInfo => objectInfo.Class == "hkbClipGenerator").Id;

            AssistantLifecycleSmoke.OpenChat(window);
            Dispatcher.UIThread.RunJobs();
            AssistantUi ui = window.AssistantUiForTest;
            AssistantTools tools = window.CreateAssistantTools();
            string chatA = ui.ActiveChatIdForTest!;

            Click(window, "+ New chat");
            Dispatcher.UIThread.RunJobs();
            string chatB = ui.ActiveChatIdForTest!;
            Check("two chats exist before the mutation isolation checks",
                chatA != chatB && ui.ChatListForTest.Count == 2);

            ui.SelectChatForTest(chatA);
            Dispatcher.UIThread.RunJobs();
            SendPreview(window, ui, tools, objectId, chatA);
            Check("chat A carries the real pending mutation",
                ui.ApprovalVisible && tools.HasPendingApproval);

            ui.SelectChatForTest(chatB);
            Dispatcher.UIThread.RunJobs();
            Check("switching to chat B rejects chat A's real pending mutation",
                !tools.HasPendingApproval && !ui.ApprovalVisible);

            Click(window, "Apply approved edit");
            Dispatcher.UIThread.RunJobs();
            Check("chat B cannot apply chat A's edit", !window.IsDirty && window.UndoStepsForTest == 0);

            ui.SelectChatForTest(chatA);
            Dispatcher.UIThread.RunJobs();
            SendPreview(window, ui, tools, objectId, chatA);
            Check("chat A can preview again after the self-correcting switch",
                tools.HasPendingApproval);

            Click(window, "+ New chat");
            Dispatcher.UIThread.RunJobs();
            Check("New Chat clears the real pending mutation",
                !tools.HasPendingApproval && !ui.ApprovalVisible);

            ui.SelectChatForTest(chatA);
            Dispatcher.UIThread.RunJobs();
            SendPreview(window, ui, tools, objectId, chatA);
            Check("chat A carries a preview before sign out", tools.HasPendingApproval);
            SignOutClearsPending(window, ui, tools);
        }
        finally
        {
            Smoke.CloseForTest(window);
        }
    }

    private static void SignOutClearsPending(MainWindow window, AssistantUi ui, AssistantTools tools)
    {
        var transport = new SignOutTransport();
        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("the smoke process path is unavailable");
        var options = new AssistantProviderOptions(
            AssistantProviderOptions.CodexBackend, "", executable);
        CodexAssistantConnection connection = window.CreateCodexConnection(options, (_, _) => transport);
        connection.ConnectAsync().GetAwaiter().GetResult();
        ui.SetConnectionForTest(connection);

        ui.SignOutForTest();
        bool cleared = WaitFor(() => !tools.HasPendingApproval && !ui.ApprovalVisible);

        Check("signing out rejects the real pending mutation", cleared);
        Check("signing out disposes the live chat session", ui.ActiveSessionForTest is null);
    }

    private static bool WaitFor(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        return condition();
    }

    private static void SendPreview(
        MainWindow window, AssistantUi ui, AssistantTools tools, string objectId, string chatId)
    {
        ui.SetSessionForTest(window.CreateAssistantSession(PreviewClient(objectId), tools), tools);
        ui.Pane.Composer.Text = "Change the clip animation.";
        Click(window, "Send");
        Dispatcher.UIThread.RunJobs();
        Check("the preview was requested from the active chat",
            ui.ActiveChatIdForTest == chatId);
    }

    private static FakeChatClient PreviewClient(string objectId) =>
        new(new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("call-1", "bgs.set_clip_animation",
                    new Dictionary<string, object?>
                    {
                        ["objectId"] = objectId,
                        ["animationName"] = "Animations\\Assistant\\Approved.hkx",
                    }),
            })),
            new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "The preview is ready for your approval.")));

    private static void Click(MainWindow window, string text) =>
        Smoke.Find<Button>(window).Single(button => button.Content?.ToString() == text)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Check(string name, bool value)
    {
        Smoke.CheckTrue(name, value);
        if (!value) throw new InvalidOperationException(name);
    }

    private sealed class CancelledSession : IAssistantSession
    {
        public Task<AssistantReply> SendAsync(
            string prompt, AssistantContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AssistantReply(
                "cancelled", "The request was cancelled.", 0,
                Array.Empty<AssistantToolActivity>(), false));

        public void ClearHistory() { }
        public void Dispose() { }
    }

    private sealed class FakeChatClient : IChatClient
    {
        private readonly Queue<ChatResponse> _responses;
        public FakeChatClient(params ChatResponse[] responses) => _responses = new(responses);
        public int CallCount { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(_responses.Dequeue());
        }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            yield break;
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class SignOutTransport : ICodexTransport
    {
        public event Action<string>? LineReceived;
        public event Action<CodexProcessExit>? Exited;

        public bool IsRunning { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task SendLineAsync(string line, CancellationToken cancellationToken)
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id) ||
                !root.TryGetProperty("method", out JsonElement methodElement))
                return Task.CompletedTask;

            string result = (methodElement.GetString() ?? "") switch
            {
                "initialize" => "{\"userAgent\":\"behaviour_graph_studio/0.154.0\",\"platformFamily\":\"windows\",\"platformOs\":\"windows\"}",
                "account/read" => "{\"account\":{\"type\":\"chatgpt\",\"email\":null,\"planType\":\"plus\"},\"requiresOpenaiAuth\":true}",
                "model/list" => "{\"data\":[]}",
                _ => "{}",
            };
            LineReceived?.Invoke("{\"id\":" + id.GetRawText() + ",\"result\":" + result + "}");
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            IsRunning = false;
            Exited?.Invoke(new CodexProcessExit(0, "closed"));
        }
    }
}
