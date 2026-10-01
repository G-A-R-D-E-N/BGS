using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BehaviourStudio.App;

internal sealed partial class AssistantPane : Border
{
    private readonly StackPanel _messages = new() { Spacing = 6 };
    private readonly ScrollViewer _conversation;
    private readonly StackPanel _queuePanel = new() { Spacing = 3 };
    private readonly Border _quickStart = new()
    {
        Background = Ux.CardBrush,
        BorderBrush = Ux.AccentBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(Ux.Radius),
        Padding = new Thickness(12),
        Margin = new Thickness(0, 0, 0, 8),
        IsVisible = false,
    };

    internal static readonly string[] QuickStartLines =
    {
        "Ask about the open behaviour, its graph, animations or the project.",
        "Choose a model with the Model button; the gear holds the provider and API key.",
        "Type / for commands: /new, /clear, /model, /cancel, /help.",
        "Enter sends and Shift+Enter adds a line; you can queue the next message while it works.",
        "Describe your goal and name the target bone, node or field; open the relevant file first.",
        "Each action waits for your approval. Review its target, value and path before choosing Approve action or Reject.",
        "Check the result after approval; an action is not a confirmed edit or save.",
        "Home > Take the tour includes the AI chat guide. Keep credentials in Settings, not chat.",
    };
    private readonly StackPanel _commandPanel = new() { Spacing = 1 };
    private readonly List<Border> _commandRows = new();
    private readonly List<AssistantCommand> _commandChoices = new();
    private int _commandChoice = -1;
    private readonly TextBlock _progressText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = Ux.MetaBrush,
        FontSize = Ux.FontBody,
    };
    private readonly Border _progress;
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string _progressPhase = "";
    private DateTime _progressStarted;
    private readonly TextBox _composer = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 54,
        MaxHeight = 140,
    };
    private readonly Button _modelButton = Ux.Secondary("Model \u25BE");
    private readonly AssistantModelPicker _picker = new();
    private readonly Button _modelReset = Ux.Secondary("\u21BA");
    private readonly Button _send = Ux.Primary("Send");
    private readonly Button _cancel = Ux.Secondary("Cancel");
    private readonly Border _approval = new();
    private readonly TextBlock _approvalDescription = new() { TextWrapping = TextWrapping.Wrap, Foreground = Ux.WarnBrush };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _approve = Ux.Primary("Approve action");
    private readonly Button _reject = Ux.Secondary("Reject");
    private readonly TextBlock _chatHeader = new()
    {
        Text = "(no chat selected)",
        Foreground = Ux.TitleBrush,
        FontSize = Ux.FontTitle,
        FontWeight = FontWeight.SemiBold,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private int _messageCount;

    public AssistantPane()
    {
        Background = Ux.RailBrush;
        BorderBrush = Ux.BorderBrush;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(12);

        var newChat = Ux.Secondary("New chat");
        var close = Ux.Secondary("Close");
        newChat.Click += (_, _) => NewChatRequested?.Invoke();
        close.Click += (_, _) => CloseRequested?.Invoke();
        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(newChat, Dock.Right);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(newChat);
        header.Children.Add(close);
        header.Children.Add(_chatHeader);
        header.Margin = new Thickness(0, 0, 0, 8);

        var conversation = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = _messages,
        };
        _conversation = conversation;

        _progress = new Border
        {
            Background = Ux.BaseBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Ux.Radius),
            Padding = new Thickness(10),
            IsVisible = false,
            Child = new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Assistant",
                        Foreground = Ux.TitleBrush,
                        FontSize = Ux.FontSmall,
                        FontWeight = FontWeight.SemiBold,
                    },
                    _progressText,
                },
            },
        };
        _messages.Children.Add(_progress);
        _progressTimer.Tick += (_, _) =>
        {
            if (!_progress.IsVisible) return;
            int seconds = (int)(DateTime.UtcNow - _progressStarted).TotalSeconds;
            _progressText.Text = seconds >= 3 ? _progressPhase + "  " + seconds + "s" : _progressPhase;
        };

        _approval.Background = Ux.CardBrush;
        _approval.BorderBrush = Ux.WarnBrush;
        _approval.BorderThickness = new Thickness(1);
        _approval.CornerRadius = new CornerRadius(Ux.Radius);
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
                new ScrollViewer { MaxHeight = 140, Content = _approvalDescription },
                approvalButtons,
            },
        };

        _status.Foreground = Ux.MetaBrush;
        _status.FontSize = Ux.FontSmall;
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        _status.VerticalAlignment = VerticalAlignment.Center;

        _modelReset.FontSize = 12;
        _modelReset.Padding = new Thickness(8, 2, 8, 2);
        _modelReset.Margin = new Thickness(4, 0, 0, 0);
        _modelReset.IsVisible = false;
        ToolTip.SetTip(_modelReset, "Use the provider's own default model");
        _modelReset.Click += (_, _) => ModelResetRequested?.Invoke();

        _modelButton.FontSize = Ux.FontSmall;
        _modelButton.Padding = new Thickness(10, 3, 10, 3);
        _modelButton.IsVisible = false;
        ToolTip.SetTip(_modelButton, "Choose the model for the next request");
        _picker.IsVisible = false;
        _modelButton.Click += (_, _) =>
        {
            _picker.IsVisible = !_picker.IsVisible;
            if (_picker.IsVisible)
            {
                _picker.FocusSearch();
                ModelControlOpened?.Invoke();
            }
        };
        _picker.Chosen += model =>
        {
            _picker.IsVisible = false;
            ModelSelected?.Invoke(model);
        };
        _picker.Dismissed += () => _picker.IsVisible = false;

        _send.Click += (_, _) => SendComposer();
        _cancel.Click += (_, _) => CancelRequested?.Invoke();
        _cancel.IsVisible = false;
        _composer.AddHandler(KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);
        _composer.TextChanged += (_, _) => RefreshCommands();
        _commandPanel.IsVisible = false;

        var toolbar = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(_send, Dock.Right);
        DockPanel.SetDock(_cancel, Dock.Right);
        DockPanel.SetDock(_modelReset, Dock.Right);
        DockPanel.SetDock(_modelButton, Dock.Right);
        toolbar.Children.Add(_send);
        toolbar.Children.Add(_cancel);
        toolbar.Children.Add(_modelReset);
        toolbar.Children.Add(_modelButton);
        toolbar.Children.Add(_status);

        var quickStartDismiss = Ux.Primary("Got it");
        quickStartDismiss.Click += (_, _) => QuickStartDismissed?.Invoke();
        var quickStartBody = new StackPanel { Spacing = 6 };
        quickStartBody.Children.Add(new TextBlock
        {
            Text = "Quick start",
            Foreground = Ux.TitleBrush,
            FontSize = Ux.FontBody,
            FontWeight = FontWeight.SemiBold,
        });
        foreach (string line in QuickStartLines)
        {
            quickStartBody.Children.Add(new TextBlock
            {
                Text = "\u2022  " + line,
                Foreground = Ux.MetaBrush,
                FontSize = Ux.FontSmall,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        quickStartBody.Children.Add(quickStartDismiss);
        _quickStart.Child = quickStartBody;

        var bottom = new StackPanel
        {
            Spacing = 0,
            Children = { _quickStart, _approval, _picker, _queuePanel, _commandPanel, _composer, toolbar },
        };

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(header, 0);
        Grid.SetRow(_conversation, 1);
        Grid.SetRow(bottom, 2);
        body.Children.Add(header);
        body.Children.Add(_conversation);
        body.Children.Add(bottom);
        Child = body;
    }

    public event Action<string>? SendRequested;
    public event Action? CancelRequested;
    public event Action? NewChatRequested;
    public event Action? CloseRequested;
    public event Action? ApproveRequested;
    public event Action? RejectRequested;
    public event Action<CodexModel>? ModelSelected;
    public event Action? ModelResetRequested;
    public event Action? ModelControlOpened;
    public event Action<int>? QueuedRemoved;
    public event Action? QuickStartDismissed;

    public bool QuickStartVisible => _quickStart.IsVisible;
    public void ShowQuickStart() => _quickStart.IsVisible = true;
    public void HideQuickStart() => _quickStart.IsVisible = false;

    public int MessageCount => _messageCount;
    public bool IsBusy => !_send.IsVisible;
    public bool ModelSelectorVisible => _modelButton.IsVisible;
    public bool ModelResetVisible
    {
        get => _modelReset.IsVisible;
        internal set => _modelReset.IsVisible = value;
    }
    public string? SelectedModelItem => _picker.SelectedModel.Length > 0 ? _picker.SelectedModel : null;
    public bool ModelPanelOpen => _picker.IsVisible;
    internal AssistantModelPicker ModelPicker => _picker;
    internal Button ModelButton => _modelButton;
    internal string ModelButtonLabel => _modelButton.Content?.ToString() ?? "";
    internal void FavoriteForTest(string wireModel) => _picker.InvokeFavorite(wireModel);
    internal Button ModelReset => _modelReset;
    internal void CloseModelPopup() => _picker.IsVisible = false;
    internal void InvokeModelSelected(CodexModel model) => ModelSelected?.Invoke(model);
    internal void InvokeModelResetRequested() => ModelResetRequested?.Invoke();

    public void SetModels(IReadOnlyList<CodexModel> models, string agent, string selected, string defaultLabel)
    {
        _picker.SetModels(models, agent, selected);
        _modelButton.IsVisible = models.Count > 0;
        _modelReset.IsVisible = selected.Length > 0;
        CodexModel? chosen = models.FirstOrDefault(model =>
            string.Equals(AssistantModelCatalog.AgentOf(model), agent, StringComparison.Ordinal) &&
            string.Equals(model.WireModel, selected, StringComparison.Ordinal));
        _modelButton.Content = (chosen?.DisplayName ?? defaultLabel) + " \u25BE";
    }
    public TextBox Composer => _composer;
    public string ChatTitle
    {
        get => _chatHeader.Text ?? "";
        set => _chatHeader.Text = value;
    }
    public string Status => _status.Text ?? "";
    public bool ApprovalVisible => _approval.IsVisible;
    public void SetDeviceCode(string url, string userCode)
    {
    }
    public void ClearDeviceCode()
    {
    }
    public void SetStatus(string text, IBrush brush)
    {
        _status.Text = text;
        _status.Foreground = brush;
    }
    public void SetBusy(bool busy)
    {
        _send.IsVisible = !busy;
        _cancel.IsVisible = busy;
        _modelButton.IsEnabled = !busy;
        _modelReset.IsEnabled = !busy;
        _approve.IsEnabled = !busy;
        _reject.IsEnabled = !busy;
    }
    public void SetApproval(bool visible) => _approval.IsVisible = visible;
    public void SetApprovalDescription(string text) => _approvalDescription.Text = text;

    public bool ProgressVisible => _progress.IsVisible;
    public string ProgressText => _progressText.Text ?? "";

    public void ShowProgress(string text)
    {
        _progressPhase = text;
        _progressStarted = DateTime.UtcNow;
        _progressText.Text = text;
        _progress.IsVisible = true;
        if (_messages.Children.Count == 0 || !ReferenceEquals(_messages.Children[^1], _progress))
        {
            _messages.Children.Remove(_progress);
            _messages.Children.Add(_progress);
        }
        if (!_progressTimer.IsEnabled) _progressTimer.Start();
        ScrollToEnd();
    }

    public void HideProgress()
    {
        _progressTimer.Stop();
        _progress.IsVisible = false;
    }

    public void Clear()
    {
        _messages.Children.Clear();
        _messages.Children.Add(_progress);
        HideProgress();
        _messageCount = 0;
        SetApproval(false);
        SetStatus("New chat.", Ux.MetaBrush);
    }

    public void AddUser(string text) => AddMessage("You", text, Ux.CardBrush);
    public void AddAssistant(string text)
    {
        if (text.Length > 0) AddMessage("Assistant", text, Ux.BaseBrush, markdown: true);
    }
    public void AddTool(string name, bool requiresApproval, string detail = "") =>
        AddMessage("BGS", $"{name}{(requiresApproval ? " (approval pending)" : "")}" +
            (detail.Length == 0 ? "" : $": {detail}"),
            Ux.RailBrush, Ux.MetaBrush, Ux.FontSmall);

    private void SendComposer()
    {
        string text = (_composer.Text ?? "").Trim();
        if (text.Length == 0) return;
        _composer.Text = "";
        SendRequested?.Invoke(text);
    }

    private void OnComposerKeyDown(object? sender, KeyEventArgs args)
    {
        if (_commandPanel.IsVisible)
        {
            switch (args.Key)
            {
                case Key.Escape:
                    args.Handled = true;
                    HideCommands();
                    return;
                case Key.Down:
                    args.Handled = true;
                    MoveCommand(1);
                    return;
                case Key.Up:
                    args.Handled = true;
                    MoveCommand(-1);
                    return;
                case Key.Tab:
                    args.Handled = true;
                    PickCommand();
                    return;
                case Key.Enter:
                    if (!TypedCommandMatchesExactly())
                    {
                        args.Handled = true;
                        PickCommand();
                        return;
                    }
                    break;
            }
        }
        if (args.Key != Key.Enter) return;
        if ((args.KeyModifiers & KeyModifiers.Shift) != 0) return;
        args.Handled = true;
        SendComposer();
    }

    private void ScrollToEnd() => Dispatcher.UIThread.Post(() =>
    {
        double bottom = _conversation.Extent.Height;
        if (_conversation.Offset.Y < bottom)
            _conversation.Offset = new Vector(_conversation.Offset.X, bottom);
    }, DispatcherPriority.Background);

    private void AddMessage(string speaker, string text, IBrush background,
                            IBrush? foreground = null, double? fontSize = null, bool markdown = false)
    {
        var label = new TextBlock
        {
            Text = speaker,
            Foreground = Ux.TitleBrush,
            FontSize = Ux.FontSmall,
            FontWeight = FontWeight.SemiBold,
        };
        Control content = markdown
            ? AssistantMarkdown.Render(text)
            : new TextBlock
            {
                Text = text,
                Foreground = foreground ?? Ux.MetaBrush,
                FontSize = fontSize ?? Ux.FontBody,
                TextWrapping = TextWrapping.Wrap,
            };
        var message = new Border
        {
            Background = background,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Ux.Radius),
            Padding = new Thickness(10),
            Child = new StackPanel { Spacing = 3, Children = { label, content } },
        };
        int index = _progress.IsVisible ? Math.Max(0, _messages.Children.Count - 1) : _messages.Children.Count;
        _messages.Children.Insert(index, message);
        _messageCount++;
        ScrollToEnd();
    }
}
