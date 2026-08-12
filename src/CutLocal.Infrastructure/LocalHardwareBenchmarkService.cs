using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CutLocal.Contracts;
using CutLocal.Domain;
using CutLocal.Imaging;
using CutLocal.Inference;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;

namespace CutLocal.Infrastructure;

/// <summary>Measures warmed provider inference with deterministic synthetic input.</summary>
public sealed class LocalHardwareBenchmarkService : IHardwareBenchmarkService
{
    private readonly IModelCatalog _modelCatalog;
    private readonly IModelPathResolver _modelPathResolver;
    private readonly ProviderSelectionService _providerSelection;
    private readonly IModelAdapterSessionCache _adapterFactory;

    /// <summary>Initializes the offline benchmark service.</summary>
    public LocalHardwareBenchmarkService(
        IModelCatalog modelCatalog,
        IModelPathResolver modelPathResolver,
        ProviderSelectionService providerSelection,
        IModelAdapterSessionCache adapterFactory)
    {
        _modelCatalog = modelCatalog;
        _modelPathResolver = modelPathResolver;
        _providerSelection = providerSelection;
        _adapterFactory = adapterFactory;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<BenchmarkResult>> RunAsync(
        HardwareBenchmarkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Iterations is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        ModelDescriptor descriptor = await _modelCatalog.GetByIdAsync(
                request.ModelId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InferenceException(
                ProcessingErrorCategory.ModelMissing,
                "BENCH_MODEL_MISSING",
                "The benchmark model manifest is not installed.");
        string modelPath = _modelPathResolver.GetModelPath(descriptor);
        IReadOnlyList<InferenceProviderDescriptor> candidates =
            await _providerSelection.GetCandidatesAsync(
                    descriptor,
                    request.Provider,
                    request.DirectMlAdapterIndex,
                    cancellationToken)
                .ConfigureAwait(false);

        using DecodedImage image = CreateSyntheticImage();
        List<BenchmarkResult> results = [];
        InferenceException? lastProviderFailure = null;
        foreach (InferenceProviderDescriptor candidate in candidates)
        {
            try
            {
                await using IModelAdapterLease lease =
                    await _adapterFactory.AcquireAsync(
                            descriptor,
                            modelPath,
                            candidate,
                            cancellationToken)
                        .ConfigureAwait(false);
                results.Add(await MeasureAsync(
                        lease.Adapter,
                        descriptor,
                        image,
                        request.Iterations,
                        cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (InferenceException exception) when (candidate.Kind != InferenceProviderKind.Cpu)
            {
                lastProviderFailure = exception;
            }
        }

        if (results.Count == 0)
        {
            throw lastProviderFailure ?? new InferenceException(
                ProcessingErrorCategory.ProviderUnavailable,
                "BENCH_PROVIDER_UNAVAILABLE",
                "No provider completed the local benchmark.");
        }

        return results;
    }

    private static async ValueTask<BenchmarkResult> MeasureAsync(
        IBackgroundRemovalModelAdapter adapter,
        ModelDescriptor descriptor,
        DecodedImage image,
        int iterations,
        CancellationToken cancellationToken)
    {
        int tensorLength = checked(descriptor.Input.Width * descriptor.Input.Height * 3);
        using IMemoryOwner<float> owner = MemoryPool<float>.Shared.Rent(tensorLength);
        Stopwatch total = Stopwatch.StartNew();
        PreprocessedInput input = adapter.Preprocess(image, owner);
        List<TimeSpan> samples = new(iterations);
        long peakWorkingSet = Environment.WorkingSet;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stopwatch inference = Stopwatch.StartNew();
            using MaskResult result = await adapter.RunAsync(input, cancellationToken)
                .ConfigureAwait(false);
            inference.Stop();
            samples.Add(inference.Elapsed);
            peakWorkingSet = Math.Max(peakWorkingSet, Environment.WorkingSet);
        }

        total.Stop();
        samples.Sort();
        TimeSpan median = samples[samples.Count / 2];
        string runtimeVersion = typeof(InferenceSession).Assembly
            .GetName()
            .Version?
            .ToString() ?? "unknown";
        return new BenchmarkResult
        {
            ModelId = descriptor.Id,
            ModelVersion = descriptor.Version,
            ProviderId = adapter.Provider.Id,
            TotalLatency = total.Elapsed,
            PeakWorkingSetBytes = peakWorkingSet,
            ThroughputPerSecond = median.TotalSeconds <= 0 ? 0 : 1d / median.TotalSeconds,
            MedianInferenceLatency = median,
            IterationCount = iterations,
            RuntimeVersion = runtimeVersion,
            OperatingSystem = RuntimeInformation.OSDescription,
            MeasuredAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static DecodedImage CreateSyntheticImage()
    {
        const int Width = 160;
        const int Height = 96;
        SKBitmap bitmap = new(new SKImageInfo(
            Width,
            Height,
            SKColorType.Bgra8888,
            SKAlphaType.Unpremul));
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                byte red = (byte)Math.Round(x * 255d / (Width - 1));
                byte green = (byte)Math.Round(y * 255d / (Height - 1));
                bitmap.SetPixel(x, y, new SKColor(red, green, 96, byte.MaxValue));
            }
        }

        return new DecodedImage(
            bitmap,
            new OriginalImageMetadata { Width = Width, Height = Height });
    }
}
