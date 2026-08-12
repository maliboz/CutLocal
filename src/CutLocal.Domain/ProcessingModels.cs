namespace CutLocal.Domain;

/// <summary>Represents a durable batch processing job.</summary>
public sealed record ProcessingJob
{
    /// <summary>Gets the durable document schema version.</summary>
    public int SchemaVersion { get; init; } = 1;
    /// <summary>Gets the job identity.</summary>
    public required Guid Id { get; init; }
    /// <summary>Gets the creation time in UTC.</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }
    /// <summary>Gets the last durable state update in UTC.</summary>
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    /// <summary>Gets the current job status.</summary>
    public required JobStatus Status { get; init; }
    /// <summary>Gets the processing preset captured for repeatability.</summary>
    public required ProcessingPreset Preset { get; init; }
    /// <summary>Gets the ordered item snapshots.</summary>
    public required IReadOnlyList<ProcessingItem> Items { get; init; }
}

/// <summary>Represents one input in a processing job.</summary>
public sealed record ProcessingItem
{
    /// <summary>Gets the item identity.</summary>
    public required Guid Id { get; init; }
    /// <summary>Gets the local input path.</summary>
    public required string InputPath { get; init; }
    /// <summary>Gets the intended local output path.</summary>
    public required string OutputPath { get; init; }
    /// <summary>Gets the current item status.</summary>
    public required ItemStatus Status { get; init; }
    /// <summary>Gets completion from zero through one.</summary>
    public required double Progress { get; init; }
    /// <summary>Gets elapsed processing time when known.</summary>
    public TimeSpan? Elapsed { get; init; }
    /// <summary>Gets the terminal typed error, when present.</summary>
    public ProcessingError? Error { get; init; }
    /// <summary>Gets how many times this item entered the processing pipeline.</summary>
    public int AttemptCount { get; init; }
    /// <summary>Gets the provider/device that produced the latest successful output.</summary>
    public string? ProviderId { get; init; }
    /// <summary>Gets whether the latest successful attempt recovered from GPU to CPU.</summary>
    public bool UsedCpuFallback { get; init; }
}

/// <summary>Describes a discovered inference provider and device.</summary>
public sealed record InferenceProviderDescriptor
{
    /// <summary>Gets the provider kind.</summary>
    public required InferenceProviderKind Kind { get; init; }
    /// <summary>Gets a stable provider/device key.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the display name.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Gets whether it is usable without downloading components.</summary>
    public required bool IsReadyOffline { get; init; }
    /// <summary>Gets the maximum safe initial session concurrency.</summary>
    public required int MaxRecommendedConcurrency { get; init; }
    /// <summary>Gets the current DXGI adapter index when this is a DirectML device.</summary>
    public int? DeviceIndex { get; init; }
    /// <summary>Gets a stable locally discovered device identifier when available.</summary>
    public string? DeviceIdentity { get; init; }
    /// <summary>Gets dedicated video memory reported by DXGI.</summary>
    public long DedicatedVideoMemoryBytes { get; init; }
}

/// <summary>Captures user-selected processing behavior.</summary>
public sealed record ProcessingPreset
{
    /// <summary>Gets the preset name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the selected manifest model identifier.</summary>
    public required string ModelId { get; init; }
    /// <summary>Gets the provider policy.</summary>
    public required InferenceProviderKind Provider { get; init; }
    /// <summary>Gets the explicitly selected DirectML adapter index.</summary>
    public int? DirectMlAdapterIndex { get; init; }
    /// <summary>Gets the requested worker concurrency before provider safety clamping.</summary>
    public int Concurrency { get; init; } = 1;
    /// <summary>Gets mask refinement settings.</summary>
    public required MaskRefinementOptions Mask { get; init; }
    /// <summary>Gets output settings.</summary>
    public required OutputConfiguration Output { get; init; }
}

/// <summary>Defines mask refinement options without imaging implementation details.</summary>
public sealed record MaskRefinementOptions
{
    /// <summary>Gets the foreground threshold in the inclusive zero-to-one range.</summary>
    public double Threshold { get; init; } = 0.5;
    /// <summary>Gets whether to produce a binary mask.</summary>
    public bool HardCut { get; init; }
    /// <summary>Gets whether to invert the alpha mask.</summary>
    public bool Invert { get; init; }
    /// <summary>Gets the feather radius in output pixels.</summary>
    public double FeatherRadius { get; init; }
    /// <summary>Gets erosion iterations.</summary>
    public int Erode { get; init; }
    /// <summary>Gets dilation iterations.</summary>
    public int Dilate { get; init; }
}

