using CutLocal.Contracts;
using CutLocal.Domain;
using CutLocal.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace CutLocal.UnitTests;

public sealed class JsonProcessingJobStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CutLocal.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveThenLoad_RoundTripsSnapshotAndLeavesNoPartialFile()
    {
        using JsonProcessingJobStore store = CreateStore();
        ProcessingJob expected = CreateJob();

        await store.SaveAsync(expected, CancellationToken.None);
        ProcessingJob actual = Assert.IsType<ProcessingJob>(
            await store.LoadLastAsync(CancellationToken.None));

        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Preset, actual.Preset);
        Assert.Equal(expected.Items, actual.Items);
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(_root, "jobs"),
            "*.partial",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task LoadLastAsync_CorruptDocument_ReturnsNull()
    {
        string directory = Path.Combine(_root, "jobs");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "last-job.json"), "{broken");
        using JsonProcessingJobStore store = CreateStore();

        Assert.Null(await store.LoadLastAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private JsonProcessingJobStore CreateStore() => new(
        new ApplicationPaths
        {
            DataRoot = _root,
            LogRoot = Path.Combine(_root, "logs"),
            ManifestRoot = Path.Combine(_root, "manifests"),
            ModelRoot = Path.Combine(_root, "models"),
        },
        NullLogger<JsonProcessingJobStore>.Instance);

    private ProcessingJob CreateJob()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new ProcessingJob
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Status = JobStatus.Paused,
            Preset = new ProcessingPreset
            {
                Name = "recovery",
                ModelId = "u2netp",
                Provider = InferenceProviderKind.DirectMl,
                DirectMlAdapterIndex = 1,
                Concurrency = 1,
                Mask = new MaskRefinementOptions { Threshold = 0.6, FeatherRadius = 2 },
                Output = new OutputConfiguration
                {
                    Format = OutputFormat.Png,
                    OutputDirectory = Path.Combine(_root, "output"),
                    ExistingOutputBehavior = ExistingOutputBehavior.Rename,
                },
            },
            Items =
            [
                new ProcessingItem
                {
                    Id = Guid.NewGuid(),
                    InputPath = Path.Combine(_root, "ürün.png"),
                    OutputPath = Path.Combine(_root, "output", "ürün.cutlocal.png"),
                    Status = ItemStatus.Inferring,
                    Progress = 0.45,
                    AttemptCount = 1,
                },
            ],
        };
    }
}
