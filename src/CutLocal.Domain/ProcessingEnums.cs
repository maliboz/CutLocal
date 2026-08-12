namespace CutLocal.Domain;

/// <summary>Describes the lifecycle of a processing job.</summary>
public enum JobStatus
{
    /// <summary>The job is waiting to run.</summary>
    Queued,
    /// <summary>The job is actively processing items.</summary>
    Running,
    /// <summary>The job is paused and retains its queue position.</summary>
    Paused,
    /// <summary>The job completed all items.</summary>
    Completed,
    /// <summary>The job reached a terminal state with one or more failed items.</summary>
    CompletedWithErrors,
    /// <summary>The job was cancelled by the user.</summary>
    Cancelled,
    /// <summary>The job cannot continue without intervention.</summary>
    Failed,
    /// <summary>The application closed while the job was active.</summary>
    Interrupted,
}

/// <summary>Describes the lifecycle of one input item.</summary>
public enum ItemStatus
{
    /// <summary>The item is waiting to run.</summary>
    Queued,
    /// <summary>The model is being prepared.</summary>
    PreparingModel,
    /// <summary>The image is being decoded.</summary>
    Decoding,
    /// <summary>The image is being prepared for inference.</summary>
    Preprocessing,
    /// <summary>The model is running.</summary>
    Inferring,
    /// <summary>The mask is being refined and composed.</summary>
    Postprocessing,
    /// <summary>The result is being encoded.</summary>
    Encoding,
    /// <summary>The result was written successfully.</summary>
    Completed,
    /// <summary>The item was intentionally left untouched because its output already existed.</summary>
    Skipped,
    /// <summary>The item failed without stopping sibling items.</summary>
    Failed,
    /// <summary>The item was cancelled.</summary>
    Cancelled,
}

/// <summary>Classifies a processing failure without exposing implementation exceptions.</summary>
public enum ProcessingErrorCategory
{
    /// <summary>The input format is unsupported.</summary>
    UnsupportedFormat,
    /// <summary>The image could not be decoded.</summary>
    DecodeFailed,
    /// <summary>The image exceeds a configured safety limit.</summary>
    ImageTooLarge,
    /// <summary>The requested model file is absent.</summary>
    ModelMissing,
    /// <summary>The model hash does not match its manifest.</summary>
    ModelCorrupted,
    /// <summary>The model metadata is incompatible with its adapter.</summary>
    ModelIncompatible,
    /// <summary>The requested execution provider is unavailable.</summary>
    ProviderUnavailable,
    /// <summary>The GPU could not allocate enough memory.</summary>
    GpuOutOfMemory,
    /// <summary>ONNX Runtime failed to execute the model.</summary>
    InferenceFailed,
    /// <summary>Mask processing or composition failed.</summary>
    PostprocessFailed,
    /// <summary>The output could not be encoded.</summary>
    EncodeFailed,
    /// <summary>The destination volume has no usable free space.</summary>
    DiskFull,
    /// <summary>The application lacks access to a required path.</summary>
    PermissionDenied,
    /// <summary>A required file is locked by another process.</summary>
    FileLocked,
    /// <summary>The operation was cancelled.</summary>
    Cancelled,
    /// <summary>An unexpected failure occurred.</summary>
    Unknown,
}

/// <summary>Describes the result class of a processing request.</summary>
public enum ProcessingOutcome
{
    /// <summary>The output was produced.</summary>
    Succeeded,
    /// <summary>The request ended with a typed error.</summary>
    Failed,
    /// <summary>The request was cancelled.</summary>
    Cancelled,
}

/// <summary>Identifies a provider policy or concrete provider.</summary>
public enum InferenceProviderKind
{
    /// <summary>Select the best validated local provider.</summary>
    Auto,
    /// <summary>Use a locally ready Windows ML provider.</summary>
    WindowsMl,
    /// <summary>Use DirectML.</summary>
    DirectMl,
    /// <summary>Use the bundled CPU provider.</summary>
    Cpu,
}

/// <summary>Controls behavior when a destination already exists.</summary>
public enum ExistingOutputBehavior
{
    /// <summary>Leave the existing file untouched.</summary>
    Skip,
    /// <summary>Replace the existing file atomically.</summary>
    Overwrite,
    /// <summary>Choose a unique destination name.</summary>
    Rename,
}

/// <summary>Identifies an output image format.</summary>
public enum OutputFormat
{
    /// <summary>PNG with alpha.</summary>
    Png,
    /// <summary>Lossless WebP with alpha.</summary>
    Webp,
    /// <summary>JPEG over an opaque background.</summary>
    Jpeg,
    /// <summary>Grayscale mask PNG.</summary>
    MaskPng,
}
