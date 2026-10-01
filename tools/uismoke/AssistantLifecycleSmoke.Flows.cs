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
{    private static void StaleApprovalIsNotReportedAsApplied()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            string path = Path.Combine("tools", "tests", "fixtures", "vanilla", "Meshes", "Actors",
                "Character", "Behaviors", "SingleAnimFurniture.hkx");
            window.Open(path);
            Dispatcher.UIThread.RunJobs();
            string id = BehaviourGraphModel.Parse(window.LoadedXml).Objects
                .First(objectInfo => objectInfo.Class == "hkbClipGenerator").Id;
            var fake = new FakeChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
                {
                    new FunctionCallContent("call-1", "bgs.set_clip_animation",
                        new Dictionary<string, object?>
                        {
                            ["objectId"] = id,
                            ["animationName"] = "Animations\\Assistant\\Approved.hkx",
                        }),
                })),
                new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    "The preview is ready for your approval.")));
            AssistantTools tools = window.CreateAssistantTools();
            window.AssistantUiForTest.SetSessionForTest(
                window.CreateAssistantSession(fake, tools), tools);
            window.AssistantUiForTest.Pane.Composer.Text = "Update the selected clip.";
            Smoke.Find<Button>(window.AssistantUiForTest!.Pane).Single(button => button.Content?.ToString() == "Send")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check("stale scenario starts with a pending preview",
                window.AssistantUiForTest.ApprovalVisible);

            string advanced = HkxTextEdit.SetParam(
                window.LoadedXml, id, "animationName", "Animations\\Assistant\\Other.hkx");
            bool committed = ((IAssistantEditorDocument)window).TryCommit(advanced, out _);
            Check("the active document advanced before approval", committed);

            Smoke.Find<Button>(window).Single(button => button.Content?.ToString() == "Approve action")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check("a stale approval is reported as not applied",
                window.AssistantUiForTest.Status.Contains("was not applied", StringComparison.Ordinal));
            Check("a stale approval never reaches the document",
                !window.LoadedXml.Contains("Approved.hkx", StringComparison.Ordinal));
        }
        finally
        {
            Smoke.CloseForTest(window);
        }
    }

    private static void WindowOwnsCodexConnection()
    {
        var window = new MainWindow();
        var transport = new WindowTransport();
        window.Show();
        try
        {
            string executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("the smoke process path is unavailable");
            var options = new AssistantProviderOptions(
                AssistantProviderOptions.CodexBackend, "", executable);
            CodexAssistantConnection connection = window.CreateCodexConnection(options, (_, _) => transport);
            connection.ConnectAsync().GetAwaiter().GetResult();
            Check("window-owned Codex connection starts", connection.IsConnected && transport.IsRunning);
        }
        finally
        {
            Smoke.CloseForTest(window);
        }
        Check("closing window disposes the Codex connection", transport.Disposed && !transport.IsRunning);
    }

    private static void ModelSelectorListsCatalogAndPersistsChoice()
    {
        Settings.TrySet("assistant.model", "", out _);
        var window = new MainWindow();
        var transport = new WindowTransport();
        window.Show();
        try
        {
            string executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("the smoke process path is unavailable");
            var options = new AssistantProviderOptions(
                AssistantProviderOptions.CodexBackend, "", executable);
            CodexAssistantConnection connection =
                window.CreateCodexConnection(options, (_, _) => transport);
            connection.ConnectAsync().GetAwaiter().GetResult();
            window.AssistantUiForTest.SetConnectionForTest(connection);
            window.AssistantUiForTest.RefreshAccountForTest();

            AssistantPane pane = window.AssistantUiForTest.Pane;
            Check("the model control appears once the catalog is known", pane.ModelSelectorVisible);
            Check("the model palette lists every catalog model", pane.ModelPicker.VisibleModels.Count == 3);
            Check("no model is forced before the user chooses", pane.SelectedModelItem is null);
            Check("the account line names the Codex config default",
                window.AssistantUiForTest.AccountStatus.Contains("Codex config default", StringComparison.Ordinal));

            pane.ModelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check("the model control opens the palette", pane.ModelPanelOpen);

            Check("the palette groups models under their provider",
                pane.ModelPicker.VisibleModels.Contains("gpt-6-astra"));
            pane.ModelPicker.SetQuery("astra");
            Check("the palette search filters the list",
                pane.ModelPicker.VisibleModels.SequenceEqual(new[] { "gpt-6-astra" }));
            pane.ModelPicker.SetQuery("nothing matches this");
            Check("an empty result is reported rather than showing everything",
                pane.ModelPicker.VisibleModels.Count == 0);
            pane.ModelPicker.SetQuery("");

            Check("choosing from the palette is applied", pane.ModelPicker.Choose("gpt-6-astra"));
            Dispatcher.UIThread.RunJobs();
            Check("choosing a model persists it", Settings.Get("assistant.model") == "gpt-6-astra");
            Check("choosing a model selects it on the connection", connection.SelectedModel == "gpt-6-astra");
            Check("choosing a model shows the chosen model", pane.SelectedModelItem == "gpt-6-astra");
            Check("choosing a model offers a reset", pane.ModelResetVisible);
            Check("the model button names the chosen model",
                pane.ModelButtonLabel.Contains("GPT-6-Astra", StringComparison.Ordinal));

            string astraIdentity = AssistantModelCatalog.Identity(
                pane.ModelPicker.VisibleModelItems.Single(model => model.WireModel == "gpt-6-astra"));
            Check("a model can be favourited", ToggleFavorite(pane, astraIdentity));
            Check("the favourites section keeps it", AssistantModelPrefs.IsFavorite(astraIdentity));
            Check("toggling a favourite does not change the model",
                Settings.Get("assistant.model") == "gpt-6-astra");
            ToggleFavorite(pane, astraIdentity);
            Check("a favourite can be removed", !AssistantModelPrefs.IsFavorite(astraIdentity));

            pane.InvokeModelResetRequested();
            Dispatcher.UIThread.RunJobs();
            Check("resetting clears the choice", Settings.Get("assistant.model") == "");
            Check("resetting returns to the Codex config default", connection.SelectedModel == "");
            Check("resetting hides the reset control", !pane.ModelResetVisible);
        }
        finally
        {
            Smoke.CloseForTest(window);
        }
    }

    private static void TheGearOpensAssistantSettings()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            EditorShell? shell = EditorShell.Find(window.Content as Control);
            Check("the shell hosts the chat and settings icons",
                shell?.ChatButton != null && shell.SettingsButton != null);
            Check("the settings gear uses the settings glyph",
                shell!.SettingsButton.Content?.ToString() == EditorShell.SettingsGlyph);
            Check("the assistant starts closed", !window.AssistantDrawerOpen);
            Check("no settings window is open at startup", window.AssistantUiForTest.SettingsWindowForTest is null);

            shell.SettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            AssistantSettingsWindow? settings = window.AssistantUiForTest.SettingsWindowForTest;
            Check("the gear opens a dedicated settings window", settings is { IsVisible: true });
            Check("opening settings does not open the chat drawer", !window.AssistantDrawerOpen);
            Check("settings offers the provider", settings!.ProviderSelector.ItemCount == 5);
            Check("settings offers the model control", settings.ModelSelector != null);
            Check("the settings model control is the shared palette",
                settings.ModelSelector.VisibleModels.Count == 17);
            settings.ModelSelector.SetAgentFilter("opencode");
            Check("the settings palette filters by agent like the pane",
                settings.ModelSelector.VisibleModels.Count == 12);
            settings.ModelSelector.SetAgentFilter("");
            Check("Codex settings expose ChatGPT sign in", settings.SignInVisible);
            Check("settings explains the ChatGPT boundary",
                settings.NoticeText.Contains("ChatGPT sign-in", StringComparison.Ordinal));

            settings.CloseForTest();
            Dispatcher.UIThread.RunJobs();
            Check("the settings window closes", !settings.IsVisible);

            shell.ChatButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check("the chat icon above the gear opens the chat drawer", window.AssistantDrawerOpen);
            Check("the chat icon does not reopen settings", !settings.IsVisible);
        }
        finally
        {
            Smoke.CloseForTest(window);
        }
    }

    private static void AssistantRepliesRenderAsMarkdown()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            OpenChat(window);
            var fake = new FakeChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "**bgs_check_project** and `bgs_inspect_object`\n\n- first\n- second")));
            AssistantTools tools = window.CreateAssistantTools();
            window.AssistantUiForTest.SetSessionForTest(window.CreateAssistantSession(fake, tools), tools);
            window.AssistantUiForTest.Pane.Composer.Text = "what can you do";
            Smoke.Find<Button>(window.AssistantUiForTest!.Pane).Single(button => button.Content?.ToString() == "Send")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            List<TextBlock> blocks = Smoke.Find<TextBlock>(window).ToList();
            string shown = string.Concat(blocks.Select(Text));

            static string Text(TextBlock block) =>
                block.Text is { Length: > 0 } text ? text
                : block.Inlines is null ? ""
                : string.Concat(block.Inlines.OfType<Run>().Select(run => run.Text));
            Check("a markdown reply shows no literal emphasis markers",
                !shown.Contains("**", StringComparison.Ordinal));
            Check("the bold tool name becomes a bold run",
                blocks.Any(block => block.Inlines is not null && block.Inlines.OfType<Run>()
                    .Any(run => run.Text == "bgs_check_project" && run.FontWeight == FontWeight.SemiBold)));
            Check("inline code becomes a code run",
                blocks.Any(block => block.Inlines is not null && block.Inlines.OfType<Run>()
                    .Any(run => run.Text == "bgs_inspect_object" && run.FontFamily != null)));
            Check("bullets are drawn as bullets", shown.Contains('\u2022', StringComparison.Ordinal));
            Check("the bullet items are rendered", shown.Contains("first", StringComparison.Ordinal));
        }
        finally
        {
            Smoke.CloseForTest(window);
        }
    }

    private static void ProviderSelectionIsExplicitAndPersisted()
    {
        Settings.TrySet("assistant.backend", "", out _);
        Func<string> previousRoot = ClaudeModelCatalog.ConfigRootForTest;
        Func<string, IReadOnlyList<string>> previousFiles = ClaudeModelCatalog.FilesForTest;
        Func<string, string?> previousRead = ClaudeModelCatalog.ReadFileForTest;
        ClaudeModelCatalog.FilesForTest = _ => new[] { "catalog.json" };
        ClaudeModelCatalog.ReadFileForTest = _ =>
            "{\"catalog\":{\"config\":{\"models\":[" +
            "{\"id\":\"claude-opus-5\",\"name\":\"Opus 5\"}," +
            "{\"id\":\"claude-haiku-4-5-20251001\",\"name\":\"Haiku 4.5\"}]}}}";

        var window = new MainWindow();
        window.Show();
        try
        {
            AssistantPane pane = window.AssistantUiForTest.Pane;
            window.AssistantUiForTest.OpenSettings();
            Dispatcher.UIThread.RunJobs();
            AssistantSettingsWindow settings = window.AssistantUiForTest.SettingsWindowForTest!;
            Check("the provider selector lives in the settings window",
                settings.ProviderSelector.ItemCount == 5);
            string[] offered = settings.ProviderSelector.ItemsSource!.Cast<AssistantProviderItem>()
                .Select(item => item.Wire).ToArray();
            Check("every provider is offered exactly once",
                offered.SequenceEqual(new[] { "codex", "claude", "opencode", "gemini", "local" }));
            Check("the assistant starts on Codex", settings.SelectedProvider == "codex");
            Check("Codex keeps the ChatGPT sign-in", window.AssistantUiForTest.SignInVisible);
            Check("the chat header no longer carries provider clutter",
                !pane.ChatTitle.Contains("detected", StringComparison.OrdinalIgnoreCase));

            settings.ProviderSelector.SelectedItem = settings.ProviderSelector.ItemsSource!
                .Cast<AssistantProviderItem>().Single(item => item.Wire == "claude");
            Dispatcher.UIThread.RunJobs();
            Check("choosing a provider persists it", Settings.Get("assistant.backend") == "claude");
            Check("choosing a provider is reported",
                window.AssistantUiForTest.Status.Contains("Claude Code", StringComparison.Ordinal));
            Check("the provider description follows the choice",
                window.AssistantUiForTest.ProviderStatus.Contains("Claude Code", StringComparison.Ordinal));
            Check("Claude uses its own sign-in rather than BGS",
                window.AssistantUiForTest.AccountStatus.Contains("claude", StringComparison.OrdinalIgnoreCase));
            Check("the ChatGPT sign-in buttons are withdrawn for a CLI provider",
                !window.AssistantUiForTest.SignInVisible);
            Check("the notice explains the CLI-managed sign-in",
                window.AssistantUiForTest.NoticeText.Contains("own command-line tool", StringComparison.Ordinal));
            Check("the settings window follows the chosen provider",
                settings.SelectedProvider == "claude" &&
                settings.AccountText.Contains("claude", StringComparison.OrdinalIgnoreCase));

            pane.ModelPicker.SetAgentFilter("Claude");
            Check("Claude offers a real model list rather than free text",
                pane.ModelPicker.VisibleModels.Count == 2);
            Check("the Claude list is the catalog's list",
                pane.ModelPicker.VisibleModels.SequenceEqual(
                    new[] { "claude-opus-5", "claude-haiku-4-5-20251001" }));
            pane.ModelPicker.SetAgentFilter("");
            Check("an unset Claude model names the CLI default",
                pane.ModelButtonLabel.Contains("Claude CLI default", StringComparison.Ordinal));

            Check("a Claude model can be chosen from the palette", pane.ModelPicker.Choose("claude-opus-5"));
            Dispatcher.UIThread.RunJobs();
            Check("a chosen Claude model is persisted",
                Settings.Get(AssistantUi.ModelSettingKey(AssistantBackend.Claude)) == "claude-opus-5");
            Check("each provider keeps its own model",
                AssistantUi.ModelSettingKey(AssistantBackend.Claude) !=
                AssistantUi.ModelSettingKey(AssistantBackend.Codex));
        }
        finally
        {
            Smoke.CloseForTest(window);
            ClaudeModelCatalog.ConfigRootForTest = previousRoot;
            ClaudeModelCatalog.FilesForTest = previousFiles;
            ClaudeModelCatalog.ReadFileForTest = previousRead;
        }

        var restored = new MainWindow();
        restored.Show();
        try
        {
            Check("the chosen provider is restored on the next window",
                restored.AssistantUiForTest.Backend == AssistantBackend.Claude);
        }
        finally
        {
            Smoke.CloseForTest(restored);
            Settings.TrySet("assistant.backend", "", out _);
            Settings.TrySet(AssistantUi.ModelSettingKey(AssistantBackend.Claude), "", out _);
        }
    }

    private static void ActiveEditorApproval()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            string path = Path.Combine("tools", "tests", "fixtures", "vanilla", "Meshes", "Actors",
                "Character", "Behaviors", "SingleAnimFurniture.hkx");
            window.Open(path);
            Dispatcher.UIThread.RunJobs();
            string id = BehaviourGraphModel.Parse(window.LoadedXml).Objects
                .First(objectInfo => objectInfo.Class == "hkbClipGenerator").Id;
            var fake = new FakeChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
                {
                    new FunctionCallContent("call-1", "bgs.set_clip_animation",
                        new Dictionary<string, object?>
                        {
                            ["objectId"] = id,
                            ["animationName"] = "Animations\\Assistant\\Approved.hkx",
                        }),
                })),
                new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    "The preview is ready for your approval.")));
            AssistantTools approvalTools = window.CreateAssistantTools();
            window.AssistantUiForTest.SetSessionForTest(
                window.CreateAssistantSession(fake, approvalTools), approvalTools);
            window.AssistantUiForTest.Pane.Composer.Text = "Update the selected clip.";
            Smoke.Find<Button>(window.AssistantUiForTest!.Pane).Single(button => button.Content?.ToString() == "Send")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check("model can invoke the real clip preview tool", fake.CallCount == 2);
            Check("preview waits for explicit approval",
                window.AssistantUiForTest.ApprovalVisible && !window.IsDirty);

            Smoke.Find<Button>(window).Single(button => button.Content?.ToString() == "Approve action")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check("approval uses the normal editor commit", window.IsDirty && window.UndoStepsForTest == 1);
            Check("approved animation reaches the active document",
                window.LoadedXml.Contains("Approved.hkx", StringComparison.Ordinal));
        }
        finally
        {
            Smoke.CloseForTest(window);
        }
    }

}
