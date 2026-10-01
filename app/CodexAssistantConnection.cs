using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BehaviourStudio.App;

public delegate ICodexTransport CodexTransportFactory(
    string executablePath, IReadOnlyList<string> arguments);

public sealed class CodexAssistantConnection : IDisposable
{
    private readonly AssistantProviderOptions _options;
    private readonly CodexTransportFactory _transportFactory;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly object _transportGate = new();
    private ICodexTransport? _process;
    private CodexAppServerClient? _client;
    private CodexAccountClient? _accounts;
    private CodexAccount _account = CodexAccount.SignedOut;
    private IReadOnlyList<CodexModel> _models = Array.Empty<CodexModel>();
    private string _selectedModel;
    private int _activeRequests;
    private int _disposed;

    public IReadOnlyList<string> LaunchArguments { get; private set; } = Array.Empty<string>();

    public CodexAssistantConnection(
        AssistantProviderOptions options, CodexTransportFactory? transportFactory = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _selectedModel = options.Model;
        _transportFactory = transportFactory ?? DefaultTransportFactory;
    }

    private static ICodexTransport DefaultTransportFactory(
        string executablePath, IReadOnlyList<string> arguments) =>
        new CodexAppServerProcess(executablePath, arguments);

    public event Action? Changed;

    public CodexAppServerClient Client =>
        _client ?? throw new InvalidOperationException("the Codex app server is not connected");

    public CodexServerInfo? ServerInfo => _client?.ServerInfo;
    public CodexAccount Account => _account;
    public IReadOnlyList<CodexModel> Models { get { lock (_transportGate) return _models; } }
    public string SelectedModel { get { lock (_transportGate) return _selectedModel; } }
    public string ModelLabel
    {
        get
        {
            lock (_transportGate)
                return _selectedModel.Length == 0
                    ? "Codex config default"
                    : _models.FirstOrDefault(m => m.WireModel == _selectedModel)?.DisplayName ?? _selectedModel;
        }
    }

    private CodexModel? DefaultModel() => _models.FirstOrDefault(m => m.IsDefault) ?? _models.FirstOrDefault();

    public bool TrySelectModel(string model, out string failure)
    {
        lock (_transportGate)
        {
            failure = "";
            if (IsDisposed || !IsConnected || !_account.SignedIn)
                failure = "Connect with ChatGPT before selecting a model.";
            else if (_activeRequests > 0)
                failure = "Wait for the current request to finish before switching models.";
            else if (model.Length > 0 && !_models.Any(m => m.WireModel == model))
                failure = "That model is not available in the current Codex catalog.";
            else if (model.Length == 0 && DefaultModel() is null)
                failure = "The Codex default is unavailable. Reconnect before resetting the model.";
            if (failure.Length > 0) return false;
            if (!Settings.TrySet("assistant.model", model, out failure)) return false;
            _selectedModel = model;
        }
        Changed?.Invoke();
        return true;
    }

    internal string BeginModelRequest()
    {
        lock (_transportGate)
        {
            ThrowIfDisposed();
            string model = _selectedModel;
            if (model.Length > 0 && _models.Count > 0 && !_models.Any(m => m.WireModel == model))
                throw new CodexProtocolException("The selected model is unavailable. Choose a model or reset to default.");
            _activeRequests++;
            return model;
        }
    }

    internal void EndModelRequest()
    {
        lock (_transportGate) _activeRequests--;
    }
    public bool IsConnected => _client?.IsConnected == true;
    public bool? DynamicToolsAvailable
    {
        get => _client?.DynamicToolsAvailable;
        set { if (_client != null) _client.DynamicToolsAvailable = value; }
    }

