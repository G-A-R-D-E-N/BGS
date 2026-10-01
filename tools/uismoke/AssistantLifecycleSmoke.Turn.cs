using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BehaviourStudio.App;
using Microsoft.Extensions.AI;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.UiSmoke;

internal static partial class AssistantLifecycleSmoke
{    private static IReadOnlyList<CodexModel> SmokeCatalog()
    {
        var models = new List<CodexModel>
        {
            new("gpt-5.6-sol", "GPT-5.6-Sol", true, "gpt-5.6-sol") { Agent = "Codex", Group = "Codex" },
            new("gpt-6-astra", "GPT-6-Astra", false, "gpt-6-astra") { Agent = "Codex", Group = "Codex" },
            new("gpt-5.5", "GPT-5.5", false, "gpt-5.5") { Agent = "Codex", Group = "Codex" },
            new("claude-opus-5", "Opus 5", false, "claude-opus-5") { Agent = "Claude", Group = "Claude" },
            new("claude-haiku-4-5-20251001", "Haiku 4.5", false, "claude-haiku-4-5-20251001")
            {
                Agent = "Claude", Group = "Claude",
            },
        };
        for (int index = 0; index < 12; index++)
        {
            string wire = "opencode-go/model-" + index.ToString("00");
            models.Add(new CodexModel(wire, "opencode model " + index.ToString("00"), false, wire)
            {
                Agent = "opencode",
                Group = "opencode-go",
                ContextLimit = index == 0 ? 1_000_000 : 200_000,
                Reasoning = index % 2 == 0,
            });
        }
        return models.AsReadOnly();
    }

