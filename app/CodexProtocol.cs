using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BehaviourStudio.App;

public static class CodexMethods
{
    public const string Initialize = "initialize";
    public const string Initialized = "initialized";
    public const string AccountRead = "account/read";
    public const string AccountLoginStart = "account/login/start";
    public const string AccountLoginCancel = "account/login/cancel";
    public const string AccountLogout = "account/logout";
    public const string AccountUpdated = "account/updated";
    public const string AccountLoginCompleted = "account/login/completed";
    public const string ModelList = "model/list";
    public const string ConfigRead = "config/read";
    public const string ThreadStart = "thread/start";
    public const string ThreadStarted = "thread/started";
    public const string TurnStart = "turn/start";
    public const string TurnInterrupt = "turn/interrupt";
    public const string TurnStarted = "turn/started";
    public const string TurnCompleted = "turn/completed";
    public const string ItemStarted = "item/started";
    public const string ItemCompleted = "item/completed";
    public const string AgentMessageDelta = "item/agentMessage/delta";
    public const string ErrorNotification = "error";
    public const string DynamicToolCall = "item/tool/call";
    public const string CommandApproval = "item/commandExecution/requestApproval";
    public const string FileChangeApproval = "item/fileChange/requestApproval";
    public const string PermissionsApproval = "item/permissions/requestApproval";
    public const string ElicitationRequest = "mcpServer/elicitation/request";
    public const string UserInputRequest = "item/tool/requestUserInput";
    public const string ApplyPatchApproval = "applyPatchApproval";
    public const string ExecCommandApproval = "execCommandApproval";
}

public enum CodexMessageKind
{
    Invalid,
    Response,
    Notification,
    ServerRequest,
}

public readonly record struct CodexEnvelope(
    CodexMessageKind Kind,
    string IdKey,
    string Method,
    JsonElement? Parameters,
    JsonElement? Result,
    int ErrorCode,
    string ErrorMessage);

public static class CodexProtocol
{
    public const int MaximumLineLength = 4 * 1024 * 1024;
    private const int MaximumErrorLength = 300;
    private const int MaximumTextLength = 4000;

    private static readonly string[] SensitiveMarkers =
    {
        "sk-", "eyJ", "bearer ", "access_token", "refresh_token", "id_token", "authorization",
    };

    private static readonly Regex ProtocolToolName =
        new("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryRead(string line, out CodexEnvelope envelope)
    {
        envelope = default;
        if (string.IsNullOrWhiteSpace(line)) return false;

        JsonDocument document;
        try { document = JsonDocument.Parse(line); }
        catch (JsonException) { return false; }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            bool hasId = root.TryGetProperty("id", out JsonElement id);
            string idKey = hasId ? IdKey(id) : "";

            bool hasMethod = root.TryGetProperty("method", out JsonElement method) &&
                             method.ValueKind == JsonValueKind.String;
            string methodName = hasMethod ? method.GetString() ?? "" : "";

            JsonElement? parameters = root.TryGetProperty("params", out JsonElement paramsValue)
                ? paramsValue.Clone() : null;
            JsonElement? result = root.TryGetProperty("result", out JsonElement resultValue)
                ? resultValue.Clone() : null;

            int errorCode = 0;
            string errorMessage = "";
            if (root.TryGetProperty("error", out JsonElement error) &&
                error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("code", out JsonElement code) &&
                    code.ValueKind == JsonValueKind.Number)
                    code.TryGetInt32(out errorCode);
                if (error.TryGetProperty("message", out JsonElement message) &&
                    message.ValueKind == JsonValueKind.String)
                    errorMessage = message.GetString() ?? "";
            }

            CodexMessageKind kind = hasMethod && hasId ? CodexMessageKind.ServerRequest
                : hasMethod ? CodexMessageKind.Notification
                : hasId && (result.HasValue || errorCode != 0 || errorMessage.Length > 0)
                    ? CodexMessageKind.Response
                : CodexMessageKind.Invalid;

            envelope = new(kind, idKey, methodName, parameters, result, errorCode, errorMessage);
            return kind != CodexMessageKind.Invalid;
        }
    }

    public static string IdKey(JsonElement id) => id.ValueKind switch
    {
        JsonValueKind.Number when id.TryGetInt64(out long value) =>
            value.ToString(CultureInfo.InvariantCulture),
        JsonValueKind.String => "s:" + (id.GetString() ?? ""),
        _ => "",
    };

    public static string String(JsonElement? element, string name)
    {
        if (element is not { } value || value.ValueKind != JsonValueKind.Object) return "";
        return value.TryGetProperty(name, out JsonElement property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? "" : "";
    }

    public static bool Bool(JsonElement? element, string name, bool fallback = false)
    {
        if (element is not { } value || value.ValueKind != JsonValueKind.Object) return fallback;
        if (!value.TryGetProperty(name, out JsonElement property)) return fallback;
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback,
        };
    }

    public static JsonElement? Property(JsonElement? element, string name)
    {
        if (element is not { } value || value.ValueKind != JsonValueKind.Object) return null;
        return value.TryGetProperty(name, out JsonElement property) ? property.Clone() : null;
    }

    public static string ErrorText(CodexEnvelope envelope) =>
        Scrub(envelope.ErrorMessage, MaximumErrorLength) is { Length: > 0 } message
            ? $"Codex rejected the request: {message}"
            : "Codex rejected the request.";

    public static string Scrub(string? text, int maximum = MaximumTextLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var builder = new StringBuilder(text.Length);
        foreach (char character in text)
            builder.Append(char.IsControl(character) ? ' ' : character);

        string scrubbed = builder.ToString();
        foreach (string marker in SensitiveMarkers)
        {
            int index;
            while ((index = scrubbed.IndexOf(marker, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = index + marker.Length;
                while (end < scrubbed.Length && !char.IsWhiteSpace(scrubbed[end]) &&
                       scrubbed[end] != ',' && scrubbed[end] != '}')
                    end++;
                scrubbed = scrubbed.Remove(index, end - index).Insert(index, "[redacted]");
            }
        }

        if (scrubbed.Length <= maximum) return scrubbed.Trim();
        return scrubbed[..maximum].Trim() + "...";
    }

    public static string ArgumentJson(JsonElement? arguments)
    {
        const int maximumArguments = 64 * 1024;
        if (arguments is not { } value) return "{}";
        string raw = value.GetRawText();
        return raw.Length is > 0 and <= maximumArguments ? raw : "{}";
    }

    public static bool IsProtocolToolName(string name) => ProtocolToolName.IsMatch(name);

    public static string ToProtocolToolName(string bgsName) => bgsName.Replace('.', '_');

    public static bool TryResolveBgsTool(
        string protocolName, IReadOnlyList<string> bgsNames, out string bgsName)
    {
        foreach (string candidate in bgsNames)
        {
            if (string.Equals(ToProtocolToolName(candidate), protocolName, StringComparison.Ordinal))
            {
                bgsName = candidate;
                return true;
            }
        }
        bgsName = "";
        return false;
    }
}
