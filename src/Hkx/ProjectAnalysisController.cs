using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenCommonwealth.Services.Hkx;

internal sealed class ProjectAnalysisController
{
    internal enum OutcomeState
    {
        Completed,
        Stale,
        Failed,
    }

    internal sealed record Outcome<T>(OutcomeState State, T? Value, Exception? Error)
        where T : class
    {
        internal bool Completed => State == OutcomeState.Completed;
        internal bool Stale => State == OutcomeState.Stale;
        internal bool Failed => State == OutcomeState.Failed;
    }

    private readonly Func<long> _currentRevision;
    private long _projectOperation;
    private long _papyrusOperation;

    internal ProjectAnalysisController(Func<long> currentRevision) =>
        _currentRevision = currentRevision ?? throw new ArgumentNullException(nameof(currentRevision));

    internal Task<Outcome<ProjectCheck.Result>> ValidateProject(
        ProjectChain chain,
        long revision,
        Action<string> publishProgress,
        Func<ProjectChain, IProgress<string>, Task<ProjectCheck.Result>>? runner = null)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(publishProgress);

        long operation = Interlocked.Increment(ref _projectOperation);
        bool Current() => IsCurrent(revision, operation, ref _projectOperation);
        IProgress<string> progress = new Progress<string>(message =>
        {
            if (Current()) publishProgress(message);
        });

        return Run(Current, () => runner != null
            ? runner(chain, progress)
            : Task.Run(() => ProjectCheck.Run(chain, progress.Report)));
    }

    internal Task<Outcome<PapyrusEvents.Index>> ScanPapyrus(
        string folder,
        long revision,
        Func<string, Task<PapyrusEvents.Index>>? runner = null)
    {
        ArgumentNullException.ThrowIfNull(folder);

        long operation = Interlocked.Increment(ref _papyrusOperation);
        bool Current() => IsCurrent(revision, operation, ref _papyrusOperation);
        return Run(Current, () => runner != null
            ? runner(folder)
            : Task.Run(() => PapyrusEvents.Scan(folder)));
    }

    private bool IsCurrent(long revision, long operation, ref long latestOperation) =>
        revision == _currentRevision() && operation == Volatile.Read(ref latestOperation);

    private static async Task<Outcome<T>> Run<T>(Func<bool> current, Func<Task<T>> work)
        where T : class
    {
        await Task.Yield();
        if (!current()) return new Outcome<T>(OutcomeState.Stale, null, null);

        try
        {
            T? value = await work();
            if (value == null)
                throw new InvalidOperationException("The analysis worker returned no result.");

            return current()
                ? new Outcome<T>(OutcomeState.Completed, value, null)
                : new Outcome<T>(OutcomeState.Stale, null, null);
        }
        catch (Exception error)
        {
            return current()
                ? new Outcome<T>(OutcomeState.Failed, null, error)
                : new Outcome<T>(OutcomeState.Stale, null, null);
        }
    }
}
