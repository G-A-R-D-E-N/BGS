using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BehaviourStudio.App;

public enum AssistantBackend
{
    Codex,
    Claude,
    Opencode,
    Gemini,
    Local,
}

public sealed record AssistantCli(string Path, string Source)
{
    public bool NeedsShell =>
        Path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
        Path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
        Path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);
}

public static class AssistantCliLocator
{
    internal static readonly AssistantBackend[] All =
    {
        AssistantBackend.Codex, AssistantBackend.Claude, AssistantBackend.Opencode,
        AssistantBackend.Gemini, AssistantBackend.Local,
    };

    internal static Func<AssistantBackend, IReadOnlyList<string>> CandidatePathsForTest =
        DefaultCandidatePaths;
    internal static Func<string, bool> FileExistsForTest = File.Exists;
    internal static Func<bool> IsWindowsForTest = () => OperatingSystem.IsWindows();

    public static string CommandName(AssistantBackend backend) => backend switch
    {
        AssistantBackend.Codex => "codex",
        AssistantBackend.Claude => "claude",
        AssistantBackend.Opencode => "opencode",
        AssistantBackend.Gemini => "gemini",
        AssistantBackend.Local => "local",
        _ => "",
    };

    public static string DisplayName(AssistantBackend backend) => backend switch
    {
        AssistantBackend.Codex => "ChatGPT via Codex",
        AssistantBackend.Claude => "Claude Code",
        AssistantBackend.Opencode => "opencode",
        AssistantBackend.Gemini => "Google Gemini (API key)",
        AssistantBackend.Local => "Local models (OpenAI-compatible)",
        _ => "",
    };

    public static string SettingsKey(AssistantBackend backend) => backend switch
    {
        AssistantBackend.Codex => "assistant.codex_path",
        AssistantBackend.Claude => "assistant.claude_path",
        AssistantBackend.Opencode => "assistant.opencode_path",
        _ => "",
    };

    public static AssistantBackend? Parse(string value)
    {
        foreach (AssistantBackend backend in All)
            if (string.Equals(value, CommandName(backend), StringComparison.OrdinalIgnoreCase))
                return backend;
        return null;
    }

    public static AssistantBackend FromSettings() =>
        Parse(Settings.Get("assistant.backend")) ?? AssistantBackend.Codex;

    public static bool TryLocate(
        AssistantBackend backend, string configured, out AssistantCli? cli, out string error)
    {
        cli = null;
        error = "";
        if (ApiProviders.IsApi(backend))
        {
            error = "This provider uses an API endpoint, not a command-line tool.";
            return false;
        }
        string name = CommandName(backend);
        if (name.Length == 0)
        {
            error = "The assistant provider is not supported.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(configured))
        {
            string explicitPath = configured.Trim();
            if (FileExistsForTest(explicitPath))
            {
                cli = new AssistantCli(explicitPath, "configured");
                return true;
            }
            error = $"The configured {name} path was not found.";
            return false;
        }

        foreach (string candidate in CandidatePathsForTest(backend))
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (!FileExistsForTest(candidate)) continue;
            cli = new AssistantCli(candidate, "detected");
            return true;
        }

        error = $"{name} was not found. Install it and reopen the assistant.";
        return false;
    }

    public static bool TryLocateFromSettings(
        AssistantBackend backend, out AssistantCli? cli, out string error) =>
        TryLocate(backend, Settings.Get(SettingsKey(backend)), out cli, out error);

    internal static IReadOnlyList<string> ExecutableNames(string name) =>
        IsWindowsForTest()
            ? new[] { name + ".exe", name + ".cmd", name + ".bat", name }
            : new[] { name };

    private static IReadOnlyList<string> DefaultCandidatePaths(AssistantBackend backend)
    {
        if (ApiProviders.IsApi(backend)) return Array.Empty<string>();
        string name = CommandName(backend);
        var candidates = new List<string>();

        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVariable))
        {
            foreach (string directory in pathVariable.Split(System.IO.Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                foreach (string executable in ExecutableNames(name))
                    candidates.Add(System.IO.Path.Combine(directory.Trim(), executable));
            }
        }

        AddHomePaths(candidates, name);
        AddStandardPaths(candidates, name);
        return candidates;
    }

    private static void AddHomePaths(List<string> candidates, string name)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length == 0) return;
        foreach (string directory in new[] { ".local/bin", ".bun/bin", "bin" })
            foreach (string executable in ExecutableNames(name))
                candidates.Add(System.IO.Path.Combine(home, directory.Replace('/', System.IO.Path.DirectorySeparatorChar), executable));
    }

    private static void AddStandardPaths(List<string> candidates, string name)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (localAppData.Length > 0)
            foreach (string executable in ExecutableNames(name))
            {
                candidates.Add(System.IO.Path.Combine(localAppData, "Programs", executable));
                candidates.Add(System.IO.Path.Combine(localAppData, "Microsoft", "WinGet", "Links", executable));
            }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (appData.Length > 0)
        {
            foreach (string executable in ExecutableNames(name))
                candidates.Add(System.IO.Path.Combine(appData, "npm", executable));
        }

        foreach (string executable in ExecutableNames(name))
        {
            candidates.Add(System.IO.Path.Combine("/usr/local/bin", executable));
            candidates.Add(System.IO.Path.Combine("/opt/homebrew/bin", executable));
        }
    }
}
