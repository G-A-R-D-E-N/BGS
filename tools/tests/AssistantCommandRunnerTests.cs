using System;
using System.IO;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class AssistantCommandRunnerTests
{
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
