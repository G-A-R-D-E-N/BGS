using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class AssistantCommandRunnerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExcessiveOutputIsDrainedAndRefused(bool standardError)
    {
        string stream = standardError ? "Error" : "Out";
        AssistantCommandResult result = await AssistantCommandRunner.RunAsync(
            new AssistantCli("pwsh", "configured"),
            new[] { "-NoProfile", "-NonInteractive", "-Command",
                $"[Console]::{stream}.Write('x' * {AssistantCommandRunner.MaxOutputCharacters + 65536})" },
            TimeSpan.FromSeconds(15));
        Assert.Equal("The command output exceeded the safe limit.", result.StandardError);
        Assert.Empty(result.StandardOutput);
    }

    [Fact]
    public async Task InternalTimeoutStillReturnsBoundedFailure()
    {
        AssistantCommandResult result = await AssistantCommandRunner.RunAsync(
            new AssistantCli("dotnet", "configured"),
            new[] { Path.Combine(AppContext.BaseDirectory, "BehaviourGraph.Mcp.dll"), "--root", AppContext.BaseDirectory },
            TimeSpan.FromMilliseconds(200));
        Assert.Equal("The command timed out.", result.StandardError);
        Assert.Empty(result.StandardOutput);
    }

    [Fact]
    public async Task CancellationDuringProcessWaitIsNotATimeout()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AssistantCommandRunner.RunAsync(
            new AssistantCli("dotnet", "configured"),
            new[] { Path.Combine(AppContext.BaseDirectory, "BehaviourGraph.Mcp.dll"), "--root", AppContext.BaseDirectory },
            TimeSpan.FromSeconds(10), cancellation.Token));
    }

    [Fact]
    public async Task AlreadyCancelledCommandDoesNotLaunchOrBecomeATimeout()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AssistantCommandRunner.RunAsync(
            new AssistantCli("missing-cli", "configured"), Array.Empty<string>(),
            TimeSpan.FromSeconds(5), cancellation.Token));
    }

    [Fact]
    public async Task NpmOpencodeShimResolvesItsNativePackageWithoutRunningTheShell()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgs-opencode-" + Guid.NewGuid().ToString("N"));
        string package = Path.Combine(root, "node_modules", "opencode-windows-x64", "bin");
        Directory.CreateDirectory(package);
        try
        {
            string shim = Path.Combine(root, "opencode.cmd");
            File.WriteAllText(shim, "@echo off\r\necho SHOULD_NOT_RUN\r\n");
            File.WriteAllText(Path.Combine(package, "opencode.exe"), "not an executable");
            AssistantCommandResult result = await AssistantCommandRunner.RunAsync(
                new AssistantCli(shim, "configured"), Array.Empty<string>(), TimeSpan.FromSeconds(5));
            Assert.Equal("The command could not be started.", result.StandardError);
            Assert.Empty(result.StandardOutput);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MissingExecutableReturnsBoundedFailure()
    {
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe");
        AssistantCommandResult result = await AssistantCommandRunner.RunAsync(
            new AssistantCli(missing, "configured"), Array.Empty<string>(), TimeSpan.FromSeconds(5));
        Assert.False(result.Succeeded);
        Assert.Empty(result.StandardOutput);
        Assert.Equal("The command could not be started.", result.StandardError);
    }

    [Fact]
    public async Task ShellShimsCannotInterpretPromptTextAsCommands()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgs-shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string shim = Path.Combine(root, "custom.cmd");
            File.WriteAllText(shim, "@echo off\r\necho %*\r\n");

            var result = await AssistantCommandRunner.RunAsync(new AssistantCli(shim, "configured"),
                new[] { "\" & echo BGS_UNAUTHORISED & rem \"" }, TimeSpan.FromSeconds(5));

            Assert.False(result.Succeeded);
            Assert.Empty(result.StandardOutput);
            Assert.Contains("native executable", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
