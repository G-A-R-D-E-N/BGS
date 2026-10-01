using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BehaviourStudio.App;

public sealed class CodexProtocolException : Exception
{
    public CodexProtocolException(string message, int code = 0, bool requiresExperimentalCapability = false)
        : base(message)
    {
        Code = code;
        RequiresExperimentalCapability = requiresExperimentalCapability;
    }

    public int Code { get; }
    public bool RequiresExperimentalCapability { get; }
}

public sealed record CodexServerInfo(
    string UserAgent,
    string Version,
    string PlatformFamily,
    string PlatformOs);

public sealed record CodexToolCall(
    string ProtocolName,
    string CallId,
    string ThreadId,
    string TurnId,
    string Arguments);

public sealed record CodexToolResult(bool Success, string Text);

public sealed class CodexAppServerClient : IDisposable
{
    private const int MaximumResultText = 8000;

    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static readonly Dictionary<string, object> Refusals = new(StringComparer.Ordinal)
    {
        [CodexMethods.CommandApproval] = new { decision = "cancel" },
        [CodexMethods.FileChangeApproval] = new { decision = "cancel" },
        [CodexMethods.ApplyPatchApproval] = new { decision = "abort" },
        [CodexMethods.ExecCommandApproval] = new { decision = "abort" },
        [CodexMethods.PermissionsApproval] = new { permissions = new { } },
        [CodexMethods.ElicitationRequest] = new { action = "decline" },
        [CodexMethods.UserInputRequest] = new { answers = new { } },
    };

    private readonly ICodexTransport _transport;
    private readonly string _clientName;
    private readonly string _clientVersion;
    private readonly bool _requestExperimental;
    private readonly TimeSpan _requestTimeout;
    private readonly object _gate = new();
    private readonly Dictionary<string, TaskCompletionSource<JsonElement?>> _pending = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Func<CodexToolCall, Task<CodexToolResult>>> _toolHandlers =
        new(StringComparer.Ordinal);

    private long _nextId;
    private bool _connected;
    private bool _disposed;

    public CodexAppServerClient(
        ICodexTransport transport,
        string clientName = "behaviour_graph_studio",
        string? clientVersion = null,
        bool requestExperimental = true,
        TimeSpan? requestTimeout = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _clientName = clientName;
        _clientVersion = clientVersion ?? BuildInfo.Version;
        _requestExperimental = requestExperimental;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        _transport.LineReceived += OnLineReceived;
        _transport.Exited += OnTransportExited;
    }

    public event Action<string, JsonElement?>? Notification;
    public event Action<string>? RefusedServerRequest;
    public event Action<CodexProcessExit>? Disconnected;

    public CodexServerInfo? ServerInfo { get; private set; }
    public bool IsConnected => _connected && _transport.IsRunning;

    public bool? DynamicToolsAvailable { get; internal set; }

