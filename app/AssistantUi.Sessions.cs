using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace BehaviourStudio.App;

internal sealed partial class AssistantUi
{
    private void InitializeProvider()
    {
        _backend = AssistantCliLocator.FromSettings();
        RefreshProviderStatus();
        RefreshAccountText();
        RefreshModelSelector();
        _ = RefreshProviderModelsAsync();
    }


    private IAssistantSession? CreateSessionForChat(string chatId)
    {
        AssistantTools tools = EnsureTools();
        if (_backend == AssistantBackend.Codex)
        {
            if (!EnsureCodex()) return null;
            return new CodexAssistantSession(_connection!, tools.Functions, () => tools.HasPendingApproval);
        }
        if (ApiProviders.IsApi(_backend)) return CreateApiSession(tools);
        if (!EnsureCli()) return null;
        BgsMcpBridge bridge = EnsureBridge(tools);
        string model = Settings.Get(ModelSettingKey(_backend));
        return _backend == AssistantBackend.Claude
            ? new ClaudeAssistantSession(_cli!, bridge, model, () => tools.HasPendingApproval)
            : new OpencodeAssistantSession(_cli!, bridge, model, () => tools.HasPendingApproval);
    }

    private bool EnsureCli()
    {
        if (_cli is not null) return true;
        if (!AssistantCliLocator.TryLocateFromSettings(_backend, out AssistantCli? cli, out string failure))
        {
            _pane.SetStatus(failure, Ux.WarnBrush);
            return false;
        }
        _cli = cli;
        return true;
    }

    private BgsMcpBridge EnsureBridge(AssistantTools tools)
    {
        if (_bridge is not null) return _bridge;
        _bridge = BgsMcpBridge.Start(tools.Functions);
        BgsMcpBridge bridge = _bridge;
        _owner.Closed += (_, _) => bridge.Dispose();
        return _bridge;
    }

    private bool EnsureCodex()
    {
        if (_connection != null) return true;
        AssistantProviderOptions options = AssistantProviderOptions.FromSettings();
        if (!AssistantProvider.TryResolveExecutable(options, out _, out string error))
        {
            _pane.SetStatus(error, Ux.WarnBrush);
            return false;
        }
        _connection = _owner.CreateCodexConnection(options);
        _connection.Changed += OnConnectionChanged;
        return true;
    }

    private void OnConnectionChanged() => Dispatcher.UIThread.Post(RefreshAccountText);

    internal static string PlanLabel(string planType) =>
        planType.Length == 0
            ? ""
            : char.ToUpperInvariant(planType[0]) + planType[1..].ToLowerInvariant();

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
