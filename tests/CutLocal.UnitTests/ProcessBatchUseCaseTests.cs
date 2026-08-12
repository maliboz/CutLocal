using System.Threading.Channels;
using CutLocal.Application;
using CutLocal.Contracts;
using CutLocal.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CutLocal.UnitTests;

public sealed class ProcessBatchUseCaseTests
{
    [Theory]
    [InlineData(InferenceProviderKind.Cpu, 2, 2)]
    [InlineData(InferenceProviderKind.Auto, 2, 1)]
    [InlineData(InferenceProviderKind.DirectMl, 2, 1)]
    public async Task ExecuteAsync_ClampsConcurrencyForProviderSafety(
        InferenceProviderKind provider,
        int requestedConcurrency,
        int expectedMaximum)
    {
        ConcurrencyTrackingProcessor processor = new();
        MemoryJobStore store = new();
        ProcessBatchUseCase useCase = CreateUseCase(processor, store);

        ProcessingJob result = await useCase.ExecuteAsync(
            CreateJob(provider, requestedConcurrency, itemCount: 8),
            progress: null,
            CancellationToken.None);

        Assert.Equal(JobStatus.Completed, result.Status);
        Assert.All(result.Items, item => Assert.Equal(ItemStatus.Completed, item.Status));
        Assert.Equal(expectedMaximum, processor.MaximumConcurrency);
        Assert.Equal(result, store.LastSaved);
    }

