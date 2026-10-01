using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BehaviourStudio.App;

namespace BehaviourStudio.Tests;

internal sealed class CodexTestServer : ICodexTransport
{
    private readonly List<string> _sent = new();

    public CodexTestServer()
    {
        Handlers[CodexMethods.Initialize] = _ =>
            "{\"userAgent\":\"behaviour_graph_studio/0.154.0 (Windows 10.0) (bgs; 1.1.0)\"," +
            "\"codexHome\":\"C:\\\\Users\\\\someone\\\\.codex\",\"platformFamily\":\"windows\",\"platformOs\":\"windows\"}";
        Handlers[CodexMethods.AccountRead] = _ => AccountResult();
        Handlers[CodexMethods.ModelList] = _ =>
            "{\"data\":[{\"id\":\"" + DefaultModel + "\",\"model\":\"" + DefaultModel +
            "\",\"displayName\":\"Test Model\",\"isDefault\":true,\"hidden\":false}]}";
        Handlers[CodexMethods.ConfigRead] = _ => ConfigResult();
        Handlers[CodexMethods.AccountLoginStart] = root =>
            RequestType(root) == "chatgptDeviceCode"
                ? "{\"type\":\"chatgptDeviceCode\",\"loginId\":\"login-device\"," +
                  "\"userCode\":\"ABCD-1234\",\"verificationUrl\":\"https://example.invalid/device\"}"
                : "{\"type\":\"chatgpt\",\"loginId\":\"login-browser\"," +
                  "\"authUrl\":\"https://example.invalid/auth\"}";
        Handlers[CodexMethods.AccountLoginCancel] = _ => "{\"status\":\"canceled\"}";
        Handlers[CodexMethods.AccountLogout] = _ => "{}";
        Handlers[CodexMethods.ThreadStart] = _ => ThreadResult("thread-" + ThreadStarts);
        Handlers[CodexMethods.TurnStart] = _ => TurnStart();
        Handlers[CodexMethods.TurnInterrupt] = _ => TurnInterrupt();
    }

    public Dictionary<string, Func<JsonElement, string?>> Handlers { get; } =
        new(StringComparer.Ordinal);

    public IReadOnlyList<string> Sent => _sent;
    public List<string> Deltas { get; } = new();
    public List<string> EmittedNotifications { get; } = new();

    public bool IsRunning { get; private set; } = true;
    public bool SignedIn { get; set; } = true;
    public string AuthMode { get; set; } = "chatgpt";
    public string PlanType { get; set; } = "plus";
    public bool RequiresOpenaiAuth { get; set; } = true;
    public bool RejectDynamicTools { get; set; }
    public bool RejectConfigRead { get; set; }
    public bool AutoCompleteTurns { get; set; } = true;
    public string DefaultModel { get; set; } = "test-model";
    public string FinalText { get; set; } = "ready";
    public string? FailMethodOnce { get; set; }
    public string[] EffectiveMcpServers { get; set; } = Array.Empty<string>();
    public bool Interrupted { get; private set; }
    public int TurnStarts { get; private set; }
    public int ThreadStarts { get; private set; }

    public JsonElement? LastConfigReadParams { get; private set; }
    public JsonElement? LastThreadStartParams { get; private set; }
    public JsonElement? LastTurnStartParams { get; private set; }
    public string LastThreadId { get; internal set; } = "";
    public string LastTurnId { get; internal set; } = "";
    public string LastToolArguments { get; private set; } = "";
    public string LastToolResponse { get; private set; } = "";
    public string LastToolName { get; set; } = "";
    public int ToolResponses { get; private set; }

