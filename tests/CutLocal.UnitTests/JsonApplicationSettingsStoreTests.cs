using CutLocal.Domain;
using CutLocal.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace CutLocal.UnitTests;

public sealed class JsonApplicationSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CutLocal.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveThenLoad_RoundTripsPhaseThreePreferences()
    {
        using JsonApplicationSettingsStore store = CreateStore();
        ApplicationSettings expected = new()
        {
            Culture = "tr-TR",
            ModelId = "u2netp",
            Provider = InferenceProviderKind.DirectMl,
            DirectMlAdapterIndex = 2,
            OutputDirectory = Path.Combine(_root, "çıktı"),
            FileNameSuffix = ".temiz",
            ExistingOutputBehavior = ExistingOutputBehavior.Overwrite,
            Threshold = 0.62,
            FeatherRadius = 3.5,
            HardCut = true,
            InvertMask = true,
            IsLivePreviewEnabled = false,
        };

        await store.SaveAsync(expected, CancellationToken.None);
        ApplicationSettings actual = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(expected, actual);
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task LoadAsync_CorruptDocument_ReturnsSafeDefaults()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "settings.json"), "{not-json");
        using JsonApplicationSettingsStore store = CreateStore();

        ApplicationSettings settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(new ApplicationSettings(), settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private JsonApplicationSettingsStore CreateStore() => new(
        new ApplicationPaths
        {
            DataRoot = _root,
            LogRoot = Path.Combine(_root, "logs"),
            ManifestRoot = Path.Combine(_root, "manifests"),
            ModelRoot = Path.Combine(_root, "models"),
        },
        NullLogger<JsonApplicationSettingsStore>.Instance);
}
