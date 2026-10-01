using System;
using System.Collections.Generic;
using System.Linq;

namespace BehaviourStudio.App;

internal sealed partial class AssistantUi
{
    private const int MaximumQueuedMessages = 5;

    private readonly List<(string ChatId, string Text)> _queue = new();

    private List<string> QueueTexts() => _queue.Select(entry => entry.Text).ToList();

    private void Enqueue(string text)
    {
        if (_controller?.List.ActiveChatId is not { Length: > 0 } chatId) return;
        if (_queue.Count >= MaximumQueuedMessages)
        {
            _pane.SetStatus($"The queue holds up to {MaximumQueuedMessages} messages.", Ux.WarnBrush);
            return;
        }
        _queue.Add((chatId, text));
        _pane.ShowQueue(QueueTexts());
        _pane.SetStatus("Queued. It sends when the current turn ends.", Ux.MetaBrush);
    }

    private void SendNextQueued()
    {
        if (_tools?.HasPendingApproval == true || _controller?.List.IsBusy == true) return;
        while (_queue.Count > 0)
        {
            (string chatId, string text) = _queue[0];
            _queue.RemoveAt(0);
            _pane.ShowQueue(QueueTexts());
            if (_controller is null) return;
            if (!string.Equals(_controller.List.ActiveChatId, chatId, StringComparison.Ordinal)) continue;
            SendNow(text);
            return;
        }
    }

    private void OnAssistantProgress(AssistantProgress progress)
    {
        switch (progress.Phase)
        {
            case AssistantPhase.Tool:
                if (progress.Detail.Length > 0)
                {
                    _pane.ShowProgress("Running " + progress.Detail + "\u2026");
                    _pane.AddTool(progress.Detail, false, "running");
                }
                else
                {
                    _pane.ShowProgress("Working\u2026");
                }
                break;
            case AssistantPhase.Thinking:
                _pane.ShowProgress(progress.Detail.Length > 0
                    ? "Thinking\u2026 " + progress.Detail
                    : "Thinking\u2026");
                break;
            default:
                _pane.ShowProgress(progress.Detail.Length > 0
                    ? progress.Detail
                    : "Assistant is working\u2026");
                break;
        }
    }
}
