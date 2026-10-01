using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace BehaviourStudio.App;

public sealed record AssistantProviderOptions(string Backend, string Model, string CodexExecutableOverride)
{
    public const string CodexBackend = "codex";

    public static AssistantProviderOptions FromSettings() => new(
        Settings.Get("assistant.backend") is { Length: > 0 } backend ? backend : CodexBackend,
        Settings.Get("assistant.model"),
        Settings.Get("assistant.codex_path"));

    public bool IsCodex => string.Equals(Backend, CodexBackend, StringComparison.Ordinal);
}

public sealed record CodexExecutable(string Path, string Source);

public static class AssistantProvider
{
    public const string DisplayName = "ChatGPT via Codex";

    public static bool TryResolveExecutable(
        AssistantProviderOptions options, out CodexExecutable? executable, out string error) =>
        CodexLocator.TryLocate(options?.CodexExecutableOverride ?? "", out executable, out error);

    public static string Describe(AssistantProviderOptions options, CodexExecutable? executable) =>
        executable is null
            ? $"{DisplayName} · Codex CLI not detected"
            : $"{DisplayName} · Codex CLI detected";
}

public static class CodexLocator
{
    internal static Func<IReadOnlyList<string>> CandidatePathsForTest = DefaultCandidatePaths;
    internal static Func<string, bool> FileExistsForTest = File.Exists;

    public static bool TryLocate(string configured, out CodexExecutable? executable, out string error)
    {
        executable = null;
        error = "";

        if (!string.IsNullOrWhiteSpace(configured))
        {
            string explicitPath = configured.Trim();
            if (IsWindowsCommandShim(explicitPath) && FileExistsForTest(explicitPath))
            {
                foreach (string candidate in NpmNativeCandidates(
                    Path.GetDirectoryName(explicitPath) ?? "", RuntimeInformation.ProcessArchitecture))
                {
                    if (!FileExistsForTest(candidate)) continue;
                    executable = new CodexExecutable(candidate, "configured-npm");
                    return true;
                }
                error = "The configured npm Codex launcher was found, but its native Codex executable was not.";
                return false;
            }
            if (FileExistsForTest(explicitPath))
            {
                executable = new CodexExecutable(explicitPath, "configured");
                return true;
            }
            error = "The configured Codex executable path was not found.";
            return false;
        }

        foreach (string candidate in CandidatePathsForTest())
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (!FileExistsForTest(candidate)) continue;
            executable = new CodexExecutable(candidate, "detected");
            return true;
        }

        error = "Codex CLI was not found. Install Codex CLI, sign in with ChatGPT, then reopen the assistant.";
        return false;
    }

    internal static IReadOnlyList<string> NpmNativeCandidates(string npmBinDirectory, Architecture architecture)
    {
        if (string.IsNullOrWhiteSpace(npmBinDirectory)) return Array.Empty<string>();
        (string package, string target) = architecture switch
        {
            Architecture.X64 => ("codex-win32-x64", "x86_64-pc-windows-msvc"),
            Architecture.Arm64 => ("codex-win32-arm64", "aarch64-pc-windows-msvc"),
            _ => ("", ""),
        };
        if (package.Length == 0) return Array.Empty<string>();

        string modules = Path.Combine(npmBinDirectory, "node_modules", "@openai");
        string native = Path.Combine("vendor", target, "bin", "codex.exe");
        return new[]
        {
            Path.Combine(modules, package, native),
            Path.Combine(modules, "codex", "node_modules", "@openai", package, native),
            Path.Combine(modules, "codex", native),
        };
    }

    private static bool IsWindowsCommandShim(string path) =>
        path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> DefaultCandidatePaths()
    {
        var candidates = new List<string>();
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVariable))
        {
            foreach (string directory in pathVariable.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                candidates.Add(Path.Combine(directory.Trim(), "codex.exe"));
                candidates.Add(Path.Combine(directory.Trim(), "codex"));
            }
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (localAppData.Length > 0)
            candidates.Add(Path.Combine(localAppData, "Programs", "OpenAI", "Codex", "bin", "codex.exe"));

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (appData.Length > 0)
        {
            string npmBin = Path.Combine(appData, "npm");
            candidates.AddRange(NpmNativeCandidates(npmBin, RuntimeInformation.ProcessArchitecture));
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0)
        {
            candidates.Add(Path.Combine(home, ".local", "bin", "codex"));
            candidates.Add(Path.Combine(home, ".codex", "bin", "codex"));
        }

        candidates.Add("/usr/local/bin/codex");
        candidates.Add("/opt/homebrew/bin/codex");

        return candidates.Distinct(StringComparer.Ordinal).ToArray();
    }
}
