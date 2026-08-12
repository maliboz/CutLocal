using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CutLocal.Contracts;
using CutLocal.Domain;
using CutLocal.Inference;
using CutLocal.Persistence;
using Microsoft.Extensions.Logging;

namespace CutLocal.Infrastructure;

/// <summary>Owns HTTPS model transfers, verification, quarantine, deletion, repair, and local import.</summary>
public sealed class ModelPackageManager : IModelPackageManager
{
    private const int CopyBufferSize = 128 * 1024;
    private const int MaximumQuarantineFilesPerModel = 3;
    private static readonly Action<ILogger, string, string, long, Exception?> LogInstalled =
        LoggerMessage.Define<string, string, long>(
            LogLevel.Information,
            new EventId(4101, nameof(LogInstalled)),
            "Installed verified model {ModelId} version {ModelVersion} ({Bytes} bytes)");
    private static readonly Action<ILogger, string, string, Exception?> LogPaused =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(4102, nameof(LogPaused)),
            "Paused model transfer for {ModelId} version {ModelVersion}");
    private static readonly Action<ILogger, string, string, Exception?> LogDownloadFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(4103, nameof(LogDownloadFailed)),
            "Model download failed for {ModelId} version {ModelVersion}");
    private static readonly Action<ILogger, string, string, Exception?> LogDeleted =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(4104, nameof(LogDeleted)),
            "Deleted local model package {ModelId} version {ModelVersion}");
    private static readonly Action<ILogger, string, string, Exception?> LogRejectedImport =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(4105, nameof(LogRejectedImport)),
            "Rejected incompatible custom model {ModelId} version {ModelVersion}");
    private static readonly Action<ILogger, string, string, Exception?> LogImported =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(4106, nameof(LogImported)),
            "Imported custom model {ModelId} version {ModelVersion}");
    private static readonly Action<ILogger, string, string, string, Exception?> LogQuarantined =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            new EventId(4107, nameof(LogQuarantined)),
            "Quarantined invalid model content for {ModelId} version {ModelVersion}; reason {Reason}");
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly HttpClient _httpClient;
    private readonly IModelCatalog _catalog;
    private readonly IModelManifestValidator _manifestValidator;
    private readonly IModelCompatibilityValidator _compatibilityValidator;
    private readonly IModelPathResolver _pathResolver;
    private readonly IModelAdapterSessionCache _sessionCache;
    private readonly ApplicationPaths _paths;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ModelPackageManager> _logger;

    /// <summary>Initializes the controlled model package store.</summary>
    public ModelPackageManager(
        HttpClient httpClient,
        IModelCatalog catalog,
        IModelManifestValidator manifestValidator,
        IModelCompatibilityValidator compatibilityValidator,
        IModelPathResolver pathResolver,
        IModelAdapterSessionCache sessionCache,
        ApplicationPaths paths,
        TimeProvider timeProvider,
        ILogger<ModelPackageManager> logger)
    {
        _httpClient = httpClient;
        _catalog = catalog;
        _manifestValidator = manifestValidator;
        _compatibilityValidator = compatibilityValidator;
        _pathResolver = pathResolver;
        _sessionCache = sessionCache;
        _paths = paths;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ModelInstallationInfo>> InspectAllAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ModelDescriptor> descriptors = await _catalog.GetAllAsync(cancellationToken);
        List<ModelInstallationInfo> result = new(descriptors.Count);
        foreach (ModelDescriptor descriptor in descriptors)
        {
            result.Add(await InspectAsync(descriptor, cancellationToken));
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<ModelPackageOperationResult> DownloadAsync(
        ModelDescriptor descriptor,
        IProgress<ModelTransferProgress>? progress,
        CancellationToken cancellationToken,
        bool licenseAcknowledged = false)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        await EnsureReviewedDownloadAsync(descriptor, cancellationToken);
        if (!descriptor.License.CommercialUseAllowed && !licenseAcknowledged)
        {
            return Failure("MODEL_LICENSE_ACK_REQUIRED", ModelInstallationState.NotInstalled);
        }

        ModelInstallationInfo state = await InspectAsync(descriptor, cancellationToken);
        if (state.State == ModelInstallationState.Installed)
        {
            return Success("MODEL_ALREADY_INSTALLED", ModelInstallationState.Installed);
        }

        string finalPath = _pathResolver.GetModelPath(descriptor);
        string partialPath = GetPartialPath(finalPath);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        if (state.State == ModelInstallationState.Corrupted)
        {
            await _sessionCache.InvalidateAsync(finalPath, cancellationToken);
            Quarantine(finalPath, descriptor, "installed");
        }

        if (File.Exists(partialPath) && new FileInfo(partialPath).Length > descriptor.FileSizeBytes)
        {
            Quarantine(partialPath, descriptor, "oversized-partial");
        }

        try
        {
            await DownloadToPartialAsync(descriptor, partialPath, progress, cancellationToken);
            if (!File.Exists(partialPath)
                || new FileInfo(partialPath).Length != descriptor.FileSizeBytes)
            {
                return Failure("MODEL_DOWNLOAD_INCOMPLETE", ModelInstallationState.Partial);
            }

            if (!await HasExpectedHashAsync(partialPath, descriptor.Sha256, cancellationToken))
            {
                Quarantine(partialPath, descriptor, "sha256");
                return Failure("MODEL_SHA256_MISMATCH", ModelInstallationState.Corrupted);
            }

            await _sessionCache.InvalidateAsync(finalPath, cancellationToken);
            File.Move(partialPath, finalPath, overwrite: true);
            LogInstalled(
                _logger,
                descriptor.Id,
                descriptor.Version,
                descriptor.FileSizeBytes,
                null);
            return Success("MODEL_INSTALLED", ModelInstallationState.Installed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogPaused(_logger, descriptor.Id, descriptor.Version, null);
            throw;
        }
        catch (HttpRequestException exception)
        {
            LogDownloadFailed(_logger, descriptor.Id, descriptor.Version, exception);
            return Failure(
                "MODEL_DOWNLOAD_FAILED",
                File.Exists(partialPath) ? ModelInstallationState.Partial : ModelInstallationState.NotInstalled);
        }
        catch (InvalidDataException exception)
        {
            if (File.Exists(partialPath))
            {
                Quarantine(partialPath, descriptor, "invalid-response");
            }

            LogDownloadFailed(_logger, descriptor.Id, descriptor.Version, exception);
            return Failure("MODEL_DOWNLOAD_INVALID", ModelInstallationState.Corrupted);
        }
    }

    /// <inheritdoc />
    public async ValueTask<ModelPackageOperationResult> RepairAsync(
        ModelDescriptor descriptor,
        IProgress<ModelTransferProgress>? progress,
        CancellationToken cancellationToken,
        bool licenseAcknowledged = false)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        await EnsureReviewedDownloadAsync(descriptor, cancellationToken);
        if (!descriptor.License.CommercialUseAllowed && !licenseAcknowledged)
        {
            return Failure("MODEL_LICENSE_ACK_REQUIRED", ModelInstallationState.NotInstalled);
        }

        ModelInstallationInfo state = await InspectAsync(descriptor, cancellationToken);
        if (state.State == ModelInstallationState.Installed)
        {
            return Success("MODEL_HEALTHY", ModelInstallationState.Installed);
        }

        string finalPath = _pathResolver.GetModelPath(descriptor);
        if (File.Exists(finalPath))
        {
            await _sessionCache.InvalidateAsync(finalPath, cancellationToken);
            Quarantine(finalPath, descriptor, "repair");
        }

        return await DownloadAsync(descriptor, progress, cancellationToken, licenseAcknowledged);
    }

    /// <inheritdoc />
    public async ValueTask<ModelPackageOperationResult> DeleteAsync(
        ModelDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        string finalPath = _pathResolver.GetModelPath(descriptor);
        await _sessionCache.InvalidateAsync(finalPath, cancellationToken);
        DeleteIfExists(finalPath);
        DeleteIfExists(GetPartialPath(finalPath));

        if (IsUserSupplied(descriptor))
        {
            DeleteIfExists(GetCustomManifestPath(descriptor));
            DeleteIfExists(GetCustomReceiptPath(descriptor));
        }

        LogDeleted(_logger, descriptor.Id, descriptor.Version, null);
        return Success("MODEL_DELETED", ModelInstallationState.NotInstalled);
    }

    /// <inheritdoc />
    public async ValueTask<ModelPackageOperationResult> ImportAsync(
        ModelImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.LicenseAcknowledged)
        {
            return Failure("MODEL_LICENSE_ACK_REQUIRED", ModelInstallationState.NotInstalled);
        }

        string onnxPath = Path.GetFullPath(request.OnnxPath);
        string manifestPath = Path.GetFullPath(request.ManifestPath);
        if (!File.Exists(onnxPath)
            || !Path.GetExtension(onnxPath).Equals(".onnx", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(manifestPath)
            || !Path.GetExtension(manifestPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return Failure("MODEL_IMPORT_FILES_INVALID", ModelInstallationState.NotInstalled);
        }

        if (new FileInfo(manifestPath).Length is <= 0 or > 1024 * 1024)
        {
            return Failure("MODEL_IMPORT_MANIFEST_INVALID", ModelInstallationState.NotInstalled);
        }

        ModelDescriptor? descriptor;
        await using (FileStream stream = OpenRead(manifestPath))
        {
            descriptor = await JsonSerializer.DeserializeAsync<ModelDescriptor>(
                stream,
                SerializerOptions,
                cancellationToken);
        }

        if (descriptor is null
            || _manifestValidator.Validate(descriptor, commercialBuild: false).Count != 0)
        {
            return Failure("MODEL_IMPORT_MANIFEST_INVALID", ModelInstallationState.NotInstalled);
        }

        ModelDescriptor? existing = await _catalog.GetByIdAsync(descriptor.Id, cancellationToken);
        if (existing is not null)
        {
            return Failure("MODEL_IMPORT_ID_EXISTS", ModelInstallationState.NotInstalled);
        }

        FileInfo sourceInfo = new(onnxPath);
        if (sourceInfo.Length != descriptor.FileSizeBytes)
        {
            return Failure("MODEL_IMPORT_SIZE_MISMATCH", ModelInstallationState.Corrupted);
        }

        if (!await HasExpectedHashAsync(onnxPath, descriptor.Sha256, cancellationToken))
        {
            return Failure("MODEL_IMPORT_SHA256_MISMATCH", ModelInstallationState.Corrupted);
        }

        try
        {
            await _compatibilityValidator.ValidateAsync(descriptor, onnxPath, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogRejectedImport(_logger, descriptor.Id, descriptor.Version, exception);
            return Failure("MODEL_IMPORT_INCOMPATIBLE", ModelInstallationState.NotInstalled);
        }

        string finalPath = _pathResolver.GetModelPath(descriptor);
        string importingPath = finalPath + ".importing";
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        DeleteIfExists(importingPath);
        try
        {
            await CopyFileAsync(onnxPath, importingPath, descriptor.FileSizeBytes, cancellationToken);
            if (!await HasExpectedHashAsync(importingPath, descriptor.Sha256, cancellationToken))
            {
                Quarantine(importingPath, descriptor, "import-sha256");
                return Failure("MODEL_IMPORT_COPY_MISMATCH", ModelInstallationState.Corrupted);
            }

            File.Move(importingPath, finalPath, overwrite: false);
            await WriteCustomManifestAndReceiptAsync(descriptor, cancellationToken);
            LogImported(_logger, descriptor.Id, descriptor.Version, null);
            return Success("MODEL_IMPORTED", ModelInstallationState.Installed);
        }
        catch
        {
            DeleteIfExists(importingPath);
            if (!File.Exists(GetCustomReceiptPath(descriptor)))
            {
                DeleteIfExists(finalPath);
            }

            throw;
        }
    }

    private async ValueTask<ModelInstallationInfo> InspectAsync(
        ModelDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        string finalPath = _pathResolver.GetModelPath(descriptor);
        string partialPath = GetPartialPath(finalPath);
        if (File.Exists(finalPath))
        {
            FileInfo info = new(finalPath);
            bool verified = info.Length == descriptor.FileSizeBytes
                && await HasExpectedHashAsync(finalPath, descriptor.Sha256, cancellationToken);
            return new ModelInstallationInfo
            {
                Descriptor = descriptor,
                State = verified ? ModelInstallationState.Installed : ModelInstallationState.Corrupted,
                LocalBytes = info.Length,
                IsUserSupplied = IsUserSupplied(descriptor),
            };
        }

        long partialBytes = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        return new ModelInstallationInfo
        {
            Descriptor = descriptor,
            State = partialBytes > 0 ? ModelInstallationState.Partial : ModelInstallationState.NotInstalled,
            LocalBytes = partialBytes,
            IsUserSupplied = IsUserSupplied(descriptor),
        };
    }

    private async Task DownloadToPartialAsync(
        ModelDescriptor descriptor,
        string partialPath,
        IProgress<ModelTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        long offset = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        using HttpResponseMessage response = await SendDownloadRequestAsync(
            descriptor,
            offset,
            cancellationToken);

        if (offset > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            ContentRangeHeaderValue? range = response.Content.Headers.ContentRange;
            if (offset == descriptor.FileSizeBytes && range?.Length == descriptor.FileSizeBytes)
            {
                return;
            }

            throw new HttpRequestException("The server rejected an incomplete model range.");
        }

        bool append = offset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (offset > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            response.Dispose();
            DeleteIfExists(partialPath);
            using HttpResponseMessage restarted = await SendDownloadRequestAsync(
                descriptor,
                offset: 0,
                cancellationToken);
            await StreamResponseAsync(
                restarted,
                partialPath,
                descriptor.FileSizeBytes,
                startingBytes: 0,
                append: false,
                progress,
                cancellationToken);
            return;
        }

        if (append)
        {
            ContentRangeHeaderValue? range = response.Content.Headers.ContentRange;
            if (range?.From != offset || range.To is null || range.Length != descriptor.FileSizeBytes)
            {
                throw new HttpRequestException("The server returned an invalid Content-Range response.");
            }
        }
        else if (offset > 0)
        {
            throw new HttpRequestException("The server did not honor the model range request.");
        }

        await StreamResponseAsync(
            response,
            partialPath,
            descriptor.FileSizeBytes,
            offset,
            append,
            progress,
            cancellationToken);
    }

    private async Task<HttpResponseMessage> SendDownloadRequestAsync(
        ModelDescriptor descriptor,
        long offset,
        CancellationToken cancellationToken)
    {
        Uri currentUri = new(descriptor.DownloadUrl, UriKind.Absolute);
        for (int redirect = 0; redirect <= 5; redirect++)
        {
            if (!currentUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new HttpRequestException("The model download redirected outside HTTPS.");
            }

            using HttpRequestMessage request = new(HttpMethod.Get, currentUri);
            if (offset > 0)
            {
                request.Headers.Range = new RangeHeaderValue(offset, null);
            }

            HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!IsRedirect(response.StatusCode))
            {
                if (response.StatusCode != HttpStatusCode.OK
                    && response.StatusCode != HttpStatusCode.PartialContent
                    && response.StatusCode != HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    response.EnsureSuccessStatusCode();
                }

                return response;
            }

            Uri? location = response.Headers.Location;
            response.Dispose();
            if (location is null)
            {
                throw new HttpRequestException("The model redirect did not include a target URI.");
            }

            currentUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
        }

        throw new HttpRequestException("The model download exceeded the redirect limit.");
    }

    private static async Task StreamResponseAsync(
        HttpResponseMessage response,
        string partialPath,
        long expectedBytes,
        long startingBytes,
        bool append,
        IProgress<ModelTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        long expectedResponseBytes = expectedBytes - startingBytes;
        if (response.Content.Headers.ContentLength is long contentLength
            && contentLength != expectedResponseBytes)
        {
            throw new HttpRequestException("The model response length does not match the manifest.");
        }

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream destination = new(
            partialPath,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[CopyBufferSize];
        long received = startingBytes;
        while (true)
        {
            int count = await source.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                break;
            }

            received = checked(received + count);
            if (received > expectedBytes)
            {
                throw new InvalidDataException("The model response exceeded its declared package size.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            progress?.Report(new ModelTransferProgress
            {
                BytesReceived = received,
                TotalBytes = expectedBytes,
            });
        }

        await destination.FlushAsync(cancellationToken);
    }

    private async Task WriteCustomManifestAndReceiptAsync(
        ModelDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.CustomManifestRoot);
        string manifestPath = GetCustomManifestPath(descriptor);
        string manifestTemporary = manifestPath + ".tmp";
        await using (FileStream stream = new(
                         manifestTemporary,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         16 * 1024,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, descriptor, SerializerOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(manifestTemporary, manifestPath, overwrite: true);
        string receiptPath = GetCustomReceiptPath(descriptor);
        string receiptTemporary = receiptPath + ".tmp";
        string receipt = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"acceptedUtc={_timeProvider.GetUtcNow():O}\nsha256={descriptor.Sha256}\nlicense={descriptor.License.Spdx}\n");
        await File.WriteAllTextAsync(receiptTemporary, receipt, Encoding.UTF8, cancellationToken);
        File.Move(receiptTemporary, receiptPath, overwrite: true);
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        long expectedBytes,
        CancellationToken cancellationToken)
    {
        await using FileStream source = OpenRead(sourcePath);
        await using FileStream destination = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, CopyBufferSize, cancellationToken);
        await destination.FlushAsync(cancellationToken);
        if (destination.Length != expectedBytes)
        {
            throw new InvalidDataException("The imported model changed while it was being copied.");
        }
    }

    private async ValueTask EnsureReviewedDownloadAsync(
        ModelDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> errors = _manifestValidator.Validate(descriptor, commercialBuild: false);
        if (errors.Count != 0)
        {
            throw new InvalidDataException(
                $"The model manifest is not approved for catalog download: {string.Join("; ", errors)}");
        }

        ModelDescriptor? catalogDescriptor = await _catalog.GetByIdAsync(
            descriptor.Id,
            cancellationToken);
        if (catalogDescriptor is null || !DescriptorsMatch(catalogDescriptor, descriptor))
        {
            throw new InvalidDataException("The requested model does not match the local reviewed catalog.");
        }
    }

    private static bool DescriptorsMatch(ModelDescriptor catalog, ModelDescriptor requested) =>
        catalog.Id.Equals(requested.Id, StringComparison.OrdinalIgnoreCase)
        && catalog.Version.Equals(requested.Version, StringComparison.Ordinal)
        && catalog.FileName.Equals(requested.FileName, StringComparison.Ordinal)
        && catalog.Sha256.Equals(requested.Sha256, StringComparison.OrdinalIgnoreCase)
        && catalog.FileSizeBytes == requested.FileSizeBytes
        && catalog.DownloadUrl.Equals(requested.DownloadUrl, StringComparison.Ordinal)
        && catalog.License.Spdx.Equals(requested.License.Spdx, StringComparison.Ordinal)
        && catalog.License.CommercialUseAllowed == requested.License.CommercialUseAllowed
        && catalog.License.AttributionRequired == requested.License.AttributionRequired
        && catalog.License.Source.Equals(requested.License.Source, StringComparison.Ordinal)
        && catalog.Input.Width == requested.Input.Width
        && catalog.Input.Height == requested.Input.Height
        && catalog.Input.Layout.Equals(requested.Input.Layout, StringComparison.OrdinalIgnoreCase)
        && catalog.Input.ColorOrder.Equals(requested.Input.ColorOrder, StringComparison.OrdinalIgnoreCase)
        && catalog.Input.Mean.SequenceEqual(requested.Input.Mean)
        && catalog.Input.Std.SequenceEqual(requested.Input.Std)
        && catalog.Input.ResizeMode.Equals(requested.Input.ResizeMode, StringComparison.OrdinalIgnoreCase)
        && string.Equals(catalog.Input.NodeName, requested.Input.NodeName, StringComparison.Ordinal)
        && catalog.Output.Activation.Equals(requested.Output.Activation, StringComparison.OrdinalIgnoreCase)
        && catalog.Output.Type.Equals(requested.Output.Type, StringComparison.OrdinalIgnoreCase)
        && string.Equals(catalog.Output.NodeName, requested.Output.NodeName, StringComparison.Ordinal)
        && catalog.RecommendedMemoryMb == requested.RecommendedMemoryMb
        && catalog.Tier.Equals(requested.Tier, StringComparison.OrdinalIgnoreCase)
        && catalog.SupportedProviders.SequenceEqual(
            requested.SupportedProviders,
            StringComparer.OrdinalIgnoreCase);

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found
        or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    private void Quarantine(string path, ModelDescriptor descriptor, string reason)
    {
        if (!File.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(_paths.ModelQuarantineRoot);
        string safeModelId = SafeFileNameSegment(descriptor.Id);
        string safeVersion = SafeFileNameSegment(descriptor.Version);
        string safeReason = SafeFileNameSegment(reason);
        string prefix = $"{safeModelId}-{safeVersion}-";
        string name = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{prefix}{_timeProvider.GetUtcNow():yyyyMMddHHmmssfff}-{safeReason}-{Guid.NewGuid():N}.onnx");
        File.Move(path, Path.Combine(_paths.ModelQuarantineRoot, name));
        PruneQuarantine(prefix);
        LogQuarantined(_logger, descriptor.Id, descriptor.Version, reason, null);
    }

    private void PruneQuarantine(string prefix)
    {
        FileInfo[] stale = new DirectoryInfo(_paths.ModelQuarantineRoot)
            .EnumerateFiles("*.onnx", SearchOption.TopDirectoryOnly)
            .Where(file => file.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.CreationTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Skip(MaximumQuarantineFilesPerModel)
            .ToArray();
        foreach (FileInfo file in stale)
        {
            try
            {
                file.Delete();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Retention is best effort; the quarantined evidence must remain available.
            }
        }
    }

    private static string SafeFileNameSegment(string value)
    {
        string safe = string.Concat(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '_'));
        return string.IsNullOrWhiteSpace(safe) ? "model" : safe;
    }

    private static async ValueTask<bool> HasExpectedHashAsync(
        string path,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenRead(path);
        byte[] digest = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(digest).Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsUserSupplied(ModelDescriptor descriptor) =>
        !string.IsNullOrWhiteSpace(_paths.CustomManifestRoot)
        && File.Exists(GetCustomReceiptPath(descriptor));

    private string GetCustomManifestPath(ModelDescriptor descriptor) =>
        Path.Combine(_paths.CustomManifestRoot, $"{descriptor.Id}.{descriptor.Version}.json");

    private string GetCustomReceiptPath(ModelDescriptor descriptor) =>
        Path.Combine(_paths.CustomManifestRoot, $"{descriptor.Id}.{descriptor.Version}.accepted");

    private static string GetPartialPath(string finalPath) => finalPath + ".partial";

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        CopyBufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static ModelPackageOperationResult Success(
        string code,
        ModelInstallationState state) => new()
        {
            Succeeded = true,
            Code = code,
            State = state,
        };

    private static ModelPackageOperationResult Failure(
        string code,
        ModelInstallationState state) => new()
        {
            Succeeded = false,
            Code = code,
            State = state,
        };
}
