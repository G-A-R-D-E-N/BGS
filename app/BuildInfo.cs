using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BehaviourStudio.App;

public static class BuildInfo
{
    private const string BuildCommitKey = "BuildCommit";

    public static string Version { get; } = ResolveVersion();
    public static string Build { get; } = ResolveBuild();
    public static string Runtime => RuntimeInformation.RuntimeIdentifier;
    public static string Report => $"Behaviour Graph Studio {Version} · build {Build} · {Runtime}";

    private static Assembly AppAssembly => typeof(BuildInfo).Assembly;

    private static string ResolveVersion() =>
        AppAssembly.GetName().Version?.ToString(3) ?? "unknown";

    private static string ResolveBuild()
    {
        string? commit = AppAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == BuildCommitKey)?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(commit)) return "local";
        return commit.Length > 12 ? commit[..12] : commit;
    }
}
