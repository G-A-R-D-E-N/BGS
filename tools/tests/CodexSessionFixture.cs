using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BehaviourStudio.App;

namespace BehaviourStudio.Tests;

internal sealed class CodexSessionFixture : IDisposable
{
    private readonly string? _previousSettingsPath;
    private readonly Func<string, bool> _previousFileExists;
    private readonly string _settingsPath;

    public CodexSessionFixture(
        IEnumerable<Microsoft.Extensions.AI.AIFunction>? tools = null,
        Func<bool>? hasPendingApproval = null)
    {
        _settingsPath = Path.Combine(Path.GetTempPath(), $"bgs-codex-settings-{Guid.NewGuid():N}.cfg");
        _previousSettingsPath = Settings.SettingsPathForTest;
        _previousFileExists = CodexLocator.FileExistsForTest;
        Settings.SettingsPathForTest = _settingsPath;
        CodexLocator.FileExistsForTest = _ => true;

        Server = new CodexTestServer();
        Connection = new CodexAssistantConnection(
            new AssistantProviderOptions(AssistantProviderOptions.CodexBackend, "", "codex-test-path"),
            (_, _) => Server);
        Session = new CodexAssistantSession(
            Connection, tools ?? Array.Empty<Microsoft.Extensions.AI.AIFunction>(), hasPendingApproval);
        SettingsPath = _settingsPath;
    }

    public CodexTestServer Server { get; }
    public CodexAssistantConnection Connection { get; }
    public CodexAssistantSession Session { get; }
    public string SettingsPath { get; }

    public static AssistantContext Context() => new(
        "fixture-document", "0", "fixture.hkx", "Graph", "1", "hkbClipGenerator",
        false, false, 0, "", "");

    public static JsonElement Parse(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }

    public static string Method(string line) =>
        Parse(line).TryGetProperty("method", out JsonElement method) ? method.GetString() ?? "" : "";

    public static string ResultJson(string line) =>
        Parse(line).TryGetProperty("result", out JsonElement result) ? result.GetRawText() : "";

    public void Dispose()
    {
        Session.Dispose();
        Settings.SettingsPathForTest = _previousSettingsPath;
        CodexLocator.FileExistsForTest = _previousFileExists;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
