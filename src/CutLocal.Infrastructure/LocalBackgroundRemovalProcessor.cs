using System.Buffers;
using System.Diagnostics;
using CutLocal.Contracts;
using CutLocal.Domain;
using CutLocal.Imaging;
using CutLocal.Inference;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace CutLocal.Infrastructure;

/// <summary>Runs the provider-aware image pipeline entirely inside the application process.</summary>
public sealed class LocalBackgroundRemovalProcessor : IRemoveBackgroundProcessor
{
    private readonly IModelCatalog _modelCatalog;
    private readonly IModelPathResolver _modelPathResolver;
    private readonly IModelAdapterSessionCache _adapterFactory;
    private readonly ProviderSelectionService _providerSelection;
    private readonly IImageDecoder _decoder;
    private readonly IMaskCompositor _compositor;
    private readonly IAtomicImageWriter _writer;
    private readonly ILogger<LocalBackgroundRemovalProcessor> _logger;

    /// <summary>Initializes the local processing pipeline.</summary>
    public LocalBackgroundRemovalProcessor(
        IModelCatalog modelCatalog,
        IModelPathResolver modelPathResolver,
        IModelAdapterSessionCache adapterFactory,
        ProviderSelectionService providerSelection,
        IImageDecoder decoder,
        IMaskCompositor compositor,
        IAtomicImageWriter writer,
        ILogger<LocalBackgroundRemovalProcessor> logger)
    {
        _modelCatalog = modelCatalog;
        _modelPathResolver = modelPathResolver;
        _adapterFactory = adapterFactory;
        _providerSelection = providerSelection;
        _decoder = decoder;
        _compositor = compositor;
        _writer = writer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<ProcessingResult> ProcessAsync(
        RemoveBackgroundRequest request,
        IProgress<ProcessingProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Stopwatch total = Stopwatch.StartNew();
        IModelAdapterLease? adapterLease = null;

        try
        {
            ValidatePaths(request);
            Report(progress, ItemStatus.PreparingModel, 0.05, "Status.PreparingModel");
            ModelDescriptor descriptor = await _modelCatalog.GetByIdAsync(
                    request.ModelId,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InferenceException(
                    ProcessingErrorCategory.ModelMissing,
                    "MODEL_MANIFEST_MISSING",
                    "The selected model manifest is not installed.");

            string modelPath = _modelPathResolver.GetModelPath(descriptor);
            IReadOnlyList<InferenceProviderDescriptor> candidates =
                await _providerSelection.GetCandidatesAsync(
                        descriptor,
                        request.Provider,
                        request.DirectMlAdapterIndex,
                        cancellationToken)
                    .ConfigureAwait(false);
            adapterLease = await AcquirePreferredAsync(
                    descriptor,
                    modelPath,
                    candidates,
                    cancellationToken)
                .ConfigureAwait(false);
            IBackgroundRemovalModelAdapter adapter = adapterLease.Adapter;

            Report(progress, ItemStatus.Decoding, 0.15, "Status.Decoding");
            Stopwatch stage = Stopwatch.StartNew();
            using DecodedImage image = await Task.Run(
                    () => _decoder.Decode(request.InputPath, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            PipelineLog.Decoded(
                _logger,
                image.Metadata.Width,
                image.Metadata.Height,
                stage.Elapsed.TotalMilliseconds);

            int tensorLength = checked(descriptor.Input.Width * descriptor.Input.Height * 3);
            using IMemoryOwner<float> inputOwner = MemoryPool<float>.Shared.Rent(tensorLength);
            Report(progress, ItemStatus.Preprocessing, 0.30, "Status.Preprocessing");
            stage.Restart();
            PreprocessedInput input = adapter.Preprocess(image, inputOwner);
            PipelineLog.Preprocessed(_logger, stage.Elapsed.TotalMilliseconds);

            Report(progress, ItemStatus.Inferring, 0.45, "Status.Inferring");
            stage.Restart();
            bool usedCpuFallback = false;
            MaskResult rawMask;
            try
            {
                rawMask = await adapter.RunAsync(input, cancellationToken).ConfigureAwait(false);
            }
            catch (InferenceException exception) when (
                GpuFallbackPolicy.ShouldRetryOnCpu(
                    adapter.Provider,
                    exception,
                    usedCpuFallback))
            {
                PipelineLog.CpuFallback(_logger, adapter.Provider.Id, exception.LogCode, exception);
                adapterLease.Invalidate();
                await adapterLease.DisposeAsync().ConfigureAwait(false);
                adapterLease = null;

                InferenceProviderDescriptor cpu = candidates.Single(
                    provider => provider.Kind == InferenceProviderKind.Cpu);
                adapterLease = await _adapterFactory.AcquireAsync(
                        descriptor,
                        modelPath,
                        cpu,
                        cancellationToken)
                    .ConfigureAwait(false);
                adapter = adapterLease.Adapter;
                input = adapter.Preprocess(image, inputOwner);
                rawMask = await adapter.RunAsync(input, cancellationToken).ConfigureAwait(false);
                usedCpuFallback = true;
            }

            using (rawMask)
            {
                PipelineLog.Inferred(
                    _logger,
                    adapter.Provider.Id,
                    descriptor.Id,
                    descriptor.Version,
                    stage.Elapsed.TotalMilliseconds);

                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, ItemStatus.Postprocessing, 0.70, "Status.Postprocessing");
                stage.Restart();
                using RefinedMask refinedMask = adapter.Postprocess(
                    rawMask,
                    image.Metadata,
                    request.MaskOptions);
                using SKBitmap composed = _compositor.Compose(image, refinedMask, cancellationToken);
                PipelineLog.Postprocessed(_logger, stage.Elapsed.TotalMilliseconds);

                Report(progress, ItemStatus.Encoding, 0.90, "Status.Encoding");
                stage.Restart();
                string committedPath = _writer.WritePng(
                    composed,
                    request.OutputPath,
                    request.ExistingOutputBehavior,
                    cancellationToken);
                PipelineLog.Encoded(_logger, stage.Elapsed.TotalMilliseconds);

                Report(progress, ItemStatus.Completed, 1.0, "Status.Completed");
                PipelineLog.Completed(
                    _logger,
                    total.Elapsed.TotalMilliseconds,
                    Environment.WorkingSet);
                return new ProcessingResult
                {
                    Outcome = ProcessingOutcome.Succeeded,
                    OutputPath = committedPath,
                    ProviderId = adapter.Provider.Id,
                    UsedCpuFallback = usedCpuFallback,
                };
            }
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            PipelineLog.Cancelled(_logger, exception);
            return FailureResult(
                ProcessingOutcome.Cancelled,
                ProcessingErrorCategory.Cancelled,
                "PROC_CANCELLED",
                "İşlem iptal edildi.",
                "Processing was cancelled.",
                retryable: true);
        }
        catch (InferenceException exception)
        {
            PipelineLog.InferenceFailure(_logger, exception.LogCode, exception);
            return FailureResult(
                ProcessingOutcome.Failed,
                exception.Category,
                exception.LogCode,
                TranslateTurkish(exception.Category),
                TranslateEnglish(exception.Category),
                IsRetryable(exception.Category));
        }
        catch (ImagingException exception)
        {
            PipelineLog.ImagingFailure(_logger, exception.LogCode, exception);
            return FailureResult(
                ProcessingOutcome.Failed,
                exception.Category,
                exception.LogCode,
                TranslateTurkish(exception.Category),
                TranslateEnglish(exception.Category),
                IsRetryable(exception.Category));
        }
        catch (OutOfMemoryException exception)
        {
            PipelineLog.UnexpectedFailure(_logger, exception);
            return FailureResult(
                ProcessingOutcome.Failed,
                ProcessingErrorCategory.ImageTooLarge,
                "PROC_MEMORY_PRESSURE",
                TranslateTurkish(ProcessingErrorCategory.ImageTooLarge),
                TranslateEnglish(ProcessingErrorCategory.ImageTooLarge),
                retryable: false);
        }
        catch (InvalidDataException exception)
        {
            PipelineLog.InvalidManifest(_logger, exception);
            return FailureResult(
                ProcessingOutcome.Failed,
                ProcessingErrorCategory.ModelCorrupted,
                "MODEL_MANIFEST_INVALID",
                TranslateTurkish(ProcessingErrorCategory.ModelCorrupted),
                TranslateEnglish(ProcessingErrorCategory.ModelCorrupted),
                retryable: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            PipelineLog.FileSystemFailure(_logger, exception);
            ProcessingErrorCategory category = exception is UnauthorizedAccessException
                ? ProcessingErrorCategory.PermissionDenied
                : ProcessingErrorCategory.FileLocked;
            return FailureResult(
                ProcessingOutcome.Failed,
                category,
                "PROC_FILESYSTEM",
                TranslateTurkish(category),
                TranslateEnglish(category),
                retryable: true);
        }
        catch (Exception exception)
        {
            PipelineLog.UnexpectedFailure(_logger, exception);
            return FailureResult(
                ProcessingOutcome.Failed,
                ProcessingErrorCategory.Unknown,
                "PROC_UNKNOWN",
                TranslateTurkish(ProcessingErrorCategory.Unknown),
                TranslateEnglish(ProcessingErrorCategory.Unknown),
                retryable: false);
        }
        finally
        {
            if (adapterLease is not null)
            {
                await adapterLease.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<IModelAdapterLease> AcquirePreferredAsync(
        ModelDescriptor descriptor,
        string modelPath,
        IReadOnlyList<InferenceProviderDescriptor> candidates,
        CancellationToken cancellationToken)
    {
        InferenceException? lastProviderFailure = null;
        foreach (InferenceProviderDescriptor candidate in candidates)
        {
            try
            {
                return await _adapterFactory.AcquireAsync(
                        descriptor,
                        modelPath,
                        candidate,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InferenceException exception) when (
                candidate.Kind != InferenceProviderKind.Cpu
                && exception.Category is ProcessingErrorCategory.ProviderUnavailable
                    or ProcessingErrorCategory.GpuOutOfMemory)
            {
                lastProviderFailure = exception;
                PipelineLog.ProviderInitializationFallback(
                    _logger,
                    candidate.Id,
                    exception.LogCode,
                    exception);
            }
        }

        throw lastProviderFailure ?? new InferenceException(
            ProcessingErrorCategory.ProviderUnavailable,
            "PROVIDER_INITIALIZATION_EXHAUSTED",
            "No provider could initialize the selected model.");
    }

    private static void ValidatePaths(RemoveBackgroundRequest request)
    {
        string input = Path.GetFullPath(request.InputPath);
        string output = Path.GetFullPath(request.OutputPath);
        if (input.Equals(output, StringComparison.OrdinalIgnoreCase))
        {
            throw new ImagingException(
                ProcessingErrorCategory.EncodeFailed,
                "ENC_INPUT_OUTPUT_COLLISION",
                "The output path cannot overwrite the active input.");
        }
    }

    private static ProcessingResult FailureResult(
        ProcessingOutcome outcome,
        ProcessingErrorCategory category,
        string logCode,
        string messageTr,
        string messageEn,
        bool retryable) => new()
        {
            Outcome = outcome,
            Error = new ProcessingError
            {
                Category = category,
                LogCode = logCode,
                MessageTr = messageTr,
                MessageEn = messageEn,
                IsRetryable = retryable,
            },
        };

    private static void Report(
        IProgress<ProcessingProgress>? progress,
        ItemStatus status,
        double value,
        string messageKey) => progress?.Report(new ProcessingProgress
        {
            Status = status,
            Value = value,
            MessageKey = messageKey,
        });

    private static bool IsRetryable(ProcessingErrorCategory category) => category is
        ProcessingErrorCategory.ProviderUnavailable
        or ProcessingErrorCategory.GpuOutOfMemory
        or ProcessingErrorCategory.FileLocked
        or ProcessingErrorCategory.DiskFull
        or ProcessingErrorCategory.Cancelled;

    private static string TranslateEnglish(ProcessingErrorCategory category) => category switch
    {
        ProcessingErrorCategory.UnsupportedFormat => "This image format is not supported.",
        ProcessingErrorCategory.DecodeFailed => "The image could not be decoded.",
        ProcessingErrorCategory.ImageTooLarge => "The image exceeds safe size limits.",
        ProcessingErrorCategory.ModelMissing => "The selected model is not installed.",
        ProcessingErrorCategory.ModelCorrupted => "The selected model is damaged or untrusted.",
        ProcessingErrorCategory.ModelIncompatible => "The model is incompatible with this application version.",
        ProcessingErrorCategory.ProviderUnavailable => "The selected inference provider is unavailable.",
        ProcessingErrorCategory.GpuOutOfMemory => "The GPU does not have enough free memory.",
        ProcessingErrorCategory.InferenceFailed => "Background removal inference failed.",
        ProcessingErrorCategory.PostprocessFailed => "The alpha mask could not be refined.",
        ProcessingErrorCategory.EncodeFailed => "The result could not be encoded.",
        ProcessingErrorCategory.DiskFull => "The destination disk is full.",
        ProcessingErrorCategory.PermissionDenied => "Access to the selected file or folder was denied.",
        ProcessingErrorCategory.FileLocked => "A required file is locked or unavailable.",
        ProcessingErrorCategory.Cancelled => "Processing was cancelled.",
        _ => "An unexpected local processing error occurred.",
    };

    private static string TranslateTurkish(ProcessingErrorCategory category) => category switch
    {
        ProcessingErrorCategory.UnsupportedFormat => "Bu görsel biçimi desteklenmiyor.",
        ProcessingErrorCategory.DecodeFailed => "Görsel çözümlenemedi.",
        ProcessingErrorCategory.ImageTooLarge => "Görsel güvenli boyut sınırlarını aşıyor.",
        ProcessingErrorCategory.ModelMissing => "Seçilen model kurulu değil.",
        ProcessingErrorCategory.ModelCorrupted => "Seçilen model bozuk veya güvenilir değil.",
        ProcessingErrorCategory.ModelIncompatible => "Model bu uygulama sürümüyle uyumlu değil.",
        ProcessingErrorCategory.ProviderUnavailable => "Seçilen inference sağlayıcısı kullanılamıyor.",
        ProcessingErrorCategory.GpuOutOfMemory => "GPU belleği yetersiz.",
        ProcessingErrorCategory.InferenceFailed => "Arka plan kaldırma inference işlemi başarısız oldu.",
        ProcessingErrorCategory.PostprocessFailed => "Alfa maskesi iyileştirilemedi.",
        ProcessingErrorCategory.EncodeFailed => "Sonuç kodlanamadı.",
        ProcessingErrorCategory.DiskFull => "Hedef disk dolu.",
        ProcessingErrorCategory.PermissionDenied => "Seçilen dosya veya klasöre erişim reddedildi.",
        ProcessingErrorCategory.FileLocked => "Gerekli dosya kilitli veya kullanılamıyor.",
        ProcessingErrorCategory.Cancelled => "İşlem iptal edildi.",
        _ => "Beklenmeyen bir yerel işlem hatası oluştu.",
    };
}
