using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace BehaviourStudio.App;

public sealed record ClaudeTurn(string Text, string Error, bool Completed)
{
    public bool Failed => Error.Length > 0;
}

public static class ClaudeCli
{
    public const string ServerName = "bgs";
    public const string AllowedTools = "mcp__" + ServerName;
    public const int MaximumAnswerCharacters = 8000;

    internal static readonly string[] DeniedTools =
    {
        "Bash", "Edit", "Write", "Read", "Glob", "Grep", "WebFetch", "WebSearch",
        "PowerShell", "NotebookEdit", "Task", "Skill",
        "TaskCreate", "TaskGet", "TaskList", "TaskOutput", "TaskStop", "TaskUpdate", "ToolSearch",
    };

    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public static string BuildMcpConfigJson(string bridgeUrl, string token)
    {
        var server = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "http",
            ["url"] = bridgeUrl,
            ["headers"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Authorization"] = "Bearer " + token,
            },
        };
        var config = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["mcpServers"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ServerName] = server,
            },
        };
        return JsonSerializer.Serialize(config, Wire);
    }

    public static IReadOnlyList<string> Arguments(
        string configPath, string model, string prompt)
    {
        var arguments = new List<string>
        {
            "-p", "--output-format", "stream-json", "--verbose",
            "--strict-mcp-config", "--mcp-config", configPath,
            "--allowedTools", AllowedTools,
            "--disallowedTools", string.Join(',', DeniedTools),
        };
        if (model.Length > 0)
        {
            arguments.Add("--model");
            arguments.Add(model);
        }
        arguments.Add(prompt);
        return arguments;
    }

    public static ClaudeTurn Parse(string output, int exitCode, string standardError)
    {
        string answer = "";
        bool isError = false;
        bool completed = false;
        string lastText = "";

        foreach (string line in output.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] != '{') continue;

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(trimmed);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                string type = CodexProtocol.String(root, "type");
                if (type == "assistant")
                {
                    string text = AssistantText(root);
                    if (text.Length > 0) lastText = text;
                    continue;
                }
                if (type != "message") continue;

                completed = true;
                isError = CodexProtocol.Bool(root, "is_error");
                answer = CodexProtocol.String(root, "result");
            }
        }

        if (isError)
        {
            string message = CodexProtocol.Scrub(
                lastText.Length > 0 ? lastText : ExitDetail(exitCode, standardError), 300);
            return new ClaudeTurn("", message.Length > 0 ? message : "Claude reported an error.", true);
        }
        if (!completed && exitCode != 0)
            return new ClaudeTurn("", ExitDetail(exitCode, standardError), false);
        if (!completed)
            return new ClaudeTurn("", "Claude did not complete the request.", false);

        string final = answer.Length > 0 ? answer : lastText;
        return new ClaudeTurn(CodexProtocol.Scrub(final.Trim(), MaximumAnswerCharacters), "", true);
    }

    private static string AssistantText(JsonElement root)
    {
        JsonElement? content = CodexProtocol.Property(CodexProtocol.Property(root, "message"), "content");
        if (content is not { ValueKind: JsonValueKind.Array } items) return "";
        var text = new StringBuilder();
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!string.Equals(CodexProtocol.String(item, "type"), "text", StringComparison.Ordinal)) continue;
            text.Append(CodexProtocol.String(item, "text"));
        }
        return text.ToString();
    }

    private static string ExitDetail(int exitCode, string standardError) =>
        CodexProtocol.Scrub(standardError.Trim(), 300) is { Length: > 0 } message
            ? message
            : "Claude exited with code " + exitCode + ".";
}
