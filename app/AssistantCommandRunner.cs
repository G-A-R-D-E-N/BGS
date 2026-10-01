using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BehaviourStudio.App;

public sealed record AssistantCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public static class AssistantCommandRunner
{
    internal const int MaxOutputCharacters = 1024 * 1024;
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
        cancellationToken.ThrowIfCancellationRequested();
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
        string? executable = AssistantCliLocator.ResolveExecutable(cli);
        if (executable is null)
            return new AssistantCommandResult(-1, "",
                "Select a native executable; shell scripts cannot safely receive assistant prompts.");
        info.FileName = executable;
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
        Task<(string Text, bool Exceeded)> output = ReadOutputAsync(process.StandardOutput, timeoutSource.Token);
        Task<(string Text, bool Exceeded)> error = ReadOutputAsync(process.StandardError, timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            var streams = await Task.WhenAll(output, error).ConfigureAwait(false);
            if (streams[0].Exceeded || streams[1].Exceeded)
                return new AssistantCommandResult(-1, "", "The command output exceeded the safe limit.");
            return new AssistantCommandResult(process.ExitCode, streams[0].Text, streams[1].Text);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
            return new AssistantCommandResult(-1, "", "The command timed out.");
        }
    }

    internal static async Task<(string Text, bool Exceeded)> ReadOutputAsync(
        StreamReader reader, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        bool exceeded = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            int retained = Math.Min(count, MaxOutputCharacters - text.Length);
            text.Append(buffer, 0, retained);
            exceeded |= retained < count;
        }
        return (text.ToString(), exceeded);
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
