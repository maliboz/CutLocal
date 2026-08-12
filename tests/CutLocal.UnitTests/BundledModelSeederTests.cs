using System.Security.Cryptography;
using CutLocal.Contracts;
using CutLocal.Domain;
using CutLocal.Infrastructure;
using CutLocal.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace CutLocal.UnitTests;

public sealed class BundledModelSeederTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CutLocal.SeederTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SeedAsync_VerifiedBundleAtomicallyActivatesMissingModel()
    {
        byte[] payload = CreatePayload();
        ModelDescriptor descriptor = CreateDescriptor(payload);
        ApplicationPaths paths = CreatePaths();
        string bundledPath = WriteBundle(paths, descriptor, payload);
        ModelPathResolver resolver = new(paths);
        BundledModelSeeder sut = CreateSeeder(paths, resolver, descriptor);

        int seeded = await sut.SeedAsync(CancellationToken.None);

        Assert.Equal(1, seeded);
        Assert.Equal(payload, await File.ReadAllBytesAsync(resolver.GetModelPath(descriptor)));
        Assert.Equal(payload, await File.ReadAllBytesAsync(bundledPath));
        AssertNoStagingFiles(paths.ModelRoot);
    }

    [Fact]
    public async Task SeedAsync_InvalidBundleFailsClosedWithoutActivatingContent()
    {
        byte[] payload = CreatePayload();
        ModelDescriptor descriptor = CreateDescriptor(payload);
        ApplicationPaths paths = CreatePaths();
        WriteBundle(paths, descriptor, Enumerable.Repeat((byte)0xCC, payload.Length).ToArray());
        ModelPathResolver resolver = new(paths);
        BundledModelSeeder sut = CreateSeeder(paths, resolver, descriptor);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => sut.SeedAsync(CancellationToken.None).AsTask());

        Assert.False(File.Exists(resolver.GetModelPath(descriptor)));
        AssertNoStagingFiles(paths.ModelRoot);
    }

    [Fact]
    public async Task SeedAsync_VerifiedBundleRepairsCorruptPerUserCopy()
    {
        byte[] payload = CreatePayload();
        ModelDescriptor descriptor = CreateDescriptor(payload);
        ApplicationPaths paths = CreatePaths();
        WriteBundle(paths, descriptor, payload);
        ModelPathResolver resolver = new(paths);
        string destination = resolver.GetModelPath(descriptor);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(
            destination,
            Enumerable.Repeat((byte)0x11, payload.Length).ToArray());
        BundledModelSeeder sut = CreateSeeder(paths, resolver, descriptor);

        int seeded = await sut.SeedAsync(CancellationToken.None);

        Assert.Equal(1, seeded);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        AssertNoStagingFiles(paths.ModelRoot);
    }

    [Fact]
    public async Task SeedAsync_MissingOptionalBundleLeavesModelUninstalled()
    {
        byte[] payload = CreatePayload();
        ModelDescriptor descriptor = CreateDescriptor(payload);
        ApplicationPaths paths = CreatePaths();
        Directory.CreateDirectory(paths.BundledModelRoot);
        ModelPathResolver resolver = new(paths);
        BundledModelSeeder sut = CreateSeeder(paths, resolver, descriptor);

        int seeded = await sut.SeedAsync(CancellationToken.None);

        Assert.Equal(0, seeded);
        Assert.False(File.Exists(resolver.GetModelPath(descriptor)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private ApplicationPaths CreatePaths() => new()
    {
        DataRoot = Path.Combine(_root, "data"),
        ModelRoot = Path.Combine(_root, "data", "models"),
        LogRoot = Path.Combine(_root, "data", "logs"),
        ManifestRoot = Path.Combine(_root, "manifests"),
        CustomManifestRoot = Path.Combine(_root, "custom-manifests"),
        ModelQuarantineRoot = Path.Combine(_root, "quarantine"),
        BundledModelRoot = Path.Combine(_root, "bundle", "models"),
    };

    private static BundledModelSeeder CreateSeeder(
        ApplicationPaths paths,
        IModelPathResolver resolver,
        ModelDescriptor descriptor) => new(
            new StaticCatalog(descriptor),
            resolver,
            paths,
            NullLogger<BundledModelSeeder>.Instance);

    private static string WriteBundle(
        ApplicationPaths paths,
        ModelDescriptor descriptor,
        byte[] payload)
    {
        string path = Path.Combine(
            paths.BundledModelRoot,
            descriptor.Id,
            descriptor.Version,
            descriptor.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, payload);
        return path;
    }

    private static byte[] CreatePayload() =>
        Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();

    private static ModelDescriptor CreateDescriptor(byte[] payload) => new()
    {
        Id = "release-fixture",
        DisplayName = "Release fixture",
        Version = "1",
        FileName = "release-fixture.onnx",
        Sha256 = Convert.ToHexString(SHA256.HashData(payload)),
        FileSizeBytes = payload.Length,
        DownloadUrl = "https://example.test/release-fixture.onnx",
        License = new ModelLicenseDescriptor
        {
            Spdx = "MIT",
            CommercialUseAllowed = true,
            AttributionRequired = true,
            Source = "https://example.test/license",
        },
        Input = new ModelInputDescriptor
        {
            Width = 16,
            Height = 16,
            Layout = "NCHW",
            ColorOrder = "RGB",
            Mean = [0.485, 0.456, 0.406],
            Std = [0.229, 0.224, 0.225],
            ResizeMode = "stretch",
        },
        Output = new ModelOutputDescriptor
        {
            Activation = "minmax",
            Type = "alpha-mask",
        },
        RecommendedMemoryMb = 64,
        Tier = "test",
        SupportedProviders = ["cpu"],
    };

    private static void AssertNoStagingFiles(string root)
    {
        if (Directory.Exists(root))
        {
            Assert.Empty(Directory.GetFiles(root, "*.seeding", SearchOption.AllDirectories));
        }
    }

    private sealed class StaticCatalog(ModelDescriptor descriptor) : IModelCatalog
    {
        public ValueTask<ModelDescriptor?> GetByIdAsync(
            string modelId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<ModelDescriptor?>(descriptor);

        public ValueTask<IReadOnlyList<ModelDescriptor>> GetAllAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ModelDescriptor>>([descriptor]);
    }
}
