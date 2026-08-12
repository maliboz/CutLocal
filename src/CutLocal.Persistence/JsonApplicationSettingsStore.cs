using System.Text.Json;
using CutLocal.Contracts;
using CutLocal.Domain;
using Microsoft.Extensions.Logging;

namespace CutLocal.Persistence;

/// <summary>Stores a small settings document under the controlled per-user data root.</summary>
public sealed class JsonApplicationSettingsStore : IApplicationSettingsStore, IDisposable
{
    private const long MaximumSettingsBytes = 64 * 1024;
    private static readonly Action<ILogger, Exception?> LogReadFailure =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(4101, nameof(LogReadFailure)),
            "Application settings could not be read; defaults will be used");
    private static readonly Action<ILogger, Exception?> LogWriteFailure =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(4102, nameof(LogWriteFailure)),
            "Application settings could not be saved");

    private readonly string _settingsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<JsonApplicationSettingsStore> _logger;
    private bool _disposed;

    /// <summary>Initializes the JSON settings store.</summary>
    public JsonApplicationSettingsStore(
        ApplicationPaths paths,
        ILogger<JsonApplicationSettingsStore> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _logger = logger;
        _settingsPath = Path.Combine(paths.DataRoot, "settings.json");
    }

    /// <inheritdoc />
    public async ValueTask<ApplicationSettings> LoadAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new ApplicationSettings();
            }

            FileInfo information = new(_settingsPath);
            if (information.Length is <= 0 or > MaximumSettingsBytes)
            {
                return new ApplicationSettings();
            }

            await using FileStream stream = new(
                _settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<ApplicationSettings>(
                    stream,
                    PersistenceJsonContext.Default.ApplicationSettings,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? new ApplicationSettings();
        }
        catch (Exception exception) when (exception is JsonException
            or IOException
            or UnauthorizedAccessException)
        {
            LogReadFailure(_logger, exception);
            return new ApplicationSettings();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(
        ApplicationSettings settings,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        settings,
                        PersistenceJsonContext.Default.ApplicationSettings,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogWriteFailure(_logger, exception);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
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

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A unique temp file is used on the next save.
        }
        catch (UnauthorizedAccessException)
        {
            // A unique temp file is used on the next save.
        }
    }
}
