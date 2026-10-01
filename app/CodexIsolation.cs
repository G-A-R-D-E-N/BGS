using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BehaviourStudio.App;

internal static class CodexIsolation
{
    private const int MaximumMcpServers = 128;
    private const int MaximumMcpNameLength = 256;
    private const string VerificationError =
        "Codex configuration could not be verified for safe assistant isolation.";

    internal static async Task<IReadOnlyList<string>> ReadMcpServerNamesAsync(
        CodexAppServerClient client, string cwd, CancellationToken cancellationToken)
    {
        JsonElement? result;
        try
        {
            result = await client.RequestAsync(
                CodexMethods.ConfigRead,
                new { includeLayers = false, cwd },
                cancellationToken).ConfigureAwait(false);
        }
        catch (CodexProtocolException)
        {
            throw new CodexProtocolException(VerificationError);
        }

        JsonElement? config = CodexProtocol.Property(result, "config");
        if (config is not { } value || value.ValueKind != JsonValueKind.Object)
            throw new CodexProtocolException(VerificationError);

        if (!value.TryGetProperty("mcp_servers", out JsonElement servers))
            return Array.Empty<string>();
        if (servers.ValueKind != JsonValueKind.Object)
            throw new CodexProtocolException(VerificationError);

        var names = new List<string>();
        foreach (JsonProperty server in servers.EnumerateObject())
        {
            if (server.Name.Length is 0 or > MaximumMcpNameLength || names.Count >= MaximumMcpServers)
                throw new CodexProtocolException(VerificationError);
            names.Add(server.Name);
        }
        return names;
    }

    internal static Dictionary<string, object?> BuildThreadConfig(IReadOnlyList<string> mcpServerNames)
    {
        var mcpServers = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (string name in mcpServerNames)
            mcpServers[name] = new { enabled = false };

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["features.apps"] = false,
            ["features.code_mode"] = false,
            ["features.code_mode_only"] = false,
            ["features.context_management"] = false,
            ["features.current_time_reminder"] = false,
            ["features.deferred_executor"] = false,
            ["features.enable_fanout"] = false,
            ["features.goals"] = false,
            ["features.hooks"] = false,
            ["features.image_generation"] = false,
            ["features.memories"] = false,
            ["features.multi_agent"] = false,
            ["features.multi_agent_v2"] = false,
            ["features.plugins"] = false,
            ["features.request_permissions_tool"] = false,
            ["features.shell_snapshot"] = false,
            ["features.shell_tool"] = false,
            ["features.standalone_web_search"] = false,
            ["features.token_budget"] = false,
            ["features.tool_suggest"] = false,
            ["features.unified_exec"] = false,
            ["features.view_image"] = false,
            ["orchestrator.skills.enabled"] = false,
            ["skills.include_instructions"] = false,
            ["token_budget.use_history_notes_extension"] = false,
            ["tools.experimental_request_user_input.enabled"] = false,
            ["tools.update_plan.enabled"] = false,
            ["web_search"] = "disabled",
            ["mcp_servers"] = mcpServers,
        };
    }

    internal static void VerifyThreadStart(JsonElement? result)
    {
        string provider = CodexProtocol.String(result, "modelProvider");
        string approval = CodexProtocol.String(result, "approvalPolicy");
        JsonElement? sandbox = CodexProtocol.Property(result, "sandbox");
        string sandboxType = CodexProtocol.String(sandbox, "type");

        if (!string.Equals(provider, "openai", StringComparison.Ordinal) ||
            !string.Equals(approval, "never", StringComparison.Ordinal) ||
            !string.Equals(sandboxType, "readOnly", StringComparison.Ordinal))
            throw new CodexProtocolException(VerificationError);

        JsonElement? workspaceRoots = CodexProtocol.Property(result, "runtimeWorkspaceRoots");
        if (workspaceRoots is not { } roots ||
            (roots.ValueKind != JsonValueKind.Array || roots.GetArrayLength() != 0))
            throw new CodexProtocolException(VerificationError);
    }
}
