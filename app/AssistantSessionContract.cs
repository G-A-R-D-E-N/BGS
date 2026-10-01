using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BehaviourStudio.App;

public sealed record AssistantToolActivity(string Name, bool RequiresApproval);

public enum AssistantPhase
{
    Working,
    Thinking,
    Tool,
}

public sealed record AssistantProgress(AssistantPhase Phase, string Detail = "");

public sealed record AssistantReply(
    string Status,
    string Text,
    int Iterations,
    IReadOnlyList<AssistantToolActivity> Tools,
    bool AwaitingApproval,
    string PersistenceNotice = "");

public interface IAssistantSession : IDisposable
{
    Task<AssistantReply> SendAsync(
        string prompt, AssistantContext context, CancellationToken cancellationToken = default);

    void ClearHistory();
}

public interface IAssistantProgressSink
{
    IProgress<AssistantProgress>? Progress { get; set; }
}

public interface IAssistantReplayAware
{
    Task<AssistantReply> SendAsync(
        string prompt, string replayPrompt, AssistantContext context,
        CancellationToken cancellationToken = default);
}
