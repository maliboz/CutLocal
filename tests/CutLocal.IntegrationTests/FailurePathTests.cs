using System.Security.Cryptography;
using CutLocal.Domain;
using CutLocal.Imaging;
using CutLocal.Inference;
using CutLocal.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace CutLocal.IntegrationTests;

public sealed class FailurePathTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "CutLocal.Failures",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Decode_TruncatedPngReturnsTypedFailure()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string path = Path.Combine(_temporaryDirectory, "truncated.png");
        File.WriteAllBytes(path, [137, 80, 78, 71, 13, 10, 26, 10, 0, 0]);

        ImagingException exception = Assert.Throws<ImagingException>(
            () => new SafePngDecoder().Decode(path, CancellationToken.None));

        Assert.Equal(ProcessingErrorCategory.DecodeFailed, exception.Category);
    }

    [Fact]
    public void Decode_LockedPngReturnsTypedFailure()
    {
        string path = FixtureModel.CreateGradientPng(_temporaryDirectory);
        using FileStream locked = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        ImagingException exception = Assert.Throws<ImagingException>(
            () => new SafePngDecoder().Decode(path, CancellationToken.None));

        Assert.Equal(ProcessingErrorCategory.FileLocked, exception.Category);
        Assert.Equal("IMG_READ_IO", exception.LogCode);
    }

    [Fact]
    public async Task AdapterFactory_WrongSha256FailsBeforeSessionActivation()
    {
        (string path, ModelDescriptor descriptor) = await FixtureModel.CreateAsync(_temporaryDirectory);
        descriptor = descriptor with { Sha256 = new string('0', 64) };
        using U2NetModelAdapterFactory factory = new(NullLoggerFactory.Instance);

        InferenceException exception = await Assert.ThrowsAsync<InferenceException>(
            async () => await factory.AcquireAsync(
                descriptor,
                path,
                WindowsInferenceProviderCatalog.Cpu,
                CancellationToken.None));

        Assert.Equal(ProcessingErrorCategory.ModelCorrupted, exception.Category);
        Assert.Equal("MODEL_SHA256_MISMATCH", exception.LogCode);
    }

    [Fact]
    public async Task AdapterFactory_CorruptOnnxWithMatchingHashReturnsModelIncompatible()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        byte[] corrupt = "not-an-onnx-model"u8.ToArray();
        string path = Path.Combine(_temporaryDirectory, "corrupt.onnx");
        await File.WriteAllBytesAsync(path, corrupt, CancellationToken.None);
        ModelDescriptor descriptor = FixtureModel.CreateDescriptor(
            Convert.ToHexString(SHA256.HashData(corrupt)));
        using U2NetModelAdapterFactory factory = new(NullLoggerFactory.Instance);

        InferenceException exception = await Assert.ThrowsAsync<InferenceException>(
            async () => await factory.AcquireAsync(
                descriptor,
                path,
                WindowsInferenceProviderCatalog.Cpu,
                CancellationToken.None));

        Assert.Equal(ProcessingErrorCategory.ModelIncompatible, exception.Category);
        Assert.Equal("MODEL_SESSION_CREATE", exception.LogCode);
    }

    [Fact]
    public async Task AdapterFactory_SameVerifiedModelReusesSessionAdapter()
    {
        (string path, ModelDescriptor descriptor) = await FixtureModel.CreateAsync(_temporaryDirectory);
        using U2NetModelAdapterFactory factory = new(NullLoggerFactory.Instance);

        await using U2NetModelAdapterFactory.ModelAdapterLease first =
            await factory.AcquireAsync(
                descriptor,
                path,
                WindowsInferenceProviderCatalog.Cpu,
                CancellationToken.None);
        await using U2NetModelAdapterFactory.ModelAdapterLease second =
            await factory.AcquireAsync(
                descriptor,
                path,
                WindowsInferenceProviderCatalog.Cpu,
                CancellationToken.None);

        Assert.Same(first.Adapter, second.Adapter);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
