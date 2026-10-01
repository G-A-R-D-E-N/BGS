using System;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class AssistantCliLocatorTests
{
    [Fact]
    public void UnsupportedWindowsShimIsNotAdvertisedAsUsable()
    {
        if (!OperatingSystem.IsWindows()) return;
        string shim = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            Guid.NewGuid().ToString("N"), "opencode.cmd");
        var previousExists = AssistantCliLocator.FileExistsForTest;
        try
        {
            AssistantCliLocator.FileExistsForTest = path => path == shim;
            Assert.False(AssistantCliLocator.TryLocate(AssistantBackend.Opencode, shim, out _, out string error));
            Assert.Contains("native executable", error, StringComparison.Ordinal);
        }
        finally { AssistantCliLocator.FileExistsForTest = previousExists; }
    }

    [Fact]
    public void LocatingABackendAsksOnlyForThatBackendsCandidates()
    {
        var previousCandidates = AssistantCliLocator.CandidatePathsForTest;
        Func<string, bool> previousExists = AssistantCliLocator.FileExistsForTest;
        try
        {
            AssistantBackend? requested = null;
            AssistantCliLocator.CandidatePathsForTest = backend =>
            {
                requested = backend;
                return new[] { "tools/" + AssistantCliLocator.CommandName(backend) };
            };
            AssistantCliLocator.FileExistsForTest = _ => true;

            Assert.True(AssistantCliLocator.TryLocate(
                AssistantBackend.Opencode, "", out AssistantCli? cli, out _));
            Assert.Equal(AssistantBackend.Opencode, requested);
            Assert.Equal("tools/opencode", cli!.Path);
            Assert.Equal("detected", cli.Source);

            Assert.True(AssistantCliLocator.TryLocate(
                AssistantBackend.Claude, "", out AssistantCli? claude, out _));
            Assert.Equal("tools/claude", claude!.Path);
        }
        finally
        {
            AssistantCliLocator.CandidatePathsForTest = previousCandidates;
            AssistantCliLocator.FileExistsForTest = previousExists;
        }
    }

    [Fact]
    public void AConfiguredPathWinsAndAMissingConfiguredPathIsRefused()
    {
        Func<string, bool> previousExists = AssistantCliLocator.FileExistsForTest;
        try
        {
            AssistantCliLocator.FileExistsForTest = path => path == "custom/claude";

            Assert.True(AssistantCliLocator.TryLocate(
                AssistantBackend.Claude, "custom/claude", out AssistantCli? cli, out _));
            Assert.Equal("custom/claude", cli!.Path);
            Assert.Equal("configured", cli.Source);

            Assert.False(AssistantCliLocator.TryLocate(
                AssistantBackend.Claude, "missing/claude", out _, out string error));
            Assert.Contains("was not found", error, StringComparison.Ordinal);
        }
        finally
        {
            AssistantCliLocator.FileExistsForTest = previousExists;
        }
    }

    [Fact]
    public void AMissingCliExplainsWhatToInstallRatherThanGuessingAPath()
    {
        var previousCandidates = AssistantCliLocator.CandidatePathsForTest;
        Func<string, bool> previousExists = AssistantCliLocator.FileExistsForTest;
        try
        {
            AssistantCliLocator.CandidatePathsForTest = _ => Array.Empty<string>();
            AssistantCliLocator.FileExistsForTest = _ => false;

            Assert.False(AssistantCliLocator.TryLocate(
                AssistantBackend.Opencode, "", out _, out string error));
            Assert.Contains("opencode", error, StringComparison.Ordinal);
            Assert.Contains("Install", error, StringComparison.Ordinal);
        }
        finally
        {
            AssistantCliLocator.CandidatePathsForTest = previousCandidates;
            AssistantCliLocator.FileExistsForTest = previousExists;
        }
    }

    [Theory]
    [InlineData("codex", AssistantBackend.Codex)]
    [InlineData("claude", AssistantBackend.Claude)]
    [InlineData("OPENCODE", AssistantBackend.Opencode)]
    public void BackendNamesRoundTrip(string value, AssistantBackend expected) =>
        Assert.Equal(expected, AssistantCliLocator.Parse(value));

    [Fact]
    public void AnUnknownBackendIsRefusedInsteadOfDefaulting()
    {
        Assert.Null(AssistantCliLocator.Parse("not-a-provider"));
        Assert.Null(AssistantCliLocator.Parse(""));
    }

    [Fact]
    public void WindowsShimsAreRecognisedAndOtherPlatformsUseABareName()
    {
        Func<bool> previousWindows = AssistantCliLocator.IsWindowsForTest;
        try
        {
            AssistantCliLocator.IsWindowsForTest = () => true;
            Assert.Contains("claude.cmd", AssistantCliLocator.ExecutableNames("claude"));
            Assert.True(new AssistantCli("claude.cmd", "detected").NeedsShell);
            Assert.True(new AssistantCli("claude.ps1", "detected").NeedsShell);
            Assert.False(new AssistantCli("claude.exe", "detected").NeedsShell);

            AssistantCliLocator.IsWindowsForTest = () => false;
            Assert.Equal(new[] { "claude" }, AssistantCliLocator.ExecutableNames("claude"));
            Assert.False(new AssistantCli("/usr/local/bin/claude", "detected").NeedsShell);
        }
        finally
        {
            AssistantCliLocator.IsWindowsForTest = previousWindows;
        }
    }

    [Fact]
    public void EveryBackendHasADistinctSettingsKeyAndDisplayName()
    {
        var keys = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (AssistantBackend backend in AssistantCliLocator.All)
        {
            Assert.NotEmpty(AssistantCliLocator.DisplayName(backend));
            Assert.NotEmpty(AssistantCliLocator.CommandName(backend));
            Assert.Equal(backend, AssistantCliLocator.Parse(AssistantCliLocator.CommandName(backend)));
            if (ApiProviders.IsApi(backend)) continue;
            Assert.True(keys.Add(AssistantCliLocator.SettingsKey(backend)));
        }
    }
}
