using System.Text.Json;
using CutLocal.Contracts;
using CutLocal.Domain;
using Microsoft.Extensions.Logging;

namespace CutLocal.Persistence;

/// <summary>Loads model descriptors from local JSON files and fails closed on invalid entries.</summary>
public sealed class JsonModelCatalog : IModelCatalog
{
    private static readonly Action<ILogger, string, Exception?> LogMissingManifestDirectory =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2001, nameof(LogMissingManifestDirectory)),
            "Model manifest directory is absent: {ManifestRoot}");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ApplicationPaths _paths;
    private readonly IModelManifestValidator _validator;
    private readonly ILogger<JsonModelCatalog> _logger;

    /// <summary>Initializes the local catalog.</summary>
    public JsonModelCatalog(
        ApplicationPaths paths,
        IModelManifestValidator validator,
        ILogger<JsonModelCatalog> logger)
    {
        _paths = paths;
        _validator = validator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<ModelDescriptor?> GetByIdAsync(
        string modelId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        IReadOnlyList<ModelDescriptor> descriptors = await GetAllAsync(cancellationToken);
        return descriptors.FirstOrDefault(
            descriptor => descriptor.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ModelDescriptor>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        List<ModelDescriptor> descriptors = [];
        if (!Directory.Exists(_paths.ManifestRoot))
        {
            LogMissingManifestDirectory(_logger, _paths.ManifestRoot, null);
        }
        else
        {
            foreach (string manifestPath in Directory.EnumerateFiles(
                         _paths.ManifestRoot,
                         "*.json",
                         SearchOption.TopDirectoryOnly))
            {
                descriptors.Add(await ReadValidatedAsync(
                    manifestPath,
                    commercialBuild: false,
                    cancellationToken));
            }
        }

        if (!string.IsNullOrWhiteSpace(_paths.CustomManifestRoot)
            && Directory.Exists(_paths.CustomManifestRoot))
        {
            foreach (string manifestPath in Directory.EnumerateFiles(
                         _paths.CustomManifestRoot,
                         "*.json",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string receiptPath = Path.ChangeExtension(manifestPath, ".accepted");
                if (!File.Exists(receiptPath))
                {
                    continue;
                }

                ModelDescriptor descriptor = await ReadValidatedAsync(
                    manifestPath,
                    commercialBuild: false,
                    cancellationToken);
                if (descriptors.Any(item => item.Id.Equals(
                    descriptor.Id,
                    StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException(
                        $"Custom manifest '{Path.GetFileName(manifestPath)}' duplicates a catalog model id.");
                }

                descriptors.Add(descriptor);
            }
        }

        return descriptors;
    }

    private async ValueTask<ModelDescriptor> ReadValidatedAsync(
        string manifestPath,
        bool commercialBuild,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using FileStream stream = new(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        ModelDescriptor? descriptor = await JsonSerializer.DeserializeAsync<ModelDescriptor>(
            stream,
            SerializerOptions,
            cancellationToken);
        if (descriptor is null)
        {
            throw new InvalidDataException($"Manifest '{Path.GetFileName(manifestPath)}' is empty.");
        }

        IReadOnlyList<string> errors = _validator.Validate(descriptor, commercialBuild);
        if (errors.Count != 0)
        {
            throw new InvalidDataException(
                $"Manifest '{Path.GetFileName(manifestPath)}' is invalid: {string.Join("; ", errors)}");
        }

        return descriptor;
    }
}
