using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace BehaviourStudio.App;

public sealed class BgsMcpProtocol
{
    public const string DefaultProtocolVersion = "2025-06-18";
    public const string ServerName = "behaviour-graph-studio";

    internal static readonly string[] SupportedVersions =
    {
        "2024-11-05", "2025-03-26", "2025-06-18",
    };

    private const int MaximumResultCharacters = 4000;
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    private readonly AIFunction[] _tools;
    private readonly Dictionary<string, AIFunction> _byName;

    public BgsMcpProtocol(IReadOnlyList<AIFunction> tools)
    {
        _tools = tools?.ToArray() ?? Array.Empty<AIFunction>();
        _byName = new Dictionary<string, AIFunction>(StringComparer.Ordinal);
        foreach (AIFunction function in _tools)
            _byName[CodexProtocol.ToProtocolToolName(function.Name)] = function;
        ToolNames = _tools.Select(function => CodexProtocol.ToProtocolToolName(function.Name)).ToArray();
    }

    public int ToolCount => _tools.Length;

    public IReadOnlyList<string> ToolNames { get; }

    public Action<string>? OnToolInvoked { get; set; }

    public async Task<string> HandleAsync(string body, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Failure("null", -32700, "Parse error");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Failure("null", -32600, "Invalid Request");

            bool hasId = root.TryGetProperty("id", out JsonElement idElement) &&
                         idElement.ValueKind != JsonValueKind.Null;
            if (!hasId) return "";
            string id = idElement.GetRawText();

            string method = CodexProtocol.String(root, "method");
            if (method.Length == 0) return Failure(id, -32600, "Invalid Request");
            JsonElement? parameters = CodexProtocol.Property(root, "params");

            switch (method)
            {
                case "initialize":
                    return Success(id, new
                    {
                        protocolVersion = Negotiate(parameters),
                        capabilities = new { tools = new { listChanged = false } },
                        serverInfo = new { name = ServerName, version = ServerVersion() },
                    });
                case "ping":
                    return Success(id, new { });
                case "tools/list":
                    return Success(id, new { tools = ToolDefinitions() });
                case "tools/call":
                    return await CallAsync(id, parameters, cancellationToken).ConfigureAwait(false);
                default:
                    return Failure(id, -32601, "Method not found");
            }
        }
    }

    private async Task<string> CallAsync(
        string id, JsonElement? parameters, CancellationToken cancellationToken)
    {
        string name = CodexProtocol.String(parameters, "name");
        if (!_byName.TryGetValue(name, out AIFunction? function))
            return Failure(id, -32602, "Unknown tool: " + CodexProtocol.Scrub(name, 80));

        JsonElement? arguments = CodexProtocol.Property(parameters, "arguments");
        OnToolInvoked?.Invoke(function.Name);
        try
        {
            object? result = await function
                .InvokeAsync(new AIFunctionArguments(ParseArguments(arguments)), cancellationToken)
                .ConfigureAwait(false);
            string text = CodexProtocol.Scrub(JsonSerializer.Serialize(result, Wire), MaximumResultCharacters);
            return Success(id, Text(text, isError: false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Success(id, Text("The BGS tool rejected the arguments.", isError: true));
        }
    }

    private static object Text(string text, bool isError) => new
    {
        content = new[] { new { type = "text", text } },
        isError,
    };

    private List<Dictionary<string, object?>> ToolDefinitions()
    {
        var definitions = new List<Dictionary<string, object?>>(_tools.Length);
        foreach (AIFunction function in _tools)
        {
            string name = CodexProtocol.ToProtocolToolName(function.Name);
            if (!CodexProtocol.IsProtocolToolName(name)) continue;
            definitions.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["description"] = CodexProtocol.Scrub(function.Description, 400),
                ["inputSchema"] = function.JsonSchema,
            });
        }
        return definitions;
    }

    private static string Negotiate(JsonElement? parameters)
    {
        string requested = CodexProtocol.String(parameters, "protocolVersion");
        return Array.IndexOf(SupportedVersions, requested) >= 0 ? requested : DefaultProtocolVersion;
    }

    private static string ServerVersion() =>
        typeof(BgsMcpProtocol).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    internal static Dictionary<string, object?> ParseArguments(JsonElement? arguments)
    {
        var parsed = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (arguments is not { ValueKind: JsonValueKind.Object } value) return parsed;
        foreach (JsonProperty property in value.EnumerateObject())
            parsed[property.Name] = property.Value.Clone();
        return parsed;
    }

    private static string Success(string id, object result) =>
        "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":" +
        JsonSerializer.Serialize(result, Wire) + "}";

    private static string Failure(string id, int code, string message) =>
        "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":{\"code\":" + code +
        ",\"message\":" + JsonSerializer.Serialize(message, Wire) + "}}";
}