    [Fact]
    public async Task PauseAsync_WaitsAtItemBoundaryUntilResume()
    {
        using ControlledProcessor processor = new();
        MemoryJobStore store = new();
        ProcessBatchUseCase useCase = CreateUseCase(processor, store);
        Task<ProcessingJob> execution = useCase.ExecuteAsync(
            CreateJob(InferenceProviderKind.Cpu, concurrency: 1, itemCount: 3),
            progress: null,
            CancellationToken.None);
        await processor.Started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(await useCase.PauseAsync(CancellationToken.None));
        processor.Release(1);
        await Task.Delay(150);

        Assert.False(processor.Started.Reader.TryRead(out _));
        Assert.Equal(JobStatus.Paused, store.LastSaved?.Status);
        Assert.True(await useCase.ResumeAsync(CancellationToken.None));
        processor.Release(2);
        ProcessingJob result = await execution.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(JobStatus.Completed, result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationMarksCurrentAndQueuedItemsCancelled()
    {
        NeverCompletingProcessor processor = new();
        MemoryJobStore store = new();
        ProcessBatchUseCase useCase = CreateUseCase(processor, store);
        using CancellationTokenSource cancellation = new();
        Task<ProcessingJob> execution = useCase.ExecuteAsync(
            CreateJob(InferenceProviderKind.Cpu, concurrency: 1, itemCount: 5),
            progress: null,
            cancellation.Token);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        ProcessingJob result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(JobStatus.Cancelled, result.Status);
        Assert.All(result.Items, item => Assert.Equal(ItemStatus.Cancelled, item.Status));
        Assert.Equal(result, store.LastSaved);
    }

    [Fact]
    public async Task ExecuteAsync_FailedItemDoesNotStopSiblingItems()
    {
        SelectiveFailureProcessor processor = new();
        MemoryJobStore store = new();
        ProcessBatchUseCase useCase = CreateUseCase(processor, store);

        ProcessingJob result = await useCase.ExecuteAsync(
            CreateJob(InferenceProviderKind.Cpu, concurrency: 2, itemCount: 4),
            progress: null,
            CancellationToken.None);

        Assert.Equal(JobStatus.CompletedWithErrors, result.Status);
        Assert.Single(result.Items, item => item.Status == ItemStatus.Failed);
        Assert.Equal(3, result.Items.Count(item => item.Status == ItemStatus.Completed));
        Assert.All(result.Items, item => Assert.Equal(1, item.AttemptCount));
        Assert.Equal(result, store.LastSaved);
    }

    [Fact]
    public async Task ExecuteAsync_SkipExistingOutputDoesNotEnterProcessorOrIncrementAttempt()
    {
        FailIfInvokedProcessor processor = new();
        MemoryJobStore store = new();
        ProcessBatchUseCase useCase = CreateUseCase(processor, store);
        ProcessingJob source = CreateJob(InferenceProviderKind.Cpu, concurrency: 1, itemCount: 1);
        ProcessingJob job = source with
        {
            Preset = source.Preset with
            {
                Output = source.Preset.Output with
                {
                    ExistingOutputBehavior = ExistingOutputBehavior.Skip,
                },
            },
        };
        await File.WriteAllBytesAsync(job.Items[0].OutputPath, [0x89]);
        try
        {
            ProcessingJob result = await useCase.ExecuteAsync(job, progress: null, CancellationToken.None);

            ProcessingItem skipped = Assert.Single(result.Items);
            Assert.Equal(JobStatus.Completed, result.Status);
            Assert.Equal(ItemStatus.Skipped, skipped.Status);
            Assert.Equal(0, skipped.AttemptCount);
            Assert.Equal(0, processor.InvocationCount);
        }
        finally
        {
            File.Delete(job.Items[0].OutputPath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_MemoryPressureGateBlocksNewAdmissionAndHonorsCancellation()
    {
        InvocationTrackingProcessor processor = new();
        MemoryJobStore store = new();
        BlockingMemoryPressureGate memoryPressureGate = new();
        ProcessBatchUseCase useCase = CreateUseCase(processor, store, memoryPressureGate);
        using CancellationTokenSource cancellation = new();
        Task<ProcessingJob> execution = useCase.ExecuteAsync(
            CreateJob(InferenceProviderKind.Cpu, concurrency: 1, itemCount: 1),
            progress: null,
            cancellation.Token);

        await memoryPressureGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, processor.InvocationCount);

        cancellation.Cancel();
        ProcessingJob result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(JobStatus.Cancelled, result.Status);
        Assert.All(result.Items, item => Assert.Equal(ItemStatus.Cancelled, item.Status));
        Assert.Equal(0, processor.InvocationCount);
    }

    private static ProcessBatchUseCase CreateUseCase(
        IRemoveBackgroundProcessor processor,
        IProcessingJobStore store,
        IMemoryPressureGate? memoryPressureGate = null) => new(
            new RemoveBackgroundUseCase(
                processor,
                NullLogger<RemoveBackgroundUseCase>.Instance),
            store,
            TimeProvider.System,
            NullLogger<ProcessBatchUseCase>.Instance,
            memoryPressureGate);

    private static ProcessingJob CreateJob(
        InferenceProviderKind provider,
        int concurrency,
        int itemCount)
    {
        string root = Path.GetTempPath();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new ProcessingJob
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Status = JobStatus.Queued,
            Preset = new ProcessingPreset
            {
                Name = "batch-test",
                ModelId = "u2netp",
                Provider = provider,
                DirectMlAdapterIndex = provider == InferenceProviderKind.DirectMl ? 0 : null,
                Concurrency = concurrency,
                Mask = new MaskRefinementOptions(),
                Output = new OutputConfiguration
                {
                    Format = OutputFormat.Png,
                    OutputDirectory = root,
                    ExistingOutputBehavior = ExistingOutputBehavior.Overwrite,
                },
            },
            Items = Enumerable.Range(0, itemCount).Select(index => new ProcessingItem
            {
                Id = Guid.NewGuid(),
                InputPath = Path.Combine(root, $"batch-input-{Guid.NewGuid():N}-{index}.png"),
                OutputPath = Path.Combine(root, $"batch-output-{Guid.NewGuid():N}-{index}.png"),
                Status = ItemStatus.Queued,
                Progress = 0,
            }).ToArray(),
        };
    }

    private sealed class ConcurrencyTrackingProcessor : IRemoveBackgroundProcessor
    {
        private int _active;
        private int _maximumConcurrency;

        public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

        public async ValueTask<ProcessingResult> ProcessAsync(
            RemoveBackgroundRequest request,
            IProgress<ProcessingProgress>? progress,
            CancellationToken cancellationToken)
        {
            int active = Interlocked.Increment(ref _active);
            UpdateMaximum(active);
            try
            {
                await Task.Delay(40, cancellationToken);
                return new ProcessingResult
                {
                    Outcome = ProcessingOutcome.Succeeded,
                    OutputPath = request.OutputPath,
                    ProviderId = request.Provider.ToString(),
                };
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private void UpdateMaximum(int candidate)
        {
            int current;
            do
            {
                current = Volatile.Read(ref _maximumConcurrency);
                if (candidate <= current)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref _maximumConcurrency, candidate, current) != current);
        }
    }

    private sealed class ControlledProcessor : IRemoveBackgroundProcessor, IDisposable
    {
        private readonly SemaphoreSlim _release = new(0);

        public Channel<RemoveBackgroundRequest> Started { get; } =
            Channel.CreateUnbounded<RemoveBackgroundRequest>();

        public async ValueTask<ProcessingResult> ProcessAsync(
            RemoveBackgroundRequest request,
            IProgress<ProcessingProgress>? progress,
            CancellationToken cancellationToken)
        {
            await Started.Writer.WriteAsync(request, cancellationToken);
            await _release.WaitAsync(cancellationToken);
            return new ProcessingResult
            {
                Outcome = ProcessingOutcome.Succeeded,
                OutputPath = request.OutputPath,
            };
        }

        public void Release(int count) => _release.Release(count);

        public void Dispose() => _release.Dispose();
    }

    private sealed class NeverCompletingProcessor : IRemoveBackgroundProcessor
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ProcessingResult> ProcessAsync(
            RemoveBackgroundRequest request,
            IProgress<ProcessingProgress>? progress,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation test processor unexpectedly resumed.");
        }
    }

    private sealed class SelectiveFailureProcessor : IRemoveBackgroundProcessor
    {
        public ValueTask<ProcessingResult> ProcessAsync(
            RemoveBackgroundRequest request,
            IProgress<ProcessingProgress>? progress,
            CancellationToken cancellationToken)
        {
            bool fail = Path.GetFileName(request.InputPath).EndsWith("-0.png", StringComparison.Ordinal);
            return ValueTask.FromResult(fail
                ? new ProcessingResult
                {
                    Outcome = ProcessingOutcome.Failed,
                    Error = new ProcessingError
                    {
                        Category = ProcessingErrorCategory.DecodeFailed,
                        LogCode = "TEST_DECODE",
                        MessageTr = "Test hatası",
                        MessageEn = "Test failure",
                        IsRetryable = true,
                    },
                }
                : new ProcessingResult
                {
                    Outcome = ProcessingOutcome.Succeeded,
                    OutputPath = request.OutputPath,
                    ProviderId = "cpu",
                });
        }
    }

    private sealed class FailIfInvokedProcessor : IRemoveBackgroundProcessor
    {
        public int InvocationCount { get; private set; }

        public ValueTask<ProcessingResult> ProcessAsync(
            RemoveBackgroundRequest request,
            IProgress<ProcessingProgress>? progress,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            throw new InvalidOperationException("Existing output should have been skipped.");
        }
    }

    private sealed class InvocationTrackingProcessor : IRemoveBackgroundProcessor
    {
        public int InvocationCount { get; private set; }

        public ValueTask<ProcessingResult> ProcessAsync(
            RemoveBackgroundRequest request,
            IProgress<ProcessingProgress>? progress,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            return ValueTask.FromResult(new ProcessingResult
            {
                Outcome = ProcessingOutcome.Succeeded,
                OutputPath = request.OutputPath,
            });
        }
    }

    private sealed class BlockingMemoryPressureGate : IMemoryPressureGate
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask WaitForCapacityAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class MemoryJobStore : IProcessingJobStore
    {
        private readonly object _sync = new();
        private ProcessingJob? _lastSaved;

        public ProcessingJob? LastSaved
        {
            get
            {
                lock (_sync)
                {
                    return _lastSaved;
                }
            }
        }

        public ValueTask<ProcessingJob?> LoadLastAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(LastSaved);

        public ValueTask SaveAsync(ProcessingJob job, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _lastSaved = job;
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _lastSaved = null;
            }

            return ValueTask.CompletedTask;
        }
    }
}
