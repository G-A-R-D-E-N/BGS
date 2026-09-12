using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class ProjectAnalysisControllerTests
{
    [Fact]
    public async Task ProjectValidation_ReturnsTheCurrentResultAndForwardsArguments()
    {
        long revision = 7;
        var controller = new ProjectAnalysisController(() => revision);
        var chain = new ProjectChain { Root = "project-root" };
        var expected = new ProjectCheck.Result();
        var published = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        ProjectChain? receivedChain = null;
        IProgress<string>? receivedProgress = null;

        var outcome = await controller.ValidateProject(
            chain, revision, message => published.TrySetResult(message),
            (suppliedChain, suppliedProgress) =>
            {
                receivedChain = suppliedChain;
                receivedProgress = suppliedProgress;
                suppliedProgress.Report("one file");
                return Task.FromResult(expected);
            });

        Assert.True(outcome.Completed);
        Assert.False(outcome.Stale);
        Assert.False(outcome.Failed);
        Assert.Same(expected, outcome.Value);
        Assert.Null(outcome.Error);
        Assert.Same(chain, receivedChain);
        Assert.NotNull(receivedProgress);
        Assert.Equal("one file", await published.Task);
    }

    [Fact]
    public async Task PapyrusScan_ClassifiesASuccessThatBelongsToAnOlderRevisionAsStale()
    {
        long revision = 11;
        var controller = new ProjectAnalysisController(() => revision);
        var started = NewSignal();
        var gate = new TaskCompletionSource<PapyrusEvents.Index>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task<ProjectAnalysisController.Outcome<PapyrusEvents.Index>> pending =
            controller.ScanPapyrus("scripts", revision, _ =>
            {
                started.TrySetResult(true);
                return gate.Task;
            });

        await started.Task;
        revision++;
        gate.SetResult(new PapyrusEvents.Index());
        var outcome = await pending;

        AssertStale(outcome);
    }

    [Fact]
    public async Task PapyrusScan_ContainsACurrentWorkerFailure()
    {
        const long revision = 19;
        var controller = new ProjectAnalysisController(() => revision);
        var failure = new IOException("worker failed");

        var outcome = await controller.ScanPapyrus(
            "scripts", revision,
            _ => Task.FromException<PapyrusEvents.Index>(failure));

        Assert.True(outcome.Failed);
        Assert.False(outcome.Completed);
        Assert.False(outcome.Stale);
        Assert.Same(failure, outcome.Error);
        Assert.Null(outcome.Value);
    }

    [Fact]
    public async Task PapyrusScan_DiscardsAFailureThatBelongsToAnOlderRevision()
    {
        long revision = 23;
        var controller = new ProjectAnalysisController(() => revision);
        var started = NewSignal();
        var gate = new TaskCompletionSource<PapyrusEvents.Index>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task<ProjectAnalysisController.Outcome<PapyrusEvents.Index>> pending =
            controller.ScanPapyrus("scripts", revision, _ =>
            {
                started.TrySetResult(true);
                return gate.Task;
            });

        await started.Task;
        revision++;
        gate.SetException(new IOException("stale failure"));
        var outcome = await pending;

        AssertStale(outcome);
    }

    [Fact]
    public async Task PapyrusScan_DiscardsAnOlderRequestOnTheSameRevision()
    {
        const long revision = 27;
        var controller = new ProjectAnalysisController(() => revision);
        var firstStarted = NewSignal();
        var secondStarted = NewSignal();
        var firstGate = NewPapyrusGate();
        var secondGate = NewPapyrusGate();
        var firstValue = new PapyrusEvents.Index();
        var secondValue = new PapyrusEvents.Index();

        Task<ProjectAnalysisController.Outcome<PapyrusEvents.Index>> first =
            controller.ScanPapyrus("remembered", revision, _ =>
            {
                firstStarted.TrySetResult(true);
                return firstGate.Task;
            });
        await firstStarted.Task;

        Task<ProjectAnalysisController.Outcome<PapyrusEvents.Index>> second =
            controller.ScanPapyrus("selected", revision, _ =>
            {
                secondStarted.TrySetResult(true);
                return secondGate.Task;
            });
        await secondStarted.Task;

        secondGate.SetResult(secondValue);
        var secondOutcome = await second;
        firstGate.SetResult(firstValue);
        var firstOutcome = await first;

        Assert.True(secondOutcome.Completed);
        Assert.Same(secondValue, secondOutcome.Value);
        AssertStale(firstOutcome);
    }

    [Fact]
    public async Task WorkerController_ContainsASynchronousRunnerThrow()
    {
        const long revision = 29;
        var controller = new ProjectAnalysisController(() => revision);
        var failure = new InvalidOperationException("synchronous runner failure");

        var outcome = await controller.ScanPapyrus(
            "scripts", revision,
            _ => throw failure);

        Assert.True(outcome.Failed);
        Assert.Same(failure, outcome.Error);
    }

    [Fact]
    public async Task WorkerController_TreatsANullResultAsAFailure()
    {
        const long revision = 31;
        var controller = new ProjectAnalysisController(() => revision);

        var outcome = await controller.ScanPapyrus(
            "scripts", revision,
            _ => Task.FromResult<PapyrusEvents.Index>(null!));

        Assert.True(outcome.Failed);
        Assert.IsType<InvalidOperationException>(outcome.Error);
        Assert.Contains("no result", outcome.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectProgress_DiscardsAQueuedMessageAfterTheRevisionChanges()
    {
        long revision = 37;
        var controller = new ProjectAnalysisController(() => revision);
        var queued = new QueuedSynchronizationContext();
        SynchronizationContext? previous = SynchronizationContext.Current;
        var messages = new List<string>();

        try
        {
            SynchronizationContext.SetSynchronizationContext(queued);
            Task<ProjectAnalysisController.Outcome<ProjectCheck.Result>> pending =
                controller.ValidateProject(
                    new ProjectChain(), revision, messages.Add,
                    (_, progress) =>
                    {
                        progress.Report("stale progress");
                        return Task.FromResult(new ProjectCheck.Result());
                    });

            queued.RunOne();
            revision++;
            queued.Drain();
            Assert.True((await pending).Completed);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Empty(messages);
    }

    [Fact]
    public async Task ProjectProgress_PublishesAQueuedMessageForTheCurrentRevision()
    {
        const long revision = 41;
        var controller = new ProjectAnalysisController(() => revision);
        var queued = new QueuedSynchronizationContext();
        SynchronizationContext? previous = SynchronizationContext.Current;
        var messages = new List<string>();

        try
        {
            SynchronizationContext.SetSynchronizationContext(queued);
            Task<ProjectAnalysisController.Outcome<ProjectCheck.Result>> pending =
                controller.ValidateProject(
                    new ProjectChain(), revision, messages.Add,
                    (_, progress) =>
                    {
                        progress.Report("current progress");
                        return Task.FromResult(new ProjectCheck.Result());
                    });

            queued.RunOne();
            queued.Drain();
            Assert.True((await pending).Completed);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Equal(new[] { "current progress" }, messages);
    }

    [Fact]
    public async Task ProjectProgress_DiscardsMessagesFromASupersededRequest()
    {
        const long revision = 43;
        var controller = new ProjectAnalysisController(() => revision);
        var queued = new QueuedSynchronizationContext();
        SynchronizationContext? previous = SynchronizationContext.Current;
        var messages = new List<string>();

        try
        {
            SynchronizationContext.SetSynchronizationContext(queued);
            Task<ProjectAnalysisController.Outcome<ProjectCheck.Result>> first =
                controller.ValidateProject(
                    new ProjectChain(), revision, messages.Add,
                    (_, progress) =>
                    {
                        progress.Report("old progress");
                        return Task.FromResult(new ProjectCheck.Result());
                    });
            queued.RunOne();

            Task<ProjectAnalysisController.Outcome<ProjectCheck.Result>> second =
                controller.ValidateProject(
                    new ProjectChain(), revision, messages.Add,
                    (_, _) => Task.FromResult(new ProjectCheck.Result()));
            queued.Drain();

            Assert.True((await first).Completed);
            Assert.True((await second).Completed);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Empty(messages);
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<PapyrusEvents.Index> NewPapyrusGate() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void AssertStale<T>(ProjectAnalysisController.Outcome<T> outcome)
        where T : class
    {
        Assert.True(outcome.Stale);
        Assert.False(outcome.Completed);
        Assert.False(outcome.Failed);
        Assert.Null(outcome.Value);
        Assert.Null(outcome.Error);
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) =>
            _queue.Enqueue((d, state));

        public void RunOne()
        {
            Assert.NotEmpty(_queue);
            var (callback, state) = _queue.Dequeue();
            callback(state);
        }

        public void Drain()
        {
            while (_queue.Count > 0) RunOne();
        }
    }
}
