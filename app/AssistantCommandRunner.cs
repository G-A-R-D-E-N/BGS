using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BehaviourStudio.App;

public sealed record AssistantCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public static class AssistantCommandRunner
{
    internal static Func<AssistantCli, IReadOnlyList<string>, IReadOnlyDictionary<string, string>?,
        TimeSpan, CancellationToken, Task<AssistantCommandResult>> RunForTest = DefaultRunAsync;

    public static Task<AssistantCommandResult> RunAsync(
        AssistantCli cli, IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        RunForTest(cli, arguments, null, timeout, cancellationToken);

    public static Task<AssistantCommandResult> RunAsync(
        AssistantCli cli, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment, TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        RunForTest(cli, arguments, environment, timeout, cancellationToken);

    private static async Task<AssistantCommandResult> DefaultRunAsync(
        AssistantCli cli, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (environment is not null)
            foreach (KeyValuePair<string, string> pair in environment)
                info.Environment[pair.Key] = pair.Value;
        if (cli.NeedsShell)
        {
            string native = Path.Combine(Path.GetDirectoryName(cli.Path) ?? "",
                "node_modules", "@anthropic-ai", "claude-code", "bin", "claude.exe");
            if (!string.Equals(Path.GetFileNameWithoutExtension(cli.Path), "claude",
                    StringComparison.OrdinalIgnoreCase) || !File.Exists(native))
                return new AssistantCommandResult(-1, "",
                    "Select a native executable; shell scripts cannot safely receive assistant prompts.");
            info.FileName = native;
        }
        else
        {
            info.FileName = cli.Path;
        }
        foreach (string argument in arguments) info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start())
                return new AssistantCommandResult(-1, "", "The command could not be started.");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return new AssistantCommandResult(-1, "", "The command could not be started.");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new AssistantCommandResult(-1, "", "The command timed out.");
        }

        return new AssistantCommandResult(process.ExitCode,
            await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
        }
    }
}
