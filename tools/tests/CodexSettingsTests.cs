using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Microsoft.Extensions.AI;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class CodexSettingsTests
{
    private static readonly string[] CredentialMarkers =
    {
        "api_key", "apikey", "api-key", "token", "secret", "bearer", "password",
        "base_url", "authorization", "cookie", "credential", "auth.json",
    };

    [Fact]
    public async Task AssistantSessionAndSignOutPersistNoCredentialMaterial()
    {
        var tool = AIFunctionFactory.Create(() => new { status = "ok" }, "bgs.inspect_behavior", "test");
        using var fixture = new CodexSessionFixture(new[] { tool });

        await fixture.Session.SendAsync("Check it.", CodexSessionFixture.Context());
        await fixture.Connection.SignOutAsync();
        Settings.Set("assistant.model", "test-model");

        string text = File.Exists(fixture.SettingsPath) ? File.ReadAllText(fixture.SettingsPath) : "";
        Assert.Contains("assistant.model=test-model", text, StringComparison.Ordinal);
        foreach (string marker in CredentialMarkers)
            Assert.DoesNotContain(marker, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProviderOptionsNeverExposeAnEndpointOrCredentialVariable()
    {
        string? previousPath = Settings.SettingsPathForTest;
        Settings.SettingsPathForTest =
            Path.Combine(Path.GetTempPath(), "bgs-provider-" + Guid.NewGuid().ToString("N") + ".cfg");
        try
        {
            AssistantProviderOptions options = AssistantProviderOptions.FromSettings();

            Assert.Equal("codex", options.Backend);
            Assert.DoesNotContain("http", options.Model, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("BGS_AI_API_KEY", options.CodexExecutableOverride, StringComparison.Ordinal);
        }
        finally
        {
            Settings.SettingsPathForTest = previousPath;
        }
    }

    [Fact]
    public void CodexOverridePathIsPreferredOverPathDiscovery()
    {
        Func<string, bool> previous = CodexLocator.FileExistsForTest;
        Func<System.Collections.Generic.IReadOnlyList<string>> previousCandidates =
            CodexLocator.CandidatePathsForTest;
        try
        {
            CodexLocator.FileExistsForTest = path =>
                string.Equals(path, "configured-codex", StringComparison.Ordinal) ||
                string.Equals(path, "path-codex", StringComparison.Ordinal);
            CodexLocator.CandidatePathsForTest = () => new[] { "path-codex" };

            Assert.True(CodexLocator.TryLocate("configured-codex", out CodexExecutable? executable, out _));
            Assert.Equal("configured-codex", executable!.Path);
            Assert.Equal("configured", executable.Source);

            Assert.True(CodexLocator.TryLocate("", out CodexExecutable? discovered, out _));
            Assert.Equal("path-codex", discovered!.Path);
            Assert.Equal("detected", discovered.Source);
        }
        finally
        {
            CodexLocator.FileExistsForTest = previous;
            CodexLocator.CandidatePathsForTest = previousCandidates;
        }
    }

    [Fact]
    public void WindowsNpmShimResolvesToNativeCodexWithoutShellExecution()
    {
        string npmBin = Path.Combine("C:", "Users", "someone", "AppData", "Roaming", "npm");
        string shim = Path.Combine(npmBin, "codex.cmd");
        System.Collections.Generic.IReadOnlyList<string> nativeCandidates =
            CodexLocator.NpmNativeCandidates(npmBin, RuntimeInformation.ProcessArchitecture);
        Assert.NotEmpty(nativeCandidates);
        string native = nativeCandidates[0];

        Func<string, bool> previous = CodexLocator.FileExistsForTest;
        try
        {
            CodexLocator.FileExistsForTest = path =>
                string.Equals(path, shim, StringComparison.Ordinal) ||
                string.Equals(path, native, StringComparison.Ordinal);

            Assert.True(CodexLocator.TryLocate(shim, out CodexExecutable? executable, out string error), error);
            Assert.Equal(native, executable!.Path);
            Assert.Equal("configured-npm", executable.Source);
            Assert.EndsWith("codex.exe", executable.Path, StringComparison.OrdinalIgnoreCase);
            Assert.False(executable.Path.EndsWith("codex.cmd", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            CodexLocator.FileExistsForTest = previous;
        }
    }

    [Fact]
    public void NpmNativeLayoutMatchesOfficialPlatformPackageShape()
    {
        string npmBin = Path.Combine("root", "npm");
        string x64 = CodexLocator.NpmNativeCandidates(npmBin, Architecture.X64)[0];
        string arm64 = CodexLocator.NpmNativeCandidates(npmBin, Architecture.Arm64)[0];

        Assert.Contains("codex-win32-x64", x64, StringComparison.Ordinal);
        Assert.Contains("x86_64-pc-windows-msvc", x64, StringComparison.Ordinal);
        Assert.Contains("codex-win32-arm64", arm64, StringComparison.Ordinal);
        Assert.Contains("aarch64-pc-windows-msvc", arm64, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("bin", "codex.exe"), x64, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("bin", "codex.exe"), arm64, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingCodexExecutableYieldsABoundedUserFacingError()
    {
        Func<string, bool> previous = CodexLocator.FileExistsForTest;
        Func<System.Collections.Generic.IReadOnlyList<string>> previousCandidates =
            CodexLocator.CandidatePathsForTest;
        try
        {
            CodexLocator.FileExistsForTest = _ => false;
            CodexLocator.CandidatePathsForTest = () => new[] { "nope" };

            Assert.False(CodexLocator.TryLocate("", out CodexExecutable? executable, out string error));
            Assert.Null(executable);
            Assert.Contains("Codex CLI was not found", error, StringComparison.Ordinal);
            Assert.True(error.Length < 200);
        }
        finally
        {
            CodexLocator.FileExistsForTest = previous;
            CodexLocator.CandidatePathsForTest = previousCandidates;
        }
    }
}
