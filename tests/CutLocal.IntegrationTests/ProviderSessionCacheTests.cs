using CutLocal.Domain;
using CutLocal.Inference;
using CutLocal.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace CutLocal.IntegrationTests;

public sealed class ProviderSessionCacheTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "CutLocal.CacheTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Cache_ReusesMatchingLeaseAndEvictsIdleLruAboveTwoKeys()
    {
        (string modelPath, ModelDescriptor descriptor) =
            await FixtureModel.CreateAsync(_temporaryDirectory);
        using U2NetModelAdapterFactory cache = new(NullLoggerFactory.Instance);

        await using (U2NetModelAdapterFactory.ModelAdapterLease first =
            await cache.AcquireAsync(
                descriptor,
                modelPath,
                WindowsInferenceProviderCatalog.Cpu,
                CancellationToken.None))
        await using (U2NetModelAdapterFactory.ModelAdapterLease matching =
            await cache.AcquireAsync(
                descriptor,
                modelPath,
                WindowsInferenceProviderCatalog.Cpu,
                CancellationToken.None))
        {
            Assert.Same(first.Adapter, matching.Adapter);
        }

        for (int version = 2; version <= 3; version++)
        {
            await using U2NetModelAdapterFactory.ModelAdapterLease lease =
                await cache.AcquireAsync(
                    descriptor with { Version = $"test-{version}" },
                    modelPath,
                    WindowsInferenceProviderCatalog.Cpu,
                    CancellationToken.None);
        }

        Assert.Equal(2, cache.CachedSessionCount);
    }

    [Fact]
    public async Task Cache_RepeatedModelSwitchingNeverExceedsTwoIdleSessions()
    {
        (string modelPath, ModelDescriptor descriptor) =
            await FixtureModel.CreateAsync(_temporaryDirectory);
        using U2NetModelAdapterFactory cache = new(NullLoggerFactory.Instance);

        for (int switchIndex = 0; switchIndex < 30; switchIndex++)
        {
            int version = switchIndex % 3;
            await using U2NetModelAdapterFactory.ModelAdapterLease lease =
                await cache.AcquireAsync(
                    descriptor with { Version = $"switch-{version}" },
                    modelPath,
                    WindowsInferenceProviderCatalog.Cpu,
                    CancellationToken.None);

            Assert.InRange(cache.CachedSessionCount, 1, 2);
        }

        Assert.Equal(2, cache.CachedSessionCount);
    }

    [Fact]
    public async Task ProviderCatalog_AlwaysContainsCpuAndReturnsValidDirectMlIndices()
    {
        WindowsInferenceProviderCatalog catalog = new();
        IReadOnlyList<InferenceProviderDescriptor> providers =
            await catalog.GetAllAsync(CancellationToken.None);

        Assert.Contains(providers, provider => provider.Kind == InferenceProviderKind.Cpu);
        Assert.All(
            providers.Where(provider => provider.Kind == InferenceProviderKind.DirectMl),
            provider => Assert.True(provider.DeviceIndex >= 0));
        Assert.Equal(
            providers.Count(provider => provider.Kind == InferenceProviderKind.DirectMl),
            providers
                .Where(provider => provider.Kind == InferenceProviderKind.DirectMl)
                .Select(provider => provider.DeviceIndex)
                .Distinct()
                .Count());
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
