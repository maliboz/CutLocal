using System.Security.Cryptography;
using CutLocal.Contracts;
using CutLocal.Domain;
using CutLocal.Persistence;
using Microsoft.Extensions.Logging;

namespace CutLocal.Infrastructure;

/// <summary>Seeds release-bundled models into the per-user model store without trusting package contents blindly.</summary>
public sealed class BundledModelSeeder : IBundledModelSeeder
{
    private const int CopyBufferSize = 128 * 1024;
    private static readonly Action<ILogger, string, string, Exception?> LogSeeded =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(4401, nameof(LogSeeded)),
            "Activated bundled model {ModelId} version {ModelVersion}");

    private readonly IModelCatalog _catalog;
    private readonly IModelPathResolver _pathResolver;
    private readonly ApplicationPaths _paths;
    private readonly ILogger<BundledModelSeeder> _logger;

    /// <summary>Initializes the bundled model activator.</summary>
    public BundledModelSeeder(
        IModelCatalog catalog,
        IModelPathResolver pathResolver,
        ApplicationPaths paths,
        ILogger<BundledModelSeeder> logger)
    {
        _catalog = catalog;
        _pathResolver = pathResolver;
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<int> SeedAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_paths.BundledModelRoot)
            || !Directory.Exists(_paths.BundledModelRoot))
        {
            return 0;
        }

        int seeded = 0;
        IReadOnlyList<ModelDescriptor> descriptors = await _catalog.GetAllAsync(cancellationToken);
        foreach (ModelDescriptor descriptor in descriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string bundledPath = GetBundledPath(descriptor);
            if (!File.Exists(bundledPath))
            {
                continue;
            }

            await ValidateAsync(bundledPath, descriptor, cancellationToken);
            string destinationPath = _pathResolver.GetModelPath(descriptor);
            if (File.Exists(destinationPath)
                && await IsValidAsync(destinationPath, descriptor, cancellationToken))
            {
                continue;
            }

            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrEmpty(destinationDirectory))
            {
                throw new InvalidDataException("The bundled model destination has no parent directory.");
            }

            Directory.CreateDirectory(destinationDirectory);
            string stagingPath = destinationPath + ".seeding";
            TryDelete(stagingPath);
            try
            {
                await CopyAsync(bundledPath, stagingPath, cancellationToken);
                await ValidateAsync(stagingPath, descriptor, cancellationToken);
                File.Move(stagingPath, destinationPath, overwrite: true);
            }
            finally
            {
                TryDelete(stagingPath);
            }

            seeded++;
            LogSeeded(_logger, descriptor.Id, descriptor.Version, null);
        }

        return seeded;
    }

    private string GetBundledPath(ModelDescriptor descriptor)
    {
        string root = Path.GetFullPath(_paths.BundledModelRoot);
        string candidate = Path.GetFullPath(Path.Combine(
            root,
            descriptor.Id,
            descriptor.Version,
            descriptor.FileName));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A bundled model path resolves outside the package model root.");
        }

        return candidate;
    }

    private static async ValueTask ValidateAsync(
        string path,
        ModelDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        if (!await IsValidAsync(path, descriptor, cancellationToken))
        {
            throw new InvalidDataException(
                $"Bundled model {descriptor.Id} version {descriptor.Version} failed size or SHA-256 verification.");
        }
    }

    private static async ValueTask<bool> IsValidAsync(
        string path,
        ModelDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        FileInfo information = new(path);
        if (information.Length != descriptor.FileSizeBytes)
        {
            return false;
        }

        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] digest = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(digest).Equals(
            descriptor.Sha256,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async ValueTask CopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using FileStream source = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream destination = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await source.CopyToAsync(destination, CopyBufferSize, cancellationToken);
        await destination.FlushAsync(cancellationToken);
        destination.Flush(flushToDisk: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A unique startup staging file cannot be activated after this attempt.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup must not replace the verification or copy failure.
        }
    }
}
