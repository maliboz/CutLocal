using CutLocal.Domain;

namespace CutLocal.Application;

/// <summary>Removes explicitly selected items while no batch execution is active.</summary>
public sealed class RemoveBatchItemsUseCase(TimeProvider timeProvider)
{
    /// <summary>Returns a snapshot without the requested item identities.</summary>
    public ProcessingJob Execute(ProcessingJob job, IEnumerable<Guid> itemIds)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(itemIds);
        if (job.Status is JobStatus.Running or JobStatus.Paused)
        {
            throw new InvalidOperationException("Items cannot be removed while the batch is active.");
        }

        HashSet<Guid> removed = itemIds.ToHashSet();
        ProcessingItem[] items = job.Items.Where(item => !removed.Contains(item.Id)).ToArray();
        if (items.Length == job.Items.Count)
        {
            return job;
        }

        return job with
        {
            UpdatedAtUtc = timeProvider.GetUtcNow(),
            Status = items.Any(item => item.Status == ItemStatus.Queued)
                ? JobStatus.Queued
                : ResolveTerminalStatus(items),
            Items = items,
        };
    }

    private static JobStatus ResolveTerminalStatus(IReadOnlyList<ProcessingItem> items) =>
        items.Any(item => item.Status == ItemStatus.Failed)
            ? JobStatus.CompletedWithErrors
            : JobStatus.Completed;
}
