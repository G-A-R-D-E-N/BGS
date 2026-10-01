using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

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
                string? executable = ResolveExecutable(new AssistantCli(explicitPath, "configured"));
                if (executable is not null)
                {
                    cli = new AssistantCli(executable, "configured");
                    return true;
                }
                error = $"The configured {name} shim has no supported native executable.";
                return false;
            }
            error = $"The configured {name} path was not found.";
            return false;
        }

        foreach (string candidate in CandidatePathsForTest(backend))
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (!FileExistsForTest(candidate)) continue;
            string? executable = ResolveExecutable(new AssistantCli(candidate, "detected"));
            if (executable is null) continue;
            cli = new AssistantCli(executable, "detected");
            return true;
        }

        error = $"{name} was not found. Install it and reopen the assistant.";
        return false;
    }

    public static bool TryLocateFromSettings(
        AssistantBackend backend, out AssistantCli? cli, out string error) =>
        TryLocate(backend, Settings.Get(SettingsKey(backend)), out cli, out error);

    internal static string? ResolveExecutable(AssistantCli cli)
    {
        if (!cli.NeedsShell) return cli.Path;
        if (!OperatingSystem.IsWindows()) return null;
        string modules = Path.Combine(Path.GetDirectoryName(cli.Path) ?? "", "node_modules");
        string name = Path.GetFileNameWithoutExtension(cli.Path);
        if (name.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            string native = Path.Combine(modules, "@anthropic-ai", "claude-code", "bin", "claude.exe");
            return FileExistsForTest(native) ? native : null;
        }
        if (!name.Equals("opencode", StringComparison.OrdinalIgnoreCase)) return null;
        string package = "opencode-windows-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        string[] packages = RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? Avx2.IsSupported ? new[] { package, package + "-baseline" } : new[] { package + "-baseline", package }
            : new[] { package };
        foreach (string parent in new[] { Path.Combine(modules, "opencode-ai", "node_modules"), modules })
            foreach (string candidate in packages)
            {
                string native = Path.Combine(parent, candidate, "bin", "opencode.exe");
                if (FileExistsForTest(native)) return native;
            }
        return null;
    }

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
