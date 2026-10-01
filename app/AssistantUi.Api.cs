using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace BehaviourStudio.App;

internal sealed partial class AssistantUi
{
    private void ChooseApiKey(string key)
    {
        if (_controller?.List.IsBusy == true)
        {
            _pane.SetStatus("Wait for the current request to finish before changing the API key.", Ux.WarnBrush);
            return;
        }
        _apiKeys[_backend] = (key ?? "").Trim();
        _controller?.EndAllSessions();
        _pane.SetApproval(false);
        RefreshProviderStatus();
        RefreshAccountText();
        _ = RefreshProviderModelsAsync();
        _ = RefreshCatalogAsync();
    }

    private void ChooseBaseUrl(string url)
    {
        if (_controller?.List.IsBusy == true)
        {
            _pane.SetStatus("Wait for the current request to finish before changing the API endpoint.", Ux.WarnBrush);
            return;
        }
        string value = (url ?? "").Trim();
        if (!Settings.TrySet(ApiProviders.BaseUrlSetting(_backend), value, out string failure))
        {
            _pane.SetStatus(failure, Ux.WarnBrush);
            return;
        }
        _controller?.EndAllSessions();
        _pane.SetApproval(false);
        RefreshProviderStatus();
        _ = RefreshProviderModelsAsync();
        _ = RefreshCatalogAsync();
    }


    internal string ApiKeyForTest => KeyFor(_backend);
    internal void SetApiKeyForTest(string key) => _apiKeys[_backend] = (key ?? "").Trim();

    private string KeyFor(AssistantBackend backend) =>
        _apiKeys.TryGetValue(backend, out string? key) ? key : "";

    private string ApiKeyFor(AssistantBackend backend)
    {
        string key = KeyFor(backend);
        if (key.Length == 0) return "";
        return ApiProviders.TryBaseUrl(backend, out Uri? endpoint, out _) &&
               ApiProviders.AllowsCredentials(endpoint!)
            ? key
            : "";
    }

    private AssistantSession? CreateApiSession(AssistantTools tools)
    {
        if (!ApiProviders.TryBaseUrl(_backend, out Uri? endpoint, out string failure))
        {
            _pane.SetStatus(failure, Ux.WarnBrush);
            return null;
        }
        string model = Settings.Get(ModelSettingKey(_backend));
        if (model.Length == 0)
        {
            _pane.SetStatus("Choose a model for this provider first.", Ux.WarnBrush);
            return null;
        }
        IChatClient client = ApiChatClients.Create(endpoint!, ApiKeyFor(_backend), model);
        return new AssistantSession(client, tools.Functions, () => tools.HasPendingApproval);
    }


    private async Task RefreshProviderModelsAsync()
    {
        if (_backend == AssistantBackend.Codex)
        {
            if (_connection is null || !_connection.IsConnected) return;
            _providerModels = _connection.Models;
            RefreshModelSelector();
            return;
        }
        if (ApiProviders.IsApi(_backend))
        {
            await RefreshApiModelsAsync().ConfigureAwait(true);
            return;
        }
        AssistantBackend requested = _backend;
        IReadOnlyList<CodexModel> models = _backend == AssistantBackend.Claude
            ? await Task.Run(ClaudeModelCatalog.Read).ConfigureAwait(true)
            : await ReadOpencodeModelsAsync().ConfigureAwait(true);
        if (_backend != requested) return;
        _providerModels = models;
        RefreshModelSelector();
    }

    private async Task<IReadOnlyList<CodexModel>> ReadOpencodeModelsAsync()
    {
        if (!EnsureCli()) return Array.Empty<CodexModel>();
        return await OpencodeModelCatalog.ReadAsync(_cli!).ConfigureAwait(true);
    }

    internal static string ApiAgent(AssistantBackend backend) => backend == AssistantBackend.Gemini
        ? AssistantModelCatalog.GeminiAgent
        : AssistantModelCatalog.LocalAgent;

    internal static IReadOnlyList<CodexModel> ApiModels(AssistantBackend backend, IReadOnlyList<string> ids)
    {
        string agent = ApiAgent(backend);
        return ids.Select(id => new CodexModel(id, id, false, id) { Agent = agent, Group = agent }).ToList();
    }

    private async Task RefreshApiModelsAsync()
    {
        AssistantBackend requested = _backend;
        if (!ApiProviders.TryBaseUrl(_backend, out _, out string failure))
        {
            _providerModels = Array.Empty<CodexModel>();
            _pane.SetStatus(failure, Ux.WarnBrush);
            RefreshModelSelector();
            return;
        }
        ApiModelList result = await ApiModelCatalog
            .ReadAsync(ApiProviders.BaseUrl(_backend), ApiKeyFor(_backend)).ConfigureAwait(true);
        if (_backend != requested) return;
        if (result.Failed)
        {
            _providerModels = Array.Empty<CodexModel>();
            _pane.SetStatus(result.Error, Ux.WarnBrush);
            RefreshModelSelector();
            return;
        }
        _providerModels = ApiModels(_backend, result.ModelIds);
        RefreshModelSelector();
    }


    private readonly Dictionary<AssistantBackend, string> _apiKeys = new();
}