    public async Task<CodexAccount> ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_client is { IsConnected: true }) return _account;

            ResetTransport();
            ThrowIfDisposed();
            if (!AssistantProvider.TryResolveExecutable(_options, out CodexExecutable? executable, out string error))
                throw new CodexProtocolException(error);

            var arguments = new List<string>
            {
                "app-server", "--listen", "stdio://",
                "-c", "model_provider=\"openai\"",
                "-c", "mcp_servers={}",
                "-c", "features.shell_tool=false",
                "-c", "features.unified_exec=false",
                "-c", "web_search=\"disabled\"",
            };
            LaunchArguments = arguments;
            ICodexTransport process = _transportFactory(executable!.Path, arguments);
            var client = new CodexAppServerClient(process);
            client.Notification += OnNotification;
            lock (_transportGate)
            {
                if (IsDisposed)
                {
                    client.Notification -= OnNotification;
                    client.Dispose();
                    ThrowIfDisposed();
                }
                _process = process;
                _client = client;
            }

            try
            {
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                ThrowIfDisposed();
                var accounts = new CodexAccountClient(client);
                CodexAccount account = await ReadAccountAsync(accounts, cancellationToken).ConfigureAwait(false);
                IReadOnlyList<CodexModel> models = await ReadModelsAsync(accounts, cancellationToken).ConfigureAwait(false);
                lock (_transportGate)
                {
                    ThrowIfDisposed();
                    if (!ReferenceEquals(_client, client))
                        throw new CodexProtocolException("The Codex app server connection changed during startup.");
                    _accounts = accounts;
                    _account = account;
                    _models = models;
                }
                Changed?.Invoke();
                return account;
            }
            catch
            {
                ResetTransport();
                ThrowIfDisposed();
                throw;
            }
        }
        finally
        {
            _connectGate.Release();
        }
    }

    public async Task<CodexAccount> RefreshAccountAsync(CancellationToken cancellationToken = default)
    {
        CodexAccountClient? accounts;
        lock (_transportGate) accounts = _accounts;
        if (accounts is null) return CodexAccount.SignedOut;

        CodexAccount account = await ReadAccountAsync(accounts, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<CodexModel> models = await ReadModelsAsync(accounts, cancellationToken).ConfigureAwait(false);
        lock (_transportGate)
        {
            if (IsDisposed || !ReferenceEquals(_accounts, accounts)) return CodexAccount.SignedOut;
            _account = account;
            _models = models;
        }
        Changed?.Invoke();
        return account;
    }

    public async Task<CodexLoginChallenge> BeginChatGptLoginAsync(CancellationToken cancellationToken = default)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);
        CodexAccountClient accounts = AccountsOrThrow();
        return await accounts.StartChatGptLoginAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CodexLoginChallenge> BeginDeviceCodeLoginAsync(CancellationToken cancellationToken = default)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);
        CodexAccountClient accounts = AccountsOrThrow();
        return await accounts.StartDeviceCodeLoginAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> CancelLoginAsync(string loginId, CancellationToken cancellationToken = default)
    {
        CodexAccountClient? accounts;
        lock (_transportGate) accounts = _accounts;
        if (accounts is null || IsDisposed) return false;
        return await accounts.CancelLoginAsync(loginId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        CodexAccountClient? accounts;
        lock (_transportGate) accounts = _accounts;
        if (accounts is null || IsDisposed) return;
        await accounts.LogoutAsync(cancellationToken).ConfigureAwait(false);
        lock (_transportGate)
        {
            if (IsDisposed || !ReferenceEquals(_accounts, accounts)) return;
            _account = CodexAccount.SignedOut;
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        ResetTransport();
    }

    private void OnNotification(string method, JsonElement? parameters)
    {
        if (IsDisposed) return;
        if (method == CodexMethods.AccountUpdated)
        {
            string authMode = CodexProtocol.String(parameters, "authMode");
            bool chatGpt = string.Equals(authMode, "chatgpt", StringComparison.OrdinalIgnoreCase);
            string planType = chatGpt
                ? CodexProtocol.Scrub(CodexProtocol.String(parameters, "planType"), 32)
                : "";
            lock (_transportGate)
            {
                if (IsDisposed) return;
                _account = authMode.Length == 0
                    ? CodexAccount.SignedOut
                    : new CodexAccount(authMode, planType, chatGpt);
            }
            Changed?.Invoke();
            return;
        }

        if (method == CodexMethods.AccountLoginCompleted)
        {
            if (CodexProtocol.Bool(parameters, "success"))
            {
                _ = RefreshAccountAsync();
            }
            else
            {
                lock (_transportGate)
                {
                    if (IsDisposed) return;
                    _account = CodexAccount.SignedOut;
                }
                Changed?.Invoke();
            }
        }
    }

    private CodexAccountClient AccountsOrThrow()
    {
        lock (_transportGate)
        {
            ThrowIfDisposed();
            return _accounts ?? throw new InvalidOperationException("the Codex account client is unavailable");
        }
    }

    private static async Task<CodexAccount> ReadAccountAsync(
        CodexAccountClient accounts, CancellationToken cancellationToken)
    {
        try
        {
            return await accounts.ReadAccountAsync(false, cancellationToken).ConfigureAwait(false);
        }
        catch (CodexProtocolException)
        {
            return CodexAccount.SignedOut;
        }
    }

    private static async Task<IReadOnlyList<CodexModel>> ReadModelsAsync(
        CodexAccountClient accounts, CancellationToken cancellationToken)
    {
        try
        {
            return await accounts.ReadModelsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (CodexProtocolException)
        {
            return Array.Empty<CodexModel>();
        }
    }

    private void ResetTransport()
    {
        CodexAppServerClient? client;
        ICodexTransport? process;
        lock (_transportGate)
        {
            client = _client;
            process = _process;
            _client = null;
            _process = null;
            _accounts = null;
            _account = CodexAccount.SignedOut;
            _models = Array.Empty<CodexModel>();
        }
        if (client != null)
        {
            client.Notification -= OnNotification;
            client.Dispose();
        }
        else
        {
            process?.Dispose();
        }
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);
}