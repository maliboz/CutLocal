using System.Buffers;
using BenchmarkDotNet.Attributes;
using CutLocal.Domain;
using CutLocal.Imaging;
using CutLocal.Inference;
using CutLocal.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;

namespace CutLocal.Benchmarks;

/// <summary>Measures every production pipeline stage at the required image sizes.</summary>
[MemoryDiagnoser]
public class PipelineStageBenchmarks : IDisposable
{
    private string? _temporaryDirectory;
    private string? _inputPath;
    private string? _outputPath;
    private SafePngDecoder? _decoder;
    private U2NetModelAdapter? _adapter;
    private DecodedImage? _decoded;
    private IMemoryOwner<float>? _inputOwner;
    private PreprocessedInput? _preprocessed;
    private float[]? _rawMask;
    private RefinedMask? _refinedMask;
    private SKBitmap? _composed;
    private AtomicPngWriter? _writer;

    /// <summary>Gets or sets the source resolution under measurement.</summary>
    [Params("512x512", "1920x1080", "4000x3000", "7680x4320")]
    public string ImageSize { get; set; } = "512x512";

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "CutLocal.PipelineBenchmarks",
            Guid.NewGuid().ToString("N"));
        string[] dimensions = ImageSize.Split('x');
        int width = int.Parse(dimensions[0], System.Globalization.CultureInfo.InvariantCulture);
        int height = int.Parse(dimensions[1], System.Globalization.CultureInfo.InvariantCulture);
        (string modelPath, ModelDescriptor descriptor) =
            await FixtureModel.CreateAsync(_temporaryDirectory);
        _inputPath = FixtureModel.CreateGradientPng(_temporaryDirectory, width, height);
        _outputPath = Path.Combine(_temporaryDirectory, "benchmark-output.png");
        _decoder = new SafePngDecoder();
        _adapter = new U2NetModelAdapter(
            descriptor,
            modelPath,
            NullLogger<U2NetModelAdapter>.Instance);
        await _adapter.WarmUpAsync(CancellationToken.None);
        _decoded = _decoder.Decode(_inputPath, CancellationToken.None);
        int tensorLength = descriptor.Input.Width * descriptor.Input.Height * 3;
        _inputOwner = MemoryPool<float>.Shared.Rent(tensorLength);
        _preprocessed = _adapter.Preprocess(_decoded, _inputOwner);
        int maskLength = descriptor.Input.Width * descriptor.Input.Height;
        _rawMask = Enumerable.Range(0, maskLength)
            .Select(index => (float)(index % descriptor.Input.Width) / (descriptor.Input.Width - 1))
            .ToArray();
        _refinedMask = new FloatMaskPostprocessor().Normalize(
            _rawMask,
            descriptor.Input.Width,
            descriptor.Input.Height,
            new MaskRefinementOptions());
        _composed = new BilinearAlphaCompositor().Compose(
            _decoded,
            _refinedMask,
            CancellationToken.None);
        _writer = new AtomicPngWriter();
    }

    [Benchmark]
    public int Decode()
    {
        using DecodedImage image = _decoder!.Decode(_inputPath!, CancellationToken.None);
        return image.Metadata.Width;
    }

    [Benchmark]
    public float Preprocess()
    {
        PreprocessedInput input = _adapter!.Preprocess(_decoded!, _inputOwner!);
        return input.Values.Span[0];
    }

    [Benchmark]
    public long TensorCreation()
    {
        using OrtValue value = OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance,
            _preprocessed!.Values,
            _preprocessed.Shape);
        return value.GetTensorTypeAndShape().ElementCount;
    }

    [Benchmark]
    public float Inference()
    {
        using MaskResult result = _adapter!
            .RunAsync(_preprocessed!, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return result.Values.Span[0];
    }

    [Benchmark]
    public float Refinement()
    {
        using RefinedMask mask = new FloatMaskPostprocessor().Normalize(
            _rawMask!,
            FixtureModel.ModelWidth,
            FixtureModel.ModelHeight,
            new MaskRefinementOptions { FeatherRadius = 1 });
        return mask.Values.Span[0];
    }

    [Benchmark]
    public int MaskResizeAndComposition()
    {
        using SKBitmap bitmap = new BilinearAlphaCompositor().Compose(
            _decoded!,
            _refinedMask!,
            CancellationToken.None);
        return bitmap.Width;
    }

    [Benchmark]
    public long Encode()
    {
        string committed = _writer!.WritePng(
            _composed!,
            _outputPath!,
            ExistingOutputBehavior.Overwrite,
            CancellationToken.None);
        return new FileInfo(committed).Length;
    }

    [Benchmark]
    public long TotalLatency()
    {
        using DecodedImage image = _decoder!.Decode(_inputPath!, CancellationToken.None);
        PreprocessedInput input = _adapter!.Preprocess(image, _inputOwner!);
        using MaskResult raw = _adapter
            .RunAsync(input, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        using RefinedMask mask = _adapter.Postprocess(
            raw,
            image.Metadata,
            new MaskRefinementOptions());
        using SKBitmap composed = new BilinearAlphaCompositor().Compose(
            image,
            mask,
            CancellationToken.None);
        string committed = _writer!.WritePng(
            composed,
            _outputPath!,
            ExistingOutputBehavior.Overwrite,
            CancellationToken.None);
        return new FileInfo(committed).Length;
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _composed?.Dispose();
        _composed = null;
        _refinedMask?.Dispose();
        _refinedMask = null;
        _inputOwner?.Dispose();
        _inputOwner = null;
        _decoded?.Dispose();
        _decoded = null;
        _adapter?.Dispose();
        _adapter = null;
        if (_temporaryDirectory is not null && Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
