using CutLocal.Domain;

namespace CutLocal.Contracts;

/// <summary>Contains the immutable inputs for a single removal operation.</summary>
public sealed record RemoveBackgroundRequest
{
    /// <summary>Gets the PNG input path.</summary>
    public required string InputPath { get; init; }
    /// <summary>Gets the final PNG output path.</summary>
    public required string OutputPath { get; init; }
    /// <summary>Gets the manifest model identifier.</summary>
    public required string ModelId { get; init; }
    /// <summary>Gets refinement options.</summary>
    public MaskRefinementOptions MaskOptions { get; init; } = new();
    /// <summary>Gets how an existing output is handled.</summary>
    public ExistingOutputBehavior ExistingOutputBehavior { get; init; } = ExistingOutputBehavior.Rename;
    /// <summary>Gets the requested provider policy.</summary>
    public InferenceProviderKind Provider { get; init; } = InferenceProviderKind.Auto;
    /// <summary>Gets an optional DirectML adapter index from the current DXGI enumeration.</summary>
    public int? DirectMlAdapterIndex { get; init; }
}

/// <summary>Captures user-selectable settings for a single-image processing run.</summary>
public sealed record SingleImageProcessingOptions
{
    /// <summary>Gets the manifest model identifier.</summary>
    public string ModelId { get; init; } = "u2netp";
    /// <summary>Gets the provider policy.</summary>
    public InferenceProviderKind Provider { get; init; } = InferenceProviderKind.Auto;
    /// <summary>Gets an optional DirectML DXGI adapter index.</summary>
    public int? DirectMlAdapterIndex { get; init; }
    /// <summary>Gets float-mask refinement settings.</summary>
    public MaskRefinementOptions MaskOptions { get; init; } = new();
    /// <summary>Gets how an existing destination is handled.</summary>
    public ExistingOutputBehavior ExistingOutputBehavior { get; init; } =
        ExistingOutputBehavior.Rename;
}

/// <summary>Executes the local decode/inference/compose/encode pipeline.</summary>
public interface IRemoveBackgroundProcessor
{
    /// <summary>Processes one input without performing network access.</summary>
    ValueTask<ProcessingResult> ProcessAsync(
        RemoveBackgroundRequest request,
        IProgress<ProcessingProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>Reports coarse processing progress suitable for a throttled UI.</summary>
public sealed record ProcessingProgress
{
    /// <summary>Gets the current item status.</summary>
    public required ItemStatus Status { get; init; }
    /// <summary>Gets completion from zero through one.</summary>
    public required double Value { get; init; }
    /// <summary>Gets a localization key or concise fallback status.</summary>
    public required string MessageKey { get; init; }
}

/// <summary>Loads and validates weight-free model manifests.</summary>
public interface IModelCatalog
{
    /// <summary>Gets one model by stable identifier.</summary>
    ValueTask<ModelDescriptor?> GetByIdAsync(string modelId, CancellationToken cancellationToken);
    /// <summary>Gets all valid model manifests.</summary>
    ValueTask<IReadOnlyList<ModelDescriptor>> GetAllAsync(CancellationToken cancellationToken);
}

/// <summary>Resolves a validated manifest to its expected local model path.</summary>
public interface IModelPathResolver
{
    /// <summary>Gets the path under the controlled application model directory.</summary>
    string GetModelPath(ModelDescriptor descriptor);
}

/// <summary>Validates model manifests and commercial distribution policy.</summary>
public interface IModelManifestValidator
{
    /// <summary>Returns all validation messages; an empty result means valid.</summary>
    IReadOnlyList<string> Validate(ModelDescriptor descriptor, bool commercialBuild);
}

/// <summary>Validates that an ONNX graph matches its manifest before local activation.</summary>
public interface IModelCompatibilityValidator
{
    /// <summary>Loads the graph on CPU and validates tensor names, types, ranks, and dimensions.</summary>
    ValueTask ValidateAsync(
        ModelDescriptor descriptor,
        string modelPath,
        CancellationToken cancellationToken);
}

/// <summary>Manages versioned ONNX packages without participating in inference execution.</summary>
public interface IModelPackageManager
{
    /// <summary>Inspects all local manifests and package states without network access.</summary>
    ValueTask<IReadOnlyList<ModelInstallationInfo>> InspectAllAsync(
        CancellationToken cancellationToken);

    /// <summary>Downloads one reviewed catalog package over HTTPS into a resumable partial file.</summary>
    ValueTask<ModelPackageOperationResult> DownloadAsync(
        ModelDescriptor descriptor,
        IProgress<ModelTransferProgress>? progress,
        CancellationToken cancellationToken,
        bool licenseAcknowledged = false);

    /// <summary>Repairs one catalog package by quarantining invalid content and downloading again.</summary>
    ValueTask<ModelPackageOperationResult> RepairAsync(
        ModelDescriptor descriptor,
        IProgress<ModelTransferProgress>? progress,
        CancellationToken cancellationToken,
        bool licenseAcknowledged = false);

    /// <summary>Deletes final and partial package content from the controlled model root.</summary>
    ValueTask<ModelPackageOperationResult> DeleteAsync(
        ModelDescriptor descriptor,
        CancellationToken cancellationToken);

    /// <summary>Imports a local ONNX file using an explicit companion manifest and license acknowledgement.</summary>
    ValueTask<ModelPackageOperationResult> ImportAsync(
        ModelImportRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Contains explicit inputs required for a safe custom ONNX import.</summary>
public sealed record ModelImportRequest
{
    /// <summary>Gets the selected local ONNX path.</summary>
    public required string OnnxPath { get; init; }
    /// <summary>Gets the companion JSON manifest path.</summary>
    public required string ManifestPath { get; init; }
    /// <summary>Gets the user's explicit acknowledgement of the supplied model license.</summary>
    public required bool LicenseAcknowledged { get; init; }
}

/// <summary>Discovers inference providers without downloading or installing components.</summary>
public interface IInferenceProviderCatalog
{
    /// <summary>Gets the provider/device inventory that is usable by this installation.</summary>
    ValueTask<IReadOnlyList<InferenceProviderDescriptor>> GetAllAsync(
        CancellationToken cancellationToken);
}

/// <summary>Runs a bounded, local benchmark for one model and provider policy.</summary>
public interface IHardwareBenchmarkService
{
    /// <summary>Measures warmed inference using synthetic input and no network access.</summary>
    ValueTask<IReadOnlyList<BenchmarkResult>> RunAsync(
        HardwareBenchmarkRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Defines a bounded local hardware benchmark.</summary>
public sealed record HardwareBenchmarkRequest
{
    /// <summary>Gets the model manifest identifier.</summary>
    public string ModelId { get; init; } = "u2netp";
    /// <summary>Gets the provider policy to measure.</summary>
    public InferenceProviderKind Provider { get; init; } = InferenceProviderKind.Auto;
    /// <summary>Gets an optional DirectML adapter index.</summary>
    public int? DirectMlAdapterIndex { get; init; }
    /// <summary>Gets the measured inference iteration count after warm-up.</summary>
    public int Iterations { get; init; } = 5;
}
