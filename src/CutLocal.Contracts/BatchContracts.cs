using CutLocal.Domain;

namespace CutLocal.Contracts;

/// <summary>Persists the most recent bounded batch job snapshot.</summary>
public interface IProcessingJobStore
{
    /// <summary>Loads the last valid snapshot, or null when none is available.</summary>
    ValueTask<ProcessingJob?> LoadLastAsync(CancellationToken cancellationToken);

    /// <summary>Atomically replaces the durable last-job snapshot.</summary>
    ValueTask SaveAsync(ProcessingJob job, CancellationToken cancellationToken);

    /// <summary>Deletes the durable snapshot after an explicit user clear action.</summary>
    ValueTask DeleteAsync(CancellationToken cancellationToken);
}

/// <summary>Summarizes one add-files or add-folder operation.</summary>
public sealed record AddInputsResult
{
    /// <summary>Gets the updated durable job snapshot.</summary>
    public required ProcessingJob Job { get; init; }
    /// <summary>Gets the number of newly queued PNG files.</summary>
    public required int AddedCount { get; init; }
    /// <summary>Gets the number of canonical path duplicates ignored.</summary>
    public required int DuplicateCount { get; init; }
    /// <summary>Gets the number of missing, unsupported, or over-limit inputs ignored.</summary>
    public required int RejectedCount { get; init; }
}

/// <summary>Reports an immutable item update and aggregate batch counters.</summary>
public sealed record BatchProgressUpdate
{
    /// <summary>Gets the owning job identity.</summary>
    public required Guid JobId { get; init; }
    /// <summary>Gets the latest immutable item snapshot when an item changed.</summary>
    public ProcessingItem? Item { get; init; }
    /// <summary>Gets the latest job status.</summary>
    public required JobStatus JobStatus { get; init; }
    /// <summary>Gets terminal item count.</summary>
    public required int TerminalCount { get; init; }
    /// <summary>Gets total item count.</summary>
    public required int TotalCount { get; init; }
    /// <summary>Gets aggregate zero-to-one progress.</summary>
    public required double OverallProgress { get; init; }
}
