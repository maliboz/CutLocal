using System.Buffers;
using CutLocal.Contracts;
using CutLocal.Domain;
using CutLocal.Imaging;
using CutLocal.Inference;
using CutLocal.Infrastructure;
using CutLocal.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace CutLocal.IntegrationTests;

public sealed class GpuCpuFallbackPipelineTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "CutLocal.FallbackTests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(ProcessingErrorCategory.ProviderUnavailable, "DML_DEVICE_LOST")]
    [InlineData(ProcessingErrorCategory.GpuOutOfMemory, "DML_GPU_OOM")]
    public async Task EligibleDirectMlFailure_InvalidatesGpuLeaseAndRetriesItemOnceOnRealCpuAdapter(
        ProcessingErrorCategory category,
        string logCode)
    {
        (string modelPath, ModelDescriptor descriptor) =
            await FixtureModel.CreateAsync(_temporaryDirectory);
        descriptor = descriptor with { SupportedProviders = ["cpu", "directml"] };
        string inputPath = FixtureModel.CreateGradientPng(_temporaryDirectory);
        string outputPath = Path.Combine(_temporaryDirectory, "fallback-output.png");
        using U2NetModelAdapter cpuAdapter = new(
            descriptor,
            modelPath,
            NullLogger<U2NetModelAdapter>.Instance);
        await cpuAdapter.WarmUpAsync(CancellationToken.None);
        InferenceProviderDescriptor directMl = new()
        {
            Kind = InferenceProviderKind.DirectMl,
            Id = "directml:luid:simulated-loss",
            DisplayName = "Simulated DirectML device",
            IsReadyOffline = true,
            MaxRecommendedConcurrency = 1,
            DeviceIndex = 0,
            DeviceIdentity = "luid:simulated-loss",
            DedicatedVideoMemoryBytes = 8L * 1024 * 1024 * 1024,
        };
        FailingGpuAdapter gpuAdapter = new(cpuAdapter, directMl, category, logCode);
        StubSessionCache sessionCache = new(gpuAdapter, cpuAdapter);
        ProviderSelectionService selection = new(new StaticProviderCatalog(
            [directMl, WindowsInferenceProviderCatalog.Cpu]));
        LocalBackgroundRemovalProcessor processor = new(
            new StaticModelCatalog(descriptor),
            new StaticPathResolver(modelPath),
            sessionCache,
            selection,
            new SafePngDecoder(),
            new BilinearAlphaCompositor(),
            new AtomicPngWriter(),
            NullLogger<LocalBackgroundRemovalProcessor>.Instance);

        ProcessingResult result = await processor.ProcessAsync(
            new RemoveBackgroundRequest
            {
                InputPath = inputPath,
                OutputPath = outputPath,
                ModelId = descriptor.Id,
                Provider = InferenceProviderKind.Auto,
                ExistingOutputBehavior = ExistingOutputBehavior.Overwrite,
            },
            progress: null,
            CancellationToken.None);

        Assert.Equal(ProcessingOutcome.Succeeded, result.Outcome);
        Assert.True(result.UsedCpuFallback);
        Assert.Equal("cpu", result.ProviderId);
        Assert.Equal([InferenceProviderKind.DirectMl, InferenceProviderKind.Cpu], sessionCache.AcquiredKinds);
        Assert.True(sessionCache.GpuLease.Invalidated);
        Assert.True(sessionCache.GpuLease.Disposed);
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task ProcessAsync_DecodeOutOfMemoryReturnsTypedMemoryPressureFailure()
    {
        (string modelPath, ModelDescriptor descriptor) =
            await FixtureModel.CreateAsync(_temporaryDirectory);
        using U2NetModelAdapterFactory sessionCache = new(NullLoggerFactory.Instance);
        ProviderSelectionService selection = new(new StaticProviderCatalog(
            [WindowsInferenceProviderCatalog.Cpu]));
        LocalBackgroundRemovalProcessor processor = new(
            new StaticModelCatalog(descriptor),
            new StaticPathResolver(modelPath),
            sessionCache,
            selection,
            new OutOfMemoryDecoder(),
            new BilinearAlphaCompositor(),
            new AtomicPngWriter(),
            NullLogger<LocalBackgroundRemovalProcessor>.Instance);

        ProcessingResult result = await processor.ProcessAsync(
            new RemoveBackgroundRequest
            {
                InputPath = Path.Combine(_temporaryDirectory, "large-input.png"),
                OutputPath = Path.Combine(_temporaryDirectory, "large-output.png"),
                ModelId = descriptor.Id,
                Provider = InferenceProviderKind.Cpu,
            },
            progress: null,
            CancellationToken.None);

        Assert.Equal(ProcessingOutcome.Failed, result.Outcome);
        Assert.Equal(ProcessingErrorCategory.ImageTooLarge, result.Error?.Category);
        Assert.Equal("PROC_MEMORY_PRESSURE", result.Error?.LogCode);
        Assert.False(result.Error?.IsRetryable);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private sealed class FailingGpuAdapter(
        IBackgroundRemovalModelAdapter cpuAdapter,
        InferenceProviderDescriptor provider,
        ProcessingErrorCategory category,
        string logCode) : IBackgroundRemovalModelAdapter
    {
        public ModelDescriptor Descriptor => cpuAdapter.Descriptor;
        public InferenceProviderDescriptor Provider => provider;

        public PreprocessedInput Preprocess(DecodedImage image, IMemoryOwner<float> inputBuffer) =>
            cpuAdapter.Preprocess(image, inputBuffer);

        public ValueTask<MaskResult> RunAsync(
            PreprocessedInput input,
            CancellationToken cancellationToken) => ValueTask.FromException<MaskResult>(
                new InferenceException(
                    category,
                    logCode,
                    "Simulated fallback-eligible DirectML failure.",
                    isGpuFallbackEligible: true));

        public RefinedMask Postprocess(
            MaskResult result,
            OriginalImageMetadata original,
            MaskRefinementOptions options) => cpuAdapter.Postprocess(result, original, options);

        public void Dispose()
        {
        }
    }

    private sealed class StubSessionCache(
        IBackgroundRemovalModelAdapter gpu,
        IBackgroundRemovalModelAdapter cpu) : IModelAdapterSessionCache
    {
        public StubLease GpuLease { get; } = new(gpu);
        public List<InferenceProviderKind> AcquiredKinds { get; } = [];

        public ValueTask<IModelAdapterLease> AcquireAsync(
            ModelDescriptor descriptor,
            string modelPath,
            InferenceProviderDescriptor provider,
            CancellationToken cancellationToken)
        {
            AcquiredKinds.Add(provider.Kind);
            IModelAdapterLease lease = provider.Kind == InferenceProviderKind.DirectMl
                ? GpuLease
                : new StubLease(cpu);
            return ValueTask.FromResult(lease);
        }
    }

    private sealed class StubLease(IBackgroundRemovalModelAdapter adapter) : IModelAdapterLease
    {
        public IBackgroundRemovalModelAdapter Adapter { get; } = adapter;
        public InferenceProviderDescriptor Provider => Adapter.Provider;
        public bool Invalidated { get; private set; }
        public bool Disposed { get; private set; }

        public void Invalidate() => Invalidated = true;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StaticProviderCatalog(IReadOnlyList<InferenceProviderDescriptor> providers)
        : IInferenceProviderCatalog
    {
        public ValueTask<IReadOnlyList<InferenceProviderDescriptor>> GetAllAsync(
            CancellationToken cancellationToken) => ValueTask.FromResult(providers);
    }

    private sealed class StaticModelCatalog(ModelDescriptor descriptor) : IModelCatalog
    {
        public ValueTask<ModelDescriptor?> GetByIdAsync(
            string modelId,
            CancellationToken cancellationToken) => ValueTask.FromResult<ModelDescriptor?>(descriptor);

        public ValueTask<IReadOnlyList<ModelDescriptor>> GetAllAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ModelDescriptor>>([descriptor]);
    }

    private sealed class StaticPathResolver(string path) : IModelPathResolver
    {
        public string GetModelPath(ModelDescriptor descriptor) => path;
    }

    private sealed class OutOfMemoryDecoder : IImageDecoder
    {
        public DecodedImage Decode(string inputPath, CancellationToken cancellationToken) =>
            throw new SimulatedOutOfMemoryException();
    }

    private sealed class SimulatedOutOfMemoryException : OutOfMemoryException
    {
    }
}
