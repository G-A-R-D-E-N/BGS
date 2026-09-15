using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BehaviourStudio.App;
using Microsoft.Extensions.AI;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.UiSmoke;

internal static class AssistantLifecycleSmoke
{
    internal static void Run()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Check("assistant starts closed", !window.AssistantDrawerOpen);
            Check("assistant toggle exists once", Smoke.Find<Button>(window)
                .Count(button => button.Content?.ToString() == "Assistant") == 1);
            Check("assistant advertises provider state without a request",
                window.AssistantUiForTest.ProviderStatus.Contains("OpenAI-compatible", StringComparison.Ordinal));

            string activity = window.SelectedActivity;
            var fake = new FakeChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello from the test provider.")));
            var toggle = Smoke.Find<Button>(window).Single(button => button.Content?.ToString() == "Assistant");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check("opening assistant shows the drawer", window.AssistantDrawerOpen);
            Check("opening assistant preserves active activity", activity == window.SelectedActivity);
            Check("opening assistant makes no provider request", fake.CallCount == 0);

            window.AssistantUiForTest.SetClientForTest(fake);
            window.AssistantUiForTest.Pane.Composer.Text = "Say hello.";
            Smoke.Find<Button>(window).Single(button => button.Content?.ToString() == "Send")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check("send reaches the configured provider", fake.CallCount == 1);
            Check("conversation shows the user and assistant messages",
                window.AssistantUiForTest.MessageCount == 2);
            Check("completed request returns to ready", window.AssistantUiForTest.Status == "Ready.");

            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check("closing assistant restores the shell", !window.AssistantDrawerOpen);
        }
        finally
        {
            Smoke.CloseForTest(window);
        }

        ActiveEditorApproval();
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
            window.AssistantUiForTest.SetClientForTest(fake);
            window.AssistantUiForTest.Pane.Composer.Text = "Update the selected clip.";
            Smoke.Find<Button>(window).Single(button => button.Content?.ToString() == "Send")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check("model can invoke the real clip preview tool", fake.CallCount == 2);
            Check("preview waits for explicit approval",
                window.AssistantUiForTest.ApprovalVisible && !window.IsDirty);

            Smoke.Find<Button>(window).Single(button => button.Content?.ToString() == "Apply approved edit")
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

    private static void Check(string name, bool value) => Smoke.CheckTrue(name, value);

    private sealed class FakeChatClient : IChatClient
    {
        private readonly Queue<ChatResponse> _responses;

        internal FakeChatClient(params ChatResponse[] responses) => _responses = new(responses);
        internal int CallCount { get; private set; }

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
}
