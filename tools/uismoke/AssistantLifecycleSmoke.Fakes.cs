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
{    private sealed class WindowTransport : ICodexTransport
    {
        internal const string Catalog =
            "{\"data\":[" +
            "{\"id\":\"gpt-5.6-sol\",\"model\":\"gpt-5.6-sol\",\"displayName\":\"GPT-5.6-Sol\",\"isDefault\":true}," +
            "{\"id\":\"gpt-6-astra\",\"model\":\"gpt-6-astra\",\"displayName\":\"GPT-6-Astra\"}," +
            "{\"id\":\"gpt-5.5\",\"model\":\"gpt-5.5\",\"displayName\":\"GPT-5.5\"}" +
            "],\"nextCursor\":null}";

        public event Action<string>? LineReceived;
        public event Action<CodexProcessExit>? Exited;

        public bool IsRunning { get; private set; }
        internal bool Disposed { get; private set; }

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

            string method = methodElement.GetString() ?? "";
            string result = method switch
            {
                "initialize" => "{\"userAgent\":\"behaviour_graph_studio/0.154.0\",\"platformFamily\":\"windows\",\"platformOs\":\"windows\"}",
                "account/read" => "{\"account\":{\"type\":\"chatgpt\",\"email\":null,\"planType\":\"plus\"},\"requiresOpenaiAuth\":true}",
                "model/list" => WindowTransport.Catalog,
                _ => "{}",
            };
            LineReceived?.Invoke("{\"id\":" + id.GetRawText() + ",\"result\":" + result + "}");
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Disposed = true;
            IsRunning = false;
        }
    }

    private sealed class GatedChatClient : IChatClient
    {
        private readonly TaskCompletionSource<ChatResponse> _first;
        private readonly Queue<ChatResponse> _rest;
        private int _calls;

        internal GatedChatClient(TaskCompletionSource<ChatResponse> first, params ChatResponse[] rest)
        {
            _first = first;
            _rest = new Queue<ChatResponse>(rest);
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _calls) == 1) return _first.Task;
            return Task.FromResult(_rest.Count > 0
                ? _rest.Dequeue()
                : new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done.")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

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
