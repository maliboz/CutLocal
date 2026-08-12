namespace CutLocal.Domain;

/// <summary>Describes one versioned ONNX model and all policy required to activate it.</summary>
public sealed record ModelDescriptor
{
    /// <summary>Gets the stable model identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the user-facing model name.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Gets the manifest version.</summary>
    public required string Version { get; init; }
    /// <summary>Gets the expected local file name.</summary>
    public required string FileName { get; init; }
    /// <summary>Gets the lowercase or uppercase SHA-256 hexadecimal digest.</summary>
    public required string Sha256 { get; init; }
    /// <summary>Gets the exact expected ONNX package size in bytes.</summary>
    public long FileSizeBytes { get; init; }
    /// <summary>Gets the HTTPS provenance/download URI.</summary>
    public required string DownloadUrl { get; init; }
    /// <summary>Gets the model-specific license policy.</summary>
    public required ModelLicenseDescriptor License { get; init; }
    /// <summary>Gets input tensor behavior.</summary>
    public required ModelInputDescriptor Input { get; init; }
    /// <summary>Gets output tensor behavior.</summary>
    public required ModelOutputDescriptor Output { get; init; }
    /// <summary>Gets the conservative recommended memory in MiB.</summary>
    public required int RecommendedMemoryMb { get; init; }
    /// <summary>Gets the catalog tier such as fast or quality.</summary>
    public required string Tier { get; init; }
    /// <summary>Gets provider names supported by the published manifest.</summary>
    public required IReadOnlyList<string> SupportedProviders { get; init; }
}

/// <summary>Describes the verified local installation state of a model package.</summary>
public enum ModelInstallationState
{
    /// <summary>No final or resumable package exists.</summary>
    NotInstalled,
    /// <summary>A resumable partial package exists.</summary>
    Partial,
    /// <summary>The final package passed size and SHA-256 verification.</summary>
    Installed,
    /// <summary>A final package exists but failed verification.</summary>
    Corrupted,
}

/// <summary>Combines manifest metadata with privacy-safe local package state.</summary>
public sealed record ModelInstallationInfo
{
    /// <summary>Gets the reviewed manifest.</summary>
    public required ModelDescriptor Descriptor { get; init; }
    /// <summary>Gets the verified installation state.</summary>
    public required ModelInstallationState State { get; init; }
    /// <summary>Gets locally present bytes without exposing a user path.</summary>
    public required long LocalBytes { get; init; }
    /// <summary>Gets whether the manifest came from a user import.</summary>
    public bool IsUserSupplied { get; init; }
}

/// <summary>Reports bounded streaming progress for one model package.</summary>
public sealed record ModelTransferProgress
{
    /// <summary>Gets bytes already persisted to the partial file.</summary>
    public required long BytesReceived { get; init; }
    /// <summary>Gets the exact expected package length.</summary>
    public required long TotalBytes { get; init; }
}

/// <summary>Provides a typed result for package install, repair, delete, and import operations.</summary>
public sealed record ModelPackageOperationResult
{
    /// <summary>Gets whether the requested operation completed successfully.</summary>
    public required bool Succeeded { get; init; }
    /// <summary>Gets a stable diagnostic/localization code.</summary>
    public required string Code { get; init; }
    /// <summary>Gets the resulting local installation state.</summary>
    public required ModelInstallationState State { get; init; }
}

/// <summary>Records the policy and provenance of model weights.</summary>
public sealed record ModelLicenseDescriptor
{
    /// <summary>Gets the SPDX identifier or expression.</summary>
    public required string Spdx { get; init; }
    /// <summary>Gets whether the reviewed license permits commercial use.</summary>
    public required bool CommercialUseAllowed { get; init; }
    /// <summary>Gets whether distribution must retain attribution.</summary>
    public required bool AttributionRequired { get; init; }
    /// <summary>Gets the authoritative license/provenance source URI.</summary>
    public required string Source { get; init; }
}

/// <summary>Describes a model's fixed image input.</summary>
public sealed record ModelInputDescriptor
{
    /// <summary>Gets the expected tensor width.</summary>
    public required int Width { get; init; }
    /// <summary>Gets the expected tensor height.</summary>
    public required int Height { get; init; }
    /// <summary>Gets the tensor layout, currently NCHW.</summary>
    public required string Layout { get; init; }
    /// <summary>Gets the channel order, currently RGB.</summary>
    public required string ColorOrder { get; init; }
    /// <summary>Gets channel means.</summary>
    public required IReadOnlyList<double> Mean { get; init; }
    /// <summary>Gets channel standard deviations.</summary>
    public required IReadOnlyList<double> Std { get; init; }
    /// <summary>Gets the resize strategy, such as stretch or letterbox.</summary>
    public required string ResizeMode { get; init; }
    /// <summary>Gets an optional required ONNX input node name.</summary>
    public string? NodeName { get; init; }
}

/// <summary>Describes the first alpha-mask output.</summary>
public sealed record ModelOutputDescriptor
{
    /// <summary>Gets the output activation or normalization policy.</summary>
    public required string Activation { get; init; }
    /// <summary>Gets the semantic output type.</summary>
    public required string Type { get; init; }
    /// <summary>Gets an optional required ONNX output node name.</summary>
    public string? NodeName { get; init; }
}
