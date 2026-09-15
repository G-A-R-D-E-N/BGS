using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.AI;

namespace BehaviourStudio.App;

internal sealed class AssistantPane : Border
{
    private readonly TextBlock _provider = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _messages = new() { Spacing = 6 };
    private readonly TextBox _composer = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 70,
        MaxHeight = 150,
    };
    private readonly Button _send = Ux.Primary("Send");
    private readonly Button _cancel = Ux.Secondary("Cancel");
    private readonly Border _approval = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _approve = Ux.Primary("Apply approved edit");
    private readonly Button _reject = Ux.Secondary("Reject");
    private int _messageCount;

    public AssistantPane()
    {
        Background = Ux.RailBrush;
        BorderBrush = Ux.BorderBrush;
        BorderThickness = new Thickness(1, 0, 0, 0);
        Padding = new Thickness(12);

        var title = new TextBlock
        {
            Text = "Assistant",
            FontSize = Ux.FontTitle,
            FontWeight = FontWeight.SemiBold,
            Foreground = Ux.TitleBrush,
        };
        var newChat = Ux.Secondary("New chat");
        var close = Ux.Secondary("Close");
        newChat.Click += (_, _) => NewChatRequested?.Invoke();
        close.Click += (_, _) => CloseRequested?.Invoke();
        var header = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(newChat, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(newChat);
        header.Children.Add(title);

        _provider.Foreground = Ux.MetaBrush;
        _provider.FontSize = Ux.FontSmall;
        _provider.Margin = new Thickness(0, 4, 0, 0);

        var notice = new TextBlock
        {
            Text = "Sends only your prompt, bounded editor context, and BGS tool results when you press Send. Opening this drawer makes no provider request.",
            Foreground = Ux.MutedBrush,
            FontSize = Ux.FontSmall,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 8),
        };

        var conversation = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = _messages,
        };

        _approval.Background = Ux.CardBrush;
        _approval.BorderBrush = Ux.WarnBrush;
        _approval.BorderThickness = new Thickness(1);
        _approval.Padding = new Thickness(8);
        _approval.Margin = new Thickness(0, 8, 0, 0);
        _approval.IsVisible = false;
        var approvalButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _approve.Click += (_, _) => ApproveRequested?.Invoke();
        _reject.Click += (_, _) => RejectRequested?.Invoke();
        approvalButtons.Children.Add(_approve);
        approvalButtons.Children.Add(_reject);
        _approval.Child = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = "BGS has a pending active-editor preview. Review it before applying.",
                    Foreground = Ux.WarnBrush,
                    TextWrapping = TextWrapping.Wrap,
                },
                approvalButtons,
            },
        };

        _status.Foreground = Ux.MetaBrush;
        _status.FontSize = Ux.FontSmall;
        _status.Margin = new Thickness(0, 6, 0, 0);
        _cancel.IsVisible = false;
        _send.Click += (_, _) => SendComposer();
        _cancel.Click += (_, _) => CancelRequested?.Invoke();
        var composerButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        composerButtons.Children.Add(_send);
        composerButtons.Children.Add(_cancel);

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var top = new StackPanel { Children = { header, _provider, notice } };
        Grid.SetRow(top, 0);
        Grid.SetRow(conversation, 1);
        var bottom = new StackPanel { Children = { _approval, _composer, composerButtons, _status } };
        Grid.SetRow(bottom, 2);
        body.Children.Add(top);
        body.Children.Add(conversation);
        body.Children.Add(bottom);
        Child = body;
    }

    public event Action<string>? SendRequested;
    public event Action? CancelRequested;
    public event Action? NewChatRequested;
    public event Action? CloseRequested;
    public event Action? ApproveRequested;
    public event Action? RejectRequested;

    public int MessageCount => _messageCount;
    public string ProviderStatus => _provider.Text ?? "";
    public string Status => _status.Text ?? "";
    public bool ApprovalVisible => _approval.IsVisible;
    public bool IsBusy => !_send.IsVisible;
    public TextBox Composer => _composer;

    public void SetProvider(string text) => _provider.Text = text;

    public void AddUser(string text) => AddMessage("You", text, Ux.CardBrush);

    public void AddAssistant(string text)
    {
        if (text.Length > 0) AddMessage("Assistant", text, Ux.BaseBrush);
    }

    public void AddTool(string name, bool requiresApproval, string detail = "") =>
        AddMessage("BGS", $"{name}{(requiresApproval ? " (approval pending)" : "")}" +
            (detail.Length == 0 ? "" : $": {detail}"),
            Ux.RailBrush, Ux.MetaBrush, Ux.FontSmall);

    public void SetStatus(string text, IBrush brush)
    {
        _status.Text = text;
        _status.Foreground = brush;
    }

    public void SetBusy(bool busy)
    {
        _composer.IsEnabled = !busy;
        _send.IsVisible = !busy;
        _cancel.IsVisible = busy;
    }

    public void SetApproval(bool visible) => _approval.IsVisible = visible;

    public void Clear()
    {
        _messages.Children.Clear();
        _messageCount = 0;
        SetApproval(false);
        SetStatus("New chat.", Ux.MetaBrush);
    }

    private void SendComposer()
    {
        string text = (_composer.Text ?? "").Trim();
        if (text.Length == 0) return;
        _composer.Text = "";
        SendRequested?.Invoke(text);
    }

    private void AddMessage(string speaker, string text, IBrush background,
                            IBrush? foreground = null, double? fontSize = null)
    {
        var label = new TextBlock
        {
            Text = speaker,
            Foreground = Ux.TitleBrush,
            FontSize = Ux.FontSmall,
            FontWeight = FontWeight.SemiBold,
        };
        var content = new TextBlock
        {
            Text = text,
            Foreground = foreground ?? Ux.MetaBrush,
            FontSize = fontSize ?? Ux.FontBody,
            TextWrapping = TextWrapping.Wrap,
        };
        _messages.Children.Add(new Border
        {
            Background = background,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Child = new StackPanel { Spacing = 3, Children = { label, content } },
        });
        _messageCount++;
    }
}

