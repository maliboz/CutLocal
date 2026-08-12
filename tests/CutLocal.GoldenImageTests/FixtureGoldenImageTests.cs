using CutLocal.Contracts;
using CutLocal.Domain;
using CutLocal.Imaging;
using CutLocal.Inference;
using CutLocal.Infrastructure;
using CutLocal.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace CutLocal.GoldenImageTests;

public sealed class FixtureGoldenImageTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "CutLocal.Golden",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task HorizontalGradient_MeetsDocumentedAlphaMaeAndIou()
    {
        (string modelPath, ModelDescriptor descriptor) = await FixtureModel.CreateAsync(_temporaryDirectory);
        string inputPath = FixtureModel.CreateGradientPng(_temporaryDirectory, width: 64, height: 32);
        string outputPath = Path.Combine(_temporaryDirectory, "golden-output.png");
        using U2NetModelAdapterFactory factory = new(NullLoggerFactory.Instance);
        LocalBackgroundRemovalProcessor processor = new(
            new StaticCatalog(descriptor),
            new StaticPathResolver(modelPath),
            factory,
            new ProviderSelectionService(new CpuProviderCatalog()),
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
                ExistingOutputBehavior = ExistingOutputBehavior.Overwrite,
            },
            progress: null,
            CancellationToken.None);

        Assert.Equal(ProcessingOutcome.Succeeded, result.Outcome);
        using SKBitmap output = SKBitmap.Decode(outputPath);
        double absoluteError = 0;
        int intersection = 0;
        int union = 0;
        byte previous = 0;
        int maximumRegression = 0;
        for (int x = 0; x < output.Width; x++)
        {
            byte actual = output.GetPixel(x, output.Height / 2).Alpha;
            byte expected = (byte)Math.Round(x * 255d / (output.Width - 1));
            absoluteError += Math.Abs(actual - expected);
            bool actualForeground = actual >= 128;
            bool expectedForeground = expected >= 128;
            if (actualForeground && expectedForeground)
            {
                intersection++;
            }

            if (actualForeground || expectedForeground)
            {
                union++;
            }

            if (x != 0)
            {
                maximumRegression = Math.Max(maximumRegression, previous - actual);
            }

            previous = actual;
        }

        double mae = absoluteError / output.Width;
        double iou = (double)intersection / union;
        Assert.True(mae <= 6, $"Expected MAE <= 6/255 but observed {mae:F3}/255.");
        Assert.True(iou >= 0.98, $"Expected IoU >= 0.98 but observed {iou:F4}.");
        Assert.True(
            maximumRegression <= 6,
            $"Expected local alpha regression <= 6/255 but observed {maximumRegression}/255.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private sealed class StaticCatalog(ModelDescriptor descriptor) : IModelCatalog
    {
        public ValueTask<ModelDescriptor?> GetByIdAsync(
            string modelId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<ModelDescriptor?>(
                descriptor.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase) ? descriptor : null);

        public ValueTask<IReadOnlyList<ModelDescriptor>> GetAllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ModelDescriptor>>([descriptor]);
    }

    private sealed class StaticPathResolver(string modelPath) : IModelPathResolver
    {
        public string GetModelPath(ModelDescriptor descriptor) => modelPath;
    }

    private sealed class CpuProviderCatalog : IInferenceProviderCatalog
    {
        public ValueTask<IReadOnlyList<InferenceProviderDescriptor>> GetAllAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<InferenceProviderDescriptor>>(
                [WindowsInferenceProviderCatalog.Cpu]);
    }
}
