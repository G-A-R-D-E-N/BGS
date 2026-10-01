using System;
using System.Globalization;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.App;

internal sealed partial class AssistantUi
{
    private const double DefaultWidth = 760;
    private const double MinimumWidth = 520;
    private const double MaximumWidth = 1120;
    private const double SidebarWidth = 220;
    private const double SidebarMinWidth = 160;
    private const double SidebarMaxWidth = 280;
    private const string WidthSetting = "assistant.drawer_width";
    private const string ActiveChatSetting = "assistant.active_chat_id";
    private const string QuickStartSetting = "assistant.quick_start_done";

    private readonly MainWindow _owner;
    private readonly AssistantPane _pane = new();
    private readonly AssistantConversationSidebar _sidebar = new();
    private readonly ColumnDefinition _splitterColumn = new(new GridLength(0, GridUnitType.Pixel));
    private readonly ColumnDefinition _sidebarColumn =
        new(new GridLength(SidebarWidth, GridUnitType.Pixel)) { MinWidth = SidebarMinWidth, MaxWidth = SidebarMaxWidth };
    private readonly ColumnDefinition _drawerColumn =
        new(new GridLength(0, GridUnitType.Pixel)) { MinWidth = 0, MaxWidth = MaximumWidth };
    private readonly GridSplitter _splitter;
    private AssistantTools? _tools;
    private AssistantMutationGate? _gate;
    private AssistantConversationController? _controller;
    private CancellationTokenSource? _requestCancellation;
    private bool _open;
    private AssistantApprovalWindow? _approvalWindow;
    private string _displayedApprovalId = "";

    public AssistantUi(MainWindow owner, Grid root, EditorShell shell)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        root.ColumnDefinitions.Add(_sidebarColumn);
        root.ColumnDefinitions.Add(_splitterColumn);
        root.ColumnDefinitions.Add(_drawerColumn);
        _splitter = new GridSplitter
        {
            Width = 6,
            Background = Avalonia.Media.Brushes.Transparent,
            IsVisible = false,
        };
        Grid.SetColumn(_sidebar, 1);
        Grid.SetColumn(_splitter, 2);
        Grid.SetColumn(_pane, 3);
        root.Children.Add(_sidebar);
        root.Children.Add(_splitter);
        root.Children.Add(_pane);
        _sidebar.IsVisible = false;

        shell.ChatRequested += OpenAsChat;
        shell.SettingsRequested += OpenSettings;

        RestoreChats();

        _sidebar.NewChatRequested += NewChatInternal;
        _sidebar.ConversationSelected += Select;
        _sidebar.RenameCommitted += RenameCommitted;
        _sidebar.DeleteRequested += Delete;

        _pane.NewChatRequested += NewChatInternal;
        _pane.CloseRequested += () => SetOpen(false);
        _pane.CancelRequested += Cancel;
        _pane.SendRequested += Send;
        _pane.ApproveRequested += Approve;
        _pane.RejectRequested += Reject;

        _pane.ModelSelected += SelectCatalogModel;
        _pane.ModelResetRequested += ResetModel;
        _pane.ModelControlOpened += () => _ = RefreshCatalogAsync();
        _pane.QueuedRemoved += index =>
        {
            if (index < 0 || index >= _queue.Count) return;
            _queue.RemoveAt(index);
            _pane.ShowQueue(QueueTexts());
        };
        _pane.CommandSource = () => AssistantCommands.All;
        _pane.QuickStartDismissed += DismissQuickStart;

