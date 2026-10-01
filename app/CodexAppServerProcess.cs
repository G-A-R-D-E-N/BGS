using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BehaviourStudio.App;

public sealed record CodexProcessExit(int? ExitCode, string Reason);

public interface ICodexTransport : IDisposable
{
    event Action<string>? LineReceived;
    event Action<CodexProcessExit>? Exited;

    bool IsRunning { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task SendLineAsync(string line, CancellationToken cancellationToken);
}

public sealed class CodexAppServerProcess : ICodexTransport
{
    private const int MaximumStderrExcerpt = 512;

    private readonly string _executablePath;
    private readonly IReadOnlyList<string> _arguments;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private Process? _process;
    private CodexLineReader? _reader;
    private StreamWriter? _stdin;
    private StringBuilder? _stderrExcerpt;
    private int _stderrLines;
    private int _exitRaised;
    private bool _disposed;

    public CodexAppServerProcess(string executablePath, IReadOnlyList<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("an executable path is required", nameof(executablePath));
        _executablePath = executablePath;
        _arguments = arguments ?? Array.Empty<string>();
    }

    public event Action<string>? LineReceived;
    public event Action<CodexProcessExit>? Exited;

    public bool IsRunning => _process is { HasExited: false };

    public int OversizedLineCount => _reader?.OversizedLineCount ?? 0;
    public int StderrLineCount => Volatile.Read(ref _stderrLines);

    internal string StderrExcerpt => _stderrExcerpt?.ToString() ?? "";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process != null) throw new InvalidOperationException("the Codex app server was already started");

        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in _arguments) startInfo.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("the Codex app server process did not start");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "the Codex app server could not be started: " + CodexProtocol.Scrub(exception.Message, 200),
                exception);
        }

        _process = process;
        _reader = new CodexLineReader(process.StandardOutput);
        _stdin = process.StandardInput;
        _stderrExcerpt = new StringBuilder();
        _ = ReadStandardOutputAsync(_lifetime.Token);
        _ = ReadStandardErrorAsync(_lifetime.Token);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(line) || line.Length > CodexProtocol.MaximumLineLength)
            throw new InvalidOperationException("the outgoing Codex message is not a valid frame");
        if (_stdin is null) throw new InvalidOperationException("the Codex app server is not running");

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stdin.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        try { _stdin?.Dispose(); } catch (IOException) { }
        try
        {
            if (_process is { HasExited: false }) _process.Kill();
            _process?.WaitForExit(2000);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
        }
        _process?.Dispose();
        _writeGate.Dispose();
        _lifetime.Dispose();
    }

    private async Task ReadStandardOutputAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await _reader!.ReadLineAsync(
                    CodexProtocol.MaximumLineLength, cancellationToken).ConfigureAwait(false);
                if (line is null) break;
                if (line.Length == 0) continue;
                LineReceived?.Invoke(line);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or IOException)
        {
        }
        RaiseExit("the Codex app server output stream ended");
    }

    private async Task ReadStandardErrorAsync(CancellationToken cancellationToken)
    {
        if (_process is null) return;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await _process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;
                Interlocked.Increment(ref _stderrLines);
                StringBuilder excerpt = _stderrExcerpt!;
                if (excerpt.Length < MaximumStderrExcerpt)
                    excerpt.Append(CodexProtocol.Scrub(line, MaximumStderrExcerpt));
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or IOException)
        {
        }
    }

    private void RaiseExit(string reason)
    {
        if (Interlocked.Exchange(ref _exitRaised, 1) != 0) return;
        int? exitCode = null;
        try { if (_process is { HasExited: true }) exitCode = _process.ExitCode; }
        catch (InvalidOperationException) { }
        Exited?.Invoke(new CodexProcessExit(exitCode, reason));
    }
}
