using System.Diagnostics;
using System.Threading.Channels;
using CutLocal.Contracts;
using CutLocal.Domain;
using Microsoft.Extensions.Logging;

namespace CutLocal.Application;

/// <summary>Runs one durable batch with bounded backpressure and provider-safe concurrency.</summary>
public sealed class ProcessBatchUseCase
{
    private const int QueueCapacity = 32;
    private static readonly Action<ILogger, string, Exception?> LogItemFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2101, nameof(LogItemFailure)),
            "Unexpected batch item failure with code {Code}; paths omitted");
    private static readonly Action<ILogger, Exception?> LogPersistenceFailure =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2102, nameof(LogPersistenceFailure)),
            "Batch durability update failed; processing will continue");

    private readonly RemoveBackgroundUseCase _removeBackground;
    private readonly IProcessingJobStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ProcessBatchUseCase> _logger;
    private readonly IMemoryPressureGate _memoryPressureGate;
    private readonly AsyncManualResetEvent _pauseGate = new(initialState: true);
    private readonly object _activeSync = new();
    private ActiveBatch? _active;

    /// <summary>Initializes the bounded batch executor.</summary>
    public ProcessBatchUseCase(
        RemoveBackgroundUseCase removeBackground,
        IProcessingJobStore store,
        TimeProvider timeProvider,
        ILogger<ProcessBatchUseCase> logger,
        IMemoryPressureGate? memoryPressureGate = null)
    {
        _removeBackground = removeBackground;
        _store = store;
        _timeProvider = timeProvider;
        _logger = logger;
        _memoryPressureGate = memoryPressureGate ?? NoMemoryPressureGate.Instance;
    }

    /// <summary>Gets whether one batch execution currently owns this use-case instance.</summary>
    public bool IsActive
    {
        get
        {
            lock (_activeSync)
            {
                return _active is not null;
            }
        }
    }

    /// <summary>Runs all queued items and returns the final durable snapshot.</summary>
    public async Task<ProcessingJob> ExecuteAsync(
        ProcessingJob job,
        IProgress<BatchProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ActiveBatch state = new(job, progress, _timeProvider);
        lock (_activeSync)
        {
            if (_active is not null)
            {
                throw new InvalidOperationException("Only one batch may run in this workspace.");
            }

            _active = state;
        }

        _pauseGate.Set();
        state.SetStatus(JobStatus.Running);
        state.Report(item: null);
        await PersistLatestAsync(state, CancellationToken.None).ConfigureAwait(false);
        try
        {
            int[] queuedIndices = state.GetQueuedIndices();
            if (queuedIndices.Length == 0)
            {
                state.SetStatus(ResolveTerminalStatus(state.Snapshot().Items));
                state.Report(item: null);
                return await PersistLatestAsync(state, CancellationToken.None).ConfigureAwait(false);
            }

            int concurrency = EffectiveConcurrency(job.Preset);
            Channel<int> channel = Channel.CreateBounded<int>(new BoundedChannelOptions(QueueCapacity)
            {
                SingleWriter = true,
                SingleReader = concurrency == 1,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
            Task producer = ProduceAsync(channel.Writer, queuedIndices, cancellationToken);
            Task[] workers = Enumerable.Range(0, concurrency)
                .Select(_ => ConsumeAsync(channel.Reader, state, cancellationToken))
                .ToArray();
            try
            {
                await Task.WhenAll(workers.Prepend(producer)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The final cancellation snapshot is applied below.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                ProcessingItem[] cancelled = state.CancelNonTerminalItems();
                foreach (ProcessingItem item in cancelled)
                {
                    state.Report(item);
                }

                state.SetStatus(JobStatus.Cancelled);
            }
            else
            {
                state.SetStatus(ResolveTerminalStatus(state.Snapshot().Items));
            }

            state.Report(item: null);
            return await PersistLatestAsync(state, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _pauseGate.Set();
            lock (_activeSync)
            {
                if (ReferenceEquals(_active, state))
                {
                    _active = null;
                }
            }
        }
    }

    /// <summary>Pauses admission of new items after current item boundaries.</summary>
    public async ValueTask<bool> PauseAsync(CancellationToken cancellationToken)
    {
        ActiveBatch? state = GetActive();
        if (state is null || state.Status != JobStatus.Running)
        {
            return false;
        }

        _pauseGate.Reset();
        state.SetStatus(JobStatus.Paused);
        state.Report(item: null);
        await PersistLatestAsync(state, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Resumes admission of queued items.</summary>
    public async ValueTask<bool> ResumeAsync(CancellationToken cancellationToken)
    {
        ActiveBatch? state = GetActive();
        if (state is null || state.Status != JobStatus.Paused)
        {
            return false;
        }

        state.SetStatus(JobStatus.Running);
        _pauseGate.Set();
        state.Report(item: null);
        await PersistLatestAsync(state, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task ProduceAsync(
        ChannelWriter<int> writer,
        IEnumerable<int> queuedIndices,
        CancellationToken cancellationToken)
    {
        Exception? completionError = null;
        try
        {
            foreach (int index in queuedIndices)
            {
                await writer.WriteAsync(index, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            completionError = exception;
            throw;
        }
        finally
        {
            writer.TryComplete(completionError);
        }
    }

    private async Task ConsumeAsync(
        ChannelReader<int> reader,
        ActiveBatch state,
        CancellationToken cancellationToken)
    {
        await foreach (int index in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            await _pauseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            await _memoryPressureGate.WaitForCapacityAsync(cancellationToken).ConfigureAwait(false);
            await ProcessItemAsync(state, index, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class NoMemoryPressureGate : IMemoryPressureGate
    {
        public static NoMemoryPressureGate Instance { get; } = new();

        public ValueTask WaitForCapacityAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private async Task ProcessItemAsync(
        ActiveBatch state,
        int index,
        CancellationToken cancellationToken)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        ProcessingItem item = state.GetItem(index);
        if (state.Preset.Output.ExistingOutputBehavior == ExistingOutputBehavior.Skip
            && File.Exists(item.OutputPath))
        {
            elapsed.Stop();
            item = state.UpdateItem(index, current => current with
            {
                Status = ItemStatus.Skipped,
                Progress = 1,
                Elapsed = elapsed.Elapsed,
            });
            state.Report(item);
            await PersistLatestAsync(state, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        item = state.UpdateItem(index, current => current with
        {
            Status = ItemStatus.PreparingModel,
            Progress = 0.02,
            Error = null,
            Elapsed = TimeSpan.Zero,
            AttemptCount = current.AttemptCount + 1,
            ProviderId = null,
            UsedCpuFallback = false,
        });
        state.Report(item);

        try
        {
            InlineProgress<ProcessingProgress> itemProgress = new(update =>
            {
                ProcessingItem changed = state.UpdateItem(index, current => current with
                {
                    Status = update.Status,
                    Progress = update.Value,
                    Elapsed = elapsed.Elapsed,
                });
                state.Report(changed);
            });
            ProcessingResult result = await _removeBackground.ExecuteAsync(
                    item.InputPath,
                    item.OutputPath,
                    new SingleImageProcessingOptions
                    {
                        ModelId = state.Preset.ModelId,
                        Provider = state.Preset.Provider,
                        DirectMlAdapterIndex = state.Preset.DirectMlAdapterIndex,
                        MaskOptions = state.Preset.Mask,
                        ExistingOutputBehavior = state.Preset.Output.ExistingOutputBehavior,
                    },
                    itemProgress,
                    cancellationToken)
                .ConfigureAwait(false);
            elapsed.Stop();
            item = state.UpdateItem(index, current => result.Outcome switch
            {
                ProcessingOutcome.Succeeded => current with
                {
                    OutputPath = result.OutputPath ?? current.OutputPath,
                    Status = ItemStatus.Completed,
                    Progress = 1,
                    Elapsed = elapsed.Elapsed,
                    Error = null,
                    ProviderId = result.ProviderId,
                    UsedCpuFallback = result.UsedCpuFallback,
                },
                ProcessingOutcome.Cancelled => current with
                {
                    Status = ItemStatus.Cancelled,
                    Elapsed = elapsed.Elapsed,
                    Error = result.Error,
                },
                _ => current with
                {
                    Status = ItemStatus.Failed,
                    Elapsed = elapsed.Elapsed,
                    Error = result.Error,
                },
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            elapsed.Stop();
            item = state.UpdateItem(index, current => current with
            {
                Status = ItemStatus.Cancelled,
                Elapsed = elapsed.Elapsed,
                Error = CancelledError(),
            });
        }
        catch (Exception exception)
        {
            elapsed.Stop();
            LogItemFailure(_logger, "BATCH_ITEM_UNEXPECTED", exception);
            item = state.UpdateItem(index, current => current with
            {
                Status = ItemStatus.Failed,
                Elapsed = elapsed.Elapsed,
                Error = UnknownError(),
            });
        }

        state.Report(item);
        await PersistLatestAsync(state, CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask<ProcessingJob> PersistLatestAsync(
        ActiveBatch state,
        CancellationToken cancellationToken = default)
    {
        await state.PersistenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        ProcessingJob snapshot = state.Snapshot();
        try
        {
            await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException)
        {
            LogPersistenceFailure(_logger, exception);
        }
        finally
        {
            state.PersistenceGate.Release();
        }

        return snapshot;
    }

    private ActiveBatch? GetActive()
    {
        lock (_activeSync)
        {
            return _active;
        }
    }

    private static int EffectiveConcurrency(ProcessingPreset preset) =>
        preset.Provider == InferenceProviderKind.Cpu
            ? Math.Clamp(preset.Concurrency, 1, 2)
            : 1;

    private static JobStatus ResolveTerminalStatus(IReadOnlyList<ProcessingItem> items) =>
        items.Any(item => item.Status == ItemStatus.Failed)
            ? JobStatus.CompletedWithErrors
            : JobStatus.Completed;

    private static ProcessingError CancelledError() => new()
    {
        Category = ProcessingErrorCategory.Cancelled,
        LogCode = "BATCH_CANCELLED",
        MessageTr = "Toplu işlem iptal edildi.",
        MessageEn = "Batch processing was cancelled.",
        IsRetryable = true,
    };

    private static ProcessingError UnknownError() => new()
    {
        Category = ProcessingErrorCategory.Unknown,
        LogCode = "BATCH_ITEM_UNKNOWN",
        MessageTr = "Öğe işlenirken beklenmeyen bir hata oluştu.",
        MessageEn = "An unexpected error occurred while processing the item.",
        IsRetryable = false,
    };

    private sealed class ActiveBatch(
        ProcessingJob job,
        IProgress<BatchProgressUpdate>? progress,
        TimeProvider timeProvider)
    {
        private readonly object _sync = new();
        private readonly ProcessingJob _job = job;
        private readonly ProcessingItem[] _items = job.Items.ToArray();
        private JobStatus _status = job.Status;

        public SemaphoreSlim PersistenceGate { get; } = new(1, 1);

        public ProcessingPreset Preset => _job.Preset;

        public JobStatus Status
        {
            get
            {
                lock (_sync)
                {
                    return _status;
                }
            }
        }

        public void SetStatus(JobStatus status)
        {
            lock (_sync)
            {
                _status = status;
            }
        }

        public int[] GetQueuedIndices()
        {
            lock (_sync)
            {
                return _items
                    .Select((item, index) => (item, index))
                    .Where(entry => entry.item.Status == ItemStatus.Queued)
                    .Select(entry => entry.index)
                    .ToArray();
            }
        }

        public ProcessingItem UpdateItem(
            int index,
            Func<ProcessingItem, ProcessingItem> update)
        {
            lock (_sync)
            {
                ProcessingItem changed = update(_items[index]);
                _items[index] = changed;
                return changed;
            }
        }

        public ProcessingItem GetItem(int index)
        {
            lock (_sync)
            {
                return _items[index];
            }
        }

        public ProcessingItem[] CancelNonTerminalItems()
        {
            lock (_sync)
            {
                List<ProcessingItem> changed = [];
                for (int index = 0; index < _items.Length; index++)
                {
                    if (IsTerminal(_items[index].Status))
                    {
                        continue;
                    }

                    _items[index] = _items[index] with
                    {
                        Status = ItemStatus.Cancelled,
                        Error = CancelledError(),
                    };
                    changed.Add(_items[index]);
                }

                return changed.ToArray();
            }
        }

        public ProcessingJob Snapshot()
        {
            lock (_sync)
            {
                return _job with
                {
                    UpdatedAtUtc = timeProvider.GetUtcNow(),
                    Status = _status,
                    Items = _items.ToArray(),
                };
            }
        }

        public void Report(ProcessingItem? item)
        {
            if (progress is null)
            {
                return;
            }

            BatchProgressUpdate update;
            lock (_sync)
            {
                int terminal = _items.Count(entry => IsTerminal(entry.Status));
                double overall = _items.Length == 0
                    ? 1
                    : _items.Sum(entry => entry.Progress) / _items.Length;
                update = new BatchProgressUpdate
                {
                    JobId = _job.Id,
                    Item = item,
                    JobStatus = _status,
                    TerminalCount = terminal,
                    TotalCount = _items.Length,
                    OverallProgress = overall,
                };
            }

            progress.Report(update);
        }

        private static bool IsTerminal(ItemStatus status) => status is
            ItemStatus.Completed or ItemStatus.Failed or ItemStatus.Cancelled or ItemStatus.Skipped;
    }

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    private sealed class AsyncManualResetEvent
    {
        private volatile TaskCompletionSource<bool> _signal;

        public AsyncManualResetEvent(bool initialState)
        {
            _signal = CreateSignal();
            if (initialState)
            {
                _signal.TrySetResult(true);
            }
        }

        public Task<bool> WaitAsync(CancellationToken cancellationToken) =>
            _signal.Task.WaitAsync(cancellationToken);

        public void Set() => _signal.TrySetResult(true);

        public void Reset()
        {
            while (true)
            {
                TaskCompletionSource<bool> current = _signal;
                if (!current.Task.IsCompleted)
                {
                    return;
                }

                TaskCompletionSource<bool> replacement = CreateSignal();
                if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _signal, replacement, current),
                    current))
                {
                    return;
                }
            }
        }

        private static TaskCompletionSource<bool> CreateSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
