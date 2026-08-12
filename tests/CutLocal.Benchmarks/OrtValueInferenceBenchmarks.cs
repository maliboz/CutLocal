using System.Buffers;
using BenchmarkDotNet.Attributes;
using CutLocal.Domain;
using CutLocal.Imaging;
using CutLocal.Inference;
using CutLocal.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace CutLocal.Benchmarks;

/// <summary>Measures the warmed reusable CPU session and pooled OrtValue path.</summary>
[MemoryDiagnoser]
public class OrtValueInferenceBenchmarks : IDisposable
{
    private string? _temporaryDirectory;
    private U2NetModelAdapter? _adapter;
    private DecodedImage? _image;
    private IMemoryOwner<float>? _owner;
    private PreprocessedInput? _input;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "CutLocal.Benchmarks",
            Guid.NewGuid().ToString("N"));
        (string modelPath, ModelDescriptor descriptor) =
            await FixtureModel.CreateAsync(_temporaryDirectory);
        string imagePath = FixtureModel.CreateGradientPng(_temporaryDirectory, 160, 96);
        _adapter = new U2NetModelAdapter(
            descriptor,
            modelPath,
            NullLogger<U2NetModelAdapter>.Instance);
        await _adapter.WarmUpAsync(CancellationToken.None);
        _image = new SafePngDecoder().Decode(imagePath, CancellationToken.None);
        int tensorLength = descriptor.Input.Width * descriptor.Input.Height * 3;
        _owner = MemoryPool<float>.Shared.Rent(tensorLength);
        _input = _adapter.Preprocess(_image, _owner);
    }

    [Benchmark]
    public float CpuOrtValueInference()
    {
        using MaskResult result = _adapter!
            .RunAsync(_input!, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return result.Values.Span[0];
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _owner?.Dispose();
        _owner = null;
        _image?.Dispose();
        _image = null;
        _adapter?.Dispose();
        _adapter = null;
        if (_temporaryDirectory is not null && Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