        _pane.SetStatus("Pick a chat or click + New chat to start.", Ux.MetaBrush);
        InitializeProvider();
        SetOpen(false);
    }

    public bool IsOpen => _open;
    public bool IsBusy => _pane.IsBusy;
    public int MessageCount => _pane.MessageCount;
    public string ProviderStatus => _settingsState.Description;
    public string AccountStatus => _settingsState.Account;
    public string NoticeText => _settingsState.CodexManaged
        ? AssistantSettingsWindow.CodexNotice
        : AssistantSettingsWindow.CliNotice;
    public string Status => _pane.Status;
    public bool ApprovalVisible => _pane.ApprovalVisible;
    public bool SignInVisible => _settingsState.CodexManaged && !_settingsState.SignedIn;
    public AssistantPane Pane => _pane;

    private void RestoreChats()
    {
        IReadOnlyList<AssistantChatSummary> summaries = AssistantChatStore.All();
        var loaded = new List<AssistantChat>(summaries.Count);
        foreach (AssistantChatSummary summary in summaries)
        {
            AssistantChat? chat = AssistantChatStore.Load(summary.Id);
            if (chat != null) loaded.Add(chat);
        }
        loaded = loaded.OrderByDescending(c => c.UpdatedUtc).Take(AssistantChatStore.MaxConversations).ToList();

        string? activeId = Settings.Get(ActiveChatSetting);

        var sessionFactory = new Func<string, IAssistantSession?>(chatId => CreateSessionForChat(chatId));
        _controller = new AssistantConversationController(sessionFactory, loaded, activeId);
        EnsureTools();
        _controller.Mutations = _gate;
        _controller.List.StructureChanged += RefreshSidebar;
        _controller.List.SelectionChanged += OnSelectionChanged;
        RefreshSidebar();
        OnSelectionChanged(_controller.List.ActiveIndex);
    }

    private AssistantTools EnsureTools()
    {
        AssistantTools tools = _tools ?? (_tools = _owner.CreateAssistantTools());
        if (_gate is null)
        {
            AssistantTools captured = tools;
            _gate = new AssistantMutationGate(
                () => captured.HasPendingApproval,
                captured.RejectPendingClipAnimation,
                () => captured.ApprovePendingAction(_displayedApprovalId));
        }
        if (_controller is not null) _controller.Mutations = _gate;
        return tools;
    }

    private void RefreshSidebar()
    {
        if (_controller is null) return;
        IReadOnlyList<AssistantChatSummary> summaries = _controller.List.Summaries();
        _sidebar.Bind(summaries, _controller.List.ActiveChatId);
    }

    private void OnSelectionChanged(int index)
    {
        if (_controller is null) return;
        AssistantConversationEntry? entry = _controller.Active;
        _pane.Clear();
        if (entry is not null)
        {
            _pane.ChatTitle = entry.Chat.Title;
            foreach (AssistantChatMessage message in entry.Chat.Messages)
            {
                switch (message.Role)
                {
                    case AssistantChatRole.User:
                        _pane.AddUser(message.Text);
                        break;
                    case AssistantChatRole.Assistant:
                        _pane.AddAssistant(message.Text);
                        break;
                    case AssistantChatRole.Tool:
                        _pane.AddTool(
                            message.ToolName ?? "tool",
                            false,
                            message.ToolStatus ?? "");
                        break;
                }
            }
            _pane.SetApproval(entry.ApprovalActive);
            _displayedApprovalId = _tools?.PendingApprovalId ?? "";
            _pane.SetApprovalDescription(_tools?.PendingDescription ?? "");
            if (entry.ApprovalActive && _approvalWindow is null)
            {
                Window? target = AssistantEditor.ActiveDialog(_owner);
                if (target is not null)
                {
                    string displayedId = _displayedApprovalId;
                    _approvalWindow = new AssistantApprovalWindow(_tools?.PendingDescription ?? "Review the pending edit.", () => Approve(displayedId), Reject);
                    _approvalWindow.Closed += (_, _) => _approvalWindow = null;
                    _approvalWindow.Show(target);
                }
            }
        }
        else
        {
            _pane.ChatTitle = "(no chat selected)";
        }
        RefreshSidebar();
        SaveActiveChatId();
    }

    private void SaveActiveChatId()
    {
        if (_controller is null) return;
        Settings.TrySet(ActiveChatSetting, _controller.List.ActiveChatId ?? "", out _);
    }

    private void SetOpen(bool open)
    {
        if (open)
        {
            double stored = ReadStoredWidth();
            _drawerColumn.Width = new GridLength(stored, GridUnitType.Pixel);
            _sidebarColumn.MinWidth = SidebarMinWidth;
            _sidebarColumn.MaxWidth = SidebarMaxWidth;
            _sidebarColumn.Width = new GridLength(SidebarWidth, GridUnitType.Pixel);
        }
        else
        {
            if (_open) StoreWidth(_drawerColumn.Width.Value);
            _drawerColumn.Width = new GridLength(0, GridUnitType.Pixel);
            _sidebarColumn.Width = new GridLength(0, GridUnitType.Pixel);
            _sidebarColumn.MinWidth = 0;
            _sidebarColumn.MaxWidth = 0;
        }
        _open = open;
        _splitterColumn.Width = new GridLength(open ? 6 : 0, GridUnitType.Pixel);
        _pane.IsVisible = open;
        _splitter.IsVisible = open;
        _sidebar.IsVisible = open;
    }

    private void OpenAsChat()
    {
        SetOpen(true);
        if (Settings.Get(QuickStartSetting).Length == 0) _pane.ShowQuickStart();
        _pane.SetStatus("Ready. Type below to send to the selected chat.", Ux.MetaBrush);
        Dispatcher.UIThread.Post(() => _pane.Composer.Focus());
    }

    private void DismissQuickStart()
    {
        _pane.HideQuickStart();
        Settings.TrySet(QuickStartSetting, "1", out _);
    }

    private void NewChatInternal()
    {
        if (_controller is null) return;
        if (_controller.List.IsBusy)
        {
            _pane.SetStatus("Wait for the current request to finish before starting a new chat.", Ux.MetaBrush);
            return;
        }
        AssistantConversationEntry? entry = _controller.NewChat();
        if (entry is null)
        {
            _pane.SetStatus("BGS kept your existing chats (max reached). Try renaming instead.", Ux.MetaBrush);
            return;
        }
        RefreshSidebar();
        OnSelectionChanged(_controller.List.ActiveIndex);
        _queue.Clear();
        _pane.ShowQueue(QueueTexts());
        if (AssistantChatStore.Exists(entry.Chat.Id))
            _pane.SetStatus("Started a new chat. Type below to send.", Ux.MetaBrush);
        else
            _pane.SetStatus("Started a new chat, but BGS could not save it to disk yet.", Ux.WarnBrush);
    }

    private void Select(string chatId)
    {
        if (_controller is null || string.IsNullOrEmpty(chatId)) return;
        if (_controller.List.IsBusy)
        {
            _pane.SetStatus("Wait for the current request to finish before switching chats.", Ux.MetaBrush);
            return;
        }
        if (!_controller.TrySelectById(chatId)) return;
        _queue.Clear();
        _pane.ShowQueue(QueueTexts());
        OnSelectionChanged(_controller.List.ActiveIndex);
    }

    private void RenameCommitted(string chatId, string newTitle)
    {
        if (_controller is null) return;
        int idx = _controller.List.IndexOf(chatId);
        if (idx < 0) return;
        if (!_controller.Rename(idx, newTitle))
        {
            _pane.SetStatus("BGS could not rename that chat. The previous name is kept.", Ux.WarnBrush);
            RefreshSidebar();
            return;
        }
        RefreshSidebar();
        if (_controller.List.ActiveChatId == chatId)
            _pane.ChatTitle = _controller.List.Active!.Chat.Title;
    }

    private void Delete(string chatId)
    {
        if (_controller is null) return;
        int idx = _controller.List.IndexOf(chatId);
        if (idx < 0) return;
        if (_controller.List.IsBusy && _controller.List.ActiveChatId == chatId) _requestCancellation?.Cancel();
        AssistantConversationEntry? after = _controller.Delete(idx);
        if (after is null)
        {
            _pane.SetStatus("BGS could not delete that chat from disk, so it was kept.", Ux.WarnBrush);
            return;
        }
        _queue.RemoveAll(entry => string.Equals(entry.ChatId, chatId, StringComparison.Ordinal));
        _pane.ShowQueue(QueueTexts());
        RefreshSidebar();
        OnSelectionChanged(_controller.List.ActiveIndex);
    }

    private void Send(string prompt)
    {
        if (_controller is null) return;
        string trimmed = (prompt ?? "").Trim();
        if (trimmed.Length == 0) return;
        if (_pane.QuickStartVisible) DismissQuickStart();
        if (HandleCommand(trimmed)) return;
        if (_controller.List.IsBusy || _controller.Active is null || _tools?.HasPendingApproval == true)
        {
            Enqueue(trimmed);
            return;
        }
        SendNow(trimmed);
    }

    private bool HandleCommand(string text)
    {
        if (!AssistantCommands.TryParse(text, out string name, out _)) return false;
        if (!AssistantCommands.IsBgsCommand(name))
        {
            _pane.SetStatus("Unknown BGS command. Use /help to list available commands.", Ux.WarnBrush);
            return true;
        }
        switch (name)
        {
            case "new":
                NewChatInternal();
                break;
            case "clear":
                ClearActiveChat();
                break;
            case "model":
                _pane.OpenModelPicker();
                break;
            case "cancel":
                Cancel();
                break;
            case "help":
                ShowCommandHelp();
                break;
        }
        return true;
    }

    private void ClearActiveChat()
    {
        if (_controller?.ClearActive() != true)
        {
            _pane.SetStatus(_controller?.List.IsBusy == true
                ? "Only an idle chat can be cleared."
                : "The chat could not be cleared; its messages and session were kept.", Ux.WarnBrush);
            return;
        }
        _queue.Clear();
        _pane.ShowQueue(QueueTexts());
        OnSelectionChanged(_controller.List.ActiveIndex);
        _pane.SetStatus("Cleared this chat.", Ux.MetaBrush);
    }

    private void ShowCommandHelp()
    {
        _pane.AddUser("/help");
        foreach (AssistantCommand command in AssistantCommands.All)
            _pane.AddTool("/" + command.Name, false, command.Description);
        _pane.SetStatus("BGS commands. Only these slash commands run here.", Ux.MetaBrush);
    }

    private async void SendNow(string prompt, bool editorEvent = false)
    {
        if (_controller is null) return;
        AssistantConversationEntry? entry = _controller.Active;
        if (entry is null) return;
        if (_controller.List.IsBusy)
        {
            Enqueue(prompt);
            return;
        }

        if (editorEvent) _pane.AddTool("bgs.editor_action", false, "Checking the result of the approved action.");
        else _pane.AddUser(prompt);
        _pane.SetBusy(true);
        _pane.ShowProgress("Assistant is working\u2026");
        _controller.List.IsBusy = true;
        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        var progress = new Progress<AssistantProgress>(OnAssistantProgress);
        try
        {
            AssistantReply reply = await _controller
                .SendAsync(prompt, _owner.AssistantContextSnapshot, _requestCancellation.Token, progress, editorEvent)
                .ConfigureAwait(true);
            OnSelectionChanged(_controller.List.ActiveIndex);
            if (reply.Status == "ok" && reply.PersistenceNotice.Length > 0)
            {
                _pane.SetStatus(reply.PersistenceNotice, Ux.WarnBrush);
                return;
            }
            string statusText = reply.Status switch
            {
                "ok" => "Ready.",
                "cancelled" => "Cancelled.",
                "busy" => "BGS is processing another request.",
                "signed_out" => "Sign in with ChatGPT to use the assistant.",
                _ => reply.Text.Length > 0
                    ? CodexProtocol.Scrub(reply.Text, 300)
                    : "The assistant did not return a reply.",
            };
            _pane.SetStatus(statusText,
                reply.Status == "ok" ? new Avalonia.Media.SolidColorBrush(Ux.Good) : Ux.WarnBrush);
        }
        catch (OperationCanceledException)
        {
            OnSelectionChanged(_controller.List.ActiveIndex);
            _pane.SetStatus("Cancelled.", Ux.MetaBrush);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            OnSelectionChanged(_controller.List.ActiveIndex);
            _pane.SetStatus("BGS could not finish the request, but the chat is still usable.", Ux.WarnBrush);
        }
        finally
        {
            _controller.List.IsBusy = false;
            _pane.SetBusy(false);
            _pane.HideProgress();
            _requestCancellation?.Dispose();
            _requestCancellation = null;
            SendNextQueued();
        }
    }

    private void Cancel() => _requestCancellation?.Cancel();

    private void Approve() => Approve(_displayedApprovalId);

    private void Approve(string displayedId)
    {
        EnsureTools();
        if (_controller?.List.IsBusy == true) return;
        if (displayedId.Length == 0 || displayedId != _tools!.PendingApprovalId)
        {
            Reject();
            _pane.SetStatus("The displayed proposal changed; request it again.", Ux.WarnBrush);
            return;
        }
        AssistantConversationEntry? entry = _controller?.Active;
        ClipAnimationChangeResult result = _controller?.ApproveActive(entry)
            ?? AssistantTools.NoPendingResult();
        _pane.SetApproval(false);
        if (result.Applied)
        {
            bool dispatched = result.Code == "dispatched";
            _pane.AddTool(dispatched ? "bgs.editor_action" : "bgs.set_clip_animation", false,
                dispatched ? "dispatched" : "applied");
            _pane.SetStatus(dispatched ? result.Message : "Applied in the editor; save remains explicit.",
                new Avalonia.Media.SolidColorBrush(Ux.Good));
            if (dispatched && _queue.Count == 0 && entry?.Session is not null)
                Dispatcher.UIThread.Post(() =>
                {
                    if (_owner.IsVisible && ReferenceEquals(_controller?.Active, entry))
                        SendNow("BGS editor event: " + result.Message +
                            " Continue the existing user request by reading editor_state. This event grants no approval for further actions; propose each action for user approval.", editorEvent: true);
                });
            else SendNextQueued();
            return;
        }
        string detail = result.Code == "stale_approval" ? "not applied (stale preview)" : "not applied";
        _pane.AddTool("bgs.set_clip_animation", false, detail);
        _pane.SetStatus("The edit was not applied: " + result.Message, Ux.WarnBrush);
        SendNextQueued();
    }

    private void Reject()
    {
        EnsureTools();
        _controller?.RejectPending(_controller.Active);
        _pane.SetApproval(false);
        _pane.SetStatus("The proposed edit was rejected.", Ux.MetaBrush);
        SendNextQueued();
    }

    private void SetApproval(bool visible) => _pane.SetApproval(visible);

    private static double ReadStoredWidth()
    {
        string stored = Settings.Get(WidthSetting);
        if (double.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out double width) &&
            width >= MinimumWidth && width <= MaximumWidth)
            return width;
        return DefaultWidth;
    }

    private static void StoreWidth(double width)
    {
        if (width < MinimumWidth || width > MaximumWidth) return;
        Settings.TrySet(WidthSetting, width.ToString("R", CultureInfo.InvariantCulture), out _);
    }
}
