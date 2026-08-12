using System.Diagnostics;
using System.Text.Json;
using CutLocal.Contracts;
using CutLocal.Domain;
using CutLocal.Imaging;
using CutLocal.Inference;
using CutLocal.Infrastructure;
using CutLocal.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace CutLocal.StressTests;

public sealed class PipelineResourceStabilityTests : IDisposable
{
    private const int SmallPipelineIterations = 500;
    private const int Synthetic4KIterations = 100;
    private const long OneMiB = 1024 * 1024;
    private static readonly JsonSerializerOptions EvidenceSerializerOptions = new()
    {
        WriteIndented = true,
    };
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "CutLocal.StressTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RealSmallPipeline_FiveHundredCyclesKeepResourcesBounded()
    {
        (string modelPath, ModelDescriptor descriptor) =
            await FixtureModel.CreateAsync(_temporaryDirectory);
        string inputPath = FixtureModel.CreateGradientPng(_temporaryDirectory);
        string outputPath = Path.Combine(_temporaryDirectory, "stress-output.png");
        using U2NetModelAdapterFactory sessions = new(NullLoggerFactory.Instance);
        LocalBackgroundRemovalProcessor processor = CreateProcessor(
            descriptor,
            modelPath,
            sessions,
            new SafePngDecoder(),
            new BilinearAlphaCompositor(),
            new AtomicPngWriter());
        RemoveBackgroundRequest request = CreateRequest(descriptor.Id, inputPath, outputPath);

        for (int warmup = 0; warmup < 10; warmup++)
        {
            AssertSucceeded(await processor.ProcessAsync(request, null, CancellationToken.None));
        }

        ForceCollectionForMeasurement();
        using Process process = Process.GetCurrentProcess();
        ResourceSnapshot baseline = Capture(process, iteration: 0, sessions.CachedSessionCount);
        List<ResourceSnapshot> samples = [baseline];
        Stopwatch elapsed = Stopwatch.StartNew();

        for (int iteration = 1; iteration <= SmallPipelineIterations; iteration++)
        {
            AssertSucceeded(await processor.ProcessAsync(request, null, CancellationToken.None));
            if (iteration % 50 == 0)
            {
                samples.Add(Capture(process, iteration, sessions.CachedSessionCount));
            }
        }

        elapsed.Stop();
        ForceCollectionForMeasurement();
        ResourceSnapshot final = Capture(
            process,
            SmallPipelineIterations,
            sessions.CachedSessionCount);
        WriteEvidence("phase-6-small-pipeline-stability.json", new
        {
            Scenario = "500 real PNG/CPU ONNX/alpha PNG cycles",
            Iterations = SmallPipelineIterations,
            ElapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
            Baseline = baseline,
            Final = final,
            PeakWorkingSetBytes = samples.Append(final).Max(sample => sample.WorkingSetBytes),
            Samples = samples,
        });

        Assert.Equal(1, sessions.CachedSessionCount);
        Assert.True(File.Exists(outputPath));
        Assert.Empty(Directory.GetFiles(_temporaryDirectory, "*.partial"));
        AssertResourceBudget(baseline, final, maximumWorkingSetGrowthMiB: 96, maximumHandleGrowth: 8);
    }

    [Fact]
    public async Task Synthetic4KLifecycle_OneHundredCyclesKeepResourcesBounded()
    {
        (string modelPath, ModelDescriptor descriptor) =
            await FixtureModel.CreateAsync(_temporaryDirectory);
        using U2NetModelAdapterFactory sessions = new(NullLoggerFactory.Instance);
        Synthetic4KDecoder decoder = new();
        LightweightCompositor compositor = new();
        RecordingWriter writer = new();
        LocalBackgroundRemovalProcessor processor = CreateProcessor(
            descriptor,
            modelPath,
            sessions,
            decoder,
            compositor,
            writer);
        RemoveBackgroundRequest request = CreateRequest(
            descriptor.Id,
            Path.Combine(_temporaryDirectory, "synthetic-4k-input.png"),
            Path.Combine(_temporaryDirectory, "synthetic-4k-output.png"));

        for (int warmup = 0; warmup < 5; warmup++)
        {
            AssertSucceeded(await processor.ProcessAsync(request, null, CancellationToken.None));
        }

        ForceCollectionForMeasurement();
        using Process process = Process.GetCurrentProcess();
        ResourceSnapshot baseline = Capture(process, iteration: 0, sessions.CachedSessionCount);
        List<ResourceSnapshot> samples = [baseline];
        Stopwatch elapsed = Stopwatch.StartNew();

        for (int iteration = 1; iteration <= Synthetic4KIterations; iteration++)
        {
            AssertSucceeded(await processor.ProcessAsync(request, null, CancellationToken.None));
            if (iteration % 10 == 0)
            {
                samples.Add(Capture(process, iteration, sessions.CachedSessionCount));
            }
        }

        elapsed.Stop();
        ForceCollectionForMeasurement();
        ResourceSnapshot final = Capture(
            process,
            Synthetic4KIterations,
            sessions.CachedSessionCount);
        WriteEvidence("phase-6-4k-lifecycle-stability.json", new
        {
            Scenario = "100 synthetic 4000x3000 decode/preprocess/CPU ONNX lifecycle cycles",
            Iterations = Synthetic4KIterations,
            ElapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
            Baseline = baseline,
            Final = final,
            PeakWorkingSetBytes = samples.Append(final).Max(sample => sample.WorkingSetBytes),
            Samples = samples,
        });

        Assert.Equal(1, sessions.CachedSessionCount);
        Assert.Equal(Synthetic4KIterations + 5, decoder.InvocationCount);
        Assert.Equal(Synthetic4KIterations + 5, compositor.InvocationCount);
        Assert.Equal(Synthetic4KIterations + 5, writer.InvocationCount);
        AssertResourceBudget(baseline, final, maximumWorkingSetGrowthMiB: 128, maximumHandleGrowth: 8);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static LocalBackgroundRemovalProcessor CreateProcessor(
        ModelDescriptor descriptor,
        string modelPath,
        IModelAdapterSessionCache sessions,
        IImageDecoder decoder,
        IMaskCompositor compositor,
        IAtomicImageWriter writer) => new(
            new StaticModelCatalog(descriptor),
            new StaticModelPathResolver(modelPath),
            sessions,
            new ProviderSelectionService(new CpuProviderCatalog()),
            decoder,
            compositor,
            writer,
            NullLogger<LocalBackgroundRemovalProcessor>.Instance);

    private static RemoveBackgroundRequest CreateRequest(
        string modelId,
        string inputPath,
        string outputPath) => new()
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            ModelId = modelId,
            Provider = InferenceProviderKind.Cpu,
            ExistingOutputBehavior = ExistingOutputBehavior.Overwrite,
        };

