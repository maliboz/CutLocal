using CutLocal.Contracts;
using CutLocal.Domain;
using Microsoft.Extensions.Logging;

namespace CutLocal.Application;

/// <summary>Orchestrates one local background-removal request.</summary>
public sealed class RemoveBackgroundUseCase
{
    private static readonly Action<ILogger, string, Exception?> LogStarting = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(1001, nameof(LogStarting)),
        "Starting local background removal with model {ModelId}; input path omitted for privacy");
    private static readonly Action<ILogger, ProcessingOutcome, string, Exception?> LogFinished =
        LoggerMessage.Define<ProcessingOutcome, string>(
            LogLevel.Information,
            new EventId(1002, nameof(LogFinished)),
            "Local background removal ended with {Outcome} and code {ErrorCode}");

    private readonly IRemoveBackgroundProcessor _processor;
    private readonly ILogger<RemoveBackgroundUseCase> _logger;

    /// <summary>Initializes the use case.</summary>
    public RemoveBackgroundUseCase(
        IRemoveBackgroundProcessor processor,
        ILogger<RemoveBackgroundUseCase> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    /// <summary>Processes one PNG and returns a typed result.</summary>
    public async ValueTask<ProcessingResult> ExecuteAsync(
        string inputPath,
        string? outputPath,
        IProgress<ProcessingProgress>? progress,
        CancellationToken cancellationToken) => await ExecuteAsync(
            inputPath,
            outputPath,
            new SingleImageProcessingOptions(),
            progress,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Processes one PNG with explicit model, provider, and mask options.</summary>
    public async ValueTask<ProcessingResult> ExecuteAsync(
        string inputPath,
        string? outputPath,
        SingleImageProcessingOptions options,
        IProgress<ProcessingProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ModelId);

        string finalOutputPath = string.IsNullOrWhiteSpace(outputPath)
            ? OutputPathPolicy.CreateSiblingPngPath(inputPath)
            : Path.GetFullPath(outputPath);

        LogStarting(_logger, options.ModelId, null);

        ProcessingResult result = await _processor.ProcessAsync(
            new RemoveBackgroundRequest
            {
                InputPath = Path.GetFullPath(inputPath),
                OutputPath = finalOutputPath,
                ModelId = options.ModelId,
                MaskOptions = options.MaskOptions,
                ExistingOutputBehavior = options.ExistingOutputBehavior,
                Provider = options.Provider,
                DirectMlAdapterIndex = options.DirectMlAdapterIndex,
            },
            progress,
            cancellationToken);

        LogFinished(_logger, result.Outcome, result.Error?.LogCode ?? "none", null);

        return result;
    }
}