/// <summary>Defines output naming and encoding behavior.</summary>
public sealed record OutputConfiguration
{
    /// <summary>Gets the output format.</summary>
    public required OutputFormat Format { get; init; }
    /// <summary>Gets the destination directory.</summary>
    public required string OutputDirectory { get; init; }
    /// <summary>Gets the filename suffix before the extension.</summary>
    public string FileNameSuffix { get; init; } = ".cutlocal";
    /// <summary>Gets how an existing destination is handled.</summary>
    public ExistingOutputBehavior ExistingOutputBehavior { get; init; } = ExistingOutputBehavior.Rename;
    /// <summary>Gets whether original pixel dimensions are preserved.</summary>
    public bool PreserveDimensions { get; init; } = true;
}

/// <summary>Represents persisted user settings.</summary>
public sealed record ApplicationSettings
{
    /// <summary>Gets the UI culture name.</summary>
    public string Culture { get; init; } = "tr-TR";
    /// <summary>Gets the selected model identifier.</summary>
    public string ModelId { get; init; } = "u2netp";
    /// <summary>Gets the provider policy.</summary>
    public InferenceProviderKind Provider { get; init; } = InferenceProviderKind.Auto;
    /// <summary>Gets the explicitly selected DirectML adapter index, when applicable.</summary>
    public int? DirectMlAdapterIndex { get; init; }
    /// <summary>Gets the last selected output directory.</summary>
    public string? OutputDirectory { get; init; }
    /// <summary>Gets the filename suffix placed before the PNG extension.</summary>
    public string FileNameSuffix { get; init; } = ".cutlocal";
    /// <summary>Gets how an existing output file is handled.</summary>
    public ExistingOutputBehavior ExistingOutputBehavior { get; init; } = ExistingOutputBehavior.Rename;
    /// <summary>Gets the float-mask threshold.</summary>
    public double Threshold { get; init; } = 0.5;
    /// <summary>Gets the edge feather radius in pixels.</summary>
    public double FeatherRadius { get; init; }
    /// <summary>Gets whether the alpha mask uses a hard threshold.</summary>
    public bool HardCut { get; init; }
    /// <summary>Gets whether the alpha mask is inverted.</summary>
    public bool InvertMask { get; init; }
    /// <summary>Gets whether changing mask controls refreshes an existing preview.</summary>
    public bool IsLivePreviewEnabled { get; init; } = true;
    /// <summary>Gets the configured worker concurrency.</summary>
    public int Concurrency { get; init; } = 1;
    /// <summary>Gets whether non-sensitive source metadata is retained.</summary>
    public bool PreserveMetadata { get; init; }
}

/// <summary>Stores a reproducible hardware/model benchmark measurement.</summary>
public sealed record BenchmarkResult
{
    /// <summary>Gets the model identifier.</summary>
    public required string ModelId { get; init; }
    /// <summary>Gets the model version.</summary>
    public required string ModelVersion { get; init; }
    /// <summary>Gets the provider/device key.</summary>
    public required string ProviderId { get; init; }
    /// <summary>Gets total latency.</summary>
    public required TimeSpan TotalLatency { get; init; }
    /// <summary>Gets measured peak working set in bytes.</summary>
    public required long PeakWorkingSetBytes { get; init; }
    /// <summary>Gets items processed per second.</summary>
    public required double ThroughputPerSecond { get; init; }
    /// <summary>Gets the median warmed inference latency.</summary>
    public required TimeSpan MedianInferenceLatency { get; init; }
    /// <summary>Gets the number of measured iterations.</summary>
    public required int IterationCount { get; init; }
    /// <summary>Gets the runtime version used for the measurement.</summary>
    public required string RuntimeVersion { get; init; }
    /// <summary>Gets the operating-system description used for the measurement.</summary>
    public required string OperatingSystem { get; init; }
    /// <summary>Gets the measurement time in UTC.</summary>
    public required DateTimeOffset MeasuredAtUtc { get; init; }
}

/// <summary>Provides a localized, stable failure contract.</summary>
public sealed record ProcessingError
{
    /// <summary>Gets the error category.</summary>
    public required ProcessingErrorCategory Category { get; init; }
    /// <summary>Gets a stable code suitable for log correlation.</summary>
    public required string LogCode { get; init; }
    /// <summary>Gets the Turkish user-facing message.</summary>
    public required string MessageTr { get; init; }
    /// <summary>Gets the English user-facing message.</summary>
    public required string MessageEn { get; init; }
    /// <summary>Gets whether retry can reasonably succeed without changing the input.</summary>
    public required bool IsRetryable { get; init; }
}

/// <summary>Represents the typed terminal result of one processing item.</summary>
public sealed record ProcessingResult
{
    /// <summary>Gets the terminal outcome.</summary>
    public required ProcessingOutcome Outcome { get; init; }
    /// <summary>Gets the committed output path on success.</summary>
    public string? OutputPath { get; init; }
    /// <summary>Gets the typed error on failure or cancellation.</summary>
    public ProcessingError? Error { get; init; }
    /// <summary>Gets the provider/device that produced the result.</summary>
    public string? ProviderId { get; init; }
    /// <summary>Gets whether the item recovered from a GPU failure on CPU.</summary>
    public bool UsedCpuFallback { get; init; }
}
