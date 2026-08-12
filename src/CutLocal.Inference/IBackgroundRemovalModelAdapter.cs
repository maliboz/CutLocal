using System.Buffers;
using CutLocal.Domain;
using CutLocal.Imaging;

namespace CutLocal.Inference;

/// <summary>Separates model-specific preprocessing, execution, and postprocessing.</summary>
public interface IBackgroundRemovalModelAdapter : IDisposable
{
    /// <summary>Gets the manifest descriptor validated by this adapter.</summary>
    ModelDescriptor Descriptor { get; }
    /// <summary>Gets the concrete provider/device used by the session.</summary>
    InferenceProviderDescriptor Provider { get; }

    /// <summary>Fills a caller-owned tensor buffer and describes the model input.</summary>
    PreprocessedInput Preprocess(
        DecodedImage image,
        IMemoryOwner<float> inputBuffer);

    /// <summary>Runs in-process ONNX inference and returns an owned float mask.</summary>
    ValueTask<MaskResult> RunAsync(
        PreprocessedInput input,
        CancellationToken cancellationToken);

    /// <summary>Converts raw model output to a float alpha mask.</summary>
    RefinedMask Postprocess(
        MaskResult result,
        OriginalImageMetadata original,
        MaskRefinementOptions options);
}

/// <summary>Describes a pooled NCHW tensor without owning its caller-provided memory.</summary>
public sealed record PreprocessedInput
{
    /// <summary>Gets the actual ONNX input node name.</summary>
    public required string InputName { get; init; }
    /// <summary>Gets tensor data.</summary>
    public required Memory<float> Values { get; init; }
    /// <summary>Gets the ONNX tensor shape.</summary>
    public required long[] Shape { get; init; }
}

/// <summary>Owns raw float output copied from ONNX Runtime native memory.</summary>
public sealed class MaskResult : IDisposable
{
    private IMemoryOwner<float>? _owner;

    /// <summary>Initializes an owned raw mask.</summary>
    internal MaskResult(IMemoryOwner<float> owner, int width, int height)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Width = width;
        Height = height;
        Values = owner.Memory[..checked(width * height)];
    }

    /// <summary>Gets output width.</summary>
    public int Width { get; }
    /// <summary>Gets output height.</summary>
    public int Height { get; }
    /// <summary>Gets row-major float output values.</summary>
    public Memory<float> Values { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        IMemoryOwner<float>? owner = Interlocked.Exchange(ref _owner, null);
        owner?.Dispose();
    }
}
