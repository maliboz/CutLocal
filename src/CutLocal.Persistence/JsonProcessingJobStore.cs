using System.Text.Json;
using CutLocal.Contracts;
using CutLocal.Domain;
using Microsoft.Extensions.Logging;

namespace CutLocal.Persistence;

/// <summary>Atomically stores the last bounded batch snapshot under local app data.</summary>
public sealed class JsonProcessingJobStore : IProcessingJobStore, IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumItems = 10_000;
    private const long MaximumDocumentBytes = 32L * 1024 * 1024;
    private static readonly Action<ILogger, Exception?> LogReadFailure =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(4201, nameof(LogReadFailure)),
            "The last batch snapshot could not be read; recovery was skipped");
    private static readonly Action<ILogger, Exception?> LogWriteFailure =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(4202, nameof(LogWriteFailure)),
            "The last batch snapshot could not be saved");

    private readonly string _jobPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<JsonProcessingJobStore> _logger;
    private bool _disposed;

    /// <summary>Initializes the controlled last-job store.</summary>
    public JsonProcessingJobStore(
        ApplicationPaths paths,
        ILogger<JsonProcessingJobStore> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _logger = logger;
        _jobPath = Path.Combine(paths.DataRoot, "jobs", "last-job.json");
    }

    /// <inheritdoc />
    public async ValueTask<ProcessingJob?> LoadLastAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_jobPath))
            {
                return null;
            }

            FileInfo information = new(_jobPath);
            if (information.Length is <= 0 or > MaximumDocumentBytes)
            {
                return null;
            }

            await using FileStream stream = new(
                _jobPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            ProcessingJob? job = await JsonSerializer.DeserializeAsync(
                    stream,
                    PersistenceJsonContext.Default.ProcessingJob,
                    cancellationToken)
                .ConfigureAwait(false);
            return job is not null && IsValid(job) ? job : null;
        }
        catch (Exception exception) when (exception is JsonException
            or IOException
            or UnauthorizedAccessException)
        {
            LogReadFailure(_logger, exception);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(job);
        if (!IsValid(job))
        {
            throw new InvalidDataException("The batch snapshot failed durability validation.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporaryPath = $"{_jobPath}.{Guid.NewGuid():N}.partial";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_jobPath)!);
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        job,
                        PersistenceJsonContext.Default.ProcessingJob,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _jobPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogWriteFailure(_logger, exception);
            throw;
        }
        finally
        {
            TryDelete(temporaryPath);
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            File.Delete(_jobPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private static bool IsValid(ProcessingJob job)
    {
        if (job.SchemaVersion != CurrentSchemaVersion
            || job.Id == Guid.Empty
            || !Enum.IsDefined(job.Status)
            || job.Items.Count > MaximumItems
            || string.IsNullOrWhiteSpace(job.Preset.ModelId)
            || job.Preset.Concurrency is < 1 or > 2
            || job.Preset.Output.Format != OutputFormat.Png
            || string.IsNullOrWhiteSpace(job.Preset.Output.OutputDirectory)
            || !Path.IsPathFullyQualified(job.Preset.Output.OutputDirectory))
        {
            return false;
        }

        HashSet<Guid> identities = [];
        HashSet<string> inputs = new(StringComparer.OrdinalIgnoreCase);
        foreach (ProcessingItem item in job.Items)
        {
            if (item.Id == Guid.Empty
                || !identities.Add(item.Id)
                || string.IsNullOrWhiteSpace(item.InputPath)
                || string.IsNullOrWhiteSpace(item.OutputPath)
                || !Path.IsPathFullyQualified(item.InputPath)
                || !Path.IsPathFullyQualified(item.OutputPath)
                || !inputs.Add(item.InputPath)
                || !Enum.IsDefined(item.Status)
                || !double.IsFinite(item.Progress)
                || item.Progress is < 0 or > 1
                || item.AttemptCount < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A future write uses a unique partial name and cannot commit this file.
        }
        catch (UnauthorizedAccessException)
        {
            // A future write uses a unique partial name and cannot commit this file.
        }
    }
}
