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
{
    internal static void Run()
    {
        string previousFolder = AssistantChatStore.Folder;
        string? previousSettings = Settings.SettingsPathForTest;
        string tempFolder = Path.Combine(Path.GetTempPath(),
            "bgs-assistant-smoke-" + Guid.NewGuid().ToString("N"));
        string settingsPath = Path.Combine(Path.GetTempPath(),
            "bgs-assistant-settings-" + Guid.NewGuid().ToString("N") + ".cfg");
        AssistantChatStore.Folder = tempFolder;
        Settings.SettingsPathForTest = settingsPath;
        Func<Task<IReadOnlyList<CodexModel>>>? previousCatalog = AssistantUi.AllAgentsForTest;
        AssistantUi.AllAgentsForTest = () => Task.FromResult(SmokeCatalog());
        try
        {
            RunInternal();
        }
        finally
        {
            AssistantUi.AllAgentsForTest = previousCatalog;
            AssistantChatStore.Folder = previousFolder;
            Settings.SettingsPathForTest = previousSettings;
            try { if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            try { if (File.Exists(settingsPath)) File.Delete(settingsPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void RunInternal()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Check("assistant starts closed", !window.AssistantDrawerOpen);
            EditorShell? shell = EditorShell.Find(window.Content as Control);
            Check("the left rail owns both assistant entry points",
                shell is { ChatButton: not null, SettingsButton: not null });
            Check("the rail icons carry the chat and settings glyphs",
                shell!.ChatButton.Content?.ToString() == EditorShell.ChatGlyph &&
                shell.SettingsButton.Content?.ToString() == EditorShell.SettingsGlyph);
            Check("the toolbar ChatGPT box is gone",
                !Smoke.Find<Button>(window).Any(button => button.Content?.ToString() == "Chat") &&
                !Smoke.Find<Button>(window).Any(button => button.Content?.ToString() == "Assistant settings"));
            Check("assistant advertises the ChatGPT subscription boundary without a request",
                window.AssistantUiForTest.ProviderStatus.Contains("ChatGPT via Codex", StringComparison.Ordinal));
            Check("assistant starts signed out and offers sign in",
                window.AssistantUiForTest.AccountStatus == "Signed out" && window.AssistantUiForTest.SignInVisible);

            string activity = window.SelectedActivity;
            var fake = new FakeChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello from the test provider.")));
            shell.SettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check("Assistant settings opens its own window, not the chat drawer",
                window.AssistantUiForTest.SettingsWindowForTest is { IsVisible: true } && !window.AssistantDrawerOpen);
            Check("Assistant settings preserves active activity", activity == window.SelectedActivity);
            Check("Assistant settings exposes ChatGPT sign in",
                window.AssistantUiForTest.SettingsWindowForTest!.SignInVisible);
            Check("opening Assistant settings makes no provider request", fake.CallCount == 0);

            shell.ChatButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check("Chat opens the assistant drawer", window.AssistantDrawerOpen);
            Check("Chat preserves active activity", activity == window.SelectedActivity);
            Check("opening Chat makes no provider request", fake.CallCount == 0);
            Check("opening Chat leaves the account signed out",
                window.AssistantUiForTest.AccountStatus == "Signed out" &&
                window.AssistantUiForTest.SignInVisible);

            AssistantTools smokeTools = window.CreateAssistantTools();
            window.AssistantUiForTest.SetSessionForTest(
                window.CreateAssistantSession(fake, smokeTools), smokeTools);
            window.AssistantUiForTest.Pane.Composer.Text = "Say hello.";
            Smoke.Find<Button>(window.AssistantUiForTest.Pane).Single(button => button.Content?.ToString() == "Send")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check("send reaches the configured provider", fake.CallCount == 1);
            Check("conversation shows the user and assistant messages",
                window.AssistantUiForTest.MessageCount == 2);
            Check("completed request returns to ready", window.AssistantUiForTest.Status == "Ready.");

            Smoke.Find<Button>(window).Single(button => button.Content?.ToString() == "Close")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check("closing assistant restores the shell", !window.AssistantDrawerOpen);
        }
        finally
        {
            Smoke.CloseForTest(window);
        }

        QuickStartShowsOnceOnFirstOpen();
        WindowOwnsCodexConnection();
        TheGearOpensAssistantSettings();
        AssistantRepliesRenderAsMarkdown();
        ModelSelectorListsCatalogAndPersistsChoice();
        ThePaletteFiltersByAgentAndSwitchesProvider();
        ApiProvidersUseASessionKeyAndQueryTheEndpoint();
        ApiCredentialsStayWithTheirProvider();
        DuplicateModelIdsStayDistinct();
        AQueuedMessageNeverCrossesConversations();
        DeletingTheLastChatLeavesAUsableConversation();
        UnknownSlashCommandsAreRefused();
        ProviderChangesResetIdleSessions();
        ProviderFailuresKeepTheirActionableDetail();
        ProgressShowsWhileATurnRuns();
        ComposerSendsQueuesAndRunsCommands();
        SlashCommandsCompleteInTheComposer();
        ProviderSelectionIsExplicitAndPersisted();
        ActiveEditorApproval();
        StaleApprovalIsNotReportedAsApplied();
    }









    private static void Pump(Func<bool> until, int milliseconds = 10000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (until()) return;
            Thread.Sleep(10);
        }
        Dispatcher.UIThread.RunJobs();
        if (!until()) throw new TimeoutException("Assistant UI smoke condition did not complete within its deadline.");
    }



    private static void PressEnter(MainWindow window, AssistantPane pane, bool shift)
    {
        pane.Composer.Focus();
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.Enter, shift ? RawInputModifiers.Shift : RawInputModifiers.None,
            PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static string TextShown(MainWindow window) =>
        string.Concat(Smoke.Find<TextBlock>(window).Select(block =>
            block.Text is { Length: > 0 } text
                ? text
                : block.Inlines is null
                    ? ""
                    : string.Concat(block.Inlines.OfType<Run>().Select(run => run.Text))));





    private static bool ToggleFavorite(AssistantPane pane, string model)
    {
        bool before = AssistantModelPrefs.IsFavorite(model);
        pane.FavoriteForTest(model);
        Dispatcher.UIThread.RunJobs();
        return AssistantModelPrefs.IsFavorite(model) != before;
    }

    internal static void OpenChat(MainWindow window)
    {
        EditorShell shell = EditorShell.Find(window.Content as Control)
            ?? throw new InvalidOperationException("the editor shell is unavailable");
        shell.ChatButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }





    private static void Check(string name, bool value)
    {
        Smoke.CheckTrue(name, value);
        if (!value) throw new InvalidOperationException(name);
    }



}
