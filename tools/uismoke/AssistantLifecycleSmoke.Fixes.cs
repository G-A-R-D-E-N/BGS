using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BehaviourStudio.App;
using Microsoft.Extensions.AI;

namespace BehaviourStudio.UiSmoke;

internal static partial class AssistantLifecycleSmoke
{
    private static void ProviderChangesResetIdleSessions()
    {
        string previousBackend = Settings.Get("assistant.backend");
        string previousModel = Settings.Get(AssistantUi.ModelSettingKey(AssistantBackend.Local));
        string previousUrl = Settings.Get(ApiProviders.BaseUrlSetting(AssistantBackend.Local));
        Settings.TrySet("assistant.backend", "local", out _);
        var window = new MainWindow();
        window.Show();
        try
        {
            OpenChat(window);
            var ui = window.AssistantUiForTest;
            ui.SetSessionForTest(new FailureReplySession());
            ui.SelectModelForTest(new CodexModel("proof-model", "proof-model", false)
                { Agent = AssistantModelCatalog.LocalAgent });
            Check("changing the API model discards cached sessions", ui.ActiveSessionForTest is null);
            ui.SetSessionForTest(new FailureReplySession());
            ui.ChooseApiKeyForTest("proof-key");
            Check("changing the API key discards cached sessions", ui.ActiveSessionForTest is null);
            ui.SetSessionForTest(new FailureReplySession());
            ui.ChooseBaseUrlForTest("http://127.0.0.1:1/v1");
            Check("changing the API endpoint discards cached sessions", ui.ActiveSessionForTest is null);

            var completion = new TaskCompletionSource<ChatResponse>();
            var client = new GatedChatClient(completion,
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
            var session = new AssistantSession(client, Array.Empty<AIFunction>());
            ui.SetSessionForTest(session);
            ui.Pane.Composer.Text = "wait for this request";
            PressEnter(window, ui.Pane, false);
            ui.ChooseApiKeyForTest("replacement-key");
            ui.ChooseBaseUrlForTest("http://127.0.0.1:2/v1");
            ui.SelectModelForTest(new CodexModel("other-model", "other-model", false)
                { Agent = AssistantModelCatalog.LocalAgent });
            Check("busy API requests retain their session and settings",
                ReferenceEquals(session, ui.ActiveSessionForTest) && ui.ApiKeyForTest == "proof-key" &&
                ApiProviders.BaseUrl(AssistantBackend.Local) == "http://127.0.0.1:1/v1" &&
                Settings.Get(AssistantUi.ModelSettingKey(AssistantBackend.Local)) == "proof-model");
            completion.SetResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
            Pump(() => !ui.Pane.IsBusy);
        }
        finally
        {
            Smoke.CloseForTest(window);
            Settings.TrySet("assistant.backend", previousBackend, out _);
            Settings.TrySet(AssistantUi.ModelSettingKey(AssistantBackend.Local), previousModel, out _);
            Settings.TrySet(ApiProviders.BaseUrlSetting(AssistantBackend.Local), previousUrl, out _);
        }
    }

    private static void ProviderFailuresKeepTheirActionableDetail()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            OpenChat(window);
            var ui = window.AssistantUiForTest;
            ui.SetSessionForTest(new FailureReplySession());
            int previousMessages = ui.MessageCount;
            ui.Pane.Composer.Text = "try the unavailable model";
            PressEnter(window, ui.Pane, false);
            Pump(() => !ui.Pane.IsBusy);
            Check("a provider failure retains its actionable detail",
                ui.Status.Contains("Choose an available model", StringComparison.Ordinal));
            Check("provider failures stay out of saved assistant history", ui.MessageCount == previousMessages + 1);
        }
        finally
        {
            Smoke.CloseForTest(window);
        }
    }

    private sealed class FailureReplySession : IAssistantSession
    {
        public Task<AssistantReply> SendAsync(string prompt, AssistantContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(new AssistantReply(
                "provider_error", "Choose an available model.", 0, Array.Empty<AssistantToolActivity>(), false));
        public void ClearHistory() { }
        public void Dispose() { }
    }

    private static void AQueuedMessageNeverCrossesConversations()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            OpenChat(window);
            AssistantPane pane = window.AssistantUiForTest.Pane;

            Smoke.Find<Button>(window).First(button => button.Content?.ToString() == "New chat")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            string chatB = window.AssistantUiForTest.ActiveChatIdForTest!;

            string chatA = window.AssistantUiForTest.ChatListForTest
                .First(summary => summary.Id != chatB).Id;
            window.AssistantUiForTest.SelectChatForTest(chatA);
            Dispatcher.UIThread.RunJobs();

            var gate = new TaskCompletionSource<ChatResponse>();
            var client = new GatedChatClient(gate,
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "First answer.")));
            AssistantTools tools = window.CreateAssistantTools();
            window.AssistantUiForTest.SetSessionForTest(
                new AssistantSession(client, Array.Empty<AIFunction>()), tools);

            pane.Composer.Text = "slow question";
            PressEnter(window, pane, shift: false);
            Check("the first chat is busy", pane.IsBusy);

            pane.Composer.Text = "queued-for-a";
            PressEnter(window, pane, shift: false);
            Check("the message queues on the busy chat",
                TextShown(window).Contains("Queued: queued-for-a", StringComparison.Ordinal));

            window.AssistantUiForTest.DeleteChatForTest(chatA);
            Dispatcher.UIThread.RunJobs();
            Check("deleting the busy chat drops its queued message",
                !TextShown(window).Contains("Queued: queued-for-a", StringComparison.Ordinal));

            gate.SetResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "First answer.")));
            Pump(() => !pane.IsBusy);
            Dispatcher.UIThread.RunJobs();
            Check("the orphaned queued message never enters the next chat",
                !TextShown(window).Contains("queued-for-a", StringComparison.Ordinal));
            Check("the surviving chat is the selected one",
                window.AssistantUiForTest.ActiveChatIdForTest == chatB);
        }
        finally
        {
            Smoke.CloseForTest(window);
        }
    }

    private static void DuplicateModelIdsStayDistinct()
    {
        Func<Task<IReadOnlyList<CodexModel>>>? previous = AssistantUi.AllAgentsForTest;
        Settings.TrySet("assistant.model_favorites", "", out _);
        Settings.TrySet("assistant.model_recents", "", out _);
        AssistantUi.AllAgentsForTest = () => Task.FromResult<IReadOnlyList<CodexModel>>(new[]
        {
            new CodexModel("shared", "Shared", false, "shared") { Agent = "Codex", Group = "Codex" },
            new CodexModel("shared", "Shared", false, "shared") { Agent = "opencode", Group = "shared" },
        });
        var window = new MainWindow();
        window.Show();
        try
        {
            OpenChat(window);
            AssistantPane pane = window.AssistantUiForTest.Pane;
            AssistantModelPicker picker = pane.ModelPicker;
            pane.ModelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            picker.SetAgentFilter("Codex");
            Check("the duplicate id is listed under its own agent",
                picker.VisibleModels.SequenceEqual(new[] { "shared" }));
            picker.FavoriteRow(0);
            Dispatcher.UIThread.RunJobs();
            Check("favouriting the duplicate marks that agent's row", StarAt(picker, 0) == "\u2605");

            picker.SetAgentFilter("opencode");
            Dispatcher.UIThread.RunJobs();
            Check("the other agent's duplicate is not favourited", StarAt(picker, 0) == "\u2606");

            picker.SetAgentFilter("Codex");
            picker.ChooseVisible(0);
            Dispatcher.UIThread.RunJobs();
            picker.SetAgentFilter("opencode");
            picker.ChooseVisible(0);
            Dispatcher.UIThread.RunJobs();
            Check("both duplicates are recorded separately",
                AssistantModelPrefs.Recents().Count == 2);
        }
        finally
        {
            Smoke.CloseForTest(window);
            AssistantUi.AllAgentsForTest = previous;
            Settings.TrySet("assistant.model_favorites", "", out _);
            Settings.TrySet("assistant.model_recents", "", out _);
        }
    }

    private static string StarAt(AssistantModelPicker picker, int index)
    {
        Border? row = picker.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(candidate => candidate.Tag is int tag && tag == index);
        if (row is null) return "";
        foreach (TextBlock block in row.GetVisualDescendants().OfType<TextBlock>())
        {
            string text = block.Text ?? "";
            if (text.Length == 1 && (text[0] == '\u2605' || text[0] == '\u2606')) return text;
        }
        return "";
    }

    private static void ApiCredentialsStayWithTheirProvider()
    {
        Settings.TrySet("assistant.backend", "", out _);
        Settings.TrySet(ApiProviders.BaseUrlSetting(AssistantBackend.Gemini), "", out _);
        Settings.TrySet(ApiProviders.BaseUrlSetting(AssistantBackend.Local), "", out _);
        Func<string, string, CancellationToken, Task<ApiModelList>> previous = ApiModelCatalog.FetchForTest;
        var seen = new List<(string Url, string Key)>();
        ApiModelCatalog.FetchForTest = (url, key, _) =>
        {
            lock (seen) seen.Add((url, key));
            return Task.FromResult(new ApiModelList(new[] { "m" }, ""));
        };
        var window = new MainWindow();
        window.Show();
        try
        {
            window.AssistantUiForTest.OpenSettings();
            Dispatcher.UIThread.RunJobs();
            AssistantSettingsWindow settings = window.AssistantUiForTest.SettingsWindowForTest!;
            Func<string, AssistantProviderItem> item = wire => settings.ProviderSelector.ItemsSource!
                .Cast<AssistantProviderItem>().Single(candidate => candidate.Wire == wire);

            settings.ProviderSelector.SelectedItem = item("gemini");
            Dispatcher.UIThread.RunJobs();
            window.AssistantUiForTest.SetApiKeyForTest("gemini-secret");
            lock (seen) seen.Clear();

            settings.ProviderSelector.SelectedItem = item("local");
            Dispatcher.UIThread.RunJobs();
            bool geminiLeaked;
            lock (seen) geminiLeaked = seen.Any(entry => entry.Key == "gemini-secret");
            Check("a Gemini secret is not sent to the Local endpoint", !geminiLeaked);

            window.AssistantUiForTest.SetApiKeyForTest("local-secret");
            lock (seen) seen.Clear();
            settings.ProviderSelector.SelectedItem = item("gemini");
            Dispatcher.UIThread.RunJobs();
            bool localLeaked;
            lock (seen) localLeaked = seen.Any(entry => entry.Key == "local-secret");
            Check("a Local secret is not sent to the Gemini endpoint", !localLeaked);

            settings.ProviderSelector.SelectedItem = item("local");
            Dispatcher.UIThread.RunJobs();
            Check("each provider keeps its own session key",
                window.AssistantUiForTest.ApiKeyForTest == "local-secret");

            Settings.TrySet(ApiProviders.BaseUrlSetting(AssistantBackend.Gemini),
                "http://example.test/v1", out _);
            lock (seen) seen.Clear();
            settings.ProviderSelector.SelectedItem = item("gemini");
            Dispatcher.UIThread.RunJobs();
            bool contactedInsecure;
            lock (seen) contactedInsecure = seen.Any(entry => entry.Url.Contains("example.test"));
            Check("an insecure Gemini endpoint is never contacted", !contactedInsecure);
        }
        finally
        {
            Smoke.CloseForTest(window);
            ApiModelCatalog.FetchForTest = previous;
            Settings.TrySet("assistant.backend", "", out _);
            Settings.TrySet(ApiProviders.BaseUrlSetting(AssistantBackend.Gemini), "", out _);
            Settings.TrySet(ApiProviders.BaseUrlSetting(AssistantBackend.Local), "", out _);
        }
    }

    private static void DeletingTheLastChatLeavesAUsableConversation()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            OpenChat(window);
            Smoke.Find<Button>(window).First(button => button.Content?.ToString() == "New chat")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            string[] ids = window.AssistantUiForTest.ChatListForTest
                .Select(summary => summary.Id).ToArray();
            Check("the smoke starts with two chats", ids.Length == 2);
            foreach (string id in ids)
            {
                window.AssistantUiForTest.DeleteChatForTest(id);
                Dispatcher.UIThread.RunJobs();
            }
            Check("deleting every chat leaves one usable conversation",
                window.AssistantUiForTest.ChatListForTest.Count == 1);
            Check("the surviving conversation is selected",
                window.AssistantUiForTest.ActiveChatIdForTest ==
                window.AssistantUiForTest.ChatListForTest[0].Id);
        }
        finally
        {
            Smoke.CloseForTest(window);
        }
    }

    private static void UnknownSlashCommandsAreRefused()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            OpenChat(window);
            AssistantPane pane = window.AssistantUiForTest.Pane;
            pane.Composer.Text = "/evil !`whoami`";
            PressEnter(window, pane, shift: false);
            Check("an unknown slash command is refused",
                pane.Status.Contains("Unknown BGS command", StringComparison.Ordinal));
            Check("a refused command never reaches the conversation",
                !TextShown(window).Contains("/evil", StringComparison.Ordinal));
        }
        finally
        {
            Smoke.CloseForTest(window);
        }
    }
}
