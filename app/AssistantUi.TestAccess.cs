using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace BehaviourStudio.App;

internal sealed partial class AssistantUi
{
    internal AssistantUi AssistantUiForTest => this;

    internal void SetSessionForTest(IAssistantSession session, AssistantTools? tools = null)
    {
        if (tools != null)
        {
            _tools = tools;
            _gate = null;
        }
        AssistantTools resolved = EnsureTools();
        AssistantConversationEntry? entry = _controller?.Active;
        if (entry is null) return;
        entry.Session?.Dispose();
        entry.Session = session;
        entry.InvalidateSession();
        entry.SessionPrimed = false;
        entry.HasPendingApproval = () => resolved.HasPendingApproval;
    }

    internal IReadOnlyList<AssistantChatSummary> ChatListForTest =>
        _controller?.List.Summaries() ?? Array.Empty<AssistantChatSummary>();

    internal string? ActiveChatIdForTest => _controller?.List.ActiveChatId;
    internal IAssistantSession? ActiveSessionForTest => _controller?.ActiveSession;

    internal bool SidebarVisibleForTest => _sidebar.IsVisible;
    internal int SidebarColumnIndexForTest => Grid.GetColumn(_sidebar);
    internal int SplitterColumnIndexForTest => Grid.GetColumn(_splitter);
    internal int PaneColumnIndexForTest => Grid.GetColumn(_pane);
    internal double SidebarColumnWidthForTest => _sidebarColumn.Width.Value;
    internal double SplitterColumnWidthForTest => _splitterColumn.Width.Value;
    internal double PaneColumnWidthForTest => _drawerColumn.Width.Value;

    internal void ApproveForTest() => Approve();
    internal void RejectForTest() => Reject();
    internal void ClearPendingMutationsForTest() => _controller?.RejectPendingMutations();
    internal bool RealPendingMutationForTest => EnsureTools().HasPendingApproval;
    internal void SetConnectionForTest(CodexAssistantConnection connection) => _connection = connection;
    internal void SelectModelForTest(CodexModel model) => SelectCatalogModel(model);
    internal void ChooseApiKeyForTest(string key) => ChooseApiKey(key);
    internal void ChooseBaseUrlForTest(string url) => ChooseBaseUrl(url);
    internal void SignOutForTest() => SignOut();
    internal void RefreshAccountForTest() => RefreshAccountText();

    internal void DeleteChatForTest(string chatId) => Delete(chatId);

    internal void SelectChatForTest(string chatId)
    {
        if (_controller is null) return;
        if (_controller.List.IsBusy) return;
        if (_controller.TrySelectById(chatId)) OnSelectionChanged(_controller.List.ActiveIndex);
    }
}
