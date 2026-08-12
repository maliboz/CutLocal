using System.Buffers;
using CutLocal.Domain;
using CutLocal.Imaging;
using CutLocal.Inference;
using CutLocal.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace CutLocal.IntegrationTests;

public sealed class CpuInferenceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "CutLocal.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task OrtValuePipeline_RunsGeneratedCpuModelAndProducesAlphaPng()
    {
        (string modelPath, ModelDescriptor descriptor) = await FixtureModel.CreateAsync(_temporaryDirectory);
        string inputPath = FixtureModel.CreateGradientPng(_temporaryDirectory);
        string outputPath = Path.Combine(_temporaryDirectory, "result.png");

        using U2NetModelAdapter adapter = new(
            descriptor,
            modelPath,
            NullLogger<U2NetModelAdapter>.Instance);
        await adapter.WarmUpAsync(CancellationToken.None);
        SafePngDecoder decoder = new();
        using DecodedImage image = decoder.Decode(inputPath, CancellationToken.None);
        int tensorLength = descriptor.Input.Width * descriptor.Input.Height * 3;
        using IMemoryOwner<float> owner = MemoryPool<float>.Shared.Rent(tensorLength);
        PreprocessedInput input = adapter.Preprocess(image, owner);
        using MaskResult rawMask = await adapter.RunAsync(input, CancellationToken.None);
        using RefinedMask mask = adapter.Postprocess(rawMask, image.Metadata, new MaskRefinementOptions());
        using SKBitmap composed = new BilinearAlphaCompositor().Compose(
            image,
            mask,
            CancellationToken.None);
        string committed = new AtomicPngWriter().WritePng(
            composed,
            outputPath,
            ExistingOutputBehavior.Overwrite,
            CancellationToken.None);

        Assert.Equal(outputPath, committed);
        Assert.True(File.Exists(outputPath));
        using SKBitmap decodedOutput = SKBitmap.Decode(outputPath);
        Assert.Equal(image.Metadata.Width, decodedOutput.Width);
        Assert.Equal(image.Metadata.Height, decodedOutput.Height);
        Assert.True(decodedOutput.GetPixel(0, decodedOutput.Height / 2).Alpha < 16);
        Assert.True(decodedOutput.GetPixel(decodedOutput.Width - 1, decodedOutput.Height / 2).Alpha > 239);
    }

    [Fact]
    public async Task OrtValuePipeline_SupportsUnicodePathsLongerThanThreeHundredCharacters()
    {
        string segment = $"gorsel-çğıöşü-{new string('a', 42)}";
        string longDirectory = _temporaryDirectory;
        for (int depth = 0; depth < 5; depth++)
        {
            longDirectory = Path.Combine(longDirectory, $"{depth}-{segment}");
        }

        (string modelPath, ModelDescriptor descriptor) =
            await FixtureModel.CreateAsync(longDirectory);
        string inputPath = FixtureModel.CreateGradientPng(longDirectory);
        string outputPath = Path.Combine(longDirectory, "saydam-sonuç-ürün.png");
        Assert.True(outputPath.Length > 300, $"Expected a >300 character path, got {outputPath.Length}.");

        using U2NetModelAdapter adapter = new(
            descriptor,
            modelPath,
            NullLogger<U2NetModelAdapter>.Instance);
        await adapter.WarmUpAsync(CancellationToken.None);
        using DecodedImage image = new SafePngDecoder().Decode(inputPath, CancellationToken.None);
        int tensorLength = descriptor.Input.Width * descriptor.Input.Height * 3;
        using IMemoryOwner<float> owner = MemoryPool<float>.Shared.Rent(tensorLength);
        PreprocessedInput input = adapter.Preprocess(image, owner);
        using MaskResult rawMask = await adapter.RunAsync(input, CancellationToken.None);
        using RefinedMask mask = adapter.Postprocess(
            rawMask,
            image.Metadata,
            new MaskRefinementOptions());
        using SKBitmap composed = new BilinearAlphaCompositor().Compose(
            image,
            mask,
            CancellationToken.None);

        string committed = new AtomicPngWriter().WritePng(
            composed,
            outputPath,
            ExistingOutputBehavior.Overwrite,
            CancellationToken.None);

        Assert.Equal(outputPath, committed);
        Assert.True(File.Exists(outputPath));
        using SKBitmap decodedOutput = SKBitmap.Decode(outputPath);
        Assert.Equal(image.Metadata.Width, decodedOutput.Width);
        Assert.Equal(image.Metadata.Height, decodedOutput.Height);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
