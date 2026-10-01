using System.IO;
using System.Linq;
using System.Text.Json;
using BehaviourStudio.App;

namespace BehaviourStudio.Tests;

internal static class OpencodeStubConfig
{
    internal static readonly string[] Advertised =
    {
        "bgs_inspect_behavior", "bgs_resolve_project_chain", "bgs_check_project",
        "bgs_search_project", "bgs_inspect_animation", "bgs_inspect_object",
        "bgs_set_clip_animation",
    };

    internal static string Resolved(string configPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
        JsonElement root = document.RootElement;
        return "{\"permission\":" + root.GetProperty("permission").GetRawText() +
            ",\"agent\":{\"bgs\":{\"mode\":\"primary\",\"permission\":" +
            root.GetProperty("agent").GetProperty("bgs").GetProperty("permission").GetRawText() +
            "}},\"mcp\":" + root.GetProperty("mcp").GetRawText() + "}";
    }

    internal static string AgentPermissionForTest()
    {
        using JsonDocument document = JsonDocument.Parse(
            OpencodeCli.BuildConfigJson("http://127.0.0.1:9/mcp", System.Array.Empty<string>(), Advertised));
        return document.RootElement.GetProperty("agent").GetProperty("bgs")
            .GetProperty("permission").GetRawText();
    }

    internal static string TopLevelPermissionForTest()
    {
        using JsonDocument document = JsonDocument.Parse(
            OpencodeCli.BuildConfigJson("http://127.0.0.1:9/mcp", System.Array.Empty<string>(), Advertised));
        return document.RootElement.GetProperty("permission").GetRawText();
    }
}
