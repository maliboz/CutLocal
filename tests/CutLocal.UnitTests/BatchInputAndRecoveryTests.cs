using CutLocal.Application;
using CutLocal.Contracts;
using CutLocal.Domain;

namespace CutLocal.UnitTests;

public sealed class BatchInputAndRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CutLocal.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void AddImages_DeduplicatesCanonicalInputsAndReservesDistinctOutputs()
    {
        string firstDirectory = Path.Combine(_root, "a");
        string secondDirectory = Path.Combine(_root, "b");
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);
        string first = CreateFile(firstDirectory, "ürün.png");
        string second = CreateFile(secondDirectory, "ürün.png");
        string rejected = CreateFile(_root, "note.txt");
        AddImagesUseCase useCase = new(TimeProvider.System);

        AddInputsResult result = useCase.Execute(
            existingJob: null,
            CreatePreset(),
            [first, Path.Combine(firstDirectory, ".", "ürün.png"), second, rejected]);

        Assert.Equal(2, result.AddedCount);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Equal(1, result.RejectedCount);
        Assert.Equal(2, result.Job.Items.Select(item => item.OutputPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(result.Job.Items, item => Assert.Equal(ItemStatus.Queued, item.Status));
    }

    [Fact]
    public void AddFolder_HonorsRecursiveSelectionAndIgnoresNonPngFiles()
    {
        string nested = Path.Combine(_root, "nested");
        Directory.CreateDirectory(nested);
        CreateFile(_root, "root.png");
        CreateFile(nested, "nested.PNG");
        CreateFile(nested, "ignored.jpg");
        AddFolderUseCase useCase = new(new AddImagesUseCase(TimeProvider.System));

        AddInputsResult shallow = useCase.Execute(null, CreatePreset(), _root, includeSubfolders: false);
        AddInputsResult recursive = useCase.Execute(null, CreatePreset(), _root, includeSubfolders: true);

        Assert.Single(shallow.Job.Items);
        Assert.Equal(2, recursive.Job.Items.Count);
    }

    [Fact]
    public async Task RecoverInterruptedJob_RequeuesOnlyNonTerminalItems()
    {
        ProcessingJob original = CreateJob(
            JobStatus.Running,
            ItemStatus.Completed,
            ItemStatus.Inferring,
            ItemStatus.Failed);
        MemoryJobStore store = new(original);
        RecoverInterruptedJobUseCase useCase = new(store, TimeProvider.System);

        ProcessingJob recovered = Assert.IsType<ProcessingJob>(
            await useCase.ExecuteAsync(CancellationToken.None));

        Assert.Equal(JobStatus.Interrupted, recovered.Status);
        Assert.Equal(ItemStatus.Completed, recovered.Items[0].Status);
        Assert.Equal(ItemStatus.Queued, recovered.Items[1].Status);
        Assert.Equal(ItemStatus.Failed, recovered.Items[2].Status);
        Assert.Same(recovered, store.LastSaved);
    }

    [Fact]
    public void RetryFailedItems_ResetsOnlyFailedItemsAndPreservesAttemptCount()
    {
        ProcessingJob original = CreateJob(
            JobStatus.CompletedWithErrors,
            ItemStatus.Completed,
            ItemStatus.Failed,
            ItemStatus.Skipped);
        original = original with
        {
            Items = original.Items.Select(item => item with { AttemptCount = 2 }).ToArray(),
        };
        RetryFailedItemsUseCase useCase = new(TimeProvider.System);

        ProcessingJob retried = useCase.Execute(original, CreatePreset());

        Assert.Equal(JobStatus.Queued, retried.Status);
        Assert.Equal(ItemStatus.Completed, retried.Items[0].Status);
        Assert.Equal(ItemStatus.Queued, retried.Items[1].Status);
        Assert.Equal(ItemStatus.Skipped, retried.Items[2].Status);
        Assert.All(retried.Items, item => Assert.Equal(2, item.AttemptCount));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private ProcessingPreset CreatePreset() => new()
    {
        Name = "test",
        ModelId = "u2netp",
        Provider = InferenceProviderKind.Cpu,
        Concurrency = 2,
        Mask = new MaskRefinementOptions(),
        Output = new OutputConfiguration
        {
            Format = OutputFormat.Png,
            OutputDirectory = Path.Combine(_root, "output"),
        },
    };

    private ProcessingJob CreateJob(JobStatus status, params ItemStatus[] itemStatuses)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new ProcessingJob
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Status = status,
            Preset = CreatePreset(),
            Items = itemStatuses.Select((itemStatus, index) => new ProcessingItem
            {
                Id = Guid.NewGuid(),
                InputPath = Path.Combine(_root, $"input-{index}.png"),
                OutputPath = Path.Combine(_root, "output", $"output-{index}.png"),
                Status = itemStatus,
                Progress = itemStatus is ItemStatus.Completed or ItemStatus.Skipped ? 1 : 0.5,
            }).ToArray(),
        };
    }

    private static string CreateFile(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, [1]);
        return path;
    }

    private sealed class MemoryJobStore(ProcessingJob job) : IProcessingJobStore
    {
        public ProcessingJob? LastSaved { get; private set; } = job;

        public ValueTask<ProcessingJob?> LoadLastAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(LastSaved);

        public ValueTask SaveAsync(ProcessingJob job, CancellationToken cancellationToken)
        {
            LastSaved = job;
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(CancellationToken cancellationToken)
        {
            LastSaved = null;
            return ValueTask.CompletedTask;
        }
    }
}