    public event Action<string>? LineReceived;
    public event Action<CodexProcessExit>? Exited;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        IsRunning = true;
        return Task.CompletedTask;
    }

    public Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        if (!IsRunning) throw new InvalidOperationException("the Codex app server is not running");
        _sent.Add(line);

        JsonDocument document;
        try { document = JsonDocument.Parse(line); }
        catch (JsonException) { return Task.CompletedTask; }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("result", out JsonElement toolResult) &&
                toolResult.ValueKind == JsonValueKind.Object &&
                toolResult.TryGetProperty("contentItems", out _))
            {
                LastToolResponse = toolResult.GetRawText();
                ToolResponses++;
                return Task.CompletedTask;
            }

            if (!root.TryGetProperty("method", out JsonElement methodElement)) return Task.CompletedTask;
            string method = methodElement.GetString() ?? "";
            bool hasId = root.TryGetProperty("id", out JsonElement id);
            if (!hasId) return Task.CompletedTask;

            if (FailMethodOnce == method)
            {
                FailMethodOnce = null;
                Exit();
                return Task.CompletedTask;
            }

            if (method == CodexMethods.ConfigRead)
            {
                RecordConfigRead(root);
                if (RejectConfigRead)
                {
                    EmitRaw(ErrorLine(id, -32603, "config read unavailable"));
                    return Task.CompletedTask;
                }
            }

            if (method == CodexMethods.ThreadStart)
            {
                RecordThreadStart(root);
                if (RejectDynamicTools && HasDynamicTools(root))
                {
                    EmitRaw(ErrorLine(id, -32600, "thread/start.dynamicTools requires experimentalApi capability"));
                    return Task.CompletedTask;
                }
            }

            if (method == CodexMethods.TurnStart) RecordTurnStart(root);

            if (Handlers.TryGetValue(method, out Func<JsonElement, string?>? handler))
            {
                string? result = handler(root);
                if (result is not null) EmitRaw(ResultLine(id, result));
                return Task.CompletedTask;
            }

            EmitRaw(ErrorLine(id, -32601, "method not found"));
            return Task.CompletedTask;
        }
    }

    public void Dispose() => IsRunning = false;

    public void EmitRaw(string line) => LineReceived?.Invoke(line);

    public void EmitServerRequest(string method, string idJson, string paramsJson)
    {
        EmittedNotifications.Add(method);
        EmitRaw("{\"id\":" + idJson + ",\"method\":\"" + method + "\",\"params\":" + paramsJson + "}");
    }

    public void Notify(string method, string paramsJson)
    {
        EmittedNotifications.Add(method);
        EmitRaw("{\"method\":\"" + method + "\",\"params\":" + paramsJson + "}");
    }

    public void Exit()
    {
        IsRunning = false;
        Exited?.Invoke(new CodexProcessExit(1, "the Codex app server output stream ended"));
    }

    public string ThreadResult(string id) =>
        "{\"thread\":{\"id\":\"" + id + "\",\"ephemeral\":true,\"preview\":\"\",\"modelProvider\":\"openai\"," +
        "\"cwd\":\"/scratch\",\"cliVersion\":\"0.154.0\",\"createdAt\":1,\"updatedAt\":1,\"projectId\":null," +
        "\"sessionId\":\"" + id + "\",\"source\":\"appServer\",\"status\":{\"type\":\"idle\"},\"turns\":[]}," +
        "\"model\":\"test-model\",\"modelProvider\":\"openai\",\"runtimeWorkspaceRoots\":[]," +
        "\"sandbox\":{\"type\":\"readOnly\",\"networkAccess\":false}," +
        "\"approvalPolicy\":\"never\",\"cwd\":\"/scratch\"}";

    private string ConfigResult()
    {
        var servers = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (string name in EffectiveMcpServers)
            servers[name] = new { enabled = true };
        var config = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["model_provider"] = "openai",
            ["mcp_servers"] = servers,
        };
        return JsonSerializer.Serialize(new { config, origins = new { } });
    }

    private string TurnStart()
    {
        TurnStarts++;
        string turnId = $"turn-{TurnStarts}";
        string threadId = LastThreadId;
        if (threadId.Length == 0) threadId = "thread-1";
        LastTurnId = turnId;
        if (AutoCompleteTurns)
        {
            Notify(CodexMethods.TurnStarted,
                "{\"turn\":{\"id\":\"" + turnId + "\"}}");
            Notify(CodexMethods.ItemStarted, Item("userMessage", "u1", null, threadId, turnId));
            Notify(CodexMethods.ItemCompleted, Item("userMessage", "u1", null, threadId, turnId));
            foreach (string delta in Deltas)
                Notify(CodexMethods.AgentMessageDelta,
                    "{\"threadId\":\"" + threadId + "\",\"turnId\":\"" + turnId + "\",\"itemId\":\"a1\",\"delta\":" +
                    JsonSerializer.Serialize(delta) + "}");
            Notify(CodexMethods.ItemStarted, Item("agentMessage", "a1", null, threadId, turnId));
            Notify(CodexMethods.ItemCompleted, Item("agentMessage", "a1", FinalText, threadId, turnId));
            Notify(CodexMethods.TurnCompleted,
                "{\"threadId\":\"" + threadId + "\",\"turn\":{\"id\":\"" + turnId + "\",\"status\":\"completed\"," +
                "\"items\":[" + Item("agentMessage", "a1", FinalText, threadId, turnId) + "],\"itemsView\":\"summary\"}}");
        }
        return "{\"turn\":{\"id\":\"" + turnId + "\",\"items\":[],\"itemsView\":\"notLoaded\"," +
               "\"status\":\"inProgress\"}}";
    }

    private string TurnInterrupt()
    {
        Interrupted = true;
        string threadId = LastThreadId;
        if (threadId.Length == 0) threadId = "thread-1";
        string turnId = LastTurnId.Length > 0 ? LastTurnId : "turn-1";
        Notify(CodexMethods.TurnCompleted,
            "{\"threadId\":\"" + threadId + "\",\"turn\":{\"id\":\"" + turnId + "\",\"status\":\"interrupted\",\"items\":[]," +
            "\"itemsView\":\"summary\"}}");
        return "{}";
    }

    private string AccountResult()
    {
        if (!SignedIn)
            return "{\"account\":null,\"requiresOpenaiAuth\":" +
                (RequiresOpenaiAuth ? "true" : "false") + "}";
        string plan = AuthMode == "chatgpt" ? ",\"email\":null,\"planType\":\"" + PlanType + "\"" : "";
        return "{\"account\":{\"type\":\"" + AuthMode + "\"" + plan + "},\"requiresOpenaiAuth\":true}";
    }

    private static bool HasDynamicTools(JsonElement root) =>
        root.TryGetProperty("params", out JsonElement parameters) &&
        parameters.TryGetProperty("dynamicTools", out JsonElement tools) &&
        tools.ValueKind == JsonValueKind.Array && tools.GetArrayLength() > 0;

    private static string RequestType(JsonElement root) =>
        root.TryGetProperty("params", out JsonElement parameters)
            ? CodexProtocol.String(parameters, "type")
            : "";

    private static string ResultLine(JsonElement id, string result) =>
        "{\"id\":" + id.GetRawText() + ",\"result\":" + result + "}";

    private static string ErrorLine(JsonElement id, int code, string message) =>
        "{\"id\":" + id.GetRawText() + ",\"error\":{\"code\":" + code + ",\"message\":" +
        JsonSerializer.Serialize(message) + "}}";

    private static string Item(string type, string id, string? text) =>
        Item(type, id, text, "thread-1", "turn-1");

    private static string Item(string type, string id, string? text, string threadId, string turnId) =>
        text is null
            ? "{\"threadId\":\"" + threadId + "\",\"turnId\":\"" + turnId + "\",\"item\":{\"id\":\"" + id + "\",\"type\":\"" +
              type + "\"}}"
            : "{\"threadId\":\"" + threadId + "\",\"turnId\":\"" + turnId + "\",\"item\":{\"id\":\"" + id + "\",\"type\":\"" +
              type + "\",\"text\":" + JsonSerializer.Serialize(text) +
              ",\"phase\":\"final_answer\"}}";

    internal void RecordConfigRead(JsonElement root)
    {
        if (root.TryGetProperty("params", out JsonElement parameters))
            LastConfigReadParams = parameters.Clone();
    }

    internal void RecordThreadStart(JsonElement root)
    {
        ThreadStarts++;
        LastThreadId = "thread-" + ThreadStarts;
        if (root.TryGetProperty("params", out JsonElement parameters))
            LastThreadStartParams = parameters.Clone();
    }

    internal void RecordTurnStart(JsonElement root)
    {
        LastTurnId = "turn-" + TurnStarts;
        if (root.TryGetProperty("params", out JsonElement parameters))
            LastTurnStartParams = parameters.Clone();
    }
}