    private static void ThePaletteFiltersByAgentAndSwitchesProvider()
    {
        Settings.TrySet("assistant.backend", "", out _);
        Settings.TrySet(AssistantUi.ModelSettingKey(AssistantBackend.Codex), "", out _);
        Settings.TrySet(AssistantUi.ModelSettingKey(AssistantBackend.Claude), "", out _);
        Settings.TrySet(AssistantUi.ModelSettingKey(AssistantBackend.Opencode), "", out _);
        var window = new MainWindow();
        window.Show();
        try
        {
            OpenChat(window);
            AssistantPane pane = window.AssistantUiForTest.Pane;
            pane.ModelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check("the model control opens an in-pane palette", pane.ModelPanelOpen);

            AssistantModelPicker picker = pane.ModelPicker;
            Check("the palette lists every agent at once", picker.VisibleModels.Count == 17);
            Check("the palette offers a filter for every agent",
                picker.FilterLabels.SequenceEqual(new[] { "All", "Codex", "Claude", "opencode" }));

            Point? listPoint = picker.ModelList.TranslatePoint(new Point(14, 14), window);
            Check("the palette list is reachable for pointer input", listPoint is not null);
            double offsetBefore = picker.ModelList.Offset.Y;
            window.MouseWheel(listPoint!.Value, new Vector(0, -120));
            Dispatcher.UIThread.RunJobs();
            Check("the mouse wheel scrolls the palette", picker.ModelList.Offset.Y > offsetBefore);

            picker.SetAgentFilter("Claude");
            Check("the agent filter narrows the palette to that agent",
                picker.VisibleModels.SequenceEqual(new[] { "claude-opus-5", "claude-haiku-4-5-20251001" }));
            picker.SetQuery("haiku");
            Check("search and the agent filter combine",
                picker.VisibleModels.SequenceEqual(new[] { "claude-haiku-4-5-20251001" }));
            picker.SetQuery("");
            picker.SetAgentFilter("");
            Check("clearing the filter restores every agent", picker.VisibleModels.Count == 17);

            picker.FocusSearch();
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            Check("Escape dismisses the palette", !pane.ModelPanelOpen);

            pane.ModelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            picker.SetAgentFilter("Claude");
            Dispatcher.UIThread.RunJobs();
            Border? row = picker.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(border => border.Tag is int index && index == 0);
            Check("the palette exposes clickable rows", row is not null);
            Point? rowPoint = row?.TranslatePoint(new Point(6, 6), window);
            Check("the row is reachable for a mouse click", rowPoint is not null);
            window.MouseDown(rowPoint!.Value, MouseButton.Left);
            window.MouseUp(rowPoint.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Check("clicking a row chooses that model",
                Settings.Get(AssistantUi.ModelSettingKey(AssistantBackend.Claude)) == "claude-opus-5");
            Check("clicking another agent's row switches provider",
                window.AssistantUiForTest.Backend == AssistantBackend.Claude);
            Check("choosing closes the palette", !pane.ModelPanelOpen);
            Check("the model button names the clicked model",
                pane.ModelButtonLabel.Contains("Opus 5", StringComparison.Ordinal));
        }
        finally
        {
            Smoke.CloseForTest(window);
            Settings.TrySet("assistant.backend", "", out _);
            Settings.TrySet(AssistantUi.ModelSettingKey(AssistantBackend.Claude), "", out _);
        }
    }

    private static void ApiProvidersUseASessionKeyAndQueryTheEndpoint()
    {
        Settings.TrySet("assistant.backend", "", out _);
        Settings.TrySet(ApiProviders.BaseUrlSetting(AssistantBackend.Local), "", out _);
        Settings.TrySet(AssistantUi.ModelSettingKey(AssistantBackend.Local), "", out _);
        Func<string, string, CancellationToken, Task<ApiModelList>> previousFetch =
            ApiModelCatalog.FetchForTest;
        Func<Task<IReadOnlyList<CodexModel>>>? previousCatalog = AssistantUi.AllAgentsForTest;
        string? fetchedUrl = null;
        ApiModelCatalog.FetchForTest = (baseUrl, _, _) =>
        {
            fetchedUrl = baseUrl;
            return Task.FromResult(new ApiModelList(new[] { "local-a", "local-b" }, ""));
        };
        AssistantUi.AllAgentsForTest = () => Task.FromResult<IReadOnlyList<CodexModel>>(
            SmokeCatalog().Concat(
                AssistantUi.ApiModels(AssistantBackend.Local, new[] { "local-a", "local-b" })).ToList());
        var window = new MainWindow();
        window.Show();
        try
        {
            AssistantPane pane = window.AssistantUiForTest.Pane;
            window.AssistantUiForTest.OpenSettings();
            Dispatcher.UIThread.RunJobs();
            AssistantSettingsWindow settings = window.AssistantUiForTest.SettingsWindowForTest!;
            Check("a CLI provider hides the API connection section", !settings.ApiSectionVisible);

            settings.ProviderSelector.SelectedItem = settings.ProviderSelector.ItemsSource!
                .Cast<AssistantProviderItem>().Single(item => item.Wire == "local");
            Dispatcher.UIThread.RunJobs();
            Check("choosing Local switches to the API provider",
                window.AssistantUiForTest.Backend == AssistantBackend.Local);
            Check("an API provider shows the connection section", settings.ApiSectionVisible);
            Check("Local defaults to the Ollama endpoint",
                settings.ApiBaseForTest.Text.Contains("11434", StringComparison.Ordinal));
            Check("choosing Local queries its endpoint for models",
                fetchedUrl is not null && fetchedUrl.Contains("11434", StringComparison.Ordinal));
            pane.ModelPicker.SetAgentFilter("Local");
            Check("the endpoint's models appear under the Local agent",
                pane.ModelPicker.VisibleModels.SequenceEqual(new[] { "local-a", "local-b" }));
            pane.ModelPicker.SetAgentFilter("");
            Check("the notice says BGS ships and stores no key",
                settings.NoticeText.Contains("never writes one to disk", StringComparison.Ordinal));

            Check("the API key box is masked", settings.ApiKeyForTest.PasswordChar == '\u2022');
            settings.ApiKeyForTest.Text = "sk-test-secret";
            settings.ApiKeyForTest.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
            });
            Dispatcher.UIThread.RunJobs();
            Check("the key is held for this session",
                window.AssistantUiForTest.ApiKeyForTest == "sk-test-secret");

            string store = Settings.SettingsPathForTest is { Length: > 0 } path && File.Exists(path)
                ? File.ReadAllText(path)
                : "";
            Check("the key is never written to the settings file",
                !store.Contains("sk-test-secret", StringComparison.Ordinal));

            Smoke.Find<Button>(settings).Single(button => button.Content?.ToString() == "Sign out")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check("signing out clears the session key",
                window.AssistantUiForTest.ApiKeyForTest.Length == 0);
        }
        finally
        {
            Smoke.CloseForTest(window);
            ApiModelCatalog.FetchForTest = previousFetch;
            AssistantUi.AllAgentsForTest = previousCatalog;
            Settings.TrySet("assistant.backend", "", out _);
            Settings.TrySet(ApiProviders.BaseUrlSetting(AssistantBackend.Local), "", out _);
            Settings.TrySet(AssistantUi.ModelSettingKey(AssistantBackend.Local), "", out _);
        }
    }

    private static void ProgressShowsWhileATurnRuns()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            OpenChat(window);
            AssistantPane pane = window.AssistantUiForTest.Pane;
            var modelGate = new TaskCompletionSource<ChatResponse>();
            var toolGate = new TaskCompletionSource<string>();
            var tool = AIFunctionFactory.Create(() => toolGate.Task, "bgs.inspect_behavior", "blocks");
            var client = new GatedChatClient(modelGate,
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "All done.")));
            AssistantTools tools = window.CreateAssistantTools();
            window.AssistantUiForTest.SetSessionForTest(new AssistantSession(client, new[] { tool }), tools);

            pane.Composer.Text = "Look at this.";
            Smoke.Find<Button>(window).Single(button => button.Content?.ToString() == "Send")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Check("a working row shows while the turn runs",
                pane.ProgressVisible && pane.ProgressText.Length > 0);

            modelGate.SetResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("call-1", "bgs.inspect_behavior", new Dictionary<string, object?>()),
            })));
            Pump(() => pane.ProgressText.Contains("bgs.inspect_behavior", StringComparison.Ordinal));

            Check("the running tool is named in the working row",
                pane.ProgressText.Contains("bgs.inspect_behavior", StringComparison.Ordinal));
            Check("the tool call appears in the conversation while it runs",
                Smoke.Find<TextBlock>(window).Any(text =>
                    (text.Text ?? "").Contains("bgs.inspect_behavior: running", StringComparison.Ordinal)));

            toolGate.SetResult("ok");
            Pump(() => !pane.ProgressVisible);

            Check("the working row clears when the turn ends", !pane.ProgressVisible);

            static string Shown(TextBlock block) => block.Text is { Length: > 0 } value
                ? value
                : block.Inlines is null
                    ? ""
                    : string.Concat(block.Inlines.OfType<Run>().Select(run => run.Text));
            Check("the turn's answer reaches the conversation",
                Smoke.Find<TextBlock>(window).Any(block =>
                    Shown(block).Contains("All done.", StringComparison.Ordinal)));
        }
        finally
        {
            Smoke.CloseForTest(window);
        }
    }

    private static void ComposerSendsQueuesAndRunsCommands()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            OpenChat(window);
            AssistantPane pane = window.AssistantUiForTest.Pane;
            var gate = new TaskCompletionSource<ChatResponse>();
            var client = new GatedChatClient(gate,
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "Second answer.")));
            AssistantTools tools = window.CreateAssistantTools();
            window.AssistantUiForTest.SetSessionForTest(
                new AssistantSession(client, Array.Empty<AIFunction>()), tools);

            pane.Composer.Text = "first message";
            PressEnter(window, pane, shift: false);
            Check("Enter sends the composer text", pane.Composer.Text == "" && pane.IsBusy);
            Check("the sent message appears in the conversation",
                TextShown(window).Contains("first message", StringComparison.Ordinal));

            pane.Composer.Text = "draft";
            PressEnter(window, pane, shift: true);
            Check("Shift+Enter keeps the draft instead of sending",
                (pane.Composer.Text ?? "").Contains("draft", StringComparison.Ordinal) &&
                !TextShown(window).Contains("Queued: draft", StringComparison.Ordinal));

            pane.Composer.Text = "second message";
            PressEnter(window, pane, shift: false);
            Check("a message sent during a turn is queued",
                TextShown(window).Contains("Queued: second message", StringComparison.Ordinal));

            gate.SetResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "First answer.")));
            Pump(() => !pane.IsBusy && !TextShown(window).Contains("Queued:", StringComparison.Ordinal));
            Check("the queued message sends when the turn ends",
                TextShown(window).Contains("Second answer.", StringComparison.Ordinal));
            Check("the queue row clears once it sends",
                !TextShown(window).Contains("Queued:", StringComparison.Ordinal));

            pane.Composer.Text = "/help";
            PressEnter(window, pane, shift: false);
            Check("/help lists the BGS commands",
                TextShown(window).Contains("/new: Start a new chat", StringComparison.Ordinal));

            pane.Composer.Text = "/clear";
            PressEnter(window, pane, shift: false);
            Check("/clear empties the conversation", pane.MessageCount == 0);

            int chats = window.AssistantUiForTest.ChatListForTest.Count;
            pane.Composer.Text = "/new";
            PressEnter(window, pane, shift: false);
            Check("/new starts a new chat",
                window.AssistantUiForTest.ChatListForTest.Count > chats);
        }
        finally
        {
            Smoke.CloseForTest(window);
        }
    }

    private static void SlashCommandsCompleteInTheComposer()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            OpenChat(window);
            AssistantPane pane = window.AssistantUiForTest.Pane;

            pane.Composer.Text = "/";
            Dispatcher.UIThread.RunJobs();
            Check("typing a slash shows the command list", pane.CommandsVisible);
            Check("the list holds the BGS commands",
                pane.CommandChoices.Any(command => command.Name == "new"));
            Check("the palette offers only the BGS commands",
                pane.CommandChoices.All(command =>
                    AssistantCommands.All.Any(bgs => bgs.Name == command.Name)));

            pane.Composer.Text = "/cl";
            Dispatcher.UIThread.RunJobs();
            Check("typing narrows the list to matching commands",
                pane.CommandChoices.Count == 1 && pane.CommandChoices[0].Name == "clear");

            pane.Composer.Text = "/";
            Dispatcher.UIThread.RunJobs();
            pane.Composer.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            Check("Tab accepts the highlighted command",
                (pane.Composer.Text ?? "").StartsWith("/new", StringComparison.Ordinal));
            Check("accepting a command closes the list", !pane.CommandsVisible);

            pane.Composer.Text = "/he";
            Dispatcher.UIThread.RunJobs();
            pane.Composer.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            Check("Escape dismisses the command list", !pane.CommandsVisible);
            Check("Escape leaves the typed text alone",
                (pane.Composer.Text ?? "").StartsWith("/he", StringComparison.Ordinal));
        }
        finally
        {
            Smoke.CloseForTest(window);
        }
    }

    private static void QuickStartShowsOnceOnFirstOpen()
    {
        Settings.TrySet("assistant.quick_start_done", "", out _);
        var window = new MainWindow();
        window.Show();
        try
        {
            AssistantPane pane = window.AssistantUiForTest.Pane;
            Check("the quick start is hidden before the chat opens", !pane.QuickStartVisible);

            OpenChat(window);
            Check("the first chat open shows the quick start", pane.QuickStartVisible);
            Check("the quick start lists the commands",
                TextShown(window).Contains("/new, /clear, /model", StringComparison.Ordinal));

            Smoke.Find<Button>(window).Single(button => button.Content?.ToString() == "Got it")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check("dismissing the quick start hides it", !pane.QuickStartVisible);
            Check("dismissing the quick start is remembered",
                Settings.Get("assistant.quick_start_done") == "1");

            OpenChat(window);
            Check("the quick start does not come back on the next open", !pane.QuickStartVisible);
        }
        finally
        {
            Smoke.CloseForTest(window);
            Settings.TrySet("assistant.quick_start_done", "", out _);
        }
    }

}