    private static void AssertSucceeded(ProcessingResult result)
    {
        Assert.True(
            result.Outcome == ProcessingOutcome.Succeeded,
            $"Pipeline failed with {result.Error?.Category}/{result.Error?.LogCode}.");
    }

    private static ResourceSnapshot Capture(Process process, int iteration, int cachedSessions)
    {
        process.Refresh();
        return new ResourceSnapshot(
            iteration,
            process.WorkingSet64,
            process.HandleCount,
            cachedSessions);
    }

    private static void AssertResourceBudget(
        ResourceSnapshot baseline,
        ResourceSnapshot final,
        int maximumWorkingSetGrowthMiB,
        int maximumHandleGrowth)
    {
        long workingSetGrowth = final.WorkingSetBytes - baseline.WorkingSetBytes;
        int handleGrowth = final.HandleCount - baseline.HandleCount;
        Assert.True(
            workingSetGrowth <= maximumWorkingSetGrowthMiB * OneMiB,
            $"Working set grew by {workingSetGrowth / (double)OneMiB:F2} MiB.");
        Assert.True(
            handleGrowth <= maximumHandleGrowth,
            $"Handle count grew by {handleGrowth}.");
    }

    private static void ForceCollectionForMeasurement()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static void WriteEvidence(string fileName, object evidence)
    {
        string root = Path.Combine(Path.GetTempPath(), "CutLocal.Tests");
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, fileName),
            JsonSerializer.Serialize(evidence, EvidenceSerializerOptions));
    }

    private sealed record ResourceSnapshot(
        int Iteration,
        long WorkingSetBytes,
        int HandleCount,
        int CachedSessions);

    private sealed class Synthetic4KDecoder : IImageDecoder
    {
        public int InvocationCount { get; private set; }

        public DecodedImage Decode(string inputPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SKBitmap bitmap = new(new SKImageInfo(
                4000,
                3000,
                SKColorType.Bgra8888,
                SKAlphaType.Unpremul));
            try
            {
                bitmap.Erase(new SKColor(32, 96, 160, byte.MaxValue));
                InvocationCount++;
                return new DecodedImage(bitmap, new OriginalImageMetadata
                {
                    Width = 4000,
                    Height = 3000,
                });
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }
    }

    private sealed class LightweightCompositor : IMaskCompositor
    {
        public int InvocationCount { get; private set; }

        public SKBitmap Compose(
            DecodedImage image,
            RefinedMask mask,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(4000, image.Metadata.Width);
            Assert.Equal(3000, image.Metadata.Height);
            InvocationCount++;
            SKBitmap result = new(new SKImageInfo(
                1,
                1,
                SKColorType.Bgra8888,
                SKAlphaType.Unpremul));
            result.Erase(SKColors.Transparent);
            return result;
        }
    }

    private sealed class RecordingWriter : IAtomicImageWriter
    {
        public int InvocationCount { get; private set; }

        public string WritePng(
            SKBitmap bitmap,
            string outputPath,
            ExistingOutputBehavior existingOutputBehavior,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(1, bitmap.Width);
            Assert.Equal(1, bitmap.Height);
            InvocationCount++;
            return outputPath;
        }
    }

    private sealed class StaticModelCatalog(ModelDescriptor descriptor) : IModelCatalog
    {
        public ValueTask<ModelDescriptor?> GetByIdAsync(
            string modelId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<ModelDescriptor?>(
                descriptor.Id.Equals(modelId, StringComparison.Ordinal) ? descriptor : null);

        public ValueTask<IReadOnlyList<ModelDescriptor>> GetAllAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ModelDescriptor>>([descriptor]);
    }

    private sealed class StaticModelPathResolver(string path) : IModelPathResolver
    {
        public string GetModelPath(ModelDescriptor descriptor) => path;
    }

    private sealed class CpuProviderCatalog : IInferenceProviderCatalog
    {
        public ValueTask<IReadOnlyList<InferenceProviderDescriptor>> GetAllAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<InferenceProviderDescriptor>>(
                [WindowsInferenceProviderCatalog.Cpu]);
    }
}