    public void RegisterToolHandler(string threadId, Func<CodexToolCall, Task<CodexToolResult>> handler)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        _toolHandlers[threadId] = handler;
    }

    public bool UnregisterToolHandler(string threadId)
    {
        if (threadId.Length == 0) return false;
        return _toolHandlers.TryRemove(threadId, out _);
    }

    public string? RegisteredToolThreadCount =>
        _toolHandlers.IsEmpty ? "0" : _toolHandlers.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public bool TryRouteToolCall(CodexToolCall call, out Func<CodexToolCall, Task<CodexToolResult>>? routed)
    {
        routed = null;
        if (call.ThreadId.Length == 0) return false;
        return _toolHandlers.TryGetValue(call.ThreadId, out routed) && routed != null;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connected) return;

        await _transport.StartAsync(cancellationToken).ConfigureAwait(false);

        object initializeParameters = new
        {
            clientInfo = new { name = _clientName, title = "Behaviour Graph Studio", version = _clientVersion },
            capabilities = _requestExperimental ? new { experimentalApi = true } : null,
        };

        JsonElement? result = await RequestAsync(CodexMethods.Initialize, initializeParameters, cancellationToken)
            .ConfigureAwait(false);
        string userAgent = CodexProtocol.String(result, "userAgent");
        ServerInfo = new CodexServerInfo(
            userAgent,
            VersionFromUserAgent(userAgent),
            CodexProtocol.String(result, "platformFamily"),
            CodexProtocol.String(result, "platformOs"));

        await SendNotificationAsync(CodexMethods.Initialized, new { }, cancellationToken).ConfigureAwait(false);
        _connected = true;
    }

    public async Task<JsonElement?> RequestAsync(
        string method, object? parameters, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        long id = Interlocked.Increment(ref _nextId);
        string idKey = id.ToString(CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate) _pending[idKey] = completion;

        try
        {
            string payload = JsonSerializer.Serialize(new { id, method, @params = parameters }, Wire);
            await _transport.SendLineAsync(payload, cancellationToken).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_requestTimeout);
            using (timeout.Token.Register(() => completion.TrySetCanceled(timeout.Token)))
            {
                try
                {
                    return await completion.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new CodexProtocolException("Codex did not answer in time.");
                }
            }
        }
        finally
        {
            lock (_gate) _pending.Remove(idKey);
        }
    }

    public Task SendNotificationAsync(
        string method, object? parameters, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string payload = JsonSerializer.Serialize(new { method, @params = parameters }, Wire);
        return _transport.SendLineAsync(payload, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _transport.LineReceived -= OnLineReceived;
        _transport.Exited -= OnTransportExited;
        FailPending("the Codex app server was closed.");
        _transport.Dispose();
    }

    internal static string VersionFromUserAgent(string userAgent)
    {
        if (string.IsNullOrEmpty(userAgent)) return "";
        int slash = userAgent.IndexOf('/');
        if (slash < 0) return "";
        int end = userAgent.IndexOfAny(new[] { ' ', '(' }, slash + 1);
        string version = end < 0 ? userAgent[(slash + 1)..] : userAgent[(slash + 1)..end];
        return CodexProtocol.Scrub(version, 32);
    }

    private void OnLineReceived(string line)
    {
        if (!CodexProtocol.TryRead(line, out CodexEnvelope envelope)) return;
        switch (envelope.Kind)
        {
            case CodexMessageKind.Response:
                CompletePending(envelope);
                break;
            case CodexMessageKind.Notification:
                RaiseNotification(envelope);
                break;
            case CodexMessageKind.ServerRequest:
                _ = HandleServerRequestAsync(envelope);
                break;
        }
    }

    private void CompletePending(CodexEnvelope envelope)
    {
        TaskCompletionSource<JsonElement?>? completion;
        lock (_gate)
        {
            if (envelope.IdKey.Length == 0 || !_pending.TryGetValue(envelope.IdKey, out completion))
                completion = null;
        }
        if (completion is null) return;

        if (envelope.Result.HasValue)
        {
            completion.TrySetResult(envelope.Result);
            return;
        }

        string message = CodexProtocol.ErrorText(envelope);
        bool experimental = envelope.ErrorMessage.Contains("experimentalApi", StringComparison.OrdinalIgnoreCase);
        completion.TrySetException(new CodexProtocolException(message, envelope.ErrorCode, experimental));
    }

    private void RaiseNotification(CodexEnvelope envelope)
    {
        try
        {
            Notification?.Invoke(envelope.Method, envelope.Parameters);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    private async Task HandleServerRequestAsync(CodexEnvelope envelope)
    {
        try
        {
            if (envelope.Method == CodexMethods.DynamicToolCall)
            {
                await HandleToolCallAsync(envelope).ConfigureAwait(false);
                return;
            }

            if (Refusals.TryGetValue(envelope.Method, out object? refusal))
            {
                await RespondAsync(envelope.IdKey, refusal).ConfigureAwait(false);
                RefusedServerRequest?.Invoke(envelope.Method);
                return;
            }

            await RespondErrorAsync(envelope.IdKey, -32601, "unsupported by Behaviour Graph Studio")
                .ConfigureAwait(false);
            RefusedServerRequest?.Invoke(envelope.Method);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    private async Task HandleToolCallAsync(CodexEnvelope envelope)
    {
        if (envelope.IdKey.Length == 0) return;
        JsonElement? parameters = envelope.Parameters;
        var call = new CodexToolCall(
            CodexProtocol.String(parameters, "tool"),
            CodexProtocol.String(parameters, "callId"),
            CodexProtocol.String(parameters, "threadId"),
            CodexProtocol.String(parameters, "turnId"),
            CodexProtocol.ArgumentJson(CodexProtocol.Property(parameters, "arguments")));

        CodexToolResult result;
        if (!TryRouteToolCall(call, out Func<CodexToolCall, Task<CodexToolResult>>? handler) || handler is null)
        {
            result = new CodexToolResult(false, "BGS is not accepting tool calls in this session.");
        }
        else
        {
            try
            {
                result = await handler(call).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result = new CodexToolResult(false, "The BGS tool failed.");
            }
        }

        object response = new
        {
            contentItems = new[] { new { type = "inputText", text = CodexProtocol.Scrub(result.Text, MaximumResultText) } },
            success = result.Success,
        };
        await RespondAsync(envelope.IdKey, response).ConfigureAwait(false);
    }

    private Task RespondAsync(string idKey, object result)
    {
        if (idKey.Length == 0) return Task.CompletedTask;
        string id = idKey.StartsWith("s:", StringComparison.Ordinal)
            ? JsonSerializer.Serialize(idKey[2..]) : idKey;
        string payload = "{\"id\":" + id + ",\"result\":" + JsonSerializer.Serialize(result, Wire) + "}";
        return _transport.SendLineAsync(payload, CancellationToken.None);
    }

    private Task RespondErrorAsync(string idKey, int code, string message)
    {
        if (idKey.Length == 0) return Task.CompletedTask;
        string id = idKey.StartsWith("s:", StringComparison.Ordinal)
            ? JsonSerializer.Serialize(idKey[2..]) : idKey;
        string payload = "{\"id\":" + id + ",\"error\":{\"code\":" +
            code.ToString(CultureInfo.InvariantCulture) + ",\"message\":" +
            JsonSerializer.Serialize(message) + "}}";
        return _transport.SendLineAsync(payload, CancellationToken.None);
    }

    private void OnTransportExited(CodexProcessExit exit)
    {
        _connected = false;
        FailPending(exit.Reason);
        Disconnected?.Invoke(exit);
    }

    private void FailPending(string reason)
    {
        TaskCompletionSource<JsonElement?>[] pending;
        lock (_gate)
        {
            pending = new TaskCompletionSource<JsonElement?>[_pending.Count];
            _pending.Values.CopyTo(pending, 0);
            _pending.Clear();
        }
        foreach (TaskCompletionSource<JsonElement?> completion in pending)
            completion.TrySetException(new CodexProtocolException(CodexProtocol.Scrub(reason, 200)));
    }
}
