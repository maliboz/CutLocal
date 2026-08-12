using System.Buffers;
using CutLocal.Domain;
using CutLocal.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace CutLocal.Inference;

/// <summary>Implements rembg-compatible U2NetP preprocessing and first-output postprocessing.</summary>
public sealed class U2NetModelAdapter : IBackgroundRemovalModelAdapter
{
    private static readonly Action<ILogger, string, string, string, Exception?> LogPreparedSession =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Information,
            new EventId(3001, nameof(LogPreparedSession)),
            "Prepared {ProviderId} session for model {ModelId} version {ModelVersion}");

    private readonly InferenceSession _session;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly MemoryPool<float> _memoryPool;
    private readonly FloatMaskPostprocessor _postprocessor;
    private readonly ILogger<U2NetModelAdapter> _logger;
    private readonly string _inputName;
    private readonly string _outputName;
    private bool _disposed;

    /// <summary>Initializes and validates a CPU session.</summary>
    public U2NetModelAdapter(
        ModelDescriptor descriptor,
        string modelPath,
        ILogger<U2NetModelAdapter> logger,
        MemoryPool<float>? memoryPool = null)
        : this(
            descriptor,
            modelPath,
            WindowsInferenceProviderCatalog.Cpu,
            logger,
            memoryPool)
    {
    }

    /// <summary>Initializes and validates a concrete provider/device session.</summary>
    public U2NetModelAdapter(
        ModelDescriptor descriptor,
        string modelPath,
        InferenceProviderDescriptor provider,
        ILogger<U2NetModelAdapter> logger,
        MemoryPool<float>? memoryPool = null)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _memoryPool = memoryPool ?? MemoryPool<float>.Shared;
        _postprocessor = new FloatMaskPostprocessor(_memoryPool);

        using SessionOptions options = ProviderSessionOptions.Create(Provider);
        _session = new InferenceSession(modelPath, options);
        (_inputName, _outputName) = ValidateMetadata(_session, Descriptor);
    }

    /// <inheritdoc />
    public ModelDescriptor Descriptor { get; }

    /// <inheritdoc />
    public InferenceProviderDescriptor Provider { get; }

    /// <inheritdoc />
    public unsafe PreprocessedInput Preprocess(
        DecodedImage image,
        IMemoryOwner<float> inputBuffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(inputBuffer);

        int width = Descriptor.Input.Width;
        int height = Descriptor.Input.Height;
        int pixelCount = checked(width * height);
        int tensorLength = checked(pixelCount * 3);
        if (inputBuffer.Memory.Length < tensorLength)
        {
            throw new ArgumentException("The input buffer is smaller than the model tensor.", nameof(inputBuffer));
        }

        SKImageInfo resizedInfo = new(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Unpremul);
        using SKBitmap resized = new(resizedInfo);
        bool scaled = image.Bitmap.ScalePixels(
            resized,
            new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (!scaled)
        {
            throw new InferenceException(
                ProcessingErrorCategory.InferenceFailed,
                "PREPROCESS_RESIZE",
                "The source image could not be resized for the model.");
        }

        byte maximum = 0;
        byte* pixels = (byte*)resized.GetPixels().ToPointer();
        for (int y = 0; y < height; y++)
        {
            byte* row = pixels + (y * resized.RowBytes);
            for (int x = 0; x < width; x++)
            {
                int offset = x * 4;
                maximum = Math.Max(maximum, row[offset]);
                maximum = Math.Max(maximum, row[offset + 1]);
                maximum = Math.Max(maximum, row[offset + 2]);
            }
        }

        float divisor = Math.Max(maximum, (byte)1);
        Span<float> tensor = inputBuffer.Memory.Span[..tensorLength];
        for (int y = 0; y < height; y++)
        {
            byte* row = pixels + (y * resized.RowBytes);
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = (y * width) + x;
                int offset = x * 4;
                float blue = row[offset] / divisor;
                float green = row[offset + 1] / divisor;
                float red = row[offset + 2] / divisor;

                tensor[pixelIndex] = NormalizeChannel(red, channel: 0);
                tensor[pixelCount + pixelIndex] = NormalizeChannel(green, channel: 1);
                tensor[(2 * pixelCount) + pixelIndex] = NormalizeChannel(blue, channel: 2);
            }
        }

        return new PreprocessedInput
        {
            InputName = _inputName,
            Values = inputBuffer.Memory[..tensorLength],
            Shape = [1, 3, height, width],
        };
    }

    /// <inheritdoc />
    public async ValueTask<MaskResult> RunAsync(
        PreprocessedInput input,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(input);

        await _runGate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => RunCore(input, cancellationToken), CancellationToken.None);
        }
        finally
        {
            _runGate.Release();
        }
    }

    /// <inheritdoc />
    public RefinedMask Postprocess(
        MaskResult result,
        OriginalImageMetadata original,
        MaskRefinementOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(original);
        return _postprocessor.Normalize(
            result.Values.Span,
            result.Width,
            result.Height,
            Descriptor.Output.Activation,
            options,
            original.Width,
            original.Height);
    }

    /// <summary>Runs one synthetic inference so model initialization is not charged to the first item.</summary>
    public async ValueTask WarmUpAsync(CancellationToken cancellationToken)
    {
        int length = checked(Descriptor.Input.Width * Descriptor.Input.Height * 3);
        using IMemoryOwner<float> owner = _memoryPool.Rent(length);
        owner.Memory.Span[..length].Clear();
        using MaskResult result = await RunAsync(
            new PreprocessedInput
            {
                InputName = _inputName,
                Values = owner.Memory[..length],
                Shape = [1, 3, Descriptor.Input.Height, Descriptor.Input.Width],
            },
            cancellationToken);
        LogPreparedSession(_logger, Provider.Id, Descriptor.Id, Descriptor.Version, null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session.Dispose();
        _runGate.Dispose();
        _disposed = true;
    }

    private MaskResult RunCore(PreprocessedInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using RunOptions runOptions = new() { LogId = "CutLocal.ModelInference" };
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((RunOptions)state!).Terminate = true,
            runOptions);
        using OrtValue inputValue = OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance,
            input.Values,
            input.Shape);
        Dictionary<string, OrtValue> inputs = new(StringComparer.Ordinal)
        {
            [input.InputName] = inputValue,
        };

        try
        {
            using IDisposableReadOnlyCollection<OrtValue> outputs = _session.Run(
                runOptions,
                inputs,
                [_outputName]);
            cancellationToken.ThrowIfCancellationRequested();
            OrtValue firstOutput = outputs[0];
            ReadOnlySpan<float> source = firstOutput.GetTensorDataAsSpan<float>();
            int maskLength = checked(Descriptor.Input.Width * Descriptor.Input.Height);
            if (source.Length < maskLength)
            {
                throw new InferenceException(
                    ProcessingErrorCategory.ModelIncompatible,
                    "MODEL_OUTPUT_TOO_SMALL",
                    "The model output tensor is smaller than the declared alpha mask.");
            }

            IMemoryOwner<float> owner = _memoryPool.Rent(maskLength);
            source[..maskLength].CopyTo(owner.Memory.Span);
            return new MaskResult(owner, Descriptor.Input.Width, Descriptor.Input.Height);
        }
        catch (OnnxRuntimeException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Inference was cancelled.", exception, cancellationToken);
        }
        catch (OnnxRuntimeException exception)
        {
            throw InferenceFailureClassifier.ClassifyRunFailure(exception, Provider);
        }
    }

    private float NormalizeChannel(float value, int channel) =>
        (value - (float)Descriptor.Input.Mean[channel])
        / (float)Descriptor.Input.Std[channel];

    internal static (string InputName, string OutputName) ValidateMetadata(
        InferenceSession session,
        ModelDescriptor descriptor)
    {
        if (session.InputMetadata.Count != 1)
        {
            throw new InferenceException(
                ProcessingErrorCategory.ModelIncompatible,
                "MODEL_INPUT_COUNT",
                "The model must expose exactly one input tensor.");
        }

        KeyValuePair<string, NodeMetadata> input = session.InputMetadata.Single();
        ValidateNodeName(descriptor.Input.NodeName, input.Key, "input");
        ValidateFloatTensor(
            input.Value,
            [1, 3, descriptor.Input.Height, descriptor.Input.Width],
            "input");

        if (session.OutputMetadata.Count == 0)
        {
            throw new InferenceException(
                ProcessingErrorCategory.ModelIncompatible,
                "MODEL_OUTPUT_COUNT",
                "The model must expose at least one output tensor.");
        }

        KeyValuePair<string, NodeMetadata> output = session.OutputMetadata.First();
        ValidateNodeName(descriptor.Output.NodeName, output.Key, "output");
        ValidateFloatTensor(
            output.Value,
            [1, 1, descriptor.Input.Height, descriptor.Input.Width],
            "output");
        return (input.Key, output.Key);
    }

    private static void ValidateNodeName(string? manifestName, string metadataName, string direction)
    {
        if (manifestName is not null && !manifestName.Equals(metadataName, StringComparison.Ordinal))
        {
            throw new InferenceException(
                ProcessingErrorCategory.ModelIncompatible,
                $"MODEL_{direction.ToUpperInvariant()}_NAME",
                $"Manifest {direction} node '{manifestName}' does not match model metadata '{metadataName}'.");
        }
    }

    private static void ValidateFloatTensor(NodeMetadata metadata, int[] expected, string direction)
    {
        if (!metadata.IsTensor || metadata.ElementDataType != TensorElementType.Float)
        {
            throw new InferenceException(
                ProcessingErrorCategory.ModelIncompatible,
                $"MODEL_{direction.ToUpperInvariant()}_TYPE",
                $"The model {direction} must be a float tensor.");
        }

        if (metadata.Dimensions.Length != expected.Length)
        {
            throw new InferenceException(
                ProcessingErrorCategory.ModelIncompatible,
                $"MODEL_{direction.ToUpperInvariant()}_RANK",
                $"The model {direction} tensor rank is incompatible.");
        }

        for (int index = 0; index < expected.Length; index++)
        {
            int actual = metadata.Dimensions[index];
            if (actual != -1 && actual != expected[index])
            {
                throw new InferenceException(
                    ProcessingErrorCategory.ModelIncompatible,
                    $"MODEL_{direction.ToUpperInvariant()}_SHAPE",
                    $"The model {direction} tensor shape does not match the manifest.");
            }
        }
    }
}
