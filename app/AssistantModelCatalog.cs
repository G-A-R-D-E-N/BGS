using System;
using System.Collections.Generic;
using System.Linq;

namespace BehaviourStudio.App;

public static class AssistantModelCatalog
{
    public const string CodexAgent = "Codex";
    public const string ClaudeAgent = "Claude";
    public const string OpencodeAgent = "opencode";
    public const string GeminiAgent = "Gemini";
    public const string LocalAgent = "Local";

    public static readonly IReadOnlyList<string> CanonicalAgents =
        new[] { CodexAgent, ClaudeAgent, OpencodeAgent, GeminiAgent, LocalAgent };

    public const string IdentitySeparator = "::";

    public static string AgentOf(CodexModel model) =>
        model.Agent.Length > 0
            ? model.Agent
            : model.Group.Length > 0 ? model.Group : "Models";

    public static string Identity(CodexModel model) =>
        AgentOf(model) + IdentitySeparator + model.WireModel;

    public static string AgentFor(AssistantBackend backend) => backend switch
    {
        AssistantBackend.Codex => CodexAgent,
        AssistantBackend.Claude => ClaudeAgent,
        AssistantBackend.Opencode => OpencodeAgent,
        AssistantBackend.Gemini => GeminiAgent,
        _ => LocalAgent,
    };

    public static IReadOnlyList<CodexModel> Merge(IEnumerable<IReadOnlyList<CodexModel>> sources)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<CodexModel>();
        foreach (IReadOnlyList<CodexModel> source in sources)
        {
            if (source is null) continue;
            foreach (CodexModel model in source)
            {
                if (!seen.Add(AgentOf(model) + "\u0000" + model.WireModel)) continue;
                merged.Add(model);
            }
        }
        return merged.AsReadOnly();
    }

    public static IReadOnlyList<string> AgentsInOrder(IEnumerable<CodexModel> models)
    {
        var present = new HashSet<string>(models.Select(AgentOf), StringComparer.Ordinal);
        var ordered = new List<string>(present.Count);
        foreach (string agent in CanonicalAgents)
            if (present.Remove(agent)) ordered.Add(agent);
        ordered.AddRange(present.OrderBy(agent => agent, StringComparer.Ordinal));
        return ordered.AsReadOnly();
    }

    public static string SectionTitle(string agent, string group)
    {
        string resolvedAgent = agent.Length > 0 ? agent : "Models";
        if (group.Length == 0 || string.Equals(group, resolvedAgent, StringComparison.OrdinalIgnoreCase))
            return resolvedAgent;
        return resolvedAgent + " \u00B7 " + group;
    }
}
