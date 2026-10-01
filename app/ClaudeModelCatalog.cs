using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BehaviourStudio.App;

public static class ClaudeModelCatalog
{
    public const string CacheFolder = "model-catalog";
    public const string SettingsKey = "assistant.model.claude";

    internal static Func<string> ConfigRootForTest = DefaultConfigRoot;
    internal static Func<string, IReadOnlyList<string>> FilesForTest = DefaultFiles;
    internal static Func<string, string?> ReadFileForTest = DefaultRead;

    public static string DefaultConfigRoot() =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } configured
            ? configured
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } home
                ? Path.Combine(home, ".claude")
                : "";

    public static IReadOnlyList<CodexModel> Read()
    {
        string root = ConfigRootForTest();
        if (root.Length == 0) return Array.Empty<CodexModel>();
        string folder = Path.Combine(root, "cache", CacheFolder);
        IReadOnlyList<string> files = FilesForTest(folder);
        if (files.Count == 0) return Array.Empty<CodexModel>();

        string newest = files[0];
        string? content = ReadFileForTest(newest);
        return content is { Length: > 0 } text ? Parse(text) : Array.Empty<CodexModel>();
    }

    public static IReadOnlyList<CodexModel> Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return Array.Empty<CodexModel>();
        }

        using (document)
        {
            JsonElement? models = CodexProtocol.Property(
                CodexProtocol.Property(CodexProtocol.Property(document.RootElement, "catalog"), "config"),
                "models");
            if (models is not { ValueKind: JsonValueKind.Array } list) return Array.Empty<CodexModel>();

            var found = new List<CodexModel>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entry in list.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                string id = CodexProtocol.Scrub(CodexProtocol.String(entry, "id"), 120);
                if (id.Length == 0 || !seen.Add(id)) continue;
                string name = CodexProtocol.Scrub(CodexProtocol.String(entry, "name"), 120);
                found.Add(new CodexModel(id, name.Length > 0 ? name : id, false, id)
                {
                    Group = AssistantModelCatalog.ClaudeAgent,
                    Agent = AssistantModelCatalog.ClaudeAgent,
                });
            }
            return found.AsReadOnly();
        }
    }

    private static IReadOnlyList<string> DefaultFiles(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return Array.Empty<string>();
            var files = new List<string>(Directory.GetFiles(folder, "*.json"));
            files.Sort(StringComparer.Ordinal);
            files.Reverse();
            return files;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static string? DefaultRead(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