internal sealed class AssistantUi
{
    private const double DefaultWidth = 420;
    private readonly MainWindow _owner;
    private readonly AssistantPane _pane = new();
    private readonly ColumnDefinition _splitterColumn = new(new GridLength(0, GridUnitType.Pixel));
    private readonly ColumnDefinition _drawerColumn =
        new(new GridLength(0, GridUnitType.Pixel)) { MinWidth = 0, MaxWidth = 520 };
    private readonly GridSplitter _splitter;
    private AssistantTools? _tools;
    private AssistantSession? _session;
    private CancellationTokenSource? _requestCancellation;
    private bool _open;

    public AssistantUi(MainWindow owner, Grid root, EditorShell shell)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        root.ColumnDefinitions.Add(_splitterColumn);
        root.ColumnDefinitions.Add(_drawerColumn);
        _splitter = new GridSplitter
        {
            Width = 6,
            Background = Brushes.Transparent,
            IsVisible = false,
        };
        Grid.SetColumn(_splitter, 1);
        Grid.SetColumn(_pane, 2);
        root.Children.Add(_splitter);
        root.Children.Add(_pane);

        var toggle = Ux.Secondary("Assistant");
        ToolTip.SetTip(toggle, "Open the AI assistant drawer.");
        toggle.Click += (_, _) => SetOpen(!_open);
        shell.Tools.Children.Add(toggle);
        _pane.NewChatRequested += NewChat;
        _pane.CloseRequested += () => SetOpen(false);
        _pane.CancelRequested += Cancel;
        _pane.SendRequested += Send;
        _pane.ApproveRequested += Approve;
        _pane.RejectRequested += Reject;
        _pane.SetProvider(ProviderText(AssistantProviderOptions.FromSettings()));
        _pane.SetStatus("Ready. Opening the drawer makes no provider request.", Ux.MetaBrush);
        SetOpen(false);
    }

    public bool IsOpen => _open;
    public bool IsBusy => _pane.IsBusy;
    public int MessageCount => _pane.MessageCount;
    public string ProviderStatus => _pane.ProviderStatus;
    public string Status => _pane.Status;
    public bool ApprovalVisible => _pane.ApprovalVisible;
    public AssistantPane Pane => _pane;

    internal void SetClientForTest(IChatClient client)
    {
        _tools ??= _owner.CreateAssistantTools();
        _session?.Dispose();
        _session = _owner.CreateAssistantSession(client, _tools);
    }

    private void SetOpen(bool open)
    {
        _open = open;
        _drawerColumn.Width = new GridLength(open ? DefaultWidth : 0, GridUnitType.Pixel);
        _splitterColumn.Width = new GridLength(open ? 6 : 0, GridUnitType.Pixel);
        _pane.IsVisible = open;
        _splitter.IsVisible = open;
    }

    private async void Send(string prompt)
    {
        if (_pane.IsBusy) return;
        _pane.AddUser(prompt);
        _pane.SetBusy(true);
        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        try
        {
            if (!EnsureSession()) return;
            var reply = await _session!.SendAsync(prompt, _owner.AssistantContextSnapshot,
                _requestCancellation.Token).ConfigureAwait(true);
            foreach (var tool in reply.Tools) _pane.AddTool(tool.Name, tool.RequiresApproval);
            _pane.AddAssistant(reply.Text);
            _pane.SetApproval(reply.AwaitingApproval);
            _pane.SetStatus(reply.Status == "ok" ? "Ready." : reply.Text,
                reply.Status == "ok" ? new SolidColorBrush(Ux.Good) : Ux.WarnBrush);
        }
        finally
        {
            _pane.SetBusy(false);
            _requestCancellation?.Dispose();
            _requestCancellation = null;
        }
    }

    private bool EnsureSession()
    {
        if (_session != null) return true;
        var options = AssistantProviderOptions.FromSettings();
        if (!AssistantProvider.TryCreate(options, out IChatClient? client, out string error))
        {
            _pane.SetStatus("Disconnected: " + error, Ux.WarnBrush);
            return false;
        }
        _tools = _owner.CreateAssistantTools();
        _session = _owner.CreateAssistantSession(client!, _tools);
        _pane.SetProvider(ProviderText(options));
        return true;
    }

    private void Cancel() => _requestCancellation?.Cancel();

    private void NewChat()
    {
        _session?.ClearHistory();
        _tools?.RejectPendingClipAnimation();
        _pane.Clear();
    }

    private void Approve()
    {
        if (_tools == null) return;
        var result = _tools.ApprovePendingClipAnimation();
        _pane.SetApproval(false);
        _pane.AddTool("bgs.set_clip_animation", false, result.Applied ? "applied" : result.Message);
        _pane.SetStatus(result.Applied ? "Applied in the editor; save remains explicit." : result.Message,
            result.Applied ? new SolidColorBrush(Ux.Good) : Ux.WarnBrush);
    }

    private void Reject()
    {
        _tools?.RejectPendingClipAnimation();
        _pane.SetApproval(false);
        _pane.SetStatus("The proposed edit was rejected.", Ux.MetaBrush);
    }

    private static string ProviderText(AssistantProviderOptions options)
    {
        string location = Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out Uri? uri) &&
                          uri.IsLoopback ? "local" : "remote";
        return $"OpenAI-compatible · {options.Model} · {location} · {options.BaseUrl}";
    }
}
