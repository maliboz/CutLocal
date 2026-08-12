using CutLocal.Domain;

namespace CutLocal.Application;

/// <summary>Resets only failed items for an explicit retry attempt.</summary>
public sealed class RetryFailedItemsUseCase(TimeProvider timeProvider)
{
    /// <summary>Returns a queued snapshot containing reset failed items.</summary>
    public ProcessingJob Execute(ProcessingJob job, ProcessingPreset preset)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(preset);
        if (job.Status is JobStatus.Running or JobStatus.Paused)
        {
            throw new InvalidOperationException("Failed items cannot be reset while the batch is active.");
        }

        bool changed = false;
        ProcessingItem[] items = job.Items.Select(item =>
        {
            if (item.Status != ItemStatus.Failed)
            {
                return item;
            }

            changed = true;
            return item with
            {
                Status = ItemStatus.Queued,
                Progress = 0,
                Elapsed = null,
                Error = null,
                ProviderId = null,
                UsedCpuFallback = false,
            };
        }).ToArray();
        return changed
            ? job with
            {
                UpdatedAtUtc = timeProvider.GetUtcNow(),
                Status = JobStatus.Queued,
                Preset = preset,
                Items = items,
            }
            : job;
    }
}
