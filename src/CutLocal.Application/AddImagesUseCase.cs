using CutLocal.Contracts;
using CutLocal.Domain;

namespace CutLocal.Application;

/// <summary>Adds canonical, unique PNG inputs to an immutable batch job snapshot.</summary>
public sealed class AddImagesUseCase(TimeProvider timeProvider)
{
    /// <summary>Gets the maximum number of items retained by one desktop batch.</summary>
    public const int MaximumBatchItems = 10_000;

    /// <summary>Adds valid files while ignoring duplicates and unsupported inputs.</summary>
    public AddInputsResult Execute(
        ProcessingJob? existingJob,
        ProcessingPreset preset,
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(paths);
        ValidateEditable(existingJob);
        ValidatePreset(preset);

        DateTimeOffset now = timeProvider.GetUtcNow();
        List<ProcessingItem> items = existingJob?.Items.ToList() ?? [];
        HashSet<string> knownInputs = new(
            items.Select(item => Path.GetFullPath(item.InputPath)),
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> reservedOutputs = new(
            items.Select(item => Path.GetFullPath(item.OutputPath)),
            StringComparer.OrdinalIgnoreCase);
        int added = 0;
        int duplicates = 0;
        int rejected = 0;

        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (items.Count >= MaximumBatchItems)
            {
                rejected++;
                continue;
            }

            if (!TryResolvePng(path, out string? inputPath))
            {
                rejected++;
                continue;
            }

            if (!knownInputs.Add(inputPath))
            {
                duplicates++;
                continue;
            }

            string requestedOutput = OutputPathPolicy.CreatePngPath(
                inputPath,
                preset.Output.OutputDirectory,
                preset.Output.FileNameSuffix);
            string outputPath = ReserveUniqueOutput(requestedOutput, reservedOutputs);
            items.Add(new ProcessingItem
            {
                Id = Guid.NewGuid(),
                InputPath = inputPath,
                OutputPath = outputPath,
                Status = ItemStatus.Queued,
                Progress = 0,
            });
            added++;
        }

        ProcessingJob job = new()
        {
            Id = existingJob?.Id ?? Guid.NewGuid(),
            CreatedAtUtc = existingJob?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now,
            Status = items.Any(item => item.Status == ItemStatus.Queued)
                ? JobStatus.Queued
                : existingJob?.Status ?? JobStatus.Queued,
            Preset = preset,
            Items = items,
        };
        return new AddInputsResult
        {
            Job = job,
            AddedCount = added,
            DuplicateCount = duplicates,
            RejectedCount = rejected,
        };
    }

    private static void ValidateEditable(ProcessingJob? job)
    {
        if (job?.Status is JobStatus.Running or JobStatus.Paused)
        {
            throw new InvalidOperationException("An active batch cannot be edited.");
        }
    }

    private static void ValidatePreset(ProcessingPreset preset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preset.ModelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(preset.Output.OutputDirectory);
        if (preset.Output.Format != OutputFormat.Png)
        {
            throw new NotSupportedException("Phase 4 batch processing currently supports RGBA PNG output.");
        }

        if (preset.Concurrency is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(preset), "Batch concurrency must be between one and two.");
        }
    }

    private static bool TryResolvePng(string? path, out string resolved)
    {
        resolved = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            resolved = Path.GetFullPath(path);
            return File.Exists(resolved)
                && Path.GetExtension(resolved).Equals(".png", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or NotSupportedException)
        {
            return false;
        }
    }

    private static string ReserveUniqueOutput(
        string requestedPath,
        HashSet<string> reservedOutputs)
    {
        string fullPath = Path.GetFullPath(requestedPath);
        if (reservedOutputs.Add(fullPath))
        {
            return fullPath;
        }

        string directory = Path.GetDirectoryName(fullPath)!;
        string stem = Path.GetFileNameWithoutExtension(fullPath);
        string extension = Path.GetExtension(fullPath);
        for (int suffix = 2; suffix <= MaximumBatchItems; suffix++)
        {
            string candidate = Path.Combine(directory, $"{stem} ({suffix}){extension}");
            if (reservedOutputs.Add(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("A unique output name could not be reserved for the batch.");
    }
}
