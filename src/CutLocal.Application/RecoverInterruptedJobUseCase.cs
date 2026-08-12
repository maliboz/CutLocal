using CutLocal.Contracts;
using CutLocal.Domain;

namespace CutLocal.Application;

/// <summary>Loads the last batch and safely requeues work interrupted by process exit.</summary>
public sealed class RecoverInterruptedJobUseCase(
    IProcessingJobStore store,
    TimeProvider timeProvider)
{
    /// <summary>Loads the last snapshot and marks active work as interrupted and queued.</summary>
    public async ValueTask<ProcessingJob?> ExecuteAsync(CancellationToken cancellationToken)
    {
        ProcessingJob? job = await store.LoadLastAsync(cancellationToken).ConfigureAwait(false);
        if (job is null || job.Status is not (
            JobStatus.Running or JobStatus.Paused or JobStatus.Interrupted))
        {
            return job;
        }

        ProcessingItem[] items = job.Items.Select(item => IsTerminal(item.Status)
            ? item
            : item with
            {
                Status = ItemStatus.Queued,
                Progress = 0,
                Elapsed = null,
                Error = null,
                ProviderId = null,
                UsedCpuFallback = false,
            }).ToArray();
        ProcessingJob recovered = job with
        {
            UpdatedAtUtc = timeProvider.GetUtcNow(),
            Status = JobStatus.Interrupted,
            Items = items,
        };
        await store.SaveAsync(recovered, cancellationToken).ConfigureAwait(false);
        return recovered;
    }

    private static bool IsTerminal(ItemStatus status) => status is
        ItemStatus.Completed or ItemStatus.Failed or ItemStatus.Skipped;
}
