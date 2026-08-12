using Microsoft.Extensions.Logging;

namespace CutLocal.Infrastructure;

internal static partial class PipelineLog
{
    [LoggerMessage(4001, LogLevel.Information, "Decoded {Width}x{Height} image in {ElapsedMs} ms; path omitted")]
    public static partial void Decoded(ILogger logger, int width, int height, double elapsedMs);

    [LoggerMessage(4002, LogLevel.Information, "Preprocessed image in {ElapsedMs} ms")]
    public static partial void Preprocessed(ILogger logger, double elapsedMs);

    [LoggerMessage(4003, LogLevel.Information, "Inference on {ProviderId} for {ModelId} version {ModelVersion} took {ElapsedMs} ms")]
    public static partial void Inferred(
        ILogger logger,
        string providerId,
        string modelId,
        string modelVersion,
        double elapsedMs);

    [LoggerMessage(4007, LogLevel.Warning, "Provider {ProviderId} failed with {LogCode}; retrying once on CPU")]
    public static partial void CpuFallback(ILogger logger, string providerId, string logCode, Exception exception);

    [LoggerMessage(4008, LogLevel.Warning, "Provider {ProviderId} could not initialize with {LogCode}; trying the next local provider")]
    public static partial void ProviderInitializationFallback(
        ILogger logger,
        string providerId,
        string logCode,
        Exception exception);

    [LoggerMessage(4004, LogLevel.Information, "Postprocessed and composed image in {ElapsedMs} ms")]
    public static partial void Postprocessed(ILogger logger, double elapsedMs);

    [LoggerMessage(4005, LogLevel.Information, "Encoded output in {ElapsedMs} ms; path omitted")]
    public static partial void Encoded(ILogger logger, double elapsedMs);

    [LoggerMessage(4006, LogLevel.Information, "Processing completed in {ElapsedMs} ms with working set {WorkingSetBytes}")]
    public static partial void Completed(ILogger logger, double elapsedMs, long workingSetBytes);

    [LoggerMessage(4101, LogLevel.Information, "Processing was cancelled; path omitted")]
    public static partial void Cancelled(ILogger logger, Exception exception);

    [LoggerMessage(4201, LogLevel.Error, "Typed inference failure {LogCode}; path omitted")]
    public static partial void InferenceFailure(ILogger logger, string logCode, Exception exception);

    [LoggerMessage(4202, LogLevel.Error, "Typed imaging failure {LogCode}; path omitted")]
    public static partial void ImagingFailure(ILogger logger, string logCode, Exception exception);

    [LoggerMessage(4203, LogLevel.Error, "Model manifest is invalid; path omitted")]
    public static partial void InvalidManifest(ILogger logger, Exception exception);

    [LoggerMessage(4204, LogLevel.Error, "Unexpected local file-system failure; path omitted")]
    public static partial void FileSystemFailure(ILogger logger, Exception exception);

    [LoggerMessage(4301, LogLevel.Critical, "Unexpected processing failure; path omitted")]
    public static partial void UnexpectedFailure(ILogger logger, Exception exception);
}
