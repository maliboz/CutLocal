using System.Security.Cryptography;
using CutLocal.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;

namespace CutLocal.Inference;

/// <summary>Owns a bounded, provider-aware cache of warmed model sessions.</summary>
public sealed class U2NetModelAdapterFactory : IModelAdapterSessionCache, IDisposable
{
    private const int MaximumCachedSessions = 2;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly ILoggerFactory _loggerFactory;
    private TaskCompletionSource _stateChanged = CreateSignal();
    private bool _disposed;

    /// <summary>Initializes the cache and disables ONNX Runtime telemetry events.</summary>
    public U2NetModelAdapterFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        OrtEnv.Instance().DisableTelemetryEvents();
    }

    /// <summary>Gets the number of currently cached native sessions.</summary>
    public int CachedSessionCount
    {
        get
        {
            _gate.Wait();
            try
            {
                return _entries.Count;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>Acquires a hash-verified, warmed adapter lease.</summary>
    public async ValueTask<ModelAdapterLease> AcquireAsync(
        ModelDescriptor descriptor,
        string modelPath,
        InferenceProviderDescriptor provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(provider);

        string cacheKey = CreateCacheKey(descriptor, modelPath, provider);
        while (true)
        {
            Task? waitForCapacity = null;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_entries.TryGetValue(cacheKey, out CacheEntry? cached)
                    && !cached.Invalidated)
                {
                    cached.LeaseCount++;
                    cached.LastUsedUtc = DateTimeOffset.UtcNow;
                    return new ModelAdapterLease(this, cached);
                }

                if (_entries.Count >= MaximumCachedSessions)
                {
                    CacheEntry? evicted = _entries.Values
                        .Where(entry => entry.LeaseCount == 0)
                        .OrderBy(entry => entry.LastUsedUtc)
                        .FirstOrDefault();
                    if (evicted is null)
                    {
                        waitForCapacity = _stateChanged.Task;
                    }
                    else
                    {
                        _entries.Remove(evicted.CacheKey);
                        evicted.Adapter.Dispose();
                    }
                }

                if (waitForCapacity is null)
                {
                    U2NetModelAdapter adapter = await CreateAdapterAsync(
                            descriptor,
                            modelPath,
                            provider,
                            cancellationToken)
                        .ConfigureAwait(false);
                    CacheEntry entry = new(cacheKey, Path.GetFullPath(modelPath), adapter)
                    {
                        LeaseCount = 1,
                        LastUsedUtc = DateTimeOffset.UtcNow,
                    };
                    _entries.Add(cacheKey, entry);
                    SignalStateChangedNoLock();
                    return new ModelAdapterLease(this, entry);
                }
            }
            finally
            {
                _gate.Release();
            }

            await waitForCapacity.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    async ValueTask<IModelAdapterLease> IModelAdapterSessionCache.AcquireAsync(
        ModelDescriptor descriptor,
        string modelPath,
        InferenceProviderDescriptor provider,
        CancellationToken cancellationToken) => await AcquireAsync(
            descriptor,
            modelPath,
            provider,
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask InvalidateAsync(string modelPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        string fullPath = Path.GetFullPath(modelPath);
        List<U2NetModelAdapter> disposeNow = [];
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (CacheEntry entry in _entries.Values.Where(entry =>
                         entry.ModelPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                entry.Invalidated = true;
                _entries.Remove(entry.CacheKey);
                if (entry.LeaseCount == 0)
                {
                    disposeNow.Add(entry.Adapter);
                }
            }

            SignalStateChangedNoLock();
        }
        finally
        {
            _gate.Release();
        }

        foreach (U2NetModelAdapter adapter in disposeNow)
        {
            adapter.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        List<U2NetModelAdapter> disposeNow = [];
        _gate.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (CacheEntry entry in _entries.Values)
            {
                entry.Invalidated = true;
                if (entry.LeaseCount == 0)
                {
                    disposeNow.Add(entry.Adapter);
                }
            }

            _entries.Clear();
            SignalStateChangedNoLock();
        }
        finally
        {
            _gate.Release();
        }

        foreach (U2NetModelAdapter adapter in disposeNow)
        {
            adapter.Dispose();
        }
    }

    private static string CreateCacheKey(
        ModelDescriptor descriptor,
        string modelPath,
        InferenceProviderDescriptor provider) =>
        string.Join(
            '|',
            descriptor.Id,
            descriptor.Version,
            descriptor.Sha256,
            Path.GetFullPath(modelPath),
            provider.Kind,
            provider.Id,
            provider.DeviceIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none");

    private async Task<U2NetModelAdapter> CreateAdapterAsync(
        ModelDescriptor descriptor,
        string modelPath,
        InferenceProviderDescriptor provider,
        CancellationToken cancellationToken)
    {
        await VerifyModelFileAsync(descriptor, modelPath, cancellationToken).ConfigureAwait(false);
        U2NetModelAdapter? adapter = null;
        try
        {
            adapter = await Task.Run(
                    () => new U2NetModelAdapter(
                        descriptor,
                        modelPath,
                        provider,
                        _loggerFactory.CreateLogger<U2NetModelAdapter>()),
                    cancellationToken)
                .ConfigureAwait(false);
            await adapter.WarmUpAsync(cancellationToken).ConfigureAwait(false);
            return adapter;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            adapter?.Dispose();
            throw;
        }
        catch (InferenceException exception)
        {
            adapter?.Dispose();
            if (provider.Kind == InferenceProviderKind.DirectMl
                && exception.Category is not ProcessingErrorCategory.ModelMissing
                    and not ProcessingErrorCategory.ModelCorrupted)
            {
                throw InferenceFailureClassifier.ClassifyInitializationFailure(exception, provider);
            }

            throw;
        }
        catch (Exception exception) when (exception is OnnxRuntimeException
            or DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException)
        {
            adapter?.Dispose();
            throw InferenceFailureClassifier.ClassifyInitializationFailure(exception, provider);
        }
    }

    private async ValueTask ReleaseAsync(CacheEntry entry)
    {
        U2NetModelAdapter? dispose = null;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (entry.LeaseCount <= 0)
            {
                return;
            }

            entry.LeaseCount--;
            entry.LastUsedUtc = DateTimeOffset.UtcNow;
            if (entry.LeaseCount == 0 && (entry.Invalidated || _disposed))
            {
                if (_entries.TryGetValue(entry.CacheKey, out CacheEntry? current)
                    && ReferenceEquals(current, entry))
                {
                    _entries.Remove(entry.CacheKey);
                }

                dispose = entry.Adapter;
            }

            SignalStateChangedNoLock();
        }
        finally
        {
            _gate.Release();
        }

        dispose?.Dispose();
    }

    private void Invalidate(CacheEntry entry)
    {
        _gate.Wait();
        try
        {
            entry.Invalidated = true;
            SignalStateChangedNoLock();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void SignalStateChangedNoLock()
    {
        TaskCompletionSource previous = _stateChanged;
        _stateChanged = CreateSignal();
        previous.TrySetResult();
    }

    private static TaskCompletionSource CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task VerifyModelFileAsync(
        ModelDescriptor descriptor,
        string modelPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(modelPath))
        {
            throw new InferenceException(
                ProcessingErrorCategory.ModelMissing,
                "MODEL_FILE_MISSING",
                "The selected model is not installed.");
        }

        await using FileStream stream = new(
            modelPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        string actualHash = Convert.ToHexString(digest);
        if (!actualHash.Equals(descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InferenceException(
                ProcessingErrorCategory.ModelCorrupted,
                "MODEL_SHA256_MISMATCH",
                "The installed model failed SHA-256 verification.");
        }
    }

    internal sealed class CacheEntry(string cacheKey, string modelPath, U2NetModelAdapter adapter)
    {
        public string CacheKey { get; } = cacheKey;
        public string ModelPath { get; } = modelPath;
        public U2NetModelAdapter Adapter { get; } = adapter;
        public int LeaseCount { get; set; }
        public bool Invalidated { get; set; }
        public DateTimeOffset LastUsedUtc { get; set; }
    }

    /// <summary>Prevents eviction while a caller is using a cached native session.</summary>
    public sealed class ModelAdapterLease : IModelAdapterLease
    {
        private U2NetModelAdapterFactory? _owner;
        private CacheEntry? _entry;

        internal ModelAdapterLease(U2NetModelAdapterFactory owner, CacheEntry entry)
        {
            _owner = owner;
            _entry = entry;
        }

        /// <summary>Gets the leased model adapter.</summary>
        public IBackgroundRemovalModelAdapter Adapter =>
            _entry?.Adapter ?? throw new ObjectDisposedException(nameof(ModelAdapterLease));

        /// <summary>Gets the concrete leased provider/device.</summary>
        public InferenceProviderDescriptor Provider => Adapter.Provider;

        /// <summary>Marks the session unusable so it is disposed after the last lease.</summary>
        public void Invalidate()
        {
            if (_owner is { } owner && _entry is { } entry)
            {
                owner.Invalidate(entry);
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            U2NetModelAdapterFactory? owner = Interlocked.Exchange(ref _owner, null);
            CacheEntry? entry = Interlocked.Exchange(ref _entry, null);
            if (owner is not null && entry is not null)
            {
                await owner.ReleaseAsync(entry).ConfigureAwait(false);
            }
        }
    }
}
