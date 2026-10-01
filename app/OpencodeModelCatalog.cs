using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BehaviourStudio.App;

public static class OpencodeModelCatalog
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static async Task<IReadOnlyList<CodexModel>> ReadAsync(
        AssistantCli cli, CancellationToken cancellationToken = default)
    {
        AssistantCommandResult detailed = await AssistantCommandRunner
            .RunAsync(cli, new[] { "models", "--verbose" }, Timeout, cancellationToken).ConfigureAwait(false);
        if (detailed.Succeeded)
        {
            IReadOnlyList<CodexModel> models = ParseVerbose(detailed.StandardOutput);
            if (models.Count > 0) return models;
        }

        AssistantCommandResult plain = await AssistantCommandRunner
            .RunAsync(cli, new[] { "models" }, Timeout, cancellationToken).ConfigureAwait(false);
        return plain.Succeeded ? Parse(plain.StandardOutput) : Array.Empty<CodexModel>();
    }

    public static IReadOnlyList<CodexModel> ParseVerbose(string output)
    {
        var models = new List<CodexModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var json = new StringBuilder();
        string? header = null;

        void Flush()
        {
            string id = header ?? "";
            header = null;
            string text = json.ToString().Trim();
            json.Clear();
            if (id.Length == 0 || text.Length == 0 || !seen.Add(id)) return;
            models.Add(Build(id, text));
        }

        foreach (string raw in output.Split('\n'))
        {
            string trimmed = raw.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed[0] is '{' or '}' or '"' or '[' or ']')
            {
                if (header is not null) json.AppendLine(raw.TrimEnd());
                continue;
            }
            if (IsModelId(trimmed))
            {
                Flush();
                header = trimmed;
                continue;
            }
            if (header is not null) json.AppendLine(raw.TrimEnd());
        }
        Flush();
        return models.AsReadOnly();
    }

    private static CodexModel Build(string id, string json)
    {
        string name = "";
        string group = id.Contains('/') ? id[..id.IndexOf('/')] : "";
        long context = 0;
        bool reasoning = false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            name = CodexProtocol.Scrub(CodexProtocol.String(root, "name"), 120);
            string provider = CodexProtocol.Scrub(CodexProtocol.String(root, "providerID"), 60);
            if (provider.Length > 0) group = provider;
            if (CodexProtocol.Property(root, "limit") is { } limit &&
                limit.TryGetProperty("context", out JsonElement contextValue) &&
                contextValue.ValueKind == JsonValueKind.Number && contextValue.TryGetInt64(out long parsed))
                context = parsed;
            reasoning = CodexProtocol.Bool(CodexProtocol.Property(root, "capabilities"), "reasoning");
        }
        catch (JsonException)
        {
        }

        return new CodexModel(id, name.Length > 0 ? name : id, false, id)
        {
            Group = group,
            Agent = AssistantModelCatalog.OpencodeAgent,
            ContextLimit = context,
            Reasoning = reasoning,
        };
    }

    public static IReadOnlyList<CodexModel> Parse(string output)
    {
        var models = new List<CodexModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in output.Split('\n'))
        {
            string id = raw.Trim().TrimEnd('\r');
            if (!IsModelId(id) || !seen.Add(id)) continue;
            models.Add(new CodexModel(id, id, false, id)
            {
                Agent = AssistantModelCatalog.OpencodeAgent,
                Group = id[..id.IndexOf('/')],
            });
        }
        return models.AsReadOnly();
    }

    internal static bool IsModelId(string value)
    {
        if (value.Length == 0 || value.Length > 200) return false;
        int separator = value.IndexOf('/');
        if (separator <= 0 || separator != value.LastIndexOf('/')) return false;
        if (separator == value.Length - 1) return false;
        foreach (char character in value)
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character)) return false;
        }
        return true;
    }
}
