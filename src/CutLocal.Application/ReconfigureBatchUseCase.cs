using CutLocal.Domain;

namespace CutLocal.Application;

/// <summary>Applies the latest preset and rebases outputs that have not committed.</summary>
public sealed class ReconfigureBatchUseCase(TimeProvider timeProvider)
{
    /// <summary>Updates queued, failed, and cancelled item destinations before execution.</summary>
    public ProcessingJob Execute(ProcessingJob job, ProcessingPreset preset)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(preset);
        if (job.Status is JobStatus.Running or JobStatus.Paused)
        {
            throw new InvalidOperationException("An active batch cannot be reconfigured.");
        }

        HashSet<string> reserved = new(
            job.Items
                .Where(item => item.Status is ItemStatus.Completed or ItemStatus.Skipped)
                .Select(item => Path.GetFullPath(item.OutputPath)),
            StringComparer.OrdinalIgnoreCase);
        ProcessingItem[] items = job.Items.Select(item =>
        {
            if (item.Status is ItemStatus.Completed or ItemStatus.Skipped)
            {
                return item;
            }

            string requested = OutputPathPolicy.CreatePngPath(
                item.InputPath,
                preset.Output.OutputDirectory,
                preset.Output.FileNameSuffix);
            return item with { OutputPath = Reserve(requested, reserved) };
        }).ToArray();
        return job with
        {
            UpdatedAtUtc = timeProvider.GetUtcNow(),
            Preset = preset,
            Items = items,
        };
    }

    private static string Reserve(string requestedPath, HashSet<string> reserved)
    {
        string fullPath = Path.GetFullPath(requestedPath);
        if (reserved.Add(fullPath))
        {
            return fullPath;
        }

        string directory = Path.GetDirectoryName(fullPath)!;
        string stem = Path.GetFileNameWithoutExtension(fullPath);
        string extension = Path.GetExtension(fullPath);
        for (int suffix = 2; suffix <= AddImagesUseCase.MaximumBatchItems; suffix++)
        {
            string candidate = Path.Combine(directory, $"{stem} ({suffix}){extension}");
            if (reserved.Add(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("A unique output name could not be reserved for the batch.");
    }
}
